using System.Globalization;
using DnWModLoader.Logging;
using UnityEngine;

namespace Parkour
{
    internal sealed class ParkourMotor
    {
        private const float MinInput = 0.2f;
        // End sprint after standing still
        private const float ToggleIdleSeconds = 0.5f;
        private const float SprintSpeed = 1.6f;
        private const float DoubleJumpHeight = 1f;
        private const float DoubleJumpCorrection = 2f;

        private const float WallRunSpeed = 8f;
        private const float WallRunSeconds = 1.5f;
        private const float WallRunStartGravity = 0.15f;
        private const float WallRunMinSpeed = 3f;
        private const float WallRunReach = 0.35f;
        private const float WallJumpReach = 0.6f;
        private const float FollowReach = 1.3f;
        private const float WallLostSeconds = 0.1f;
        private const float MaxWallTurnDegreesPerSecond = 180f;
        private const float MinWallRunClearance = 0.4f;
        // Falls faster than this are too fast to catch a wall
        private const float MaxCatchFallSpeed = 6f;
        private const float MaxRunUpSpeed = 2f;
        private const float WallRunAcceleration = 12f;
        // Ease back down if wall-running too fast
        private const float WallRunDeceleration = 2f;
        private const float StallSpeed = 1.5f;
        private const float MaxWallSlideSpeed = 4f;
        // Smoothed wall-magnetism
        private const float LeanSpeed = 0.75f;
        private const float LeanAcceleration = 3f;
        private const float LookAheadSeconds = 0.3f;
        private const float MaxStepOutSpeed = 4f;
        private const float DropOffPush = 1.5f;
        private const float StrideLength = 1.2f;
        // Wall jump coyote time
        private const float WallJumpGraceSeconds = 0.2f;
        private const float ReattachDelaySeconds = 0.2f;
        private const float WallJumpAimDegrees = 45f;
        private const float WallJumpPush = 6f;
        private const float WallJumpHeight = 1.2f;

        public readonly PlayerBody Player;
        private readonly ParkourSettings _settings;
        private readonly ModLogger _logger;

        private bool _sprintToggled;
        private float _idleSeconds;
        private bool _doubleJumpUsed;

        private bool _wallRunning;
        private WallHit _wall;
        private Vector3 _runDirection;
        private float _wallRunStartedAt;
        private float _wallMissingSeconds;
        private float _stride;

        private WallHit _lastWall;
        private float _lastWallAt = float.NegativeInfinity;
        // The same wall can't be used twice in a row
        private bool _hasUsedWall;
        private WallHit _usedWall;
        private float _attachAllowedAt;

        public ParkourMotor(PlayerBody player, ParkourSettings settings, ModLogger logger)
        {
            Player = player;
            _settings = settings;
            _logger = logger;
        }

        public bool IsWallRunning
        {
            get { return _wallRunning; }
        }

        public void UpdateInput(float deltaTime)
        {
            if (_settings.SprintMode.Value != SprintMode.Toggle)
            {
                _sprintToggled = false;
                return;
            }
            var actions = MenuManager.actions.Player;
            if (actions.Sprint.WasPressedThisFrame())
            {
                _sprintToggled = !_sprintToggled;
                _idleSeconds = 0f;
            }
            if (actions.Move.ReadValue<Vector2>().magnitude >= MinInput) _idleSeconds = 0f;
            else if ((_idleSeconds += deltaTime) > ToggleIdleSeconds) _sprintToggled = false;
        }

        public float SprintFactor()
        {
            if (!IsSprinting()) return 1f;
            // Crouching
            return Mathf.Lerp(1f, SprintSpeed, Player.Locomotion.Posture);
        }

        private bool IsSprinting()
        {
            if (!_settings.Sprint.Value) return false;
            bool wanted = _settings.SprintMode.Value == SprintMode.Toggle ? _sprintToggled : MenuManager.actions.Player.Sprint.IsPressed();
            Vector3 input = Player.Locomotion.MoveInput.Flat();
            // No sprinting backwards
            return wanted && input.magnitude >= MinInput && Vector3.Dot(input.normalized, Player.Forward) > -0.3f;
        }

        public float CameraRoll(float tilt)
        {
            return _wallRunning ? -Vector3.Dot(_wall.Normal, Player.Right) * tilt : 0f;
        }

        public float SpeedShare()
        {
            float walk = Player.WalkSpeed;
            float sprint = Mathf.Max(walk * SprintSpeed, walk + 2f);
            return Mathf.InverseLerp(walk + 0.5f, sprint, Player.Rigidbody.linearVelocity.Flat().magnitude);
        }

        // Intercept vanilla jump logic
        public void Step(ref float jumpBuffer, float coyoteTime)
        {
            var locomotion = Player.Locomotion;
            bool grounded = locomotion.IsGrounded;
            bool airborne = !grounded && locomotion.CurrentMovementMode is FallingMovementMode;
            if (_wallRunning && !airborne) EndWallRun(grounded ? "landed" : "interrupted", 0f);
            if (grounded)
            {
                _doubleJumpUsed = false;
                _hasUsedWall = false;
                _lastWallAt = float.NegativeInfinity;
            }
            if (!airborne) return;

            if (_wallRunning) UpdateWallRun();
            else TryStartWallRun();

            if (jumpBuffer > 0f && coyoteTime <= 0f && (TryWallJump() || TryDoubleJump())) jumpBuffer = 0f;
        }

        private bool TryDoubleJump()
        {
            if (!_settings.DoubleJump.Value || _doubleJumpUsed) return false;
            Vector3 velocity = Player.Rigidbody.linearVelocity;
            Vector3 horizontal = velocity.Flat();
            Vector3 input = Player.Locomotion.MoveInput.Flat();
            if (input.magnitude >= MinInput)
            {
                float speedLimit = Mathf.Max(horizontal.magnitude, Player.WalkSpeed);
                horizontal = Vector3.ClampMagnitude(horizontal + input.normalized * (DoubleJumpCorrection * Mathf.Min(input.magnitude, 1f)), speedLimit);
            }
            Player.Rigidbody.linearVelocity = horizontal + Vector3.up * Mathf.Max(velocity.y, LiftSpeed(DoubleJumpHeight));
            _doubleJumpUsed = true;
            _logger.Debug("Double jump at " + Format(horizontal.magnitude) + " m/s");
            return true;
        }

        private void TryStartWallRun()
        {
            if (!_settings.WallRun.Value || Time.fixedTime < _attachAllowedAt) return;
            Vector3 velocity = Player.Rigidbody.linearVelocity;
            Vector3 horizontal = velocity.Flat();
            Vector3 input = Player.Locomotion.MoveInput.Flat();
            if (horizontal.magnitude < WallRunMinSpeed || velocity.y < -MaxCatchFallSpeed || input.magnitude < MinInput) return;
            if (!WallProbe.FindNearest(Player, horizontal.normalized, WallRunReach, out WallHit wall)) return;
            if (_hasUsedWall && wall.SameSurface(_usedWall)) return;

            Vector3 along = Vector3.ProjectOnPlane(horizontal, wall.Normal);
            Vector3 steer = input.normalized;
            if (along.magnitude < WallRunMinSpeed || Vector3.Dot(steer, along.normalized) < 0.3f || Vector3.Dot(steer, wall.Normal) > 0.5f) return;
            if (!WallProbe.IsTall(Player, wall, WallRunReach) || Player.FeetClearance(MinWallRunClearance) < MinWallRunClearance) return;
            BeginWallRun(wall, along.normalized, velocity);
        }

        private void BeginWallRun(WallHit wall, Vector3 direction, Vector3 velocity)
        {
            _wallRunning = true;
            _wall = wall;
            _runDirection = direction;
            _wallRunStartedAt = Time.fixedTime;
            _wallMissingSeconds = 0f;
            _stride = 0f;

            velocity.y = Mathf.Clamp(velocity.y, 0f, MaxRunUpSpeed);
            float away = Vector3.Dot(velocity, wall.Normal);
            if (away > 0f) velocity -= wall.Normal * away;
            Player.Rigidbody.linearVelocity = velocity;
            _doubleJumpUsed = false;
            Footsteps.Play(wall.Collider, wall.Point, wall.Normal);
            _logger.Debug("Wall-run on " + Name(wall.Collider) + " at " + Format(Vector3.Dot(velocity, direction)) + " m/s");
        }

        private void UpdateWallRun()
        {
            float deltaTime = Time.fixedDeltaTime;
            if (WallProbe.Follow(Player, _wall, FollowReach, out WallHit wall))
            {
                wall.Normal = Vector3.RotateTowards(_wall.Normal, wall.Normal, MaxWallTurnDegreesPerSecond * Mathf.Deg2Rad * deltaTime, 0f);
                _wall = wall;
                _wallMissingSeconds = 0f;
            }
            else if ((_wallMissingSeconds += deltaTime) > WallLostSeconds)
            {
                EndWallRun("ran off the wall", 0f);
                return;
            }

            Vector3 normal = _wall.Normal;
            Vector3 direction = Vector3.ProjectOnPlane(_runDirection, normal).Flat();
            bool facingWall = direction.sqrMagnitude < 0.01f;
            direction.Normalize();
            float time = Time.fixedTime - _wallRunStartedAt;
            Vector3 velocity = Player.Rigidbody.linearVelocity;
            Vector3 input = Player.Locomotion.MoveInput.Flat();
            float along = Vector3.Dot(velocity, direction);
            string reason = null;
            if (!_settings.WallRun.Value) reason = "wall-runs turned off";
            else if (facingWall) reason = "turned into a wall";
            else if (time >= WallRunSeconds) reason = "out of time";
            else if (input.magnitude < MinInput || Vector3.Dot(input.normalized, normal) > 0.8f || Vector3.Dot(input.normalized, direction) < -0.2f) reason = "let go";
            else if (Player.Locomotion.PostureInput < 0f) reason = "crouched";
            else if (along < StallSpeed) reason = "stalled";
            if (reason != null)
            {
                EndWallRun(reason, DropOffPush);
                return;
            }
            _runDirection = direction;

            along = along < WallRunSpeed ? Mathf.Min(WallRunSpeed, along + WallRunAcceleration * deltaTime) : Mathf.Max(WallRunSpeed, along - WallRunDeceleration * deltaTime);
            float intoWall = Mathf.MoveTowards(Mathf.Max(-Vector3.Dot(velocity, normal), 0f), LeanSpeed, LeanAcceleration * deltaTime);
            float stepOut = WallProbe.StepOutAhead(Player, normal, _runDirection, Mathf.Max(along, 1f) * LookAheadSeconds, out float obstacleDistance);
            if (stepOut > 0f) intoWall = -Mathf.Min(stepOut / Mathf.Max(obstacleDistance / Mathf.Max(along, 1f), deltaTime), MaxStepOutSpeed);
            // Ease gravity
            float progress = time / WallRunSeconds;
            float gravityShare = Mathf.Lerp(WallRunStartGravity, 1f, progress * progress);
            float vertical = Mathf.Max(velocity.y - Physics.gravity.y * (1f - gravityShare) * deltaTime, -MaxWallSlideSpeed);
            Player.Rigidbody.linearVelocity = _runDirection * along - normal * intoWall + Vector3.up * vertical;

            _stride += along * deltaTime;
            if (_stride >= StrideLength)
            {
                _stride -= StrideLength;
                Footsteps.Play(_wall.Collider, _wall.Point, normal);
            }
        }

        private void EndWallRun(string reason, float push)
        {
            _wallRunning = false;
            _lastWall = _wall;
            _lastWallAt = Time.fixedTime;
            _usedWall = _wall;
            _hasUsedWall = true;
            _attachAllowedAt = Time.fixedTime + ReattachDelaySeconds;
            if (push > 0f) Player.Rigidbody.linearVelocity += _wall.Normal * push;
            _logger.Debug("Wall-run ended after " + Format(Time.fixedTime - _wallRunStartedAt) + " s: " + reason);
        }

        private bool TryWallJump()
        {
            if (!_settings.WallJump.Value) return false;
            WallHit wall;
            if (_wallRunning) wall = _wall;
            else if (Time.fixedTime - _lastWallAt <= WallJumpGraceSeconds) wall = _lastWall;
            else
            {
                Vector3 facing = Player.Rigidbody.linearVelocity.Flat();
                if (facing.sqrMagnitude < 0.01f) facing = Player.Forward;
                if (!WallProbe.FindNearest(Player, facing.normalized, WallJumpReach, out wall) || (_hasUsedWall && wall.SameSurface(_usedWall))) return false;
            }

            Vector3 velocity = Player.Rigidbody.linearVelocity;
            Vector3 normal = wall.Normal;
            // Change velocity vector
            Vector3 horizontal = Vector3.ProjectOnPlane(velocity.Flat(), normal) + normal * WallJumpPush;
            Vector3 input = Player.Locomotion.MoveInput.Flat();
            if (input.magnitude >= MinInput && Vector3.Dot(input.normalized, normal) > 0.1f)
                horizontal = Vector3.RotateTowards(horizontal, input.normalized * horizontal.magnitude, WallJumpAimDegrees * Mathf.Deg2Rad, 0f);
            Player.Rigidbody.linearVelocity = horizontal + Vector3.up * Mathf.Max(velocity.y, LiftSpeed(WallJumpHeight));

            if (_wallRunning) EndWallRun("jumped off", 0f);
            _lastWallAt = float.NegativeInfinity;
            _usedWall = wall;
            _hasUsedWall = true;
            _attachAllowedAt = Time.fixedTime + ReattachDelaySeconds;
            _doubleJumpUsed = false;
            Footsteps.Play(wall.Collider, wall.Point, normal);
            _logger.Debug("Wall jump off " + Name(wall.Collider) + " at " + Format(horizontal.magnitude) + " m/s");
            return true;
        }

        private static float LiftSpeed(float height)
        {
            return Mathf.Sqrt(2f * Mathf.Abs(Physics.gravity.y) * Mathf.Max(height, 0f));
        }

        private static string Name(Collider collider)
        {
            return collider != null ? collider.name : "?";
        }

        private static string Format(float value)
        {
            return value.ToString("0.0", CultureInfo.InvariantCulture);
        }
    }
}

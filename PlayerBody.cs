using HarmonyLib;
using UnityEngine;

namespace Parkour
{
    internal sealed class PlayerBody
    {
        // Collision layers
        public const int SurfaceMask = 129;

        private static readonly AccessTools.FieldRef<MassSpringController, float> SpringLengthField = AccessTools.FieldRefAccess<MassSpringController, float>("defaultSpringLength");
        private static readonly AccessTools.FieldRef<WalkingMovementMode, float> WalkSpeedField = AccessTools.FieldRefAccess<WalkingMovementMode, float>("walkingMovementSpeed");

        public readonly LocomotionController Locomotion;
        public readonly LookController Look;
        public readonly Rigidbody Rigidbody;
        public readonly CapsuleCollider Capsule;
        public readonly float SpringLength;
        public readonly float WalkSpeed;

        private PlayerBody(LocomotionController locomotion, LookController look, Rigidbody rigidbody, CapsuleCollider capsule, float springLength, float walkSpeed)
        {
            Locomotion = locomotion;
            Look = look;
            Rigidbody = rigidbody;
            Capsule = capsule;
            SpringLength = springLength;
            WalkSpeed = walkSpeed;
        }

        public static PlayerBody Find(LocomotionController locomotion)
        {
            var spring = locomotion.GetComponent<MassSpringController>();
            var look = locomotion.GetComponent<LookController>();
            var rigidbody = locomotion.GetComponent<Rigidbody>();
            var capsule = locomotion.GetComponent<CapsuleCollider>();
            if (spring == null || look == null || rigidbody == null || capsule == null) return null;
            var walking = locomotion.GetMovementMode<WalkingMovementMode>();
            return new PlayerBody(locomotion, look, rigidbody, capsule, SpringLengthField(spring), walking != null ? WalkSpeedField(walking) : 5f);
        }

        public bool IsAlive
        {
            get { return Locomotion != null && Rigidbody != null; }
        }

        public float Radius
        {
            get { return Capsule.radius; }
        }

        public Vector3 Center
        {
            get { return Rigidbody.position + Rigidbody.rotation * Capsule.center; }
        }

        public Vector3 Waist
        {
            get { return Center - Vector3.up * HalfSpan; }
        }

        public Vector3 Chest
        {
            get { return Center + Vector3.up * HalfSpan; }
        }

        private float HalfSpan
        {
            get { return Mathf.Max(0f, Capsule.height * 0.5f - Capsule.radius); }
        }

        public Vector3 Forward
        {
            get { return Quaternion.Euler(0f, Look.LookYaw, 0f) * Vector3.forward; }
        }

        public Vector3 Right
        {
            get { return Quaternion.Euler(0f, Look.LookYaw, 0f) * Vector3.right; }
        }

        public float FeetClearance(float limit)
        {
            if (Physics.Raycast(Rigidbody.position, Vector3.down, out RaycastHit hit, SpringLength + limit, SurfaceMask, QueryTriggerInteraction.Ignore)) return hit.distance - SpringLength;
            return limit;
        }
    }
}

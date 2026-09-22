using System.Collections.Generic;
using UnityEngine;

namespace Parkour
{
    internal struct WallHit
    {
        public Collider Collider;
        // Wall hit point
        public Vector3 Point;
        // Wall normal
        public Vector3 Normal;
        public float Gap;

        public bool SameSurface(WallHit other)
        {
            return Vector3.Dot(Normal, other.Normal) > 0.9f && Mathf.Abs(Vector3.Dot(Point - other.Point, other.Normal)) < 1f;
        }
    }

    internal static class WallProbe
    {
        private const float ProbeRadius = 0.2f;
        // Surfaces steeper than about 70 degrees count as walls
        private const float MaxNormalY = 0.3f;
        private const int Directions = 8;
        // Follow gentle slope changes
        private const float MinFollowNormalDot = 0.65f;
        private const float BlockingNormalDot = -0.3f;
        // Step over small obstacles
        private const float StepOutIncrement = 0.15f;
        private const int StepOutTries = 6;
        private const float CastThickness = 0.9f;

        private static readonly RaycastHit[] Hits = new RaycastHit[16];
        // Colliders from dragons or loose physics objects
        private static readonly Dictionary<int, bool> Unrunnable = new Dictionary<int, bool>();

        public static void ForgetSurfaces()
        {
            Unrunnable.Clear();
        }

        public static bool FindNearest(PlayerBody player, Vector3 forward, float reach, out WallHit wall)
        {
            wall = default(WallHit);
            bool found = false;
            Vector3 center = player.Center;
            for (int i = 0; i < Directions; i++)
            {
                Vector3 direction = Quaternion.AngleAxis(i * 360f / Directions, Vector3.up) * forward;
                if (!TryHitWall(player, center, direction, reach, out WallHit hit) || (found && hit.Gap >= wall.Gap)) continue;
                wall = hit;
                found = true;
            }
            return found;
        }

        public static bool IsTall(PlayerBody player, WallHit wall, float reach)
        {
            return TryHitWall(player, player.Waist, -wall.Normal, reach, out _) && TryHitWall(player, player.Chest, -wall.Normal, reach, out _);
        }

        public static bool Follow(PlayerBody player, WallHit wall, float reach, out WallHit found)
        {
            return (TryHitWall(player, player.Center, -wall.Normal, reach, out found) && Vector3.Dot(found.Normal, wall.Normal) >= MinFollowNormalDot)
                || (TryHitWall(player, player.Waist, -wall.Normal, reach, out found) && Vector3.Dot(found.Normal, wall.Normal) >= MinFollowNormalDot);
        }

        public static float StepOutAhead(PlayerBody player, Vector3 normal, Vector3 direction, float distance, out float obstacleDistance)
        {
            if (!IsBlocked(player, Vector3.zero, direction, distance, out obstacleDistance)) return 0f;
            for (int i = 1; i <= StepOutTries; i++)
            {
                if (!IsBlocked(player, normal * (i * StepOutIncrement), direction, distance, out _)) return i * StepOutIncrement + player.Radius * (1f - CastThickness);
            }
            return -1f;
        }

        private static bool IsBlocked(PlayerBody player, Vector3 offset, Vector3 direction, float distance, out float obstacleDistance)
        {
            obstacleDistance = float.MaxValue;
            // Extra cast as the player capsule already reports the wall collision
            int count = Physics.CapsuleCastNonAlloc(player.Waist + offset, player.Chest + offset, player.Radius * CastThickness, direction, Hits, distance, PlayerBody.SurfaceMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (Hits[i].distance <= 0f || Hits[i].rigidbody == player.Rigidbody || Vector3.Dot(Hits[i].normal.Flat(), direction) > BlockingNormalDot) continue;
                obstacleDistance = Mathf.Min(obstacleDistance, Hits[i].distance);
            }
            return obstacleDistance < float.MaxValue;
        }

        private static bool TryHitWall(PlayerBody player, Vector3 origin, Vector3 direction, float reach, out WallHit wall)
        {
            wall = default(WallHit);

            float distance = (player.Radius + reach) / 0.9f - ProbeRadius;
            if (!Cast(player, origin, direction, distance, out RaycastHit hit)) return false;
            if (!hit.collider.Raycast(new Ray(origin, hit.point - origin), out RaycastHit face, Vector3.Distance(origin, hit.point) + 0.05f) || Mathf.Abs(face.normal.y) > MaxNormalY) return false;
            Vector3 normal = face.normal.Flat().normalized;
            float gap = Vector3.Dot(origin - face.point, normal) - player.Radius;
            if (gap > reach) return false;
            wall = new WallHit { Collider = hit.collider, Point = face.point, Normal = normal, Gap = gap };
            return true;
        }

        private static bool Cast(PlayerBody player, Vector3 origin, Vector3 direction, float distance, out RaycastHit nearest)
        {
            nearest = default(RaycastHit);
            bool found = false;
            int count = Physics.SphereCastNonAlloc(origin, ProbeRadius, direction, Hits, distance, PlayerBody.SurfaceMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                // Ignore zero distance hits
                if (Hits[i].distance <= 0f || Hits[i].rigidbody == player.Rigidbody || (found && Hits[i].distance >= nearest.distance)) continue;
                nearest = Hits[i];
                found = true;
            }
            return found && IsRunnable(nearest.collider);
        }

        private static bool IsRunnable(Collider collider)
        {
            int id = collider.GetInstanceID();
            if (!Unrunnable.TryGetValue(id, out bool unrunnable))
            {
                var attached = collider.attachedRigidbody;
                unrunnable = (attached != null && !attached.isKinematic) || collider.GetComponentInParent<MountableObject>() != null || collider.GetComponentInParent<WalkNWashDragonDescriptor>() != null;
                Unrunnable[id] = unrunnable;
            }
            return !unrunnable;
        }
    }
}

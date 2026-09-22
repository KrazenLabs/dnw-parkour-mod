using UnityEngine;

namespace Parkour
{
    internal static class Footsteps
    {
        public static void Play(Collider surface, Vector3 point, Vector3 normal)
        {
            var database = PhysicsMaterialExtensionDatabase.GetDatabase();
            if (database == null || surface == null) return;
            if (database.TryGetImpactInfo(surface.sharedMaterial, PhysicsMaterialExtension.PhysicMaterialInfoType.Soft, PhysicsMaterialExtension.PhysicsResponseType.Footstep, out var impact))
                impact.Apply(point, normal);
        }
    }
}

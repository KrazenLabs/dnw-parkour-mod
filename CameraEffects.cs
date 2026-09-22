using System;
using HarmonyLib;
using UnityEngine;

namespace Parkour
{
    // Camera tilt and fov change
    internal static class CameraEffects
    {
        private const float RollSmoothSeconds = 0.12f;
        private const float FovSmoothSeconds = 0.3f;
        private const float WallRunTilt = 12f;
        private const float SpeedFieldOfView = 8f;

        private static float _roll;
        private static float _rollVelocity;
        private static float _fov;
        private static float _fovVelocity;

        public static void Apply(ref OrbitCameraData data)
        {
            var mod = ParkourMod.Instance;
            if (mod == null) return;
            var motor = mod.Motor;
            float roll = motor != null ? motor.CameraRoll(WallRunTilt) : 0f;
            float fov = motor != null ? motor.SpeedShare() * SpeedFieldOfView : 0f;
            float deltaTime = Time.unscaledDeltaTime;
            _roll = Mathf.SmoothDamp(_roll, roll, ref _rollVelocity, RollSmoothSeconds, float.PositiveInfinity, deltaTime);
            _fov = Mathf.SmoothDamp(_fov, fov, ref _fovVelocity, FovSmoothSeconds, float.PositiveInfinity, deltaTime);
            data.rotation *= Quaternion.AngleAxis(_roll, Vector3.forward);
            data.fov += _fov;
        }
    }

    [HarmonyPatch(typeof(OrbitCamera), "SetOrbit")]
    internal static class OrbitCamera_SetOrbit_Patch
    {
        private static void Prefix(OrbitCamera __instance, ref OrbitCameraData data)
        {
            if (!(__instance is WalkNWashOrbitCamera)) return;
            try
            {
                CameraEffects.Apply(ref data);
            }
            catch (Exception e)
            {
                ParkourMod.Instance?.Report(e, "Camera effects");
            }
        }
    }
}

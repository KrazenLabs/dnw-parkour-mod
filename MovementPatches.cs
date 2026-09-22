using System;
using HarmonyLib;
using UnityEngine;

namespace Parkour
{
    [HarmonyPatch(typeof(LocomotionController), "JumpUpdate")]
    internal static class LocomotionController_JumpUpdate_Patch
    {
        private static void Prefix(LocomotionController __instance, ref float ___jumpInputBuffer, float ___coyoteTime)
        {
            var mod = ParkourMod.Instance;
            if (mod == null) return;
            try
            {
                var motor = mod.MotorFor(__instance);
                if (motor != null) motor.Step(ref ___jumpInputBuffer, ___coyoteTime);
            }
            catch (Exception e)
            {
                mod.Report(e, "Parkour step");
            }
        }
    }

    // Sprinting
    [HarmonyPatch(typeof(LocomotionMovementMode), "ApplyMovementInput")]
    internal static class LocomotionMovementMode_ApplyMovementInput_Patch
    {
        private static void Prefix(LocomotionMovementMode __instance, LocomotionController controller, ref float maxSpeed)
        {
            var mod = ParkourMod.Instance;
            if (mod == null || !(__instance is WalkingMovementMode)) return;
            try
            {
                var motor = mod.MotorFor(controller);
                if (motor != null) maxSpeed *= motor.SprintFactor();
            }
            catch (Exception e)
            {
                mod.Report(e, "Sprint");
            }
        }
    }

    // Take over control during wall-run
    [HarmonyPatch(typeof(FallingMovementMode), nameof(FallingMovementMode.MovementUpdate))]
    internal static class FallingMovementMode_MovementUpdate_Patch
    {
        private static void Postfix(LocomotionController controller, ref Vector3 acceleration)
        {
            var motor = ParkourMod.Instance?.Motor;
            if (motor != null && motor.IsWallRunning && motor.Player.Locomotion == controller) acceleration = Vector3.zero;
        }
    }
}

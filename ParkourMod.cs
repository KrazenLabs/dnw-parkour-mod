using System;
using System.Collections.Generic;
using DnWModLoader;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Parkour
{
    public sealed class ParkourMod : Mod
    {
        internal static ParkourMod Instance { get; private set; }
        internal ParkourSettings Settings { get; private set; }

        internal ParkourMotor Motor
        {
            get { return _motor != null && _motor.Player.IsAlive ? _motor : null; }
        }

        private readonly HashSet<string> _reportedErrors = new HashSet<string>();
        private ParkourMotor _motor;

        public override void OnInitialize()
        {
            Instance = this;
            Settings = new ParkourSettings(Config);
            Logger.Info("Initialized.");
        }

        public override void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _motor = null;
            WallProbe.ForgetSurfaces();
        }

        public override void OnUpdate()
        {
            var motor = Motor;
            if (motor == null) return;
            try
            {
                motor.UpdateInput(Time.unscaledDeltaTime);
            }
            catch (Exception e)
            {
                Report(e, "Sprint input");
            }
        }

        internal ParkourMotor MotorFor(LocomotionController locomotion)
        {
            if (_motor != null && _motor.Player.Locomotion == locomotion) return _motor;
            if (locomotion == null || locomotion.GetComponent<PlayerController>() == null) return null;
            var player = PlayerBody.Find(locomotion);
            if (player == null) return null;
            _motor = new ParkourMotor(player, Settings, Logger);
            return _motor;
        }

        internal void Report(Exception e, string what)
        {
            if (_reportedErrors.Add(what + ": " + e.GetType().FullName)) Logger.Exception(e, what + " failed");
        }
    }
}

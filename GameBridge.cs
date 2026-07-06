using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MissileCamNO
{
    /// <summary>All game-version-specific calls live here. Fix game updates in this file only.</summary>
    internal static class GameBridge
    {
        internal static bool InSinglePlayerMission()
        {
            // [CONFIRMED] MissionManager.IsRunning ; GameManager.gameState == GameState.SinglePlayer
            return MissionManager.IsRunning && GameManager.gameState == GameState.SinglePlayer;
        }

        internal static Aircraft? GetLocalAircraft()
        {
            // [CONFIRMED] GameManager.GetLocalAircraft(out Aircraft)
            return GameManager.GetLocalAircraft(out Aircraft ac) ? ac : null;
        }

        internal static void FollowUnit(Unit unit)
        {
            // [CONFIRMED] the exact API the map uses to follow a selected unit.
            var cam = SceneSingleton<CameraStateManager>.i;
            if (cam == null || unit == null) return;
            cam.SetFollowingUnit(unit);        // point the camera at this missile
            cam.SwitchState(cam.orbitState);   // orbit/follow it, just like a map selection
            // While following the missile, the game's native "Switch View" key (L) still cycles
            // through the missile's camera angles. Returning to the aircraft is done explicitly
            // via ReturnToAircraft() below (bound to a configurable key), because the native key
            // only cycles views of whatever unit is currently followed.
        }

        internal static void ReturnToAircraft()
        {
            // Point the camera back at the local player's aircraft and snap to the cockpit view.
            // [CONFIRMED] CameraStateManager.cockpitState (CameraCockpitState) + SetFollowingUnit/SwitchState.
            var cam = SceneSingleton<CameraStateManager>.i;
            if (cam == null) return;
            var aircraft = GetLocalAircraft();
            if (aircraft == null) return;
            cam.SetFollowingUnit(aircraft);      // follow our own aircraft again
            cam.SwitchState(cam.cockpitState);   // restore the normal cockpit view
        }
    }

    /// <summary>Tracks the local player's own in-flight missiles via the aircraft's launch events.</summary>
    internal sealed class MissileTracker
    {
        private Aircraft? _aircraft;
        private readonly List<Missile> _owned = new List<Missile>();

        internal void EnsureAttached(Aircraft? aircraft)
        {
            if (ReferenceEquals(aircraft, _aircraft)) return;
            Detach();
            _aircraft = aircraft;
            if (_aircraft == null) return;

            // [CONFIRMED] canonical "my missiles" API (MissileHoldCam pattern):
            _aircraft.onRegisterMissile += OnRegister;
            _aircraft.onDeregisterMissile += OnDeregister;

            // Seed with any missiles already in flight that this aircraft fired
            // (covers the case where we attach after a launch). [CONFIRMED] UnitRegistry.allUnits, Missile.owner
            foreach (var u in UnitRegistry.allUnits)
                if (u is Missile m && !m.disabled && ReferenceEquals(m.owner, _aircraft) && !_owned.Contains(m))
                    _owned.Add(m);
        }

        internal void DetachIfNeeded(Aircraft? aircraft)
        {
            if (!ReferenceEquals(aircraft, _aircraft)) Detach();
        }

        private void Detach()
        {
            if (_aircraft != null)
            {
                _aircraft.onRegisterMissile -= OnRegister;
                _aircraft.onDeregisterMissile -= OnDeregister;
            }
            _aircraft = null;
            _owned.Clear();
        }

        private void OnRegister(Missile m)   { if (m != null && !_owned.Contains(m)) _owned.Add(m); }
        private void OnDeregister(Missile m) { _owned.Remove(m); }

        /// <summary>Live, in-launch-order snapshot; prunes any that detonated/despawned.</summary>
        internal List<Missile> LiveMissiles()
        {
            _owned.RemoveAll(m => m == null || m.disabled);
            return _owned.ToList();
        }
    }
}
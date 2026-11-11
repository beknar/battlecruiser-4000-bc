using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>The ship destroyed, and back at its respawn point after a while.</summary>
    public partial class CarrierController
    {
        /// <summary>Everything aboard shuts down: guns, CIWS, the bow gun, the shield and the
        /// engines; the hull stops being solid so the wreckage isn't shot at or hit.</summary>
        void OnShipDestroyed()
        {
            SetSystems(false);
            throttle = 0f;
            speed = liftSpeed = 0f;   // the wreckage has already taken the ship's motion with it
            rudder = pitchInput = elevator = 0f;
        }

        /// <summary>Back at the starting point, whole, at rest, with everything working.</summary>
        void OnShipRespawned()
        {
            if (spawnFrame != null) spawnPosition = spawnFrame.position + spawnFrame.rotation * spawnOffset;
            transform.SetPositionAndRotation(spawnPosition, spawnRotation);
            heading = spawnRotation.eulerAngles.y;
            pitchAngle = 0f;
            rollAngle = 0f;
            speed = liftSpeed = 0f;
            throttle = 0f;
            autoLevel = false;
            SetSystems(true);
        }

        // found once at the start: when the ship comes apart its gun mounts fly off as wreckage
        // and are no longer under it
        CarrierTurret[] allTurrets;
        MainBeam[] allBeams;
        Collider[] hullColliders;
        Shield shieldComp;
        ThrusterFlare[] allFlares;

        void FindSystems()
        {
            if (allTurrets != null) return;
            allTurrets = GetComponentsInChildren<CarrierTurret>(true);
            allBeams = GetComponentsInChildren<MainBeam>(true);
            hullColliders = System.Array.FindAll(GetComponentsInChildren<Collider>(true), c => c.GetComponent<Shield>() == null);
            shieldComp = GetComponentInChildren<Shield>(true);
            allFlares = GetComponentsInChildren<ThrusterFlare>(true);
        }

        void SetSystems(bool on)
        {
            FindSystems();
            foreach (var tu in allTurrets) if (tu) { if (!on) tu.target = null; tu.enabled = on; }
            if (on) SetTarget(target);
            var ciws = GetComponent<CiwsDirector>();
            if (ciws != null) ciws.active = on;
            foreach (var beam in allBeams)
            {
                if (!beam) continue;
                if (!on) beam.Abort();
                beam.enabled = on;
            }
            if (shieldComp != null) shieldComp.SetOffline(!on);
            foreach (var c in hullColliders) if (c) c.enabled = on;
            if (engines != null)
                foreach (var e in engines)
                {
                    if (!e) continue;
                    e.enabled = on;
                    foreach (var r in e.GetComponentsInChildren<Renderer>(true)) r.enabled = on;
                    if (e.glow) e.glow.enabled = on;
                }
            foreach (var f in allFlares) if (f) { f.thrust = 0f; f.Apply(0f); f.enabled = on; }
            if (hum != null) hum.volume = on ? hum.volume : 0f;
        }

        /// <summary>Where the ship comes back after being destroyed (the last place it warped to),
        /// kept relative to the reach it is in (a planet's, moon's or field's orbit, which doesn't
        /// spin, or a station turning with its planet), or else to `frame`.</summary>
        public void SetSpawn(Vector3 position, Quaternion rotation, Transform frame = null)
        {
            if (OrbitalSystem.Instance != null)
            {
                var reach = OrbitalSystem.Instance.ReachAt(position);
                if (reach != null) frame = reach;
            }
            spawnPosition = position;
            spawnRotation = rotation;
            spawnFrame = frame;
            spawnOffset = frame != null ? Quaternion.Inverse(frame.rotation) * (position - frame.position) : Vector3.zero;
        }
    }
}

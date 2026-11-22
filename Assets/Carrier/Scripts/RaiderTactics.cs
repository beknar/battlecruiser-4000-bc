using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// The Imperial Raider's way of fighting: fast attack on the light. It goes for the lightest
    /// thing of the players' it can see: their drones and fighters first, then a wingman or a ship
    /// of their fleet, then the lightest player's ship (the least hull, built). It makes the
    /// fighter pilot's runs on it, and breaks off for home when the heavier side closes (its own
    /// hull under `breakBelow`). Its guns double as point defence against missiles (PointDefence).
    /// </summary>
    public class RaiderTactics : SensorTactic
    {
        public float breakBelow = 0.3f;
        /// <summary>What it went for, in words (the tests).</summary>
        public string Why { get; private set; } = "";
        public Transform Prey { get; private set; }

        protected override void Think(bool seeing)
        {
            Prey = Choose(out string why);
            Why = why;
            if (Prey == null) { Doing = "searching"; return; }
            if (Prey != pilot.target) { pilot.target = Prey; if (weapons != null) weapons.SetTarget(Prey); }
            Doing = HullOf(transform) < breakBelow ? "breaking off" : $"raiding {Prey.name} ({why})";
        }

        Transform Choose(out string why)
        {
            // the players' craft
            foreach (var s in PlayerShips.All)
            {
                var cc = s != null ? s.GetComponent<CarrierController>() : null;
                if (cc == null) continue;
                foreach (var c in cc.Wing)
                    if (c != null && c.isActiveAndEnabled && Sees(c.transform)) { why = "a drone or fighter"; return c.transform; }
            }
            // a wingman or a ship of their fleet (the light escorts), then the lightest player's ship
            Transform best = null;
            float bestHull = float.MaxValue;
            bool escort = false;
            foreach (var p in PlayerShips.All)
            {
                if (p == null || !p.gameObject.activeInHierarchy || !PlayerShips.Present(p) || !Sees(p)) continue;
                if (!p.TryGetComponent(out Health h) || h.Dead) continue;
                bool isEscort = p.GetComponent<Demo.FleetPilot>() != null || p.GetComponent<Demo.Wingman>() != null;
                if (escort && !isEscort) continue;
                if (isEscort && !escort) { escort = true; bestHull = float.MaxValue; }
                if (h.maxHealth < bestHull) { bestHull = h.maxHealth; best = p; }
            }
            why = best == null ? "" : escort ? "a wingman or escort" : "the lightest ship";
            return best;
        }

        public override void Steer(Vector3 pos, ref Vector3 desired, ref float wantSpeed)
        {
            if (Prey == null) return;
            // hurt: out, fast, away from what it raided
            if (HullOf(transform) < breakBelow) { desired = (pos - Prey.position).normalized; wantSpeed = pilot.attackSpeed; }
        }
    }
}

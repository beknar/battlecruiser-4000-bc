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

        protected override void Stood() => Prey = null;

        protected override void OnDisable()
        {
            base.OnDisable();
            Prey = null;
        }

        protected override void Think(bool seeing)
        {
            // 3. hurt: it breaks off for home and hides (back with the spawns, or repaired)
            if (HullOf(transform) < breakBelow)
            {
                Prey = null;
                Doing = "breaking off";
                if (pilot.Current == CombatPilot.State.Engage) { pilot.target = null; pilot.Disengage(); }
                return;
            }
            Prey = Choose(out string why);
            Why = why;
            if (Prey == null) { Doing = "searching"; return; }
            if (Prey != pilot.target) { pilot.target = Prey; if (weapons != null) weapons.SetTarget(Prey); }
            Doing = $"raiding {Prey.name} ({why})";
        }

        Transform Choose(out string why)
        {
            // the players' craft (their drones and fighters, the nearest it sees)
            Transform craft = null;
            float near = float.MaxValue;
            foreach (var s in PlayerShips.All)
            {
                var cc = s != null ? s.GetComponent<CarrierController>() : null;
                if (cc == null) continue;
                foreach (var c in cc.Wing)
                {
                    if (c == null || !c.isActiveAndEnabled || c.kind == Craft.Kind.Dropship || !Sees(c.transform)) continue;
                    float d = (c.transform.position - transform.position).sqrMagnitude;
                    if (d < near) { near = d; craft = c.transform; }
                }
            }
            if (craft != null) { why = "a drone or fighter"; return craft; }
            // a wingman or a ship of their fleet (the light escorts), then the lightest player's ship
            Transform best = null;
            float bestHull = float.MaxValue;
            bool escort = false;
            foreach (var p in Rivalry.TargetsFor(transform))
            {
                if (p == null || !Sees(p)) continue;
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
            // (it makes the fighter pilot's runs on its prey; hurt, it has disengaged: CombatPilot
            // flies it home)
        }
    }
}

using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// The Grey Lance's way of fighting: precision strike.
    /// - It goes for the cripple: of the players' ships it can see, the one with the least hull
    ///   left; but first anything of theirs that goes for its Shroud or Reaper (a drone or fighter
    ///   within `screenRange` of them), and a facility (a player's or ally's installation) close
    ///   in its way when no player's ship is near.
    /// - It doesn't come head on: it swings out to the target's flank or rear (`flankOffset`)
    ///   and runs in from there, its salvo on the engines and weapons, then breaks off (the
    ///   fighter pilot's run).
    /// </summary>
    public class LanceTactics : GreysTactic
    {
        public float flankOffset = 1800f;
        public float screenRange = 3000f;
        public float facilityRange = 4000f;
        [Tooltip("Flanking at most this long (s), then in from wherever it is: a target that turns with it is never flanked.")]
        public float flankTime = 6f;
        float flankSince;
        bool closedIn;   // (this run has come in close: its pass can be over)

        public enum Phase { None, Flank, Run }
        public Phase Current { get; private set; }
        /// <summary>Which flank it took (+1 right, -1 left), and what it went for, in words.</summary>
        public int Side { get; private set; } = 1;
        public string Why { get; private set; } = "";
        /// <summary>Runs made, each from a flank (the tests).</summary>
        public int Passes { get; private set; }
        Transform strike;

        protected override void Stood()
        {
            Current = Phase.None;
            strike = null;
            ShipSystems.SetAim(transform.root, ShipSystem.Hull);
        }

        protected override void Think(bool seeing)
        {
            strike = Choose(out string why);
            Why = why;
            if (strike == null) { Stood(); Doing = "searching"; return; }
            if (strike != Target)
            {
                pilot.target = strike;
                if (weapons != null) weapons.SetTarget(strike);
            }
            // its salvo on the engines (a ship that can't run is the Reaper's), then the weapons
            var sys = strike.GetComponent<ShipSystems>();
            ShipSystems.SetAim(transform.root, sys != null && sys.EnginesDown ? ShipSystem.Weapons : ShipSystem.Engines);
            // out to the flank till it is there, then the run
            Vector3 flank = FlankPoint(strike);
            if (Current != Phase.Run && ((flank - transform.position).sqrMagnitude < 600f * 600f || (Current == Phase.Flank && Time.time - flankSince > flankTime))) { Current = Phase.Run; closedIn = false; }
            else if (Current == Phase.None) { Current = Phase.Flank; flankSince = Time.time; Side = Random.value < 0.5f ? 1 : -1; }
            // past the target and going away (the pilot's run over: it extends): round again from
            // the other flank
            Vector3 toT = strike.position - transform.position;
            // (only once the run has closed in: one started from the flank on a target behind it
            // turns in first, and isn't a pass yet)
            if (Current == Phase.Run && toT.magnitude < 900f) closedIn = true;
            if (Current == Phase.Run && closedIn && toT.magnitude > 1200f && Vector3.Dot(transform.forward, toT) < 0f) { Current = Phase.Flank; flankSince = Time.time; Side = -Side; Passes++; }
            Doing = $"{(Current == Phase.Run ? "attack run" : "flanking")} on {strike.name} ({why})";
        }

        /// <summary>What it strikes: a player's craft at its Shroud or Reaper, else a facility in
        /// its way with no player's ship near, else the seen player's ship with the least hull.</summary>
        EncounterGroup band;
        bool bandLooked;

        /// <summary>Is `g` of this ship's own band (none known: any)?</summary>
        bool OfMyBand(GreysShip g)
        {
            if (!bandLooked)
            {
                bandLooked = true;
                foreach (var eg in FindObjectsByType<EncounterGroup>(FindObjectsSortMode.None))
                    if (eg.pilots != null && System.Array.IndexOf(eg.pilots, pilot) >= 0) { band = eg; break; }
            }
            return band == null || (g.TryGetComponent(out CombatPilot gp) && band.pilots != null && System.Array.IndexOf(band.pilots, gp) >= 0);
        }

        Transform Choose(out string why)
        {
            foreach (var g in GreysShip.All)
            {
                if (g == null || !g.Alive || (g.role != GreysShip.Role.Shroud && g.role != GreysShip.Role.Reaper) || !OfMyBand(g)) continue;
                foreach (var s in PlayerShips.All)
                {
                    var cc = s != null ? s.GetComponent<CarrierController>() : null;
                    if (cc == null) continue;
                    foreach (var c in cc.Wing)
                        if (c != null && c.isActiveAndEnabled && (c.transform.position - g.transform.position).sqrMagnitude < screenRange * screenRange && Sees(c.transform))
                        { why = $"screening the {g.role}"; return c.transform; }
                }
            }
            Transform cripple = null;
            float least = 10f;
            foreach (var p in Rivalry.TargetsFor(transform))
            {
                if (p == null || !Sensors.CanSee(transform, p)) continue;
                if (p.TryGetComponent(out Health h) && h.Dead) continue;
                // (the factions at war, the many whole ships told apart by how near: the nearest)
                float hull = HullOf(p) + (Rivalry.On ? Vector3.Distance(transform.position, p.position) / 40000f : 0f);
                if (hull < least) { least = hull; cripple = p; }
            }
            if (cripple == null)
            {
                foreach (var inst in Installation.All)
                {
                    if (inst == null || inst.hostile || !inst.TryGetComponent(out Health ih) || ih.Dead) continue;
                    if ((inst.transform.position - transform.position).sqrMagnitude < facilityRange * facilityRange)
                    { why = "a facility in its way"; return inst.transform; }
                }
                why = "";
                return null;
            }
            why = least < 0.5f ? "running down a cripple" : "the weakest it sees";
            return cripple;
        }

        Vector3 FlankPoint(Transform t)
        {
            Vector3 fwd = t.forward, right = t.right;
            return t.position + right * Side * flankOffset - fwd * flankOffset * 0.6f;
        }

        public override void Steer(Vector3 pos, ref Vector3 desired, ref float wantSpeed)
        {
            if (strike == null || Current != Phase.Flank) return;
            Vector3 go = FlankPoint(strike) - pos;
            desired = go.sqrMagnitude > 1f ? go.normalized : desired;
            wantSpeed = pilot.attackSpeed;
        }
    }
}

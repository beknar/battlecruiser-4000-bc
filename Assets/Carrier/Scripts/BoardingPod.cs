using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// A Vrask boarding pod: a small, fast, armoured craft launched from a Fang's pod hatches (or
    /// by a Snare at the ship its tractor beam holds: PodLauncher). It flies at a ship as a missile
    /// does, and is one: co-op shows it with the missiles, and point defence shoots it down
    /// (Missile.toughness: it takes more hits than a missile).
    ///
    /// On reaching a ship whose shield is down, or under a quarter (ShieldBelow), it breaches the
    /// hull and puts a swarm of breachers aboard (Boarders.Board), who fight the crew. With the
    /// shield up it bursts on it, for a little damage. A ship with no crew to fight (automated)
    /// isn't boarded: the pod bursts on it. One shown from another player's game (no damage) does
    /// neither: the host's game decides, and tells the boarded ship's game (CoopWorld.Boarding).
    /// </summary>
    public class BoardingPod : Missile
    {
        /// <summary>A shield under this share of its strength lets a pod (or a Fang's ram) through.</summary>
        public const float ShieldBelow = 0.25f;

        [Tooltip("Breachers aboard: at least, at most.")]
        public int crewMin = Boarders.PodMin;
        public int crewMax = Boarders.PodMax;

        /// <summary>Pods that got aboard, and that burst on a shield, since the level started.</summary>
        public static int Breached { get; private set; }
        public static int Burst { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCounts() { Breached = Burst = 0; }

        protected override void Detonate()
        {
            if (Gone) return;
            var ship = target != null ? TractorBeam.ShipOf(target) : null;
            // (against the hull itself too: a big ship's centre can be far from where it hits)
            if (damage > 0f && ship != null && (AtTarget() || Touching(ship)))
            {
                if (ShieldLets(ship) && Boarders.CanBoard(ship))
                {
                    Breached++;
                    Boarders.Board(ship, Random.Range(crewMin, crewMax + 1), Boarders.FactionOf(owner));
                    SoundFX.Play(Sfx.Impact, transform.position, 1f, 0.55f);
                    Finish(null);
                    return;
                }
                Burst++;
            }
            base.Detonate();
        }

        static readonly Collider[] Near = new Collider[24];

        /// <summary>Up against the ship: one of its colliders within reach.</summary>
        bool Touching(Transform ship)
        {
            int n = Physics.OverlapSphereNonAlloc(transform.position, proximity + 15f, Near, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (Near[i] != null && Near[i].transform.IsChildOf(ship)) return true;
            return false;
        }

        /// <summary>Does a ship's shield let a pod through: none, down, or under ShieldBelow
        /// (another player's ship: as its game last said)?</summary>
        public static bool ShieldLets(Transform ship) => ShieldUnder(ship, ShieldBelow);

        /// <summary>Is a ship's shield under this share of its strength (none, or down: yes)?</summary>
        public static bool ShieldUnder(Transform ship, float share)
        {
            ship = TractorBeam.ShipOf(ship);
            if (ship == null) return false;
            var rp = ship.GetComponent<Demo.RemotePlayer>();
            if (rp != null) return rp.Shield < share;
            var s = ship.GetComponentInChildren<Shield>(true);
            return s == null || !s.Up || s.Fraction < share;
        }
    }
}

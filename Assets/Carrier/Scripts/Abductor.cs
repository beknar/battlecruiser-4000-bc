using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// The Grey Reaper's abduction beam, from its "abduct" point: while its tractor holds a ship
    /// (TractorBeam), every `interval` seconds it takes `crewPerTake` of its crew alive
    /// (Crew.Take: prisoners of the Greys, not dead; never below the survivors: the Greys take
    /// people, not ships) and `cargoPerTake` tonnes of ore from its hold (CargoHold.Abduct). After
    /// `takes` of them (or the crew down to its survivors) it takes one sample: a component of the
    /// ship, at random, its condition halved till it is repaired; then its work is done (Done:
    /// the group's prize, GreysFleet withdraws with it).
    ///
    /// Another player's ship in co-op is abducted from in that player's game: the host tells it
    /// (SendAbduct). Drawn as a pale shaft from its point to the ship while it takes.
    /// </summary>
    public class Abductor : MonoBehaviour
    {
        public float interval = 3f;
        public int crewPerTake = 4;
        public float cargoPerTake = 6f;
        public int takes = 8;
        public Transform point;
        public LineRenderer shaft;

        TractorBeam beam;
        Health health;
        float next;
        int done;
        Transform working;

        /// <summary>Crew taken, tonnes taken, samples taken (here, or sent to another game).</summary>
        public int CrewTaken { get; private set; }
        public float CargoTaken { get; private set; }
        public int Samples { get; private set; }
        /// <summary>Its work on the ship it holds is done: the prize taken.</summary>
        public bool Done { get; private set; }
        /// <summary>The ship it took from last.</summary>
        public Transform Took { get; private set; }

        /// <summary>Co-op: an abduction from another player's ship (to, crew, tonnes, a sample's
        /// component + 1 or 0), sent to that player's game; true if it was theirs.</summary>
        public static System.Func<Transform, int, float, int, bool> SendAbduct;

        void Awake()
        {
            beam = GetComponent<TractorBeam>();
            health = GetComponent<Health>();
            if (point == null) point = transform;
        }

        void OnDisable() { if (shaft != null) shaft.enabled = false; }

        void Update()
        {
            var held = beam != null && beam.Locked && (health == null || !health.Dead) ? beam.Holding : null;
            if (held != working) { working = held; done = 0; Done = false; next = Time.time + interval; }
            if (shaft != null)
            {
                shaft.enabled = held != null && !Done;
                if (shaft.enabled) { shaft.SetPosition(0, point.position); shaft.SetPosition(1, held.position); }
            }
            if (held == null || Done || Time.time < next) return;
            next = Time.time + interval;
            Take(held);
        }

        /// <summary>One take from the ship it holds: crew, cargo, and at the last a sample.</summary>
        void Take(Transform ship)
        {
            done++;
            var crew = ship.GetComponent<Crew>();
            // (people are taken whole: within one of its survivors is as low as it goes)
            bool last = done >= takes || (crew != null && crew.Current - crew.Floor < 1f);
            ShipSystem sample = ShipSystem.Hull;
            if (last) sample = ShipSystems.Components[Random.Range(0, ShipSystems.Components.Length)];
            Took = ship;
            // another player's ship: their game does it
            if (SendAbduct != null && SendAbduct(ship, crewPerTake, cargoPerTake, last ? (int)sample + 1 : 0))
            {
                CrewTaken += crewPerTake;
                CargoTaken += cargoPerTake;
                if (last) { Samples++; Done = true; }
                return;
            }
            var (n, t) = Apply(ship, crewPerTake, cargoPerTake, last ? (int)sample + 1 : 0, GreysShip.Faction);
            CrewTaken += n;
            CargoTaken += t;
            if (last) { Samples++; Done = true; }
        }

        /// <summary>An abduction on a ship in this game (its own, or told by the host): crew,
        /// tonnes, and a sample (component + 1; 0: none). Returns what was taken.</summary>
        public static (int crew, float tonnes) Apply(Transform ship, int crew, float tonnes, int sample, string faction)
        {
            if (ship == null) return (0, 0f);
            int n = ship.TryGetComponent(out Crew c) ? c.Take(crew, faction) : 0;
            float t = ship.TryGetComponent(out CargoHold hold) ? hold.Abduct(tonnes) : 0f;
            string what = null;
            if (sample > 0 && ship.TryGetComponent(out ShipSystems sys))
            {
                var s = (ShipSystem)(sample - 1);
                sys.SetCondition(s, sys.Condition(s) * 0.5f);
                what = ShipSystems.Label(s);
            }
            if (ship == PlayerShips.Primary && (n > 0 || t > 0f || what != null))
            {
                SoundFX.Ui(Sfx.UiAlert);
                Notices.Post($"The {faction} are taking us: {n} of the crew gone{(t > 0f ? $", {t:0} t from the hold" : "")}" +
                             (what != null ? $"; they took a sample of our {what}" : ""));
            }
            return (n, t);
        }
    }
}

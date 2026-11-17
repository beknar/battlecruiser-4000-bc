using System;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// Boarders on a ship: the Vrask breachers a boarding pod (BoardingPod) or a Fang's ram
    /// (FangTactics) has put aboard, fighting its crew (Crew). Every moment the boarders overcome
    /// crew and the crew kill boarders, each side by its numbers (Fight): a boarder is worth many
    /// crewmen, but only so many of the crew can get at each one (Frontage), so a big crew grinds
    /// a small party down and a small crew is overrun. Of the crew the boarders overcome, half
    /// are taken alive (the Vrask take prisoners) and half are killed.
    ///
    /// - Repelled: every boarder dead. The crew they had taken are freed.
    /// - Captured: the crew down to its survivors (CrewTable.survivors of the complement: Crew.Lose
    ///   goes no lower, and IndustryLedger.DropOff keeps that many) with boarders still alive. The
    ///   ship is taken whole (Health.Capture: no breakup, no wreck), and those taken are prisoners
    ///   of the Vrask (WorldState.Prisoners). The player's ship comes back at its respawn point with
    ///   the survivors aboard, its hold seized (CarrierController.Boarding, CargoHold); a fleet ship
    ///   is lost to the fleet (FleetDirector).
    ///
    /// The fight runs in the game whose ship it is: in co-op the host's pods send word to the
    /// boarded player's game (CoopWorld.Boarding), which fights it and decides the capture, and
    /// says how many boarders are aboard in its ship's updates (CoopMsg.bd).
    /// </summary>
    [DisallowMultipleComponent]
    public class Boarders : MonoBehaviour
    {
        /// <summary>Crew a boarder overcomes a second.</summary>
        public const float KillRate = 0.25f;
        /// <summary>Boarders a crewman fighting them kills a second.</summary>
        public const float DefendRate = 0.02f;
        /// <summary>Crew who can get at each boarder (more than that can't join the fight).</summary>
        public const float Frontage = 4f;
        /// <summary>Of the crew the boarders overcome, the share taken alive (the rest are killed).</summary>
        public const float TakenShare = 0.5f;
        /// <summary>Breachers in a pod (at least, at most), and the party a Fang's ram puts aboard.</summary>
        public const int PodMin = 8, PodMax = 12, RamParty = 12;

        public enum Outcome { None, Fighting, Repelled, Captured }

        /// <summary>How the last fight went (None: no fight yet, or cleared).</summary>
        public Outcome Result { get; private set; }
        /// <summary>Boarders alive (fractions of a boarder add up).</summary>
        public float Count { get; private set; }
        /// <summary>Boarders aboard and fighting, whole.</summary>
        public int Aboard => Result == Outcome.Fighting && Count > 0f ? Mathf.Max(1, Mathf.RoundToInt(Count)) : 0;
        /// <summary>Boarders that have come aboard in this fight, and the breaches they came through.</summary>
        public int Arrived { get; private set; }
        public int Breaches { get; private set; }
        public float BoardersKilled { get; private set; }
        /// <summary>Crew killed, and taken alive, by the boarders in this fight.</summary>
        public float CrewKilled { get; private set; }
        public float CrewTaken { get; private set; }
        /// <summary>The crew taken prisoner (when captured), and freed (when repelled).</summary>
        public int Prisoners { get; private set; }
        public int Freed { get; private set; }
        /// <summary>When this fight began, and ended.</summary>
        public float Began { get; private set; } = -1f;
        public float Ended { get; private set; } = -1f;

        /// <summary>A fight is over: repelled or captured (the tests, the HUD).</summary>
        public static event Action<Boarders> Over;
        /// <summary>Co-op: takes a boarding of a ship another player's game flies (true: sent there).</summary>
        public static Func<Transform, int, bool> SendAboard;
        /// <summary>Co-op: boarders aboard another player's ship, as its game last said.</summary>
        public static Func<Transform, int> RemoteCount;

        Crew crew;
        Health health;

        // ------------------------------------------------------------------ the fight's numbers

        /// <summary>`dt` seconds of the fight between `boarders` and `crew` aboard: the crew the
        /// boarders overcome, and the boarders the crew kill.</summary>
        public static void Fight(float boarders, float crew, float dt, out float crewLost, out float boardersLost)
        {
            boarders = Mathf.Max(0f, boarders);
            crew = Mathf.Max(0f, crew);
            crewLost = boarders * KillRate * dt;
            boardersLost = Mathf.Min(crew, boarders * Frontage) * DefendRate * dt;
        }

        // ------------------------------------------------------------------ boarding a ship

        /// <summary>Can boarders get aboard this ship and fight (a crew to fight: one with none is
        /// automated)? Another player's ship in co-op can: its own game fights it.</summary>
        public static bool CanBoard(Transform ship)
        {
            ship = TractorBeam.ShipOf(ship);
            if (ship == null) return false;
            if (RemoteCount != null && ship.GetComponent<Demo.RemotePlayer>() != null) return true;
            var h = ship.GetComponent<Health>();
            var c = ship.GetComponent<Crew>();
            return h != null && !h.Dead && c != null && c.complement > 0;
        }

        /// <summary>`n` boarders get aboard a ship (a pod's breach, a Fang's ram): they join any
        /// already fighting there, or start a fight. In co-op, another player's ship is boarded in
        /// that player's game (SendAboard). Returns the fight here (null: sent, or no crew).</summary>
        public static Boarders Board(Transform ship, int n)
        {
            ship = TractorBeam.ShipOf(ship);
            if (ship == null || n <= 0) return null;
            if (SendAboard != null && SendAboard(ship, n)) return null;
            if (!CanBoard(ship)) return null;
            var b = ship.GetComponent<Boarders>();
            if (b == null) b = ship.gameObject.AddComponent<Boarders>();
            b.Add(n);
            return b;
        }

        /// <summary>The boarders on a ship (its fight, over or not), or null.</summary>
        public static Boarders On(Transform ship)
        {
            ship = TractorBeam.ShipOf(ship);
            return ship != null ? ship.GetComponent<Boarders>() : null;
        }

        /// <summary>Boarders aboard a ship now (another player's, as its game last said).</summary>
        public static int CountOn(Transform ship)
        {
            ship = TractorBeam.ShipOf(ship);
            if (ship == null) return 0;
            var b = ship.GetComponent<Boarders>();
            if (b != null) return b.Aboard;
            return RemoteCount != null ? RemoteCount(ship) : 0;
        }

        /// <summary>The HUD's warning for a ship with boarders aboard (empty: none): how many, and
        /// the crew fighting them.</summary>
        public static string HudText(Transform ship)
        {
            var b = On(ship);
            if (b == null || b.Aboard <= 0) return "";
            var c = b.crew;
            string crewText = c != null ? $"crew {c.Aboard} of {c.complement} fighting them" : "the crew fighting them";
            return $"BOARDERS ABOARD: {b.Aboard}\n{crewText}";
        }

        // ------------------------------------------------------------------ the fight

        void Awake()
        {
            crew = GetComponent<Crew>();
            health = GetComponent<Health>();
            if (health != null)
            {
                health.onRespawned ??= new UnityEngine.Events.UnityEvent();
                health.onRespawned.AddListener(Clear);
            }
        }

        void OnDestroy()
        {
            if (health != null && health.onRespawned != null) health.onRespawned.RemoveListener(Clear);
        }

        bool Primary => transform == PlayerShips.Primary;
        /// <summary>One of the players' ships (a fleet ship, a wingman's).</summary>
        bool Ours
        {
            get
            {
                foreach (var s in PlayerShips.All) if (s == transform) return true;
                return false;
            }
        }
        string ShipName => TryGetComponent(out CarrierController cc) ? cc.shipName : name;

        void Add(int n)
        {
            if (Result != Outcome.Fighting)
            {
                Clear();
                Result = Outcome.Fighting;
                Began = Time.time;
            }
            bool first = Arrived == 0;
            Count += n;
            Arrived += n;
            Breaches++;
            if (Primary)
            {
                SoundFX.Ui(Sfx.UiAlert);
                Notices.Post(first ? $"Boarders! {n} Vrask breachers are aboard" : $"{n} more boarders aboard: {Aboard} now");
            }
            else if (first && Ours) Notices.Post($"The {ShipName} is boarded: {n} Vrask aboard");
            // (a crew already down to its survivors can't fight them off)
            Step(0f);
        }

        void Update()
        {
            if (Result == Outcome.Fighting) Step(Time.deltaTime);
        }

        /// <summary>How long boarders fight before a ship can be taken, however few its crew.</summary>
        public const float MinFight = 15f;
        float fought;

        /// <summary>`dt` seconds of the fight (public for the tests).</summary>
        public void Step(float dt)
        {
            if (Result != Outcome.Fighting) return;
            if (crew == null || health == null || health.Dead) { Count = 0f; Result = Outcome.None; return; }
            Fight(Count, crew.Current, dt, out float crewLost, out float boardersLost);
            float before = crew.Current;
            if (crewLost > 0f) crew.Lose(crewLost);           // (never below the survivors)
            float lost = before - crew.Current;
            CrewTaken += lost * TakenShare;
            CrewKilled += lost * (1f - TakenShare);
            float k = Mathf.Min(Count, boardersLost);
            Count -= k;
            BoardersKilled += k;
            fought += dt;
            if (Count < 0.5f) Repel();
            // (taken once the crew is down to its survivors, and the boarders have fought for the
            // ship a while: never at the moment a pod breaches)
            else if (crew.AtFloor && fought >= MinFight) Capture();
        }

        /// <summary>Every boarder dead: those they had taken are freed.</summary>
        void Repel()
        {
            BoardersKilled += Count;
            Count = 0f;
            Result = Outcome.Repelled;
            Ended = Time.time;
            Freed = crew != null ? crew.Add(Mathf.FloorToInt(CrewTaken + 0.01f)) : 0;
            if (Primary) Notices.Post($"Boarders repelled: {Mathf.RoundToInt(CrewKilled)} crew killed, {Freed} freed");
            else if (Ours) Notices.Post($"The {ShipName} has thrown off her boarders");
            Over?.Invoke(this);
        }

        /// <summary>The crew at its survivors, boarders still aboard: the ship is taken. Those the
        /// boarders took are prisoners of the Vrask; the survivors are left aboard.</summary>
        void Capture()
        {
            Result = Outcome.Captured;
            Ended = Time.time;
            Prisoners = Mathf.RoundToInt(CrewTaken);
            WorldState.TakePrisoners(Prisoners);
            Over?.Invoke(this);
            health.Capture();
        }

        /// <summary>No fight (a new one, or the ship back from capture).</summary>
        public void Clear()
        {
            Result = Outcome.None;
            Count = 0f;
            Arrived = Breaches = Prisoners = Freed = 0;
            BoardersKilled = CrewKilled = CrewTaken = 0f;
            Began = Ended = -1f;
        }
    }
}

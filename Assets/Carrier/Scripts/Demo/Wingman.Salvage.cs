using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The wingman's Salvage order: it strips the wrecks fights leave (Wreck), with the salvage
    /// beam a player has (Salvage), one after another.
    ///   - the wreck asked for ("the wreck of the Fang Hunter"), else the nearest within the
    ///     sensors' reach (30 km);
    ///   - warps to it if it's far, and flies in till the beam reaches it;
    ///   - turns the beam on (in combat mode, where the beam works) and holds still while it
    ///     strips the wreck; when it's stripped, the next one;
    ///   - with the hold full, to the nearest market (as mining goes), where it sells everything
    ///     aboard (IndustryPanel.SellCargo: goods, parts and any ore; the parts kept back for a
    ///     player who asked for them, "give me the parts", while they take under half the hold),
    ///     and back to the wrecks;
    ///   - with none left, it says so and goes back to the order before (following or defending
    ///     its leader, mining, or holding).
    /// A fight close by is fought first, the beam off; then it carries on, as mining does. Every
    /// game has its own wrecks, so what it takes is never a player's.
    /// </summary>
    public partial class Wingman
    {
        public enum SalvageStep { Find, ToWreck, Strip, ToMarket, Docking, Sell, Leave }

        /// <summary>Where it is in its round of salvaging and selling.</summary>
        public SalvageStep SalvagingStep { get; private set; }
        /// <summary>The wreck it is flying to or stripping (null: choosing the next).</summary>
        public Wreck SalvageTarget { get; private set; }
        /// <summary>What the wreck asked for was the wreck of ("" for the nearest).</summary>
        public string SalvageNamed { get; private set; } = "";
        /// <summary>Ran out of wrecks (and went back to the order before): the order is done.</summary>
        public bool SalvageFinished { get; private set; }
        /// <summary>What the salvage sold for, and how often, since the order was given.</summary>
        public float SalvageCredits { get; private set; }
        public int SalvageSales { get; private set; }
        /// <summary>The player the parts it salvages are kept for (0: nobody; they're sold). CoopBot
        /// sets it ("give me the parts") and hands them over when that player is near.</summary>
        public int PartsFor { get; set; }

        /// <summary>The most of the hold parts kept for a player may take; past it they're sold.</summary>
        public const float PartsKept = 0.5f;
        /// <summary>A wreck further than this (m) is warped to, not flown to.</summary>
        public const float WreckWarp = 15000f;

        // what the beam had salvaged when the order was given (Salvage.Salvaged counts the level's)
        float salvaged0;
        // the order before (back to it with no wrecks left), and its leader
        Order afterSalvage = Order.Hold;
        int afterSalvageLeader;
        // where it was salvaging: the next wreck is looked for from there (after a sale, say)
        Vector3? salvagingAt;
        // wrecks it couldn't get to or strip (passed over this order)
        readonly HashSet<Wreck> passedOver = new HashSet<Wreck>();

        Salvage salvager;
        /// <summary>The salvage beam on its ship (the level's Salvage, made by the HUD).</summary>
        Salvage Salvager
        {
            get
            {
                if (salvager != null && salvager.demo == demo) return salvager;
                var s = Salvage.Instance;
                salvager = s != null && s.demo == demo ? s : FindObjectsByType<Salvage>(FindObjectsSortMode.None).FirstOrDefault(x => x.demo == demo);
                return salvager;
            }
        }

        /// <summary>Tonnes the beam has taken since the order was given.</summary>
        public float SalvageTaken => Salvager != null ? Mathf.Max(0f, Salvager.Salvaged - salvaged0) : 0f;

        /// <summary>The nearest wreck with something left in it within `reach` of a point (none: null).</summary>
        public static Wreck NearestWreck(Vector3 from, float reach = Salvage.Around, ICollection<Wreck> except = null) =>
            Wreck.All.Where(w => w != null && !w.Empty && (except == null || !except.Contains(w)) && (w.transform.position - from).sqrMagnitude < reach * reach)
                     .OrderBy(w => (w.transform.position - from).sqrMagnitude).FirstOrDefault();

        /// <summary>The wreck an order names ("the Fang Hunter", "the wreck of the Fang Hunter",
        /// "Fang Hunter wreck"), by what it was (the whole name, or words of it in a row, as a
        /// target's is matched), the nearest of the best with something left in it; null if none.</summary>
        public static Wreck WreckNamed(string said, Vector3 from)
        {
            string name = WreckName(said);
            if (name.Length == 0) return null;
            Wreck best = null;
            int bestScore = 0;
            float bestDist = float.MaxValue;
            foreach (var w in Wreck.All)
            {
                if (w == null || w.Empty) continue;
                int score = NameMatch(name, w.Of, false);
                if (score == 0 && w.Of.IndexOf(name, System.StringComparison.OrdinalIgnoreCase) >= 0) score = 1;
                if (score == 0) continue;
                float d = (w.transform.position - from).sqrMagnitude;
                if (score > bestScore || (score == bestScore && d < bestDist)) { best = w; bestScore = score; bestDist = d; }
            }
            return best;
        }

        /// <summary>What a wreck said is the wreck of: "the wreck of the Fang Hunter", "Fang
        /// Hunter's wreck", "the Fang Hunter wreck": "Fang Hunter".</summary>
        public static string WreckName(string said)
        {
            string s = (said ?? "").Trim().TrimEnd('.', '!', '?').Trim();
            foreach (var lead in new[] { "the wreck of ", "that wreck of ", "wreck of ", "the wreckage of ", "wreckage of ", "what's left of ", "what is left of " })
                if (s.StartsWith(lead, System.StringComparison.OrdinalIgnoreCase)) { s = s.Substring(lead.Length).Trim(); break; }
            foreach (var tail in new[] { "'s wreck", "'s wreckage", " wreck", " wreckage" })
                if (s.EndsWith(tail, System.StringComparison.OrdinalIgnoreCase)) { s = s.Substring(0, s.Length - tail.Length).Trim(); break; }
            foreach (var article in new[] { "the ", "that ", "this ", "a " })
                if (s.StartsWith(article, System.StringComparison.OrdinalIgnoreCase)) { s = s.Substring(article.Length).Trim(); break; }
            return s;
        }

        /// <summary>Salvage `first` (the one asked for, or the nearest), then the wrecks near it,
        /// selling what it takes when the hold is full; with none left, back to the order before.
        /// `named`: what the order called the wreck ("" for the nearest). The same order again
        /// carries on.</summary>
        public void SalvageWrecks(Wreck first, string named = "")
        {
            named = named ?? "";
            if (Current == Order.Salvage && SalvageNamed == named && (first == null || first == SalvageTarget || named.Length == 0))
            {
                passedOver.Clear();
                return;
            }
            // (what it goes back to: its leader, followed or defended, mining, or holding)
            if (Current != Order.Salvage)
            {
                afterSalvage = Current == Order.Follow || Current == Order.Defend || Current == Order.Mine ? Current : Order.Hold;
                afterSalvageLeader = LeaderId;
            }
            if (Current == Order.Salvage) StopSalvageGear();
            LeaveLeader();
            Set(Order.Salvage);
            SalvageTarget = first;
            SalvageNamed = named;
            SalvageFinished = false;
            SalvageCredits = 0f;
            SalvageSales = 0;
            salvagingAt = null;
            passedOver.Clear();
            salvaged0 = Salvager != null ? Salvager.Salvaged : 0f;
            GoSalvage(SalvageStep.Find);
        }

        void GoSalvage(SalvageStep s)
        {
            SalvagingStep = s;
            stepSince = Time.time;
            if (s == SalvageStep.ToMarket) Market = null;
        }

        /// <summary>A fight: the beam off; afterwards it goes back to the same wreck.</summary>
        void SalvagePause()
        {
            StopSalvageGear();
            if (SalvagingStep == SalvageStep.ToWreck || SalvagingStep == SalvageStep.Strip) GoSalvage(SalvageStep.Find);
        }

        void StopSalvageGear()
        {
            var sv = Salvager;
            if (sv != null && sv.Working != null) sv.Stop();
        }

        /// <summary>No wreck left: said, and back to the order before.</summary>
        void NothingLeft()
        {
            StopSalvageGear();
            float took = SalvageTaken;
            Remark?.Invoke(took >= 0.5f ? $"nothing left to salvage; {took:F0} t taken" : "nothing to salvage within 30 km");
            SalvageFinished = true;
            SalvageTarget = null;
            var o = afterSalvage;
            int leader = afterSalvageLeader;
            if ((o == Order.Follow || o == Order.Defend) && leader != 0)
            {
                if (o == Order.Defend) Defend(leader);
                else Follow(leader);
            }
            else if (o == Order.Mine) Mine(MineField, MineChoice);
            else { Hold(); Doing = "holding position: nothing left to salvage"; }
        }

        /// <summary>One frame of the Salvage order.</summary>
        void SalvageUpdate(CarrierController cc)
        {
            var hold = Cargo;
            var sv = Salvager;
            if (hold == null || sv == null) { Doing = "can't salvage: no salvage beam or hold"; Current = Order.Hold; return; }
            Vector3 me = cc.transform.position;
            // the hold full (nearly): off to sell
            if (SalvagingStep <= SalvageStep.Strip && hold.Free < 1f)
            {
                StopSalvageGear();
                GoSalvage(SalvageStep.ToMarket);
            }
            switch (SalvagingStep)
            {
                case SalvageStep.Find:
                {
                    Steer(0f, 0f);
                    cc.throttle = 0f;
                    if (Undocking()) { Doing = "undocking to salvage"; return; }
                    var w = SalvageTarget != null && !SalvageTarget.Empty && !passedOver.Contains(SalvageTarget) ? SalvageTarget
                          : NearestWreck(salvagingAt ?? me, Salvage.Around, passedOver);
                    if (w == null) { NothingLeft(); return; }
                    SalvageTarget = w;
                    GoSalvage(SalvageStep.ToWreck);
                    return;
                }
                case SalvageStep.ToWreck:
                {
                    var w = SalvageTarget;
                    if (w == null || w.Empty) { SalvageTarget = null; GoSalvage(SalvageStep.Find); return; }
                    // (the beam works in combat mode only)
                    if (!Modes.Is(ModeOfPlay.Combat)) Modes.Set(ModeOfPlay.Combat);
                    salvagingAt = w.transform.position;
                    if (sv.InReach(w))
                    {
                        Steer(0f, 0f);
                        cc.throttle = 0f;
                        sv.SalvageWreck(w);
                        if (sv.Working == w) { GoSalvage(SalvageStep.Strip); Doing = $"salvaging {w.Name}"; }
                        else { passedOver.Add(w); SalvageTarget = null; GoSalvage(SalvageStep.Find); }
                        return;
                    }
                    // (far too long about it, or pinned against something on the way: another one)
                    if (Time.time - stepSince > 180f || (Time.time - stepSince > 30f && cc.Speed < 1f && (drive == null || drive.Current == WarpDrive.State.Ready)))
                    {
                        passedOver.Add(w);
                        SalvageTarget = null;
                        GoSalvage(SalvageStep.Find);
                        return;
                    }
                    float d = sv.DistanceTo(w);
                    // far: a warp to it (its own warp point; a refusal, under fire say: flown to instead)
                    var wp = w.GetComponent<WarpPoint>();
                    if (d > WreckWarp && wp != null && wp.enabled && TryWarp(wp, $"warping to {w.Name} to salvage it")) return;
                    if (Undocking()) { Doing = "undocking to salvage"; return; }
                    var v = ShipAutopilot.Travel(cc, w.transform.position, w.transform, out float clear);
                    Steer(v.x, v.y);
                    // (easing off as it comes in, to stop with the wreck in the beam's reach)
                    cc.throttle = Mathf.Clamp((d - sv.range * 0.5f) / 4000f, 0.15f, 0.8f) * clear;
                    Doing = $"flying to {w.Name} ({CombatMode.Distance(d)} off)";
                    return;
                }
                case SalvageStep.Strip:
                {
                    Steer(0f, 0f);
                    cc.throttle = 0f;
                    if (!Modes.Is(ModeOfPlay.Combat)) Modes.Set(ModeOfPlay.Combat);
                    var w = SalvageTarget;
                    // (stripped: the next one)
                    if (w == null || w.Empty) { SalvageTarget = null; GoSalvage(SalvageStep.Find); return; }
                    // (the beam off: drifted out of its reach, say; back in to it)
                    if (sv.Working != w) { GoSalvage(SalvageStep.ToWreck); return; }
                    Doing = $"salvaging {w.Name}: {w.Summary(2)} left; hold {hold.Used:F0} of {hold.capacity:F0} t";
                    return;
                }
                case SalvageStep.ToMarket:
                {
                    StopSalvageGear();
                    // (nothing it can sell aboard: the parts kept, say; back to the wrecks)
                    if (Sellable(hold) < 0.5f)
                    {
                        if (hold.Free < 1f) { Doing = "can't salvage: the hold is full and there's nothing to sell"; Current = Order.Hold; return; }
                        GoSalvage(SalvageStep.Find);
                        return;
                    }
                    switch (TowardMarket(cc, me, $"{hold.Used:F0} t of salvage", "salvage"))
                    {
                        case Trip.Docked: GoSalvage(SalvageStep.Sell); break;
                        case Trip.Docking: GoSalvage(SalvageStep.Docking); break;
                        case Trip.Failed: Current = Order.Hold; break;
                    }
                    return;
                }
                case SalvageStep.Docking:
                {
                    int docked = DockingAtMarket();
                    if (docked > 0) GoSalvage(SalvageStep.Sell);
                    else if (docked < 0) GoSalvage(SalvageStep.ToMarket);
                    return;
                }
                case SalvageStep.Sell:
                {
                    float credits = IndustryPanel.SellCargo(hold, KeepParts(hold), out string what, out _);
                    if (what.Length > 0)
                    {
                        SalvageCredits += credits;
                        SalvageSales++;
                        Sold?.Invoke($"sold {what} at {Market?.displayName ?? "the market"} for {credits:N0} credits");
                    }
                    givenUp.Clear();
                    dockTries = 0;
                    GoSalvage(SalvageStep.Leave);
                    return;
                }
                case SalvageStep.Leave:
                    if (Undocking()) { Doing = $"leaving {Market?.displayName}"; return; }
                    // (back to where it was salvaging: the wreck it was at, or the next near it)
                    GoSalvage(SalvageStep.Find);
                    return;
            }
        }

        /// <summary>The tonnes of parts kept back from a sale: those for a player who asked for
        /// them, while they take under PartsKept of the hold (past that, all are sold, or the hold
        /// would fill with them for ever).</summary>
        float KeepParts(CargoHold hold)
        {
            if (PartsFor == 0 || hold == null) return 0f;
            float parts = PartsTonnes(hold);
            return parts < hold.capacity * PartsKept ? parts : 0f;
        }

        /// <summary>Tonnes of parts aboard.</summary>
        public static float PartsTonnes(CargoHold hold)
        {
            var table = IndustryTable.Get();
            return hold == null || table == null ? 0f : hold.Contents().Where(e => table.Part(e.id) != null).Sum(e => table.Mass(e.id, e.amount));
        }

        /// <summary>Tonnes aboard a sale would sell (all of it, but for the parts kept back).</summary>
        float Sellable(CargoHold hold) => hold == null ? 0f : hold.Used - KeepParts(hold);
    }
}

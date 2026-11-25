using System.Linq;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The wingman's Mine order: it mines asteroids and sells what it cuts, over and over.
    ///   - to an asteroid field (the one asked for, else the one it is at, else the nearest),
    ///     warping there if it isn't at one;
    ///   - scans (V) for deposits, chooses the nearest, flies to it (mining mode's own approach,
    ///     which cuts the engines within 100 m of the rock) and cuts it with the laser until the
    ///     hold is full, going on to the next deposit when one is mined out;
    ///   - with the hold full (or told to sell), to the nearest place that buys ore (a market:
    ///     the Thalassa Highport, Deepcut Station, the Jovara L5 Depot, or a place where people
    ///     live), warping there, docks (flying in to the berth), sells the ore, undocks;
    ///   - and back to the field for more.
    /// A fight close by is fought first, as always; then it carries on.
    /// </summary>
    public partial class Wingman
    {
        public enum MineStep { ToField, Scan, Approach, Cut, ToMarket, Docking, Sell, Leave }

        /// <summary>Where it is in its round of mining and selling.</summary>
        public MineStep Step { get; private set; }
        /// <summary>The field it mines (null: the nearest).</summary>
        public WarpPoint MineField { get; private set; }
        /// <summary>The place it sells at, on the way there.</summary>
        public Installation Market { get; private set; }
        /// <summary>Ore sold, and what it fetched, since the order was given (for the status).</summary>
        public float TonnesSold { get; private set; }
        public float CreditsEarned { get; private set; }
        public int Sales { get; private set; }

        float stepSince, nextScan, nextMineTry, cutIdleSince = -1f;
        // the order a Sell interrupted (back to it once sold; null: it was mining)
        Order? afterSale;
        int afterLeader;
        // rocks it couldn't get to or cut (passed over till it goes to a field again)
        readonly System.Collections.Generic.HashSet<IMineralSource> skipped = new System.Collections.Generic.HashSet<IMineralSource>();

        /// <summary>Mine (at a field by name, or the nearest), and sell when the hold is full.</summary>
        public void Mine(WarpPoint field = null)
        {
            // (the same order again, as when it's given twice: carried on with, not started over,
            // so the run isn't sent back to the field and its sales aren't forgotten; but the rocks
            // it passed over get another look, and a sale under way goes back to mining after)
            if (Current == Order.Mine && MineField == field)
            {
                afterSale = null;
                skipped.Clear();
                nextScan = 0f;
                return;
            }
            LeaveLeader();
            MineField = field;
            Set(Order.Mine);
            afterSale = null;
            TonnesSold = CreditsEarned = 0f;
            Sales = 0;
            Go(MineStep.ToField);
        }

        /// <summary>Take what is aboard to the nearest market now; then back to mining if it was,
        /// else to following its leader (or holding).</summary>
        public void SellNow()
        {
            if (Current != Order.Mine)
            {
                afterSale = Current;
                afterLeader = LeaderId;
                LeaveLeader();
                Set(Order.Mine);
                MineField = null;
            }
            Go(MineStep.ToMarket);
        }

        static bool BuysOre(Installation i) => i != null && (i.market || IndustryPanel.IsMarket(IndustrySite.At(i)));

        void Go(MineStep s)
        {
            Step = s;
            stepSince = Time.time;
            if (s == MineStep.ToMarket) Market = null;
        }

        CargoHold Cargo => Ship != null ? Ship.GetComponentInChildren<CargoHold>() : null;

        /// <summary>The asteroid field it means: the one asked for, the one it is at, or the nearest.</summary>
        WarpPoint FieldFor(Vector3 me)
        {
            if (MineField != null) return MineField;
            var here = Places.At(me);
            if (here != null && here.kind == WarpPoint.Kind.AsteroidField) return here;
            return WarpPoint.All.Where(w => w != null && w.kind == WarpPoint.Kind.AsteroidField)
                .OrderBy(w => (w.Position - me).sqrMagnitude).FirstOrDefault();
        }

        /// <summary>The nearest friendly place that buys ore, and can be docked at.</summary>
        Installation NearestMarket(Vector3 me) =>
            Installation.All.Where(i => i != null && i.isActiveAndEnabled && i.canDock && !i.hostile && !givenUp.Contains(i) && BuysOre(i))
                .OrderBy(i => (i.BerthPosition - me).sqrMagnitude).FirstOrDefault();

        // markets it couldn't get in to dock at (three tries): passed over till it next sells
        readonly System.Collections.Generic.HashSet<Installation> givenUp = new System.Collections.Generic.HashSet<Installation>();
        int dockTries;

        // the rock it was flying to or cutting when a fight broke in: back to it afterwards
        IMineralSource resumeRock;
        float skippedCleared;

        /// <summary>A fight: the laser off and the approach called off; afterwards it goes back
        /// to the same rock (if it's still there), else looks again.</summary>
        void MinePause()
        {
            StopMiningGear();
            if (Step == MineStep.Approach || Step == MineStep.Cut)
            {
                var mm = MiningMode;
                if (mm != null && mm.Chosen != null) resumeRock = mm.Chosen;
                Go(MineStep.Scan);
            }
        }

        /// <summary>Is `p` at the asteroid field (within its reach), whatever world is near it?
        /// (Places.At names the planet or moon first, so a field close to one would never count
        /// as reached, and the bot would warp to it again and again.)</summary>
        public static bool AtField(WarpPoint field, Vector3 p) =>
            field != null && Vector3.Distance(p, field.Position) < Mathf.Max(field.bodyRadius * 1.6f, 80000f);

        void StopMiningGear()
        {
            var mm = MiningMode;
            if (mm != null && mm.Approaching) mm.StopApproach();
            var laser = demo != null ? demo.Laser : null;
            if (laser != null && laser.On) laser.TurnOff();
        }

        MiningMode miningMode;
        MiningMode MiningMode => miningMode != null ? miningMode : (miningMode = FindAnyObjectByType<MiningMode>());

        /// <summary>One frame of the Mine order.</summary>
        void MineUpdate(CarrierController cc)
        {
            var hold = Cargo;
            var laser = demo.Laser;
            var sensor = demo.Sensor;
            var mm = MiningMode;
            if (hold == null || laser == null || sensor == null || mm == null) { Doing = "can't mine: no mining gear"; Current = Order.Hold; return; }
            Vector3 me = cc.transform.position;
            // the hold full (nearly): off to sell
            if (Step <= MineStep.Cut && (hold.Full || hold.Free < 1f))
            {
                StopMiningGear();
                Go(MineStep.ToMarket);
            }
            switch (Step)
            {
                case MineStep.ToField:
                {
                    if (Undocking()) { Doing = "undocking to go mining"; return; }
                    var field = FieldFor(me);
                    if (field == null) { Doing = "can't mine: no asteroid field"; Current = Order.Hold; return; }
                    if (AtField(field, me)) { skipped.Clear(); resumeRock = null; nextScan = 0f; Go(MineStep.Scan); return; }
                    // (a warp refused, under fire say: it tries again in a while, still mining)
                    TryWarp(field, $"warping to {field.displayName} to mine");
                    return;
                }
                case MineStep.Scan:
                {
                    Steer(0f, 0f);
                    cc.throttle = 0f;
                    if (!Modes.Is(ModeOfPlay.Mining)) Modes.Set(ModeOfPlay.Mining);
                    if (sensor.Sweeping) { Doing = "scanning for deposits"; return; }
                    // (the rock a fight took it off, if it's still there; else the nearest the last
                    // scan found here, not what an earlier one found at another field)
                    var found = resumeRock != null && resumeRock.Valid && !resumeRock.Depleted && !skipped.Contains(resumeRock)
                                && laser.Reach(resumeRock) < sensor.spaceRange ? resumeRock
                              : sensor.Finds.Select(f => f.source)
                                    .Where(s => s != null && s.Valid && !s.Depleted && !(s is SurfaceVein) && !skipped.Contains(s) && laser.Reach(s) < sensor.spaceRange)
                                    .OrderBy(s => laser.Reach(s)).FirstOrDefault();
                    resumeRock = null;
                    if (found != null)
                    {
                        mm.Choose(found);
                        if (laser.Reach(found) <= laser.range * 0.9f) { Go(MineStep.Cut); return; }
                        Release();   // (mining mode's approach flies her)
                        mm.StartApproach();
                        Go(MineStep.Approach);
                        Doing = $"flying to {found.Label}";
                        return;
                    }
                    if (Time.time >= nextScan)
                    {
                        nextScan = Time.time + 8f;
                        // (a fresh scan, a minute on: the rocks passed over get another chance, so
                        // it doesn't wander the field with every rock near it skipped)
                        if (Time.time - skippedCleared > 60f) { skipped.Clear(); skippedCleared = Time.time; }
                        if (!sensor.Scan(out string why)) Doing = $"can't scan: {why}";
                        else Doing = "scanning for deposits";
                        return;
                    }
                    // nothing found here: in toward the field's middle, and look again
                    var field = FieldFor(me);
                    if (field != null && Time.time - stepSince > 6f)
                    {
                        var v = ShipAutopilot.Travel(cc, field.Position, null, out float clear);
                        Steer(v.x, v.y);
                        cc.throttle = 0.5f * clear;
                        Doing = $"looking for deposits in {field.displayName}";
                    }
                    return;
                }
                case MineStep.Approach:
                {
                    if (mm.Chosen == null || !mm.Chosen.Valid || mm.Chosen.Depleted) { Go(MineStep.Scan); return; }
                    if (mm.Approaching)
                    {
                        // (pinned against another rock on the way, or far too long about it: another one)
                        if (Time.time - stepSince > 150f || (Time.time - stepSince > 15f && cc.Speed < 1f && laser.Reach(mm.Chosen) > 400f))
                        {
                            mm.StopApproach();
                            skipped.Add(mm.Chosen);
                            Go(MineStep.Scan);
                            return;
                        }
                        Doing = $"flying to {mm.Chosen.Label} ({CombatMode.Distance(laser.Reach(mm.Chosen))} off)";
                        return;
                    }
                    // there (or the approach gave up): cut it if it is in reach, else look again
                    Go(laser.Reach(mm.Chosen) <= laser.range ? MineStep.Cut : MineStep.Scan);
                    return;
                }
                case MineStep.Cut:
                {
                    Steer(0f, 0f);
                    cc.throttle = 0f;
                    if (!Modes.Is(ModeOfPlay.Mining)) Modes.Set(ModeOfPlay.Mining);
                    if (laser.On)
                    {
                        // (on, but with nothing it can cut: the rock mined out, out of reach, or no
                        // clear line to it): off, and the next one
                        if (laser.Cutting) cutIdleSince = -1f;
                        else if (cutIdleSince < 0f) cutIdleSince = Time.time;
                        else if (Time.time - cutIdleSince > 3f)
                        {
                            cutIdleSince = -1f;
                            if (mm.Chosen != null && mm.Chosen.Valid && !mm.Chosen.Depleted) skipped.Add(mm.Chosen);
                            laser.TurnOff();
                            Go(MineStep.Scan);
                            return;
                        }
                        Doing = $"mining {laser.Source?.Label ?? mm.Chosen?.Label ?? "a rock"}: hold {hold.Used:F0} of {hold.capacity:F0} t";
                        return;
                    }
                    // the deposit mined out, or out of reach: the next one
                    if (mm.Chosen == null || !mm.Chosen.Valid || mm.Chosen.Depleted || laser.Reach(mm.Chosen) > laser.range) { Go(MineStep.Scan); return; }
                    if (Time.time < nextMineTry) return;
                    nextMineTry = Time.time + 2f;
                    if (!laser.TurnOn(out string why)) { Doing = $"can't mine: {why}"; Go(MineStep.Scan); }
                    return;
                }
                case MineStep.ToMarket:
                {
                    StopMiningGear();
                    if (hold.OreTonnes < 0.5f)
                    {
                        // (no ore, and no room for any: a hold full of goods it doesn't sell)
                        if (hold.Free < 1f) { Doing = "can't mine: the hold is full of goods, not ore"; Current = Order.Hold; return; }
                        Go(afterSale != null ? MineStep.Leave : MineStep.ToField);
                        return;
                    }
                    var dock = Docking.Instance;
                    // docked already (told to sell there): sell, if it buys ore; else away first
                    if (dock != null && dock.Current == Docking.State.Docking) { Doing = "docking"; return; }
                    if (Docking.Docked)
                    {
                        if (BuysOre(dock.At)) { Market = dock.At; Go(MineStep.Sell); }
                        else { Undocking(); Doing = "undocking to sell the ore at a market"; }
                        return;
                    }
                    if (dock != null && dock.Current == Docking.State.Undocking) return;
                    if (Market == null) Market = NearestMarket(me);
                    if (Market == null) { Doing = "nowhere to sell ore"; Current = Order.Hold; return; }
                    var wp = Market.Point;
                    // in the approach to its berth: dock (flying in); kept off while it can't yet
                    string w = "";
                    var cand = dock != null ? dock.Candidate(out w) : null;
                    if (cand == Market)
                    {
                        if (w != "") { Steer(0f, 0f); cc.throttle = 0f; Doing = $"waiting to dock at {Market.displayName}: {w}"; return; }
                        Release();
                        if (dock.Dock()) { Go(MineStep.Docking); Doing = $"docking at {Market.displayName} to sell"; }
                        return;
                    }
                    if (wp == null) { Doing = $"can't find the way to {Market.displayName}"; Current = Order.Hold; return; }
                    // far: warp there (a refusal: again in a while); close (at the place, but outside its approach): fly in
                    if (Vector3.Distance(me, Market.BerthPosition) > Mathf.Max(20000f, wp.arrivalDistance * 3f))
                    {
                        TryWarp(wp, $"warping to {Market.displayName} to sell {hold.OreTonnes:F0} t of ore");
                        return;
                    }
                    var v = ShipAutopilot.Travel(cc, Market.ApproachPoint(me), null, out float clear);
                    Steer(v.x, v.y);
                    cc.throttle = 0.6f * clear;
                    Doing = $"flying to {Market.displayName} to sell";
                    return;
                }
                case MineStep.Docking:
                {
                    if (Docking.Docked) { Go(MineStep.Sell); return; }
                    var dock = Docking.Instance;
                    var nav = FindAnyObjectByType<NavigationMode>();
                    // (navigation flies her in to the berth, then docking brings her alongside)
                    if ((dock != null && dock.Current != Docking.State.Free) || (nav != null && nav.Travelling)) { Doing = $"docking at {Market?.displayName}"; return; }
                    if (Time.time - stepSince > 5f)
                    {
                        // (it gave up: again, and after three tries another market)
                        if (++dockTries >= 3 && Market != null) { givenUp.Add(Market); dockTries = 0; }
                        Go(MineStep.ToMarket);
                    }
                    return;
                }
                case MineStep.Sell:
                {
                    float credits = hold.SellAll(out float tonnes);
                    if (tonnes > 0f)
                    {
                        TonnesSold += tonnes;
                        CreditsEarned += credits;
                        Sales++;
                        Sold?.Invoke($"sold {tonnes:F0} t of ore at {Market?.displayName ?? "the market"} for {credits:N0} credits");
                    }
                    givenUp.Clear();
                    dockTries = 0;
                    Go(MineStep.Leave);
                    return;
                }
                case MineStep.Leave:
                    if (Undocking()) { Doing = $"leaving {Market?.displayName}"; return; }
                    // (told to sell while doing something else: back to following or defending, or holding)
                    if (afterSale != null)
                    {
                        var o = afterSale.Value;
                        afterSale = null;
                        if (o == Order.Defend && afterLeader != 0) Defend(afterLeader);
                        else if (o == Order.Follow && afterLeader != 0) Follow(afterLeader);
                        else Hold();
                        return;
                    }
                    Go(MineStep.ToField);
                    return;
            }
        }

        /// <summary>Said on the comms when it has sold a load (CoopBot passes it on).</summary>
        public event System.Action<string> Sold;
    }
}

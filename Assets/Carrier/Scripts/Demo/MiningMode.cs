using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarrierGame.Demo
{
    /// <summary>How the mining list orders the deposits found (O turns through them).</summary>
    public enum DepositOrder
    {
        /// <summary>The nearest the laser first.</summary>
        Nearest,
        /// <summary>The most worth first: credits a tonne times the tonnes the rock started with
        /// (not what is left, so the rows don't move while the laser cuts).</summary>
        MostValuable,
        /// <summary>The most credits a tonne first.</summary>
        Richest,
    }

    /// <summary>
    /// The mining mode's menu (Tab): the deposits the scan found (what they are, their best
    /// minerals, the tonnes left, how far, in reach or not), then the ore in the hold. O orders
    /// the deposits (nearest, most valuable or richest first) and F lists only those with one
    /// mineral in them (each mineral found in turn, the most valuable first, then all again); both
    /// are kept between sessions, and T / Shift+T go through the deposits in the same order.
    /// Choosing a deposit makes it the laser's (it cuts that one first, while in reach); C (X on
    /// the pad) flies the ship to it, until it is in reach of the laser; Z on a deposit chooses it
    /// and switches the laser on. On the ore, J (or Enter) ejects that mineral, and in a co-op
    /// game G hands all of it to the AI wingman nearest, within 50 km (CoopCargo).
    /// </summary>
    public class MiningMode : MonoBehaviour, IMenuSource
    {
        public CarrierDemoInput demo;

        /// <summary>The deposit chosen (the laser's first choice).</summary>
        public IMineralSource Chosen { get; private set; }
        /// <summary>Flying to the chosen deposit now.</summary>
        public bool Approaching { get; private set; }

        CarrierController Ship => demo != null ? demo.carrier : null;
        float throttleSet = -1f;
        float approachLog;

        // ------------------------------------------------------------ the menu

        public string Title => "MINING: DEPOSITS AND THE HOLD";

        public string Hint(bool pad) => pad ? "D-pad choose   A choose the deposit / eject the ore   X fly to it   B close"
                                            : "Up/Down choose   Enter choose / eject   C fly there   Z cut   J eject   O order   F mineral   Tab close";

        /// <summary>The most deposits the menu lists, the first in its order (and its filter).</summary>
        public const int MaxListed = 20;

        public void Rows(List<MenuRow> rows)
        {
            var ship = Ship;
            var sensor = demo != null ? demo.Sensor : null;
            var laser = demo != null ? demo.Laser : null;
            var hold = demo != null ? demo.Cargo : null;
            if (ship == null || laser == null || sensor == null) { rows.Add(new MenuRow { header = true, text = "NO MINING GEAR ON THIS SHIP" }); return; }
            var by = Order;
            var only = Filter;
            // (ordered and filtered before the cut, so the first listed are the most valuable of
            // all the finds, not of the nearest)
            var found = Arrange(Finds(sensor), laser.Reach, by, only);
            int total = found.Count, all = only == null ? total : Arrange(Finds(sensor), laser.Reach, DepositOrder.Nearest, null).Count;
            // (the first so many only: a field can give a hundred finds, and the hold's ore is listed below)
            if (found.Count > MaxListed) found = found.Take(MaxListed).ToList();
            if (Chosen != null && Chosen.Valid && !Chosen.Depleted && !found.Contains(Chosen)) found.Insert(0, Chosen);
            string noun = only == null ? "DEPOSITS FOUND" : $"{Minerals.Name(only.Value).ToUpper()} DEPOSITS";
            rows.Add(new MenuRow
            {
                header = true,
                text = all == 0 ? (sensor.Sweeping ? "SCANNING..." : "NO DEPOSITS FOUND: V TO SCAN")
                     : total == 0 ? $"NO {noun} ({all} OTHERS: F)"
                     : total > MaxListed ? $"{noun} ({total}, THE FIRST {MaxListed})" : $"{noun} ({total})",
                // (the order and the filter, with their keys)
                detail = all == 0 ? "" : $"O: {OrderName(by)}   F: {(only == null ? "all" : Minerals.Name(only.Value))}",
            });
            foreach (var s in found)
            {
                float reach = laser.Reach(s);
                bool inReach = reach <= laser.range;
                string mark = s == Chosen ? "<color=#ffc070>> </color>" : "";
                // (ordered by worth: what each is worth, so the order can be seen)
                string worth = by == DepositOrder.MostValuable ? $"{Credits(PerTonne(s.Mix, only) * s.Initial)} cr   "
                             : by == DepositOrder.Richest ? $"{Credits(PerTonne(s.Mix, only))} cr/t   " : "";
                rows.Add(new MenuRow
                {
                    text = $"{mark}{s.Label}  <color=#9aa4b0>{Best(s, only)}</color>",
                    detail = $"{worth}{s.Remaining:F0} t   {CombatMode.Distance(reach)}{(inReach ? "   in reach" : "")}",
                    tag = s,
                });
            }
            if (hold == null) return;
            var ore = hold.Manifest();
            // (an AI wingman near: G hands it the ore chosen)
            float off = -1f;
            var wingman = ore.Count > 0 ? CoopCargo.WingmanNear(ship.transform.position, out off) : null;
            rows.Add(new MenuRow
            {
                header = true,
                text = ore.Count == 0 ? $"THE HOLD: NO ORE ({hold.Used:F0} of {hold.capacity:F0} t)" : $"THE HOLD: ORE ({hold.Used:F0} of {hold.capacity:F0} t)",
                detail = wingman != null ? $"G: give to {wingman.name}, {CoopBot.HowFar(off)} off" : "",
            });
            foreach (var (m, t) in ore)
                rows.Add(new MenuRow
                {
                    text = $"<color={Minerals.Hex(m)}>{Minerals.Name(m)}</color>",
                    detail = $"{t:F1} t   {t * Minerals.Value(m):N0} cr",
                    tag = m,
                });
        }

        /// <summary>Its two most valuable minerals (by their share of its worth); with a filter,
        /// that mineral first.</summary>
        static string Best(IMineralSource s, Mineral? only)
        {
            var mix = s.Mix;
            if (mix == null || mix.Length == 0) return "";
            var best = mix.OrderByDescending(x => only != null && x.mineral == only.Value).ThenByDescending(x => x.fraction * Minerals.Value(x.mineral)).Take(2);
            // (a trace as a trace: 0.3%, not 0%)
            return string.Join(", ", best.Select(x => $"{Minerals.Name(x.mineral)} {Percent(x.fraction)}%"));
        }

        static string Percent(float fraction) => fraction >= 0.095f ? (fraction * 100f).ToString("F0") : (fraction * 100f).ToString("0.0#");

        /// <summary>Credits, short: 950, 12k, 1.4M.</summary>
        static string Credits(float cr) => cr >= 1e6f ? $"{cr / 1e6f:0.#}M" : cr >= 1e4f ? $"{cr / 1e3f:F0}k" : $"{cr:N0}";

        // ------------------------------------------------------------ the list's order and filter

        /// <summary>What an order is called in the list's heading and the messages.</summary>
        public static string OrderName(DepositOrder o) =>
            o == DepositOrder.MostValuable ? "most valuable first" : o == DepositOrder.Richest ? "richest first" : "nearest first";

        /// <summary>Credits a tonne of a mix as a whole, or (`only`) of that one mineral in it.</summary>
        public static float PerTonne(Share[] mix, Mineral? only)
        {
            if (mix == null) return 0f;
            if (only == null) return Minerals.ValuePerTonne(mix);
            foreach (var x in mix) if (x.mineral == only.Value) return x.fraction * Minerals.Value(x.mineral);
            return 0f;
        }

        /// <summary>Is there any of a mineral in it?</summary>
        public static bool Holds(IMineralSource s, Mineral m)
        {
            var mix = s.Mix;
            if (mix == null) return false;
            foreach (var x in mix) if (x.mineral == m && x.fraction > 0f) return true;
            return false;
        }

        /// <summary>The deposits as the list has them: those still there and not mined out (each
        /// once), only those with `only` in them (null: all), in `order`, the nearest by `reach`
        /// breaking ties. With a filter, worth is that mineral's alone (the most platinum, not the
        /// most valuable rock with a little platinum in it). The menu cuts this to MaxListed; T
        /// and Shift+T go through all of it.</summary>
        public static List<IMineralSource> Arrange(IEnumerable<IMineralSource> finds, System.Func<IMineralSource, float> reach, DepositOrder order, Mineral? only)
        {
            var list = finds.Where(s => s != null && s.Valid && !s.Depleted && (only == null || Holds(s, only.Value))).Distinct();
            switch (order)
            {
                case DepositOrder.MostValuable: return list.OrderByDescending(s => PerTonne(s.Mix, only) * s.Initial).ThenBy(reach).ToList();
                case DepositOrder.Richest: return list.OrderByDescending(s => PerTonne(s.Mix, only)).ThenBy(reach).ToList();
                default: return list.OrderBy(reach).ToList();
            }
        }

        /// <summary>The minerals in the deposits (any share of them), the most valuable first:
        /// what F goes through.</summary>
        public static List<Mineral> MineralsIn(IEnumerable<IMineralSource> finds) =>
            finds.Where(s => s != null && s.Valid && !s.Depleted && s.Mix != null)
                 .SelectMany(s => s.Mix).Where(x => x.fraction > 0f).Select(x => x.mineral).Distinct()
                 .OrderByDescending(m => Minerals.Value(m)).ThenBy(m => (int)m).ToList();

        static IEnumerable<IMineralSource> Finds(MineralSensor sensor) => sensor.Finds.Select(f => f.source);

        /// <summary>Where the order and the filter are kept: the player's, or for the tests, reels
        /// and AI players keys of their own (as Captain.PrefKey), which leaves the player's alone;
        /// they always start from the nearest first, all minerals.</summary>
        public static string OrderKey => LaunchArgs.Automated ? "mining.order (automated)" : "mining.order";
        public static string FilterKey => LaunchArgs.Automated ? "mining.filter (automated)" : "mining.filter";

        DepositOrder listOrder;
        Mineral? listFilter;
        bool settingsRead;

        void ReadSettings()
        {
            if (settingsRead) return;
            settingsRead = true;
            if (LaunchArgs.Automated) return;
            try
            {
                int o = PlayerPrefs.GetInt(OrderKey, 0);
                if (System.Enum.IsDefined(typeof(DepositOrder), o)) listOrder = (DepositOrder)o;
                string f = PlayerPrefs.GetString(FilterKey, "");
                if (System.Enum.TryParse(f, out Mineral m) && System.Enum.IsDefined(typeof(Mineral), m)) listFilter = m;
            }
            catch { }
        }

        void SaveSettings()
        {
            try
            {
                PlayerPrefs.SetInt(OrderKey, (int)listOrder);
                PlayerPrefs.SetString(FilterKey, listFilter != null ? listFilter.Value.ToString() : "");
                PlayerPrefs.Save();
            }
            catch { }
        }

        /// <summary>The list's order (O).</summary>
        public DepositOrder Order { get { ReadSettings(); return listOrder; } }
        /// <summary>The list's mineral (F): only the deposits with it in them; null, all of them.</summary>
        public Mineral? Filter { get { ReadSettings(); return listFilter; } }

        /// <summary>O: the next order (nearest, most valuable, richest first, and round again).</summary>
        public void CycleOrder()
        {
            ReadSettings();
            listOrder = (DepositOrder)(((int)listOrder + 1) % 3);
            SaveSettings();
            SoundFX.Ui(Sfx.UiLock);
            Notices.Post($"Deposits listed {OrderName(listOrder)} (O)");
        }

        /// <summary>F: list only the deposits with the next mineral found in them (the most
        /// valuable first), and after the last, all of them again.</summary>
        public void CycleFilter()
        {
            ReadSettings();
            var sensor = demo != null ? demo.Sensor : null;
            var kinds = sensor != null ? MineralsIn(Finds(sensor)) : new List<Mineral>();
            if (kinds.Count == 0 && listFilter == null) { SoundFX.Ui(Sfx.UiError); Notices.Post("No deposits found to choose a mineral from: V scans"); return; }
            // (one not among the finds now, kept from another field or an earlier session: the first)
            int i = listFilter != null ? kinds.IndexOf(listFilter.Value) : -1;
            listFilter = i + 1 < kinds.Count ? kinds[i + 1] : (Mineral?)null;
            SaveSettings();
            SoundFX.Ui(Sfx.UiLock);
            if (listFilter == null) { Notices.Post("All the deposits listed (F)"); return; }
            var laser = demo.Laser;
            int n = laser != null ? Arrange(Finds(sensor), laser.Reach, DepositOrder.Nearest, listFilter).Count : 0;
            Notices.Post($"Only the deposits with {Minerals.Name(listFilter.Value).ToLower()} in them listed: {n} found (F)");
        }

        public void Act(MenuRow row)
        {
            if (row.tag is IMineralSource s) Choose(s);
            else if (row.tag is Mineral m) Eject(m);
        }

        public bool Keys(Keyboard k, Gamepad pad, MenuRow row)
        {
            bool fly = (k != null && k.cKey.wasPressedThisFrame) || (pad != null && !pad.selectButton.isPressed && pad.buttonWest.wasPressedThisFrame);
            if (fly && row?.tag is IMineralSource s) { Choose(s); StartApproach(); return true; }
            if (k != null && k.zKey.wasPressedThisFrame && row?.tag is IMineralSource z)
            {
                var laser = demo.Laser;
                if (laser == null) return false;
                // (in reach: cut it; out of reach, it is chosen, to fly to first)
                Choose(z);
                if (laser.Reach(z) <= laser.range && !laser.On) demo.ToggleMiningLaser();
                return true;
            }
            // (G on the ore: to the AI wingman near, in a co-op game)
            if (k != null && k.gKey.wasPressedThisFrame && row?.tag is Mineral g) { Give(g); return true; }
            if (k != null && k.jKey.wasPressedThisFrame && row?.tag is Mineral m) { Eject(m); return true; }
            // (the list's order and filter: CarrierDemoInput has these keys with the menu shut)
            if (k != null && k.oKey.wasPressedThisFrame) { CycleOrder(); return true; }
            if (k != null && k.fKey.wasPressedThisFrame) { CycleFilter(); return true; }
            return false;
        }

        // ------------------------------------------------------------ choosing, ejecting

        /// <summary>Make a deposit the laser's first choice.</summary>
        public void Choose(IMineralSource s)
        {
            var laser = demo != null ? demo.Laser : null;
            if (laser == null || s == null) return;
            Chosen = s;
            laser.Preferred = s;
            SoundFX.Ui(Sfx.UiLock);
            float reach = laser.Reach(s);
            Notices.Post(reach <= laser.range ? $"{s.Label} chosen: in reach of the laser (Z)"
                                              : $"{s.Label} chosen: {CombatMode.Distance(reach)} off. C flies there");
        }

        /// <summary>Put a whole mineral over the side.</summary>
        public void Eject(Mineral m) => demo?.Jettison(m);

        /// <summary>Hand a whole mineral to the AI wingman nearest (G): CoopCargo says how it went.</summary>
        public void Give(Mineral m)
        {
            var hold = demo != null ? demo.Cargo : null;
            if (hold == null || hold[m] <= 0f) return;
            CoopCargo.GiveToWingman(hold, new List<CoopItem> { new CoopItem { id = m.ToString(), a = hold[m] } });
        }

        // ------------------------------------------------------------ flying to a deposit

        /// <summary>C (X on the pad): fly to the chosen deposit, until it is in reach of the laser.</summary>
        public void ToggleApproach()
        {
            if (Approaching) { StopApproach("Stopped short: you have the helm"); return; }
            StartApproach();
        }

        /// <summary>The deposits found, in the list's filter and (`nearest` false) its order, or
        /// nearest the laser first: what the menu lists, before its cut.</summary>
        List<IMineralSource> Found(bool nearest = false)
        {
            var sensor = demo != null ? demo.Sensor : null;
            var laser = demo != null ? demo.Laser : null;
            if (sensor == null || laser == null) return new List<IMineralSource>();
            return Arrange(Finds(sensor), laser.Reach, nearest ? DepositOrder.Nearest : Order, Filter);
        }

        /// <summary>T (Shift+T back): choose the next deposit found, in the list's order.</summary>
        public void CycleDeposit(int dir)
        {
            var found = Found();
            if (found.Count == 0)
            {
                SoundFX.Ui(Sfx.UiError);
                // (none with the list's mineral in them, but others found)
                var sensor = demo != null ? demo.Sensor : null;
                bool others = Filter != null && sensor != null && MineralsIn(Finds(sensor)).Count > 0;
                Notices.Post(others ? $"No deposits with {Minerals.Name(Filter.Value).ToLower()} in them found: F lists others" : "No deposits found: V scans");
                return;
            }
            int i = Chosen != null ? found.IndexOf(Chosen) : -1;
            i = i < 0 ? (dir > 0 ? 0 : found.Count - 1) : (i + dir + found.Count) % found.Count;
            Choose(found[i]);
        }

        /// <summary>A flight to an asteroid or a comet stops (every engine cut) this close to it (m).</summary>
        public const float StopAt = 100f;
        float approachStarted;

        public void StartApproach()
        {
            var laser = demo != null ? demo.Laser : null;
            if (laser == null) return;
            // (none chosen yet: the nearest found, of those the list has)
            if ((Chosen == null || !Chosen.Valid || Chosen.Depleted) && Found(nearest: true).FirstOrDefault() is IMineralSource first) Choose(first);
            if (Chosen == null || !Chosen.Valid || Chosen.Depleted)
            {
                SoundFX.Ui(Sfx.UiError);
                Notices.Post("Fly to: choose a deposit first (Tab)");
                return;
            }
            if (Chosen is SurfaceVein ? laser.Reach(Chosen) <= laser.range * 0.8f : laser.Reach(Chosen) <= StopAt)
            { Notices.Post($"{Chosen.Label} is in reach already"); return; }
            demo.StopAutopilotsBut(this);
            Approaching = true;
            approachStarted = Time.time;
            approachLog = 0f;
            throttleSet = -1f;
            Notices.Post($"Flying to {Chosen.Label}: steer to take over");
        }

        /// <summary>Stop flying there: the helm (and the throttle as it is) back to the pilot;
        /// `arrived`, the engines are cut too.</summary>
        public void StopApproach(string why = null, bool arrived = false)
        {
            if (!Approaching) return;
            Approaching = false;
            var ship = Ship;
            if (ship != null && arrived) ship.throttle = 0f;
            if (demo != null) { demo.autoRudder = null; demo.autoPitch = null; demo.autoElevator = null; }
            if (why != null) Notices.Post(why);
        }

        void Update()
        {
            var ship = Ship;
            var laser = demo != null ? demo.Laser : null;
            // (the laser's choice kept the same as this one: a ship swapped for another has a
            // laser of its own)
            if (laser != null && Chosen != null && laser.Preferred != Chosen && Chosen.Valid && !Chosen.Depleted) laser.Preferred = Chosen;
            if (!Approaching) return;
            if (ship == null || ship.Destroyed || laser == null || Chosen == null || !Chosen.Valid || Chosen.Depleted) { StopApproach(); return; }
            if (demo.PilotFlying) { StopApproach("Stopped: you have the helm"); return; }
            // (the throttle moved by the pilot: theirs, and the approach is off)
            if (throttleSet >= 0f && Mathf.Abs(ship.throttle - throttleSet) > 0.01f) { StopApproach("Stopped: you have the throttle"); return; }
            if (!Modes.Is(ModeOfPlay.Mining) || demo.ShipHeld) { StopApproach(); return; }
            Vector3 p = Chosen.Position;
            bool vein = Chosen is SurfaceVein;
            float reach = laser.Reach(Chosen);
            Vector3 flat = Vector3.ProjectOnPlane(p - ship.transform.position, ship.upFrame * Vector3.up);
            bool over = vein && flat.magnitude < 300f;
            // (an asteroid's or a comet's: right up to it, every engine cut within StopAt of it, or
            // as close as the hull lets her: up against it, stopped)
            bool against = !vein && reach < 400f && ship.Speed < 1.5f && Time.time > approachStarted + 5f;
            if (vein ? over && reach <= laser.range * 0.8f : reach <= StopAt || against)
            {
                StopApproach($"{Chosen.Label} in reach: Z cuts it", arrived: true);
                return;
            }
            // over a surface: up clear of the dunes and ridges first, then level, toward the vein,
            // and down over it if it is still out of the laser's reach
            var landing = PlanetLanding.Instance;
            bool low = vein && landing != null && landing.Altitude < 250f;
            bool high = vein && flat.magnitude < 600f && reach > laser.range * 0.8f;
            demo.autoElevator = low ? 1f : high ? -1f : vein ? 0f : (float?)null;
            var v = ShipAutopilot.BowOn(ship, vein ? p + ship.upFrame * Vector3.up * Vector3.Dot(ship.transform.position - p, ship.upFrame * Vector3.up) : p);
            demo.autoRudder = v.x;
            demo.autoPitch = vein ? 0f : v.y;
            float to = vein ? flat.magnitude : reach;
            // (easing right off over the last few hundred metres, to stop by the rock, not on it)
            ship.throttle = low || over ? 0.2f : to > 3500f ? 1f : to > 1600f ? 0.6f : to > 600f ? 0.3f
                          : vein ? 0.2f
                          // (the last of the way to a rock: slowing as it closes, to about 2.5 m/s at it, under
                          // the speed a bump would do damage at)
                          : Mathf.Clamp((to - StopAt) * 0.12f, 2.5f, 45f) / Mathf.Max(1f, ship.maxSpeed);
            throttleSet = ship.throttle;
        }

        // ------------------------------------------------------------ on screen

        void OnGUI()
        {
            if (Chosen == null || !Chosen.Valid || Chosen.Depleted || !Modes.Is(ModeOfPlay.Mining)) return;
            if (Hud.ReelMode && !Hud.ReelShowsMarks) return;
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 sp = cam.WorldToScreenPoint(Chosen.Position);
            if (sp.z <= 0f) return;
            float s = Hud.S, box = 20f * s;
            var p = new Vector2(sp.x, Screen.height - sp.y);
            var c = new Color(1f, 0.75f, 0.35f);
            Hud.Corners(new Rect(p.x - box, p.y - box, box * 2f, box * 2f), 6f * s, Mathf.Max(1f, 2f * s), c);
            var laser = demo.Laser;
            string text = $"<color=#ffc070>{Chosen.Label}  {(laser != null ? CombatMode.Distance(laser.Reach(Chosen)) : "")}{(Approaching ? "  flying there" : "")}</color>";
            GUI.Label(new Rect(p.x + box + 4f * s, p.y - 9f * s, 420f * s, 18f * s), text, Hud.Text(12));
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The mining mode's menu (Tab): the deposits the scan found (nearest first: what they are,
    /// their best minerals, the tonnes left, how far, in reach or not), then the ore in the hold.
    /// Choosing a deposit makes it the laser's (it cuts that one first, while in reach); C (X on
    /// the pad) flies the ship to it, until it is in reach of the laser; Z on a deposit chooses it
    /// and switches the laser on. On the ore, J (or Enter) ejects that mineral.
    /// </summary>
    public class MiningMode : MonoBehaviour, IMenuSource
    {
        public CarrierDemoInput demo;

        /// <summary>The deposit chosen (the laser's first choice).</summary>
        public IMineralSource Chosen { get; private set; }
        /// <summary>Flying to the chosen deposit now.</summary>
        public bool Approaching { get; private set; }

        CarrierController Ship => demo != null ? demo.carrier : null;
        float approachLog;

        // ------------------------------------------------------------ the menu

        public string Title => "MINING: DEPOSITS AND THE HOLD";

        public string Hint(bool pad) => pad ? "D-pad choose   A choose the deposit / eject the ore   X fly to it   B close"
                                            : "Up/Down choose   Enter choose / eject   C fly to it   Z cut it   J eject   Tab close";

        /// <summary>The most deposits the menu lists, the nearest first.</summary>
        public const int MaxListed = 20;

        public void Rows(List<MenuRow> rows)
        {
            var ship = Ship;
            var sensor = demo != null ? demo.Sensor : null;
            var laser = demo != null ? demo.Laser : null;
            var hold = demo != null ? demo.Cargo : null;
            if (ship == null || laser == null || sensor == null) { rows.Add(new MenuRow { header = true, text = "NO MINING GEAR ON THIS SHIP" }); return; }
            var found = sensor.Finds.Select(f => f.source).Where(s => s != null && s.Valid && !s.Depleted).Distinct()
                              .OrderBy(s => laser.Reach(s)).ToList();
            // (the nearest only: a field can give a hundred finds, and the hold's ore is listed below)
            int total = found.Count;
            if (found.Count > MaxListed) found = found.Take(MaxListed).ToList();
            if (Chosen != null && Chosen.Valid && !Chosen.Depleted && !found.Contains(Chosen)) found.Insert(0, Chosen);
            rows.Add(new MenuRow { header = true, text = total == 0 ? (sensor.Sweeping ? "SCANNING..." : "NO DEPOSITS FOUND: V TO SCAN")
                                                     : total > MaxListed ? $"DEPOSITS FOUND ({total}, THE NEAREST {MaxListed})" : $"DEPOSITS FOUND ({total})" });
            foreach (var s in found)
            {
                float reach = laser.Reach(s);
                bool inReach = reach <= laser.range;
                string mark = s == Chosen ? "<color=#ffc070>> </color>" : "";
                rows.Add(new MenuRow
                {
                    text = $"{mark}{s.Label}  <color=#9aa4b0>{Best(s)}</color>",
                    detail = $"{s.Remaining:F0} t   {CombatMode.Distance(reach)}{(inReach ? "   in reach" : "")}",
                    tag = s,
                });
            }
            if (hold == null) return;
            var ore = hold.Manifest();
            rows.Add(new MenuRow { header = true, text = ore.Count == 0 ? $"THE HOLD: NO ORE ({hold.Used:F0} of {hold.capacity:F0} t)" : $"THE HOLD: ORE ({hold.Used:F0} of {hold.capacity:F0} t)" });
            foreach (var (m, t) in ore)
                rows.Add(new MenuRow
                {
                    text = $"<color={Minerals.Hex(m)}>{Minerals.Name(m)}</color>",
                    detail = $"{t:F1} t   {t * Minerals.Value(m):N0} cr",
                    tag = m,
                });
        }

        static string Best(IMineralSource s)
        {
            var mix = s.Mix;
            if (mix == null || mix.Length == 0) return "";
            return string.Join(", ", mix.OrderByDescending(x => x.fraction * Minerals.Value(x.mineral)).Take(2)
                                        .Select(x => $"{Minerals.Name(x.mineral)} {x.fraction * 100f:F0}%"));
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
                Choose(z);
                var laser = demo.Laser;
                if (laser != null && !laser.On) demo.ToggleMiningLaser();
                return true;
            }
            if (k != null && k.jKey.wasPressedThisFrame && row?.tag is Mineral m) { Eject(m); return true; }
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
        public void Eject(Mineral m)
        {
            var hold = demo != null ? demo.Cargo : null;
            if (hold == null || hold[m] <= 0f) return;
            float t = hold.Jettison(m);
            SoundFX.Ui(Sfx.UiClear);
            Notices.Post($"Ejected {t:F1} t of {Minerals.Name(m).ToLower()}: {hold.Used:F0} of {hold.capacity:F0} t aboard");
        }

        // ------------------------------------------------------------ flying to a deposit

        /// <summary>C (X on the pad): fly to the chosen deposit, until it is in reach of the laser.</summary>
        public void ToggleApproach()
        {
            if (Approaching) { StopApproach("Stopped short: you have the helm"); return; }
            StartApproach();
        }

        public void StartApproach()
        {
            var laser = demo != null ? demo.Laser : null;
            if (laser == null) return;
            if (Chosen == null || !Chosen.Valid || Chosen.Depleted)
            {
                SoundFX.Ui(Sfx.UiError);
                Notices.Post("Fly to: choose a deposit first (Tab)");
                return;
            }
            if (laser.Reach(Chosen) <= laser.range * 0.8f) { Notices.Post($"{Chosen.Label} is in reach already"); return; }
            Approaching = true;
            approachLog = 0f;
            Notices.Post($"Flying to {Chosen.Label}: steer to take over");
        }

        public void StopApproach(string why = null)
        {
            if (!Approaching) return;
            Approaching = false;
            var ship = Ship;
            if (ship != null) ship.throttle = 0f;
            if (demo != null) { demo.autoRudder = null; demo.autoPitch = null; demo.autoElevator = null; }
            if (why != null) Notices.Post(why);
        }

        void Update()
        {
            if (!Approaching) return;
            var ship = Ship;
            var laser = demo != null ? demo.Laser : null;
            if (ship == null || ship.Destroyed || laser == null || Chosen == null || !Chosen.Valid || Chosen.Depleted) { StopApproach(); return; }
            if (demo.PilotFlying) { StopApproach("Stopped: you have the helm"); return; }
            if (!Modes.Is(ModeOfPlay.Mining)) { StopApproach(); return; }
            Vector3 p = Chosen.Position;
            bool vein = Chosen is SurfaceVein;
            float reach = laser.Reach(Chosen);
            Vector3 flat = Vector3.ProjectOnPlane(p - ship.transform.position, ship.upFrame * Vector3.up);
            if (vein ? flat.magnitude < 300f : reach < laser.range * 0.6f)
            {
                StopApproach($"{Chosen.Label} in reach: Z cuts it");
                return;
            }
            // over a surface: up clear of the dunes and ridges first, then level, toward the vein
            var landing = PlanetLanding.Instance;
            bool low = vein && landing != null && landing.Altitude < 250f;
            demo.autoElevator = low ? 1f : vein ? 0f : (float?)null;
            var v = ShipAutopilot.BowOn(ship, vein ? p + ship.upFrame * Vector3.up * Vector3.Dot(ship.transform.position - p, ship.upFrame * Vector3.up) : p);
            demo.autoRudder = v.x;
            demo.autoPitch = vein ? 0f : v.y;
            float to = vein ? flat.magnitude : reach;
            ship.throttle = low ? 0.2f : to > 3500f ? 1f : to > 1600f ? 0.6f : 0.3f;
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

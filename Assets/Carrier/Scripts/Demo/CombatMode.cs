using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The combat mode's menu (Tab): the targets in range, nearest first, with what they are, how
    /// far and how hurt; choosing one targets it. And lining up on the target (C, A on the pad):
    /// the ship turns until as many of its guns bear on the target as can (the bow onto it while
    /// the main gun charges), and keeps them bearing as it moves, until the pilot steers, the
    /// target is gone, or C again.
    /// </summary>
    public class CombatMode : MonoBehaviour, IMenuSource
    {
        public CarrierDemoInput demo;

        CarrierController Ship => demo != null ? demo.carrier : null;

        /// <summary>Lining up on the target now.</summary>
        public bool LiningUp { get; private set; }
        /// <summary>The guns that bear on the target now (of those it has), as of the last look.</summary>
        public int Bearing { get; private set; }
        public int Guns { get; private set; }

        bool saidLinedUp;
        float nextLook;
        float wantHeading;     // the heading wanted (degrees), as of the last look

        // ------------------------------------------------------------ the menu

        public string Title => "COMBAT: TARGETS AND WRECKS";

        public string Hint(bool pad) => pad ? "D-pad choose   A target it / choose the wreck   X line up on it   B close"
                                            : "Up/Down choose   Enter target it / choose the wreck   C line up   Z salvage   Tab close";

        public void Rows(List<MenuRow> rows)
        {
            var ship = Ship;
            if (ship == null) return;
            Vector3 me = ship.transform.position;
            var found = Targets.All.Where(t => t != null && !CombatPilot.IsHidden(t) && TargetDisplay.InRange(t, me))
                .Where(t => !t.TryGetComponent(out Health h) || !h.Dead)
                .OrderBy(t => (t.position - me).sqrMagnitude).ToList();
            rows.Add(new MenuRow { header = true, text = found.Count == 0 ? "NO TARGETS IN RANGE" : $"TARGETS ({found.Count})" });
            foreach (var t in found)
            {
                string name = Wingman.NameOf(t), cls = t.TryGetComponent(out TargetInfo info) ? info.shipClass : "";
                float d = Vector3.Distance(me, t.position);
                string hull = t.TryGetComponent(out Health h) && h.maxHealth > 0f ? $"hull {Mathf.CeilToInt(100f * h.Current / h.maxHealth)}%" : "";
                string mark = t == ship.target ? "<color=#ff8a80>> </color>" : "";
                rows.Add(new MenuRow
                {
                    text = $"{mark}{name}{(cls.Length > 0 && cls != name ? $"  <color=#9aa4b0>{cls}</color>" : "")}",
                    detail = $"{Distance(d)}   {hull}",
                    tag = t,
                });
            }
            // wrecks to salvage, within 30 km
            var salvage = Salvage.Instance;
            var wrecks = salvage != null ? salvage.Near(30000f).ToList() : new List<Wreck>();
            if (wrecks.Count > 0)
            {
                rows.Add(new MenuRow { header = true, text = $"WRECKS ({wrecks.Count})" });
                foreach (var w in wrecks)
                {
                    string mark = w == salvage.Chosen || w == salvage.Working ? "<color=#ffc070>> </color>" : "";
                    rows.Add(new MenuRow
                    {
                        text = $"{mark}{w.Name}  <color=#9aa4b0>{w.Summary(2)}</color>",
                        detail = $"{Distance(salvage.DistanceTo(w))}   {w.Tonnes:F0} t{(salvage.InReach(w) ? "   in reach" : "")}",
                        tag = w,
                    });
                }
            }
        }

        public static string Distance(float m) => m >= 10000f ? $"{m / 1000f:F0} km" : m >= 1000f ? $"{m / 1000f:F1} km" : $"{m:F0} m";

        public void Act(MenuRow row)
        {
            if (row.tag is Transform t && t != null) demo.SelectTarget(t);
            else if (row.tag is Wreck w && w != null) Salvage.Instance?.Choose(w);
        }

        public bool Keys(Keyboard k, Gamepad pad, MenuRow row)
        {
            // Z on a wreck: choose it and salvage it (if in reach)
            if (k != null && k.zKey.wasPressedThisFrame && row?.tag is Wreck w && w != null)
            {
                var salvage = Salvage.Instance;
                if (salvage == null) return false;
                salvage.Choose(w);
                if (salvage.Working == null) salvage.Toggle();
                return true;
            }
            bool lineUp = (k != null && k.cKey.wasPressedThisFrame) || (pad != null && !pad.selectButton.isPressed && pad.buttonWest.wasPressedThisFrame);
            if (!lineUp || !(row?.tag is Transform t) || t == null) return false;
            demo.SelectTarget(t);
            StartLineUp();
            return true;
        }

        // ------------------------------------------------------------ lining up

        public void ToggleLineUp()
        {
            if (LiningUp) { StopLineUp("Line-up off"); return; }
            StartLineUp();
        }

        public void StartLineUp()
        {
            var ship = Ship;
            if (ship == null || ship.Destroyed) return;
            if (ship.target == null) { SoundFX.Ui(Sfx.UiError); Notices.Post(PadSupport.UsingPad ? "Line up: pick a target first (Y)" : "Line up: pick a target first (T, or click a ship)"); return; }
            LiningUp = true;
            saidLinedUp = false;
            nextLook = 0f;
            SoundFX.Ui(Sfx.UiLock);
            Notices.Post($"Lining up on {Wingman.NameOf(ship.target)}: steer to take over");
        }

        public void StopLineUp(string why = null)
        {
            if (!LiningUp) return;
            LiningUp = false;
            if (demo != null) { demo.autoRudder = null; demo.autoPitch = null; }
            if (why != null) Notices.Post(why);
        }

        void Update()
        {
            var ship = Ship;
            if (!LiningUp) return;
            if (ship == null || ship.Destroyed) { StopLineUp(); return; }
            if (ship.target == null || (ship.target.TryGetComponent(out Health th) && th.Dead)) { StopLineUp("Line-up off: no target"); return; }
            if (demo.PilotFlying) { StopLineUp("Line-up off: you have the helm"); return; }
            if (!Modes.Is(ModeOfPlay.Combat)) { StopLineUp(); return; }
            // (not through a warp, a landing or a climb, nor while the comms line is typed into:
            // the ship isn't the pilot's to take back then)
            if ((demo.Warp != null && demo.Warp.Current != WarpDrive.State.Ready) || PlanetLanding.Busy || Docking.Busy
                || InputFocus.Has(InputFocus.Panel.Chat) || SystemMap.IsOpen || IndustryPanel.IsOpen) { StopLineUp(); return; }
            Vector3 target = ship.target.position;
            var gun = demo.MainGun;
            bool bow = gun != null && gun.Current != MainBeam.State.Idle;
            if (bow)
            {
                Vector2 v = ShipAutopilot.BowOn(ship, target);
                demo.autoRudder = v.x;
                demo.autoPitch = v.y;
                return;
            }
            if (Time.time >= nextLook)
            {
                nextLook = Time.time + 0.25f;
                // (a heading to steer for, rather than a turn: the turn is worked out again every
                // frame from it, so the ship settles on it instead of overshooting)
                wantHeading = ship.Heading + BestTurn(ship, target, out int bearing, out int guns);
                Bearing = bearing;
                Guns = guns;
            }
            float turn = Mathf.DeltaAngle(ship.Heading, wantHeading);
            if (!saidLinedUp && Guns > 0 && Bearing > 0 && Mathf.Abs(turn) < 6f)
            {
                saidLinedUp = true;
                Notices.Post($"Lined up: {Bearing} of {Guns} guns bear on {Wingman.NameOf(ship.target)}");
            }
            if (Bearing == 0)
            {
                // none can bear from any heading (out of their reach): the bow goes after it
                Vector2 v = ShipAutopilot.BowOn(ship, target);
                demo.autoRudder = v.x;
                demo.autoPitch = v.y;
                return;
            }
            demo.autoRudder = Mathf.Clamp(turn / 15f, -1f, 1f);
            demo.autoPitch = Mathf.Clamp(-ship.Pitch / 5f, -1f, 1f);
        }

        /// <summary>The turn (degrees off the heading, about the ship's up) that brings the most of
        /// its guns to bear on `target` (the smallest turn of those that bring as many), and how
        /// many bear after it. Each heading is tried by turning the target round the ship instead.</summary>
        public static float BestTurn(CarrierController ship, Vector3 target, out int bearing, out int guns)
        {
            var turrets = ship.mainTurrets.Where(t => t != null).ToList();
            guns = turrets.Count;
            bearing = 0;
            Vector3 me = ship.transform.position, up = ship.upFrame * Vector3.up, to = target - me;
            if (guns == 0) { Vector3 f = Vector3.ProjectOnPlane(to, up); return Vector3.SignedAngle(Vector3.ProjectOnPlane(ship.transform.forward, up), f, up); }
            float best = 0f;
            for (int step = 0; step <= 36; step++)
            {
                // 0, +10, -10, +20, -20 ... (the smallest turns first)
                float turn = (step + 1) / 2 * 10f * (step % 2 == 1 ? 1f : -1f);
                if (turn > 180f || turn < -180f) continue;
                Vector3 seen = me + Quaternion.AngleAxis(-turn, up) * to;
                int n = 0;
                foreach (var t in turrets) if (t.CanBear(seen)) n++;
                if (n > bearing) { bearing = n; best = turn; }
                if (bearing == guns) break;
            }
            return best;
        }
    }
}

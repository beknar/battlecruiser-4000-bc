using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The navigation mode. The system map (M) warps to places: the star, the planets and moons,
    /// the asteroid fields, the comets, the Lagrange points and what is built out on its own.
    /// At a place, the menu (Tab) lists its points of interest (Places): its bases, the stations
    /// in orbit, the bases in an asteroid field, nearest first, with how far each is and how long
    /// the trip takes. Then:
    ///   Enter (A): auto-travel there. The ship flies itself, in real time (no warp, no fade):
    ///              round a planet at a height, in cruise, coming down to where a warp would drop
    ///              it; straight across a field.
    ///   K (X):     auto-travel there and land on its pad (the auto-lander, flown down), or dock.
    ///   C:         just set the course: the place is marked on screen, to fly to by hand.
    /// X (B on the pad), or taking the helm, stops the auto-travel; the course stays marked.
    /// </summary>
    public class NavigationMode : MonoBehaviour, IMenuSource, IMenuStart
    {
        public CarrierDemoInput demo;

        public enum Then { Arrive, Land, Dock }

        /// <summary>The point of interest the course is set for (marked on screen).</summary>
        public WarpPoint Course { get; private set; }
        /// <summary>Flying itself to the course now.</summary>
        public bool Travelling { get; private set; }
        public Then Finish { get; private set; }
        /// <summary>What the auto-travel is doing, in a few words (for the HUD and the self-test).</summary>
        public string Phase { get; private set; } = "";
        /// <summary>Trips finished, and how long the last took (for the self-test).</summary>
        public int Arrivals { get; private set; }
        public float LastTrip { get; private set; }

        CarrierController Ship => demo != null ? demo.carrier : null;
        WarpDrive Drive => demo != null ? demo.Warp : null;
        float started, nextNotice;
        bool finishing;
        float throttleSet = -1f;    // the throttle as auto-travel last set it (-1: not set)
        // the place the ship is at (looked at twice a second), and where the course's marker
        // goes (once a frame: finding a clear arrival point is not cheap)
        WarpPoint here;
        float hereAt;
        Vector3 markAt;
        float markDist;

        /// <summary>Where auto-travel is flying the ship now (null when it isn't): K docks at
        /// that station anywhere in its approach, even nearer the middle of the field it is in.</summary>
        public static WarpPoint Flying { get; private set; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetFlying() => Flying = null;
        void OnDisable() { Flying = null; }

        // ------------------------------------------------------------ the menu

        public string Title
        {
            get
            {
                return here != null ? $"NAVIGATION: AT {here.displayName.ToUpperInvariant()}" : "NAVIGATION: OPEN SPACE";
            }
        }

        public string Hint(bool pad) => pad ? "D-pad choose   A fly there   X fly there and land or dock   B close"
                                            : "Up/Down choose   Enter fly there   K and land or dock   C set the course   Tab close";

        public void Rows(List<MenuRow> rows)
        {
            var ship = Ship;
            if (ship == null) return;
            Vector3 me = ship.transform.position;
            var place = Places.At(me);
            var pois = place != null ? Places.PointsOfInterest(place, me) : new List<WarpPoint>();
            if (place == null) { rows.Add(new MenuRow { header = true, text = "NO PLACE NEAR: M FOR THE SYSTEM MAP" }); AllyRows(rows, me); return; }
            // (a place that is itself somewhere to dock, out on its own: the Lantern Arcology, Kestrel Deep Yard)
            if (place.Built && DockAt(place) != null && !pois.Contains(place)) pois.Insert(0, place);
            rows.Add(new MenuRow { header = true, text = pois.Count == 0 ? $"NOTHING BUILT AT {place.displayName.ToUpperInvariant()}: M FOR THE MAP"
                                                                         : $"POINTS OF INTEREST AT {place.displayName.ToUpperInvariant()} ({pois.Count})" });
            foreach (var p in pois)
            {
                float d = Vector3.Distance(me, Destination(p, me));
                string mark = p == Course ? "<color=#7ee08a>> </color>" : "";
                string how = CanLandAt(p) ? "pad" : DockAt(p) != null ? "dock" : "";
                rows.Add(new MenuRow
                {
                    text = $"{mark}{p.displayName}  <color=#9aa4b0>{Kind(p)}{(p.hostile ? "  <color=#ff6050>hostile</color>" : "")}</color>",
                    detail = $"{CombatMode.Distance(d)}   {Eta(d)}{(how.Length > 0 ? "   " + how : "")}",
                    tag = p,
                });
            }
            AllyRows(rows, me);
        }

        /// <summary>The other players and AI wingmen (co-op), to mini-warp to (Enter).</summary>
        void AllyRows(List<MenuRow> rows, Vector3 me)
        {
            var map = Map;
            if (map == null) return;
            map.RefreshAllies();
            var allies = map.Allies.Where(a => a.ship != null && a.kind != "fleet").ToList();
            if (allies.Count == 0) return;
            rows.Add(new MenuRow { header = true, text = $"ALLIES ({allies.Count}): ENTER WARPS TO THEM" });
            foreach (var a in allies.OrderBy(a => (a.ship.position - me).sqrMagnitude))
                rows.Add(new MenuRow
                {
                    text = $"<color=#{ColorUtility.ToHtmlStringRGB(a.color)}>{a.name}</color>  <color=#9aa4b0>{a.kind}</color>",
                    detail = a.noWarp ?? CombatMode.Distance(Vector3.Distance(me, a.ship.position)),
                    enabled = a.noWarp == null,
                    tag = a.ship,
                });
        }

        static string Kind(WarpPoint p)
        {
            switch (p.kind)
            {
                case WarpPoint.Kind.SurfaceBase: return "on the surface";
                case WarpPoint.Kind.Station: return "station";
                case WarpPoint.Kind.Installation: return "installation";
                case WarpPoint.Kind.Weapons: return "weapons";
                case WarpPoint.Kind.AsteroidBase: return "asteroid base";
                case WarpPoint.Kind.Landmark: return string.IsNullOrEmpty(p.kindLabel) ? "landmark" : p.kindLabel;
                default: return p.kind.ToString().ToLowerInvariant();
            }
        }

        /// <summary>A rough time for the trip, in cruise (the higher, the faster).</summary>
        static string Eta(float m)
        {
            if (m < 20000f) return m < 3000f ? "here" : "under a minute";
            float s = 25f + m / 7000f;
            return s < 90f ? $"~{s:F0} s" : $"~{s / 60f:F0} min";
        }

        public void Act(MenuRow row)
        {
            if (row.tag is WarpPoint p) TravelTo(p, Then.Arrive);
            // an ally: a mini-warp to it
            else if (row.tag is Transform ship && Map != null)
            {
                Map.RefreshAllies();
                Map.SelectAlly(ship);
                if (Map.WarpToAlly()) ModeMenu.Close();
            }
        }

        SystemMap map;
        SystemMap Map => map != null ? map : (map = FindAnyObjectByType<SystemMap>());

        /// <summary>The menu opens on the course set, if any (so Tab, Enter flies it).</summary>
        public object StartAt => Course;

        public bool Keys(Keyboard k, Gamepad pad, MenuRow row)
        {
            if (!(row?.tag is WarpPoint p)) return false;
            bool land = (k != null && k.kKey.wasPressedThisFrame) || (pad != null && !pad.selectButton.isPressed && pad.buttonWest.wasPressedThisFrame);
            if (land) { TravelTo(p, FinishFor(p)); return true; }
            if (k != null && k.cKey.wasPressedThisFrame) { SetCourse(p); return true; }
            return false;
        }

        // ------------------------------------------------------------ where, and how to finish

        /// <summary>Where the trip to a point of interest ends: where a warp to it would drop the ship.</summary>
        static Vector3 Destination(WarpPoint p, Vector3 from) => p.ArrivalPoint(from);

        /// <summary>How a trip there ends when asked to land or dock (K, X on the pad, LB):
        /// down on its pad, at its berth, or just there.</summary>
        public static Then FinishFor(WarpPoint p) => CanLandAt(p) ? Then.Land : DockAt(p) != null ? Then.Dock : Then.Arrive;

        static bool CanLandAt(WarpPoint p) =>
            (p.kind == WarpPoint.Kind.SurfaceBase && p.LandingBody != null && p.LandingBody.surface != SurfaceKind.None)
            // (a landmark on a surface: down there, by the auto-lander)
            || (p.kind == WarpPoint.Kind.Landmark && p.overhead && p.follow != null && p.follow.surface != SurfaceKind.None);

        static Installation DockAt(WarpPoint p) =>
            Installation.All.FirstOrDefault(i => i != null && i.canDock && !i.hostile && i.displayName == p.displayName);

        // ------------------------------------------------------------ the course, and flying it

        /// <summary>Mark a point of interest on screen, to fly to by hand.</summary>
        public void SetCourse(WarpPoint p)
        {
            Course = p;
            SoundFX.Ui(Sfx.UiLock);
            Notices.Post(PadSupport.UsingPad ? $"Course set for {p.displayName}: marked on screen. A flies there"
                                             : $"Course set for {p.displayName}: marked on screen. Tab, then Enter, flies there");
        }

        /// <summary>Fly to a point of interest, and land or dock there if asked.</summary>
        public bool TravelTo(WarpPoint p, Then then)
        {
            var ship = Ship;
            if (ship == null || p == null) return false;
            string why = ship.Destroyed ? "the ship is destroyed"
                       : Drive != null && Drive.Current != WarpDrive.State.Ready ? "in warp"
                       : PlanetLanding.Busy ? "take off first (K)"
                       : PlanetLanding.Instance != null && PlanetLanding.Instance.Autopilot ? "the landing auto-pilot is flying her (K stops it)"
                       : Docking.Busy ? "undock first (K)" : null;
            // (only to the points of interest at the place the ship is: the map warps to others)
            var place = Places.PlaceOf(p) ?? p;
            if (why == null && place != Places.At(ship.transform.position)) why = $"warp to {place.displayName} first (M)";
            if (why != null) { SoundFX.Ui(Sfx.UiError); Notices.Post($"Can't fly to {p.displayName}: {why}"); return false; }
            demo.StopAutopilotsBut(this);
            Course = p;
            here = place;
            Finish = then;
            Travelling = true;
            finishing = false;
            throttleSet = -1f;
            started = Time.time;
            nextNotice = 0f;
            Phase = "setting off";
            ModeMenu.Close();
            SoundFX.Ui(Sfx.UiLock);
            Notices.Post($"Flying to {p.displayName}{(then == Then.Land ? ", to land" : then == Then.Dock ? ", to dock" : "")}: " +
                         $"{CombatMode.Distance(Vector3.Distance(ship.transform.position, Destination(p, ship.transform.position)))}. X or the helm stops it");
            return true;
        }

        public void Stop(string why = null)
        {
            if (!Travelling) return;
            // (the auto-lander, if it is taking her down for this trip, is called off too)
            if (finishing && Finish == Then.Land && PlanetLanding.Instance != null) PlanetLanding.Instance.CancelAutoLand();
            End(why);
        }

        void End(string why)
        {
            Travelling = false;
            throttleSet = -1f;
            finishing = false;
            Phase = "";
            var pf = PlanetFlight.Instance;
            if (pf != null) pf.autoCruise = false;
            if (demo != null) { demo.autoRudder = null; demo.autoPitch = null; demo.autoElevator = null; }
            var ship = Ship;
            if (ship != null) ship.throttle = Mathf.Min(ship.throttle, 0.3f);
            if (why != null) Notices.Post(why);
        }

        void Update()
        {
            var ship = Ship;
            if (ship != null && Time.time >= hereAt)
            {
                hereAt = Time.time + 0.5f;
                here = Places.At(ship.transform.position);
                // a course for somewhere else (the ship has warped away): forgotten
                if (Course != null && !Travelling && (Places.PlaceOf(Course) ?? Course) != here) Course = null;
            }
            if (Course != null && ship != null)
            {
                // (a station: its berth, which is what the distance means to a pilot docking)
                var berthOf = DockAt(Course);
                markAt = berthOf != null ? berthOf.BerthPosition : Destination(Course, ship.transform.position);
                markDist = Vector3.Distance(ship.transform.position, markAt);
            }
            Flying = Travelling ? Course : null;
            if (!Travelling) return;
            if (ship == null || ship.Destroyed || Course == null) { Stop(); return; }
            // landing or docking: done once down, or docked (before anything else: a site's
            // screen opening there changes the mode)
            if (finishing && (PlanetLanding.OnSurface || Docking.Docked)) { Arrived(); return; }
            if (finishing && Docking.Busy)
            {
                throttleSet = -1f;
                demo.autoRudder = null; demo.autoPitch = null;
                Phase = "docking";
                return;
            }
            // landed, or docked, some other way (K on the way): the trip is over, not flown on
            // over the surface or carried on after take-off
            if (!finishing && (PlanetLanding.Busy || Docking.Busy || (PlanetLanding.Instance != null && PlanetLanding.Instance.Autopilot)))
            {
                Stop();
                return;
            }
            if (demo.PilotFlying) { Stop($"Auto-travel off: you have the helm. {Course.displayName} stays marked"); return; }
            // (the throttle moved by the pilot: theirs, and auto-travel is off)
            if (throttleSet >= 0f && Mathf.Abs(ship.throttle - throttleSet) > 0.01f) { Stop($"Auto-travel off: you have the throttle. {Course.displayName} stays marked"); return; }
            // (another mode chosen: its keys and autopilots are the player's now)
            if (!Modes.Is(ModeOfPlay.Navigation)) { Stop($"Auto-travel off. {Course.displayName} stays marked"); return; }
            if (Drive != null && Drive.Current != WarpDrive.State.Ready) { Stop(); return; }
            if (finishing)
            {
                if (Finish == Then.Dock || Finish == Then.Arrive) FlyDock(ship);
                // (the auto-lander flying her down from orbit is still "in space" till the ground
                // takes over: it is the lander being off that means it gave up)
                else if (!PlanetLanding.Busy && !(PlanetLanding.Instance != null && PlanetLanding.Instance.Autopilot) && Time.time > nextNotice)
                    Stop("The auto-lander couldn't take her down here");
                return;
            }
            Vector3 me = ship.transform.position;
            Vector3 dest = Destination(Course, me);
            float dist = Vector3.Distance(me, dest);
            if (dist < 2500f) { Reached(ship); return; }
            Vector3 carrot = dest;
            var body = Course.LandingBody != Course ? Course.LandingBody : Places.PlaceOf(Course);
            if (body != null && (body.kind == WarpPoint.Kind.Planet || body.kind == WarpPoint.Kind.Moon))
                carrot = RoundTheBody(body, me, dest, out dist);
            var v = ShipAutopilot.BowOn(ship, carrot);
            demo.autoRudder = v.x;
            demo.autoPitch = v.y;
            // in cruise all the way (it slows by itself near a station, and lower down), the
            // throttle easing off near the end: no faster than would cover what is left in a few
            // seconds, so she slows smoothly from cruise and doesn't overshoot
            var pf = PlanetFlight.Instance;
            if (pf != null) pf.autoCruise = true;
            float top = pf != null && pf.TopSpeed > 0f ? pf.TopSpeed : ship.maxSpeed;
            float want = Mathf.Max(dist / 6f, ship.maxSpeed * 0.4f);
            ship.throttle = Mathf.Clamp(want / Mathf.Max(1f, top), 0.02f, 1f);
            throttleSet = ship.throttle;
            bool far = pf != null && pf.Cruising && ship.Speed > ship.maxSpeed * 1.5f;
            Phase = far ? $"cruising, {CombatMode.Distance(dist)} to go" : $"coming in, {CombatMode.Distance(dist)} to go";
        }

        /// <summary>The point to steer for on the way round a planet or moon: a little way along
        /// the great circle to the destination, at a height that is greater the longer the way
        /// (cruise is faster higher up), coming down to the destination's height at the end.
        /// `left` is how far there is still to go, round the curve.</summary>
        static Vector3 RoundTheBody(WarpPoint body, Vector3 me, Vector3 dest, out float left)
        {
            Vector3 c = body.Position;
            float R = body.bodyRadius;
            Vector3 u0 = (me - c).normalized, u1 = (dest - c).normalized;
            float h0 = Vector3.Distance(me, c) - R, h1 = Vector3.Distance(dest, c) - R;
            float angle = Vector3.Angle(u0, u1) * Mathf.Deg2Rad;
            float arc = angle * (R + Mathf.Max(h0, h1));
            left = arc + Mathf.Abs(h0 - h1);
            if (angle < 0.02f) return dest;
            // the height to fly at: on a glide slope down to the destination's height, as high
            // above it as there is still way to go round (cruise goes as the height, so she
            // comes in fast and slows smoothly, rather than crawling the last of the way low down)
            float want = Mathf.Clamp(h1 + arc * 0.8f, h1, Mathf.Max(h1, 300000f));
            // steer for a point some way ahead along the circle (no more than the way left)
            float step = Mathf.Min(angle, Mathf.Max(0.05f, 60000f / (R + want)));
            Vector3 dir = Vector3.Slerp(u0, u1, step / angle);
            float h = Mathf.Lerp(h0, want, 0.5f);
            return c + dir.normalized * (R + h);
        }

        void Reached(CarrierController ship)
        {
            var pf = PlanetFlight.Instance;
            if (pf != null) pf.autoCruise = false;
            demo.autoRudder = null; demo.autoPitch = null;
            ship.throttle = 0f;
            throttleSet = 0f;
            if (Finish == Then.Land)
            {
                finishing = true;
                Phase = "landing";
                nextNotice = Time.time + 6f;
                // (the auto-lander has the throttle now)
                throttleSet = -1f;
                var landing = PlanetLanding.Instance;
                if (landing == null || !landing.LandAt(Course)) Stop($"At {Course.displayName}: couldn't land");
                return;
            }
            // (to a station: the last of the way in to its berth, then docked, or (just going
            // there) stopped in docking range, where K docks)
            if ((Finish == Then.Dock || Finish == Then.Arrive) && DockAt(Course) != null)
            {
                finishing = true;
                dockLegStart = Time.time;
                dockStall = -1f;
                backOffs = 0;
                backOffUntil = 0f;
                Phase = "closing on the berth";
                return;
            }
            Arrived();
        }

        float dockLegStart, dockStall = -1f, backOffUntil;
        int backOffs;

        /// <summary>The last of the way in to a station: to the berth, then dock (or, only going
        /// there, stop in docking range).</summary>
        void FlyDock(CarrierController ship)
        {
            var inst = DockAt(Course);
            var docking = Docking.Instance;
            if (inst == null || docking == null) { Stop("Nowhere to dock"); return; }
            if (docking.Candidate(out string why) == inst && inst.InDockRange(ship.transform.position) &&
                inst.ApproachPoint(ship.transform.position) == inst.BerthPosition)
            {
                ship.throttle = 0f;
                throttleSet = -1f;
                demo.autoRudder = null; demo.autoPitch = null;
                if (Finish == Then.Arrive) { Arrived(); return; }
                if (why == "" && docking.Dock()) Phase = "docking";
                else if (why != "") { Stop($"Can't dock at {inst.displayName}: {why}"); }
                return;
            }
            // stuck against something (the rock's bumps, the buildings by the pad): backed off
            // out from it and tried again; given up, with a word, after a few goes, or far too long
            if (ship.Speed < 2f && Time.time >= backOffUntil) { if (dockStall < 0f) dockStall = Time.time; } else dockStall = -1f;
            if (dockStall > 0f && Time.time - dockStall > 4f)
            {
                backOffs++;
                dockStall = -1f;
                backOffUntil = Time.time + 6f;
            }
            if (backOffs > 3 || Time.time - dockLegStart > 180f)
            {
                Stop($"Couldn't get in to {inst.displayName}'s berth: fly in by hand, then K");
                return;
            }
            Vector3 aim;
            if (Time.time < backOffUntil)
            {
                // (out, away from what it is built into, and up the berth's way out)
                Vector3 me = ship.transform.position, c = inst.Centre;
                Vector3 outward = (me - c).sqrMagnitude > 1f ? (me - c).normalized : -ship.transform.forward;
                aim = me + outward * 1500f + (inst.BerthPosition - c).normalized * 500f;
                Phase = "backing off for another way in";
            }
            // (round to the open side of the berth first: not through the rock it is dug into)
            else aim = inst.ApproachPoint(ship.transform.position);
            var v = ShipAutopilot.BowOn(ship, aim);
            demo.autoRudder = v.x;
            demo.autoPitch = v.y;
            float d = Vector3.Distance(ship.transform.position, aim);
            ship.throttle = d > 3000f ? 0.6f : 0.3f;
            throttleSet = ship.throttle;
            if (Time.time >= backOffUntil) Phase = "closing on the berth";
        }

        void Arrived()
        {
            LastTrip = Time.time - started;
            Arrivals++;
            string where = Course.displayName;
            bool dockable = DockAt(Course) != null;
            End(null);
            Notices.Post(PlanetLanding.OnSurface ? $"Down at {where}" : Docking.Docked ? $"Docked at {where}"
                         : dockable ? $"Arrived at {where}: K docks" : $"Arrived at {where}");
        }

        // ------------------------------------------------------------ on screen

        void OnGUI()
        {
            var ship = Ship;
            if (Course == null || ship == null || PlanetLanding.OnSurface || Docking.Docked) return;
            if (Hud.ReelMode && !Hud.ReelShowsMarks) return;
            var cam = Camera.main;
            if (cam == null) return;
            float s = Hud.S;
            float d = markDist;
            string eta = ship.Speed > 5f ? $"  {Mathf.CeilToInt(d / ship.Speed)} s" : "";
            string text = $"<color=#7ee08a>{Course.displayName}  {CombatMode.Distance(d)}{eta}{(Travelling ? $"  ({Phase})" : "")}</color>";
            Vector3 sp = cam.WorldToScreenPoint(Course.Position);
            var c = new Color(0.5f, 0.9f, 0.55f);
            bool onScreen = sp.z > 0f && sp.x > 0f && sp.x < Screen.width && sp.y > 0f && sp.y < Screen.height;
            if (onScreen)
            {
                var p = new Vector2(sp.x, Screen.height - sp.y);
                float box = 18f * s;
                Hud.Corners(new Rect(p.x - box, p.y - box, box * 2f, box * 2f), 6f * s, Mathf.Max(1f, 2f * s), c);
                GUI.Label(new Rect(p.x + box + 4f * s, p.y - 9f * s, 520f * s, 18f * s), text, Hud.Text(13));
            }
            else
            {
                // off screen: a pointer at the edge, toward it
                Vector2 dir = new Vector2(sp.x - Screen.width * 0.5f, sp.y - Screen.height * 0.5f);
                if (sp.z < 0f) dir = -dir;
                if (dir.sqrMagnitude < 1f) dir = Vector2.up;
                dir.Normalize();
                float edge = Mathf.Min(Screen.width, Screen.height) * 0.42f;
                var p = new Vector2(Screen.width * 0.5f + dir.x * edge, Screen.height * 0.5f - dir.y * edge);
                float box = 10f * s;
                Hud.Fill(new Rect(p.x - box * 0.5f, p.y - box * 0.5f, box, box), c);
                // (the label kept clear of the controls box and the ship's console)
                var lr = new Rect(p.x - 160f * s, p.y + box, 320f * s, 18f * s);
                if (lr.Overlaps(ShipHud.HelpRect)) lr.x = ShipHud.HelpRect.xMax + 8f * s;
                if (lr.Overlaps(ShipConsole.Rect)) lr.y = ShipConsole.Rect.yMin - lr.height - 6f * s;
                GUI.Label(lr, text, Hud.Text(12, false, TextAnchor.UpperCenter));
            }
        }
    }
}

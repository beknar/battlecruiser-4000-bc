using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The star system map: a top-down chart of every place the frigate can mini-warp to (the
    /// star, the planets and their moons, the asteroid fields, the comets, the Lagrange points,
    /// and what is built out in deep space on its own), with the bodies' orbits, the
    /// frigate's own position and heading, and for the chosen destination its distance and how
    /// long the trip will take, and the points of interest there (its bases and stations: Places),
    /// which the navigation mode's menu flies to once the ship is there. Built places are small
    /// squares; a name that would run into another is left off until the place is chosen. Pick a
    /// destination and warp from here.
    /// Everything moves on its orbit (OrbitalSystem), and the map with it: the comets' long
    /// ellipses are drawn, and the five Lagrange points of Thalassa and Jovara, going round
    /// with them.
    ///
    /// The other ships of the player's side are on it too: the other players' (in co-op), the AI
    /// wingmen's, and the fleet's, each marked with its name. Click one (another player, or a
    /// wingman) and WARP, or double-click it, for a mini-warp to them: the ship drops out close
    /// by them, astern and to one side.
    ///
    /// Keyboard and mouse: M opens and closes it; click a place (or use the arrow keys / WASD,
    /// Tab to step through), then click WARP, double-click, or press Enter. Esc closes it.
    /// Gamepad: R3 (press the right stick) opens it; the left stick or D-pad moves between places,
    /// LB/RB step through them, A warps, B closes. It opens on the place the ship is at.
    ///
    /// The view can be moved and zoomed, from the whole system down to a planet and its moons:
    /// the mouse wheel zooms about the pointer, dragging (with the left button once the pointer
    /// has moved a few pixels, or with the right or middle) moves the map, and so do Shift and the
    /// arrows or WASD; + and - zoom about the middle, and Home shows the whole system again; on the
    /// pad the right stick moves it and the triggers zoom. Choosing a place off the view brings it
    /// in. Everything is drawn relative to the view's middle and kept inside the map's square. A
    /// click is taken on the release, so a drag doesn't choose what it started on.
    ///
    /// Moons are a planet's (PassedOver): at the whole system's view a moon is drawn within a
    /// fraction of a pixel of its planet, so until the view is close enough to draw it apart, a
    /// click there, the hover, Tab and the arrows take the planet, and the moon's name is left
    /// off. The info panel lists a planet's moons and every point of interest round it
    /// (Places.Around).
    ///
    /// The local map (LOCAL MAP beside WARP, L, Y on the pad), with a planet or moon chosen or the
    /// ship at one, shows the planet, its moons and every point of interest round them, centred on
    /// the planet as it goes round and scaled so the outermost moon fits; a high orbit, straight
    /// over the pole, is drawn off the planet's disc. WARP there warps to a moon or a point of
    /// interest; on a point of interest at the place the ship is at, FLY TO flies there in real
    /// time (NavigationMode.TravelTo). BACK, Esc, L (B or Y on the pad) go back to the system map.
    /// </summary>
    public class SystemMap : MonoBehaviour
    {
        public CarrierController carrier;
        public WarpDrive drive;
        public string systemName = "Kestrel System";
        /// <summary>The star this system goes round (its warp point), for "orbits ...".</summary>
        static string StarName => WarpPoint.All.Find(w => w != null && w.kind == WarpPoint.Kind.Star)?.displayName ?? "the star";

        public static bool IsOpen => InputFocus.Has(InputFocus.Panel.SystemMap);
        /// <summary>The map closed this frame (so Esc doesn't also quit the game).</summary>
        public static bool JustClosed => InputFocus.JustReleased(InputFocus.Panel.SystemMap);

        public WarpPoint Selected { get; private set; }
        public WarpPoint Hover { get; private set; }

        /// <summary>Another ship of the player's side, as the map shows it.</summary>
        public struct Ally
        {
            public string name, kind;
            public Transform ship;
            public Color color;
            /// <summary>Why it can't be warped to (null: it can).</summary>
            public string noWarp;
        }

        /// <summary>The other ships of the player's side on the map now.</summary>
        public readonly List<Ally> Allies = new List<Ally>();
        /// <summary>The ally chosen (its ship), instead of a place; null if none.</summary>
        public Transform SelectedAlly { get; private set; }
        Transform lastClickedAlly;

        Rect mapRect, infoRect, warpButton;
        float pxPerM = 1f;
        Vector2 center;
        // the view: how far in (1 the whole system) and where its middle is (map metres, x and z,
        // from the star); a drag under way and where it was last frame
        float zoom = 1f;
        Vector2 view;
        bool dragging;
        Vector2 dragLast;
        // the left button: held since it went down on the map (or the panel), where, and whether
        // it has moved far enough to be a drag (not a click)
        bool leftDown, leftDrag;
        Vector2 leftFrom, leftLast;

        /// <summary>How far the pointer moves with the left button held before it drags the view
        /// (less, and letting go is a click).</summary>
        public const float DragPx = 6f;
        /// <summary>How far apart (in pixels, times the text scale) a moon and its planet have to
        /// be drawn before the moon can be chosen on its own.</summary>
        public const float MoonApartPx = 16f;

        /// <summary>(For the tests: lay the map out for a screen this size, not the real one.)</summary>
        [System.NonSerialized] public Vector2 screenSize;

        /// <summary>How far the view is zoomed in (1: the whole system).</summary>
        public float Zoom => zoom;
        /// <summary>The most it zooms in: the map about 2,000 km across.</summary>
        public float MaxZoom => Mathf.Max(1f, extent * 2f / 2.0e6f);
        Texture2D disc, ring, arrow;
        float extent = 1f, lastClick = -1f, stickRepeat;
        WarpPoint lastClicked;
        List<WarpPoint> order = new List<WarpPoint>();

        void Start()
        {
            if (carrier == null) carrier = PlayerShips.Primary != null ? PlayerShips.Primary.GetComponent<CarrierController>() : null;
            if (drive == null && carrier != null) drive = carrier.GetComponent<WarpDrive>();
            disc = MakeDisc(64, false);
            ring = MakeDisc(64, true);
            arrow = MakeArrow(48);
        }

        // ------------------------------------------------------------ opening, choosing, warping

        public void Open()
        {
            if (drive == null || !InputFocus.Take(InputFocus.Panel.SystemMap)) return;
            // (an ally chosen last time, from navigation's menu say, isn't carried over)
            SelectedAlly = null;
            // (it opens on the system map)
            localPlanet = null;
            localOrder.Clear();
            RefreshOrder();
            ResetView();
            Layout();
            // it opens on where the ship is (at a moon, drawn on its planet at this view: the
            // planet); out in open space, on the place chosen last time, or the nearest
            var here = HerePlace;
            if (here != null && order.Contains(here)) Selected = PassedOver(here) ? here.orbitParent : here;
            else if (Selected == null || !order.Contains(Selected) || PassedOver(Selected))
                Selected = order.Where(p => !PassedOver(p)).OrderBy(p => Vector3.Distance(p.Position, carrier.transform.position)).FirstOrDefault();
            SoundFX.Ui(Sfx.UiLock);
        }

        public void Close()
        {
            if (!IsOpen) return;
            InputFocus.Release(InputFocus.Panel.SystemMap);
            SoundFX.Ui(Sfx.UiClear);
            localPlanet = null;
            // (back to the mode it was opened from)
            if (switchedMode) Modes.Return(ModeOfPlay.Navigation);
            switchedMode = false;
        }

        // the map opened from another mode, which it goes back to when it closes
        bool switchedMode;

        public void Toggle() { if (IsOpen) Close(); else Open(); }

        // ------------------------------------------------------------ the local map

        // the planet the local map shows (null: the system map), and what is on it: the planet,
        // its moons and every point of interest round them (Places.Around), and how far out the
        // furthest of them is (m, seen from above)
        WarpPoint localPlanet;
        readonly List<WarpPoint> localOrder = new List<WarpPoint>();
        float localExtent = 1f;

        /// <summary>The local map is up, in place of the system map: a planet, its moons and
        /// every point of interest round them.</summary>
        public bool Local => localPlanet != null;
        /// <summary>The planet the local map shows (null on the system map).</summary>
        public WarpPoint LocalPlanet => localPlanet;
        /// <summary>What the local map shows: the planet, its moons, then the points of interest
        /// (empty on the system map).</summary>
        public IReadOnlyList<WarpPoint> LocalPoints => localOrder;

        // what the keys, the stick and the pointer choose from: the places, or the local map's points
        List<WarpPoint> Choices => localPlanet != null ? localOrder : order;

        /// <summary>The planet a place belongs with (a moon's planet, or the planet itself), or null.</summary>
        public static WarpPoint PlanetOf(WarpPoint w)
        {
            if (w == null) return null;
            if (w.kind == WarpPoint.Kind.Moon && w.orbitParent != null && w.orbitParent.kind == WarpPoint.Kind.Planet) return w.orbitParent;
            return w.kind == WarpPoint.Kind.Planet ? w : null;
        }

        /// <summary>The planet LOCAL MAP would show: the one chosen (or the moon's planet), or the
        /// one the ship is at (or at a moon of); null if neither, when the button isn't shown.</summary>
        public WarpPoint LocalTarget => (SelectedAlly == null ? PlanetOf(Selected) : null) ?? PlanetOf(HerePlace);

        /// <summary>Show the local map (L, Y on the pad, LOCAL MAP). It keeps the choice if it is on
        /// it, and otherwise chooses where the ship is, or the planet.</summary>
        public bool OpenLocal()
        {
            var planet = IsOpen ? LocalTarget : null;
            if (planet == null) { SoundFX.Ui(Sfx.UiError); return false; }
            localPlanet = planet;
            RefreshLocal(true);
            if (SelectedAlly != null || Selected == null || !localOrder.Contains(Selected))
                Selected = HerePlace != null && localOrder.Contains(HerePlace) ? HerePlace : planet;
            SelectedAlly = null;
            leftDown = leftDrag = dragging = false;
            Layout();
            SoundFX.Ui(Sfx.UiLock);
            return true;
        }

        /// <summary>Back to the system map (L, Esc, B on the pad, BACK). A point of interest chosen
        /// on the local map gives way to its place, which is what the system map shows.</summary>
        public void CloseLocal()
        {
            if (localPlanet == null) return;
            localPlanet = null;
            localOrder.Clear();
            if (Selected != null && !order.Contains(Selected)) Selected = Places.PlaceOf(Selected) ?? Selected;
            Layout();
            SoundFX.Ui(Sfx.UiClear);
        }

        public void ToggleLocal() { if (localPlanet != null) CloseLocal(); else OpenLocal(); }

        /// <summary>What the local map shows, worked out again (a wreck comes and goes); `fit`:
        /// and its scale, so the outermost moon, and what is round it, fits.</summary>
        void RefreshLocal(bool fit)
        {
            localOrder.Clear();
            if (localPlanet == null) return;
            localOrder.Add(localPlanet);
            foreach (var w in Places.Around(localPlanet)) if (!localOrder.Contains(w)) localOrder.Add(w);
            if (!fit) return;
            Vector3 c = localPlanet.Position;
            localExtent = Mathf.Max(1000f, localPlanet.bodyRadius * 1.5f);
            foreach (var w in localOrder)
                localExtent = Mathf.Max(localExtent, Flat(w.Position - c).magnitude + (w.kind == WarpPoint.Kind.Moon ? w.bodyRadius * 1.5f : 0f));
            localExtent *= 1.1f;
        }

        /// <summary>The choice is a point of interest at the place the ship is at: FLY TO (Enter,
        /// A, a double click) flies there in real time, as navigation mode's menu does, rather
        /// than warping.</summary>
        public bool CanFlyTo(WarpPoint p) => p != null && SelectedAlly == null && !Places.IsPlace(p) && HerePlace != null && Places.PlaceOf(p) == HerePlace;

        /// <summary>Fly to the point of interest chosen (NavigationMode.TravelTo), and close the map.</summary>
        public bool FlyToSelected()
        {
            var p = Selected;
            var navigation = Nav;
            if (!CanFlyTo(p) || navigation == null) { SoundFX.Ui(Sfx.UiError); return false; }
            if (!navigation.TravelTo(p, NavigationMode.Then.Arrive)) return false;
            // (the trip is navigation's: closing the map mustn't take the player back to the mode
            // it was opened from, which would stop it, so navigation is kept as their own choice)
            switchedMode = false;
            Modes.Set(ModeOfPlay.Navigation);
            Close();
            return true;
        }

        /// <summary>Enter, A, a double click, the button: fly to the choice if it is a point of
        /// interest here (FLY TO), otherwise warp to it.</summary>
        public bool Go() => CanFlyTo(Selected) ? FlyToSelected() : WarpToSelected();

        NavigationMode nav;
        NavigationMode Nav => nav != null ? nav : (nav = FindAnyObjectByType<NavigationMode>());

        /// <summary>Choose a place. Chosen by key or stick, it is brought into view; clicked on
        /// (`bring` false), the view stays put, so a second click finds it where the first did.</summary>
        public void Select(WarpPoint p, bool bring = true)
        {
            if (p == null || (p == Selected && SelectedAlly == null)) return;
            Selected = p;
            if (bring) BringIntoView(p.Position);
            SelectedAlly = null;
            SoundFX.Ui(Sfx.UiLock);
        }

        /// <summary>Choose another ship of the player's side (to warp to).</summary>
        public void SelectAlly(Transform ship)
        {
            if (ship == null || ship == SelectedAlly) return;
            SelectedAlly = ship;
            SoundFX.Ui(Sfx.UiLock);
        }

        Ally? AllyOf(Transform ship)
        {
            foreach (var a in Allies) if (a.ship == ship) return a;
            return null;
        }

        /// <summary>The other ships of the player's side: the other players' and the AI
        /// wingmen's (in co-op, in this level), and the fleet's.</summary>
        public void RefreshAllies()
        {
            Allies.Clear();
            var client = CoopClient.Instance;
            var world = CoopWorld.Instance;
            if (client != null && client.Connected && world != null)
                foreach (var p in client.Players)
                {
                    if (p.id == client.MyId) continue;
                    var r = world.RemoteOf(p.id);
                    if (r == null) continue;
                    bool bot = CoopBot.IsBot(p);
                    string down = !string.IsNullOrEmpty(r.Surface) ? $"down on {r.Surface}" : null;
                    if (r.Hidden && down == null) continue;   // (in warp, destroyed, or not heard from)
                    // (already close by, in formation say: nothing to warp across)
                    if (down == null && carrier != null && Vector3.Distance(carrier.transform.position, r.transform.position) < WarpDrive.AllyMinDistance)
                        down = $"already with you ({CombatMode.Distance(Vector3.Distance(carrier.transform.position, r.transform.position))} off)";
                    Allies.Add(new Ally
                    {
                        name = p.name, kind = bot ? "AI wingman" : "player", ship = r.transform,
                        color = bot ? new Color(1f, 0.7f, 0.3f) : new Color(0.45f, 0.85f, 1f),
                        noWarp = down,
                    });
                }
            var fleet = FleetDirector.Instance;
            if (fleet != null)
                foreach (var s in fleet.Ships)
                    if (s != null)
                        Allies.Add(new Ally { name = s.displayName, kind = "fleet", ship = s.transform, color = new Color(0.55f, 1f, 0.6f), noWarp = "she flies with you" });
            if (SelectedAlly != null && AllyOf(SelectedAlly) == null) SelectedAlly = null;
        }

        /// <summary>A mini-warp to the ally chosen, if the drive allows it.</summary>
        public bool WarpToAlly()
        {
            var a = SelectedAlly != null ? AllyOf(SelectedAlly) : null;
            if (a == null || drive == null) return false;
            if (a.Value.noWarp != null) { SoundFX.Ui(Sfx.UiError); Notices.Post($"Can't warp to {a.Value.name}: {a.Value.noWarp}"); return false; }
            if (drive.EngageTo(a.Value.ship, a.Value.name, out string why))
            {
                // (done with: the map next time opens on the places)
                SelectedAlly = null;
                Notices.Post($"Warping to {a.Value.name}: {drive.ChargeTime + drive.TravelTime:F1} s");
                Close();
                return true;
            }
            SoundFX.Ui(Sfx.UiError);
            Notices.Post($"Can't warp to {a.Value.name}: {why}");
            return false;
        }

        /// <summary>Warp to the chosen place (or ally), if the drive allows it.</summary>
        public bool WarpToSelected()
        {
            if (SelectedAlly != null) return WarpToAlly();
            if (Selected == null || drive == null) return false;
            if (IsHere(Selected))
            {
                SoundFX.Ui(Sfx.UiError);
                Notices.Post($"Already at {Selected.displayName}");
                return false;
            }
            if (drive.Engage(Selected, out string why))
            {
                Notices.Post($"Warping to {Selected.displayName}: {drive.ChargeTime + drive.TravelTime:F1} s");
                Close();
                return true;
            }
            SoundFX.Ui(Sfx.UiError);
            Notices.Post("Can't warp: " + why);
            return false;
        }

        // the place the ship is at (the map lists places only: at the Highport, it is at
        // Thalassa), worked out once a frame
        WarpPoint herePlace;
        int hereFrame = -1;

        bool IsHere(WarpPoint p) => p != null && p == HerePlace;

        WarpPoint HerePlace
        {
            get
            {
                if (carrier == null) return null;
                if (hereFrame != Time.frameCount)
                {
                    hereFrame = Time.frameCount;
                    herePlace = Places.At(carrier.transform.position);
                }
                return herePlace;
            }
        }

        /// <summary>A moon drawn on top of its planet at the view's zoom (nearer it on the map than
        /// MoonApartPx): it counts as part of the planet, so a click, the hover, Tab and the arrows
        /// pass over it, and its name is left off, until the view is close enough to draw it apart.
        /// (Never on the local map: there every moon is drawn apart.)</summary>
        public bool PassedOver(WarpPoint w) => localPlanet == null && PassedOver(w, pxPerM);

        /// <summary>The same, at a scale of `pixelsPerMetre`.</summary>
        public static bool PassedOver(WarpPoint w, float pixelsPerMetre) =>
            w != null && w.kind == WarpPoint.Kind.Moon && w.orbitParent != null
            && Flat(w.Position - w.orbitParent.Position).magnitude * Mathf.Abs(pixelsPerMetre) < MoonApartPx * Hud.S;

        /// <summary>The places on the map, in their reading order (worked out again).</summary>
        public void RefreshOrder()
        {
            // a sensible reading order: the star, then outward by distance from it, moons after their planet
            Vector3 star = WarpPoint.All.FirstOrDefault(w => w.kind == WarpPoint.Kind.Star)?.Position ?? Vector3.zero;
            float Key(WarpPoint w) => Vector3.Distance(star, (w.orbitParent != null ? w.orbitParent : w).Position) * 1000f
                                      + (w.orbitParent != null ? Vector3.Distance(w.Position, w.orbitParent.Position) / 1000f : 0f);
            // (the places: their points of interest are flown to from there, in navigation mode)
            order = WarpPoint.All.Where(Places.IsPlace).OrderBy(Key).ToList();
            extent = 1f;
            foreach (var w in order) extent = Mathf.Max(extent, Flat(w.Position - star).magnitude + w.arrivalDistance);
        }

        /// <summary>Choose the next place in the reading order (`dir` -1: the one before),
        /// passing over a moon drawn on its planet.</summary>
        public void Step(int dir)
        {
            if (order.Count == 0) RefreshOrder();
            var list = Choices;
            if (list.Count == 0) return;
            int i = Selected != null ? list.IndexOf(Selected) : -1;
            for (int n = 0; n < list.Count; n++)
            {
                i = ((i + dir) % list.Count + list.Count) % list.Count;
                if (!PassedOver(list[i])) { Select(list[i]); return; }
            }
        }

        /// <summary>Move the choice to the nearest place lying in a direction on the map (GUI space, y down).</summary>
        public void StepToward(Vector2 dir)
        {
            if (Selected == null) { Step(1); return; }
            dir.Normalize();
            Vector2 from = ScreenPosOf(Selected);
            WarpPoint best = null;
            float bestScore = float.MaxValue;
            foreach (var w in Choices)
            {
                if (w == Selected || PassedOver(w)) continue;
                Vector2 d = ScreenPosOf(w) - from;
                float dist = d.magnitude;
                if (dist < 1f) continue;
                float cos = Vector2.Dot(d / dist, dir);
                if (cos < 0.35f) continue;                     // within about 70 degrees of the push
                float score = dist * (2f - cos);
                if (score < bestScore) { bestScore = score; best = w; }
            }
            if (best != null) Select(best);
        }

        // ------------------------------------------------------------ input

        void Update()
        {
            if (drive == null || carrier == null) return;
            if (GameMenu.IsOpen || GameMenu.JustClosed) return;   // the pause menu has the keys
            var k = Keyboard.current;
            var pad = Gamepad.current;
            var mouse = Mouse.current;

            if (!IsOpen)
            {
                if (carrier.Destroyed) return;
                bool want = (k != null && k.mKey.wasPressedThisFrame) || (pad != null && pad.rightStickButton.wasPressedThisFrame && !carrier.HasHangar);
                if (!want) return;
                // no warping from the surface (or on the way down or up)
                if (PlanetLanding.Busy) { if (PlanetLanding.OnSurface) Notices.Post("Take off before using the system map"); return; }
                Open();
                // (the map is navigation's)
                if (IsOpen && !Modes.Is(ModeOfPlay.Navigation)) { Modes.Set(ModeOfPlay.Navigation, auto: true); switchedMode = true; }
                return;
            }
            if (localPlanet != null) RefreshLocal(false);
            Layout();
            RefreshAllies();
            bool local = localPlanet != null;

            if (k != null)
            {
                if (k.mKey.wasPressedThisFrame) { Close(); return; }
                // (Esc on the local map goes back to the system map)
                if (k.escapeKey.wasPressedThisFrame) { if (local) CloseLocal(); else Close(); return; }
                // L: the local map, and back (read only while the map is up: outside it, L levels off)
                if (k.lKey.wasPressedThisFrame) { ToggleLocal(); return; }
                if (!local)
                {
                    if (k.equalsKey.wasPressedThisFrame || k.numpadPlusKey.wasPressedThisFrame) ZoomAt(mapRect.center, 1.5f);
                    if (k.minusKey.wasPressedThisFrame || k.numpadMinusKey.wasPressedThisFrame) ZoomAt(mapRect.center, 1f / 1.5f);
                    if (k.homeKey.wasPressedThisFrame) ResetView();
                }
                if (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame) { Go(); return; }
                if (k.tabKey.wasPressedThisFrame) Step(k.shiftKey.isPressed ? -1 : 1);
                if (k.shiftKey.isPressed && !local)
                {
                    // Shift and the arrows (or WASD) move the view, for as long as they are held
                    // (a way, in GUI space, y down; the view goes that way, as the right stick's does)
                    Vector2 way = Vector2.zero;
                    if (k.leftArrowKey.isPressed || k.aKey.isPressed) way.x -= 1f;
                    if (k.rightArrowKey.isPressed || k.dKey.isPressed) way.x += 1f;
                    if (k.upArrowKey.isPressed || k.wKey.isPressed) way.y -= 1f;
                    if (k.downArrowKey.isPressed || k.sKey.isPressed) way.y += 1f;
                    if (way.sqrMagnitude > 0f) Pan(-way.normalized * mapRect.width * 0.8f * Time.unscaledDeltaTime);
                }
                else
                {
                    if (k.leftArrowKey.wasPressedThisFrame || k.aKey.wasPressedThisFrame) StepToward(Vector2.left);
                    if (k.rightArrowKey.wasPressedThisFrame || k.dKey.wasPressedThisFrame) StepToward(Vector2.right);
                    if (k.upArrowKey.wasPressedThisFrame || k.wKey.wasPressedThisFrame) StepToward(Vector2.down);
                    if (k.downArrowKey.wasPressedThisFrame || k.sKey.wasPressedThisFrame) StepToward(Vector2.up);
                }
            }
            if (pad != null)
            {
                if (pad.rightStickButton.wasPressedThisFrame) { Close(); return; }
                // (B on the local map goes back to the system map; Y shows it, and goes back)
                if (pad.buttonEast.wasPressedThisFrame) { if (local) CloseLocal(); else Close(); return; }
                if (pad.buttonNorth.wasPressedThisFrame) { ToggleLocal(); return; }
                if (pad.buttonSouth.wasPressedThisFrame) { Go(); return; }
                if (pad.rightShoulder.wasPressedThisFrame) Step(1);
                if (pad.leftShoulder.wasPressedThisFrame) Step(-1);
                // the right stick moves the view, the triggers zoom it (not the local map's)
                float dt = Time.unscaledDeltaTime;
                Vector2 rs = pad.rightStick.ReadValue();
                if (rs.magnitude > 0.2f && !local) Pan(new Vector2(-rs.x, rs.y) * mapRect.width * 0.8f * dt);
                float zt = pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue();
                if (Mathf.Abs(zt) > 0.1f && !local) ZoomAt(mapRect.center, Mathf.Pow(4f, zt * dt));
                Vector2 d = pad.dpad.ReadValue();
                Vector2 ls = pad.leftStick.ReadValue();
                if (ls.magnitude > d.magnitude) d = ls;
                stickRepeat -= Time.unscaledDeltaTime;
                if (d.magnitude > 0.6f)
                {
                    if (stickRepeat <= 0f) { StepToward(new Vector2(d.x, -d.y)); stickRepeat = 0.3f; }
                }
                else stickRepeat = 0f;
            }
            if (mouse != null)
            {
                Vector2 mp = mouse.position.ReadValue();
                Vector2 gui = new Vector2(mp.x, Screen.height - mp.y);
                Hover = mapRect.Contains(gui) ? PointAt(gui) : null;
                // the left button: a click when it is let go within DragPx of where it went down
                // (on WARP, or a place: chosen, or warped to on a double click); moved further on
                // the map, it drags the view instead, and chooses nothing
                if (mouse.leftButton.wasPressedThisFrame) { leftDown = true; leftDrag = false; leftFrom = leftLast = gui; }
                if (leftDown && !leftDrag && mapRect.Contains(leftFrom) && Vector2.Distance(gui, leftFrom) >= DragPx) leftDrag = true;
                if (leftDrag) { Pan(gui - leftLast); leftLast = gui; }
                if (leftDown && !mouse.leftButton.isPressed)
                {
                    bool click = !leftDrag && Vector2.Distance(gui, leftFrom) < DragPx;
                    leftDown = leftDrag = false;
                    if (click) Click(leftFrom);
                }
                // the wheel zooms about the pointer; a right or middle drag moves the view
                float wheel = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(wheel) > 0.01f && mapRect.Contains(gui)) ZoomAt(gui, wheel > 0f ? 1.25f : 0.8f);
                bool held = mouse.rightButton.isPressed || mouse.middleButton.isPressed;
                bool pressed = mouse.rightButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame;
                if (pressed && !dragging && mapRect.Contains(gui)) { dragging = true; dragLast = gui; }
                else if (!held) dragging = false;
                if (dragging) { Pan(gui - dragLast); dragLast = gui; }
            }
        }

        /// <summary>A click at a GUI position: choose a place, or warp (WARP button, or a double
        /// click; FLY TO to a point of interest here), or the local map and back (LOCAL MAP, BACK).</summary>
        public void Click(Vector2 gui)
        {
            if (warpButton.Contains(gui)) { Go(); return; }
            if (localButton.Contains(gui)) { ToggleLocal(); return; }
            // (only on the map's square: zoomed in, places off it are under the panel beside it)
            if (!mapRect.Contains(gui)) return;
            // another ship of the player's side (before a place: it is drawn over them)
            var ally = AllyAt(gui);
            if (ally != null)
            {
                bool again = ally == lastClickedAlly && Time.unscaledTime - lastClick < 0.4f;
                lastClickedAlly = ally;
                lastClicked = null;
                lastClick = Time.unscaledTime;
                SelectAlly(ally);
                if (again) WarpToAlly();
                return;
            }
            lastClickedAlly = null;
            var p = PointAt(gui);
            if (p == null) return;
            bool twice = p == lastClicked && Time.unscaledTime - lastClick < 0.4f;
            lastClicked = p;
            lastClick = Time.unscaledTime;
            Select(p, bring: false);
            if (twice) Go();
        }

        // where each ally's name is drawn (it can be clicked too: in formation, its arrow is
        // under the frigate's own)
        readonly List<(Rect r, Transform ship)> allyLabels = new List<(Rect, Transform)>();

        /// <summary>The ally's ship drawn at a GUI position (within a few pixels of its arrow, or
        /// on its name), or null.</summary>
        public Transform AllyAt(Vector2 gui, float maxPx = 16f)
        {
            // (a name over a place's marker: the place, clicked right on it, comes first)
            if (PointAt(gui, 10f) == null)
                foreach (var (r, ship) in allyLabels) if (ship != null && r.Contains(gui) && AllyOf(ship) != null) return ship;
            Transform best = null;
            float bestD = maxPx * Hud.S;
            foreach (var a in Allies)
            {
                if (a.ship == null) continue;
                float d = Vector2.Distance(ToScreen(a.ship.position), gui);
                if (d < bestD) { bestD = d; best = a.ship; }
            }
            return best;
        }

        /// <summary>Where an ally's ship is drawn on the map (GUI space).</summary>
        public Vector2 ScreenPosOf(Transform ship) => ToScreen(ship.position);

        /// <summary>Where an ally's name is drawn on the map (GUI space), if it is drawn.</summary>
        public Vector2? LabelPosOf(Transform ship)
        {
            foreach (var (r, s) in allyLabels) if (s == ship) return r.center;
            return null;
        }

        /// <summary>The place drawn nearest a GUI position, within `maxPx` (times the text scale),
        /// or null. A moon drawn on its planet is passed over: the planet is what is there.</summary>
        public WarpPoint PointAt(Vector2 gui, float maxPx = 26f)
        {
            WarpPoint best = null;
            float bestD = maxPx * Hud.S;
            foreach (var w in Choices)
            {
                if (PassedOver(w)) continue;
                float d = Vector2.Distance(ScreenPosOf(w), gui);
                if (d < bestD) { bestD = d; best = w; }
            }
            return best;
        }

        // ------------------------------------------------------------ layout and drawing

        static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);

        /// <summary>Where a place (or, on the local map, a point of interest) is drawn (GUI space).</summary>
        public Vector2 ScreenPosOf(WarpPoint p) => localPlanet != null ? LocalPos(p) : ToScreen(p.Position);

        /// <summary>Where the local map draws a point: seen from above, as the system map does,
        /// except that one straight over or under its planet or moon (a high orbit, over the
        /// pole), which would sit on the body's middle, is drawn just off its disc: up and to the
        /// right of it (down and to the left, under it).</summary>
        Vector2 LocalPos(WarpPoint w)
        {
            Vector2 p = ToScreen(w.Position);
            var body = Places.PlaceOf(w);
            if (body == null || (body.kind != WarpPoint.Kind.Planet && body.kind != WarpPoint.Kind.Moon)) return p;
            Vector3 off = w.Position - body.Position;
            if (Flat(off).magnitude >= off.magnitude * 0.5f) return p;
            Vector2 way = off.y >= 0f ? new Vector2(0.7071f, -0.7071f) : new Vector2(-0.7071f, 0.7071f);
            return ToScreen(body.Position) + way * (BodyPx(body) * 0.5f + 14f * Hud.S);
        }

        /// <summary>How big the local map draws a planet or moon: at its size, and no smaller than
        /// the system map draws it.</summary>
        float BodyPx(WarpPoint w) => Mathf.Max((w.kind == WarpPoint.Kind.Moon ? 7f : 22f) * Hud.S, w.bodyRadius * 2f * Mathf.Abs(pxPerM));
        // (relative to the view's middle first: zoomed in, the star is millions of pixels off)
        Vector2 ToScreen(Vector3 world) => mapRect.center + new Vector2(world.x - viewWorld.x, -(world.z - viewWorld.y)) * pxPerM;
        /// <summary>The map point (x, z) under a GUI position.</summary>
        public Vector2 FromScreen(Vector2 gui) => viewWorld + new Vector2(gui.x - mapRect.center.x, -(gui.y - mapRect.center.y)) / pxPerM;
        // the view's middle in the world (x, z), and the star's
        Vector2 viewWorld, starFlat;
        float BasePxPerM => mapRect.width * 0.47f / Mathf.Max(1f, extent);

        /// <summary>Zoom by `factor` about a GUI position (it stays over the same place).</summary>
        public void ZoomAt(Vector2 gui, float factor)
        {
            if (localPlanet != null) return;   // (the local map keeps its planet and its moons in view)
            Layout();
            Vector2 at = FromScreen(gui);
            zoom = Mathf.Clamp(zoom * factor, 1f, MaxZoom);
            Layout();
            Vector2 now = FromScreen(gui);
            view += at - now;
            ClampView();
            Layout();
        }

        /// <summary>Move the view by a drag of `px` (GUI space).</summary>
        public void Pan(Vector2 px)
        {
            if (localPlanet != null) return;
            Layout();
            view += new Vector2(-px.x, px.y) / pxPerM;
            ClampView();
            Layout();
        }

        /// <summary>The whole system again.</summary>
        public void ResetView()
        {
            zoom = 1f;
            view = Vector2.zero;
            dragging = false;
            leftDown = leftDrag = false;
        }

        /// <summary>Move the view, if need be, so a place is on it (well inside the edge).</summary>
        public void BringIntoView(Vector3 world)
        {
            if (localPlanet != null) return;
            Layout();
            var inner = new Rect(mapRect.x + mapRect.width * 0.1f, mapRect.y + mapRect.height * 0.1f, mapRect.width * 0.8f, mapRect.height * 0.8f);
            if (inner.Contains(ToScreen(world))) return;
            view = new Vector2(world.x, world.z) - starFlat;
            ClampView();
            Layout();
        }

        /// <summary>A place is in the map's square now (GUI space).</summary>
        public bool InView(Vector3 world) => mapRect.Contains(ToScreen(world));

        void ClampView()
        {
            float most = extent * 1.05f;
            if (view.magnitude > most) view = view.normalized * most;
        }

        /// <summary>A label's rect is all on the map, below its title.</summary>
        bool Inside(Rect r) => r.xMin >= mapRect.xMin && r.xMax <= mapRect.xMax && r.yMin >= mapRect.yMin + 40f * Hud.S && r.yMax <= mapRect.yMax;

        bool OnMap(Vector2 p, float margin) =>
            p.x > mapRect.xMin - margin && p.x < mapRect.xMax + margin && p.y > mapRect.yMin - margin && p.y < mapRect.yMax + margin;
        public Rect WarpButtonRect => warpButton;

        /// <summary>Where the map's square and its panel go on screen, and the view's scale and middle.</summary>
        public void Layout()
        {
            float s = Hud.S;
            float sw = screenSize.x > 0f ? screenSize.x : Screen.width, sh = screenSize.y > 0f ? screenSize.y : Screen.height;
            float margin = 30f * s;
            float infoW = 340f * s;
            float side = Mathf.Min(sh - margin * 2f, sw - infoW - margin * 3f);
            float x0 = (sw - side - infoW - margin) * 0.5f;
            mapRect = new Rect(x0, (sh - side) * 0.5f, side, side);
            infoRect = new Rect(mapRect.xMax + margin, mapRect.y, infoW, side);
            Vector3 star = WarpPoint.All.FirstOrDefault(w => w != null && w.kind == WarpPoint.Kind.Star)?.Position ?? Vector3.zero;
            starFlat = new Vector2(star.x, star.z);
            zoom = Mathf.Clamp(zoom, 1f, MaxZoom);
            if (localPlanet != null)
            {
                // the local map: on the planet (kept there as it goes round), the outermost moon in
                pxPerM = mapRect.width * 0.46f / Mathf.Max(1f, localExtent);
                viewWorld = Flat(localPlanet.Position);
            }
            else
            {
                pxPerM = BasePxPerM * zoom;
                viewWorld = starFlat + view;
            }
            center = ToScreen(Vector3.zero);
            // WARP (FLY TO), with LOCAL MAP (BACK) beside it when there is a local map to show
            var row = new Rect(infoRect.x + 20f * s, infoRect.yMax - 120f * s, infoRect.width - 40f * s, 50f * s);
            if (localPlanet != null || LocalTarget != null)
            {
                float gap = 10f * s;
                warpButton = new Rect(row.x, row.y, (row.width - gap) * 0.5f, row.height);
                localButton = new Rect(warpButton.xMax + gap, row.y, row.xMax - warpButton.xMax - gap, row.height);
            }
            else
            {
                warpButton = row;
                localButton = Rect.zero;
            }
        }

        Rect localButton;
        /// <summary>The LOCAL MAP button (BACK on the local map); empty when it isn't shown.</summary>
        public Rect LocalButtonRect => localButton;

        void OnGUI()
        {
            if (!IsOpen || drive == null) return;
            if (order.Count == 0) RefreshOrder();
            Layout();
            float s = Hud.S;
            Hud.Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0.01f, 0.03f, 0.6f));
            Hud.Fill(mapRect, new Color(0.01f, 0.02f, 0.05f, 0.92f));
            Hud.Fill(infoRect, new Color(0.01f, 0.02f, 0.05f, 0.92f));
            Hud.Panel(mapRect);
            Hud.Panel(infoRect);
            GUI.Label(new Rect(mapRect.x + 14f * s, mapRect.y + 10f * s, mapRect.width, 40f * s),
                localPlanet != null ? $"<b>{localPlanet.displayName.ToUpper()}</b>   <color=#9aa4b0>local map: its moons and points of interest</color>"
                                    : $"<b>{StarSystems.Current.name.ToUpper()}</b>   <color=#9aa4b0>system map: mini-warp</color>", Hud.Text(22));

            var label = Hud.Text(15);
            if (localPlanet != null) LocalBodies(s, label);
            else SystemBodies(s, label);
            // the frigate, and the way to the chosen place (or ally)
            Vector2 me = ToScreen(carrier.transform.position);
            if (SelectedAlly != null)
            {
                Vector2 ap = ToScreen(SelectedAlly.position);
                DottedLine(me, ap, new Color(0.55f, 0.8f, 1f, 0.8f), 8f * s);
                float pulse = 1f + 0.15f * Mathf.Sin(Time.unscaledTime * 6f);
                Draw(ring, ap, 30f * s * pulse, new Color(0.55f, 0.85f, 1f, 1f));
            }
            else if (Selected != null)
            {
                Vector2 sp = ScreenPosOf(Selected);
                DottedLine(me, sp, new Color(0.55f, 0.8f, 1f, 0.8f), 8f * s);
                float pulse = 1f + 0.15f * Mathf.Sin(Time.unscaledTime * 6f);
                Draw(ring, sp, 34f * s * pulse, new Color(0.55f, 0.85f, 1f, 1f));
            }
            if (Hover != null && Hover != Selected) Draw(ring, ScreenPosOf(Hover), 30f * s, new Color(1f, 1f, 1f, 0.6f));
            if (OnMap(me, 0f))
            {
                var m = GUI.matrix;
                GUIUtility.RotateAroundPivot(carrier.Heading, me);
                Draw(arrow, me, 22f * s, new Color(0.5f, 1f, 0.6f, 1f));
                GUI.matrix = m;
                GUI.Label(new Rect(me.x + 12f * s, me.y + 4f * s, 200f * s, 24f * s), "<color=#7ee08a>Frigate</color>", label);
            }
            // the other ships of the player's side, each with its heading and name
            allyLabels.Clear();
            for (int i = 0; i < Allies.Count; i++)
            {
                var a = Allies[i];
                if (a.ship == null) continue;
                Vector2 ap = ToScreen(a.ship.position);
                if (!OnMap(ap, 0f)) continue;
                Vector3 f = a.ship.forward;
                var gm = GUI.matrix;
                GUIUtility.RotateAroundPivot(Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg, ap);
                Draw(arrow, ap, 16f * s, a.color);
                GUI.matrix = gm;
                // (stacked under the frigate's own label when it is at the same spot)
                float below = Vector2.Distance(ap, me) < 14f * s ? 18f * s * (i + 1) : 0f;
                string text = $"<color=#{ColorUtility.ToHtmlStringRGB(a.color)}>{a.name}</color>  <color=#9aa4b0>{a.kind}{(a.noWarp != null && a.kind != "fleet" ? ", " + a.noWarp : "")}</color>";
                var st = Hud.Text(13);
                var lr = new Rect(ap.x + 10f * s, ap.y + 2f * s + below, 320f * s, 22f * s);
                GUI.Label(lr, text, st);
                // (clickable where the name is: in formation, the arrow is under the frigate's)
                float tw = st.CalcSize(new GUIContent(text)).x;
                allyLabels.Add((new Rect(lr.x - 4f * s, lr.y, Mathf.Min(lr.width, tw) + 8f * s, lr.height), a.ship));
                if (a.ship == SelectedAlly) Hud.Outline(allyLabels[allyLabels.Count - 1].r, Mathf.Max(1f, s), new Color(a.color.r, a.color.g, a.color.b, 0.8f));
            }
            // scale bar
            float barM = ScaleBar(mapRect.width * 0.2f / pxPerM);
            Hud.Fill(new Rect(mapRect.x + 20f * s, mapRect.yMax - 26f * s, barM * pxPerM, 2f * s), new Color(0.7f, 0.8f, 0.9f, 0.7f));
            GUI.Label(new Rect(mapRect.x + 20f * s, mapRect.yMax - 50f * s, mapRect.width - 40f * s, 24f * s),
                      $"{(barM >= 1e6f ? (barM / 1e9f >= 1f ? $"{barM / 1e9f:0.#} million km" : $"{barM / 1000f:N0} km") : $"{barM / 1000f:N0} km")}" +
                      (localPlanet != null ? "" : zoom > 1.01f ? $"   <color=#9aa4b0>zoom {zoom:0.#}x   Home: whole system</color>" : ""), label);

            Info(s);
        }

        /// <summary>The local map's planet and moons (at their size, no smaller than the system
        /// map draws them), the moons' orbits, every point of interest round them, and their names.</summary>
        void LocalBodies(float s, GUIStyle label)
        {
            if (localOrder.Count == 0) RefreshLocal(false);
            Vector2 c = ScreenPosOf(localPlanet);
            foreach (var w in localOrder)
                if (w.kind == WarpPoint.Kind.Moon) DottedCircle(c, Vector2.Distance(ScreenPosOf(w), c), new Color(0.6f, 0.7f, 0.85f, 0.3f));
            foreach (var w in localOrder)
            {
                Vector2 p = ScreenPosOf(w);
                if (!OnMap(p, 40f * s)) continue;
                if (w == localPlanet || w.kind == WarpPoint.Kind.Moon) Draw(disc, p, BodyPx(w), w.mapColor);
                // (a landmark: a ring with a dot, so it shows on its body's disc, which is its colour)
                else if (w.kind == WarpPoint.Kind.Landmark)
                {
                    Draw(ring, p, 14f * s, new Color(0.8f, 0.92f, 1f, 0.95f));
                    Draw(disc, p, 4f * s, new Color(0.8f, 0.92f, 1f, 0.95f));
                }
                else Structure(p, w, s);
                if (w.Threatened) Draw(ring, p, (w.kind == WarpPoint.Kind.Moon || w == localPlanet ? BodyPx(w) + 10f * s : 22f * s), new Color(1f, 0.35f, 0.3f, 0.9f));
            }
            // names: the chosen one, the one under the pointer and where the ship is first, then
            // the planet and the moons, then the rest; one that would overlap another is left off
            placed.Clear();
            hereNow = localOrder.FirstOrDefault(IsHere);
            ranked.Clear();
            ranked.AddRange(localOrder);
            ranked.Sort((a, b) => LabelRank(a).CompareTo(LabelRank(b)));
            foreach (var w in ranked)
            {
                Vector2 p = ScreenPosOf(w);
                bool here = w == hereNow;
                string name = w.displayName + (here ? "  <color=#7ee08a>(here)</color>" : "") + (w.Threatened ? "  <color=#ff6050>!</color>" : "");
                var content = new GUIContent(name);
                Vector2 size = label.CalcSize(content);
                // (beside a body's disc, not on it)
                float off = (w == localPlanet || w.kind == WarpPoint.Kind.Moon ? BodyPx(w) * 0.5f : 0f) + 10f * s;
                var r = new Rect(p.x + off, p.y - 9f * s, size.x, size.y);
                bool must = w == Selected || w == Hover || here;
                if (!OnMap(p, 0f) || !Inside(r)) continue;
                if (!must && Overlaps(r)) continue;
                placed.Add(r);
                GUI.Label(new Rect(r.x, r.y, 300f * s, 24f * s), content, label);
            }
        }

        /// <summary>The system map's orbits, bodies, fields and what is built, and their names.</summary>
        void SystemBodies(float s, GUIStyle label)
        {
            var star = order.FirstOrDefault(w => w.kind == WarpPoint.Kind.Star);
            Vector2 starPx = star != null ? ScreenPosOf(star) : center;
            // orbits: planets and fields round the star, moons round their planet (a comet's
            // long, tilted orbit isn't drawn)
            foreach (var w in order)
            {
                if (w.kind == WarpPoint.Kind.Star || w.kind == WarpPoint.Kind.Comet || w.Built) continue;
                Vector2 c = w.orbitParent != null ? ScreenPosOf(w.orbitParent) : starPx;
                float r = Vector2.Distance(ScreenPosOf(w), c);
                DottedCircle(c, r, w.orbitParent != null ? new Color(0.6f, 0.7f, 0.85f, 0.25f) : new Color(0.6f, 0.7f, 0.85f, 0.35f));
            }
            // the comets' orbits, and the Lagrange points
            var orbits = OrbitalSystem.Instance;
            if (orbits != null && Event.current.type == EventType.Repaint)
            {
                Vector3 sp = orbits.StarPosition;
                var small = Hud.Text(12);
                foreach (var o in orbits.Orbits)
                {
                    if (o == null) continue;
                    if (o.kind == Orbit.Kind.Kepler && o.parent == null && o.eccentricity > 0.01)
                    {
                        Vector2 last = ToScreen(sp + o.EllipsePoint(0.0));
                        for (int k = 1; k <= 140; k++)
                        {
                            Vector2 q = ToScreen(sp + o.EllipsePoint(k * System.Math.PI * 2.0 / 140.0));
                            DottedLine(last, q, new Color(0.55f, 0.85f, 1f, 0.3f), 9f * s, 2f * s);
                            last = q;
                        }
                    }
                    if (o.lagrangeOnMap)
                        for (int n = 1; n <= 5; n++)
                        {
                            Vector2 q = ToScreen(orbits.LagrangePoint(o, n));
                            if (!OnMap(q, 0f)) continue;
                            var c = n >= 4 ? new Color(0.78f, 0.72f, 1f, 0.85f) : new Color(0.78f, 0.72f, 1f, 0.55f);
                            Hud.Fill(new Rect(q.x - 5f * s, q.y - 0.75f * s, 10f * s, 1.5f * s), c);
                            Hud.Fill(new Rect(q.x - 0.75f * s, q.y - 5f * s, 1.5f * s, 10f * s), c);
                            if (Inside(new Rect(q.x + 4f * s, q.y + 1f * s, 24f * s, 16f * s)))
                                GUI.Label(new Rect(q.x + 4f * s, q.y + 1f * s, 40f * s, 16f * s), $"<color=#b8b0f0>L{n}</color>", small);
                        }
                }
            }
            // the bodies and fields, then what is built
            foreach (var w in order)
            {
                Vector2 p = ScreenPosOf(w);
                float big = w.kind == WarpPoint.Kind.AsteroidField ? Mathf.Max(10f * s, w.bodyRadius * pxPerM) : 40f * s;
                if (!OnMap(p, big)) continue;
                if (w.Built && !OnMap(p, -12f * s)) continue;
                if (w.Built) Structure(p, w, s);
                else if (w.kind == WarpPoint.Kind.AsteroidField) Field(p, w, s);
                else if (w.kind == WarpPoint.Kind.Comet)
                {
                    // a bright head with its tail streaming away from the star
                    Vector2 away = (p - starPx).sqrMagnitude > 1f ? (p - starPx).normalized : Vector2.right;
                    DottedLine(p, p + away * 34f * s, new Color(w.mapColor.r, w.mapColor.g, w.mapColor.b, 0.7f), 3f * s);
                    Draw(disc, p, 8f * s, w.mapColor);
                }
                else
                {
                    float size = (w.kind == WarpPoint.Kind.Star ? 30f : w.kind == WarpPoint.Kind.Moon ? 7f
                                 : Mathf.Lerp(10f, 22f, Mathf.InverseLerp(3000f, 16000f, w.bodyRadius))) * s;
                    if (w.kind == WarpPoint.Kind.Star) Draw(disc, p, size * 2.2f, new Color(w.mapColor.r, w.mapColor.g, w.mapColor.b, 0.18f));
                    Draw(disc, p, size, w.mapColor);
                }
                if (w.Threatened) Draw(ring, p, (w.Built ? 20f : 30f) * s, new Color(1f, 0.35f, 0.3f, 0.9f));
            }
            // names: the chosen place, the one under the mouse and the one the frigate is at
            // first, then the bodies, then what is built; one that would overlap another is left off
            placed.Clear();
            hereNow = order.FirstOrDefault(IsHere);
            // (at a moon drawn on its planet: the planet's name says so)
            var hereMoon = hereNow != null && PassedOver(hereNow) ? hereNow : null;
            ranked.Clear();
            ranked.AddRange(order);
            ranked.Sort((a, b) => LabelRank(a).CompareTo(LabelRank(b)));
            foreach (var w in ranked)
            {
                // (a moon drawn on its planet has no name of its own, unless it is the one chosen)
                if (w != Selected && PassedOver(w)) continue;
                Vector2 p = ScreenPosOf(w);
                bool here = w == hereNow || (hereMoon != null && w == hereMoon.orbitParent);
                string where = w == hereNow ? "(here)" : $"(at {hereMoon?.displayName})";
                string name = w.displayName + (here ? $"  <color=#7ee08a>{where}</color>" : "") + (w.Threatened ? "  <color=#ff6050>!</color>" : "");
                var content = new GUIContent(name);
                Vector2 size = label.CalcSize(content);
                var r = new Rect(p.x + 12f * s, p.y - 9f * s, size.x, size.y);
                bool must = w == Selected || w == Hover || here;
                if (!OnMap(p, 0f) || !Inside(r)) continue;
                if (!must && Overlaps(r)) continue;
                placed.Add(r);
                GUI.Label(new Rect(r.x, r.y, 300f * s, 24f * s), content, label);
            }
        }

        /// <summary>A round length (1, 2 or 5 times a power of ten metres) at most `most`.</summary>
        public static float ScaleBar(float most)
        {
            if (most <= 1f) return 1f;
            float p = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(most)));
            return most >= 5f * p ? 5f * p : most >= 2f * p ? 2f * p : p;
        }

        readonly List<Rect> placed = new List<Rect>();
        readonly List<WarpPoint> ranked = new List<WarpPoint>();
        WarpPoint hereNow;

        int LabelRank(WarpPoint w) => w == Selected ? 0 : w == Hover ? 1 : w == hereNow ? 2 : Places.IsBody(w) ? 3 : !w.Built ? 4 : 5;

        bool Overlaps(Rect r)
        {
            foreach (var q in placed) if (q.Overlaps(r)) return true;
            return false;
        }

        /// <summary>A built place: a small square (a diamond for a base on a surface), with a
        /// dark edge so it shows against an orbit or a planet's disc.</summary>
        void Structure(Vector2 p, WarpPoint w, float s)
        {
            if (Event.current.type != EventType.Repaint) return;
            float k = (w.kind == WarpPoint.Kind.Station ? 11f : 8f) * s;
            var m = GUI.matrix;
            if (w.kind == WarpPoint.Kind.SurfaceBase || w.kind == WarpPoint.Kind.Lagrange) GUIUtility.RotateAroundPivot(45f, p);
            var outer = new Rect(p.x - k * 0.5f - 1.5f * s, p.y - k * 0.5f - 1.5f * s, k + 3f * s, k + 3f * s);
            Hud.Fill(outer, new Color(0f, 0.02f, 0.05f, 0.9f));
            Hud.Fill(new Rect(p.x - k * 0.5f, p.y - k * 0.5f, k, k), w.mapColor);
            GUI.matrix = m;
        }

        /// <summary>How a place goes round (null for the star).</summary>
        public static string OrbitText(WarpPoint w)
        {
            var o = OrbitalSystem.OrbitOf(w.transform);
            if (o == null) return null;
            string Min(double sec) => sec >= 5400.0 ? $"{sec / 3600.0:F1} h" : $"{sec / 60.0:F0} min";
            if (o.kind == Orbit.Kind.Lagrange && o.secondary != null)
                return $"Goes round with {o.secondary.name.Replace(" (orbit)", "")}, every {Min(o.Period)}";
            string round = o.parent != null ? o.parent.name.Replace(" (orbit)", "") : StarName;
            string orbitName = o.name.Replace(" (orbit)", "");
            bool own = w.transform == o.transform || w.transform.parent == o.transform;
            if (!own && w.kind == WarpPoint.Kind.Station) return $"Geostationary: always over the same place on {orbitName}";
            if (!own) return $"On {orbitName}, which orbits {round} every {Min(o.Period)}";
            string what = o.eccentricity > 0.01 ? $"Orbits {StarName} on a long ellipse" : $"Orbits {round}";
            return $"{what} every {Min(o.Period)}";
        }

        /// <summary>What the info panel calls a place.</summary>
        public static string KindText(WarpPoint w)
        {
            // (a landmark, chosen on the local map: where it is, and what it is)
            if (w.kind == WarpPoint.Kind.Landmark && w.follow != null) return $"At {w.follow.displayName}: {(string.IsNullOrEmpty(w.kindLabel) ? "landmark" : w.kindLabel)}";
            if (!string.IsNullOrEmpty(w.kindLabel)) return w.kindLabel;
            return w.kind switch
            {
                WarpPoint.Kind.Star => "Star",
                WarpPoint.Kind.Moon => $"Moon of {(w.orbitParent ? w.orbitParent.displayName : "?")}",
                WarpPoint.Kind.AsteroidField => "Asteroid field",
                WarpPoint.Kind.Comet => "Comet",
                WarpPoint.Kind.Station => "Space station",
                WarpPoint.Kind.Installation => "Installation",
                WarpPoint.Kind.Weapons => "Orbital weapons battery",
                WarpPoint.Kind.AsteroidBase => "Asteroid base",
                WarpPoint.Kind.SurfaceBase => $"Base on {(w.landsOn ? w.landsOn.displayName : "?")}",
                WarpPoint.Kind.Lagrange => "Lagrange point installation",
                _ => "Planet",
            };
        }

        void Info(float s)
        {
            float x = infoRect.x + 20f * s, y = infoRect.y + 18f * s, w = infoRect.width - 40f * s;
            // (a copy: Hud.Text's styles are shared)
            var body = Wrapped(ref bodyStyle, 17);
            var ally = SelectedAlly != null ? AllyOf(SelectedAlly) : null;
            if (ally != null)
            {
                var a = ally.Value;
                GUI.Label(new Rect(x, y, w, 34f * s), $"<b>{a.name}</b>", Hud.Text(26));
                y += 36f * s;
                GUI.Label(new Rect(x, y, w, 24f * s), $"<color=#9aa4b0>{(a.kind == "player" ? "Another player's ship" : a.kind == "AI wingman" ? "An AI wingman" : "A ship of your fleet")}</color>", body);
                y += 34f * s;
                float km = Vector3.Distance(carrier.transform.position, a.ship.position) / 1000f;
                GUI.Label(new Rect(x, y, w, 60f * s),
                    a.noWarp != null ? $"Distance  <b>{km:F0} km</b>\n<color=#9aa4b0>No warp: {a.noWarp}</color>"
                                     : $"Distance  <b>{km:F0} km</b>\nMini-warp time  <b>{drive.ChargeTime + drive.TravelTimeTo(a.ship):F1} s</b>  <color=#9aa4b0>(drops out close by)</color>", body);
            }
            else if (Selected != null)
            {
                GUI.Label(new Rect(x, y, w, 34f * s), $"<b>{Selected.displayName}</b>", Hud.Text(26));
                y += 36f * s;
                string kind = KindText(Selected);
                GUI.Label(new Rect(x, y, w, 24f * s), $"<color=#9aa4b0>{kind}</color>", body);
                y += 30f * s;
                string orbitText = OrbitText(Selected);
                if (orbitText != null)
                {
                    var oc = new GUIContent($"<color=#9aa4b0>{orbitText}</color>");
                    float oh = body.CalcHeight(oc, w);
                    GUI.Label(new Rect(x, y, w, oh), oc, body);
                    y += oh + 8f * s;
                }
                string there = AroundText(Selected);
                var desc = new GUIContent(Selected.description + (Selected.Threatened ? "\n<color=#ff6050>Hostile contacts reported.</color>" : "") + there);
                float h = body.CalcHeight(desc, w);
                GUI.Label(new Rect(x, y, w, h), desc, body);
                y += h + 18f * s;
                float km = drive.DistanceTo(Selected) / 1000f;
                bool here = IsHere(Selected);
                GUI.Label(new Rect(x, y, w, 60f * s),
                    here ? "<color=#7ee08a>You are here</color>"
                         : CanFlyTo(Selected) ? $"Distance  <b>{km:F0} km</b>\n<color=#9aa4b0>Here at {HerePlace.displayName}: FLY TO flies there, in real time</color>"
                         : $"Distance  <b>{km:F0} km</b>\nWarp time  <b>{drive.ChargeTime + drive.TravelTimeTo(Selected):F1} s</b>  <color=#9aa4b0>(charge {drive.ChargeTime:F0} s + trip {drive.TravelTimeTo(Selected):F1} s)</color>", body);
            }
            bool ok = drive.CanWarp(out string why);
            string status = ok ? "<color=#7ee08a>Warp drive ready</color>" : $"<color=#ff6050>Warp drive locked: {why}</color>";
            GUI.Label(new Rect(x, warpButton.y - 40f * s, w, 30f * s), status, body);
            // (a point of interest here is flown to, not warped to: the drive isn't needed)
            bool fly = ally == null && CanFlyTo(Selected);
            bool usable = fly || (ok && (ally != null ? ally.Value.noWarp == null : Selected != null && !IsHere(Selected)));
            bool two = localButton.width > 0f;
            string go = fly ? "FLY TO" : "WARP";
            Hud.Fill(warpButton, usable ? new Color(0.2f, 0.45f, 0.8f, 0.9f) : new Color(0.25f, 0.28f, 0.32f, 0.8f));
            Hud.Outline(warpButton, Mathf.Max(1f, 2f * s), usable ? new Color(0.6f, 0.85f, 1f) : new Color(0.5f, 0.55f, 0.6f));
            GUI.Label(warpButton, usable ? $"<b>{go}</b>" : $"<color=#9aa4b0><b>{go}</b></color>", Hud.Text(two ? 20 : 24, true, TextAnchor.MiddleCenter));
            if (two)
            {
                Hud.Fill(localButton, new Color(0.18f, 0.36f, 0.48f, 0.9f));
                Hud.Outline(localButton, Mathf.Max(1f, 2f * s), new Color(0.6f, 0.85f, 1f));
                GUI.Label(localButton, localPlanet != null ? "<b>BACK</b>" : "<b>LOCAL MAP</b>", Hud.Text(20, true, TextAnchor.MiddleCenter));
            }
            bool pad = PadSupport.UsingPad && Gamepad.current != null;
            string help = localPlanet != null
                ? (pad ? "Stick / D-pad: choose   LB/RB: step\nA: warp, or fly to a point here\nY or B: the system map   R3: close"
                       : "Click, arrows or WASD: choose   Tab: step\nWARP / FLY TO, double-click or Enter: go\nL, Esc or BACK: the system map   M: close")
                : (pad ? "Stick / D-pad: choose   LB/RB: step\nA: warp   B or R3: close   Y: local map\nRight stick: move   LT/RT: zoom"
                       : "Click, arrows or WASD: choose   Tab: step\nWARP, double-click or Enter: warp   M/Esc: close\nWheel or +/-: zoom   drag or Shift+arrows: move\nHome: the whole system   L: the local map");
            var helpStyle = Wrapped(ref helpTextStyle, 13);
            GUI.Label(new Rect(x, warpButton.yMax + 10f * s, w, Mathf.Max(20f * s, Screen.height - warpButton.yMax - 14f * s)), $"<color=#9aa4b0>{help}</color>", helpStyle);
        }

        // what is round the place chosen, as the info panel puts it (worked out once a frame:
        // OnGUI runs several times a frame)
        string aroundText = "";
        WarpPoint aroundOf;
        int aroundFrame = -1;

        /// <summary>The info panel's lines on what is round a place (Places.Around): a planet's
        /// moons, then its own points of interest, then each moon's. A name that starts with its
        /// place's is shortened under it ("Selene High Orbit" is "High Orbit", at Selene).</summary>
        string AroundText(WarpPoint place)
        {
            if (place == aroundOf && aroundFrame == Time.frameCount) return aroundText;
            aroundOf = place;
            aroundFrame = Time.frameCount;
            aroundText = "";
            var around = Places.Around(place);
            if (around.Count == 0) return aroundText;
            string Name(WarpPoint p, WarpPoint at)
            {
                string n = at != null && p.displayName.StartsWith(at.displayName + " ") ? p.displayName.Substring(at.displayName.Length + 1) : p.displayName;
                return n + (p.Threatened ? " (hostile)" : "");
            }
            var moons = around.Where(p => p.kind == WarpPoint.Kind.Moon && p.orbitParent == place).ToList();
            var own = around.Where(p => !moons.Contains(p) && Places.PlaceOf(p) == place).ToList();
            var sb = new System.Text.StringBuilder();
            if (moons.Count > 0) sb.Append($"\nMoons: {string.Join(", ", moons.Select(m => Name(m, null)))}");
            if (own.Count > 0) sb.Append($"\nPoints of interest: {string.Join(", ", own.Select(p => Name(p, place)))}");
            foreach (var m in moons)
            {
                var atMoon = around.Where(p => Places.PlaceOf(p) == m).ToList();
                if (atMoon.Count > 0) sb.Append($"\nAt {m.displayName}: {string.Join(", ", atMoon.Select(p => Name(p, m)))}");
            }
            aroundText = $"<color=#9aa4b0>{sb}</color>";
            return aroundText;
        }

        GUIStyle bodyStyle, helpTextStyle;

        /// <summary>A wrapping copy of Hud.Text's style (whose styles are shared), made again only
        /// when the text size changes.</summary>
        static GUIStyle Wrapped(ref GUIStyle kept, int basePx)
        {
            var src = Hud.Text(basePx);
            if (kept == null || kept.fontSize != src.fontSize) kept = new GUIStyle(src) { wordWrap = true };
            return kept;
        }

        void Field(Vector2 p, WarpPoint w, float s)
        {
            // a scatter of specks, the same every frame
            var rnd = new System.Random(w.displayName.GetHashCode());
            float r = Mathf.Max(10f * s, w.bodyRadius * pxPerM);
            for (int i = 0; i < 26; i++)
            {
                float a = (float)rnd.NextDouble() * Mathf.PI * 2f, d = Mathf.Sqrt((float)rnd.NextDouble()) * r;
                Draw(disc, p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d, (2f + (float)rnd.NextDouble() * 3f) * s, w.mapColor);
            }
        }

        void Draw(Texture2D t, Vector2 c, float size, Color col)
        {
            if (Event.current.type != EventType.Repaint || !OnMap(c, -size * 0.5f)) return;
            var old = GUI.color;
            GUI.color = col;
            GUI.DrawTexture(new Rect(c.x - size * 0.5f, c.y - size * 0.5f, size, size), t);
            GUI.color = old;
        }

        /// <summary>A dotted circle, only the part of it on the map: a dot every few pixels, so
        /// zoomed in, a huge orbit is a handful of dots across the view, not thousands.</summary>
        void DottedCircle(Vector2 c, float r, Color col)
        {
            if (Event.current.type != EventType.Repaint || r < 2f) return;
            float s = Mathf.Max(1.5f, 2f * Hud.S);
            float half = mapRect.width * 0.75f;                     // (the square's half-diagonal, and a bit)
            float d = Vector2.Distance(c, mapRect.center);
            if (d - r > half || r - d > half) return;              // (all off the map, or the map all inside it)
            float spacing = 7f * Hud.S;
            float from = 0f, span = Mathf.PI * 2f;
            if (r > half)
            {
                // (the arc that can be on the map: round the direction of the map's middle)
                float toMid = Mathf.Atan2(mapRect.center.y - c.y, mapRect.center.x - c.x);
                float a = Mathf.Asin(Mathf.Clamp01(half / r)) * 1.3f;
                from = toMid - a;
                span = a * 2f;
            }
            int dots = Mathf.Clamp(Mathf.CeilToInt(span * r / spacing), 12, 3000);
            for (int i = 0; i < dots; i++)
            {
                float a = from + i * span / dots;
                var p = new Vector2(c.x + Mathf.Cos(a) * r, c.y + Mathf.Sin(a) * r);
                if (!OnMap(p, 0f)) continue;
                Hud.Fill(new Rect(p.x - s * 0.5f, p.y - s * 0.5f, s, s), col);
            }
        }

        /// <summary>A dotted line, the part of it on the map (a dot every `spacing` pixels).</summary>
        void DottedLine(Vector2 a, Vector2 b, Color col, float spacing, float dot = 0f)
        {
            if (Event.current.type != EventType.Repaint) return;
            // (clipped to the map's square first: zoomed in, a course can be a million pixels long)
            float t0 = 0f, t1 = 1f;
            Vector2 d = b - a;
            bool Clip(float p, float q)
            {
                if (Mathf.Abs(p) < 1e-6f) return q >= 0f;
                float r = q / p;
                if (p < 0f) { if (r > t1) return false; if (r > t0) t0 = r; }
                else { if (r < t0) return false; if (r < t1) t1 = r; }
                return true;
            }
            if (!Clip(-d.x, a.x - mapRect.xMin) || !Clip(d.x, mapRect.xMax - a.x) || !Clip(-d.y, a.y - mapRect.yMin) || !Clip(d.y, mapRect.yMax - a.y)) return;
            Vector2 from = a + d * t0, to = a + d * t1;
            float len = Vector2.Distance(from, to);
            int n = Mathf.Min(400, Mathf.FloorToInt(len / spacing));
            float s = dot > 0f ? dot : Mathf.Max(2f, 3f * Hud.S);
            // (the first dot only where the line really starts: not at the map's edge)
            for (int i = t0 > 0f ? 0 : 1; i < n; i++)
            {
                Vector2 p = Vector2.Lerp(from, to, i / (float)Mathf.Max(1, n));
                Hud.Fill(new Rect(p.x - s * 0.5f, p.y - s * 0.5f, s, s), col);
            }
        }

        static Texture2D MakeDisc(int n, bool ringOnly)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color[n * n];
            float c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                    float a = ringOnly ? Mathf.Clamp01(1f - Mathf.Abs(d - 0.85f) / 0.08f) : Mathf.Clamp01((1f - d) / 0.08f);
                    px[y * n + x] = new Color(1f, 1f, 1f, a);
                }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        static Texture2D MakeArrow(int n)
        {
            // an arrowhead pointing up the texture (north on the map before rotation)
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color[n * n];
            for (int ty = 0; ty < n; ty++)          // texture rows run bottom to top as drawn
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2f - 1f, v = (ty + 0.5f) / n;
                    float half = 0.8f * (1f - v);                                  // the tip at the top
                    bool notch = v < 0.32f && Mathf.Abs(u) < 0.45f * (0.32f - v) / 0.32f + 0.05f;
                    bool inside = Mathf.Abs(u) < half && v > 0.04f && !notch;
                    px[ty * n + x] = new Color(1f, 1f, 1f, inside ? 1f : 0f);
                }
            t.SetPixels(px);
            t.Apply();
            return t;
        }
    }
}

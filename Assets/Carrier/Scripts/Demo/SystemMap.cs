using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The star system map: a top-down chart of every place the frigate can mini-warp to (the
    /// star, the planets and their moons, the asteroid fields, the comets, and what has been
    /// built: stations, installations, weapons batteries, bases), with the bodies' orbits, the
    /// frigate's own position and heading, and for the chosen destination its distance and how
    /// long the trip will take. Built places are small squares; a name that would run into
    /// another is left off until the place is chosen. Pick a destination and warp from here.
    ///
    /// Keyboard and mouse: M opens and closes it; click a place (or use the arrow keys / WASD,
    /// Tab to step through), then click WARP, double-click, or press Enter. Esc closes it.
    /// Gamepad: R3 (press the right stick) opens it; the left stick or D-pad moves between places,
    /// LB/RB step through them, A warps, B closes.
    /// </summary>
    public class SystemMap : MonoBehaviour
    {
        public CarrierController carrier;
        public WarpDrive drive;
        public string systemName = "Kestrel System";

        public static bool IsOpen { get; private set; }
        static int closedFrame = -10;
        /// <summary>The map closed this frame (so Esc doesn't also quit the game).</summary>
        public static bool JustClosed => Time.frameCount - closedFrame <= 1;

        public WarpPoint Selected { get; private set; }
        public WarpPoint Hover { get; private set; }

        Rect mapRect, infoRect, warpButton;
        float pxPerM = 1f;
        Vector2 center;
        Texture2D disc, ring, arrow;
        float extent = 1f, lastClick = -1f, stickRepeat;
        WarpPoint lastClicked;
        List<WarpPoint> order = new List<WarpPoint>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { IsOpen = false; closedFrame = -10; }

        void Start()
        {
            if (carrier == null) carrier = FindAnyObjectByType<CarrierController>();
            if (drive == null && carrier != null) drive = carrier.GetComponent<WarpDrive>();
            disc = MakeDisc(64, false);
            ring = MakeDisc(64, true);
            arrow = MakeArrow(48);
        }

        // ------------------------------------------------------------ opening, choosing, warping

        public void Open()
        {
            if (drive == null) return;
            IsOpen = true;
            RefreshOrder();
            if (Selected == null || IsHere(Selected))
                Selected = order.Where(p => !IsHere(p)).OrderBy(p => Vector3.Distance(p.Position, carrier.transform.position)).FirstOrDefault();
            SoundFX.Ui(Sfx.UiLock);
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            closedFrame = Time.frameCount;
            SoundFX.Ui(Sfx.UiClear);
        }

        public void Toggle() { if (IsOpen) Close(); else Open(); }

        public void Select(WarpPoint p)
        {
            if (p == null || p == Selected) return;
            Selected = p;
            SoundFX.Ui(Sfx.UiLock);
        }

        /// <summary>Warp to the chosen place, if the drive allows it.</summary>
        public bool WarpToSelected()
        {
            if (Selected == null || drive == null) return false;
            if (IsHere(Selected))
            {
                SoundFX.Ui(Sfx.UiError);
                CarrierDemoInput.Toast($"Already at {Selected.displayName}");
                return false;
            }
            if (drive.Engage(Selected, out string why))
            {
                CarrierDemoInput.Toast($"Warping to {Selected.displayName}: {drive.chargeTime + drive.TravelTime:F1} s");
                Close();
                return true;
            }
            SoundFX.Ui(Sfx.UiError);
            CarrierDemoInput.Toast("Can't warp: " + why);
            return false;
        }

        bool IsHere(WarpPoint p) => carrier != null && p != null && p == WarpPoint.Nearest(carrier.transform.position)
                                    && Vector3.Distance(carrier.transform.position, p.Position) < p.arrivalDistance * 1.6f;

        void RefreshOrder()
        {
            // a sensible reading order: the star, then outward by distance from it, moons after their planet
            Vector3 star = WarpPoint.All.FirstOrDefault(w => w.kind == WarpPoint.Kind.Star)?.Position ?? Vector3.zero;
            float Key(WarpPoint w) => Vector3.Distance(star, (w.orbitParent != null ? w.orbitParent : w).Position) * 1000f
                                      + (w.orbitParent != null ? Vector3.Distance(w.Position, w.orbitParent.Position) / 1000f : 0f);
            order = WarpPoint.All.Where(w => w != null).OrderBy(Key).ToList();
            extent = 1f;
            foreach (var w in order) extent = Mathf.Max(extent, Flat(w.Position - star).magnitude + w.arrivalDistance);
        }

        public void Step(int dir)
        {
            if (order.Count == 0) RefreshOrder();
            int i = Selected != null ? order.IndexOf(Selected) : -1;
            i = (i + dir + order.Count) % order.Count;
            Select(order[i]);
        }

        /// <summary>Move the choice to the nearest place lying in a direction on the map (GUI space, y down).</summary>
        public void StepToward(Vector2 dir)
        {
            if (Selected == null) { Step(1); return; }
            dir.Normalize();
            Vector2 from = ScreenPosOf(Selected);
            WarpPoint best = null;
            float bestScore = float.MaxValue;
            foreach (var w in order)
            {
                if (w == Selected) continue;
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
                if (PlanetLanding.Busy) { if (PlanetLanding.OnSurface) CarrierDemoInput.Toast("Take off before using the system map"); return; }
                Open();
                return;
            }
            Layout();

            if (k != null)
            {
                if (k.mKey.wasPressedThisFrame || k.escapeKey.wasPressedThisFrame) { Close(); return; }
                if (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame) { WarpToSelected(); return; }
                if (k.tabKey.wasPressedThisFrame) Step(k.shiftKey.isPressed ? -1 : 1);
                if (k.leftArrowKey.wasPressedThisFrame || k.aKey.wasPressedThisFrame) StepToward(Vector2.left);
                if (k.rightArrowKey.wasPressedThisFrame || k.dKey.wasPressedThisFrame) StepToward(Vector2.right);
                if (k.upArrowKey.wasPressedThisFrame || k.wKey.wasPressedThisFrame) StepToward(Vector2.down);
                if (k.downArrowKey.wasPressedThisFrame || k.sKey.wasPressedThisFrame) StepToward(Vector2.up);
            }
            if (pad != null)
            {
                if (pad.buttonEast.wasPressedThisFrame || pad.rightStickButton.wasPressedThisFrame) { Close(); return; }
                if (pad.buttonSouth.wasPressedThisFrame) { WarpToSelected(); return; }
                if (pad.rightShoulder.wasPressedThisFrame) Step(1);
                if (pad.leftShoulder.wasPressedThisFrame) Step(-1);
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
                if (mouse.leftButton.wasPressedThisFrame) Click(gui);
            }
        }

        /// <summary>A click at a GUI position: choose a place, or warp (WARP button, or a double click).</summary>
        public void Click(Vector2 gui)
        {
            if (warpButton.Contains(gui)) { WarpToSelected(); return; }
            var p = PointAt(gui);
            if (p == null) return;
            bool twice = p == lastClicked && Time.unscaledTime - lastClick < 0.4f;
            lastClicked = p;
            lastClick = Time.unscaledTime;
            Select(p);
            if (twice) WarpToSelected();
        }

        public WarpPoint PointAt(Vector2 gui, float maxPx = 26f)
        {
            WarpPoint best = null;
            float bestD = maxPx * Hud.S;
            foreach (var w in order)
            {
                float d = Vector2.Distance(ScreenPosOf(w), gui);
                if (d < bestD) { bestD = d; best = w; }
            }
            return best;
        }

        // ------------------------------------------------------------ layout and drawing

        static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);

        public Vector2 ScreenPosOf(WarpPoint p) => ToScreen(p.Position);
        Vector2 ToScreen(Vector3 world) => center + new Vector2(world.x, -world.z) * pxPerM;
        public Rect WarpButtonRect => warpButton;

        void Layout()
        {
            float s = Hud.S;
            float margin = 30f * s;
            float infoW = 340f * s;
            float side = Mathf.Min(Screen.height - margin * 2f, Screen.width - infoW - margin * 3f);
            float x0 = (Screen.width - side - infoW - margin) * 0.5f;
            mapRect = new Rect(x0, (Screen.height - side) * 0.5f, side, side);
            infoRect = new Rect(mapRect.xMax + margin, mapRect.y, infoW, side);
            Vector3 star = WarpPoint.All.FirstOrDefault(w => w != null && w.kind == WarpPoint.Kind.Star)?.Position ?? Vector3.zero;
            pxPerM = side * 0.47f / Mathf.Max(1f, extent);
            center = mapRect.center - new Vector2(star.x, -star.z) * pxPerM;
            warpButton = new Rect(infoRect.x + 20f * s, infoRect.yMax - 120f * s, infoRect.width - 40f * s, 50f * s);
        }

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
                $"<b>{systemName.ToUpper()}</b>   <color=#9aa4b0>system map: mini-warp</color>", Hud.Text(22));

            var star = order.FirstOrDefault(w => w.kind == WarpPoint.Kind.Star);
            Vector2 starPx = star != null ? ScreenPosOf(star) : center;
            // orbits: planets and fields round the star, moons round their planet (a comet's
            // long, tilted orbit isn't drawn)
            foreach (var w in order)
            {
                if (w.kind == WarpPoint.Kind.Star || w.kind == WarpPoint.Kind.Comet || w.Built) continue;
                Vector2 c = w.orbitParent != null ? ScreenPosOf(w.orbitParent) : starPx;
                float r = Vector2.Distance(ScreenPosOf(w), c);
                DottedCircle(c, r, w.orbitParent != null ? new Color(0.6f, 0.7f, 0.85f, 0.25f) : new Color(0.6f, 0.7f, 0.85f, 0.35f),
                             Mathf.Clamp(Mathf.RoundToInt(r * 0.35f), 24, 260));
            }
            // the bodies and fields, then what is built
            var label = Hud.Text(15);
            foreach (var w in order)
            {
                Vector2 p = ScreenPosOf(w);
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
                if (w.hostile) Draw(ring, p, (w.Built ? 20f : 30f) * s, new Color(1f, 0.35f, 0.3f, 0.9f));
            }
            // names: the chosen place, the one under the mouse and the one the frigate is at
            // first, then the bodies, then what is built; one that would overlap another is left off
            placed.Clear();
            hereNow = order.FirstOrDefault(IsHere);
            ranked.Clear();
            ranked.AddRange(order);
            ranked.Sort((a, b) => LabelRank(a).CompareTo(LabelRank(b)));
            foreach (var w in ranked)
            {
                Vector2 p = ScreenPosOf(w);
                bool here = w == hereNow;
                string name = w.displayName + (here ? "  <color=#7ee08a>(here)</color>" : "") + (w.hostile ? "  <color=#ff6050>!</color>" : "");
                var content = new GUIContent(name);
                Vector2 size = label.CalcSize(content);
                var r = new Rect(p.x + 12f * s, p.y - 9f * s, size.x, size.y);
                bool must = w == Selected || w == Hover || here;
                if (!must && Overlaps(r)) continue;
                placed.Add(r);
                GUI.Label(new Rect(r.x, r.y, 300f * s, 24f * s), content, label);
            }
            // the frigate, and the way to the chosen place
            Vector2 me = ToScreen(carrier.transform.position);
            if (Selected != null)
            {
                Vector2 sp = ScreenPosOf(Selected);
                DottedLine(me, sp, new Color(0.55f, 0.8f, 1f, 0.8f), 8f * s);
                float pulse = 1f + 0.15f * Mathf.Sin(Time.unscaledTime * 6f);
                Draw(ring, sp, 34f * s * pulse, new Color(0.55f, 0.85f, 1f, 1f));
            }
            if (Hover != null && Hover != Selected) Draw(ring, ScreenPosOf(Hover), 30f * s, new Color(1f, 1f, 1f, 0.6f));
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(carrier.Heading, me);
            Draw(arrow, me, 22f * s, new Color(0.5f, 1f, 0.6f, 1f));
            GUI.matrix = m;
            GUI.Label(new Rect(me.x + 12f * s, me.y + 4f * s, 200f * s, 24f * s), "<color=#7ee08a>Frigate</color>", label);
            // scale bar
            float km50 = 50000f * pxPerM;
            Hud.Fill(new Rect(mapRect.x + 20f * s, mapRect.yMax - 26f * s, km50, 2f * s), new Color(0.7f, 0.8f, 0.9f, 0.7f));
            GUI.Label(new Rect(mapRect.x + 20f * s, mapRect.yMax - 50f * s, 200f, 24f * s), "50 km", label);

            Info(s);
        }

        readonly List<Rect> placed = new List<Rect>();
        readonly List<WarpPoint> ranked = new List<WarpPoint>();
        WarpPoint hereNow;

        int LabelRank(WarpPoint w) => w == Selected ? 0 : w == Hover ? 1 : w == hereNow ? 2 : !w.Built ? 3 : 4;

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

        /// <summary>What the info panel calls a place.</summary>
        public static string KindText(WarpPoint w)
        {
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
            var body = Hud.Text(17);
            body.wordWrap = true;
            if (Selected != null)
            {
                GUI.Label(new Rect(x, y, w, 34f * s), $"<b>{Selected.displayName}</b>", Hud.Text(26));
                y += 36f * s;
                string kind = KindText(Selected);
                GUI.Label(new Rect(x, y, w, 24f * s), $"<color=#9aa4b0>{kind}</color>", body);
                y += 30f * s;
                var desc = new GUIContent(Selected.description + (Selected.hostile ? "\n<color=#ff6050>Hostile contacts reported.</color>" : ""));
                float h = body.CalcHeight(desc, w);
                GUI.Label(new Rect(x, y, w, h), desc, body);
                y += h + 18f * s;
                float km = drive.DistanceTo(Selected) / 1000f;
                bool here = IsHere(Selected);
                GUI.Label(new Rect(x, y, w, 60f * s),
                    here ? "<color=#7ee08a>You are here</color>"
                         : $"Distance  <b>{km:F0} km</b>\nWarp time  <b>{drive.chargeTime + drive.TravelTimeTo(Selected):F1} s</b>  <color=#9aa4b0>(charge {drive.chargeTime:F0} s + trip {drive.TravelTimeTo(Selected):F1} s)</color>", body);
            }
            bool ok = drive.CanWarp(out string why);
            string status = ok ? "<color=#7ee08a>Warp drive ready</color>" : $"<color=#ff6050>Warp drive locked: {why}</color>";
            GUI.Label(new Rect(x, warpButton.y - 40f * s, w, 30f * s), status, body);
            bool usable = ok && Selected != null && !IsHere(Selected);
            Hud.Fill(warpButton, usable ? new Color(0.2f, 0.45f, 0.8f, 0.9f) : new Color(0.25f, 0.28f, 0.32f, 0.8f));
            Hud.Outline(warpButton, Mathf.Max(1f, 2f * s), usable ? new Color(0.6f, 0.85f, 1f) : new Color(0.5f, 0.55f, 0.6f));
            GUI.Label(warpButton, usable ? "<b>WARP</b>" : "<color=#9aa4b0><b>WARP</b></color>", Hud.Text(24, true, TextAnchor.MiddleCenter));
            bool pad = PadSupport.UsingPad && Gamepad.current != null;
            string help = pad ? "Stick / D-pad: choose   LB/RB: step\nA: warp   B or R3: close"
                              : "Click or arrows/WASD: choose   Tab: step\nWARP, double-click or Enter: warp   M/Esc: close";
            GUI.Label(new Rect(x, warpButton.yMax + 12f * s, w, 50f * s), $"<color=#9aa4b0>{help}</color>", Hud.Text(14));
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

        static void Draw(Texture2D t, Vector2 c, float size, Color col)
        {
            if (Event.current.type != EventType.Repaint) return;
            var old = GUI.color;
            GUI.color = col;
            GUI.DrawTexture(new Rect(c.x - size * 0.5f, c.y - size * 0.5f, size, size), t);
            GUI.color = old;
        }

        void DottedCircle(Vector2 c, float r, Color col, int dots)
        {
            if (Event.current.type != EventType.Repaint || r < 2f) return;
            float s = Mathf.Max(1.5f, 2f * Hud.S);
            for (int i = 0; i < dots; i++)
            {
                float a = i * Mathf.PI * 2f / dots;
                Hud.Fill(new Rect(c.x + Mathf.Cos(a) * r - s * 0.5f, c.y + Mathf.Sin(a) * r - s * 0.5f, s, s), col);
            }
        }

        static void DottedLine(Vector2 a, Vector2 b, Color col, float spacing)
        {
            if (Event.current.type != EventType.Repaint) return;
            float len = Vector2.Distance(a, b);
            int n = Mathf.Min(400, Mathf.FloorToInt(len / spacing));
            float s = Mathf.Max(2f, 3f * Hud.S);
            for (int i = 1; i < n; i++)
            {
                Vector2 p = Vector2.Lerp(a, b, i / (float)n);
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

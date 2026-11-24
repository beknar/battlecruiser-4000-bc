using UnityEngine;
using UnityEngine.InputSystem;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The start menu's Customise step: the chosen ship's paint (ShipPaint), shown on the turning
    /// preview as it is chosen. A preset (or the factory finish), or any of the 100 patterns with
    /// its scale and angle; the primary, secondary and trim colours and the lettering's (a palette
    /// of 24, and hue, saturation and value); a logo; the lettering on the hull. Random, Reset (the
    /// paint as it was when the step opened), Back and Done. Done (Enter, or A) keeps the scheme for
    /// that class and launches; from the pause menu (OpenPaint) it paints the ship being flown.
    ///
    /// Mouse: click anything, drag the sliders, click in the lettering to type. Keyboard: up/down
    /// (W/S) a row, left/right (A/D) change it (held: repeats), Q/E the pattern family, T type the
    /// lettering (Enter or up/down to stop), R random, Enter or Space done, Esc back. Pad: D-pad or
    /// left stick, LB/RB the pattern family, Y random, X reset, A done, B back.
    ///
    /// The PLACE page (P, the pad's View button, or its tab) takes the right-hand column instead
    /// of the colours: where the logo or the lettering is on the hull (along it and up it), its
    /// size, its turn and which sides it's on, and "Put back". On the picture then, the left
    /// button drags the one chosen about the hull, and the right button turns the ship.
    /// </summary>
    public partial class StartMenu
    {
        enum Step { Ships, Paint }
        Step step;
        /// <summary>Opened from the pause menu: Done paints the ship being flown, and Back closes.</summary>
        bool paintOnly;
        string paintId;
        GameObject paintPrefab;

        PaintScheme edit = new PaintScheme();
        // the factory finish chosen: the hull's own colours (a logo and lettering may go on them)
        bool factory => edit.pattern == ShipPaint.FactoryPattern;
        /// <summary>The factory finish with nothing on it: kept as no scheme at all.</summary>
        bool PlainFactory => factory && edit.logo < 0 && PaintScheme.SanitizeText(draft).Length == 0;
        PaintScheme opened;           // as it was when the step opened (null: the factory finish)
        int presetIndex = -1;         // the preset last chosen (-1 the factory finish, -2 none)
        string draft = "";            // the lettering as typed (cleaned on the way to the hull)
        int slot;                     // the colour being changed: primary, secondary, trim, lettering
        float hue, sat, val;
        Color32 hsvOf;                // the colour hue/sat/val were taken from
        int hsvSlot = -1;
        int focus;                    // the row the keys change
        bool typing, wantType, wantStopTyping;
        int dragging = -1;
        float holdNext, lastDir;
        readonly System.Random rng = new System.Random();
        readonly Rect[] paintRects = new Rect[Rows];
        int typeFrame = -10;          // T pressed to type: that key isn't typed

        const int RowPreset = 0, RowPattern = 1, RowScale = 2, RowAngle = 3, RowSlot = 4, RowSwatch = 5,
                  RowHue = 6, RowSat = 7, RowVal = 8, RowLogo = 9, RowText = 10,
                  // (the PLACE page's, in the right-hand column instead of the colours)
                  RowDecal = 11, RowAlong = 12, RowUp = 13, RowSize = 14, RowTurn = 15, RowSides = 16, Rows = 17;
        static readonly string[] SideNames = { "Both sides", "Port", "Starboard" };

        /// <summary>The PLACE page is up (where the logo and lettering are), not the colours.</summary>
        bool placing;
        /// <summary>The logo is being placed (else the lettering).</summary>
        bool placeLogo = true;
        bool draggingDecal;

        /// <summary>The PLACE page is showing (for the tests).</summary>
        public bool Placing => Customising && placing;

        // the placement of the one being placed
        ref int Along { get { if (placeLogo) return ref edit.logoAlong; return ref edit.textAlong; } }
        ref int Up { get { if (placeLogo) return ref edit.logoUp; return ref edit.textUp; } }
        ref int Size { get { if (placeLogo) return ref edit.logoSize; return ref edit.textSize; } }
        ref int Turn { get { if (placeLogo) return ref edit.logoTurn; return ref edit.textTurn; } }
        ref int Sides { get { if (placeLogo) return ref edit.logoSides; return ref edit.textSides; } }

        /// <summary>Is a row on the page showing (the left half always; the right, the colours or PLACE)?</summary>
        bool RowShown(int row) => row <= RowAngle || (placing ? row >= RowDecal : row <= RowText);

        /// <summary>The PLACE page up or down (the right-hand column's rows go with it).</summary>
        public void TogglePlacing()
        {
            placing = !placing;
            draggingDecal = false;
            if (!RowShown(focus)) focus = placing ? RowDecal : RowSlot;
            // (the lettering's field isn't drawn on the PLACE page: no longer typed into)
            typing = false;
            GUIUtility.keyboardControl = 0;
            SoundFX.Ui(Sfx.UiClear);
        }

        /// <summary>The one being placed back where the class puts it.</summary>
        void PutBack()
        {
            Along = 0; Up = 0; Size = 100; Turn = 0; Sides = 0;
            Touched(true, decal: true);
            SoundFX.Ui(Sfx.UiClear);
        }

        /// <summary>The one being placed moved to where the pointer is on the preview's hull: the
        /// pointer's ray onto the side of the hull's bounds that faces the camera.</summary>
        void DragDecal(Vector2 gui, Rect pic)
        {
            if (previewModel == null || previewCam == null || !ShipPaint.PaintedBounds(previewModel, out var b)) return;
            var t = previewModel.transform;
            var ray = previewCam.ViewportPointToRay(new Vector3((gui.x - pic.x) / pic.width, 1f - (gui.y - pic.y) / pic.height, 0f));
            Vector3 o = t.InverseTransformPoint(ray.origin), d = t.InverseTransformDirection(ray.direction);
            float x = o.x >= b.center.x ? b.max.x : b.min.x;
            if (Mathf.Abs(d.x) < 1e-5f) return;
            float along = (x - o.x) / d.x;
            if (along <= 0f) return;
            var f = ShipPaint.Fractions(b, o + d * along);
            var place = ShipPaint.PlacementFor(paintId);
            Along = Mathf.Clamp(Mathf.RoundToInt((f.x - (placeLogo ? place.logoZ : place.textZ)) * 100f), -PaintScheme.MaxShift, PaintScheme.MaxShift);
            Up = Mathf.Clamp(Mathf.RoundToInt((f.y - (placeLogo ? place.logoY : place.textY)) * 100f), -PaintScheme.MaxShift, PaintScheme.MaxShift);
            Touched(true, decal: true);
        }
        static readonly string[] SlotNames = { "Primary", "Secondary", "Trim", "Lettering" };
        const string TextControl = "paintLettering";

        /// <summary>The paint shown and kept ("" the factory finish).</summary>
        string EditCode => PlainFactory ? "" : Scheme().ToCode();

        /// <summary>The paint as it is being made (a code; "" the plain factory finish), for the tests.</summary>
        public string PaintCode => EditCode;

        PaintScheme Scheme()
        {
            edit.text = draft;
            return edit.Clamped();
        }

        /// <summary>The Customise step is showing (for the reels and tests).</summary>
        public bool Customising => IsOpen && step == Step.Paint;

        /// <summary>Go on from the ships to their paint (the reels launch straight away).</summary>
        public void Next()
        {
            if (offered.Length == 0) return;
            if (LiveSwap) { Launch(); return; }
            BeginPaint(offered[selected].id, offered[selected].prefab, false);
        }

        /// <summary>From the pause menu: paint the ship being flown.</summary>
        public void OpenPaint()
        {
            Open();
            if (!IsOpen) return;
            string id = PlayerFrigate.Flying;
            var entry = roster != null ? roster.Find(id) : null;
            GameObject prefab = entry != null ? entry.prefab : null;
            if (prefab == null)
            {
                var pf = FindAnyObjectByType<PlayerFrigate>();
                if (pf != null && pf.ship != null) prefab = pf.ship.gameObject;
            }
            if (prefab == null) { Close(); return; }
            BeginPaint(entry != null ? entry.id : id, prefab, true);
        }

        void BeginPaint(string id, GameObject prefab, bool only)
        {
            paintId = id;
            paintPrefab = prefab;
            paintOnly = only;
            var s = ShipPaint.ForPlayer(id);
            opened = s?.Clone();
            SetScheme(s);
            focus = RowPreset;
            slot = 0;
            hsvSlot = -1;
            typing = wantType = false;
            dragging = -1;
            step = Step.Paint;
            ResetTurntable();
            turningPreview = false;
            previewPaint = null;
            openedFrame = Time.frameCount;
            SoundFX.Ui(Sfx.UiClear);
        }

        void SetScheme(PaintScheme s)
        {
            edit = s != null ? s.Clone() : FactoryScheme();
            draft = edit.text ?? "";
            presetIndex = factory ? -1 : -2;
            string code = edit.ToCode();
            for (int i = 0; i < ShipPaint.Presets.Length; i++)
                if (ShipPaint.FromPreset(i, edit).ToCode() == code) presetIndex = i;
            hsvSlot = -1;
        }

        /// <summary>The factory finish, to start from: no logo or lettering, a preset's colours
        /// kept for when a pattern is chosen.</summary>
        static PaintScheme FactoryScheme()
        {
            var f = ShipPaint.FromPreset(0);
            f.pattern = ShipPaint.FactoryPattern;
            f.logo = -1;
            f.text = "";
            return f;
        }

        /// <summary>Something was changed: it is a scheme of the player's own now. A change to the
        /// logo or lettering alone (`decal`) leaves the factory finish the factory finish, with
        /// them on it; anything else paints her (the first pattern, if none was chosen).</summary>
        void Touched(bool keepPreset = false, bool decal = false)
        {
            if (factory && decal) return;
            if (factory) edit.pattern = 0;
            if (!keepPreset) presetIndex = -2;
        }

        void ChoosePreset(int i)
        {
            if (i < 0)
            {
                // (the logo and lettering stay, on the hull's own colours)
                edit.pattern = ShipPaint.FactoryPattern;
                presetIndex = -1;
            }
            else
            {
                edit = ShipPaint.FromPreset(i, edit);
                edit.text = draft;
                presetIndex = i;
            }
            hsvSlot = -1;
            SoundFX.Ui(Sfx.UiClear);
        }

        void RandomScheme()
        {
            edit = ShipPaint.Random(rng, edit);
            edit.text = draft;
            Touched();
            hsvSlot = -1;
            SoundFX.Ui(Sfx.UiClear);
        }

        void ResetScheme()
        {
            SetScheme(opened?.Clone());
            SoundFX.Ui(Sfx.UiClear);
        }

        /// <summary>Keep the scheme for the class, then launch (or, from the pause menu, paint the
        /// ship and go back to the game).</summary>
        public void DonePainting()
        {
            if (step != Step.Paint) return;
            ShipPaint.Set(paintId, PlainFactory ? null : Scheme());
            typing = false;
            GUIUtility.keyboardControl = 0;
            if (paintOnly)
            {
                ShipPaint.ApplyPlayer();
                Close();
                SoundFX.Ui(Sfx.UiLock);
                return;
            }
            step = Step.Ships;
            Launch();
        }

        void BackFromPaint()
        {
            ResetTurntable();
            turningPreview = false;
            typing = false;
            GUIUtility.keyboardControl = 0;
            if (paintOnly) { Close(); return; }
            step = Step.Ships;
            previewPaint = null;
            openedFrame = Time.frameCount;
            SoundFX.Ui(Sfx.UiClear);
        }

        /// <summary>A row changed by a step left (-1) or right (+1).</summary>
        void Adjust(int row, int dir)
        {
            switch (row)
            {
                case RowPreset:
                    ChoosePreset(((presetIndex < -1 ? -1 : presetIndex) + 1 + dir + ShipPaint.Presets.Length + 1) % (ShipPaint.Presets.Length + 1) - 1);
                    return;
                case RowPattern: edit.pattern = factory ? (dir > 0 ? 0 : ShipPaint.Patterns - 1) : (edit.pattern + dir + ShipPaint.Patterns) % ShipPaint.Patterns; break;
                case RowScale: edit.scale = Mathf.Clamp(edit.scale + dir * 5, PaintScheme.MinScale, PaintScheme.MaxScale); break;
                case RowAngle: edit.angle = (edit.angle + dir * 15 + 360) % 360; break;
                case RowSlot: slot = (slot + dir + 4) % 4; return;
                case RowSwatch:
                {
                    int at = NearestSwatch(edit.GetColor(slot));
                    edit.SetColor(slot, ShipPaint.Palette[(at + dir + ShipPaint.Palette.Length) % ShipPaint.Palette.Length]);
                    break;
                }
                case RowHue: SyncHsv(); hue = Mathf.Repeat(hue + dir * 0.02f, 1f); FromHsv(); break;
                case RowSat: SyncHsv(); sat = Mathf.Clamp01(sat + dir * 0.04f); FromHsv(); break;
                case RowVal: SyncHsv(); val = Mathf.Clamp01(val + dir * 0.04f); FromHsv(); break;
                case RowLogo: edit.logo = (edit.logo + 1 + dir + ShipPaint.Logos + 1) % (ShipPaint.Logos + 1) - 1; break;
                case RowDecal: placeLogo = !placeLogo; SoundFX.Ui(Sfx.UiClear); return;
                case RowAlong: Along = Mathf.Clamp(Along + dir, -PaintScheme.MaxShift, PaintScheme.MaxShift); Touched(true, decal: true); return;
                case RowUp: Up = Mathf.Clamp(Up + dir, -PaintScheme.MaxShift, PaintScheme.MaxShift); Touched(true, decal: true); return;
                case RowSize: Size = Mathf.Clamp(Size + dir * 5, PaintScheme.MinDecal, PaintScheme.MaxDecal); Touched(true, decal: true); return;
                case RowTurn: Turn = (Turn + dir * 5 + 360) % 360; Touched(true, decal: true); return;
                case RowSides: Sides = (Sides + dir + 3) % 3; Touched(true, decal: true); return;
                default: return;
            }
            Touched(decal: row == RowLogo || (slot == 3 && row >= RowSwatch && row <= RowVal));
        }

        static int NearestSwatch(Color32 c)
        {
            int best = 0, bestD = int.MaxValue;
            for (int i = 0; i < ShipPaint.Palette.Length; i++)
            {
                var p = ShipPaint.Palette[i];
                int d = (p.r - c.r) * (p.r - c.r) + (p.g - c.g) * (p.g - c.g) + (p.b - c.b) * (p.b - c.b);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>Hue, saturation and value from the colour being changed (when it isn't the one
        /// they were last taken from).</summary>
        void SyncHsv()
        {
            var c = edit.GetColor(slot);
            if (hsvSlot == slot && c.r == hsvOf.r && c.g == hsvOf.g && c.b == hsvOf.b) return;
            Color.RGBToHSV(c, out hue, out sat, out val);
            hsvSlot = slot;
            hsvOf = c;
        }

        void FromHsv()
        {
            Color32 c = Color.HSVToRGB(hue, sat, val);
            edit.SetColor(slot, c);
            hsvOf = edit.GetColor(slot);
            hsvSlot = slot;
        }

        // ------------------------------------------------------------ keys and pad

        void PaintUpdate()
        {
            var k = Keyboard.current;
            var pad = Gamepad.current;
            if (typing)
            {
                // (the letters go to the lettering; these leave it)
                if (k != null && (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame || k.escapeKey.wasPressedThisFrame))
                    wantStopTyping = true;
                if (k != null && k.upArrowKey.wasPressedThisFrame) { wantStopTyping = true; focus = RowLogo; }
                if (k != null && k.downArrowKey.wasPressedThisFrame) { wantStopTyping = true; focus = RowPreset; }
                if (pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame))
                    wantStopTyping = true;
                if (pad != null && pad.dpad.up.wasPressedThisFrame) { wantStopTyping = true; focus = RowLogo; }
                if (pad != null && pad.dpad.down.wasPressedThisFrame) { wantStopTyping = true; focus = RowPreset; }
                return;
            }
            int vertical = 0, horizontal = 0;
            bool hNow = false;
            if (k != null)
            {
                if (k.upArrowKey.wasPressedThisFrame || k.wKey.wasPressedThisFrame) vertical = -1;
                if (k.downArrowKey.wasPressedThisFrame || k.sKey.wasPressedThisFrame) vertical = +1;
                if (k.leftArrowKey.isPressed || k.aKey.isPressed) horizontal = -1;
                if (k.rightArrowKey.isPressed || k.dKey.isPressed) horizontal = +1;
                hNow |= k.leftArrowKey.wasPressedThisFrame || k.aKey.wasPressedThisFrame || k.rightArrowKey.wasPressedThisFrame || k.dKey.wasPressedThisFrame;
                if (k.qKey.wasPressedThisFrame) Family(-1);
                if (k.eKey.wasPressedThisFrame) Family(+1);
                if (k.rKey.wasPressedThisFrame) RandomScheme();
                if (k.pKey.wasPressedThisFrame) TogglePlacing();
                if (k.tKey.wasPressedThisFrame) { focus = RowText; wantType = true; typeFrame = Time.frameCount; }
                if (k.escapeKey.wasPressedThisFrame || k.backspaceKey.wasPressedThisFrame) { BackFromPaint(); return; }
                if (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame) { DonePainting(); return; }
            }
            if (pad != null)
            {
                if (pad.dpad.up.wasPressedThisFrame) vertical = -1;
                if (pad.dpad.down.wasPressedThisFrame) vertical = +1;
                float sx = pad.leftStick.x.ReadValue(), sy = pad.leftStick.y.ReadValue();
                if (pad.dpad.left.isPressed || sx < -0.5f) horizontal = -1;
                if (pad.dpad.right.isPressed || sx > 0.5f) horizontal = +1;
                hNow |= pad.dpad.left.wasPressedThisFrame || pad.dpad.right.wasPressedThisFrame;
                if (Mathf.Abs(sy) < 0.4f) stickRepeat = 0f;
                else if (Time.unscaledTime >= stickRepeat)
                {
                    vertical = sy > 0f ? -1 : 1;
                    stickRepeat = Time.unscaledTime + (stickRepeat == 0f ? 0.35f : 0.2f);
                }
                if (pad.leftShoulder.wasPressedThisFrame) Family(-1);
                if (pad.rightShoulder.wasPressedThisFrame) Family(+1);
                if (pad.buttonNorth.wasPressedThisFrame) RandomScheme();
                if (pad.buttonWest.wasPressedThisFrame) ResetScheme();
                if (pad.selectButton.wasPressedThisFrame) TogglePlacing();
                if (pad.buttonEast.wasPressedThisFrame) { BackFromPaint(); return; }
                if (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame) { DonePainting(); return; }
            }
            if (vertical != 0)
            {
                // (the rows on the page: the right-hand column's are the colours' or PLACE's)
                do focus = (focus + vertical + Rows) % Rows; while (!RowShown(focus));
                if (focus == RowText) wantType = true;
                SoundFX.Ui(Sfx.UiClear);
            }
            // left/right: a step, then repeating while held
            if (horizontal == 0) lastDir = 0f;
            else if (hNow || horizontal != (int)lastDir) { Adjust(focus, horizontal); lastDir = horizontal; holdNext = Time.unscaledTime + 0.35f; }
            else if (Time.unscaledTime >= holdNext) { Adjust(focus, horizontal); holdNext = Time.unscaledTime + 0.06f; }
        }

        void Family(int dir)
        {
            edit.pattern = factory ? (dir > 0 ? 0 : ShipPaint.Patterns - ShipPaint.Variants)
                                   : (edit.pattern + dir * ShipPaint.Variants + ShipPaint.Patterns) % ShipPaint.Patterns;
            Touched();
            SoundFX.Ui(Sfx.UiClear);
        }

        // ------------------------------------------------------------ turning the preview

        /// <summary>The mouse on the picture: drag to turn the ship (round and over), the wheel to
        /// zoom, a double-click to put her back on the turntable. With the PLACE page up, the left
        /// button drags the logo or the lettering about the hull and the right one turns her.</summary>
        void TurnPreviewWithMouse(Rect pic)
        {
            var e = Event.current;
            // (a release anywhere ends a turn or a drag, even one another control has used)
            if (e.rawType == EventType.MouseUp) { turningPreview = false; draggingDecal = false; }
            if (placing && e.type == EventType.MouseDown && e.button == 0 && pic.Contains(e.mousePosition))
            {
                if (typing) { GUI.FocusControl(null); GUIUtility.keyboardControl = 0; }
                draggingDecal = true;
                DragDecal(e.mousePosition, pic);
                e.Use();
            }
            else if (draggingDecal && e.type == EventType.MouseDrag)
            {
                DragDecal(e.mousePosition, pic);
                e.Use();
            }
            else if (placing && e.type == EventType.MouseDown && e.button == 1 && pic.Contains(e.mousePosition))
            {
                turningPreview = true;
                e.Use();
            }
            else if (e.type == EventType.MouseDown && e.button == 0 && pic.Contains(e.mousePosition) && !placing)
            {
                // (a click on the picture stops the typing, as a click on nothing does)
                if (typing) { GUI.FocusControl(null); GUIUtility.keyboardControl = 0; }
                if (e.clickCount >= 2) ResetTurntable();
                else turningPreview = true;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && turningPreview)
            {
                TurnPreview(e.delta.x * 0.4f, e.delta.y * 0.3f);
                e.Use();
            }
            else if (e.type == EventType.ScrollWheel && pic.Contains(e.mousePosition))
            {
                ZoomPreview(e.delta.y > 0f ? 1.12f : 1f / 1.12f);
                e.Use();
            }
        }

        bool turningPreview;

        // ------------------------------------------------------------ drawing (and the mouse)

        static bool Clicked(Rect r)
        {
            var e = Event.current;
            if (e.type != EventType.MouseDown || e.button != 0 || !r.Contains(e.mousePosition)) return false;
            e.Use();
            return true;
        }

        /// <summary>A slider: click or drag along it. Returns the value (changed: true when moved).</summary>
        float Slider(Rect r, int id, float v, float min, float max, Color fill, out bool changed)
        {
            changed = false;
            var e = Event.current;
            bool grab = e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition);
            if (grab) dragging = id;
            if (dragging == id && (grab || e.type == EventType.MouseDrag))
            {
                v = Mathf.Lerp(min, max, Mathf.InverseLerp(r.x, r.xMax, e.mousePosition.x));
                changed = true;
                e.Use();
            }
            if (e.rawType == EventType.MouseUp && dragging == id) dragging = -1;
            float s = Hud.S;
            var track = new Rect(r.x, r.y + r.height * 0.35f, r.width, r.height * 0.3f);
            Hud.Fill(track, new Color(1f, 1f, 1f, 0.12f));
            float t = Mathf.InverseLerp(min, max, v);
            Hud.Fill(new Rect(track.x, track.y, track.width * t, track.height), fill);
            Hud.Fill(new Rect(track.x + track.width * t - 3f * s, r.y + 2f * s, 6f * s, r.height - 4f * s), new Color(1f, 0.85f, 0.55f));
            return v;
        }

        void Focus(int row, Rect r)
        {
            paintRects[row] = r;
            if (focus == row) Hud.Outline(r, Mathf.Max(1f, 2f * Hud.S), new Color(1f, 0.72f, 0.3f, 0.9f));
            if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition)) focus = row;
        }

        static Color Ui(Color32 c) => new Color(c.r / 255f, c.g / 255f, c.b / 255f, 1f);

        void Button(Rect r, string label, bool lit, System.Action act)
        {
            float s = Hud.S;
            Hud.Fill(r, lit ? new Color(0.25f, 0.45f, 0.8f, 0.75f) : new Color(1f, 1f, 1f, 0.08f));
            Hud.Outline(r, Mathf.Max(1f, (lit ? 2f : 1f) * s), lit ? new Color(1f, 0.72f, 0.3f) : new Color(1f, 1f, 1f, 0.2f));
            GUI.Label(r, $"<b>{label}</b>", Hud.Text(lit ? 20 : 16, false, TextAnchor.MiddleCenter));
            if (Clicked(r)) act();
        }

        void PaintGUI()
        {
            float s = Hud.S;
            Hud.Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0.01f, 0.03f, 0.92f));
            float pad = 24f * s;
            float w = Mathf.Min(Screen.width - pad * 2f, 1500f * s), h = Mathf.Min(Screen.height - pad * 2f, 860f * s);
            var panel = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            Hud.Panel(panel);
            var entry = roster != null ? roster.Find(paintId) : null;
            string shipName = entry != null ? entry.displayName.ToUpperInvariant() : "SHIP";
            GUI.Label(new Rect(panel.x, panel.y + pad * 0.6f, w, 40f * s), $"PAINT YOUR {shipName}", Hud.Text(26, true, TextAnchor.MiddleCenter));
            float top = panel.y + pad * 0.6f + 56f * s;
            float bottom = panel.yMax - pad - 64f * s - 10f * s;
            var head = Hud.Text(15, false);
            var small = Hud.Text(13, false, TextAnchor.MiddleLeft);
            var e = Event.current;

            // ---- the presets, down the left
            float lw = Mathf.Min(290f * s, w * 0.21f), x0 = panel.x + pad;
            GUI.Label(new Rect(x0, top, lw, 22f * s), "<b>PRESETS</b>", head);
            float py = top + 26f * s;
            float ph = Mathf.Clamp((bottom - py - 2f * 48f * s) / (ShipPaint.Presets.Length + 1) - 4f * s, 18f * s, 38f * s);
            var presetsArea = new Rect(x0 - 4f * s, py - 4f * s, lw + 8f * s, (ph + 4f * s) * (ShipPaint.Presets.Length + 1) + 4f * s);
            Focus(RowPreset, presetsArea);
            for (int i = -1; i < ShipPaint.Presets.Length; i++)
            {
                var r = new Rect(x0, py, lw, ph);
                bool sel = i == presetIndex;
                Hud.Fill(r, sel ? new Color(0.25f, 0.45f, 0.8f, 0.55f) : new Color(1f, 1f, 1f, 0.05f));
                string label = i < 0 ? "Factory finish" : ShipPaint.Presets[i].name;
                GUI.Label(new Rect(r.x + 10f * s, r.y, r.width - 70f * s, r.height), label, Hud.Text(15, sel, TextAnchor.MiddleLeft));
                if (i >= 0)
                {
                    var ps = ShipPaint.Presets[i].Scheme;
                    float sw = Mathf.Min(14f * s, r.height - 8f * s);
                    for (int c = 0; c < 3; c++)
                        Hud.Fill(new Rect(r.xMax - (3 - c) * (sw + 3f * s) - 6f * s, r.y + (r.height - sw) * 0.5f, sw, sw), Ui(ps.GetColor(c)));
                }
                if (Clicked(r)) { focus = RowPreset; ChoosePreset(i); }
                py += ph + 4f * s;
            }
            py += 8f * s;
            Button(new Rect(x0, py, lw, 40f * s), "Random", false, RandomScheme);
            Button(new Rect(x0, py + 48f * s, lw, 40f * s), "Reset", false, ResetScheme);

            // ---- the picture, and the patterns under it
            float rx = x0 + lw + pad, rw = panel.xMax - pad - rx;
            float aw = rw * 0.56f, bx = rx + aw + pad, bw = rw - aw - pad;
            float picH = Mathf.Min(aw * 9f / 16f, (bottom - top) * 0.36f), picW = picH * 16f / 9f;
            var pic = new Rect(rx + (aw - picW) * 0.5f, top, picW, picH);
            if (previewTex != null) GUI.DrawTexture(pic, previewTex, ScaleMode.ScaleToFit, false);
            Hud.Outline(pic, Mathf.Max(1f, s), new Color(1f, 1f, 1f, 0.15f));
            if (factory) GUI.Label(new Rect(pic.x + 8f * s, pic.y + 6f * s, pic.width, 20f * s),
                                   $"<color=#9aa4b0>Factory finish{(PlainFactory ? "" : ", with the logo and lettering")}</color>", small);
            GUI.Label(new Rect(pic.x + 8f * s, pic.yMax - 22f * s, pic.width - 16f * s, 20f * s),
                      placing ? $"<color=#6f7a86>drag to move the {(placeLogo ? "logo" : "lettering")}, right-drag to turn, wheel to zoom</color>"
                              : "<color=#6f7a86>drag to turn, wheel to zoom, double-click: back on the turntable</color>", small);
            TurnPreviewWithMouse(pic);

            float gy = pic.yMax + 12f * s;
            GUI.Label(new Rect(rx, gy, aw, 22f * s),
                      factory ? "<b>PATTERN</b>   none: the factory finish  <color=#9aa4b0>(choose one to paint her)</color>"
                              : $"<b>PATTERN</b>   {ShipPaint.FamilyOf(edit.pattern)}: {ShipPaint.PatternName(edit.pattern)}  <color=#9aa4b0>({edit.pattern + 1} of {ShipPaint.Patterns})</color>", head);
            gy += 26f * s;
            float fw = Mathf.Min(170f * s, aw * 0.3f), cw = (aw - fw) / ShipPaint.Variants;
            float ch = Mathf.Clamp((bottom - gy - 2f * 34f * s - 8f * s) / ShipPaint.Families, 12f * s, 30f * s);
            Focus(RowPattern, new Rect(rx - 4f * s, gy - 4f * s, aw + 8f * s, ch * ShipPaint.Families + 8f * s));
            for (int f = 0; f < ShipPaint.Families; f++)
            {
                float y = gy + f * ch;
                bool famSel = !factory && edit.pattern / ShipPaint.Variants == f;
                GUI.Label(new Rect(rx, y, fw - 6f * s, ch), famSel ? $"<b>{ShipPaint.FamilyNames[f]}</b>" : $"<color=#9aa4b0>{ShipPaint.FamilyNames[f]}</color>",
                          Hud.Text(12, false, TextAnchor.MiddleLeft));
                for (int v = 0; v < ShipPaint.Variants; v++)
                {
                    int p = f * ShipPaint.Variants + v;
                    var r = new Rect(rx + fw + v * cw + 1f * s, y + 1f * s, cw - 2f * s, ch - 2f * s);
                    bool sel = p == edit.pattern;
                    // (each cell in the scheme's colours: secondary for the family's row, trim when chosen)
                    Hud.Fill(r, sel ? Ui(edit.trim) : Color.Lerp(Ui(edit.primary), Ui(edit.secondary), ((v + f) & 1) * 0.6f) * new Color(1f, 1f, 1f, 0.55f));
                    if (sel) Hud.Outline(r, Mathf.Max(1f, 2f * s), Color.white);
                    if (Clicked(r)) { edit.pattern = p; focus = RowPattern; Touched(); }
                }
            }
            float sy = gy + ch * ShipPaint.Families + 8f * s;
            float lab = 90f * s;
            var scaleRow = new Rect(rx, sy, aw, 30f * s);
            Focus(RowScale, scaleRow);
            GUI.Label(new Rect(rx, sy, lab, 30f * s), "Scale", small);
            float sc = Slider(new Rect(rx + lab, sy, aw - lab - 70f * s, 30f * s), RowScale, edit.scale, PaintScheme.MinScale, PaintScheme.MaxScale, new Color(0.45f, 0.75f, 1f, 0.9f), out bool moved);
            if (moved) { edit.scale = Mathf.RoundToInt(sc / 5f) * 5; focus = RowScale; Touched(); }
            GUI.Label(new Rect(scaleRow.xMax - 64f * s, sy, 64f * s, 30f * s), $"{edit.scale}%", small);
            sy += 34f * s;
            var angleRow = new Rect(rx, sy, aw, 30f * s);
            Focus(RowAngle, angleRow);
            GUI.Label(new Rect(rx, sy, lab, 30f * s), "Angle", small);
            float an = Slider(new Rect(rx + lab, sy, aw - lab - 70f * s, 30f * s), RowAngle, edit.angle, 0f, 359f, new Color(0.45f, 0.75f, 1f, 0.9f), out moved);
            if (moved) { edit.angle = Mathf.RoundToInt(an / 5f) * 5 % 360; focus = RowAngle; Touched(); }
            GUI.Label(new Rect(angleRow.xMax - 64f * s, sy, 64f * s, 30f * s), $"{edit.angle}°", small);

            // ---- the colours, the logo and the lettering, on the right (or, PLACE, where the
            // logo and lettering go): a tab for each
            float cy = top;
            var tabColours = new Rect(bx, cy - 4f * s, bw * 0.5f - 3f * s, 28f * s);
            var tabPlace = new Rect(bx + bw * 0.5f + 3f * s, cy - 4f * s, bw * 0.5f - 3f * s, 28f * s);
            Hud.Fill(tabColours, !placing ? new Color(0.25f, 0.45f, 0.8f, 0.55f) : new Color(1f, 1f, 1f, 0.05f));
            Hud.Fill(tabPlace, placing ? new Color(0.25f, 0.45f, 0.8f, 0.55f) : new Color(1f, 1f, 1f, 0.05f));
            GUI.Label(tabColours, "<b>COLOURS</b>", Hud.Text(15, !placing, TextAnchor.MiddleCenter));
            GUI.Label(tabPlace, $"<b>PLACE</b> <color=#9aa4b0>({(Gamepad.current != null ? "View" : "P")})</color>", Hud.Text(15, placing, TextAnchor.MiddleCenter));
            if (Clicked(placing ? tabColours : tabPlace)) TogglePlacing();
            cy += 30f * s;
            if (placing) { PlaceGUI(bx, cy, bw, s); BackAndDone(panel, pad, w, s); return; }
            float tw = bw / 4f;
            Focus(RowSlot, new Rect(bx - 3f * s, cy - 3f * s, bw + 6f * s, 46f * s));
            for (int i = 0; i < 4; i++)
            {
                var r = new Rect(bx + i * tw + 2f * s, cy, tw - 4f * s, 40f * s);
                bool sel = i == slot;
                Hud.Fill(r, sel ? new Color(0.25f, 0.45f, 0.8f, 0.55f) : new Color(1f, 1f, 1f, 0.05f));
                Hud.Fill(new Rect(r.x + 6f * s, r.y + 8f * s, 24f * s, 24f * s), Ui(edit.GetColor(i)));
                Hud.Outline(new Rect(r.x + 6f * s, r.y + 8f * s, 24f * s, 24f * s), Mathf.Max(1f, s), new Color(1f, 1f, 1f, 0.3f));
                GUI.Label(new Rect(r.x + 34f * s, r.y, r.width - 36f * s, r.height), SlotNames[i], Hud.Text(13, sel, TextAnchor.MiddleLeft));
                if (Clicked(r)) { slot = i; focus = RowSlot; }
            }
            cy += 50f * s;
            float sw2 = Mathf.Min(bw / 8f, 34f * s);
            Focus(RowSwatch, new Rect(bx - 3f * s, cy - 3f * s, sw2 * 8f + 6f * s, sw2 * 3f + 6f * s));
            var cur = edit.GetColor(slot);
            for (int i = 0; i < ShipPaint.Palette.Length; i++)
            {
                var r = new Rect(bx + (i % 8) * sw2 + 2f * s, cy + (i / 8) * sw2 + 2f * s, sw2 - 4f * s, sw2 - 4f * s);
                var pc = ShipPaint.Palette[i];
                Hud.Fill(r, Ui(pc));
                if (pc.r == cur.r && pc.g == cur.g && pc.b == cur.b) Hud.Outline(r, Mathf.Max(1f, 2f * s), Color.white);
                if (Clicked(r)) { edit.SetColor(slot, pc); focus = RowSwatch; Touched(decal: slot == 3); }
            }
            cy += sw2 * 3f + 8f * s;
            SyncHsv();
            string[] hsvNames = { "Hue", "Saturation", "Value" };
            for (int i = 0; i < 3; i++)
            {
                int row = RowHue + i;
                var r = new Rect(bx, cy, bw, 26f * s);
                Focus(row, r);
                GUI.Label(new Rect(bx, cy, 92f * s, 26f * s), hsvNames[i], small);
                float now = i == 0 ? hue : i == 1 ? sat : val;
                var fill = i == 0 ? Color.HSVToRGB(hue, 1f, 1f) : i == 1 ? Color.HSVToRGB(hue, sat, 1f) : Color.HSVToRGB(0f, 0f, val);
                float nv = Slider(new Rect(bx + 92f * s, cy, bw - 92f * s, 26f * s), row, now, 0f, 1f, fill, out moved);
                if (moved)
                {
                    if (i == 0) hue = nv; else if (i == 1) sat = nv; else val = nv;
                    FromHsv();
                    focus = row;
                    Touched(decal: slot == 3);
                }
                cy += 30f * s;
            }

            cy += 6f * s;
            GUI.Label(new Rect(bx, cy, bw, 22f * s), $"<b>LOGO</b>   {(edit.logo < 0 ? "none" : ShipPaint.LogoNames[edit.logo])}", head);
            cy += 26f * s;
            float lc = Mathf.Min(bw / 7f, 56f * s);
            Focus(RowLogo, new Rect(bx - 3f * s, cy - 3f * s, lc * 7f + 6f * s, lc * 2f + 6f * s));
            var ui = ShipPaint.LogoAtlasUi;
            for (int i = -1; i < ShipPaint.Logos; i++)
            {
                int at = i + 1;
                var r = new Rect(bx + (at % 7) * lc + 2f * s, cy + (at / 7) * lc + 2f * s, lc - 4f * s, lc - 4f * s);
                bool sel = i == edit.logo;
                Hud.Fill(r, sel ? new Color(0.25f, 0.45f, 0.8f, 0.6f) : new Color(0f, 0f, 0f, 0.5f));
                if (i < 0) GUI.Label(r, "none", Hud.Text(12, false, TextAnchor.MiddleCenter));
                else if (ui != null)
                {
                    var old = GUI.color;
                    GUI.color = Ui(edit.letters);
                    GUI.DrawTextureWithTexCoords(r, ui, new Rect((i % 4) / 4f, 1f - (i / 4 + 1) / 3f, 0.25f, 1f / 3f), true);
                    GUI.color = old;
                }
                if (sel) Hud.Outline(r, Mathf.Max(1f, 2f * s), Color.white);
                if (Clicked(r)) { edit.logo = i; focus = RowLogo; Touched(decal: true); }
            }
            cy += lc * 2f + 10f * s;

            GUI.Label(new Rect(bx, cy, bw, 22f * s), "<b>LETTERING</b>", head);
            cy += 26f * s;
            var textRect = new Rect(bx, cy, bw, 36f * s);
            Focus(RowText, new Rect(bx - 3f * s, cy - 3f * s, bw + 6f * s, 42f * s));
            var field = new GUIStyle(GUI.skin.textField) { fontSize = Mathf.RoundToInt(18 * s), alignment = TextAnchor.MiddleLeft };
            if (wantType) { GUI.FocusControl(TextControl); wantType = false; }
            if (wantStopTyping) { GUI.FocusControl(null); GUIUtility.keyboardControl = 0; wantStopTyping = false; }
            if (e.type == EventType.KeyDown && Time.frameCount - typeFrame <= 1 && (e.keyCode == KeyCode.T || char.ToUpperInvariant(e.character) == 'T')) e.Use();
            GUI.SetNextControlName(TextControl);
            string typed = GUI.TextField(textRect, draft, ShipPaint.MaxLetters + 4, field);
            if (typed != draft)
            {
                // capitals, digits, the dash and spaces as they are typed (trimmed on the hull)
                var sb = new System.Text.StringBuilder();
                foreach (char c0 in typed.ToUpperInvariant())
                {
                    char c = c0 == '_' ? ' ' : c0;
                    if (((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == ' ') && sb.Length < ShipPaint.MaxLetters) sb.Append(c);
                }
                draft = sb.ToString();
                Touched(true, decal: true);
            }
            typing = GUI.GetNameOfFocusedControl() == TextControl;
            if (typing) focus = RowText;
            cy += 40f * s;
            GUI.Label(new Rect(bx, cy, bw, 20f * s), $"<color=#9aa4b0>A-Z, 0-9, - and spaces, up to {ShipPaint.MaxLetters}; in the lettering colour</color>", Hud.Text(12, false));

            BackAndDone(panel, pad, w, s);

            // a click on nothing in particular stops the typing
            if (e.type == EventType.MouseDown && !textRect.Contains(e.mousePosition) && typing) { GUI.FocusControl(null); GUIUtility.keyboardControl = 0; }
        }

        /// <summary>The PLACE page: which one, where it is along and up the hull, its size and
        /// turn, its sides, and Put back.</summary>
        void PlaceGUI(float bx, float cy, float bw, float s)
        {
            var small = Hud.Text(13, false, TextAnchor.MiddleLeft);
            var head = Hud.Text(15, false);
            GUI.Label(new Rect(bx, cy, bw, 22f * s), "<b>MOVE</b>", head);
            cy += 26f * s;
            Focus(RowDecal, new Rect(bx - 3f * s, cy - 3f * s, bw + 6f * s, 46f * s));
            for (int i = 0; i < 2; i++)
            {
                var r = new Rect(bx + i * bw * 0.5f + 2f * s, cy, bw * 0.5f - 4f * s, 40f * s);
                bool sel = (i == 0) == placeLogo;
                Hud.Fill(r, sel ? new Color(0.25f, 0.45f, 0.8f, 0.55f) : new Color(1f, 1f, 1f, 0.05f));
                string what = i == 0 ? (edit.logo < 0 ? "Logo (none)" : "Logo") : (PaintScheme.SanitizeText(draft).Length == 0 ? "Lettering (none)" : "Lettering");
                GUI.Label(r, what, Hud.Text(15, sel, TextAnchor.MiddleCenter));
                if (Clicked(r)) { placeLogo = i == 0; focus = RowDecal; }
            }
            cy += 52f * s;
            float lab = 92f * s;
            void Row(int row, string name, int value, int min, int max, string shown, System.Action<int> set)
            {
                var r = new Rect(bx, cy, bw, 30f * s);
                Focus(row, r);
                GUI.Label(new Rect(bx, cy, lab, 30f * s), name, small);
                float v = Slider(new Rect(bx + lab, cy, bw - lab - 64f * s, 30f * s), row, value, min, max, new Color(0.45f, 0.75f, 1f, 0.9f), out bool moved);
                if (moved) { set(Mathf.RoundToInt(v)); focus = row; Touched(true, decal: true); }
                GUI.Label(new Rect(r.xMax - 60f * s, cy, 60f * s, 30f * s), shown, small);
                cy += 36f * s;
            }
            Row(RowAlong, "Along", Along, -PaintScheme.MaxShift, PaintScheme.MaxShift, $"{Along:+0;-0;0}%", v => Along = v);
            Row(RowUp, "Up", Up, -PaintScheme.MaxShift, PaintScheme.MaxShift, $"{Up:+0;-0;0}%", v => Up = v);
            Row(RowSize, "Size", Size, PaintScheme.MinDecal, PaintScheme.MaxDecal, $"{Size}%", v => Size = Mathf.RoundToInt(v / 5f) * 5);
            Row(RowTurn, "Turn", Turn, 0, 359, $"{Turn}°", v => Turn = Mathf.RoundToInt(v / 5f) * 5 % 360);
            Focus(RowSides, new Rect(bx - 3f * s, cy - 3f * s, bw + 6f * s, 40f * s));
            for (int i = 0; i < 3; i++)
            {
                var r = new Rect(bx + i * bw / 3f + 2f * s, cy, bw / 3f - 4f * s, 34f * s);
                bool sel = Sides == i;
                Hud.Fill(r, sel ? new Color(0.25f, 0.45f, 0.8f, 0.55f) : new Color(1f, 1f, 1f, 0.05f));
                GUI.Label(r, SideNames[i], Hud.Text(13, sel, TextAnchor.MiddleCenter));
                if (Clicked(r)) { Sides = i; focus = RowSides; Touched(true, decal: true); }
            }
            cy += 46f * s;
            Button(new Rect(bx, cy, bw * 0.5f, 38f * s), "Put back", false, PutBack);
            cy += 48f * s;
            GUI.Label(new Rect(bx, cy, bw, 60f * s),
                      "<color=#9aa4b0>Drag on the picture to move it about the hull (on its sides: not the top or the belly). Along is toward the bow; Turn is as it reads.</color>",
                      new GUIStyle(Hud.Text(12, false)) { wordWrap = true });
        }

        void BackAndDone(Rect panel, float pad, float w, float s)
        {
            // ---- back and done
            var done = new Rect(panel.xMax - pad - 260f * s, panel.yMax - pad - 64f * s, 260f * s, 56f * s);
            Button(done, paintOnly ? "DONE" : "DONE: LAUNCH", true, DonePainting);
            launchRect = done;
            Button(new Rect(done.x - 150f * s, done.y + 8f * s, 136f * s, 44f * s), paintOnly ? "Cancel" : "Back", false, BackFromPaint);
            string hint = Gamepad.current != null
                ? "D-pad rows / change   LB/RB pattern family   View place   Y random   X reset   A done   B back"
                : "Up/down rows   Left/right change   Q/E pattern family   T lettering   P place   R random   Enter done   Esc back";
            GUI.Label(new Rect(panel.x + pad, panel.yMax - pad - 26f * s, w - 460f * s, 24f * s), $"<color=#9aa4b0>{hint}</color>", Hud.Text(13, false));
        }
    }
}

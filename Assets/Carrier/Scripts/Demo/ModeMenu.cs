using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarrierGame.Demo
{
    /// <summary>A row of a mode's menu: a heading, or something to choose.</summary>
    public sealed class MenuRow
    {
        public string text = "", detail = "";
        /// <summary>A heading over the rows that follow (not chosen).</summary>
        public bool header;
        /// <summary>Can be acted on now (a row that can't is shown greyed, with its detail saying why).</summary>
        public bool enabled = true;
        /// <summary>What the row is (a target, a deposit, a place...): its selection is kept by it as the list changes.</summary>
        public object tag;
    }

    /// <summary>What a mode's menu shows, and what choosing a row does.</summary>
    public interface IMenuSource
    {
        string Title { get; }
        /// <summary>The keys the menu takes, for its foot (keyboard or pad).</summary>
        string Hint(bool pad);
        void Rows(List<MenuRow> rows);
        /// <summary>The row chosen: Enter, A, or a click.</summary>
        void Act(MenuRow row);
        /// <summary>Any other key for the row selected (a mode's own: land there, eject this...):
        /// true if one was used.</summary>
        bool Keys(Keyboard k, Gamepad pad, MenuRow row);
    }

    /// <summary>A menu's source that has a row to open on (its tag), rather than the first.</summary>
    public interface IMenuStart
    {
        object StartAt { get; }
    }

    /// <summary>
    /// A mode's menu (Tab, or Back + Y on the pad): a list to choose from, over the game, which
    /// goes on while it is open. The ship can still be flown (bar the nose up/down, whose keys
    /// and D-pad choose in the list), and the mode's own direct keys still work (the guns, the
    /// targets, the scan...); what opens or does something else (landing and docking, the map,
    /// cruise, a strike, a site's screen, the comms line) waits till it is closed, as some of
    /// those keys are the menu's own (K lands at a point of interest). Up/down choose, Enter (A)
    /// acts on the row, a click picks and acts, Tab or Esc (B) closes it; P (Start) closes it and
    /// pauses. Its rows are looked at again a few times a second, so distances and states stay
    /// current.
    /// </summary>
    public class ModeMenu : MonoBehaviour
    {
        public static ModeMenu Instance { get; private set; }
        public static bool IsOpen => InputFocus.Has(InputFocus.Panel.ModeMenu);
        public static bool JustClosed => InputFocus.JustReleased(InputFocus.Panel.ModeMenu);
        /// <summary>Its screen area while open (GUI coordinates): a click there isn't on a ship.</summary>
        public static Rect Area { get; private set; }

        public static IMenuSource Source => Instance != null && IsOpen ? Instance.source : null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Instance = null; Area = Rect.zero; }

        IMenuSource source;
        readonly List<MenuRow> rows = new List<MenuRow>();
        readonly List<Rect> rowRects = new List<Rect>();
        int selected, scroll, openedFrame = -10;
        float refreshAt, stickRepeat;

        void Awake() => Instance = this;
        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (IsOpen) InputFocus.Release(InputFocus.Panel.ModeMenu);
        }

        /// <summary>Open a menu (closing any other).</summary>
        public static void Open(IMenuSource s)
        {
            var m = Instance;
            if (m == null || s == null) return;
            if (IsOpen && m.source == s) return;
            if (!IsOpen && !InputFocus.Take(InputFocus.Panel.ModeMenu)) return;
            m.source = s;
            m.selected = -1;
            m.scroll = 0;
            m.openedFrame = Time.frameCount;
            m.Refresh((s as IMenuStart)?.StartAt);
            SoundFX.Ui(Sfx.UiClear);
        }

        public static void Toggle(IMenuSource s)
        {
            if (IsOpen && Instance.source == s) Close();
            else Open(s);
        }

        public static void Close()
        {
            if (!IsOpen) return;
            InputFocus.Release(InputFocus.Panel.ModeMenu);
            if (Instance != null) Instance.source = null;
            Area = Rect.zero;
        }

        /// <summary>The row selected (null if none).</summary>
        public MenuRow Selected => selected >= 0 && selected < rows.Count ? rows[selected] : null;

        /// <summary>Select the first row whose tag is `tag` (for the self-test).</summary>
        public bool Select(object tag)
        {
            for (int i = 0; i < rows.Count; i++)
                if (!rows[i].header && Equals(rows[i].tag, tag)) { selected = i; return true; }
            return false;
        }

        /// <summary>The rows as shown now (for the self-test).</summary>
        public IReadOnlyList<MenuRow> Rows => rows;

        void Refresh(object startAt = null)
        {
            object keep = Selected?.tag ?? startAt;
            rows.Clear();
            source.Rows(rows);
            refreshAt = Time.unscaledTime + 0.3f;
            selected = -1;
            if (keep != null)
                for (int i = 0; i < rows.Count; i++)
                    if (!rows[i].header && Equals(rows[i].tag, keep)) { selected = i; break; }
            if (selected < 0) Step(+1, true);
        }

        /// <summary>To the next row that isn't a heading.</summary>
        void Step(int dir, bool fromTop = false)
        {
            if (rows.Count == 0) { selected = -1; return; }
            int i = fromTop ? (dir > 0 ? -1 : rows.Count) : selected;
            for (int n = 0; n < rows.Count; n++)
            {
                i += dir;
                if (i < 0 || i >= rows.Count) { if (fromTop) break; i = dir > 0 ? -1 : rows.Count; continue; }
                if (!rows[i].header) { selected = i; return; }
            }
        }

        void Act()
        {
            var row = Selected;
            if (row == null) return;
            if (!row.enabled)
            {
                SoundFX.Ui(Sfx.UiError);
                if (row.detail.Length > 0) Notices.Post(row.detail);
                return;
            }
            source.Act(row);
            if (IsOpen) Refresh();
        }

        void Update()
        {
            if (!IsOpen || source == null) return;
            if (Time.unscaledTime >= refreshAt) Refresh();
            // (not the key that opened it)
            if (Time.frameCount == openedFrame) return;
            var k = Keyboard.current;
            var pad = Gamepad.current;
            if (k != null)
            {
                if (k.tabKey.wasPressedThisFrame || k.escapeKey.wasPressedThisFrame) { Close(); return; }
                if (k.upArrowKey.wasPressedThisFrame) Step(-1);
                if (k.downArrowKey.wasPressedThisFrame) Step(+1);
                if (k.pageUpKey.wasPressedThisFrame) for (int i = 0; i < 6; i++) Step(-1);
                if (k.pageDownKey.wasPressedThisFrame) for (int i = 0; i < 6; i++) Step(+1);
                if (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame) { Act(); return; }
            }
            if (pad != null)
            {
                bool back = pad.selectButton.isPressed;
                // (B alone: Back + B is landing or docking, which waits)
                if ((!back && pad.buttonEast.wasPressedThisFrame) || (back && pad.buttonNorth.wasPressedThisFrame)) { Close(); return; }
                if (!back)
                {
                    float y = pad.dpad.ReadValue().y;
                    if (Mathf.Abs(y) > 0.5f)
                    {
                        if (stickRepeat <= 0f) { Step(y > 0f ? -1 : 1); stickRepeat = stickRepeat < 0f ? 0.12f : 0.35f; }
                        else stickRepeat -= Time.unscaledDeltaTime;
                    }
                    else stickRepeat = 0f;
                    if (pad.buttonSouth.wasPressedThisFrame) { Act(); return; }
                }
            }
            if (source != null && source.Keys(k, pad, Selected) && IsOpen) Refresh();
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                Vector2 m = mouse.position.ReadValue();
                Vector2 gui = new Vector2(m.x, Screen.height - m.y);
                for (int i = 0; i < rowRects.Count && i + scroll < rows.Count; i++)
                    if (rowRects[i].Contains(gui) && !rows[i + scroll].header) { selected = i + scroll; Act(); return; }
            }
        }

        void OnGUI()
        {
            if (!IsOpen || source == null) { rowRects.Clear(); return; }
            GUI.depth = -50;
            float s = Hud.S;
            float w = Mathf.Min(640f * s, Screen.width * 0.6f), pad = 12f * s, rowH = 26f * s;
            int visible = Mathf.Max(4, Mathf.FloorToInt((Screen.height * 0.55f - 90f * s) / rowH));
            if (selected >= 0)
            {
                if (selected < scroll) scroll = selected;
                if (selected >= scroll + visible) scroll = selected - visible + 1;
            }
            scroll = Mathf.Clamp(scroll, 0, Mathf.Max(0, rows.Count - visible));
            int shown = Mathf.Min(visible, rows.Count - scroll);
            float h = pad * 2f + 34f * s + Mathf.Max(1, shown) * rowH + 30f * s;
            var r = new Rect((Screen.width - w) * 0.5f, 70f * s, w, h);
            Area = r;
            Hud.Panel(r);
            Hud.Fill(new Rect(r.x, r.y, r.width, Mathf.Max(2f, 3f * s)), new Color(1f, 0.72f, 0.3f));
            float y = r.y + pad;
            GUI.Label(new Rect(r.x + pad, y, w - pad * 2f, 26f * s), source.Title, Hud.Text(19, true));
            if (scroll > 0 || scroll + shown < rows.Count)
                GUI.Label(new Rect(r.x + pad, y, w - pad * 2f, 26f * s), $"<color=#9aa4b0>{scroll + 1}-{scroll + shown} of {rows.Count}</color>", Hud.Text(13, false, TextAnchor.MiddleRight));
            y += 34f * s;
            rowRects.Clear();
            if (rows.Count == 0)
                GUI.Label(new Rect(r.x + pad, y, w - pad * 2f, rowH), "<color=#9aa4b0>Nothing here</color>", Hud.Text(15));
            for (int i = 0; i < shown; i++)
            {
                var row = rows[i + scroll];
                var rr = new Rect(r.x + pad * 0.5f, y, w - pad, rowH);
                rowRects.Add(rr);
                if (i + scroll == selected) Hud.Fill(rr, new Color(1f, 0.72f, 0.3f, 0.22f));
                string text = row.header ? $"<b><color=#ffc070>{row.text}</color></b>" : row.enabled ? row.text : $"<color=#7d8794>{row.text}</color>";
                GUI.Label(new Rect(rr.x + pad * 0.5f, rr.y, rr.width * 0.58f, rowH), text, Hud.Text(15, false, TextAnchor.MiddleLeft));
                if (row.detail.Length > 0)
                    GUI.Label(new Rect(rr.x + rr.width * 0.58f, rr.y, rr.width * 0.42f - pad * 0.5f, rowH),
                              $"<color=#9aa4b0>{row.detail}</color>", Hud.Text(13, false, TextAnchor.MiddleRight));
                y += rowH;
            }
            if (rows.Count == 0) y += rowH;
            GUI.Label(new Rect(r.x + pad, r.yMax - pad - 18f * s, w - pad * 2f, 18f * s),
                      $"<color=#9aa4b0>{source.Hint(PadSupport.UsingPad && Gamepad.current != null)}</color>", Hud.Text(12, false, TextAnchor.MiddleCenter));
        }
    }
}

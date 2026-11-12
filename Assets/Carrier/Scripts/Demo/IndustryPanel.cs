using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The industrial sites' screen, open while the frigate is docked at one (in space, or on
    /// the asteroid's pad) or landed on one's pad (on a planet or moon). U opens and closes it
    /// (Back + RB on the pad); it opens by itself on arrival. Four pages:
    ///   STORAGE  move ore, refined materials and parts between the hold and the site's storage;
    ///   REFINE   queue refining jobs (minerals in storage into refined materials);
    ///   BUILD    queue factory jobs (refined materials into ship parts);
    ///   FIT      fit parts to the frigate, or take them off (back into storage).
    /// The game goes on while it is open (the jobs run in game time). Keyboard: up/down (W/S) to
    /// choose, Tab or Q/E for the pages, left/right (A/D) to load or store, Shift for all, Enter
    /// to act, Backspace to cancel the last job, Esc or U to close. Pad: D-pad, LB/RB pages, A act,
    /// X store all (or queue five), Y load all (or cancel), B close. Mouse: click a row, a tab or a
    /// row's buttons.
    /// </summary>
    public class IndustryPanel : MonoBehaviour
    {
        public CarrierController carrier;

        public enum Page { Storage, Refine, Build, Fit }
        static readonly Page[] Pages = { Page.Storage, Page.Refine, Page.Build, Page.Fit };

        public static IndustryPanel Instance { get; private set; }
        public static bool IsOpen => InputFocus.Has(InputFocus.Panel.Industry);
        public static bool JustClosed => InputFocus.JustReleased(InputFocus.Panel.Industry);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        public Page Current { get; private set; }
        public IndustrySite Site { get; private set; }
        public int Selected => selected;

        int selected, scroll, openedFrame = -10;
        float stickRepeat;
        IndustrySite offeredAt;      // opened by itself here already (until the ship leaves)
        string message = "";
        float messageUntil;
        readonly List<string> rows = new List<string>();
        readonly List<Rect> rowRects = new List<Rect>();
        readonly Rect[] tabRects = new Rect[Pages.Length];
        readonly List<(Rect r, string id, int action)> buttons = new List<(Rect, string, int)>();

        // a row's actions (for the mouse's buttons too)
        const int LoadOne = 1, LoadAll = 2, StoreOne = 3, StoreAll = 4, QueueOne = 5, QueueFive = 6, FitToggle = 7, CancelLast = 8;
        const string AllOre = "*ore", Everything = "*all";

        void Awake() => Instance = this;
        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (IsOpen) InputFocus.Release(InputFocus.Panel.Industry);
        }

        CargoHold hold;
        ShipFittings fittings;
        float rowsDue;
        CargoHold Hold
        {
            get { if (hold == null && carrier != null) hold = carrier.GetComponent<CargoHold>(); return hold; }
        }
        ShipFittings Fittings
        {
            get { if (fittings == null && carrier != null) fittings = carrier.GetComponent<ShipFittings>(); return fittings; }
        }
        static IndustryTable Table => IndustryTable.Get();

        /// <summary>The site the frigate is at now (docked, or landed on its pad), or null.</summary>
        public IndustrySite Here()
        {
            if (carrier == null || carrier.Destroyed) return null;
            if (Docking.Docked && Docking.Instance != null) return IndustrySite.At(Docking.Instance.At);
            var landing = PlanetLanding.Instance;
            if (PlanetLanding.OnSurface && landing != null && landing.Landed)
                return IndustrySite.PadUnder(carrier.transform.position);
            return null;
        }

        public bool Open(Page page = Page.Storage)
        {
            var site = Here();
            if (site == null || site.Spec == null) return false;
            if (!IsOpen && !InputFocus.Take(InputFocus.Panel.Industry)) return false;
            Site = site;
            offeredAt = site;
            openedFrame = Time.frameCount;
            Show(page);
            SoundFX.Ui(Sfx.UiLock);
            return true;
        }

        public void Close()
        {
            if (!IsOpen) return;
            InputFocus.Release(InputFocus.Panel.Industry);
            SoundFX.Ui(Sfx.UiClear);
        }

        public void Show(Page page)
        {
            Current = page;
            selected = 0;
            scroll = 0;
            BuildRows();
        }

        void Say(string text, bool bad = false)
        {
            message = bad ? $"<color=#ff8070>{text}</color>" : text;
            messageUntil = Time.unscaledTime + 4f;
            if (bad) SoundFX.Ui(Sfx.UiError);
        }

        // ------------------------------------------------------------ rows

        void BuildRows()
        {
            rowsDue = Time.unscaledTime + 0.25f;
            rows.Clear();
            if (Site == null) return;
            var spec = Site.Spec;
            var stock = Site.State.stock;
            var hold = Hold;
            switch (Current)
            {
                case Page.Storage:
                    rows.Add(AllOre);
                    rows.Add(Everything);
                    var seen = new HashSet<string>();
                    if (hold != null) foreach (var e in hold.Contents()) if (seen.Add(e.id)) rows.Add(e.id);
                    foreach (var e in stock.Sorted(Table)) if (seen.Add(e.id)) rows.Add(e.id);
                    break;
                case Page.Refine:
                    foreach (var r in Table.refined) if (spec.Refines(r.id)) rows.Add(r.id);
                    break;
                case Page.Build:
                    foreach (var p in Table.parts) if (spec.Builds(p.id)) rows.Add(p.id);
                    break;
                case Page.Fit:
                    if (!spec.CanFit) break;
                    var fit = Fittings;
                    foreach (var p in Table.parts)
                        if ((fit != null && fit.IsFitted(p.id)) || stock.Amount(p.id) >= 1f || (hold != null && hold.Amount(p.id) >= 1f)) rows.Add(p.id);
                    break;
            }
            selected = Mathf.Clamp(selected, 0, Mathf.Max(0, rows.Count - 1));
        }

        string SelectedId => selected >= 0 && selected < rows.Count ? rows[selected] : null;

        bool PageAvailable(Page p)
        {
            var spec = Site != null ? Site.Spec : null;
            if (spec == null) return false;
            switch (p)
            {
                case Page.Refine: return spec.CanRefine;
                case Page.Build: return spec.CanBuild;
                case Page.Fit: return spec.CanFit;
            }
            return true;
        }

        // ------------------------------------------------------------ actions

        /// <summary>Do one of a row's actions (keys, pad, mouse and the self-test all come here).</summary>
        public void Act(string id, int action)
        {
            if (Site == null || id == null) return;
            var hold = Hold;
            var stock = Site.State.stock;
            switch (action)
            {
                case StoreOne:
                case StoreAll:
                    if (hold == null) return;
                    if (id == AllOre || id == Everything)
                    {
                        float moved = 0f;
                        foreach (var e in hold.Contents())
                        {
                            if (id == AllOre && Table.Kind(e.id) != ItemKind.Raw) continue;
                            moved += Table.Mass(e.id, Store(e.id, e.amount));
                        }
                        Say(moved > 0f ? $"Stored {moved:F0} t" : "Nothing to store", moved <= 0f);
                    }
                    else
                    {
                        float want = action == StoreAll ? hold.Amount(id) : Step(id);
                        float t = Store(id, want);
                        Say(t > 0f ? $"Stored {Table.Amount(id, t)} of {Table.Name(id).ToLower()}" : $"No {Table.Name(id).ToLower()} aboard", t <= 0f);
                    }
                    break;
                case LoadOne:
                case LoadAll:
                    if (hold == null || id == AllOre || id == Everything) return;
                    {
                        float want = action == LoadAll ? stock.Amount(id) : Mathf.Min(Step(id), stock.Amount(id));
                        if (want <= 0f) { Say($"No {Table.Name(id).ToLower()} in storage", true); break; }
                        float got = hold.Load(id, want);
                        stock.Take(id, got);
                        IndustryLedger.Touch();
                        if (got <= 0f) Say("The hold is full", true);
                        else Say($"Loaded {Table.Amount(id, got)} of {Table.Name(id).ToLower()}" + (got < want - 1e-3f ? " (the hold is full)" : ""));
                    }
                    break;
                case QueueOne:
                case QueueFive:
                    {
                        bool build = Current == Page.Build;
                        int n = 0, want = action == QueueFive ? 5 : 1;
                        string why = "";
                        while (n < want && IndustryLedger.Queue(Site.siteName, id, build, out why)) n++;
                        if (n > 0) Say($"Queued {n} x {Table.Name(id).ToLower()}");
                        else Say($"Can't {(build ? "build" : "refine")} {Table.Name(id).ToLower()}: {why}", true);
                    }
                    break;
                case CancelLast:
                    {
                        bool build = Current == Page.Build;
                        var q = build ? Site.State.building : Site.State.refining;
                        // (the last one queued of this recipe: never another's)
                        int i = q.FindLastIndex(j => j.recipe == id);
                        if (i >= 0 && IndustryLedger.Cancel(Site.siteName, build, i)) Say("Cancelled: its inputs are back in storage");
                        else Say($"No {Table.Name(id).ToLower()} queued", true);
                    }
                    break;
                case FitToggle:
                    {
                        var fit = Fittings;
                        if (fit == null) return;
                        if (fit.IsFitted(id))
                        {
                            if (!fit.CanUnfit(id, out string full)) { Say($"Can't take off the {Table.Name(id).ToLower()}: {full}", true); break; }
                            fit.Unfit(id);
                            stock.Add(id, 1f);
                            IndustryLedger.Touch();
                            Say($"Took off the {Table.Name(id).ToLower()} (into storage)");
                        }
                        else if (!fit.CanFit(id, out string why)) Say($"Can't fit the {Table.Name(id).ToLower()}: {why}", true);
                        else
                        {
                            bool fromStore = stock.Amount(id) >= 1f;
                            if (!fromStore && (hold == null || hold.Amount(id) < 1f)) { Say("None here to fit", true); break; }
                            if (fromStore) stock.Take(id, 1f); else hold.Unload(id, 1f);
                            fit.Fit(id);
                            IndustryLedger.Touch();
                            Say($"Fitted the {Table.Name(id).ToLower()}");
                            SoundFX.Ui(Sfx.UiLock);
                        }
                    }
                    break;
            }
            BuildRows();
        }

        /// <summary>Move up to `amount` from the hold into storage; returns how much went.</summary>
        float Store(string id, float amount)
        {
            var hold = Hold;
            if (hold == null) return 0f;
            float t = hold.Unload(id, amount);
            if (t > 0f) { Site.State.stock.Add(id, t); IndustryLedger.Touch(); }
            return t;
        }

        static float Step(string id) => Table.Part(id) != null ? 1f : 10f;

        /// <summary>Act on the selected row (for the self-test).</summary>
        public void ActOnSelected(int action) => Act(SelectedId, action);
        public static int Action(string name)
        {
            switch (name)
            {
                case "load": return LoadOne;
                case "loadAll": return LoadAll;
                case "store": return StoreOne;
                case "storeAll": return StoreAll;
                case "queue": return QueueOne;
                case "queue5": return QueueFive;
                case "fit": return FitToggle;
                case "cancel": return CancelLast;
            }
            return 0;
        }

        // ------------------------------------------------------------ input

        void Update()
        {
            var here = Here();
            if (IsOpen && here != Site) { Close(); return; }
            if (here == null) offeredAt = null;
            var k = Keyboard.current;
            var pad = Gamepad.current;
            if (!IsOpen)
            {
                // it opens by itself once on arriving; after that U (Back + RB)
                if (here == null || InputFocus.PanelUp) return;
                bool ask = (k != null && k.uKey.wasPressedThisFrame) || (pad != null && pad.selectButton.isPressed && pad.rightShoulder.wasPressedThisFrame);
                if (ask || offeredAt != here) Open();
                return;
            }
            if (Time.frameCount == openedFrame) return;
            // (what is aboard and stored changes as jobs finish: the list is looked at again a
            // few times a second, and after anything done here)
            if (Time.unscaledTime >= rowsDue) BuildRows();
            bool shift = k != null && (k.leftShiftKey.isPressed || k.rightShiftKey.isPressed);
            string id = SelectedId;
            if (k != null)
            {
                if (k.escapeKey.wasPressedThisFrame || k.uKey.wasPressedThisFrame) { Close(); return; }
                if (k.tabKey.wasPressedThisFrame) NextPage(shift ? -1 : 1);
                if (k.qKey.wasPressedThisFrame) NextPage(-1);
                if (k.eKey.wasPressedThisFrame) NextPage(1);
                if (k.upArrowKey.wasPressedThisFrame || k.wKey.wasPressedThisFrame) Move(-1);
                if (k.downArrowKey.wasPressedThisFrame || k.sKey.wasPressedThisFrame) Move(1);
                if (k.pageUpKey.wasPressedThisFrame) Move(-8);
                if (k.pageDownKey.wasPressedThisFrame) Move(8);
                if (Current == Page.Storage)
                {
                    if (k.leftArrowKey.wasPressedThisFrame || k.aKey.wasPressedThisFrame) Act(id, shift ? LoadAll : LoadOne);
                    if (k.rightArrowKey.wasPressedThisFrame || k.dKey.wasPressedThisFrame) Act(id, shift ? StoreAll : StoreOne);
                }
                if (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame) Primary(id, shift);
                if (k.backspaceKey.wasPressedThisFrame || k.deleteKey.wasPressedThisFrame)
                    if (Current == Page.Refine || Current == Page.Build) Act(id, CancelLast);
            }
            if (pad != null)
            {
                if (pad.buttonEast.wasPressedThisFrame || pad.startButton.wasPressedThisFrame) { Close(); return; }
                if (pad.leftShoulder.wasPressedThisFrame) NextPage(-1);
                if (pad.rightShoulder.wasPressedThisFrame && !pad.selectButton.isPressed) NextPage(1);
                if (pad.dpad.up.wasPressedThisFrame) Move(-1);
                if (pad.dpad.down.wasPressedThisFrame) Move(1);
                float y = pad.leftStick.y.ReadValue();
                if (Mathf.Abs(y) < 0.4f) stickRepeat = 0f;
                else if (Time.unscaledTime >= stickRepeat)
                {
                    Move(y > 0f ? -1 : 1);
                    stickRepeat = Time.unscaledTime + (stickRepeat == 0f ? 0.35f : 0.15f);
                }
                if (Current == Page.Storage)
                {
                    if (pad.dpad.left.wasPressedThisFrame) Act(id, LoadOne);
                    if (pad.dpad.right.wasPressedThisFrame) Act(id, StoreOne);
                    if (pad.buttonWest.wasPressedThisFrame) Act(id, StoreAll);
                    if (pad.buttonNorth.wasPressedThisFrame) Act(id, LoadAll);
                }
                else if (Current == Page.Refine || Current == Page.Build)
                {
                    if (pad.buttonWest.wasPressedThisFrame) Act(id, QueueFive);
                    if (pad.buttonNorth.wasPressedThisFrame) Act(id, CancelLast);
                }
                if (pad.buttonSouth.wasPressedThisFrame) Primary(id, false);
            }
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                Vector2 mp = mouse.position.ReadValue();
                Vector2 gui = new Vector2(mp.x, Screen.height - mp.y);
                for (int i = 0; i < tabRects.Length; i++)
                    if (tabRects[i].Contains(gui) && PageAvailable(Pages[i])) { Show(Pages[i]); return; }
                foreach (var (r, bid, action) in buttons)
                    if (r.Contains(gui)) { Act(bid, action); return; }
                for (int i = 0; i < rowRects.Count; i++)
                    if (rowRects[i].Contains(gui)) { selected = scroll + i; return; }
            }
            if (mouse != null)
            {
                float wheel = mouse.scroll.ReadValue().y;
                if (wheel > 0.1f) Move(-1);
                else if (wheel < -0.1f) Move(1);
            }
        }

        /// <summary>Enter / A: the row's main action.</summary>
        void Primary(string id, bool more)
        {
            switch (Current)
            {
                case Page.Storage:
                    if (id == AllOre || id == Everything) Act(id, StoreAll);
                    else if (Hold != null && Hold.Amount(id) > 0f) Act(id, StoreAll);
                    else Act(id, LoadAll);
                    break;
                case Page.Refine:
                case Page.Build:
                    Act(id, more ? QueueFive : QueueOne);
                    break;
                case Page.Fit:
                    Act(id, FitToggle);
                    break;
            }
        }

        void NextPage(int dir)
        {
            int i = System.Array.IndexOf(Pages, Current);
            for (int n = 0; n < Pages.Length; n++)
            {
                i = (i + dir + Pages.Length) % Pages.Length;
                if (PageAvailable(Pages[i])) { Show(Pages[i]); SoundFX.Ui(Sfx.UiLock); return; }
            }
        }

        void Move(int dir)
        {
            if (rows.Count == 0) return;
            selected = Mathf.Clamp(selected + dir, 0, rows.Count - 1);
        }

        // ------------------------------------------------------------ drawing

        static readonly Color Accent = new Color(1f, 0.72f, 0.3f);
        static readonly Color Good = new Color(0.5f, 0.9f, 0.55f);

        void OnGUI()
        {
            if (!IsOpen || Site == null) return;
            GUI.depth = -90;
            float s = Hud.S;
            float W = Mathf.Min(Screen.width - 24f * s, 1220f * s), H = Mathf.Min(Screen.height - 24f * s, 720f * s);
            var panel = new Rect((Screen.width - W) * 0.5f, (Screen.height - H) * 0.5f, W, H);
            Hud.Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0.01f, 0.03f, 0.45f));
            Hud.Fill(panel, new Color(0.03f, 0.05f, 0.09f, 0.96f));
            Hud.Outline(panel, Mathf.Max(1f, s), new Color(1f, 1f, 1f, 0.12f));
            buttons.Clear();
            float pad = 18f * s;
            var spec = Site.Spec;
            var hold = Hold;

            // the heading: where, what it does, the credits and the hold
            GUI.Label(new Rect(panel.x + pad, panel.y + pad * 0.6f, W * 0.6f, 34f * s), Site.siteName.ToUpper(), Hud.Text(24, true, TextAnchor.MiddleLeft));
            GUI.Label(new Rect(panel.x + pad, panel.y + pad * 0.6f + 30f * s, W * 0.6f, 22f * s),
                      $"<color=#9aa4b0>{IndustrySite.Services(spec)}  ·  {spec.description}</color>", Hud.Text(13, false, TextAnchor.MiddleLeft));
            if (hold != null)
            {
                var hr = new Rect(panel.xMax - pad - 360f * s, panel.y + pad * 0.6f, 360f * s, 24f * s);
                GUI.Label(hr, $"Credits  <color=#ffc04d>{hold.Credits:N0} cr</color>", Hud.Text(15, true, TextAnchor.MiddleRight));
                var bar = new Rect(hr.x, hr.yMax + 8f * s, hr.width, 12f * s);
                Hud.Fill(bar, new Color(1f, 1f, 1f, 0.08f));
                Hud.Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(hold.Fraction), bar.height), hold.Fraction > 0.95f ? new Color(1f, 0.45f, 0.35f) : new Color(0.35f, 0.65f, 1f));
                GUI.Label(new Rect(bar.x, bar.yMax + 2f * s, bar.width, 18f * s), $"Hold  {hold.Used:F0} / {hold.capacity:F0} t", Hud.Text(12, false, TextAnchor.MiddleRight));
            }

            // the pages
            float ty = panel.y + 80f * s;
            float tw = 150f * s;
            for (int i = 0; i < Pages.Length; i++)
            {
                var r = new Rect(panel.x + pad + i * (tw + 6f * s), ty, tw, 34f * s);
                tabRects[i] = r;
                bool on = Pages[i] == Current, can = PageAvailable(Pages[i]);
                Hud.Fill(r, on ? new Color(0.25f, 0.45f, 0.8f, 0.6f) : new Color(1f, 1f, 1f, can ? 0.06f : 0.02f));
                string name = Pages[i].ToString().ToUpper();
                GUI.Label(r, can ? name : $"<color=#5a6470>{name}</color>", Hud.Text(15, on, TextAnchor.MiddleCenter));
            }
            string tabsHint = Gamepad.current != null && PadSupport.UsingPad ? "LB / RB" : "Tab or Q / E";
            GUI.Label(new Rect(panel.x + pad + 4 * (tw + 6f * s), ty, 200f * s, 34f * s), $"<color=#7a8490>{tabsHint}</color>", Hud.Text(12, false, TextAnchor.MiddleLeft));

            // the list on the left, the selected row's details on the right
            float top = ty + 46f * s, bottom = panel.yMax - pad - 28f * s;
            bool queues = Current == Page.Refine || Current == Page.Build;
            float listBottom = queues ? bottom - 150f * s : bottom;
            var list = new Rect(panel.x + pad, top, W * 0.6f - pad, listBottom - top);
            var detail = new Rect(list.xMax + pad, top, panel.xMax - pad - (list.xMax + pad), bottom - top);
            DrawList(list, s);
            DrawDetail(detail, s);
            if (queues) DrawQueue(new Rect(list.x, listBottom + 10f * s, list.width, bottom - listBottom - 10f * s), s);

            // the message line and the keys
            if (Time.unscaledTime < messageUntil)
                GUI.Label(new Rect(panel.x + pad, bottom + 4f * s, W * 0.6f, 24f * s), message, Hud.Text(14, true, TextAnchor.MiddleLeft));
            GUI.Label(new Rect(panel.x + W * 0.45f, bottom + 4f * s, W * 0.55f - pad, 24f * s), $"<color=#8a94a0>{KeysHint()}</color>", Hud.Text(12, false, TextAnchor.MiddleRight));
        }

        string KeysHint()
        {
            bool padUsed = Gamepad.current != null && PadSupport.UsingPad;
            switch (Current)
            {
                case Page.Storage: return padUsed ? "D-pad ← load  → store   X store all   Y load all   B close" : "← load  → store   Shift for all   Enter all   U / Esc close";
                case Page.Refine:
                case Page.Build: return padUsed ? "A queue one   X queue five   Y cancel last   B close" : "Enter queue one   Shift+Enter five   Backspace cancel   U / Esc close";
                default: return padUsed ? "A fit / take off   B close" : "Enter fit / take off   U / Esc close";
            }
        }

        void DrawList(Rect area, float s)
        {
            rowRects.Clear();
            float rowH = 44f * s;
            int fits = Mathf.Max(1, Mathf.FloorToInt(area.height / rowH));
            if (selected < scroll) scroll = selected;
            if (selected >= scroll + fits) scroll = selected - fits + 1;
            scroll = Mathf.Clamp(scroll, 0, Mathf.Max(0, rows.Count - fits));
            if (rows.Count == 0)
            {
                string why = Current == Page.Fit ? "No parts here to fit: build them in BUILD, or bring them in the hold."
                                                 : Current == Page.Storage ? "Nothing aboard or in storage." : "Nothing can be made here.";
                GUI.Label(new Rect(area.x, area.y, area.width, 40f * s), $"<color=#9aa4b0>{why}</color>", Hud.Text(14, false, TextAnchor.MiddleLeft));
                return;
            }
            // column headings
            for (int i = 0; i < fits && scroll + i < rows.Count; i++)
            {
                var r = new Rect(area.x, area.y + i * rowH, area.width, rowH - 4f * s);
                rowRects.Add(r);
                int index = scroll + i;
                bool sel = index == selected;
                Hud.Fill(r, sel ? new Color(0.25f, 0.45f, 0.8f, 0.5f) : new Color(1f, 1f, 1f, 0.04f));
                DrawRow(r, rows[index], sel, s);
            }
            if (rows.Count > fits)
                GUI.Label(new Rect(area.x, area.yMax - 2f * s, area.width, 16f * s), $"<color=#7a8490>{scroll + 1}-{Mathf.Min(rows.Count, scroll + fits)} of {rows.Count}</color>", Hud.Text(11, false, TextAnchor.UpperRight));
        }

        void DrawRow(Rect r, string id, bool sel, float s)
        {
            var hold = Hold;
            var stock = Site.State.stock;
            float x = r.x + 6f * s;
            if (id == AllOre || id == Everything)
            {
                GUI.Label(new Rect(x + 4f * s, r.y, r.width - 200f * s, r.height), id == AllOre ? "Store all the ore aboard" : "Store everything aboard", Hud.Text(15, sel, TextAnchor.MiddleLeft));
                Button(new Rect(r.xMax - 96f * s, r.y + 7f * s, 90f * s, r.height - 14f * s), "Store", id, StoreAll, s);
                return;
            }
            var icon = IndustryTable.Icon(id);
            var ir = new Rect(x, r.y + 2f * s, r.height - 4f * s, r.height - 4f * s);
            if (icon != null) GUI.DrawTexture(ir, icon, ScaleMode.ScaleToFit);
            else if (IndustryTable.AsMineral(id, out var m))
            {
                // a raw mineral: its colour swatch and symbol
                Hud.Fill(new Rect(ir.x + ir.width * 0.2f, ir.y + ir.height * 0.2f, ir.width * 0.6f, ir.height * 0.6f), Minerals.Get(m).color);
                GUI.Label(ir, $"<color=#000000>{Minerals.Symbol(m)}</color>", Hud.Text(11, true, TextAnchor.MiddleCenter));
            }
            x = ir.xMax + 8f * s;
            var t = Table;
            switch (Current)
            {
                case Page.Storage:
                    {
                        float aboard = hold != null ? hold.Amount(id) : 0f, stored = stock.Amount(id);
                        GUI.Label(new Rect(x, r.y, r.width * 0.38f, r.height), t.Name(id), Hud.Text(15, sel, TextAnchor.MiddleLeft));
                        float bx = r.xMax - 4f * s;
                        // [<< all] [< ] aboard | stored [ >] [all >>]
                        float bw = 44f * s, gap = 4f * s, by = r.y + 7f * s, bh = r.height - 14f * s;
                        Button(new Rect(bx - bw, by, bw, bh), "»", id, StoreAll, s);
                        Button(new Rect(bx - 2f * bw - gap, by, bw, bh), "›", id, StoreOne, s);
                        float colW = 92f * s;
                        float storedX = bx - 2f * bw - gap * 2f - colW;
                        GUI.Label(new Rect(storedX, r.y, colW, r.height), stored > 0f ? t.Amount(id, stored) : "<color=#5a6470>-</color>", Hud.Text(14, false, TextAnchor.MiddleRight));
                        float loadX = storedX - gap * 2f - 2f * bw - gap;
                        Button(new Rect(loadX + bw + gap, by, bw, bh), "‹", id, LoadOne, s);
                        Button(new Rect(loadX, by, bw, bh), "«", id, LoadAll, s);
                        GUI.Label(new Rect(loadX - colW - gap, r.y, colW, r.height), aboard > 0f ? t.Amount(id, aboard) : "<color=#5a6470>-</color>", Hud.Text(14, false, TextAnchor.MiddleRight));
                    }
                    break;
                case Page.Refine:
                case Page.Build:
                    {
                        bool build = Current == Page.Build;
                        var inputs = build ? t.Part(id).inputs : t.Refined(id).inputs;
                        string outText = build ? $"{t.Name(id)}" : $"{t.Name(id)}  <color=#9aa4b0>{t.Refined(id).makes:0.##} t</color>";
                        GUI.Label(new Rect(x, r.y + 1f * s, r.width * 0.45f, r.height * 0.55f), outText, Hud.Text(15, sel, TextAnchor.MiddleLeft));
                        GUI.Label(new Rect(x, r.y + r.height * 0.5f, r.width - (x - r.x) - 110f * s, r.height * 0.5f), Inputs(inputs, stock), Hud.Text(12, false, TextAnchor.MiddleLeft));
                        float secs = build ? t.Part(id).seconds : t.Refined(id).seconds;
                        GUI.Label(new Rect(r.xMax - 210f * s, r.y, 100f * s, r.height * 0.55f), $"<color=#9aa4b0>{secs:F0} s</color>", Hud.Text(12, false, TextAnchor.MiddleRight));
                        int queued = 0;
                        foreach (var j in build ? Site.State.building : Site.State.refining) if (j.recipe == id) queued++;
                        if (queued > 0) GUI.Label(new Rect(r.xMax - 210f * s, r.y + r.height * 0.45f, 100f * s, r.height * 0.55f), $"<color=#ffc04d>{queued} queued</color>", Hud.Text(11, false, TextAnchor.MiddleRight));
                        bool can = IndustryLedger.CanQueue(Site.siteName, id, build, out _);
                        string verb = build ? "Build" : "Refine";
                        Button(new Rect(r.xMax - 100f * s, r.y + 7f * s, 94f * s, r.height - 14f * s), can ? verb : $"<color=#8a94a0>{verb}</color>", id, QueueOne, s, can);
                    }
                    break;
                case Page.Fit:
                    {
                        var fit = Fittings;
                        bool fitted = fit != null && fit.IsFitted(id);
                        GUI.Label(new Rect(x, r.y + 1f * s, r.width * 0.5f, r.height * 0.55f), t.Name(id), Hud.Text(15, sel, TextAnchor.MiddleLeft));
                        GUI.Label(new Rect(x, r.y + r.height * 0.5f, r.width * 0.6f, r.height * 0.5f), $"<color=#9aa4b0>{Effects(t.Part(id))}</color>", Hud.Text(12, false, TextAnchor.MiddleLeft));
                        string where = fitted ? "<color=#7ee08a>fitted</color>"
                                     : $"<color=#9aa4b0>{(stock.Amount(id) >= 1f ? $"{stock.Amount(id):F0} stored" : "")}{(hold != null && hold.Amount(id) >= 1f ? $" {hold.Amount(id):F0} aboard" : "")}</color>";
                        GUI.Label(new Rect(r.xMax - 260f * s, r.y, 150f * s, r.height), where, Hud.Text(12, false, TextAnchor.MiddleRight));
                        Button(new Rect(r.xMax - 100f * s, r.y + 7f * s, 94f * s, r.height - 14f * s), fitted ? "Take off" : "Fit", id, FitToggle, s);
                    }
                    break;
            }
        }

        void Button(Rect r, string label, string id, int action, float s, bool ready = true)
        {
            buttons.Add((r, id, action));
            // (a job that can be queued now stands out; one still short of its inputs doesn't)
            Hud.Fill(r, ready ? new Color(0.3f, 0.55f, 0.9f, 0.35f) : new Color(1f, 1f, 1f, 0.07f));
            Hud.Outline(r, Mathf.Max(1f, s), new Color(1f, 1f, 1f, ready ? 0.3f : 0.12f));
            GUI.Label(r, label, Hud.Text(14, true, TextAnchor.MiddleCenter));
        }

        static string Inputs(Ingredient[] inputs, Stock stock)
        {
            var t = Table;
            var sb = new System.Text.StringBuilder();
            foreach (var i in inputs)
            {
                if (sb.Length > 0) sb.Append("  ");
                bool have = stock.Has(i.item, i.tonnes);
                sb.Append(have ? "<color=#9ad0a0>" : "<color=#d08070>");
                sb.Append($"{t.Name(i.item)} {i.tonnes:0.##} t");
                sb.Append("</color>");
            }
            return sb.ToString();
        }

        static readonly Dictionary<string, string> StatNames = new Dictionary<string, string>
        {
            { "hull", "hull" }, { "shield", "shield" }, { "recharge", "shield recharge" }, { "speed", "top speed" },
            { "acceleration", "acceleration" }, { "turn", "turn rate" }, { "cargo", "hold" }, { "gunDamage", "gun damage" },
            { "gunRange", "gun range" }, { "gunRate", "rate of fire" }, { "beamDamage", "beam damage" }, { "beamCharge", "beam charge time" },
            { "miningRate", "mining rate" },
        };

        public static string StatName(string stat) => StatNames.TryGetValue(stat, out var n) ? n : stat;

        public static string Effects(PartSpec p)
        {
            if (p?.effects == null) return "";
            var parts = new List<string>();
            foreach (var e in p.effects)
            {
                float pc = (e.factor - 1f) * 100f;
                parts.Add($"{StatName(e.stat)} {(pc >= 0f ? "+" : "")}{pc:F0}%");
            }
            return string.Join(", ", parts);
        }

        void DrawDetail(Rect area, float s)
        {
            Hud.Fill(area, new Color(1f, 1f, 1f, 0.03f));
            float pad = 12f * s;
            var t = Table;
            string id = SelectedId;
            float y = area.y + pad;
            if (Current == Page.Fit || (Current != Page.Storage && id == null))
            {
                DrawShip(new Rect(area.x + pad, area.yMax - 270f * s, area.width - pad * 2f, 260f * s), s);
                if (id == null) return;
            }
            if (id == null || id == AllOre || id == Everything)
            {
                var hold = Hold;
                if (hold != null)
                    GUI.Label(new Rect(area.x + pad, y, area.width - pad * 2f, 120f * s),
                              $"Aboard: {hold.Used:F0} of {hold.capacity:F0} t\nOre {hold.OreTonnes:F0} t, worth {hold.Value:N0} cr\nIn storage here: {Site.State.stock.Mass(t):F0} t",
                              Hud.Text(14, false, TextAnchor.UpperLeft));
                return;
            }
            float pic = Mathf.Min(170f * s, area.width * 0.45f);
            var icon = IndustryTable.Icon(id);
            var pr = new Rect(area.x + pad, y, pic, pic);
            Hud.Fill(pr, new Color(0f, 0f, 0f, 0.35f));
            if (icon != null) GUI.DrawTexture(pr, icon, ScaleMode.ScaleToFit);
            float tx = pr.xMax + pad;
            float tw = area.xMax - pad - tx;
            GUI.Label(new Rect(tx, y, tw, 26f * s), t.Name(id), Hud.Text(18, true, TextAnchor.UpperLeft));
            var kind = t.Kind(id);
            string kindText = kind == ItemKind.Raw ? "Raw mineral" : kind == ItemKind.Refined ? "Refined material" : "Ship part";
            GUI.Label(new Rect(tx, y + 26f * s, tw, 20f * s), $"<color=#9aa4b0>{kindText}  ·  {t.Value(id):N0} cr{(kind == ItemKind.Part ? " each" : " a tonne")}</color>", Hud.Text(12, false, TextAnchor.UpperLeft));
            GUI.Label(new Rect(tx, y + 48f * s, tw, pic - 48f * s), t.Describe(id), Hud.Text(13, false, TextAnchor.UpperLeft));
            y = pr.yMax + pad;
            var sb = new System.Text.StringBuilder();
            if (kind == ItemKind.Refined)
            {
                var r = t.Refined(id);
                sb.Append($"Refined from: {Recipe(r.inputs)}\nMakes {r.makes:0.##} t in {r.seconds:F0} s");
            }
            else if (kind == ItemKind.Part)
            {
                var p = t.Part(id);
                sb.Append($"Built from: {Recipe(p.inputs)}\nTakes {p.seconds:F0} s  ·  weighs {p.mass:F0} t\nFitted: {Effects(p)}");
            }
            else if (IndustryTable.AsMineral(id, out _))
            {
                var used = new List<string>();
                foreach (var r in t.refined) foreach (var i in r.inputs) if (i.item == id) { used.Add(r.name.ToLower()); break; }
                sb.Append(used.Count > 0 ? $"Refined into {string.Join(", ", used)}" : "Not used in refining: sell it at a market");
            }
            GUI.Label(new Rect(area.x + pad, y, area.width - pad * 2f, 120f * s), sb.ToString(), Hud.Text(13, false, TextAnchor.UpperLeft));
        }

        static string Recipe(Ingredient[] inputs)
        {
            var t = Table;
            var list = new List<string>();
            foreach (var i in inputs) list.Add($"{i.tonnes:0.##} t {t.Name(i.item).ToLower()}");
            return string.Join(", ", list);
        }

        /// <summary>The frigate's numbers now, against as built, and its slots.</summary>
        void DrawShip(Rect area, float s)
        {
            var fit = Fittings;
            if (fit == null) return;
            Hud.Fill(area, new Color(0f, 0f, 0f, 0.25f));
            float y = area.y + 8f * s, x = area.x + 10f * s;
            GUI.Label(new Rect(x, y, area.width, 22f * s), $"{PlayerFrigate.Flying.ToUpper()}  <color=#9aa4b0>slots {fit.Fitted.Count} of {fit.slots}</color>", Hud.Text(14, true, TextAnchor.UpperLeft));
            y += 26f * s;
            var lines = new System.Text.StringBuilder();
            foreach (var stat in IndustryTable.Stats)
            {
                float f = fit.Factor(stat);
                if (Mathf.Approximately(f, 1f)) continue;
                float pc = (f - 1f) * 100f;
                lines.Append($"{StatName(stat)}  <color=#7ee08a>{(pc >= 0 ? "+" : "")}{pc:F0}%</color>\n");
            }
            if (lines.Length == 0) lines.Append("<color=#9aa4b0>No parts fitted: the frigate is as built.</color>");
            GUI.Label(new Rect(x, y, area.width - 20f * s, area.height - 34f * s), lines.ToString(), Hud.Text(12, false, TextAnchor.UpperLeft));
        }

        void DrawQueue(Rect area, float s)
        {
            bool build = Current == Page.Build;
            var q = build ? Site.State.building : Site.State.refining;
            Hud.Fill(area, new Color(0f, 0f, 0f, 0.3f));
            var t = Table;
            GUI.Label(new Rect(area.x + 8f * s, area.y + 4f * s, area.width, 20f * s),
                      $"{(build ? "FACTORY" : "REFINERY")} QUEUE  <color=#9aa4b0>{q.Count} of {IndustryLedger.QueueLimit}</color>", Hud.Text(13, true, TextAnchor.UpperLeft));
            float y = area.y + 28f * s, rowH = 22f * s;
            int shown = Mathf.FloorToInt((area.height - 30f * s) / rowH);
            for (int i = 0; i < q.Count && i < shown; i++)
            {
                var j = q[i];
                var r = new Rect(area.x + 8f * s, y, area.width - 16f * s, rowH - 4f * s);
                if (i == 0)
                {
                    Hud.Fill(r, new Color(1f, 1f, 1f, 0.06f));
                    Hud.Fill(new Rect(r.x, r.y, r.width * j.Progress, r.height), new Color(0.35f, 0.65f, 1f, 0.45f));
                }
                string name = t.Name(j.recipe);
                string when = i == 0 ? $"{j.left / Mathf.Max(0.01f, IndustryLedger.SpeedUp):F0} s left" : "waiting";
                GUI.Label(r, $"  {name}  <color=#9aa4b0>{when}</color>", Hud.Text(12, i == 0, TextAnchor.MiddleLeft));
                y += rowH;
            }
            if (q.Count == 0) GUI.Label(new Rect(area.x + 8f * s, y, area.width, rowH), "<color=#9aa4b0>Idle</color>", Hud.Text(12, false, TextAnchor.MiddleLeft));
        }

        /// <summary>Where a tab is on screen (for the self-test's mouse).</summary>
        public Vector2 TabScreenPos(Page p)
        {
            var r = tabRects[System.Array.IndexOf(Pages, p)];
            return new Vector2(r.center.x, Screen.height - r.center.y);
        }

        /// <summary>Select the row for an item (for the self-test); false if it isn't listed.</summary>
        public bool SelectItem(string id)
        {
            BuildRows();
            int i = rows.IndexOf(id);
            if (i < 0) return false;
            selected = i;
            return true;
        }
    }
}

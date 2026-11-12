using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The start menu: as a game starts, choose the frigate to fly (FrigateRoster) and the captain
    /// on the bridge. The chosen frigate turns slowly in a preview, with bars for how it compares
    /// to the others and a few words about it. The game waits while the menu is up.
    ///
    /// Launch with a different frigate from the one in the level and the level starts again with
    /// the new one (PlayerFrigate puts it in); with the same one, the game just begins. The menu
    /// shows once a run (the pause menu's Ship item brings it back), and never for the self-tests,
    /// the autotest, the reels, or a frigate given on the command line or the page address.
    ///
    /// Mouse (click a frigate, a captain or Launch), keyboard (up/down or W/S the frigate,
    /// left/right or A/D the captain, Enter or Space to launch) and pad (D-pad or left stick up and
    /// down the frigate, D-pad left/right or LB/RB the captain, A to launch).
    /// </summary>
    public class StartMenu : MonoBehaviour
    {
        public static bool IsOpen => InputFocus.Has(InputFocus.Panel.StartMenu);

        /// <summary>For the frigates reel: choosing a frigate swaps it in where the ship is
        /// (PlayerFrigate.SwapTo) rather than starting the level again, and the game doesn't wait
        /// while the menu is up.</summary>
        public static bool LiveSwap;

        /// <summary>The menu will come up as this level starts.</summary>
        public static bool WillOpen
        {
            get
            {
                var roster = FrigateRoster.Get();
                return !FrigateChoice.Chosen && !LaunchArgs.Automated && !Hud.ReelMode && FrigateChoice.Given == null
                       && roster != null && roster.frigates.Length > 0;
            }
        }

        FrigateRoster roster;
        int selected;
        bool paused;
        float savedScale = 1f, stickRepeat;
        int openedFrame = -10;
        readonly List<Rect> rowRects = new List<Rect>();
        readonly Rect[] captainRects = new Rect[Captain.Roster.Length];
        Rect launchRect;
        FrigateStats[] stats;
        FrigateStats best;

        // the preview: a copy of the chosen frigate's model, on its own layer, turning in front of
        // a camera that draws into a texture
        const int PreviewLayer = 31;
        static readonly Vector3 PreviewAt = new Vector3(0f, -60000f, 0f);
        Camera previewCam;
        RenderTexture previewTex;
        GameObject previewModel;
        int previewOf = -1;
        float previewYaw = 200f;
        int savedMainMask;
        Camera savedMain;

        void Start()
        {
            if (WillOpen) Open();
        }

        public void Open()
        {
            roster = FrigateRoster.Get();
            if (roster == null || roster.frigates.Length == 0 || IsOpen || !InputFocus.Take(InputFocus.Panel.StartMenu)) return;
            openedFrame = Time.frameCount;
            selected = Mathf.Max(0, roster.IndexOf(string.IsNullOrEmpty(PlayerFrigate.Flying) ? FrigateChoice.Current : PlayerFrigate.Flying));
            stats = new FrigateStats[roster.frigates.Length];
            best = new FrigateStats();
            for (int i = 0; i < stats.Length; i++)
            {
                var s = stats[i] = FrigateStats.Of(roster.frigates[i].prefab);
                best.speed = Mathf.Max(best.speed, s.speed);
                best.turnRate = Mathf.Max(best.turnRate, s.turnRate);
                best.hull = Mathf.Max(best.hull, s.hull);
                best.shield = Mathf.Max(best.shield, s.shield);
                best.gunDps = Mathf.Max(best.gunDps, s.gunDps);
                best.ciws = Mathf.Max(best.ciws, s.ciws);
                best.beamDamage = Mathf.Max(best.beamDamage, s.beamDamage);
                best.cargo = Mathf.Max(best.cargo, s.cargo);
                best.slots = Mathf.Max(best.slots, s.slots);
                best.crew = Mathf.Max(best.crew, s.crew);
            }
            // (in co-op the game goes on, as with the pause menu: the others are still playing)
            paused = !LiveSwap && !CoopClient.Active;
            if (paused)
            {
                savedScale = Time.timeScale > 0f ? Time.timeScale : 1f;
                Time.timeScale = 0f;
                AudioListener.pause = true;
            }
            MakePreview();
        }

        public void Close()
        {
            if (!IsOpen) return;
            InputFocus.Release(InputFocus.Panel.StartMenu);
            if (paused) { Time.timeScale = savedScale; AudioListener.pause = false; paused = false; }
            DropPreview();
        }

        void OnDestroy()
        {
            if (IsOpen) Close();
        }

        /// <summary>Fly the chosen frigate with the chosen captain.</summary>
        public void Launch()
        {
            var e = roster.frigates[selected];
            FrigateChoice.Set(e.id);
            FrigateChoice.Chosen = true;
            if (CoopClient.Active) CoopClient.Instance.SendCaptain();
            bool change = !string.Equals(e.id, PlayerFrigate.Flying, System.StringComparison.OrdinalIgnoreCase);
            Close();
            SoundFX.Ui(Sfx.UiLock);
            if (change && !LiveSwap) { GameMenu.ReloadLevel(); return; }
            if (change) PlayerFrigate.SwapTo(e.id);
            var coop = GetComponent<CoopMenu>();
            if (coop != null) coop.Offer();
        }

        /// <summary>The frigate highlighted (for the reel, which steps through them).</summary>
        public int Selected => selected;
        public void Select(int i) => Choose(i);

        void Choose(int i)
        {
            if (roster == null || roster.frigates.Length == 0) return;
            selected = (i + roster.frigates.Length) % roster.frigates.Length;
            SoundFX.Ui(Sfx.UiClear);
        }

        // ------------------------------------------------------------ input

        void Update()
        {
            if (!IsOpen || Time.frameCount == openedFrame) return;
            var k = Keyboard.current;
            var pad = Gamepad.current;
            if (k != null)
            {
                if (k.upArrowKey.wasPressedThisFrame || k.wKey.wasPressedThisFrame) Choose(selected - 1);
                if (k.downArrowKey.wasPressedThisFrame || k.sKey.wasPressedThisFrame) Choose(selected + 1);
                if (k.leftArrowKey.wasPressedThisFrame || k.aKey.wasPressedThisFrame) Captain.Index--;
                if (k.rightArrowKey.wasPressedThisFrame || k.dKey.wasPressedThisFrame) Captain.Index++;
                if (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame) { Launch(); return; }
            }
            if (pad != null)
            {
                if (pad.dpad.up.wasPressedThisFrame) Choose(selected - 1);
                if (pad.dpad.down.wasPressedThisFrame) Choose(selected + 1);
                if (pad.dpad.left.wasPressedThisFrame || pad.leftShoulder.wasPressedThisFrame) Captain.Index--;
                if (pad.dpad.right.wasPressedThisFrame || pad.rightShoulder.wasPressedThisFrame) Captain.Index++;
                float y = pad.leftStick.y.ReadValue();
                if (Mathf.Abs(y) < 0.4f) stickRepeat = 0f;
                else if (Time.unscaledTime >= stickRepeat)
                {
                    Choose(selected + (y > 0f ? -1 : 1));
                    stickRepeat = Time.unscaledTime + (stickRepeat == 0f ? 0.35f : 0.2f);
                }
                if (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame) { Launch(); return; }
            }
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                Vector2 mp = mouse.position.ReadValue();
                var gui = new Vector2(mp.x, Screen.height - mp.y);
                for (int i = 0; i < rowRects.Count; i++) if (rowRects[i].Contains(gui)) { Choose(i); return; }
                for (int i = 0; i < captainRects.Length; i++) if (captainRects[i].Contains(gui)) { Captain.Index = i; return; }
                if (launchRect.Contains(gui)) Launch();
            }
        }

        // ------------------------------------------------------------ the preview

        void MakePreview()
        {
            if (previewCam != null) return;
            previewTex = new RenderTexture(1024, 576, 24) { name = "Frigate preview", antiAliasing = 4 };
            var go = new GameObject("Frigate preview camera");
            previewCam = go.AddComponent<Camera>();
            previewCam.targetTexture = previewTex;
            previewCam.cullingMask = 1 << PreviewLayer;
            previewCam.clearFlags = CameraClearFlags.SolidColor;
            previewCam.backgroundColor = new Color(0.02f, 0.03f, 0.06f, 1f);
            previewCam.fieldOfView = 30f;
            previewCam.nearClipPlane = 5f;
            previewCam.farClipPlane = 3000f;
            var light = new GameObject("Frigate preview light").AddComponent<Light>();
            light.transform.SetParent(go.transform, false);
            light.type = LightType.Directional;
            light.intensity = 1.6f;
            light.cullingMask = 1 << PreviewLayer;
            light.transform.rotation = Quaternion.Euler(35f, -40f, 0f);
            // the level's camera leaves the preview layer alone
            savedMain = Camera.main;
            if (savedMain != null) { savedMainMask = savedMain.cullingMask; savedMain.cullingMask &= ~(1 << PreviewLayer); }
        }

        void DropPreview()
        {
            if (previewModel != null) Destroy(previewModel);
            if (previewCam != null) Destroy(previewCam.gameObject);
            if (previewTex != null) { previewTex.Release(); Destroy(previewTex); }
            if (savedMain != null) savedMain.cullingMask = savedMainMask;
            previewModel = null;
            previewCam = null;
            previewTex = null;
            previewOf = -1;
        }

        /// <summary>A copy of a frigate's visible parts (no behaviours): its hull, turrets, CIWS and
        /// bow gun, without the shield bubble or engine plumes.</summary>
        public static GameObject ModelOf(GameObject prefab)
        {
            var root = new GameObject("Frigate preview model");
            var rt = prefab.transform;
            foreach (var mr in prefab.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!mr.enabled || mr.GetComponentInParent<Shield>(true) != null) continue;
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var part = new GameObject(mr.name);
                part.layer = PreviewLayer;
                part.transform.SetParent(root.transform, false);
                var m = rt.worldToLocalMatrix * mr.transform.localToWorldMatrix;
                part.transform.localPosition = m.GetColumn(3);
                part.transform.localRotation = m.rotation;
                part.transform.localScale = m.lossyScale;
                part.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var r = part.AddComponent<MeshRenderer>();
                r.sharedMaterials = mr.sharedMaterials;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return root;
        }

        void LateUpdate()
        {
            if (!IsOpen || previewCam == null || roster == null) return;
            if (previewOf != selected)
            {
                if (previewModel != null) Destroy(previewModel);
                previewModel = ModelOf(roster.frigates[selected].prefab);
                previewModel.transform.position = PreviewAt;
                previewOf = selected;
            }
            previewYaw += 14f * (paused ? Time.unscaledDeltaTime : Time.deltaTime);
            // put back every frame: while the game runs the level may shift everything (to keep
            // the player's ship near the middle of the world), the preview with it
            previewModel.transform.SetPositionAndRotation(PreviewAt, Quaternion.Euler(0f, previewYaw, 0f));
            float len = Mathf.Max(100f, stats[selected].hull > 0f ? Bounds(previewModel).size.magnitude : 200f);
            var cam = previewCam.transform;
            cam.position = PreviewAt + new Vector3(0f, len * 0.32f, -len * 1.45f);
            cam.LookAt(PreviewAt);
        }

        static Bounds Bounds(GameObject go)
        {
            var b = new Bounds(go.transform.position, Vector3.zero);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            return b;
        }

        // ------------------------------------------------------------ drawing

        void OnGUI()
        {
            if (!IsOpen || roster == null) return;
            GUI.depth = -110;   // over the HUD and the pause menu
            float s = Hud.S;
            Hud.Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0.01f, 0.03f, 0.92f));
            float pad = 24f * s;
            float w = Mathf.Min(Screen.width - pad * 2f, 1500f * s), h = Mathf.Min(Screen.height - pad * 2f, 860f * s);
            var panel = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            Hud.Panel(panel);
            GUI.Label(new Rect(panel.x, panel.y + pad * 0.6f, w, 40f * s), "CHOOSE YOUR FRIGATE AND CAPTAIN", Hud.Text(26, true, TextAnchor.MiddleCenter));

            // the frigates, down the left
            float top = panel.y + pad * 0.6f + 56f * s, listW = Mathf.Min(360f * s, w * 0.3f), rowH = 74f * s;
            rowRects.Clear();
            for (int i = 0; i < roster.frigates.Length; i++)
            {
                var e = roster.frigates[i];
                var r = new Rect(panel.x + pad, top + i * (rowH + 8f * s), listW, rowH);
                rowRects.Add(r);
                bool sel = i == selected;
                Hud.Fill(r, sel ? new Color(0.25f, 0.45f, 0.8f, 0.55f) : new Color(1f, 1f, 1f, 0.05f));
                if (sel) Hud.Outline(r, Mathf.Max(1f, 2f * s), new Color(1f, 0.72f, 0.3f));
                GUI.Label(new Rect(r.x + 14f * s, r.y + 8f * s, r.width - 20f * s, 28f * s), $"<b>{e.displayName.ToUpperInvariant()}</b>", Hud.Text(20, false));
                GUI.Label(new Rect(r.x + 14f * s, r.y + 38f * s, r.width - 20f * s, 24f * s), $"<color=#9aa4b0>{e.role}</color>", Hud.Text(14, false));
            }

            // the chosen one: its picture, its bars and its description
            float rx = panel.x + pad * 2f + listW, rw = panel.xMax - pad - rx;
            // the picture leaves the bars beside it room to read
            float picW = Mathf.Min(rw * 0.55f, h * 0.46f * 16f / 9f);
            var pic = new Rect(rx, top, picW, picW * 9f / 16f);
            if (previewTex != null) GUI.DrawTexture(pic, previewTex, ScaleMode.ScaleToFit, false);
            Hud.Outline(pic, Mathf.Max(1f, s), new Color(1f, 1f, 1f, 0.15f));
            var st = stats[selected];
            float bx = pic.xMax + pad, bw = panel.xMax - pad - bx, by = top;
            if (bw < 220f * s) { bx = rx; bw = rw; by = pic.yMax + 10f * s; }
            void Bar(string label, float v, float max, string value)
            {
                float lw = 110f * s, vw = 92f * s, bh = 26f * s;
                GUI.Label(new Rect(bx, by, lw, bh), $"<b>{label}</b>", Hud.Text(14, false, TextAnchor.MiddleLeft));
                var track = new Rect(bx + lw, by + bh * 0.3f, Mathf.Max(20f, bw - lw - vw), bh * 0.4f);
                Hud.Fill(track, new Color(1f, 1f, 1f, 0.1f));
                Hud.Fill(new Rect(track.x, track.y, track.width * Mathf.Clamp01(max > 0f ? v / max : 0f), track.height), new Color(0.45f, 0.75f, 1f, 0.9f));
                GUI.Label(new Rect(track.xMax + 8f * s, by, vw, bh), value, Hud.Text(13, false, TextAnchor.MiddleLeft));
                by += bh + 4f * s;
            }
            Bar("Speed", st.speed, best.speed, $"{st.speed:F0} m/s");
            Bar("Turning", st.turnRate, best.turnRate, $"{st.turnRate:F0}°/s");
            Bar("Hull", st.hull, best.hull, $"{st.hull:F0}");
            Bar("Shield", st.shield, best.shield, $"{st.shield:F0}");
            Bar("Guns", st.gunDps, best.gunDps, $"{st.turrets} × {st.barrels / Mathf.Max(1, st.turrets)}");
            Bar("Point defence", st.ciws, best.ciws, $"{st.ciws} CIWS");
            Bar("Main gun", st.beamDamage, best.beamDamage, $"{st.beamCharge:F0} s charge");
            Bar("Cargo", st.cargo, best.cargo, $"{st.cargo:F0} t");
            Bar("Part slots", st.slots, best.slots, $"{st.slots} slots");
            Bar("Crew", st.crew, best.crew, $"{st.crew}");
            float dy = Mathf.Max(pic.yMax, by) + 12f * s;
            var desc = Hud.Text(15, false);
            desc.wordWrap = true;
            GUI.Label(new Rect(rx, dy, rw, 90f * s), roster.frigates[selected].description, desc);
            desc.wordWrap = false;

            // the captains, along the bottom
            int n = Captain.Roster.Length;
            float thumb = Mathf.Min(96f * s, (w - pad * 2f) / n - 10f * s), gap = 10f * s;
            float cy = panel.yMax - pad - 38f * s - thumb - 26f * s;
            GUI.Label(new Rect(panel.x + pad, cy - 30f * s, 300f * s, 26f * s), "<b>CAPTAIN</b>", Hud.Text(16, false));
            for (int i = 0; i < n; i++)
            {
                var r = new Rect(panel.x + pad + i * (thumb + gap), cy, thumb, thumb);
                captainRects[i] = r;
                bool chosen = i == Captain.Index;
                var face = Captain.Face(i, Mood.Neutral);
                Hud.Fill(r, new Color(0f, 0f, 0f, 0.6f));
                if (face != null)
                {
                    var old = GUI.color;
                    if (!chosen) GUI.color = new Color(0.6f, 0.6f, 0.65f, 1f);
                    GUI.DrawTexture(r, face, ScaleMode.ScaleAndCrop);
                    GUI.color = old;
                }
                Hud.Outline(r, Mathf.Max(1f, (chosen ? 3f : 1f) * s), chosen ? new Color(1f, 0.72f, 0.3f) : new Color(1f, 1f, 1f, 0.2f));
            }
            GUI.Label(new Rect(panel.x + pad, cy + thumb + 6f * s, n * (thumb + gap), 22f * s), $"Capt. {Captain.Current.name}",
                      Hud.Text(15, true, TextAnchor.MiddleCenter));

            // launch
            launchRect = new Rect(panel.xMax - pad - 260f * s, panel.yMax - pad - 64f * s, 260f * s, 56f * s);
            Hud.Fill(launchRect, new Color(0.25f, 0.45f, 0.8f, 0.75f));
            Hud.Outline(launchRect, Mathf.Max(1f, 2f * s), new Color(1f, 0.72f, 0.3f));
            GUI.Label(launchRect, "<b>LAUNCH</b>", Hud.Text(22, false, TextAnchor.MiddleCenter));
            string hint = Gamepad.current != null ? "D-pad up/down frigate   LB/RB captain   A launch" : "Up/down frigate   Left/right captain   Enter launch";
            GUI.Label(new Rect(panel.x + pad, panel.yMax - pad - 26f * s, w - 300f * s, 24f * s), $"<color=#9aa4b0>{hint}</color>", Hud.Text(13, false));
        }
    }
}

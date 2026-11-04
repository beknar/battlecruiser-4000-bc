using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarrierGame.Demo
{
    /// <summary>Keyboard, mouse and gamepad (Xbox 360 / XInput) controls for trying the carrier out,
    /// with an on-screen help box that shows whichever set the player is using.</summary>
    public class CarrierDemoInput : MonoBehaviour
    {
        public CarrierController carrier;
        public OrbitCamera orbitCamera;
        public Transform[] targets;
        [Tooltip("Lock onto the first target when the game starts (off for an ambush: the enemies start hidden).")]
        public bool targetOnStart = true;

        int targetIndex, followIndex = -1;
        TargetDisplay display;
        static Rect helpRect;

        /// <summary>Recording the demo reel: the controls list is left off the screen.</summary>
        public static bool ReelMode;

        /// <summary>Rudder from the self-test or reel autopilot; used while the player isn't steering.</summary>
        [System.NonSerialized] public float? autoRudder;
        /// <summary>Pitch and elevator input from the self-test; used while the player isn't flying.</summary>
        [System.NonSerialized] public float? autoPitch, autoElevator;

        /// <summary>Screen area of the help box (GUI coordinates).</summary>
        public static Rect HelpRect => helpRect;

        WarpDrive warp;

        void Awake()
        {
            if (carrier != null) warp = carrier.GetComponent<WarpDrive>();
            display = gameObject.AddComponent<TargetDisplay>();
            display.carrier = carrier;
            display.targets = targets;
        }

        void Start()
        {
            if (carrier != null && targets != null && targets.Length > 0 && targetOnStart) carrier.SetTarget(targets[0]);
            if (carrier != null && orbitCamera != null && orbitCamera.target == carrier.transform)
            {
                orbitCamera.distance = ShipViewDistance;
                orbitCamera.offset = ShipViewOffset;
            }
            // no point rendering faster than the screen refreshes; keeps the laptop GPU cool
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = 120;
            StartCoroutine(FixDisplayWhenReady());
            PadSupport.Init();
            if (!TryGetComponent(out menu)) menu = gameObject.AddComponent<GameMenu>();
#if UNITY_WEBGL && !UNITY_EDITOR
            // tell the page which graphics API it got: WebGPU, or the WebGL 2 fallback
            CarrierPageGraphics(SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.WebGPU ? "WebGPU" : "WebGL 2");
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        static extern void CarrierPageGraphics(string name);   // Plugins/WebGL/CarrierPage.jslib
#endif

        void OnDestroy() => PadSupport.Shutdown();
        void OnApplicationFocus(bool focus) { if (!focus) PadSupport.StopRumble(); }
        void OnApplicationPause(bool paused) { if (paused) PadSupport.StopRumble(); }

        void Update()
        {
            if (carrier == null) return;
            var k = Keyboard.current;
            var pad = Gamepad.current;
            PadSupport.Track(pad, k, Mouse.current);
            // the pause menu has the keys, mouse and pad while it is open
            if (GameMenu.IsOpen || GameMenu.JustClosed) return;

            // the system map has the keys and the pad while it is open; the warp drive flies the
            // ship while it is charging or in warp
            bool warpFlying = warp != null && warp.Current != WarpDrive.State.Ready;
            if (SystemMap.IsOpen || SystemMap.JustClosed || warpFlying)
            {
                WatchDestruction();
                if (!carrier.Destroyed)
                {
                    carrier.rudder = autoRudder ?? 0f;
                    carrier.pitchInput = autoPitch ?? 0f;
                    carrier.elevator = autoElevator ?? 0f;
                }
                if (warpFlying && !SystemMap.IsOpen && !SystemMap.JustClosed)
                {
                    if ((k != null && (k.escapeKey.wasPressedThisFrame || k.pKey.wasPressedThisFrame)) || (pad != null && pad.startButton.wasPressedThisFrame)) menu.Open();
                }
                return;
            }

            float turn = 0f, throttleRate = 0f, pitch = 0f, lift = 0f;
            if (k != null)
            {
                KeyboardControls(k);
                if (k.aKey.isPressed) turn = -1f;
                else if (k.dKey.isPressed) turn = 1f;
                if (k.wKey.isPressed) throttleRate += 1f;
                if (k.sKey.isPressed) throttleRate -= 1f;
                if (k.upArrowKey.isPressed) pitch += 1f;           // nose up
                if (k.downArrowKey.isPressed) pitch -= 1f;         // nose down
                if (k.spaceKey.isPressed) lift += 1f;              // elevator thrusters up
                if (k.leftCtrlKey.isPressed || k.rightCtrlKey.isPressed) lift -= 1f;
            }
            if (orbitCamera != null) orbitCamera.padTriggerZoom = !GunshipPad;
            if (pad != null)
            {
                PadControls(pad);
                Vector2 ls = pad.leftStick.ReadValue();   // the stick already has a dead zone
                if (Mathf.Abs(ls.x) > Mathf.Abs(turn)) turn = ls.x;
                if (Mathf.Abs(ls.y) > Mathf.Abs(throttleRate)) throttleRate = ls.y;
                if (GunshipPad)
                {
                    if (!pad.selectButton.isPressed)
                    {
                        if (pad.dpad.up.isPressed) pitch = 1f;
                        else if (pad.dpad.down.isPressed) pitch = -1f;
                    }
                    float padLift = pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue();
                    if (Mathf.Abs(padLift) > 0.05f && Mathf.Abs(padLift) > Mathf.Abs(lift)) lift = padLift;
                }
            }
            WatchDestruction();
            if (carrier.Destroyed) return;
            carrier.rudder = turn != 0f || !autoRudder.HasValue ? turn : autoRudder.Value;
            if (MainGun != null && mainGun.Current != MainBeam.State.Idle) mainGun.target = carrier.target;
            carrier.pitchInput = Mathf.Clamp(pitch != 0f || !autoPitch.HasValue ? pitch : autoPitch.Value, -1f, 1f);
            carrier.elevator = Mathf.Clamp(lift != 0f || !autoElevator.HasValue ? lift : autoElevator.Value, -1f, 1f);
            if (throttleRate != 0f) carrier.throttle = Mathf.Clamp01(carrier.throttle + throttleRate * Time.deltaTime * 0.3f);
        }

        bool wasDestroyed;

        void WatchDestruction()
        {
            bool dead = carrier.Destroyed;
            if (dead && !wasDestroyed)
            {
                Toast($"{carrier.shipName} destroyed!");
                PadSupport.Rumble(1f, 0.8f, 1.2f);
            }
            else if (!dead && wasDestroyed)
            {
                Toast($"{carrier.shipName} back in action: shield and hull restored");
                if (orbitCamera != null && orbitCamera.target == carrier.transform) orbitCamera.distance = ShipViewDistance;
            }
            wasDestroyed = dead;
        }

        /// <summary>The frigate's pad layout (no hangar, so the D-pad and triggers fly the ship).</summary>
        bool GunshipPad => carrier != null && !carrier.HasHangar;

        public void LevelOff()
        {
            carrier.LevelOff();
            Toast("Levelling off");
        }

        void KeyboardControls(Keyboard k)
        {
            // P too: in a browser at full screen, the first Esc only leaves full screen
            if (k.escapeKey.wasPressedThisFrame || k.pKey.wasPressedThisFrame) { menu.Open(); return; }
            if (k.hKey.wasPressedThisFrame) showHelp = !showHelp;
            if (k.equalsKey.wasPressedThisFrame || k.numpadPlusKey.wasPressedThisFrame) TextSize(+1);
            if (k.minusKey.wasPressedThisFrame || k.numpadMinusKey.wasPressedThisFrame) TextSize(-1);
            if (k.f11Key.wasPressedThisFrame && !OnWeb) ToggleFullScreen();   // in a browser, F11 is the browser's
            if (k.nKey.wasPressedThisFrame) ToggleMute();
            if (carrier.Destroyed) return;   // nothing to fly or fight with until it respawns

            if (k.digit1Key.wasPressedThisFrame) LaunchFighters();
            if (k.digit2Key.wasPressedThisFrame) LaunchDrones();
            if (k.digit3Key.wasPressedThisFrame) LaunchDropships();
            if (k.mKey.wasPressedThisFrame && warp == null) FireMissiles();   // with a warp drive, M is the system map
            if (k.fKey.wasPressedThisFrame) FireGuns();
            if (k.gKey.wasPressedThisFrame) ToggleAutoFire();
            if (k.qKey.wasPressedThisFrame) WingAttack();
            if (k.eKey.wasPressedThisFrame) WingEscort();
            if (k.rKey.wasPressedThisFrame) WingReturn();

            // targeting: T cycles, a click picks a ship, X stops targeting
            if (k.tKey.wasPressedThisFrame) NextTarget();
            if (k.xKey.wasPressedThisFrame || k.backspaceKey.wasPressedThisFrame) ClearTarget();
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame) PickAt(mouse.position.ReadValue());

            if (k.cKey.wasPressedThisFrame) CycleCamera();
            if (k.lKey.wasPressedThisFrame) LevelOff();
            if (k.bKey.wasPressedThisFrame) ChargeMainGun();
        }

        // ------------------------------------------------------------ main gun, shield, CIWS

        MainBeam mainGun;
        Shield shield;
        CiwsDirector ciws;
        Health hull;
        bool systemsFound;

        void FindSystems()
        {
            if (systemsFound || carrier == null) return;
            systemsFound = true;
            mainGun = carrier.GetComponentInChildren<MainBeam>();
            shield = carrier.GetComponentInChildren<Shield>(true);
            ciws = carrier.GetComponent<CiwsDirector>();
            hull = carrier.GetComponent<Health>();
        }

        public MainBeam MainGun { get { FindSystems(); return mainGun; } }

        /// <summary>Start charging the bow beam (it fires by itself after 10 s, straight ahead,
        /// locking on if the target is close to the bow line); pressing again cancels the charge.</summary>
        public void ChargeMainGun()
        {
            if (carrier.Destroyed) return;
            FindSystems();
            if (mainGun == null) { NotFitted($"The {carrier.shipName.ToLower()} has no main gun"); return; }
            switch (mainGun.Current)
            {
                case MainBeam.State.Idle:
                    mainGun.target = carrier.target;
                    mainGun.StartCharge();
                    Toast($"Main gun charging: {mainGun.chargeTime:F0} s, keep the bow on the target");
                    PadSupport.Rumble(0.2f, 0.1f, 0.3f);
                    break;
                case MainBeam.State.Charging:
                    mainGun.Cancel();
                    Toast("Main gun charge cancelled");
                    break;
            }
        }

        string SystemsText()
        {
            FindSystems();
            var sb = new System.Text.StringBuilder();
            if (carrier.Destroyed)
                return $"\n<color=#ff6050><b>DESTROYED</b></color>   respawning in {Mathf.CeilToInt(carrier.RespawnIn)} s";
            if (shield != null || hull != null)
            {
                string sh = shield == null ? "" : shield.Up
                    ? $"Shield {Pct(shield.Fraction)}" : "Shield <color=#ff6050>DOWN</color>";
                string hu = hull == null ? "" : $"   Hull {Pct(hull.Current / Mathf.Max(1f, hull.maxHealth))}";
                sb.Append("\n" + sh + hu);
            }
            if (mainGun != null)
            {
                string key = PadSupport.UsingPad && Gamepad.current != null ? "X" : "B";
                string g = mainGun.Current switch
                {
                    MainBeam.State.Charging => $"<color=#bfe6ff>charging {mainGun.ChargeTimeLeft:F1} s</color>  {Bar(mainGun.ChargeProgress)}",
                    MainBeam.State.Firing => "<color=#ffffff><b>FIRING</b></color>",
                    _ => $"<color=#9aa4b0>{key} to charge</color>",
                };
                sb.Append($"\nMain gun  {g}");
                if (mainGun.Current == MainBeam.State.Charging && carrier.target != null)
                {
                    float err = ShipAutopilot.BowError(carrier, carrier.target.position);
                    sb.Append(err <= mainGun.aimAssist ? "  <color=#7ee08a>on target</color>" : $"  <color=#ffb060>{err:F0}° off</color>");
                }
            }
            if (warp != null)
            {
                string key = PadSupport.UsingPad && Gamepad.current != null ? "R3" : "M";
                string w = warp.Current switch
                {
                    WarpDrive.State.Charging => $"<color=#bfe6ff>charging for {warp.Destination?.displayName}  {Mathf.Max(0f, warp.chargeTime - warp.StateTime):F1} s</color>",
                    WarpDrive.State.Warping => $"<color=#bfe6ff><b>IN WARP</b> to {warp.Destination?.displayName}  {warp.TimeLeft:F1} s</color>",
                    WarpDrive.State.Arriving => $"<color=#bfe6ff>dropping out at {warp.Destination?.displayName}</color>",
                    _ => warp.InCombat(out string why) ? $"<color=#ff6050>locked: {why}</color>" : $"<color=#7ee08a>ready</color>  <color=#9aa4b0>{key} system map</color>",
                };
                sb.Append($"\nWarp  {w}");
                var here = WarpPoint.Nearest(carrier.transform.position);
                if (here != null && warp.Current == WarpDrive.State.Ready)
                    sb.Append($"\nNear  {here.displayName}  <color=#9aa4b0>{Vector3.Distance(here.Position, carrier.transform.position) / 1000f:F0} km</color>");
            }
            if (Cover.Active)
            {
                // among the rocks: how many hostiles can see us (the rest can't shoot)
                int seen = 0, total = 0;
                foreach (var info in TargetInfo.All)
                {
                    if (info == null || !info.Hostile) continue;
                    var h = info.GetComponent<Health>();
                    if (h != null && h.Dead) continue;
                    if (CombatPilot.IsHidden(info.transform)) continue;
                    // only the ones close enough to matter (not a patrol on the far side of the system)
                    if (!TargetDisplay.InRange(info.transform, carrier.transform.position)) continue;
                    total++;
                    if (Cover.Visible(info.transform.position, carrier.transform.position)) seen++;
                }
                if (total > 0)
                    sb.Append(seen == 0 ? "\nCover  <color=#7ee08a>hidden from every enemy</color>"
                                        : $"\nCover  in sight of {seen} of {total} enemies");
            }
            if (ciws != null)
            {
                var t = ciws.EngagedTarget;
                string n = t == null ? null : t.GetComponent<TargetInfo>() is TargetInfo ti && ti != null ? ti.Name : t.name;
                sb.Append(n == null ? "\nCIWS  <color=#9aa4b0>standing by</color>" : $"\nCIWS  engaging {n}");
            }
            return sb.ToString();
        }

        static string Pct(float f)
        {
            string c = f > 0.6f ? "#7ee08a" : f > 0.3f ? "#ffc04d" : "#ff6050";
            return $"<color={c}>{f:P0}</color>";
        }

        /// <summary>The bar's block. The browser has no system fonts to fall back on, and the
        /// built-in font has no '■'.</summary>
        static readonly char Block = OnWeb ? '|' : '■';

        static string Bar(float k)
        {
            int n = Mathf.RoundToInt(Mathf.Clamp01(k) * 10f);
            return "<color=#bfe6ff>" + new string(Block, n) + "</color><color=#4a5560>" + new string(Block, 10 - n) + "</color>";
        }

        // ------------------------------------------------------------ gamepad

        bool backCombo;

        /// <summary>Xbox 360 layout. Back acts when released, so it can also be held for
        /// combinations (Back + D-pad up/down = text size). Start opens the pause menu.</summary>
        void PadControls(Gamepad p)
        {
            bool back = p.selectButton.isPressed;
            if (p.startButton.wasPressedThisFrame) { menu.Open(); return; }

            if (back)
            {
                if (p.dpad.up.wasPressedThisFrame) { TextSize(+1); backCombo = true; }
                if (p.dpad.down.wasPressedThisFrame) { TextSize(-1); backCombo = true; }
            }
            else if (GunshipPad)
            {
                // D-pad up/down pitch the nose (read in Update); left/right zoom the camera
                if (orbitCamera != null)
                {
                    float z = (p.dpad.left.isPressed ? 1f : 0f) - (p.dpad.right.isPressed ? 1f : 0f);
                    if (z != 0f)
                        orbitCamera.distance = Mathf.Clamp(orbitCamera.distance * Mathf.Pow(3.3f, z * Time.unscaledDeltaTime),
                                                           orbitCamera.minDistance, orbitCamera.maxDistance);
                }
            }
            else
            {
                if (p.dpad.up.wasPressedThisFrame) LaunchFighters();
                if (p.dpad.right.wasPressedThisFrame) LaunchDrones();
                if (p.dpad.down.wasPressedThisFrame) LaunchDropships();
                if (p.dpad.left.wasPressedThisFrame) WingReturn();
            }
            if (p.selectButton.wasReleasedThisFrame) { if (!backCombo) showHelp = !showHelp; backCombo = false; }
            if (carrier.Destroyed) return;

            if (p.buttonSouth.wasPressedThisFrame) { if (GunshipPad) LevelOff(); else WingAttack(); }   // A
            if (p.buttonEast.wasPressedThisFrame) ClearTarget();          // B
            if (p.buttonWest.wasPressedThisFrame) { if (GunshipPad) ChargeMainGun(); else WingEscort(); }   // X
            if (p.buttonNorth.wasPressedThisFrame) NextTarget();          // Y
            if (p.rightShoulder.wasPressedThisFrame) FireGuns();          // RB
            if (p.leftShoulder.wasPressedThisFrame) FireMissiles();       // LB
            if (p.leftStickButton.wasPressedThisFrame) ToggleAutoFire();  // L3
            if (p.rightStickButton.wasPressedThisFrame && warp == null) CycleCamera();    // R3 (the system map, with a warp drive)
            // right stick orbits and LT/RT zoom: handled by OrbitCamera
        }

        // ------------------------------------------------------------ actions (keys, pad and the auto test)

        public void LaunchFighters() { if (Hangar()) { carrier.LaunchFighters(); PadSupport.Rumble(0.15f, 0.3f, 0.2f); } }
        public void LaunchDrones() { if (Hangar()) { carrier.LaunchDrones(); PadSupport.Rumble(0.1f, 0.25f, 0.15f); } }
        public void LaunchDropships() { if (Hangar()) { carrier.LaunchDropships(); PadSupport.Rumble(0.3f, 0.2f, 0.25f); } }
        public void WingEscort() { if (Hangar()) carrier.OrderWing(Craft.Order.Escort); }
        public void WingReturn() { if (Hangar()) carrier.OrderWing(Craft.Order.Return); }

        /// <summary>True if the ship carries craft; otherwise says so.</summary>
        bool Hangar()
        {
            if (carrier.HasHangar) return true;
            NotFitted($"The {carrier.shipName.ToLower()} has no hangar: no fighters, drones or dropships");
            return false;
        }

        void NotFitted(string message)
        {
            SoundFX.Ui(Sfx.UiError);
            Toast(message);
        }

        /// <summary>With a target, each gun that can bear on it fires as soon as it is lined up
        /// (never on the way round); with no target, everything fires where it points.</summary>
        public void FireGuns()
        {
            if (carrier.Destroyed) return;
            int fired = 0;
            foreach (var t in carrier.mainTurrets)
            {
                if (!t) continue;
                if (carrier.target == null) { t.Fire(); fired++; }
                else if (t.RequestFire()) fired++;
            }
            if (fired == 0)
            {
                NotFitted("No guns bear on the target: turn to bring a broadside round");
                return;
            }
            PadSupport.Rumble(0.6f, 0.2f, 0.18f);
        }

        public void ToggleAutoFire()
        {
            bool on = false;
            foreach (var t in carrier.mainTurrets) if (t) on = t.autoFire = !t.autoFire;
            Toast(on ? "Main guns: auto-fire ON" : "Main guns: auto-fire OFF");
        }

        public void FireMissiles()
        {
            if (!carrier.HasMissiles) { NotFitted($"The {carrier.shipName.ToLower()} has no missiles: guns only"); return; }
            if (carrier.target == null) { NoTarget(); return; }
            carrier.FireMissiles();
            PadSupport.Rumble(0.35f, 0.5f, 0.35f);
        }

        public void WingAttack()
        {
            if (!Hangar()) return;
            if (carrier.target == null) NoTarget();
            else carrier.OrderWing(Craft.Order.Attack);
        }

        public void NextTarget()
        {
            if (targets == null || targets.Length == 0) return;
            // the next target in the list that isn't still hiding in an ambush, and is within range
            for (int tries = 0; tries < targets.Length; tries++)
            {
                targetIndex = carrier.target == null && tries == 0 ? targetIndex % targets.Length : (targetIndex + 1) % targets.Length;
                var t = targets[targetIndex];
                if (t == null || CombatPilot.IsHidden(t)) continue;
                if (!TargetDisplay.InRange(t, carrier.transform.position)) continue;
                Select(t);
                return;
            }
            NoTarget();
        }

        /// <summary>Stop targeting: guns stow, craft attacking it go back to escort.</summary>
        public void ClearTarget()
        {
            if (carrier.target == null) return;
            carrier.SetTarget(null);
            SoundFX.Ui(Sfx.UiClear);
            Toast("Target cleared: guns stowed, attacking craft back to escort");
        }

        public void CycleCamera()
        {
            if (orbitCamera == null) return;
            // cycle the camera between the carrier and each craft in the air
            followIndex++;
            if (followIndex >= carrier.Wing.Count) followIndex = -1;
            bool onCarrier = followIndex < 0 || carrier.Wing[followIndex] == null;
            orbitCamera.target = onCarrier ? carrier.transform : carrier.Wing[followIndex].transform;
            orbitCamera.distance = onCarrier ? ShipViewDistance : 80f;
            orbitCamera.offset = onCarrier ? ShipViewOffset : Vector3.zero;
        }

        float shipRadius;

        /// <summary>Radius of the player's ship, from its renderers.</summary>
        public float ShipRadius
        {
            get
            {
                if (shipRadius <= 0f && carrier != null)
                {
                    var rs = carrier.GetComponentsInChildren<Renderer>();
                    var b = new Bounds(carrier.transform.position, Vector3.zero);
                    foreach (var r in rs) if (r is MeshRenderer || r is SkinnedMeshRenderer) b.Encapsulate(r.bounds);
                    shipRadius = Mathf.Max(20f, b.extents.magnitude);
                }
                return shipRadius;
            }
        }

        /// <summary>A comfortable camera distance for the whole ship.</summary>
        public float ShipViewDistance => ShipRadius * 3.1f;
        public Vector3 ShipViewOffset => new Vector3(0f, ShipRadius * 0.1f, 0f);

        public static void ToggleMute()
        {
            SoundFX.Muted = !SoundFX.Muted;
            Toast(SoundFX.Muted ? "Sound off" : "Sound on");
        }

        static void TextSize(int dir) =>
            Hud.UserScale = Mathf.Clamp(Hud.UserScale + 0.15f * dir, 0.5f, 3f);

        /// <summary>Running in a web browser (the page owns the window: no resizing; quitting goes
        /// through the page).</summary>
        public static bool OnWeb => Application.platform == RuntimePlatform.WebGLPlayer;

        public void SelectTarget(Transform t) => Select(t);

        void Select(Transform t)
        {
            if (t == null) return;
            int i = System.Array.IndexOf(targets, t);
            if (i >= 0) targetIndex = i;
            carrier.SetTarget(t);
            SoundFX.Ui(Sfx.UiLock);
        }

        void NoTarget()
        {
            SoundFX.Ui(Sfx.UiError);
            Toast(PadSupport.UsingPad ? "No target: press Y to pick one" : "No target: press T or click a ship");
        }

        /// <summary>Target the hostile under (or nearest to) the mouse click.</summary>
        void PickAt(Vector2 screen)
        {
            var cam = Camera.main;
            if (cam == null || targets == null) return;
            var gui = new Vector2(screen.x, Screen.height - screen.y);
            if (helpRect.Contains(gui) || (display != null && display.PanelRect.Contains(gui))) return;

            Transform best = null;
            // the nearest thing under the cursor that isn't our own ship or its shield
            var hits = Physics.RaycastAll(cam.ScreenPointToRay(screen), 40000f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                if (hit.transform.IsChildOf(carrier.transform)) continue;
                foreach (var t in targets)
                    if (t != null && hit.transform.IsChildOf(t) && !CombatPilot.IsHidden(t) && TargetDisplay.InRange(t, carrier.transform.position)) best = t;
                break;
            }
            if (best == null)
            {
                float bestDist = 70f * Hud.S;
                foreach (var t in targets)
                {
                    if (t == null) continue;
                    var h = t.GetComponent<Health>();
                    if (h != null && h.Dead) continue;
                    if (CombatPilot.IsHidden(t) || !TargetDisplay.InRange(t, carrier.transform.position)) continue;
                    Vector3 sp = cam.WorldToScreenPoint(t.position);
                    if (sp.z <= 0f) continue;
                    float d = Vector2.Distance(new Vector2(sp.x, sp.y), screen);
                    if (d < bestDist) { bestDist = d; best = t; }
                }
            }
            if (best != null && best != carrier.target) Select(best);
        }

        // ------------------------------------------------------------ messages

        static string toast;
        static float toastUntil;

        /// <summary>Show a short message at the bottom of the screen.</summary>
        public static void Toast(string message)
        {
            toast = message;
            toastUntil = Time.unscaledTime + 2.5f;
        }

        // ------------------------------------------------------------ display + HUD

        public static bool HasArg(string arg) => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), arg) >= 0;

        /// <summary>Standalone builds run borderless full screen at the monitor's native resolution.
        /// If an earlier run left a different size in the registry, fix it once the game is up and
        /// running (never during startup), and only when it actually differs. A size given on the
        /// command line (-screen-fullscreen / -screen-width) is left alone.</summary>
        System.Collections.IEnumerator FixDisplayWhenReady()
        {
            if (Application.isEditor || OnWeb || HasArg("-screen-fullscreen") || HasArg("-screen-width") || HasArg("-autotest")) yield break;
            yield return new WaitForSecondsRealtime(1f);
            int w = Display.main.systemWidth, h = Display.main.systemHeight;
            if (Screen.fullScreenMode == FullScreenMode.FullScreenWindow && Screen.width == w && Screen.height == h) yield break;
            Screen.SetResolution(w, h, FullScreenMode.FullScreenWindow);
        }

        static void ToggleFullScreen()
        {
            int w = Display.main.systemWidth, h = Display.main.systemHeight;
            if (Screen.fullScreenMode == FullScreenMode.Windowed)
                Screen.SetResolution(w, h, FullScreenMode.FullScreenWindow);
            else
                Screen.SetResolution(Mathf.RoundToInt(w * 0.8f), Mathf.RoundToInt(h * 0.8f), FullScreenMode.Windowed);
        }

        string keyHelp, padHelp;
        GameMenu menu;

        /// <summary>The text for here, or for a browser: it has no system fonts to fall back on,
        /// and the built-in font has no arrows.</summary>
        static string W(string native, string web) => OnWeb ? web : native;

        /// <summary>The controls list, leaving out systems this ship doesn't have.</summary>
        string KeyHelp => keyHelp ??= string.Join("\n", new[]
        {
            carrier.HasHangar ? "1 fighters   2 drones   3 dropships" : null,
            (carrier.HasMissiles ? "M missile salvo   " : "") + "F fire guns   G auto-fire on/off",
            MainGun != null ? "B main gun: charge (fires after 10 s)" : null,
            carrier.HasHangar ? "Q wing attack   E escort   R return to carrier" : null,
            "W/S throttle   A/D turn",
            OnWeb ? "Up/Down arrows nose up/down   L level off" : "↑/↓ nose up/down   L level off",
            "Space/Ctrl elevator thrusters up/down",
            "T next target   click a ship to target",
            "X stop targeting",
            carrier.HasHangar ? "C camera: carrier / craft" : null,
            "Right-drag orbit   mouse wheel zoom",
            warp != null ? "M system map: mini-warp" : null,
            "H hide help   +/- text size   N sound",
            OnWeb ? "F11 full screen   Esc or P menu" : "F11 window   Esc or P menu",
        }.Where(l => l != null));

        string PadHelp => padHelp ??= string.Join("\n", new[]
        {
            "<b>Gamepad</b>",
            "Left stick  throttle / turn",
            carrier.HasHangar ? "Right stick  orbit camera   LT/RT zoom" : W("Right stick  orbit camera   D-pad ←/→ zoom", "Right stick  orbit camera   D-pad left/right zoom"),
            carrier.HasHangar ? W("D-pad  ↑ fighters  → drones  ↓ dropships  ← return", "D-pad  up fighters  right drones  down dropships  left return")
                              : W("D-pad ↑/↓ nose up/down   A level off", "D-pad up/down  nose up/down   A level off"),
            carrier.HasHangar ? null : "LT/RT elevator thrusters down/up",
            carrier.HasHangar ? "A wing attack   X escort" : null,
            "Y next target   B stop targeting",
            "RB fire guns" + (carrier.HasMissiles ? "   LB missile salvo" : "") + (MainGun != null && !carrier.HasHangar ? "   X main gun" : ""),
            "L3 auto-fire on/off" + (carrier.HasHangar ? "   R3 camera: carrier / craft" : ""),
            warp != null ? "R3 system map: mini-warp" : null,
            "Back hide help   Start menu",
            W("Back + D-pad ↑↓ text size", "Back + D-pad up/down text size"),
        }.Where(l => l != null));

        bool showHelp = true;

        void OnGUI()
        {
            if (carrier == null) return;
            float s = Hud.S;
            var style = Hud.Text(20);
            string target = carrier.target == null ? "<color=#9aa4b0>none</color>"
                : carrier.target.GetComponent<TargetInfo>() is TargetInfo ti && ti != null ? ti.Name : carrier.target.name;
            bool pad = PadSupport.UsingPad && Gamepad.current != null;
            string guns = carrier.GunsBearingText();
            string text =
                $"<b>{carrier.shipName.ToUpper()}</b>" + (SoundFX.Muted ? "   <color=#9aa4b0>(sound off)</color>" : "") + "\n" +
                $"Speed {carrier.Speed:F0} m/s   throttle {carrier.throttle:P0}\n" +
                $"Pitch {carrier.Pitch:+0;-0;0}°   climb {carrier.ClimbRate:+0;-0;0} m/s" +
                (Mathf.Abs(carrier.LiftSpeed) > 0.5f ? $"   <color=#9ad0ff>elevators {(carrier.LiftSpeed > 0 ? "up" : "down")}</color>" : "") + "\n" +
                (carrier.HasHangar ? $"Fighters {carrier.fighterStock}  Drones {carrier.droneStock}  Dropships {carrier.dropshipStock}\n" +
                                     $"In flight {carrier.Wing.Count}   " : "") +
                (carrier.HasMissiles ? $"Missile cells ready {carrier.missiles.ReadyCells}\n" : carrier.HasHangar ? "\n" : "") +
                $"Target {target}" +
                (guns.Length > 0 ? $"\nGuns bearing  {(guns.StartsWith("none") ? "<color=#ffb060>" + guns + "</color>" : guns)}" : "") +
                SystemsText() +
                (ReelMode ? "" : "\n\n" + (showHelp ? (pad ? PadHelp : KeyHelp) : (pad ? "<i>Back: show controls</i>" : "<i>H show controls</i>")));
            var content = new GUIContent(text);
            Vector2 sz = style.CalcSize(content);
            float pad2 = 12f * s;
            helpRect = new Rect(pad2, pad2, sz.x + pad2 * 2f, sz.y + pad2 * 2f);
            Hud.Panel(helpRect);
            GUI.Label(new Rect(helpRect.x + pad2, helpRect.y + pad2, sz.x, sz.y), content, style);

            if (carrier.Destroyed)
            {
                var big = Hud.Text(40, true, TextAnchor.MiddleCenter);
                var msg = new GUIContent($"<color=#ff6050>{carrier.shipName.ToUpper()} DESTROYED</color>\n" +
                                         $"<size={Mathf.RoundToInt(22 * s)}>respawning in {Mathf.CeilToInt(carrier.RespawnIn)} s</size>");
                Vector2 bsz = big.CalcSize(msg);
                var br = new Rect((Screen.width - bsz.x) * 0.5f - pad2 * 2f, Screen.height * 0.28f, bsz.x + pad2 * 4f, bsz.y + pad2 * 2f);
                Hud.Panel(br);
                GUI.Label(br, msg, big);
            }

            if (toast != null && Time.unscaledTime < toastUntil)
            {
                var ts = Hud.Text(20, true, TextAnchor.MiddleCenter);
                var tc = new GUIContent(toast);
                Vector2 tsz = ts.CalcSize(tc);
                var r = new Rect((Screen.width - tsz.x) * 0.5f - pad2, Screen.height - tsz.y - pad2 * 6f, tsz.x + pad2 * 2f, tsz.y + pad2);
                Hud.Panel(r);
                GUI.Label(r, tc, ts);
            }
        }
    }
}

using UnityEngine;
using UnityEngine.InputSystem;

namespace CarrierGame.Demo
{
    /// <summary>Keyboard, mouse and gamepad (Xbox 360 / XInput) controls for the player's ship,
    /// and the actions they trigger (also driven by the self-tests and reels). It sets the level
    /// up around itself as it wakes (GameSession: the HUD, co-op, the pause menu).</summary>
    public class CarrierDemoInput : MonoBehaviour
    {
        public CarrierController carrier;
        public OrbitCamera orbitCamera;
        [Tooltip("The level's hostiles, as the scene has them (Targets keeps the live list).")]
        public Transform[] targets;
        [Tooltip("Lock onto the first target when the game starts (off for an ambush: the enemies start hidden).")]
        public bool targetOnStart = true;

        int targetIndex, followIndex = -1;
        ShipHud hud;

        /// <summary>Rudder from the self-test or reel autopilot; used while the player isn't steering.</summary>
        [System.NonSerialized] public float? autoRudder;
        /// <summary>Pitch, elevator and roll input from the self-test; used while the player isn't flying.</summary>
        [System.NonSerialized] public float? autoPitch, autoElevator, autoRoll;
        /// <summary>The pilot is steering (the rudder, the nose, the elevator thrusters or the roll),
        /// this frame: it takes the controls back from an auto-pilot.</summary>
        public bool PilotFlying { get; private set; }

        WarpDrive warp;
        /// <summary>The landing controller, if this level has one.</summary>
        static PlanetLanding Landing => PlanetLanding.Instance;

        void Awake()
        {
            if (carrier != null) warp = carrier.GetComponent<WarpDrive>();
            Targets.Reset(targets);
            Targets.Removed += TargetRemoved;
            hud = GameSession.Begin(this);
        }

        void Start()
        {
            if (carrier != null && Targets.All.Count > 0 && targetOnStart) carrier.SetTarget(Targets.All[0]);
            if (carrier != null && orbitCamera != null && orbitCamera.target == carrier.transform)
            {
                orbitCamera.distance = ShipViewDistance;
                orbitCamera.offset = ShipViewOffset;
            }
        }

        void OnDestroy() => Targets.Removed -= TargetRemoved;

        /// <summary>Something left the list: keep our place in it, and stop aiming at it.</summary>
        void TargetRemoved(Transform t, int i)
        {
            if (i < targetIndex) targetIndex--;
            if (targetIndex >= Targets.All.Count) targetIndex = 0;
            if (carrier != null && carrier.target == t) carrier.SetTarget(null);
        }

        GameMenu menu;
        /// <summary>The pause menu (GameSession puts it up as the level starts).</summary>
        GameMenu Menu => menu != null ? menu : (menu = GetComponent<GameMenu>());

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
            bool warpFlying = (warp != null && warp.Current != WarpDrive.State.Ready) || PlanetLanding.Transitioning;
            // (and so has an industrial site's screen)
            bool panel = SystemMap.IsOpen || SystemMap.JustClosed || IndustryPanel.IsOpen || IndustryPanel.JustClosed;
            if (panel || warpFlying)
            {
                WatchDestruction();
                if (!carrier.Destroyed)
                {
                    carrier.rudder = autoRudder ?? 0f;
                    carrier.pitchInput = autoPitch ?? 0f;
                    carrier.elevator = autoElevator ?? 0f;
                    carrier.rollInput = autoRoll ?? 0f;
                }
                if (warpFlying && !panel)
                {
                    if ((k != null && (k.escapeKey.wasPressedThisFrame || k.pKey.wasPressedThisFrame)) || (pad != null && pad.startButton.wasPressedThisFrame)) Menu.Open();
                }
                return;
            }

            float turn = 0f, throttleRate = 0f, pitch = 0f, lift = 0f, roll = 0f;
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
                if (!carrier.HasHangar)                            // Q/E roll (a carrier's are its wing's orders)
                {
                    if (k.qKey.isPressed) roll -= 1f;
                    if (k.eKey.isPressed) roll += 1f;
                }
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
                        if (pad.dpad.left.isPressed) roll = -1f;
                        else if (pad.dpad.right.isPressed) roll = 1f;
                    }
                    float padLift = pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue();
                    if (Mathf.Abs(padLift) > 0.05f && Mathf.Abs(padLift) > Mathf.Abs(lift)) lift = padLift;
                }
            }
            PilotFlying = turn != 0f || pitch != 0f || lift != 0f || roll != 0f;
            WatchDestruction();
            if (carrier.Destroyed) return;
            carrier.rudder = turn != 0f || !autoRudder.HasValue ? turn : autoRudder.Value;
            if (MainGun != null && mainGun.Current != MainBeam.State.Idle) mainGun.target = carrier.target;
            carrier.pitchInput = Mathf.Clamp(pitch != 0f || !autoPitch.HasValue ? pitch : autoPitch.Value, -1f, 1f);
            carrier.elevator = Mathf.Clamp(lift != 0f || !autoElevator.HasValue ? lift : autoElevator.Value, -1f, 1f);
            carrier.rollInput = Mathf.Clamp(roll != 0f || !autoRoll.HasValue ? roll : autoRoll.Value, -1f, 1f);
            if (throttleRate != 0f) carrier.throttle = Mathf.Clamp01(carrier.throttle + throttleRate * Time.deltaTime * 0.3f);
        }

        bool wasDestroyed;

        void WatchDestruction()
        {
            bool dead = carrier.Destroyed;
            if (dead && !wasDestroyed)
            {
                Notices.Post($"{carrier.shipName} destroyed!");
                PadSupport.Rumble(1f, 0.8f, 1.2f);
            }
            else if (!dead && wasDestroyed)
            {
                Notices.Post($"{carrier.shipName} back in action: shield and hull restored");
                if (orbitCamera != null && orbitCamera.target == carrier.transform) orbitCamera.distance = ShipViewDistance;
            }
            wasDestroyed = dead;
        }

        /// <summary>The frigate's pad layout (no hangar, so the D-pad and triggers fly the ship).</summary>
        bool GunshipPad => carrier != null && !carrier.HasHangar;

        public void LevelOff()
        {
            carrier.LevelOff();
            Notices.Post("Levelling off");
        }

        void KeyboardControls(Keyboard k)
        {
            // P too: in a browser at full screen, the first Esc only leaves full screen
            if (k.escapeKey.wasPressedThisFrame || k.pKey.wasPressedThisFrame) { Menu.Open(); return; }
            if (k.hKey.wasPressedThisFrame) hud.ToggleHelp();
            if (k.equalsKey.wasPressedThisFrame || k.numpadPlusKey.wasPressedThisFrame) Hud.ChangeTextSize(+1);
            if (k.minusKey.wasPressedThisFrame || k.numpadMinusKey.wasPressedThisFrame) Hud.ChangeTextSize(-1);
            if (k.f11Key.wasPressedThisFrame && !LaunchArgs.OnWeb) GameSession.ToggleFullScreen();   // in a browser, F11 is the browser's
            if (k.nKey.wasPressedThisFrame) ToggleMute();
            if (carrier.Destroyed) return;   // nothing to fly or fight with until it respawns

            if (k.digit1Key.wasPressedThisFrame) LaunchFighters();
            if (k.digit2Key.wasPressedThisFrame) LaunchDrones();
            if (k.digit3Key.wasPressedThisFrame) LaunchDropships();
            if (k.mKey.wasPressedThisFrame && warp == null) FireMissiles();   // with a warp drive, M is the system map
            if (k.fKey.wasPressedThisFrame) FireGuns();
            if (k.gKey.wasPressedThisFrame) ToggleAutoFire();
            if (k.qKey.wasPressedThisFrame && carrier.HasHangar) WingAttack();
            if (k.eKey.wasPressedThisFrame && carrier.HasHangar) WingEscort();
            if (k.rKey.wasPressedThisFrame) WingReturn();

            // targeting: T cycles, a click picks a ship, X stops targeting
            if (k.tKey.wasPressedThisFrame) NextTarget();
            if (k.xKey.wasPressedThisFrame || k.backspaceKey.wasPressedThisFrame) ClearTarget();
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame) PickAt(mouse.position.ReadValue());

            if (k.cKey.wasPressedThisFrame) CycleCamera();
            if (k.lKey.wasPressedThisFrame) LevelOff();
            if (k.bKey.wasPressedThisFrame) ChargeMainGun();

            // mining: Z laser, V scan, I cargo list, J jettison
            if (k.zKey.wasPressedThisFrame) ToggleMiningLaser();
            if (k.vKey.wasPressedThisFrame) MineralScan();
            if (k.iKey.wasPressedThisFrame) ToggleManifest();
            if (k.jKey.wasPressedThisFrame) Jettison();
        }

        // ------------------------------------------------------------ main gun, shield, CIWS

        MainBeam mainGun;
        Shield shield;
        CiwsDirector ciws;
        Health hull;
        MiningLaser miningLaser;
        MineralSensor sensor;
        CargoHold cargo;
        bool systemsFound;

        void FindSystems()
        {
            if (systemsFound || carrier == null) return;
            systemsFound = true;
            mainGun = carrier.GetComponentInChildren<MainBeam>();
            shield = carrier.GetComponentInChildren<Shield>(true);
            ciws = carrier.GetComponent<CiwsDirector>();
            hull = carrier.GetComponent<Health>();
            miningLaser = carrier.GetComponentInChildren<MiningLaser>();
            sensor = carrier.GetComponentInChildren<MineralSensor>();
            cargo = carrier.GetComponent<CargoHold>();
        }

        /// <summary>The ship was swapped for another (PlayerFrigate.SwapTo): find its systems and
        /// its size again.</summary>
        public void ShipChanged()
        {
            systemsFound = false;
            shipRadius = 0f;
            warp = carrier != null ? carrier.GetComponent<WarpDrive>() : null;
        }

        public MainBeam MainGun { get { FindSystems(); return mainGun; } }
        public MiningLaser Laser { get { FindSystems(); return miningLaser; } }
        public MineralSensor Sensor { get { FindSystems(); return sensor; } }
        public CargoHold Cargo { get { FindSystems(); return cargo; } }
        public Health Hull { get { FindSystems(); return hull; } }
        public Shield ShieldGen { get { FindSystems(); return shield; } }
        public CiwsDirector Ciws { get { FindSystems(); return ciws; } }
        public WarpDrive Warp => warp;

        // ------------------------------------------------------------ mining

        /// <summary>Switch the mining laser on (it cuts the nearest deposit in range) or off.</summary>
        public void ToggleMiningLaser()
        {
            if (Laser == null) { NotFitted($"The {carrier.shipName.ToLower()} has no mining laser"); return; }
            if (miningLaser.On)
            {
                miningLaser.TurnOff();
                SoundFX.Ui(Sfx.UiClear);
                Notices.Post("Mining laser off");
                return;
            }
            if (!miningLaser.TurnOn(out string why)) { NotFitted("Mining laser: " + why); return; }
            SoundFX.Ui(Sfx.UiLock);
            Notices.Post($"Mining laser on: cutting the nearest deposit within {miningLaser.range / 1000f:F1} km");
            PadSupport.Rumble(0.15f, 0.1f, 0.2f);
        }

        /// <summary>Sweep for minerals: the finds are marked on screen.</summary>
        public void MineralScan()
        {
            if (Sensor == null) { NotFitted($"The {carrier.shipName.ToLower()} has no mineral sensor"); return; }
            if (!sensor.Scan(out string why)) { NotFitted("Mineral scan: " + why); return; }
            Notices.Post($"Mineral scan: sweeping out to {sensor.ScanRange / 1000f:F0} km");
        }

        public void ToggleManifest()
        {
            if (Cargo == null) { NotFitted($"The {carrier.shipName.ToLower()} has no cargo hold"); return; }
            MiningDisplay.ManifestOpen = !MiningDisplay.ManifestOpen;
        }

        /// <summary>Dump the cheapest mineral in the hold, to make room for better.</summary>
        public void Jettison()
        {
            if (Cargo == null) { NotFitted($"The {carrier.shipName.ToLower()} has no cargo hold"); return; }
            var m = cargo.Cheapest();
            if (m == null) { NotFitted("The cargo hold is empty"); return; }
            float t = cargo.Jettison(m.Value);
            SoundFX.Ui(Sfx.UiClear);
            Notices.Post($"Jettisoned {t:F1} t of {Minerals.Name(m.Value).ToLower()}: {cargo.Used:F0} of {cargo.capacity:F0} t aboard");
        }

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
                    Notices.Post($"Main gun charging: {mainGun.chargeTime:F0} s, keep the bow on the target");
                    PadSupport.Rumble(0.2f, 0.1f, 0.3f);
                    break;
                case MainBeam.State.Charging:
                    mainGun.Cancel();
                    Notices.Post("Main gun charge cancelled");
                    break;
            }
        }

        // ------------------------------------------------------------ gamepad

        bool backCombo;

        /// <summary>Xbox 360 layout. Back acts when released, so it can also be held for
        /// combinations (Back + D-pad up/down = text size). Start opens the pause menu.</summary>
        void PadControls(Gamepad p)
        {
            bool back = p.selectButton.isPressed;
            if (p.startButton.wasPressedThisFrame) { Menu.Open(); return; }

            if (back)
            {
                if (p.dpad.up.wasPressedThisFrame) { Hud.ChangeTextSize(+1); backCombo = true; }
                if (p.dpad.down.wasPressedThisFrame) { Hud.ChangeTextSize(-1); backCombo = true; }
                // Back + D-pad left/right: zoom the camera (on a frigate's pad, left/right roll the ship)
                if (GunshipPad && orbitCamera != null)
                {
                    float z = (p.dpad.left.isPressed ? 1f : 0f) - (p.dpad.right.isPressed ? 1f : 0f);
                    if (z != 0f)
                    {
                        orbitCamera.distance = Mathf.Clamp(orbitCamera.distance * Mathf.Pow(3.3f, z * Time.unscaledDeltaTime),
                                                           orbitCamera.minDistance, orbitCamera.maxDistance);
                        backCombo = true;
                    }
                }
                // Back + A/Y/X/B: mining laser, scan, cargo list, jettison
                if (!carrier.Destroyed && Laser != null)
                {
                    if (p.buttonSouth.wasPressedThisFrame) { ToggleMiningLaser(); backCombo = true; }
                    if (p.buttonNorth.wasPressedThisFrame) { MineralScan(); backCombo = true; }
                    if (p.buttonWest.wasPressedThisFrame) { ToggleManifest(); backCombo = true; }
                    if (p.buttonEast.wasPressedThisFrame) { Jettison(); backCombo = true; }
                }
            }
            else if (GunshipPad)
            {
                // D-pad up/down pitch the nose and left/right roll the ship (read in Update)
            }
            else
            {
                if (p.dpad.up.wasPressedThisFrame) LaunchFighters();
                if (p.dpad.right.wasPressedThisFrame) LaunchDrones();
                if (p.dpad.down.wasPressedThisFrame) LaunchDropships();
                if (p.dpad.left.wasPressedThisFrame) WingReturn();
            }
            if (p.selectButton.wasReleasedThisFrame) { if (!backCombo) hud.ToggleHelp(); backCombo = false; }
            if (carrier.Destroyed || (back && Laser != null)) return;   // with Back held, the face buttons are the mining controls

            if (p.buttonSouth.wasPressedThisFrame) { if (GunshipPad) LevelOff(); else WingAttack(); }   // A
            if (p.buttonEast.wasPressedThisFrame) ClearTarget();          // B
            if (p.buttonWest.wasPressedThisFrame) { if (GunshipPad) ChargeMainGun(); else WingEscort(); }   // X
            if (p.buttonNorth.wasPressedThisFrame) NextTarget();          // Y
            if (p.rightShoulder.wasPressedThisFrame) FireGuns();          // RB
            if (p.leftShoulder.wasPressedThisFrame && (carrier.HasMissiles || Landing == null)) FireMissiles();   // LB (landing, without missiles)
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
            Notices.Post(message);
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
            Notices.Post(on ? "Main guns: auto-fire ON" : "Main guns: auto-fire OFF");
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
            var targets = Targets.All;
            if (targets.Count == 0) return;
            // the next target in the list that isn't still hiding in an ambush, and is within range
            for (int tries = 0; tries < targets.Count; tries++)
            {
                targetIndex = carrier.target == null && tries == 0 ? targetIndex % targets.Count : (targetIndex + 1) % targets.Count;
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
            Notices.Post("Target cleared: guns stowed, attacking craft back to escort");
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
            Notices.Post(SoundFX.Muted ? "Sound off" : "Sound on");
        }

        public void SelectTarget(Transform t) => Select(t);

        void Select(Transform t)
        {
            if (t == null) return;
            int i = IndexOf(t);
            if (i >= 0) targetIndex = i;
            carrier.SetTarget(t);
            SoundFX.Ui(Sfx.UiLock);
        }

        static int IndexOf(Transform t)
        {
            var targets = Targets.All;
            for (int i = 0; i < targets.Count; i++) if (targets[i] == t) return i;
            return -1;
        }

        void NoTarget()
        {
            SoundFX.Ui(Sfx.UiError);
            Notices.Post(PadSupport.UsingPad ? "No target: press Y to pick one" : "No target: press T or click a ship");
        }

        /// <summary>Target the hostile under (or nearest to) the mouse click.</summary>
        void PickAt(Vector2 screen)
        {
            var cam = Camera.main;
            if (cam == null) return;
            if (hud.Covers(new Vector2(screen.x, Screen.height - screen.y))) return;
            var targets = Targets.All;

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
    }
}

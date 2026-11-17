using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarrierGame.Demo
{
    /// <summary>
    /// Docking at the friendly stations, bases and depots of the star system. Within docking
    /// range of one, out of combat, K (LB on the pad) brings the frigate in: the docking computer
    /// flies her to a point out along the berth's way in, lines her up and takes her in slowly
    /// (down onto a pad from above it, nose first into a pier), and the last few metres she is
    /// drawn in and moored. She is repaired (hull and shield) there. The helm, the throttle or a
    /// fight calls the approach off; one that gets nowhere (blocked, far too long) is finished
    /// the old way, with a fade. K again undocks: she pulls away from the berth under her own
    /// power (up off a pad first). While docked the warp drive and landing are locked.
    /// </summary>
    [DefaultExecutionOrder(290)]   // before PlanetLanding, which shares the K key
    public class Docking : MonoBehaviour
    {
        public enum State { Free, Docking, Docked, Undocking }

        public CarrierController carrier;
        public WarpDrive drive;
        public float fadeTime = 1.1f;
        [Tooltip("How far out along the berth's way in the ship lines up before the last of the way in.")]
        public float standOff = 300f;
        [Tooltip("Top speed on the way in to the stand-off point, and on the final run in (m/s).")]
        public float approachSpeed = 80f, finalSpeed = 25f;
        [Tooltip("How long a flown approach may take before it is finished with a fade.")]
        public float approachLimit = 150f;

        /// <summary>Docking with the fade at once, as it used to be, instead of flying in (the
        /// self-tests and reels that aren't about docking, so their timings hold).</summary>
        public static bool QuickDock;
        /// <summary>Flown approaches finished (and how many had to be finished with a fade).</summary>
        public static int Flown, FadedIn;
        /// <summary>The fade over the screen now (0: none): a flown docking never fades.</summary>
        public float Fade => fade;
        /// <summary>What the docking computer is doing on the way in ("" when it isn't).</summary>
        public string Phase { get; private set; } = "";
        /// <summary>Flying in under the docking computer (not yet drawn in to the berth).</summary>
        public bool Approaching { get; private set; }

        public static Docking Instance { get; private set; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Instance = null; QuickDock = false; Flown = FadedIn = 0; }

        CarrierDemoInput demo;
        CarrierDemoInput Demo => demo != null ? demo : (demo = FindAnyObjectByType<CarrierDemoInput>());

        public State Current { get; private set; }
        /// <summary>Docked, or on the way in or out.</summary>
        public static bool Busy => Instance != null && Instance.Current != State.Free;
        public static bool Docked => Instance != null && Instance.Current == State.Docked;
        /// <summary>K is docking's (somewhere to dock is in range, or the ship is docked): landing leaves it alone.</summary>
        public static bool WantsKey => Instance != null && (Instance.Current != State.Free || Instance.candidate != null);

        /// <summary>Where the ship is docked (or docking).</summary>
        public Installation At { get; private set; }
        public int Dockings { get; private set; }
        /// <summary>What the last visit did: the hull repaired (points).</summary>
        public float LastRepair { get; private set; }

        Installation candidate;
        string candidateWhy = "";
        float fade;

        void Awake()
        {
            Instance = this;
            if (carrier != null && drive == null) drive = carrier.GetComponent<WarpDrive>();
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>The place the ship could dock at now (null if none), and if it can't yet, why:
        /// in docking range of its berth, or out where a warp or an auto-flight there leaves the
        /// ship (K then flies her in first: Dock).</summary>
        public Installation Candidate(out string why)
        {
            why = "";
            if (carrier == null || PlanetLanding.Busy) return null;
            var s = Installation.ApproachableNear(carrier.transform.position, NavigationMode.Flying);
            if (s == null) return null;
            if (carrier.Destroyed) why = "the frigate is destroyed";
            else if (carrier.Tractored) why = "held by a tractor beam";
            else if (drive != null && drive.Current != WarpDrive.State.Ready) why = "in warp";
            else if (drive != null && drive.InCombat(out string c)) why = c;
            else if (PlanetLanding.Instance != null && PlanetLanding.Instance.Autopilot) why = "the landing auto-pilot is flying her";
            return s;
        }

        /// <summary>Dock at the place in range (the K key): in docking range, brought in to the
        /// berth now; farther out, flown in to it first (navigation's auto-travel, ending docked).
        /// Returns false (and says why) if it can't.</summary>
        public bool Dock()
        {
            if (Current != State.Free) return false;
            var s = Candidate(out string why);
            if (s == null || why != "")
            {
                Notices.Post(s != null ? $"Can't dock at {s.displayName}: {why}" : "Nowhere to dock in range");
                SoundFX.Ui(Sfx.UiError);
                return false;
            }
            if (!s.InDockRange(carrier.transform.position)) return FlyIn(s);
            StartCoroutine(DockAt(s));
            return true;
        }

        /// <summary>Out in the approach: flown in to the berth, then docked.</summary>
        bool FlyIn(Installation s)
        {
            var nav = Nav;
            var wp = s.Point;
            if (nav == null || wp == null) { Notices.Post($"Fly closer to {s.displayName} to dock"); SoundFX.Ui(Sfx.UiError); return false; }
            // (already on the way in there: carry on)
            if (FlyingIn(s)) return true;
            var was = Modes.Current;
            if (!Modes.Is(ModeOfPlay.Navigation)) Modes.Set(ModeOfPlay.Navigation);
            if (nav.TravelTo(wp, NavigationMode.Then.Dock)) return true;
            if (was != ModeOfPlay.Navigation) Modes.Set(was);
            return false;
        }

        NavigationMode nav;
        NavigationMode Nav => nav != null ? nav : (nav = FindAnyObjectByType<NavigationMode>());

        /// <summary>Navigation is flying the ship in to dock at it now.</summary>
        public bool FlyingIn(Installation s) =>
            s != null && Nav != null && Nav.Travelling && Nav.Finish == NavigationMode.Then.Dock && Nav.Course == s.Point;

        public bool Undock()
        {
            if (Current != State.Docked) return false;
            StartCoroutine(Leave());
            return true;
        }

        IEnumerator DockAt(Installation s)
        {
            Current = State.Docking;
            At = s;
            if (drive != null) drive.lockedBecause = "undock first";
            carrier.SetTarget(null);
            SoundFX.Ui(Sfx.UiLock);
            // (the other autopilots off: navigation only if it isn't the one bringing her in)
            var d = Demo;
            if (d != null) d.StopAutopilotsBut(FlyingIn(s) ? Nav : null);
            bool flown = false;
            if (!QuickDock)
            {
                Notices.Post($"Docking at {s.displayName}: the docking computer has her");
                var result = new FlightResult();
                float began = Time.time;
                yield return ComputerApproach(s, result);
                Debug.Log($"[Docking] {s.displayName}: the docking computer took {Time.time - began:F0} s " +
                          (result.calledOff != null ? $"(called off: {result.calledOff})" : result.inPlace ? "(flown in)" : "(got nowhere: faded in)"));
                if (result.calledOff != null)
                {
                    if (FlyingIn(s)) Nav.Stop($"Docking at {s.displayName} called off: {result.calledOff}");
                    else Notices.Post($"Docking at {s.displayName} called off: {result.calledOff}");
                    SoundFX.Ui(Sfx.UiError);
                    Release();
                    yield break;
                }
                if (carrier.Destroyed) { Release(); yield break; }
                flown = result.inPlace;
                if (flown) Flown++;
                else { FadedIn++; Notices.Post($"Docking at {s.displayName}: bringing her in"); }
            }
            else Notices.Post($"Docking at {s.displayName}");
            carrier.externalMotion = true;
            carrier.enginesCut = true;
            if (!flown)
            {
                // (the old way: the screen fades while she is drawn in)
                for (float t = 0f; t < fadeTime; t += Time.deltaTime)
                {
                    if (carrier.Destroyed) { Release(); yield break; }
                    fade = t / fadeTime;
                    Vector3 to = s.BerthPosition - carrier.transform.position;
                    if (to.sqrMagnitude > 1f) carrier.transform.position += to.normalized * Mathf.Min(to.magnitude, 120f * Time.deltaTime);
                    yield return null;
                }
                fade = 1f;
            }
            if (carrier.Destroyed) { Release(); yield break; }
            Park();
            yield return null;

            // the station's services: a full repair (the hold is sold on the trade page, by choice)
            var hull = carrier.GetComponent<Health>();
            float before = hull != null ? hull.Current : 0f;
            if (hull != null) hull.Repair();
            LastRepair = hull != null ? hull.Current - before : 0f;
            var shield = carrier.GetComponentInChildren<Shield>();
            if (shield != null) shield.SetOffline(false);
            Dockings++;
            Current = State.Docked;
            SoundFX.Play(Sfx.Impact, carrier.transform.position, 0.35f, 0.5f);
            PadSupport.Rumble(0.3f, 0.2f, 0.25f);
            Notices.Post($"Docked at {s.displayName}: {Services()}");
            if (!flown)
            {
                for (float t = 0f; t < fadeTime; t += Time.deltaTime)
                {
                    fade = 1f - t / fadeTime;
                    yield return null;
                }
            }
            fade = 0f;
        }

        class FlightResult { public string calledOff; public bool inPlace; }

        string cancelWhy;

        /// <summary>Call off a flown approach under way (an AI pilot given new orders): false if
        /// there is none.</summary>
        public bool CancelApproach(string why)
        {
            if (!Approaching) return false;
            cancelWhy = string.IsNullOrEmpty(why) ? "called off" : why;
            return true;
        }

        /// <summary>Where and how she is moored at the berth: there, level, her nose to the way out.</summary>
        Quaternion ParkRotation()
        {
            Vector3 f = At != null && At.berth != null ? At.berth.forward : carrier.transform.forward;
            f = Vector3.ProjectOnPlane(f, carrier.Up);
            if (f.sqrMagnitude < 1e-4f) f = carrier.upFrame * Vector3.forward;
            return Quaternion.LookRotation(f.normalized, carrier.Up);
        }

        /// <summary>The docking computer flies her in: out along the berth's way in to a point
        /// standOff out, then slowly in (down onto a pad from over it, nose first into a pier),
        /// and the last few metres she is drawn in to the berth. Called off by the helm, the
        /// throttle, a fight or her destruction; finished with a fade (inPlace false) when it gets
        /// nowhere.</summary>
        IEnumerator ComputerApproach(Installation s, FlightResult result)
        {
            var d = Demo;
            if (d == null) yield break;
            Approaching = true;
            float start = Time.time, throttleSet = -1f, checkAt = Time.time + 15f, logAt = Time.time + 60f;
            Vector3 checkFrom = carrier.transform.position;
            bool final = false;
            cancelWhy = null;
            try
            {
                while (true)
                {
                    if (carrier.Destroyed) yield break;
                    if (d.PilotFlying) { result.calledOff = "you have the helm"; yield break; }
                    if (throttleSet >= 0f && Mathf.Abs(carrier.throttle - throttleSet) > 0.01f) { result.calledOff = "you have the throttle"; yield break; }
                    if (drive != null && drive.InCombat(out string fight)) { result.calledOff = fight; yield break; }
                    if (cancelWhy != null) { result.calledOff = cancelWhy; yield break; }
                    Vector3 me = carrier.transform.position, b = s.BerthPosition, f = s.ApproachAxis;
                    bool pad = Vector3.Dot(f, carrier.Up) > 0.7f;
                    float toBerth = Vector3.Distance(me, b);
                    // (in: drawn the last few metres)
                    if (toBerth < 40f && carrier.Speed < 8f) break;
                    // (nowhere: not moving for 15 s, blocked; or far too long about it)
                    bool stuck = false;
                    if (Time.time >= checkAt)
                    {
                        stuck = Vector3.Distance(me, checkFrom) < 30f;
                        checkFrom = me;
                        checkAt = Time.time + 15f;
                    }
                    if (stuck || Time.time - start > approachLimit) { ClearControls(d); yield break; }

                    float accel = Mathf.Max(1f, carrier.acceleration);
                    Vector3 aim;
                    float want;
                    float? lift = 0f;
                    Vector3 so = b + f * standOff;
                    if (!final)
                    {
                        // out to the stand-off point: straight there from in front of the berth, near
                        // its line; from anywhere else, round what the berth is built into first
                        float along = Vector3.Dot(me - b, f);
                        float lateral = Vector3.ProjectOnPlane(me - b, f).magnitude;
                        bool inFront = along > 0f && lateral < Mathf.Max(150f, along * 0.7f);
                        aim = inFront ? so : s.ApproachPoint(me);
                        float left = Vector3.Distance(me, aim) + (inFront ? 0f : Vector3.Distance(aim, so));
                        want = Mathf.Clamp(Mathf.Sqrt(2f * accel * 0.6f * Mathf.Max(0f, left - 40f)), 6f, approachSpeed);
                        // (slow enough to turn onto it: its turning circle no wider than the way left
                        // to the point, and easing off while the bow is well off it; at full speed
                        // she circled the stand-off point and never settled on the way in)
                        float toAim = Vector3.Distance(me, aim);
                        float turnable = Mathf.Max(6f, carrier.turnRate * Mathf.Deg2Rad * toAim * 0.5f);
                        float bowOff = ShipAutopilot.BowError(carrier, aim);
                        want = Mathf.Min(want, turnable) * Mathf.Lerp(1f, 0.25f, Mathf.Clamp01(bowOff / 90f));
                        Phase = $"lining up, {CombatMode.Distance(Vector3.Distance(me, so))} to go";
                        // (a long time about it: what it is doing, for the log)
                        if (Time.time >= logAt)
                        {
                            logAt = Time.time + 15f;
                            Debug.Log($"[Docking] {s.displayName}: still lining up after {Time.time - start:F0} s: {Vector3.Distance(me, so):F0} m from the stand-off point, " +
                                      $"{along:F0} m out along the way in, {lateral:F0} m off its line, in front {inFront}, aiming {Vector3.Distance(me, aim):F0} m off, speed {carrier.Speed:F0}");
                        }
                        // (at the stand-off point, or already in nearer than it on the berth's line)
                        if (inFront && (Vector3.Distance(me, so) < 70f || (along < standOff + 30f && lateral < Mathf.Max(60f, along * 0.3f)))) final = true;
                    }
                    else if (pad)
                    {
                        // over the pad: stop over it, turn to the way out, and come down on the lift thrusters
                        float height = Vector3.Dot(me - b, f);
                        Vector3 over = b + f * height;
                        float off = Vector3.Distance(me, over);
                        aim = off > 25f ? over : me + (ParkRotation() * Vector3.forward) * 1000f;
                        want = off > 25f ? Mathf.Clamp(off / 4f, 3f, finalSpeed) : 0f;
                        lift = off >= 90f ? 0f : height < 0f ? 0.5f : -Mathf.Clamp(height / 80f, 0.2f, 1f);
                        Phase = $"coming down onto the pad, {height:F0} m";
                    }
                    else
                    {
                        // into a pier: nose first, slower the nearer
                        aim = b;
                        want = Mathf.Clamp(toBerth / 6f + 3f, 4f, finalSpeed);
                        Phase = $"final approach, {toBerth:F0} m";
                    }
                    var v = ShipAutopilot.BowOn(carrier, aim);
                    d.autoRudder = v.x;
                    d.autoPitch = v.y;
                    d.autoElevator = lift;
                    d.autoRoll = null;
                    carrier.throttle = Mathf.Clamp01(want / Mathf.Max(1f, carrier.maxSpeed));
                    throttleSet = carrier.throttle;
                    yield return null;
                }
                // drawn in: eased to the berth, her nose to the way out
                ClearControls(d);
                Phase = "coming alongside";
                carrier.externalMotion = true;
                carrier.enginesCut = true;
                Vector3 p0 = carrier.transform.position;
                Quaternion q0 = carrier.transform.rotation, q1 = ParkRotation();
                for (float t = 0f; t < 1.5f; t += Time.deltaTime)
                {
                    if (carrier.Destroyed) yield break;
                    float k = Mathf.SmoothStep(0f, 1f, t / 1.5f);
                    carrier.transform.SetPositionAndRotation(Vector3.Lerp(p0, s.BerthPosition, k), Quaternion.Slerp(q0, q1, k));
                    carrier.SetMotion(0f);
                    yield return null;
                }
                result.inPlace = true;
            }
            finally
            {
                Approaching = false;
                Phase = "";
                if (!result.inPlace) ClearControls(d);
            }
        }

        void ClearControls(CarrierDemoInput d)
        {
            if (d == null) return;
            d.autoRudder = d.autoPitch = d.autoElevator = d.autoRoll = null;
            carrier.throttle = 0f;
        }

        string Services()
        {
            string r = LastRepair > 0.5f ? $"hull repaired (+{LastRepair:F0})" : "hull sound";
            if (At != null && At.market) r += ", a market";
            var site = IndustrySite.At(At);
            if (site != null)
            {
                var what = new System.Collections.Generic.List<string>();
                if (site.Spec != null) what.Add(IndustrySite.Services(site.Spec));
                if (site.Recruits) what.Add("recruits crew");
                if (what.Count > 0) r += $", {string.Join(", ", what)} (U)";
            }
            return r;
        }

        /// <summary>Moor the ship at the berth, level, its nose to the way out.</summary>
        void Park()
        {
            if (At == null) return;
            Vector3 f = At.berth != null ? At.berth.forward : carrier.transform.forward;
            // (level in the ship's own up frame: out from the planet the station circles)
            f = Vector3.ProjectOnPlane(f, carrier.Up);
            if (f.sqrMagnitude < 1e-4f) f = carrier.upFrame * Vector3.forward;
            carrier.transform.position = At.BerthPosition;
            carrier.Face(f);
            carrier.SetMotion(0f);
            carrier.throttle = 0f;
        }

        IEnumerator Leave()
        {
            Current = State.Undocking;
            Notices.Post($"Undocking from {At.displayName}");
            SoundFX.Ui(Sfx.UiClear);
            Park();
            carrier.externalMotion = false;
            carrier.enginesCut = false;
            // (off a pad: up clear of it, and of what is built round it, before going ahead)
            var d = Demo;
            bool pad = At != null && Vector3.Dot(At.ApproachAxis, carrier.Up) > 0.7f;
            if (pad && d != null && !QuickDock)
            {
                for (float t = 0f; t < 4f && !carrier.Destroyed && !d.PilotFlying; t += Time.deltaTime)
                {
                    d.autoElevator = 1f;
                    yield return null;
                }
                d.autoElevator = null;
            }
            carrier.SetMotion(30f);
            carrier.throttle = Mathf.Max(carrier.throttle, 0.3f);
            for (float t = 0f; t < 2f && !carrier.Destroyed; t += Time.deltaTime) yield return null;
            Release();
        }

        void Release()
        {
            if (Current == State.Free) return;
            if (Current == State.Docking || Current == State.Docked) carrier.externalMotion = false;
            carrier.enginesCut = false;
            if (drive != null && drive.lockedBecause == "undock first") drive.lockedBecause = null;
            Current = State.Free;
            At = null;
            fade = 0f;
        }

        void Update()
        {
            if (carrier == null) return;
            if (Current == State.Free) candidate = Candidate(out candidateWhy);
            else candidate = null;
            if (Current == State.Docked)
            {
                // destroyed at the berth (it shouldn't happen): it comes back at its respawn point
                if (carrier.Destroyed) { StopAllCoroutines(); Release(); return; }
                Park();
            }
            if (InputFocus.PanelUp) return;   // a panel has the keys
            var k = Keyboard.current;
            var pad = Gamepad.current;
            bool press = (k != null && k.kKey.wasPressedThisFrame) || CarrierDemoInput.PadLandPressed(pad);
            if (!press) return;
            if (Current == State.Free && candidate != null) Dock();
            else if (Current == State.Docked) Undock();
        }

        /// <summary>The HUD's docking line (null when there is nothing to say).</summary>
        public string HudText()
        {
            string key = PadSupport.UsingPad && Gamepad.current != null ? "Back+B" : "K";
            switch (Current)
            {
                case State.Docking: return $"Dock  <color=#bfe6ff>docking at {At?.displayName}{(Phase.Length > 0 ? ": " + Phase : "")}</color>";
                case State.Undocking: return $"Dock  <color=#bfe6ff>leaving {At?.displayName}</color>";
                case State.Docked: return $"Docked  {At?.displayName}  <color=#7ee08a>{Services()}</color>  <color=#9aa4b0>{key} undock</color>";
            }
            if (candidate == null) return null;
            if (candidateWhy != "") return $"Dock  <color=#ff6050>{candidate.displayName}: {candidateWhy}</color>";
            if (FlyingIn(candidate)) return $"Dock  <color=#bfe6ff>flying in to {candidate.displayName}</color>";
            return candidate.InDockRange(carrier.transform.position)
                ? $"Dock  <color=#7ee08a>{key} dock at {candidate.displayName}</color>"
                : $"Dock  <color=#7ee08a>{key} fly in and dock at {candidate.displayName}</color>";
        }

        void OnGUI()
        {
            if (fade <= 0.001f || Event.current.type != EventType.Repaint) return;
            GUI.depth = -60;
            var c = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, Mathf.Clamp01(fade));
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = c;
        }
    }
}

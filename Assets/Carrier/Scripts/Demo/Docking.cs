using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarrierGame.Demo
{
    /// <summary>
    /// Docking at the friendly stations, bases and depots of the star system. Within docking
    /// range of one, out of combat, K (LB on the pad) brings the frigate in: the screen fades,
    /// and the ship is moored at the berth, repaired (hull and shield), and where the station
    /// has a market its hold is sold. K again undocks: the ship pulls away from the berth under
    /// its own power. While docked the warp drive and landing are locked.
    /// </summary>
    [DefaultExecutionOrder(290)]   // before PlanetLanding, which shares the K key
    public class Docking : MonoBehaviour
    {
        public enum State { Free, Docking, Docked, Undocking }

        public CarrierController carrier;
        public WarpDrive drive;
        public float fadeTime = 1.1f;

        public static Docking Instance { get; private set; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        public State Current { get; private set; }
        /// <summary>Docked, or on the way in or out.</summary>
        public static bool Busy => Instance != null && Instance.Current != State.Free;
        public static bool Docked => Instance != null && Instance.Current == State.Docked;
        /// <summary>K is docking's (somewhere to dock is in range, or the ship is docked): landing leaves it alone.</summary>
        public static bool WantsKey => Instance != null && (Instance.Current != State.Free || Instance.candidate != null);

        /// <summary>Where the ship is docked (or docking).</summary>
        public Installation At { get; private set; }
        public int Dockings { get; private set; }
        /// <summary>What the last visit did: hull repaired (points), ore sold (tonnes, credits).</summary>
        public float LastRepair { get; private set; }
        public float LastSoldTonnes { get; private set; }
        public float LastSoldCredits { get; private set; }

        Installation candidate;
        string candidateWhy = "";
        float fade;

        void Awake()
        {
            Instance = this;
            if (carrier != null && drive == null) drive = carrier.GetComponent<WarpDrive>();
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>The place the ship could dock at now (null if none), and if it can't yet, why.</summary>
        public Installation Candidate(out string why)
        {
            why = "";
            if (carrier == null || PlanetLanding.Busy) return null;
            var s = Installation.DockableNear(carrier.transform.position);
            if (s == null) return null;
            if (carrier.Destroyed) why = "the frigate is destroyed";
            else if (drive != null && drive.Current != WarpDrive.State.Ready) why = "in warp";
            else if (drive != null && drive.InCombat(out string c)) why = c;
            return s;
        }

        /// <summary>Dock at the place in range (the K key). Returns false (and says why) if it can't.</summary>
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
            StartCoroutine(DockAt(s));
            return true;
        }

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
            carrier.externalMotion = true;
            SoundFX.Ui(Sfx.UiLock);
            Notices.Post($"Docking at {s.displayName}");
            for (float t = 0f; t < fadeTime; t += Time.deltaTime)
            {
                if (carrier.Destroyed) { Release(); yield break; }
                fade = t / fadeTime;
                Vector3 to = s.BerthPosition - carrier.transform.position;
                if (to.sqrMagnitude > 1f) carrier.transform.position += to.normalized * Mathf.Min(to.magnitude, 120f * Time.deltaTime);
                yield return null;
            }
            fade = 1f;
            if (carrier.Destroyed) { Release(); yield break; }
            Park();
            yield return null;

            // the station's services: a full repair, and the hold sold where there is a market
            var hull = carrier.GetComponent<Health>();
            float before = hull != null ? hull.Current : 0f;
            if (hull != null) hull.Repair();
            LastRepair = hull != null ? hull.Current - before : 0f;
            var shield = carrier.GetComponentInChildren<Shield>();
            if (shield != null) shield.SetOffline(false);
            LastSoldTonnes = 0f;
            LastSoldCredits = 0f;
            var hold = carrier.GetComponent<CargoHold>();
            if (s.market && hold != null && hold.Used > 0.5f)
            {
                LastSoldCredits = hold.SellAll(out float tonnes);
                LastSoldTonnes = tonnes;
            }
            Dockings++;
            Current = State.Docked;
            SoundFX.Play(Sfx.Impact, carrier.transform.position, 0.35f, 0.5f);
            PadSupport.Rumble(0.3f, 0.2f, 0.25f);
            Notices.Post($"Docked at {s.displayName}: {Services()}");
            for (float t = 0f; t < fadeTime; t += Time.deltaTime)
            {
                fade = 1f - t / fadeTime;
                yield return null;
            }
            fade = 0f;
        }

        string Services()
        {
            string r = LastRepair > 0.5f ? $"hull repaired (+{LastRepair:F0})" : "hull sound";
            if (At != null && At.market)
                r += LastSoldTonnes > 0.5f ? $", sold {LastSoldTonnes:F0} t of ore for {LastSoldCredits:N0} cr" : ", nothing to sell";
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
            carrier.SetMotion(30f);
            carrier.throttle = Mathf.Max(carrier.throttle, 0.3f);
            for (float t = 0f; t < 2f && !carrier.Destroyed; t += Time.deltaTime) yield return null;
            Release();
        }

        void Release()
        {
            if (Current == State.Free) return;
            if (Current == State.Docking || Current == State.Docked) carrier.externalMotion = false;
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
            bool press = (k != null && k.kKey.wasPressedThisFrame) || (pad != null && !carrier.HasMissiles && pad.leftShoulder.wasPressedThisFrame);
            if (!press) return;
            if (Current == State.Free && candidate != null) Dock();
            else if (Current == State.Docked) Undock();
        }

        /// <summary>The HUD's docking line (null when there is nothing to say).</summary>
        public string HudText()
        {
            string key = PadSupport.UsingPad && Gamepad.current != null ? "LB" : "K";
            switch (Current)
            {
                case State.Docking: return $"Dock  <color=#bfe6ff>docking at {At?.displayName}</color>";
                case State.Undocking: return $"Dock  <color=#bfe6ff>leaving {At?.displayName}</color>";
                case State.Docked: return $"Docked  {At?.displayName}  <color=#7ee08a>{Services()}</color>  <color=#9aa4b0>{key} undock</color>";
            }
            if (candidate == null) return null;
            return candidateWhy == "" ? $"Dock  <color=#7ee08a>{key} dock at {candidate.displayName}</color>"
                                      : $"Dock  <color=#ff6050>{candidate.displayName}: {candidateWhy}</color>";
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

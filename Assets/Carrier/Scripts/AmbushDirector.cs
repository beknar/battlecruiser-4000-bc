using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// Runs the asteroid-field ambush. The enemies start hidden behind the big asteroid and the
    /// rocks and wrecks round it (CombatPilot, lurking). The first wave, fighters, springs the
    /// ambush as soon as one of them can see the frigate within `spotRange` (or the frigate
    /// reaches the trigger point); the second wave, the heavy cruiser and its drones, follows
    /// `secondWaveDelay` seconds later. The music switches from the travelling track to the
    /// battle track the moment the ambush is sprung.
    /// </summary>
    public class AmbushDirector : MonoBehaviour
    {
        public CarrierController player;
        public CombatPilot[] firstWave;
        public CombatPilot[] secondWave;
        public float secondWaveDelay = 10f;
        [Tooltip("A lurking fighter that can see the player this close springs the ambush.")]
        public float spotRange = 3200f;
        [Tooltip("The ambush is sprung anyway when the player comes this close to the trigger point.")]
        public Vector3 triggerPoint;
        public float triggerRadius = 600f;

        [Header("Music")]
        public string travelTrack = "Travel_Calm";
        public string battleTrack = "Ambush_Combat";

        [Header("Route (for the demo reel's autopilot)")]
        [Tooltip("The way across the big asteroid and round its far end.")]
        public Vector3[] route;
        [Tooltip("Index in the route where the ship comes round the end of the asteroid.")]
        public int roundingIndex = 5;

        public static AmbushDirector Instance { get; private set; }
        public bool Sprung { get; private set; }
        public bool SecondWaveIn { get; private set; }
        public float SprungAt { get; private set; } = -1f;
        public string SprungBy { get; private set; } = "";

        void Awake()
        {
            Instance = this;
            MusicDirector.UseTracks(travelTrack, battleTrack, loopsInSync: false, waitForCombatCue: true);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            if (player == null) player = FindAnyObjectByType<CarrierController>();
        }

        void Update()
        {
            if (player == null) return;
            if (!Sprung)
            {
                foreach (var e in firstWave)
                    if (e != null && e.Current != CombatPilot.State.Lurk) { Spring($"{e.name} came under fire"); return; }
                foreach (var e in secondWave)
                    if (e != null && e.Current != CombatPilot.State.Lurk) { Spring($"{e.name} came under fire"); return; }
                // any player's ship (in co-op, the others' too) reaching the far side or being seen
                if (!player.Destroyed && Watch(player.transform)) return;
                foreach (var ship in PlayerShips.All)
                {
                    if (ship == null || ship == player.transform) continue;
                    var h = ship.GetComponent<Health>();
                    if (h != null && h.Dead) continue;
                    if (PlayerShips.Available != null && !PlayerShips.Available(ship)) continue;
                    if (Watch(ship)) return;
                }
                return;
            }
            if (!SecondWaveIn && Time.time >= SprungAt + secondWaveDelay)
            {
                SecondWaveIn = true;
                foreach (var e in secondWave) if (e) e.Engage();
                SoundFX.Ui(Sfx.UiAlert);
                Demo.CarrierDemoInput.Toast("Heavy cruiser and drones closing from behind the rocks!");
            }
        }

        /// <summary>Spring the ambush if this ship has reached the trigger point or a lurking
        /// fighter can see it.</summary>
        bool Watch(Transform ship)
        {
            Vector3 p = ship.position;
            if (Vector3.Distance(p, triggerPoint) < triggerRadius) { Spring("the frigate reached the far side"); return true; }
            foreach (var f in firstWave)
            {
                if (f == null) continue;
                Vector3 e = f.transform.position;
                if (Vector3.Distance(e, p) < spotRange && Cover.Visible(e, p)) { Spring($"spotted by {f.name}"); return true; }
            }
            return false;
        }

        /// <summary>Spring the ambush: the fighters attack and the battle music starts.</summary>
        public void Spring(string reason)
        {
            if (Sprung) return;
            Sprung = true;
            SprungAt = Time.time;
            SprungBy = reason;
            foreach (var e in firstWave) if (e) e.Engage();
            MusicDirector.EnterCombat(secondWaveDelay + 20f);
            SoundFX.Ui(Sfx.UiAlert);
            Demo.CarrierDemoInput.Toast("AMBUSH! Fighters inbound");
            // give the guns something to shoot at if the pilot hasn't picked a target
            var demo = FindAnyObjectByType<Demo.CarrierDemoInput>();
            var nearest = CombatPilot.Nearest(firstWave, player.transform.position);
            if (demo != null && player.target == null && nearest != null) demo.SelectTarget(nearest.transform);
        }
    }
}

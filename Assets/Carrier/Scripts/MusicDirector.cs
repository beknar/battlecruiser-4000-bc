using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// Plays the soundtrack: a calm track and a combat track, crossfading between them.
    ///
    /// Open space (the default): two arrangements of the same track ("Void" from the Space
    /// Science Fiction Music Pack) loop in sync; while weapons and explosions are heard nearby
    /// it crossfades to the combat arrangement, and eases back once things have been quiet.
    ///
    /// The asteroid ambush (UseTracks): a quiet travelling track until the ambush is sprung
    /// (EnterCombat), then a battle track from its first bar, staying until the fighting has
    /// been over for a while.
    /// </summary>
    public class MusicDirector : MonoBehaviour
    {
        public float calmVolume = 0.30f;
        public float combatVolume = 0.34f;
        [Tooltip("Seconds without combat sounds before the music calms down.")]
        public float combatHold = 10f;
        public float fadeToCombat = 2.5f;
        public float fadeToCalm = 6f;

        /// <summary>0 = calm track, 1 = combat track.</summary>
        public static float CombatMix { get; private set; }
        public static bool Playing { get; private set; }
        /// <summary>The name of the track that is mostly audible now.</summary>
        public static string NowPlaying => !Playing ? "" : CombatMix >= 0.5f ? combatTrack : calmTrack;

        // set by the scene before the music starts (see UseTracks)
        static string calmTrack = "Music_Calm", combatTrack = "Music_Combat";
        static bool synced = true, eventDriven;
        static float forcedUntil = -1f, sceneHold = -1f, sceneFadeIn = -1f;

        AudioSource calm, combat;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            calmTrack = "Music_Calm"; combatTrack = "Music_Combat";
            synced = true; eventDriven = false;
            forcedUntil = sceneHold = sceneFadeIn = -1f;
            CombatMix = 0f; Playing = false;
        }

        /// <summary>Pick the scene's tracks (call from a scene object's Awake). With eventDriven the
        /// music stays calm until EnterCombat, then follows the fighting as usual.</summary>
        public static void UseTracks(string calmName, string combatName, bool loopsInSync, bool waitForCombatCue,
                                     float holdAfterFighting = 20f, float fadeIn = 1.2f)
        {
            calmTrack = calmName; combatTrack = combatName;
            synced = loopsInSync; eventDriven = waitForCombatCue;
            sceneHold = holdAfterFighting; sceneFadeIn = fadeIn;
        }

        /// <summary>Switch to the combat track now (an ambush), and keep it for at least `seconds`.</summary>
        public static void EnterCombat(float seconds = 15f)
        {
            eventDriven = false;
            forcedUntil = Time.time + seconds;
        }

        void Start()
        {
            var calmClip = SoundFX.LoadLibrary("Music/" + calmTrack);
            var combatClip = SoundFX.LoadLibrary("Music/" + combatTrack);
            if (calmClip == null || combatClip == null) { enabled = false; return; }
            if (sceneHold > 0f) combatHold = sceneHold;
            if (sceneFadeIn > 0f) fadeToCombat = sceneFadeIn;
            calm = Source(calmClip[0]);
            combat = Source(combatClip[0]);
            calm.volume = calmVolume;
            combat.volume = 0f;
            calm.Play();
            // separate tracks: the combat one waits silently and starts from the top when needed
            if (synced) combat.Play();
            Playing = true;
        }

        AudioSource Source(AudioClip clip)
        {
            var a = gameObject.AddComponent<AudioSource>();
            a.clip = clip;
            a.loop = true;
            a.spatialBlend = 0f;
            a.priority = 0;          // never stolen by effects
            a.dopplerLevel = 0f;
            a.playOnAwake = false;
            return a;
        }

        void Update()
        {
            if (calm == null) return;
            bool fighting = Time.time < forcedUntil ||
                            (!eventDriven && Time.time - SoundFX.LastCombatTime < combatHold);
            float target = fighting ? 1f : 0f;
            if (!synced && target > 0f && !combat.isPlaying) { combat.timeSamples = 0; combat.Play(); }
            float rate = 1f / (target > CombatMix ? fadeToCombat : fadeToCalm);
            CombatMix = Mathf.MoveTowards(CombatMix, target, rate * Time.deltaTime);
            // equal-power crossfade, so the overall level holds steady through the switch
            float k = CombatMix * Mathf.PI * 0.5f;
            calm.volume = calmVolume * Mathf.Cos(k);
            combat.volume = combatVolume * Mathf.Sin(k);
            if (synced)
            {
                // keep the two arrangements locked together
                if (Mathf.Abs(combat.timeSamples - calm.timeSamples) > 2048) combat.timeSamples = calm.timeSamples;
            }
            else if (CombatMix <= 0f && combat.isPlaying) combat.Stop();   // next fight starts it afresh
        }
    }
}

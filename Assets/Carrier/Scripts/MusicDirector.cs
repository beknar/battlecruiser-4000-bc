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
    ///
    /// While it is calm, the atmospheric music changes every one to five minutes (cycleMin to
    /// cycleMax seconds, picked afresh each time): it crossfades to the next of the calm tracks
    /// there are (every "...Calm" track in the music folder), from the top. Away from the scene's
    /// own calm track, the combat track starts from its first bar when a fight starts, rather
    /// than running in step.
    /// </summary>
    public class MusicDirector : MonoBehaviour
    {
        public float calmVolume = 0.30f;
        public float combatVolume = 0.34f;
        [Tooltip("Seconds without combat sounds before the music calms down.")]
        public float combatHold = 10f;
        public float fadeToCombat = 2.5f;
        public float fadeToCalm = 6f;
        [Tooltip("The calm music changes after this many seconds at the least, and the most (picked afresh each time).")]
        public float cycleMin = 60f, cycleMax = 300f;
        [Tooltip("Seconds the change from one calm track to the next takes.")]
        public float cycleFade = 8f;

        /// <summary>Calm tracks changed to since the level started (for the self-test).</summary>
        public static int Cycles { get; private set; }

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
        // the calm tracks to go round, the one playing, the one fading in (and how far), and when
        // the next change is due
        AudioClip[] calmClips;
        /// <summary>The calm tracks the music goes round (those of them there are).</summary>
        static readonly string[] CalmTracks = { "Music_Calm", "Travel_Calm" };
        // the combat track was started from its first bar (away from the scene's own calm track):
        // it isn't pulled into step with the calm one until it has stopped
        bool combatFromTop;
        AudioClip pairedCalm;
        int calmIndex;
        AudioSource calmNext;
        float cycleK = -1f, nextCycle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            calmTrack = "Music_Calm"; combatTrack = "Music_Combat";
            synced = true; eventDriven = false;
            forcedUntil = sceneHold = sceneFadeIn = -1f;
            CombatMix = 0f; Playing = false; Cycles = 0;
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

        /// <summary>Drop any combat cue still running (the level restarted).</summary>
        public static void ForgetCombat() => forcedUntil = -1f;

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
            pairedCalm = calmClip[0];
            combat = Source(combatClip[0]);
            // the calm tracks there are, the scene's own first (only those loaded)
            var list = new System.Collections.Generic.List<AudioClip> { calmClip[0] };
            foreach (var name in CalmTracks)
            {
                if (name == calmTrack) continue;
                var c = SoundFX.LoadLibrary("Music/" + name);
                if (c != null && c.Length > 0) list.Add(c[0]);
            }
            calmClips = list.ToArray();
            calmIndex = 0;
            calmNext = Source(calmClip[0]);
            calmNext.volume = 0f;
            nextCycle = Time.time + Random.Range(cycleMin, cycleMax);
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
            // enemies out of hiding close by: still a fight, even in a lull
            if (!eventDriven && Camera.main != null && CombatPilot.AnyEngagedNear(Camera.main.transform.position, 12000f)) fighting = true;
            float target = fighting ? 1f : 0f;
            // in step with the combat arrangement only while the scene's own calm track plays (and
            // the combat one wasn't started from its top, away from it)
            bool paired = synced && calm.clip == pairedCalm && cycleK < 0f;
            bool inStep = paired && !combatFromTop;
            if (!inStep && target > 0f && !combat.isPlaying) { combat.timeSamples = 0; combat.Play(); combatFromTop = synced; }
            CycleCalm(fighting);
            float rate = 1f / (target > CombatMix ? fadeToCombat : fadeToCalm);
            CombatMix = Mathf.MoveTowards(CombatMix, target, rate * Time.deltaTime);
            // equal-power crossfade, so the overall level holds steady through the switch
            float k = CombatMix * Mathf.PI * 0.5f;
            float calmLevel = calmVolume * Mathf.Cos(k);
            // (equal power, as the combat crossfade: the level holds steady through the change)
            float x = (cycleK < 0f ? 0f : Mathf.Clamp01(cycleK)) * Mathf.PI * 0.5f;
            calm.volume = calmLevel * Mathf.Cos(x);
            calmNext.volume = calmLevel * Mathf.Sin(x);
            combat.volume = combatVolume * Mathf.Sin(k);
            if (inStep)
            {
                // keep the two arrangements locked together
                if (Mathf.Abs(combat.timeSamples - calm.timeSamples) > 2048) combat.timeSamples = calm.timeSamples;
            }
            else if (CombatMix <= 0f && combat.isPlaying)
            {
                // the fight over: the next starts it afresh (or, back on the scene's own calm
                // track, it runs in step with it again)
                combat.Stop();
                combatFromTop = false;
                if (paired) { combat.timeSamples = calm.timeSamples; combat.Play(); }
            }
        }

        /// <summary>The next calm track as soon as it is calm (the self-test).</summary>
        public void ChangeTrackSoon() => nextCycle = Time.time;

        /// <summary>The calm music going round: when one is due (and it is calm), the next calm
        /// track fades in from the top over cycleFade seconds, and the old one stops.</summary>
        void CycleCalm(bool fighting)
        {
            if (calmClips == null || calmClips.Length < 2) return;
            if (cycleK >= 0f)
            {
                cycleK += Time.deltaTime / Mathf.Max(0.5f, cycleFade);
                if (cycleK < 1f) return;
                // done: the new one is the calm track now
                calm.Stop();
                (calm, calmNext) = (calmNext, calm);
                calmNext.volume = 0f;
                cycleK = -1f;
                Cycles++;
                nextCycle = Time.time + Random.Range(cycleMin, cycleMax);
                // (back on the scene's own calm track: the combat arrangement runs in step again,
                // unless one started from its top is still playing out a fight)
                if (synced && calm.clip == pairedCalm && !combat.isPlaying) { combat.timeSamples = calm.timeSamples; combat.Play(); combatFromTop = false; }
                return;
            }
            // (not in the middle of a fight, nor coming out of one: the next quiet spell)
            if (Time.time < nextCycle || fighting || CombatMix > 0f) return;
            calmIndex = (calmIndex + 1) % calmClips.Length;
            calmNext.clip = calmClips[calmIndex];
            calmNext.timeSamples = 0;
            calmNext.volume = 0f;
            calmNext.Play();
            cycleK = 0f;
            Debug.Log($"[Music] calm track: {calmNext.clip.name}");
        }
    }
}

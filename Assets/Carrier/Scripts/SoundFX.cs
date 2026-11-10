using System;
using UnityEngine;

namespace CarrierGame
{
    public enum Sfx { Cannon, PointDefense, Blaster, MissileLaunch, ExplosionBig, ExplosionSmall, Impact, Catapult, UiLock, UiClear, UiError, UiKill,
                      ShieldHit, BeamCharge, BeamFire, HeavyBeamCharge, HeavyBeamFire, UiAlert,
                      WarpCharge, WarpJump, WarpExit, SensorPing }

    /// <summary>
    /// Plays the game's sounds. Each sound uses the library recordings in
    /// Resources/CarrierAudio/&lt;Sfx name&gt;/ (several takes, picked at random so repeated shots
    /// don't sound identical). A sound with no recordings there is synthesised in code instead;
    /// the frigate's heavy bow beam is always synthesised. Sounds in the world are 3D; interface
    /// sounds are not. Busy sounds are rate limited so a big salvo doesn't turn into noise.
    /// Also starts the music (MusicDirector) and the engine hum and ambience loops.
    /// </summary>
    public class SoundFX : MonoBehaviour
    {
        class Def
        {
            public AudioClip[] clips;
            public bool library;
            public float volume, minDistance, minInterval;
            public int maxVoices;
            public bool ui;
            public float lastPlayed = -100f;
            public int lastClip = -1;
        }

        public const string LibraryRoot = "CarrierAudio/";

        static SoundFX instance;
        Def[] defs;
        AudioSource[] voices;
        int[] voiceSfx;
        int nextVoice;
        AudioClip engineClip;
        Transform listener;

        /// <summary>When a weapon or explosion was last heard within combat range (drives the music).</summary>
        public static float LastCombatTime { get; private set; } = -1000f;
        const float CombatHearingRange = 6000f;

        /// <summary>Which sounds come from the library and which are synthesised (for the logs).</summary>
        public static string SourceReport { get; private set; } = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { LastCombatTime = -1000f; SourceReport = ""; }

        /// <summary>How many effect voices are sounding right now (for the auto test log).</summary>
        public static int PlayingVoices
        {
            get
            {
                if (instance == null) return 0;
                int n = 0;
                foreach (var v in instance.voices) if (v != null && v.isPlaying) n++;
                return n;
            }
        }

        public static bool Muted
        {
            get => AudioListener.volume < 0.01f;
            set => AudioListener.volume = value ? 0f : 1f;
        }

        /// <summary>The level is starting again (Restart in the pause menu): stop the sounds still
        /// playing and forget the fighting, so the music starts calm.</summary>
        public static void LevelRestart()
        {
            LastCombatTime = -1000f;
            MusicDirector.ForgetCombat();
            if (instance == null || instance.voices == null) return;
            foreach (var v in instance.voices) if (v != null) v.Stop();
        }

        static SoundFX Instance
        {
            get
            {
                if (instance == null && Application.isPlaying) new GameObject("SoundFX").AddComponent<SoundFX>();
                return instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() => _ = Instance;

        void Awake()
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
            defs = new Def[Enum.GetValues(typeof(Sfx)).Length];
            var lib = new System.Collections.Generic.List<string>();
            var synth = new System.Collections.Generic.List<string>();
            // synthVol is used for the synthesised fallback, libVol for library recordings
            void D(Sfx s, Func<AudioClip> make, float synthVol, float libVol, float minDist, float interval, int max, bool ui = false)
            {
                var clips = LoadLibrary(s.ToString());
                bool fromLibrary = clips != null;
                (fromLibrary ? lib : synth).Add(fromLibrary ? $"{s} ({clips.Length})" : s.ToString());
                defs[(int)s] = new Def
                {
                    clips = fromLibrary ? clips : new[] { make() }, library = fromLibrary,
                    volume = fromLibrary ? libVol : synthVol, minDistance = minDist, minInterval = interval, maxVoices = max, ui = ui,
                };
            }
            D(Sfx.Cannon, SfxSynth.Cannon, 0.5f, 0.55f, 900f, 0.08f, 4);
            D(Sfx.PointDefense, SfxSynth.PointDefense, 0.18f, 0.16f, 600f, 0.06f, 3);
            D(Sfx.Blaster, SfxSynth.Blaster, 0.28f, 0.3f, 250f, 0.04f, 5);
            D(Sfx.MissileLaunch, SfxSynth.MissileLaunch, 0.4f, 0.45f, 900f, 0.12f, 4);
            D(Sfx.ExplosionBig, SfxSynth.ExplosionBig, 0.75f, 0.85f, 2500f, 0.2f, 3);
            D(Sfx.ExplosionSmall, SfxSynth.ExplosionSmall, 0.4f, 0.5f, 900f, 0.08f, 4);
            D(Sfx.Impact, SfxSynth.Impact, 0.25f, 0.3f, 700f, 0.08f, 3);
            D(Sfx.Catapult, SfxSynth.Catapult, 0.5f, 0.5f, 800f, 0.2f, 3);
            D(Sfx.UiLock, () => SfxSynth.Beeps("ui_lock", new[] { 880f, 1320f }, 0.08f, 0.04f), 0.35f, 0.4f, 0f, 0.05f, 2, true);
            D(Sfx.UiClear, () => SfxSynth.Beeps("ui_clear", new[] { 1175f, 700f }, 0.08f, 0.04f), 0.35f, 0.4f, 0f, 0.05f, 2, true);
            D(Sfx.UiError, SfxSynth.Buzz, 0.4f, 0.4f, 0f, 0.15f, 1, true);
            D(Sfx.UiKill, () => SfxSynth.Beeps("ui_kill", new[] { 660f, 880f, 1320f }, 0.09f, 0.02f), 0.5f, 0.5f, 0f, 0.3f, 1, true);
            D(Sfx.UiAlert, () => SfxSynth.Beeps("ui_alert", new[] { 990f, 740f, 990f, 740f }, 0.14f, 0.06f), 0.45f, 0.45f, 0f, 1f, 1, true);
            D(Sfx.WarpCharge, SfxSynth.BeamCharge, 0.5f, 0.6f, 2500f, 1f, 1);
            D(Sfx.WarpJump, SfxSynth.ExplosionSmall, 0.6f, 0.8f, 3000f, 0.5f, 1);
            D(Sfx.WarpExit, SfxSynth.ExplosionSmall, 0.6f, 0.75f, 3000f, 0.5f, 1);
            D(Sfx.ShieldHit, SfxSynth.ShieldHit, 0.35f, 0.4f, 900f, 0.07f, 4);
            D(Sfx.BeamCharge, SfxSynth.BeamCharge, 0.45f, 0.55f, 1500f, 0.5f, 2);
            D(Sfx.BeamFire, SfxSynth.BeamFire, 0.6f, 0.65f, 2500f, 0.5f, 2);
            // the frigate's bow beam: no library recording matches it, so it stays synthesised
            D(Sfx.HeavyBeamCharge, SfxSynth.HeavyBeamCharge, 0.7f, 0.7f, 1800f, 0.5f, 2);
            D(Sfx.HeavyBeamFire, SfxSynth.HeavyBeamFire, 0.85f, 0.85f, 3000f, 0.5f, 2);
            D(Sfx.SensorPing, SfxSynth.SensorPing, 0.45f, 0.45f, 0f, 0.5f, 1, true);

            var engineLib = LoadLibrary("EngineLoop");
            engineClip = engineLib != null ? engineLib[0] : SfxSynth.EngineLoop();
            var ambientLib = LoadLibrary("AmbientLoop");
            (engineLib != null ? lib : synth).Add("EngineLoop");
            (ambientLib != null ? lib : synth).Add("AmbientLoop");
            SourceReport = $"library: {string.Join(", ", lib)}\nsynthesised: {string.Join(", ", synth)}";
            Debug.Log("[SoundFX] " + SourceReport.Replace("\n", " | "));

            voices = new AudioSource[40];
            voiceSfx = new int[voices.Length];
            for (int i = 0; i < voices.Length; i++)
            {
                var go = new GameObject("Voice");
                go.transform.SetParent(transform, false);
                var a = go.AddComponent<AudioSource>();
                a.playOnAwake = false;
                a.dopplerLevel = 0f;
                a.rolloffMode = AudioRolloffMode.Logarithmic;
                a.maxDistance = 30000f;
                voices[i] = a;
                voiceSfx[i] = -1;
            }
            var ambient = gameObject.AddComponent<AudioSource>();
            ambient.clip = ambientLib != null ? ambientLib[0] : SfxSynth.AmbientLoop();
            ambient.loop = true;
            ambient.spatialBlend = 0f;
            ambient.volume = ambientLib != null ? 0.07f : 0.1f;
            ambient.Play();

            var l = FindAnyObjectByType<AudioListener>();
            if (l != null)
            {
                listener = l.transform;
                if (l.GetComponent<MasterLimiter>() == null) l.gameObject.AddComponent<MasterLimiter>();
            }
            gameObject.AddComponent<MusicDirector>();
        }

        /// <summary>The recordings in Resources/CarrierAudio/&lt;folder&gt;, in name order, or null if there are none.</summary>
        public static AudioClip[] LoadLibrary(string folder)
        {
            var clips = Resources.LoadAll<AudioClip>(LibraryRoot + folder);
            if (clips == null || clips.Length == 0) return null;
            Array.Sort(clips, (a, b) => string.CompareOrdinal(a.name, b.name));
            return clips;
        }

        /// <summary>Play a sound stretched (within reason) to last `duration` seconds, e.g. a
        /// weapon's charge-up. With alignEnd, a clip that is still too short starts late so that
        /// it finishes exactly when the charge does. Returns the voice playing it (or null), so the
        /// caller can stop it if the charge is called off.</summary>
        public static AudioSource PlayFitted(Sfx s, Vector3 position, float duration, bool alignEnd)
        {
            var sfx = Instance;
            if (sfx == null) return null;
            var d = sfx.defs[(int)s];
            if (d == null) return null;
            float len = d.clips[0].length;
            float pitch = len / Mathf.Max(0.05f, duration);
            if (d.library) pitch = Mathf.Clamp(pitch, 0.75f, 1.35f);   // recordings only bend so far
            float delay = alignEnd ? Mathf.Max(0f, duration - len / pitch) : 0f;
            return sfx.PlayInternal(s, position, 1f, pitch, false, delay);
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        /// <summary>Play a sound at a point in the world.</summary>
        public static void Play(Sfx s, Vector3 position, float volume = 1f, float pitch = 1f)
        {
            var sfx = Instance;
            if (sfx != null) sfx.PlayInternal(s, position, volume, pitch, true, 0f);
        }

        /// <summary>Play an interface sound (not positioned).</summary>
        public static void Ui(Sfx s) => Play(s, Vector3.zero);

        /// <summary>Play a sound that isn't part of a fight (a touchdown's thump), so the music
        /// doesn't take it for one.</summary>
        public static void PlayPeaceful(Sfx s, Vector3 position, float volume = 1f, float pitch = 1f)
        {
            var sfx = Instance;
            if (sfx != null) sfx.PlayInternal(s, position, volume, pitch, true, 0f, false);
        }

        AudioSource PlayInternal(Sfx s, Vector3 position, float volume, float pitch, bool vary, float delay, bool fight = true)
        {
            var d = defs[(int)s];
            if (d == null || d.clips == null || d.clips.Length == 0) return null;
            bool fightingSound = fight && !d.ui && s != Sfx.WarpCharge && s != Sfx.WarpJump && s != Sfx.WarpExit && s != Sfx.Catapult;
            if (fightingSound && (listener == null || (position - listener.position).sqrMagnitude < CombatHearingRange * CombatHearingRange))
                LastCombatTime = Time.time;
            float now = Time.unscaledTime;
            if (now - d.lastPlayed < d.minInterval) return null;
            int playing = 0;
            for (int i = 0; i < voices.Length; i++)
                if (voiceSfx[i] == (int)s && voices[i].isPlaying) playing++;
            if (playing >= d.maxVoices) return null;
            d.lastPlayed = now;

            int v = FreeVoice();
            var a = voices[v];
            voiceSfx[v] = (int)s;
            a.Stop();
            a.clip = d.clips[PickClip(d)];
            a.volume = Mathf.Clamp01(d.volume * volume);
            if (d.ui)
            {
                a.spatialBlend = 0f;
                a.pitch = pitch;
            }
            else
            {
                a.spatialBlend = 1f;
                a.minDistance = d.minDistance;
                // several takes already vary the sound; a little pitch spread on top
                float spread = d.clips.Length > 1 ? 0.04f : 0.07f;
                a.pitch = vary ? pitch * UnityEngine.Random.Range(1f - spread, 1f + spread) : pitch;
                a.transform.position = position;
            }
            if (delay > 0.01f) a.PlayDelayed(delay);
            else a.Play();
            return a;
        }

        /// <summary>A random take, never the same one twice running.</summary>
        static int PickClip(Def d)
        {
            int n = d.clips.Length;
            if (n == 1) return 0;
            int i;
            if (d.lastClip < 0) i = UnityEngine.Random.Range(0, n);
            else
            {
                i = UnityEngine.Random.Range(0, n - 1);   // any take but the last one
                if (i >= d.lastClip) i++;
            }
            d.lastClip = i;
            return i;
        }

        int FreeVoice()
        {
            for (int k = 0; k < voices.Length; k++)
            {
                int i = (nextVoice + k) % voices.Length;
                if (!voices[i].isPlaying)
                {
                    nextVoice = (i + 1) % voices.Length;
                    return i;
                }
            }
            int steal = nextVoice;
            nextVoice = (nextVoice + 1) % voices.Length;
            return steal;
        }

        /// <summary>A looping engine hum attached to a ship; the caller sets its volume and pitch.</summary>
        public static AudioSource EngineHum(Transform ship, float minDistance)
        {
            var sfx = Instance;
            if (sfx == null) return null;
            var a = ship.gameObject.AddComponent<AudioSource>();
            a.clip = sfx.engineClip;
            a.loop = true;
            a.spatialBlend = 1f;
            a.rolloffMode = AudioRolloffMode.Logarithmic;
            a.minDistance = minDistance;
            a.maxDistance = 30000f;
            a.dopplerLevel = 0f;
            a.volume = 0.4f;
            a.Play();
            return a;
        }
    }
}

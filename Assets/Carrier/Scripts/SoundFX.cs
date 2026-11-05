using System;
using UnityEngine;

namespace CarrierGame
{
    public enum Sfx { Cannon, PointDefense, Blaster, MissileLaunch, ExplosionBig, ExplosionSmall, Impact, Catapult, UiLock, UiClear, UiError, UiKill,
                      ShieldHit, BeamCharge, BeamFire, HeavyBeamCharge, HeavyBeamFire, UiAlert,
                      WarpCharge, WarpJump, WarpExit }

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

    /// <summary>Builds the sound effects from oscillators and filtered noise.</summary>
    static class SfxSynth
    {
        const int SR = 44100;
        const float Tau = Mathf.PI * 2f;
        static readonly System.Random rng = new System.Random(7);

        static float Noise() => (float)(rng.NextDouble() * 2.0 - 1.0);
        static float Chance() => (float)rng.NextDouble();
        static float Exp(float x) => Mathf.Exp(x);
        static float Sat(float x) => (float)Math.Tanh(x);

        sealed class LowPass
        {
            float y;
            public float Run(float x, float cutoff)
            {
                y += (1f - Mathf.Exp(-Tau * cutoff / SR)) * (x - y);
                return y;
            }
        }

        sealed class Osc
        {
            double phase;
            public float Sin(float freq)
            {
                phase += freq / (double)SR;
                phase -= Math.Floor(phase);
                return Mathf.Sin(Tau * (float)phase);
            }

            /// <summary>A sawtooth: rich in harmonics, so a low note still reads as a low note on small speakers.</summary>
            public float Saw(float freq)
            {
                phase += freq / (double)SR;
                phase -= Math.Floor(phase);
                return 2f * (float)phase - 1f;
            }
        }

        /// <summary>Render a clip sample by sample, normalise it, and (for loops) crossfade the seam.</summary>
        static AudioClip Make(string name, float seconds, Func<float, float> sample, bool loop = false)
        {
            int n = Mathf.CeilToInt(seconds * SR);
            int fade = loop ? SR / 2 : 0;
            var raw = new float[n + fade];
            for (int i = 0; i < raw.Length; i++) raw[i] = sample(i / (float)SR);
            var d = new float[n];
            Array.Copy(raw, d, n);
            if (loop)
            {
                for (int i = 0; i < fade; i++)
                {
                    float w = i / (float)fade;
                    d[i] = d[i] * w + raw[n + i] * (1f - w);
                }
            }
            else
            {
                int tail = Mathf.Min(n, SR / 100);
                for (int i = 0; i < tail; i++) d[n - 1 - i] *= i / (float)tail;
            }
            float peak = 1e-6f;
            foreach (float x in d) peak = Mathf.Max(peak, Mathf.Abs(x));
            float g = 0.9f / peak;
            for (int i = 0; i < n; i++) d[i] *= g;
            var clip = AudioClip.Create(name, n, 1, SR, false);
            clip.SetData(d, 0);
            return clip;
        }

        public static AudioClip Cannon()
        {
            var o = new Osc(); var a = new LowPass(); var b = new LowPass();
            return Make("cannon", 1.6f, t =>
            {
                float boom = o.Sin(60f + 180f * Exp(-t * 12f)) * Exp(-t * 4f);
                float cut = 350f + 4000f * Exp(-t * 9f);
                float body = b.Run(a.Run(Noise(), cut), cut) * Exp(-t * 3f) * 4f;
                float crack = Noise() * Exp(-t * 60f) * 0.5f;
                return Sat(1.6f * (boom + body + crack));
            });
        }

        /// <summary>An energy shield taking a hit: a bright zap falling into a hollow, ringing thrum.</summary>
        public static AudioClip ShieldHit()
        {
            var o = new Osc(); var o2 = new Osc(); var a = new LowPass(); var h = new LowPass();
            return Make("shield_hit", 0.7f, t =>
            {
                float f = 260f + 1400f * Exp(-t * 14f);
                float ring = o.Sin(f) * (0.6f + 0.4f * o2.Sin(37f)) * Exp(-t * 5f);
                float x = Noise();
                float fizz = (x - h.Run(x, 1800f)) * Exp(-t * 30f) * 0.8f;
                float body = a.Run(Noise(), 700f) * Exp(-t * 9f) * 2.5f;
                return Sat(1.4f * (ring + fizz + body));
            });
        }

        /// <summary>A beam weapon charging: a whine climbing for 10 s, with crackle building up.</summary>
        public static AudioClip BeamCharge()
        {
            var o = new Osc(); var o2 = new Osc(); var o3 = new Osc(); var h = new LowPass();
            return Make("beam_charge", 10f, t =>
            {
                float k = Mathf.Clamp01(t / 10f);
                float f = 90f * Mathf.Pow(14f, k);                       // 90 Hz up to ~1.3 kHz
                float tremolo = 0.75f + 0.25f * Mathf.Sin(Tau * (3f + 22f * k * k) * t);
                float tone = (o.Sin(f) * 0.7f + o2.Sin(f * 1.5f) * 0.3f + o3.Sin(f * 0.5f) * 0.4f) * tremolo;
                float x = Noise();
                float crackle = (x - h.Run(x, 3000f)) * (Noise() > 0.97f - 0.08f * k ? 1f : 0.1f) * k;
                float env = Mathf.Clamp01(t / 0.4f) * (0.25f + 0.75f * k);
                return Sat(1.3f * (tone * env + crackle * 0.5f));
            });
        }

        /// <summary>A beam firing: a deep roaring hum with a hiss on top, for 2.5 s.</summary>
        public static AudioClip BeamFire()
        {
            var o = new Osc(); var o2 = new Osc(); var a = new LowPass(); var b = new LowPass(); var h = new LowPass();
            return Make("beam_fire", 2.6f, t =>
            {
                float env = Mathf.Clamp01(t / 0.05f) * Mathf.Clamp01((2.6f - t) / 0.4f);
                float hum = o.Sin(70f + 6f * Mathf.Sin(Tau * 4f * t)) * 0.8f + o2.Sin(141f) * 0.4f;
                float roar = b.Run(a.Run(Noise(), 2200f), 2200f) * 4f;
                float x = Noise();
                float hiss = (x - h.Run(x, 4000f)) * 0.35f;
                float blast = Noise() * Exp(-t * 12f);
                return Sat(1.5f * (hum + roar + hiss + blast) * env);
            });
        }

        /// <summary>The frigate's heavy bow gun charging: a dark, detuned growl climbing from a
        /// 28 Hz rumble to about 140 Hz over 10 s, its tone opening up and its throb speeding
        /// up as it nears full charge, with low crackle building underneath.</summary>
        public static AudioClip HeavyBeamCharge()
        {
            var s1 = new Osc(); var s2 = new Osc(); var sub = new Osc();
            var tone1 = new LowPass(); var tone2 = new LowPass(); var rum1 = new LowPass(); var rum2 = new LowPass();
            var crk = new LowPass();
            return Make("heavy_beam_charge", 10f, t =>
            {
                float k = Mathf.Clamp01(t / 10f);
                float f = 28f * Mathf.Pow(5f, k);                         // 28 Hz up to 140 Hz
                float saw = s1.Saw(f) + s2.Saw(f * 1.007f);                // slow beating between the two
                float cut = 160f + 1100f * k * k;                         // dark at first, opening up
                float growl = tone2.Run(tone1.Run(saw, cut), cut) * 1.6f;
                float subTone = sub.Sin(f * 0.5f) * 0.9f;
                float throb = 0.65f + 0.35f * Mathf.Sin(Tau * (1.2f + 7f * k * k) * t);
                float rumble = rum2.Run(rum1.Run(Noise(), 110f), 110f) * (0.4f + 1.4f * k) * 6f;
                float crackle = crk.Run(Noise(), 1200f) * (Noise() > 0.975f - 0.07f * k ? 3f : 0.2f) * k;
                float swell = 1f + 0.6f * Mathf.Clamp01((t - 9f) / 1f);  // last-second surge
                float env = Mathf.Clamp01(t / 0.6f) * (0.35f + 0.65f * k) * swell;
                return Sat(1.5f * ((growl + subTone) * throb * env + rumble * env + crackle * 0.5f));
            });
        }

        /// <summary>The frigate's heavy bow gun firing: a deep thump dropping to 32 Hz, then a
        /// low, fluttering roar on a 38 Hz hum for 2.5 s, with only a little hiss on top.</summary>
        public static AudioClip HeavyBeamFire()
        {
            var thump = new Osc(); var hum = new Osc(); var hum2 = new Osc(); var g = new Osc();
            var gl = new LowPass(); var r1 = new LowPass(); var r2 = new LowPass(); var bl = new LowPass(); var h = new LowPass();
            return Make("heavy_beam_fire", 2.6f, t =>
            {
                float env = Mathf.Clamp01(t / 0.03f) * Mathf.Clamp01((2.6f - t) / 0.5f);
                float boom = thump.Sin(32f + 110f * Exp(-t * 9f)) * Exp(-t * 4f) * 1.6f;
                float low = hum.Sin(38f + 1.5f * Mathf.Sin(Tau * 3f * t)) * 0.9f + hum2.Sin(57f) * 0.35f;
                float flutter = 0.7f + 0.3f * Mathf.Sin(Tau * 11f * t);
                float growl = gl.Run(g.Saw(76f + 2f * Mathf.Sin(Tau * 5f * t)), 650f) * 1.4f * flutter;
                float roar = r2.Run(r1.Run(Noise(), 480f), 480f) * 6f;
                float blast = bl.Run(Noise(), 1400f) * Exp(-t * 7f) * 1.5f;
                float x = Noise();
                float hiss = (x - h.Run(x, 3500f)) * 0.08f;
                return Sat(1.5f * (boom + (low + growl + roar + hiss) * env + blast));
            });
        }

        public static AudioClip PointDefense()
        {
            var o = new Osc(); var a = new LowPass(); var h = new LowPass();
            return Make("pd_gun", 0.22f, t =>
            {
                float e = Exp(-t * 25f);
                float tone = o.Sin(220f + 700f * Exp(-t * 45f)) * e;
                float x = Noise();
                float hiss = (x - h.Run(x, 2000f)) * Exp(-t * 70f);
                float thud = a.Run(Noise(), 900f) * e * 3f;
                return Sat(1.6f * (tone * 0.7f + hiss * 0.6f + thud));
            });
        }

        public static AudioClip Blaster()
        {
            var o = new Osc(); var o2 = new Osc();
            return Make("blaster", 0.28f, t =>
            {
                float f = 320f + 1700f * Exp(-t * 15f);
                float e = Exp(-t * 10f) * Mathf.Clamp01(t / 0.003f);
                return Sat(2.5f * o.Sin(f)) * e * 0.8f + o2.Sin(f * 0.5f) * e * 0.4f;
            });
        }

        public static AudioClip MissileLaunch()
        {
            var o = new Osc(); var a = new LowPass(); var b = new LowPass();
            return Make("missile", 2.2f, t =>
            {
                float env = Mathf.Clamp01(t / 0.05f) * Exp(-t * 1f);
                float cut = 600f + 4500f * Mathf.Clamp01(t / 0.3f) * Exp(-t * 0.9f);
                float roar = b.Run(a.Run(Noise(), cut), cut) * 4f * env * (0.85f + 0.15f * Mathf.Sin(Tau * 27f * t));
                float thump = o.Sin(80f + 120f * Exp(-t * 18f)) * Exp(-t * 10f);
                return Sat(1.5f * (roar + thump));
            });
        }

        public static AudioClip ExplosionBig()
        {
            var o = new Osc(); var a = new LowPass(); var b = new LowPass();
            float spark = 0f;
            return Make("explosion_big", 4f, t =>
            {
                float env = Mathf.Clamp01(t / 0.006f) * (0.6f * Exp(-t * 0.9f) + 0.4f * Exp(-t * 4f));
                float cut = 220f + 2600f * Exp(-t * 2f);
                float rumble = b.Run(a.Run(Noise(), cut), cut) * 5f * env;
                if (Chance() < 0.0018f * Exp(-t * 1.3f)) spark = 1f;
                spark *= 0.995f;
                float crackle = Noise() * spark * 0.5f;
                float sub = o.Sin(48f + 50f * Exp(-t * 3f)) * Exp(-t * 1.6f) * 0.7f;
                return Sat(1.7f * (rumble + crackle + sub));
            });
        }

        public static AudioClip ExplosionSmall()
        {
            var o = new Osc(); var a = new LowPass(); var b = new LowPass();
            float spark = 0f;
            return Make("explosion_small", 1.4f, t =>
            {
                float env = Mathf.Clamp01(t / 0.004f) * (0.5f * Exp(-t * 2.5f) + 0.5f * Exp(-t * 8f));
                float cut = 400f + 4000f * Exp(-t * 4f);
                float rumble = b.Run(a.Run(Noise(), cut), cut) * 5f * env;
                if (Chance() < 0.003f * Exp(-t * 3f)) spark = 1f;
                spark *= 0.993f;
                float crackle = Noise() * spark * 0.4f;
                float sub = o.Sin(80f + 80f * Exp(-t * 6f)) * Exp(-t * 5f) * 0.5f;
                return Sat(1.6f * (rumble + crackle + sub));
            });
        }

        public static AudioClip Impact()
        {
            float[] f = { 520f, 1240f, 1990f, 2870f, 3940f };
            float[] k = { 7f, 10f, 14f, 19f, 26f };
            float[] amp = { 1f, 0.7f, 0.5f, 0.35f, 0.25f };
            var os = new Osc[f.Length];
            for (int i = 0; i < os.Length; i++) os[i] = new Osc();
            var h = new LowPass();
            return Make("impact", 0.7f, t =>
            {
                float s = 0f;
                for (int i = 0; i < f.Length; i++) s += os[i].Sin(f[i]) * amp[i] * Exp(-t * k[i]);
                float x = Noise();
                float click = (x - h.Run(x, 3000f)) * Exp(-t * 120f);
                return Sat(1.2f * (s * 0.5f + click));
            });
        }

        public static AudioClip Catapult()
        {
            var o = new Osc(); var o2 = new Osc(); var a = new LowPass(); var b = new LowPass();
            return Make("catapult", 1.8f, t =>
            {
                float clunk = o.Sin(90f + 60f * Exp(-t * 30f)) * Exp(-t * 22f) * 1.2f;
                float rise = Mathf.Clamp01(t / 1.1f);
                float env = Mathf.Clamp01((t - 0.03f) / 0.25f) * Exp(-Mathf.Max(0f, t - 0.9f) * 4f);
                float cut = 400f + 3000f * rise;
                float x = a.Run(Noise(), cut);
                float band = x - b.Run(x, cut * 0.35f);
                float whine = o2.Sin(180f + 700f * rise) * 0.18f * env;
                return Sat(1.4f * (clunk + band * 4f * env + whine));
            });
        }

        public static AudioClip EngineLoop()
        {
            var a = new LowPass(); var b = new LowPass();
            return Make("engine", 4f, t =>
            {
                float am = 0.85f + 0.15f * Mathf.Sin(Tau * 0.5f * t);
                float tone = 0.5f * Mathf.Sin(Tau * 55f * t) + 0.35f * Mathf.Sin(Tau * 82.5f * t)
                           + 0.25f * Mathf.Sin(Tau * 110f * t) + 0.12f * Mathf.Sin(Tau * 165.5f * t);
                float rush = b.Run(a.Run(Noise(), 500f), 500f) * 6f;
                return Sat((tone * am + rush) * 0.8f);
            }, loop: true);
        }

        public static AudioClip AmbientLoop()
        {
            var a = new LowPass();
            return Make("ambient", 12f, t =>
            {
                float s = 0.5f * Mathf.Sin(Tau * 55f * t) * (0.6f + 0.4f * Mathf.Sin(Tau * 0.05f * t))
                        + 0.35f * Mathf.Sin(Tau * 82.4f * t) * (0.6f + 0.4f * Mathf.Sin(Tau * 0.07f * t + 1f))
                        + 0.25f * Mathf.Sin(Tau * 110f * t) * (0.5f + 0.5f * Mathf.Sin(Tau * 0.04f * t + 2f))
                        + 0.15f * Mathf.Sin(Tau * 164.8f * t) * (0.5f + 0.5f * Mathf.Sin(Tau * 0.09f * t + 3f));
                float hiss = a.Run(Noise(), 1200f) * 0.6f * (0.5f + 0.5f * Mathf.Sin(Tau * 0.03f * t));
                return (s + hiss) * 0.6f;
            }, loop: true);
        }

        public static AudioClip Beeps(string name, float[] freqs, float beepLen, float gap)
        {
            var o = new Osc();
            float step = beepLen + gap;
            return Make(name, freqs.Length * step, t =>
            {
                int i = Mathf.Min(freqs.Length - 1, (int)(t / step));
                float lt = t - i * step;
                if (lt > beepLen) return 0f;
                float e = Mathf.Clamp01(lt / 0.004f) * Mathf.Clamp01((beepLen - lt) / 0.02f);
                return Sat(2f * o.Sin(freqs[i])) * e;
            });
        }

        public static AudioClip Buzz()
        {
            var o = new Osc();
            return Make("ui_error", 0.3f, t =>
            {
                float e = (t < 0.12f || (t > 0.16f && t < 0.28f)) ? 1f : 0f;
                return Sat(4f * o.Sin(140f)) * e * 0.8f;
            });
        }
    }
}

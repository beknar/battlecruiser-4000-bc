using System;
using UnityEngine;

namespace CarrierGame
{
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

        /// <summary>The mineral sensor's pulse: a sonar ping with a fading echo.</summary>
        public static AudioClip SensorPing()
        {
            var o = new Osc(); var o2 = new Osc();
            return Make("sensor_ping", 1.6f, t =>
            {
                float ping = o.Sin(1250f) * Exp(-t * 7f) * Mathf.Clamp01(t / 0.004f);
                float te = t - 0.45f;
                float echo = te > 0f ? o2.Sin(1250f) * Exp(-te * 9f) * 0.35f * Mathf.Clamp01(te / 0.004f) : 0f;
                return Sat(1.6f * (ping + echo));
            });
        }

        /// <summary>The mining laser cutting rock: a low electric hum with a gritty crackle (a loop).</summary>
        public static AudioClip MiningLoop()
        {
            var o = new Osc(); var o2 = new Osc(); var o3 = new Osc(); var a = new LowPass(); var h = new LowPass();
            return Make("mining_loop", 3f, t =>
            {
                float hum = o.Saw(98f) * 0.35f + o2.Sin(196f) * 0.3f + o3.Sin(293f) * 0.12f;
                float wobble = 0.8f + 0.2f * Mathf.Sin(Tau * 4f * t);
                float x = Noise();
                float grit = (x - h.Run(x, 2500f)) * (Chance() < 0.02f ? 1.6f : 0.25f);
                float rumble = a.Run(Noise(), 250f) * 3f;
                return Sat(0.9f * (hum * wobble + grit + rumble));
            }, loop: true);
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

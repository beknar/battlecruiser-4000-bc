using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// Co-op: another player's ship in this game. A copy of the local ship with its controls and
    /// weapons taken out, flown by that player's updates (smoothed: shown a little in the past,
    /// between the last two updates). It can be hit (the host's enemies go for it; the hits go to
    /// its player) and it shows that player's shots and beam. It isn't shown while its player is
    /// in warp, down on a planet, or destroyed.
    /// </summary>
    public class RemotePlayer : MonoBehaviour, IVelocity
    {
        public int Id { get; private set; }
        public string Name { get; private set; } = "";
        public float Shield { get; private set; } = 1f;
        public float Hull { get; private set; } = 1f;
        public bool Dead { get; private set; }
        public int Warp { get; private set; }
        public string Surface { get; private set; } = "";
        /// <summary>Not in this space now (in warp, on a planet, destroyed, or not heard from).</summary>
        public bool Hidden { get; private set; } = true;
        public Vector3 Velocity { get; private set; }
        public float LastHeard { get; private set; } = -100f;
        public int Updates { get; private set; }

        struct Sample { public double ts; public Vector3 p; public Quaternion q; public Vector3 v; }
        readonly List<Sample> samples = new List<Sample>();
        Renderer[] renderers;
        Collider[] colliders;
        Health health;
        bool shown = true;

        /// <summary>How far in the past the ship is shown, so there are two updates to go between.</summary>
        public const double Delay = 0.15;

        public void Init(int id, string name)
        {
            Id = id;
            Name = name;
            renderers = GetComponentsInChildren<Renderer>(true);
            colliders = GetComponentsInChildren<Collider>(true);
            health = GetComponent<Health>();
            if (health != null) { health.cannotDie = true; health.respawnDelay = 0f; }
            Show(false);
        }

        public void Rename(string name) => Name = name;

        public void Push(CoopMsg m)
        {
            LastHeard = Time.unscaledTime;
            Updates++;
            bool wasDead = Dead;
            Shield = m.sh; Hull = m.hu; Dead = m.dead;
            Warp = m.warp; Surface = m.surf ?? "";
            // it blew up: an explosion where it was
            if (Dead && !wasDead && health != null && health.deathEffect != null && shown)
            {
                var e = Instantiate(health.deathEffect, transform.position, Quaternion.identity);
                e.transform.localScale *= health.deathEffectScale;
            }
            var s = new Sample { ts = m.ts, p = new Vector3(m.x, m.y, m.z), q = new Quaternion(m.qx, m.qy, m.qz, m.qw), v = new Vector3(m.vx, m.vy, m.vz) };
            // a ship that wasn't here jumps to where it is, rather than sliding from where it was
            if (Hidden) samples.Clear();
            if (samples.Count > 0 && s.ts <= samples[samples.Count - 1].ts) return;
            samples.Add(s);
            if (samples.Count > 30) samples.RemoveAt(0);
        }

        void Show(bool on)
        {
            if (shown == on) return;
            shown = on;
            if (renderers != null) foreach (var r in renderers) if (r != null) r.enabled = on;
            if (colliders != null) foreach (var c in colliders) if (c != null) c.enabled = on;
        }

        /// <summary>Put it where its player's updates say it was, a moment ago.</summary>
        public void Place(CoopWorld world, double serverNow)
        {
            bool quiet = Time.unscaledTime - LastHeard > 3f;
            Hidden = Dead || Warp == 2 || Surface.Length > 0 || quiet || samples.Count == 0;
            Show(!Hidden);
            if (samples.Count == 0) return;
            double t = serverNow - Delay;
            Sample a = samples[0], b = samples[samples.Count - 1];
            for (int i = samples.Count - 1; i > 0; i--)
                if (samples[i - 1].ts <= t) { a = samples[i - 1]; b = samples[i]; break; }
            Vector3 p;
            Quaternion q;
            if (t >= b.ts)
            {
                // past the last update: carry on as it was going, briefly
                float ahead = (float)System.Math.Min(0.5, t - b.ts);
                p = b.p + b.v * ahead;
                q = b.q;
                Velocity = b.v;
            }
            else
            {
                float k = b.ts > a.ts ? Mathf.Clamp01((float)((t - a.ts) / (b.ts - a.ts))) : 1f;
                p = Vector3.Lerp(a.p, b.p, k);
                q = Quaternion.Slerp(a.q, b.q, k);
                Velocity = Vector3.Lerp(a.v, b.v, k);
            }
            transform.SetPositionAndRotation(world.FromNet(p), q);
        }
    }
}

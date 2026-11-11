using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// Particle effects for things in the water (Seafaring's splashes, wake and bubbles, the
    /// torpedoes' bubbles and wake): world-space systems emitted by hand, with soft white particles
    /// that fade in and out. "Flat" ones lie on the water (horizontal billboards), for foam.
    /// </summary>
    public static class WaterFx
    {
        public static ParticleSystem Make(string name, Material mat, int max, Color color, float lifeMin, float lifeMax,
                                          float sizeMin, float sizeMax, float gravity, bool flat = false, float grow = 1f)
        {
            var go = new GameObject(name);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main;
            m.loop = true;
            m.playOnAwake = false;
            m.maxParticles = max;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            m.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m.startColor = color;
            m.gravityModifier = gravity;
            var em = ps.emission;
            em.rateOverTime = 0f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.7f, 0.55f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            if (!Mathf.Approximately(grow, 1f))
            {
                var sz = ps.sizeOverLifetime;
                sz.enabled = true;
                sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, grow));
            }
            // (foam spreading on the water slows as it goes)
            var lim = ps.limitVelocityOverLifetime;
            lim.enabled = flat;
            lim.limit = 10000f;
            lim.drag = 0.5f;
            lim.multiplyDragByParticleSize = false;
            lim.multiplyDragByParticleVelocity = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            if (mat != null) r.sharedMaterial = mat;
            if (flat) r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        static ParticleSystem.EmitParams p;

        /// <summary>One particle, just so.</summary>
        public static void Emit(ParticleSystem ps, Vector3 at, Vector3 velocity, float size, float life, float alpha = 1f)
        {
            if (ps == null) return;
            p.position = at;
            p.velocity = velocity;
            p.startSize = size;
            p.startLifetime = life;
            var c = ps.main.startColor.color;
            c.a *= alpha;
            p.startColor = c;
            p.rotation = Random.Range(0f, 360f);
            ps.Emit(p, 1);
        }

        /// <summary>A system whose emitter is going (a torpedo gone off): it stops emitting and is
        /// left where it is until its last particles have faded.</summary>
        public static void Leave(ParticleSystem ps)
        {
            if (ps == null) return;
            ps.transform.SetParent(null, true);
            ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            Object.Destroy(ps.gameObject, ps.main.startLifetime.constantMax + 0.5f);
        }
    }
}

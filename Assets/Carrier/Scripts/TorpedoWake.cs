using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// A torpedo's run through the water: a stream of bubbles from its tail and, running shallow,
    /// a line of foam on the surface over it (fainter the deeper it is). When it goes off
    /// (Missile.Detonate) both are left behind to fade, with a last burst of bubbles.
    /// </summary>
    public class TorpedoWake : MonoBehaviour
    {
        [Tooltip("Soft round particle (alpha-blended).")]
        public Material material;
        [Tooltip("Where the bubbles come from: the tail.")]
        public Transform tail;
        [Tooltip("Bubbles a metre run.")]
        public float bubblesPerMetre = 1.2f;
        [Tooltip("Deeper than this the surface shows nothing (m).")]
        public float showsFrom = 28f;

        ParticleSystem bubbles, foam;
        /// <summary>Bubbles in its trail now (for the self-test).</summary>
        public int Bubbles => bubbles != null ? bubbles.particleCount : 0;
        Vector3 last;
        float bubbleDue, foamDue;
        bool left;

        void Start()
        {
            bubbles = WaterFx.Make("Torpedo Bubbles", material, 900, new Color(0.82f, 0.95f, 1f, 0.75f), 2.2f, 4f, 1.2f, 3.5f, -0.08f, grow: 2f);
            foam = WaterFx.Make("Torpedo Wake", material, 600, new Color(0.94f, 0.98f, 1f, 0.7f), 5f, 7f, 3f, 6f, 0f, flat: true, grow: 3f);
            last = Tail;
        }

        Vector3 Tail => tail != null ? tail.position : transform.position - transform.forward * 5f;

        void LateUpdate()
        {
            if (bubbles == null || left) return;
            Vector3 now = Tail;
            Vector3 step = now - last;
            float run = step.magnitude;
            if (run > 200f) { last = now; return; }   // (moved by the origin shift, not by running)
            // bubbles all along the stretch run this frame, drifting up
            bubbleDue += run * bubblesPerMetre;
            while (bubbleDue >= 1f)
            {
                bubbleDue -= 1f;
                Vector3 p = Vector3.Lerp(last, now, Random.value) + Random.insideUnitSphere * 0.8f;
                if (Sea.Here && p.y > Sea.Level - 0.3f) continue;
                WaterFx.Emit(bubbles, p, Random.insideUnitSphere * 1.2f + Vector3.up * Random.Range(0.5f, 2.5f), Random.Range(1.2f, 3.5f), Random.Range(2.2f, 4f));
            }
            // and the line it draws on the surface, running shallow
            if (Sea.Here)
            {
                float depth = Sea.Level - now.y;
                float show = 1f - Mathf.InverseLerp(4f, showsFrom, depth);
                foamDue += show > 0f ? run / 2.5f : 0f;
                while (foamDue >= 1f)
                {
                    foamDue -= 1f;
                    Vector3 p = Vector3.Lerp(last, now, Random.value);
                    p.y = Sea.Level + 0.3f;
                    Vector3 side = Vector3.Cross(Vector3.up, step.normalized) * Random.Range(-1.5f, 1.5f);
                    WaterFx.Emit(foam, p + side, side * 0.8f, Random.Range(3f, 6f), Random.Range(5f, 7f), show);
                }
            }
            last = now;
        }

        /// <summary>It has gone off: a burst of bubbles where it was, and its trail left to fade.</summary>
        public void Leave()
        {
            if (left) return;
            left = true;
            if (bubbles != null)
            {
                Vector3 at = transform.position;
                for (int i = 0; i < 90; i++)
                    WaterFx.Emit(bubbles, at + Random.insideUnitSphere * 3f, Random.insideUnitSphere * 9f + Vector3.up * Random.Range(2f, 8f), Random.Range(1.2f, 4f), Random.Range(2f, 4f));
            }
            WaterFx.Leave(bubbles);
            WaterFx.Leave(foam);
            bubbles = foam = null;
        }

        void OnDestroy()
        {
            // (gone some other way, with the level: its effects go too)
            if (bubbles != null) Destroy(bubbles.gameObject);
            if (foam != null) Destroy(foam.gameObject);
        }
    }
}

using UnityEngine;

namespace CarrierGame
{
    /// <summary>A flash that grows and fades: an emissive sphere plus a point light.</summary>
    public class Explosion : MonoBehaviour
    {
        public float duration = 1.2f;
        public float startSize = 2f;
        public float endSize = 30f;
        public Renderer flash;
        public Light glow;

        float age;
        Color baseColor;
        float baseIntensity;
        MaterialPropertyBlock block;
        static readonly int ColorId = Shader.PropertyToID("_BaseColor");

        void Start()
        {
            block = new MaterialPropertyBlock();
            if (flash != null) baseColor = flash.sharedMaterial.GetColor(ColorId);
            if (glow != null) baseIntensity = glow.intensity;
            Apply(0f);
            // big blasts rumble, small ones pop; scaled-up blasts (a cruiser dying) sound deeper
            float scale = transform.localScale.x;
            bool big = endSize * scale >= 50f;
            SoundFX.Play(big ? Sfx.ExplosionBig : Sfx.ExplosionSmall, transform.position, 1f,
                         Mathf.Clamp(1.1f - 0.08f * scale, 0.65f, 1.1f));
        }

        void Update()
        {
            age += Time.deltaTime;
            float t = Mathf.Clamp01(age / duration);
            Apply(t);
            if (age >= duration) Destroy(gameObject);
        }

        void Apply(float t)
        {
            float s = Mathf.Lerp(startSize, endSize, 1f - (1f - t) * (1f - t)) * transform.localScale.x;
            if (flash != null)
            {
                flash.transform.localScale = Vector3.one * Mathf.Lerp(startSize, endSize, 1f - (1f - t) * (1f - t));
                block.SetColor(ColorId, baseColor * (1f - t));
                flash.SetPropertyBlock(block);
            }
            if (glow != null)
            {
                glow.intensity = baseIntensity * (1f - t);
                glow.range = s * 3f;
            }
        }
    }
}

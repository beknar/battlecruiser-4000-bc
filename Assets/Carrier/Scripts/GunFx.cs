using UnityEngine;

namespace CarrierGame
{
    /// <summary>A gun's muzzle flash or a round's impact, made of particle systems (the game's own
    /// copies of an Asset Store pack's effects, made by CarrierBuilder.GunFx in the git-ignored
    /// Kit: a clone without the pack has none, and its guns show the bolts' own glow and the usual
    /// small explosion instead). Effects come from the Pool and go back to it once played; a muzzle
    /// flash follows its muzzle while it plays, as the ship moves on. Its lights, if it has any,
    /// flash and fade at once.</summary>
    public class GunFx : MonoBehaviour
    {
        [Tooltip("Seconds until it goes back to the pool (its longest particle's life).")]
        public float life = 1.5f;
        [Tooltip("Seconds its lights take to fade.")]
        public float lightFade = 0.08f;

        /// <summary>Farther than this from the camera an effect isn't played (metres).</summary>
        public const float MaxDistance = 6000f;

        /// <summary>How many have been played (for the self-test).</summary>
        public static int Played;

        ParticleSystem[] roots;
        Light[] lights;
        float[] intensity;
        Transform follow;
        float age;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Played = 0;

        void Awake()
        {
            // (the systems to start: those not under another, which start their children)
            roots = System.Array.FindAll(GetComponentsInChildren<ParticleSystem>(true),
                p => p.transform.parent == null || p.transform.parent.GetComponentInParent<ParticleSystem>(true) == null);
            lights = GetComponentsInChildren<Light>(true);
            intensity = new float[lights.Length];
            for (int i = 0; i < lights.Length; i++) intensity[i] = lights[i].intensity;
        }

        void OnEnable()
        {
            age = 0f;
            Played++;
            foreach (var p in roots) { p.Clear(true); p.Play(true); }
            for (int i = 0; i < lights.Length; i++) { lights[i].intensity = intensity[i]; lights[i].enabled = true; }
        }

        void OnDisable() => follow = null;

        /// <summary>Play `prefab` at a place, `scale` times its own size, following `with` (if it is
        /// given) while it plays. Nothing (null) if there is no prefab.</summary>
        public static GunFx Play(GunFx prefab, Vector3 position, Quaternion rotation, float scale = 1f, Transform with = null)
        {
            // (none where nobody could see it: a game without graphics, the battle simulator or the
            // AI wingman's, or too far from the camera to be more than a speck)
            if (prefab == null || Application.isBatchMode) return null;
            var cam = Camera.main;
            if (cam != null && (cam.transform.position - position).sqrMagnitude > MaxDistance * MaxDistance) return null;
            var fx = Pool.Take(prefab, position, rotation);
            if (scale != 1f) fx.transform.localScale *= scale;
            fx.follow = with;
            return fx;
        }

        /// <summary>The flash of a round fired from a muzzle, if the round has one (following `with`
        /// while it plays, if it is given); none under the water, where the round doesn't glow
        /// either.</summary>
        public static void Muzzle(Projectile round, Vector3 position, Quaternion rotation, Transform with = null)
        {
            if (round == null || round.muzzleFx == null || Sea.Depth(position) > 0.5f) return;
            Play(round.muzzleFx, position, rotation, round.fxScale, with);
        }

        void LateUpdate()
        {
            if (follow != null) transform.SetPositionAndRotation(follow.position, follow.rotation);
            age += Time.deltaTime;
            float k = lightFade > 0f ? Mathf.Clamp01(1f - age / lightFade) : 0f;
            for (int i = 0; i < lights.Length; i++)
            {
                lights[i].intensity = intensity[i] * k;
                if (k <= 0f) lights[i].enabled = false;
            }
            if (age >= life) Pool.Give(gameObject);
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// A cloak (the Grey scout's): while it is on, the ship's signature is kept to `hiddenScale`
    /// of its own (Signature.hiddenScale), so the players' sensors find it only close in: a ship
    /// of signature 1 cloaked to a tenth is seen inside 3 km of the 30, sooner by a frigate with
    /// good ECCM (Sensors). Firing, or a hit, lights the ship up (its Signature's lightUp: seen
    /// as any ship), and the cloak takes `recloakTime` more to come back once that is over.
    ///
    /// Unseen, a cloaked ship isn't drawn at all: every renderer under it is kept from rendering
    /// (Renderer.forceRenderingOff, apart from what other systems switch on and off: its shield
    /// bubble, flares, plume, plates), and its lights are off. Seen, it shimmers. A destroyed ship
    /// is left to its breakup. In co-op the host's game says whether it is up (NetSet), while the
    /// ship is a puppet; a game that takes over as host runs its own again.
    /// </summary>
    [RequireComponent(typeof(Signature))]
    public class Cloak : MonoBehaviour
    {
        [Tooltip("The share of its signature it keeps cloaked.")]
        public float hiddenScale = 0.1f;
        [Tooltip("Once it is no longer lit up by firing or a hit, how long (s) the cloak takes to come back.")]
        public float recloakTime = 4f;
        [Tooltip("Cloaked from the start (its tactics turn it off and on).")]
        public bool engaged = true;

        Signature sig;
        Health health;
        readonly List<Renderer> renderers = new List<Renderer>();
        readonly List<Light> lights = new List<Light>();
        readonly List<Light> lightsOff = new List<Light>();
        int childCount = -1;
        float nextLook;
        float downUntil;
        bool netUp;

        /// <summary>The cloak is up now (engaged, and not dropped by firing or a hit).</summary>
        public bool Up { get; private set; }

        /// <summary>Not drawn now (unseen while cloaked).</summary>
        public bool Hidden { get; private set; }

        /// <summary>Co-op: a puppet of the host's (its up and down are the host's to say).</summary>
        public bool NetDriven => TryGetComponent(out Demo.NetPuppet _);

        void Awake()
        {
            sig = GetComponent<Signature>();
            health = GetComponent<Health>();
        }

        void OnDisable() { Draw(true, 1f); if (sig != null) sig.hiddenScale = 1f; }

        /// <summary>Turn it on (its tactics: slipping away) or off (for good, till turned on again).</summary>
        public void Engage(bool on) => engaged = on;

        /// <summary>Co-op: the host's game says whether it is up (while this is its puppet).</summary>
        public void NetSet(bool up) => netUp = up;

        void Update()
        {
            bool puppet = NetDriven;
            if (!puppet && sig != null && sig.Lit)
                // fired or hit (lit up for its signature's lightUp): down, and back a while after
                downUntil = Time.time + recloakTime;
            Up = puppet ? netUp : engaged && Time.time >= downUntil;
            if (sig != null) sig.hiddenScale = Up ? hiddenScale : 1f;
            // a destroyed ship: its breakup's to show; the cloak's hiding off
            if (health != null && health.Dead) { Draw(true, 1f); return; }
            // drawn when seen (shimmering while cloaked), not at all when not
            bool seen = !Up || Sensors.Detected(transform);
            Draw(seen, Up ? 0.75f : 1f);
        }

        /// <summary>Draw it (with a flicker: the share of frames it shows), or keep it from being drawn.</summary>
        void Draw(bool show, float flicker)
        {
            // (its parts can change: plates made after it, breakup chunks; looked at again when they do)
            if (childCount != transform.childCount || Time.time >= nextLook) Collect();
            bool on = show && (flicker >= 1f || Random.value < flicker);
            foreach (var r in renderers) if (r != null && r.forceRenderingOff == on) r.forceRenderingOff = !on;
            // its lights: off while hidden (each set back as it was when it shows again)
            if (!show)
            {
                foreach (var l in lights)
                    if (l != null && l.enabled) { l.enabled = false; if (!lightsOff.Contains(l)) lightsOff.Add(l); }
            }
            else if (lightsOff.Count > 0)
            {
                foreach (var l in lightsOff) if (l != null) l.enabled = true;
                lightsOff.Clear();
            }
            Hidden = !show;
        }

        void Collect()
        {
            childCount = transform.childCount;
            nextLook = Time.time + 1f;
            renderers.Clear();
            GetComponentsInChildren(true, renderers);
            lights.Clear();
            GetComponentsInChildren(true, lights);
        }
    }
}

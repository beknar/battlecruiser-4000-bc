using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// A cloak (the Grey scout's): while it is on, the ship's signature is kept to `hiddenScale`
    /// of its own (Signature.hiddenScale), so the players' sensors find it only close in: a ship
    /// of signature 1 cloaked to a tenth is seen inside 3 km of the 30, sooner by a frigate with
    /// good ECCM (Sensors). Firing, or a hit, lights the ship up (its Signature's lightUp: seen
    /// as any ship), and the cloak takes `recloakTime` more to come back once that is over. Unseen, a cloaked ship isn't drawn at all; seen, it
    /// shimmers. In co-op the host's game says whether it is on (Net), and each game works out
    /// for itself whether its own player sees it.
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
        Renderer[] renderers;
        bool shown = true;
        float downUntil;

        /// <summary>The cloak is up now (engaged, and not dropped by firing or a hit).</summary>
        public bool Up { get; private set; }

        /// <summary>Co-op: set by the host's game (its puppet's own firing and hits aren't its).</summary>
        public bool NetDriven { get; set; }

        void Awake()
        {
            sig = GetComponent<Signature>();
            renderers = GetComponentsInChildren<Renderer>(true);
        }

        void OnDisable() { Show(true); if (sig != null) sig.hiddenScale = 1f; }

        /// <summary>Turn it on (its tactics: slipping away) or off (for good, till turned on again).</summary>
        public void Engage(bool on) => engaged = on;

        /// <summary>Co-op: the host's game says whether it is up.</summary>
        public void NetSet(bool up)
        {
            NetDriven = true;
            engaged = up;
            downUntil = 0f;
        }

        void Update()
        {
            if (!NetDriven && sig != null && sig.Lit)
            {
                // fired or hit (lit up for its signature's lightUp): down, and back a while after
                downUntil = Time.time + recloakTime;
            }
            Up = engaged && (NetDriven || Time.time >= downUntil);
            if (sig != null) sig.hiddenScale = Up ? hiddenScale : 1f;
            // drawn when seen (shimmering while cloaked), not at all when not
            bool seen = !Up || Sensors.Detected(transform);
            if (!seen) Show(false);
            else if (Up) Show(Random.value > 0.25f);   // the shimmer: a flicker
            else Show(true);
        }

        void Show(bool on)
        {
            if (on == shown || renderers == null) return;
            shown = on;
            foreach (var r in renderers) if (r != null && !(r is LineRenderer) && !(r is TrailRenderer)) r.enabled = on;
        }
    }
}

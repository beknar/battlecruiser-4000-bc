using UnityEngine;

namespace CarrierGame
{
    /// <summary>The fires (FireFx) the game kindles, made by CarrierBuilder.FireFx into the
    /// git-ignored Kit's Resources (Kit/FireFX/Resources/FireKit): a clone without the pack has
    /// no FireKit, and nothing burns.</summary>
    public class FireKit : ScriptableObject
    {
        [Tooltip("A tall blaze: a wreck's biggest piece.")]
        public FireFx blaze;
        [Tooltip("A wide fire, low on the ground: a building struck from orbit, a wreck's lesser piece.")]
        public FireFx groundFire;
        [Tooltip("The smoke of what has burnt out: rubble struck a while ago.")]
        public FireFx smoulder;

        [Tooltip("Seconds a settlement's building burns, once struck while the ship is down there (its rubble smoulders on after).")]
        public float ruinBurn = 600f;
        [Tooltip("Seconds a wreck burns.")]
        public float wreckBurn = 240f;

        static FireKit kit;
        static bool looked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { kit = null; looked = false; }

        /// <summary>The fires, or null without the pack (or where fires aren't shown).</summary>
        public static FireKit Get()
        {
            if (!FireFx.Supported) return null;
            if (!looked) { looked = true; kit = Resources.Load<FireKit>("FireKit"); }
            return kit;
        }
    }
}

using UnityEngine;

namespace CarrierGame
{
    /// <summary>Where an enemy is posted (a planet, a field): it is carried along with that, wherever it flies.</summary>
    public class OrbitHome : MonoBehaviour
    {
        public Transform home;

        /// <summary>Something launched by a ship (a missile, a shell) goes with the place that
        /// ship belongs to, as the ship does: carried along with a moving comet or moon rather
        /// than left behind in open space while it is on its way.</summary>
        /// `target`: what it is going for, whose place it takes when the ship that launched it has
        /// none of its own (the player's ship: a missile of its at the Vrask round a comet).
        public static void Inherit(GameObject go, Transform owner, Transform target = null)
        {
            if (go == null) return;
            var src = owner != null ? owner.GetComponentInParent<OrbitHome>() : null;
            if ((src == null || src.home == null) && target != null) src = target.GetComponentInParent<OrbitHome>();
            var mine = go.GetComponent<OrbitHome>();
            if (src == null || src.home == null) { if (mine != null) mine.home = null; return; }
            if (mine == null) mine = go.AddComponent<OrbitHome>();
            mine.home = src.home;
        }
    }
}

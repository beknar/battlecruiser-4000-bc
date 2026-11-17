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
        public static void Inherit(GameObject go, Transform owner)
        {
            if (go == null) return;
            var src = owner != null ? owner.GetComponentInParent<OrbitHome>() : null;
            var mine = go.GetComponent<OrbitHome>();
            if (src == null || src.home == null) { if (mine != null) mine.home = null; return; }
            if (mine == null) mine = go.AddComponent<OrbitHome>();
            mine.home = src.home;
        }
    }
}

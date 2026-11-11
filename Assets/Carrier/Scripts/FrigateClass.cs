using UnityEngine;

namespace CarrierGame
{
    /// <summary>Which of the frigates (FrigateRoster) a ship is.</summary>
    [DisallowMultipleComponent]
    public class FrigateClass : MonoBehaviour
    {
        public string id;

        /// <summary>The class of a ship ("" if it isn't one of the frigates).</summary>
        public static string Of(Component ship) => ship != null && ship.TryGetComponent(out FrigateClass c) ? c.id : "";
    }
}

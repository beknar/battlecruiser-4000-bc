using UnityEngine;

namespace CarrierGame
{
    /// <summary>Which of the frigates (FrigateRoster) a ship is.</summary>
    [DisallowMultipleComponent]
    public class FrigateClass : MonoBehaviour
    {
        public string id;

        [Tooltip("Its electronic counter-countermeasures, 0 to 1 (frigates.json): how far it sees a cloaked ship (up to twice as far), and later how much jamming and EMP it shrugs off.")]
        public float eccm;

        /// <summary>A ship's ECCM (0: none, or not a frigate).</summary>
        public static float EccmOf(Component ship) => ship != null && ship.TryGetComponent(out FrigateClass c) ? Mathf.Clamp01(c.eccm) : 0f;

        /// <summary>The class of a ship ("" if it isn't one of the frigates).</summary>
        public static string Of(Component ship) => ship != null && ship.TryGetComponent(out FrigateClass c) ? c.id : "";
    }
}

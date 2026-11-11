using UnityEngine;

namespace CarrierGame
{
    /// <summary>Which prefab an object from the Pool was made from (so it can be put by for it).</summary>
    [AddComponentMenu("")]
    public sealed class Pooled : MonoBehaviour
    {
        public GameObject prefab;
    }
}

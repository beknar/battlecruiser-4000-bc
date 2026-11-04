using System.Collections;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// A launch bay or hangar. Craft spawn at the launch points (their forward is the launch
    /// direction) and fly out through the mouth; returning craft fly back in to land.
    /// </summary>
    public class LaunchBay : MonoBehaviour
    {
        public Transform[] launchPoints;
        [Tooltip("A point at the bay opening; its forward points out of the bay.")]
        public Transform mouth;
        public float launchSpeed = 60f;
        public float interval = 1.5f;

        int next;
        public bool Busy { get; private set; }

        public void Launch(Craft prefab, int count, CarrierController carrier, Transform target)
        {
            if (prefab != null && count > 0) StartCoroutine(LaunchRoutine(prefab, count, carrier, target));
        }

        IEnumerator LaunchRoutine(Craft prefab, int count, CarrierController carrier, Transform target)
        {
            Busy = true;
            for (int i = 0; i < count; i++)
            {
                var p = launchPoints[next];
                next = (next + 1) % launchPoints.Length;
                var c = Instantiate(prefab, p.position, p.rotation);
                SoundFX.Play(Sfx.Catapult, p.position, 1f, prefab.kind == Craft.Kind.Dropship ? 0.7f : 1f);
                c.carrier = carrier;
                c.homeBay = this;
                c.homePoint = p;
                c.target = target;
                c.speed = launchSpeed;
                c.order = Craft.Order.Launch;
                carrier?.Register(c);
                yield return new WaitForSeconds(interval);
            }
            Busy = false;
        }
    }
}

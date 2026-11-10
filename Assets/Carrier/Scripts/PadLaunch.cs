using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarrierGame
{
    /// <summary>A ship parked on a launch pad: when the alarm goes up it lifts straight off, then
    /// its pilot takes over (and comes back to hover over the pad when the fight is over).</summary>
    public class PadLaunch : MonoBehaviour, IOriginShift
    {
        public CombatPilot pilot;
        public float climb = 240f;
        public float climbTime = 4.5f;
        public bool Launched { get; private set; }
        bool launching;

        public void Launch()
        {
            if (Launched || launching) return;
            launching = true;
            StartCoroutine(Lift());
        }

        Vector3 start;

        IEnumerator Lift()
        {
            var h = GetComponent<Health>();
            start = transform.position;
            SoundFX.Play(Sfx.WarpExit, start, 0.5f, 1.4f);
            for (float t = 0f; t < climbTime; t += Time.deltaTime)
            {
                if (h != null && h.Dead) yield break;
                float k = t / climbTime;
                transform.position = start + Vector3.up * climb * (k * k * (3f - 2f * k));
                yield return null;
            }
            transform.position = start + Vector3.up * climb;
            launching = false;
            Launched = true;
            if (pilot != null)
            {
                pilot.SetHome(transform.position, transform.rotation);
                pilot.enabled = true;
                pilot.Engage();
            }
        }

        public void OriginShifted(Vector3 d) => start += d;
    }
}

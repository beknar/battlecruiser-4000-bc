using System.Collections;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>A block of vertical launch cells. Fires salvos of guided missiles from them in turn.</summary>
    public class MissileLauncher : MonoBehaviour
    {
        public Missile missilePrefab;
        public Transform[] cells;
        public float cellReload = 8f;
        public float salvoInterval = 0.15f;

        float[] readyAt;
        int next;

        void Awake() => readyAt = new float[cells != null ? cells.Length : 0];

        public int ReadyCells
        {
            get
            {
                int n = 0;
                for (int i = 0; i < readyAt.Length; i++) if (Time.time >= readyAt[i]) n++;
                return n;
            }
        }

        /// <summary>Launch up to `count` missiles at the target, one cell after another.</summary>
        public void FireSalvo(Transform target, int count, Vector3 carrierVelocity = default)
        {
            StartCoroutine(Salvo(target, count, carrierVelocity));
        }

        IEnumerator Salvo(Transform target, int count, Vector3 carrierVelocity)
        {
            for (int fired = 0, tries = 0; fired < count && tries < cells.Length; tries++)
            {
                int i = next;
                next = (next + 1) % cells.Length;
                if (Time.time < readyAt[i] || cells[i] == null) continue;
                readyAt[i] = Time.time + cellReload;
                var m = Instantiate(missilePrefab, cells[i].position, cells[i].rotation);
                m.target = target;
                m.inheritedVelocity = carrierVelocity;
                fired++;
                yield return new WaitForSeconds(salvoInterval);
            }
        }
    }
}

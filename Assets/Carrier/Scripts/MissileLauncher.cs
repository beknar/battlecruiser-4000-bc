using System.Collections;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>A block of vertical launch cells. Fires salvos of guided missiles from them in turn.
    /// With a magazine (a destroyer's or cruiser's), only so many missiles are aboard: each one
    /// fired is one fewer until the ship is rearmed (docked or landed at a site).</summary>
    public class MissileLauncher : MonoBehaviour
    {
        public Missile missilePrefab;
        public Transform[] cells;
        public float cellReload = 8f;
        public float salvoInterval = 0.15f;
        [Tooltip("Missiles aboard when full (-1: no limit).")]
        public int magazine = -1;

        float[] readyAt;
        int next;
        int stock = -2;

        void Awake() => readyAt = new float[cells != null ? cells.Length : 0];

        /// <summary>Missiles left aboard (-1: no limit).</summary>
        public int Stock
        {
            get => magazine < 0 ? -1 : stock < 0 ? magazine : stock;
            set => stock = magazine < 0 ? -2 : Mathf.Clamp(value, 0, magazine);
        }

        /// <summary>Short of a full magazine.</summary>
        public bool NeedsRearming => magazine >= 0 && Stock < magazine;

        /// <summary>Fill the magazine; returns how many were loaded.</summary>
        public int Rearm()
        {
            if (!NeedsRearming) return 0;
            int n = magazine - Stock;
            stock = magazine;
            return n;
        }

        public int ReadyCells
        {
            get
            {
                int n = 0;
                if (readyAt == null) return 0;
                for (int i = 0; i < readyAt.Length; i++) if (Time.time >= readyAt[i]) n++;
                return magazine < 0 ? n : Mathf.Min(n, Stock);
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
                if (magazine >= 0)
                {
                    if (Stock <= 0) yield break;
                    Stock = Stock - 1;
                }
                readyAt[i] = Time.time + cellReload;
                var m = Instantiate(missilePrefab, cells[i].position, cells[i].rotation);
                m.owner = transform.root;
                m.target = target;
                m.inheritedVelocity = carrierVelocity;
                fired++;
                yield return new WaitForSeconds(salvoInterval);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// The frigate's ore hold: so many tonnes of each mineral, up to `capacity` in all. What the
    /// mining laser brings in goes here; the cheapest mineral can be jettisoned to make room for
    /// better. The cargo is lost if the ship is destroyed. Docked at a station with a market,
    /// the whole hold is sold, and the credits kept.
    /// </summary>
    public class CargoHold : MonoBehaviour
    {
        [Tooltip("Most the hold can carry, in tonnes.")]
        public float capacity = 600f;

        readonly float[] tonnes = new float[Minerals.Count];

        /// <summary>Tonnes loaded, of everything.</summary>
        public float Used { get; private set; }
        public float Free => Mathf.Max(0f, capacity - Used);
        public bool Full => Free < 0.01f;
        public float Fraction => capacity > 0f ? Used / capacity : 1f;
        /// <summary>Everything ever brought aboard, in tonnes.</summary>
        public float TotalMined { get; private set; }
        public float TotalJettisoned { get; private set; }
        /// <summary>Tonnes lost with the ship.</summary>
        public float TotalLost { get; private set; }
        /// <summary>Credits earned selling ore.</summary>
        public float Credits { get; private set; }
        public float TotalSold { get; private set; }

        public event Action Changed;

        public float this[Mineral m] => tonnes[(int)m];

        public float Value
        {
            get
            {
                float v = 0f;
                for (int i = 0; i < tonnes.Length; i++) v += tonnes[i] * Minerals.Value((Mineral)i);
                return v;
            }
        }

        void Start()
        {
            if (TryGetComponent(out Health h)) h.onDestroyed.AddListener(LoseAll);
        }

        void OnDestroy()
        {
            if (TryGetComponent(out Health h) && h.onDestroyed != null) h.onDestroyed.RemoveListener(LoseAll);
        }

        /// <summary>Load up to `t` tonnes of a mineral; returns how much there was room for.</summary>
        public float Add(Mineral m, float t)
        {
            t = Mathf.Min(Mathf.Max(0f, t), Free);
            if (t <= 0f) return 0f;
            tonnes[(int)m] += t;
            Used += t;
            TotalMined += t;
            Changed?.Invoke();
            return t;
        }

        /// <summary>Load a mix, in proportion, as far as there is room. Returns the tonnes loaded.</summary>
        public float AddMix(Share[] mix, float t)
        {
            t = Mathf.Min(Mathf.Max(0f, t), Free);
            if (t <= 0f) return 0f;
            foreach (var s in mix)
            {
                float part = t * s.fraction;
                tonnes[(int)s.mineral] += part;
            }
            Used += t;
            TotalMined += t;
            Changed?.Invoke();
            return t;
        }

        /// <summary>What is aboard, most valuable cargo (by its total worth) first.</summary>
        public List<(Mineral mineral, float tonnes)> Manifest()
        {
            var list = new List<(Mineral, float)>();
            for (int i = 0; i < tonnes.Length; i++)
                if (tonnes[i] >= 0.005f) list.Add(((Mineral)i, tonnes[i]));
            list.Sort((a, b) => (b.Item2 * Minerals.Value(b.Item1)).CompareTo(a.Item2 * Minerals.Value(a.Item1)));
            return list;
        }

        /// <summary>The least valuable mineral aboard (per tonne), or null if the hold is empty.</summary>
        public Mineral? Cheapest()
        {
            Mineral? best = null;
            for (int i = 0; i < tonnes.Length; i++)
                if (tonnes[i] >= 0.005f && (best == null || Minerals.Value((Mineral)i) < Minerals.Value(best.Value))) best = (Mineral)i;
            return best;
        }

        /// <summary>Dump every tonne of one mineral. Returns the tonnes dumped.</summary>
        public float Jettison(Mineral m)
        {
            float t = tonnes[(int)m];
            if (t <= 0f) return 0f;
            tonnes[(int)m] = 0f;
            Used = Mathf.Max(0f, Used - t);
            TotalJettisoned += t;
            Changed?.Invoke();
            return t;
        }

        /// <summary>Sell everything aboard at its worth: returns the credits it fetched (and the
        /// tonnes sold).</summary>
        public float SellAll(out float tonnesSold)
        {
            tonnesSold = Used;
            if (Used <= 0f) return 0f;
            float v = Value;
            Array.Clear(tonnes, 0, tonnes.Length);
            Used = 0f;
            Credits += v;
            TotalSold += tonnesSold;
            Changed?.Invoke();
            return v;
        }

        /// <summary>Empty the hold (the ship was destroyed).</summary>
        void LoseAll()
        {
            if (Used <= 0f) return;
            TotalLost += Used;
            Array.Clear(tonnes, 0, tonnes.Length);
            Used = 0f;
            Changed?.Invoke();
        }
    }
}

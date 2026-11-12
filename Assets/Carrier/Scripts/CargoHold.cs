using System;
using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// The frigate's hold: so many tonnes of each mineral, and the refined materials and ship
    /// parts it carries between the industrial sites (IndustryTable), up to `capacity` tonnes in
    /// all. What the mining laser brings in goes here; the cheapest mineral can be jettisoned to
    /// make room for better. The cargo is lost if the ship is destroyed. Docked at a station with
    /// a market, the ore is sold, and the credits kept.
    /// </summary>
    public class CargoHold : MonoBehaviour
    {
        [Tooltip("Most the hold can carry, in tonnes.")]
        public float capacity = 600f;

        readonly float[] tonnes = new float[Minerals.Count];
        // refined materials (tonnes) and parts (by the part)
        readonly Stock goods = new Stock();

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

        /// <summary>What the ore aboard is worth (credits).</summary>
        public float Value
        {
            get
            {
                float v = 0f;
                for (int i = 0; i < tonnes.Length; i++) v += tonnes[i] * Minerals.Value((Mineral)i);
                return v;
            }
        }

        /// <summary>Tonnes of ore aboard (the rest of what is used is refined goods and parts).</summary>
        public float OreTonnes
        {
            get
            {
                float t = 0f;
                for (int i = 0; i < tonnes.Length; i++) t += tonnes[i];
                return t;
            }
        }

        /// <summary>The refined materials and parts aboard.</summary>
        public Stock Goods => goods;

        /// <summary>How much of an item is aboard (tonnes, or parts).</summary>
        public float Amount(string id)
        {
            if (IndustryTable.AsMineral(id, out var m)) return tonnes[(int)m];
            return goods.Amount(id);
        }

        /// <summary>How much of an item there is room for (whole parts only).</summary>
        public float RoomFor(string id)
        {
            var p = IndustryTable.Get().Part(id);
            if (p == null) return Free;
            return p.mass > 0f ? Mathf.Floor(Free / p.mass + 1e-4f) : float.MaxValue;
        }

        /// <summary>Take aboard up to `amount` of an item brought from storage (not mined): returns
        /// how much there was room for.</summary>
        public float Load(string id, float amount)
        {
            var table = IndustryTable.Get();
            if (table.Part(id) != null) amount = Mathf.Floor(amount + 1e-4f);
            amount = Mathf.Min(Mathf.Max(0f, amount), RoomFor(id));
            if (amount <= 0f) return 0f;
            if (IndustryTable.AsMineral(id, out var m)) tonnes[(int)m] += amount;
            else if (table.Kind(id) != ItemKind.None) goods.Add(id, amount);
            else return 0f;
            Used += table.Mass(id, amount);
            Changed?.Invoke();
            return amount;
        }

        /// <summary>Put off up to `amount` of an item (into storage): returns how much there was.</summary>
        public float Unload(string id, float amount)
        {
            var table = IndustryTable.Get();
            if (table.Part(id) != null) amount = Mathf.Floor(amount + 1e-4f);
            float t;
            if (IndustryTable.AsMineral(id, out var m))
            {
                t = Mathf.Min(Mathf.Max(0f, amount), tonnes[(int)m]);
                tonnes[(int)m] -= t;
                if (tonnes[(int)m] < 1e-4f) tonnes[(int)m] = 0f;
            }
            else t = goods.Take(id, amount);
            if (t <= 0f) return 0f;
            Used = Mathf.Max(0f, Used - table.Mass(id, t));
            if (Used < 1e-4f) Used = 0f;
            Changed?.Invoke();
            return t;
        }

        /// <summary>Everything aboard, by item id: ore first (most valuable first), then goods.</summary>
        public List<Stock.Entry> Contents()
        {
            var list = new List<Stock.Entry>();
            foreach (var (m, t) in Manifest()) list.Add(new Stock.Entry { id = m.ToString(), amount = t });
            list.AddRange(goods.Sorted(IndustryTable.Get()));
            return list;
        }

        /// <summary>Put back what was saved (the hold as it was last time, and the credits):
        /// it may be more than this frigate's hold takes, in which case nothing more fits.</summary>
        public void Restore(List<Stock.Entry> contents, float credits)
        {
            Array.Clear(tonnes, 0, tonnes.Length);
            goods.Clear();
            Used = 0f;
            var table = IndustryTable.Get();
            if (contents != null)
                foreach (var e in contents)
                {
                    if (e == null || e.amount <= 0f) continue;
                    if (IndustryTable.AsMineral(e.id, out var m)) tonnes[(int)m] += e.amount;
                    else if (table.Kind(e.id) != ItemKind.None) goods.Add(e.id, e.amount);
                    else continue;
                    Used += table.Mass(e.id, e.amount);
                }
            Credits = credits;
            Changed?.Invoke();
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

        /// <summary>Sell all the ore aboard at its worth: returns the credits it fetched (and the
        /// tonnes sold). Refined goods and parts stay aboard.</summary>
        public float SellAll(out float tonnesSold)
        {
            tonnesSold = OreTonnes;
            if (tonnesSold <= 0f) return 0f;
            float v = Value;
            Array.Clear(tonnes, 0, tonnes.Length);
            Used = goods.Mass(IndustryTable.Get());
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
            goods.Clear();
            Used = 0f;
            Changed?.Invoke();
        }
    }
}

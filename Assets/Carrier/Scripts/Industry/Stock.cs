using System;
using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// A store of items by id (IndustryTable): an installation's storage. Raw and refined items
    /// in tonnes, parts by the part. No limit on what it holds; the ships that bring it are what
    /// is limited.
    /// </summary>
    [Serializable]
    public class Stock
    {
        [Serializable]
        public class Entry
        {
            public string id;
            public float amount;
        }

        // (a list, so JsonUtility can save it)
        public List<Entry> items = new List<Entry>();

        public event Action Changed;

        Entry Find(string id)
        {
            foreach (var e in items) if (e.id == id) return e;
            return null;
        }

        public float Amount(string id) => Find(id)?.amount ?? 0f;

        public bool Has(string id, float amount) => Amount(id) >= amount - 1e-4f;

        public void Add(string id, float amount)
        {
            if (string.IsNullOrEmpty(id) || amount <= 0f) return;
            var e = Find(id);
            if (e == null) items.Add(e = new Entry { id = id });
            e.amount += amount;
            Changed?.Invoke();
        }

        /// <summary>Take up to `amount`: returns how much there was.</summary>
        public float Take(string id, float amount)
        {
            var e = Find(id);
            if (e == null || amount <= 0f) return 0f;
            float t = Mathf.Min(amount, e.amount);
            e.amount -= t;
            if (e.amount < 1e-4f) items.Remove(e);
            Changed?.Invoke();
            return t;
        }

        /// <summary>Everything in it, raw first, then refined, then parts, each by name.</summary>
        public List<Entry> Sorted(IndustryTable table)
        {
            var list = new List<Entry>(items);
            list.Sort((a, b) =>
            {
                int k = table.Kind(a.id).CompareTo(table.Kind(b.id));
                return k != 0 ? k : string.CompareOrdinal(table.Name(a.id), table.Name(b.id));
            });
            return list;
        }

        public float Mass(IndustryTable table)
        {
            float m = 0f;
            foreach (var e in items) m += table.Mass(e.id, e.amount);
            return m;
        }

        public float Value(IndustryTable table)
        {
            float v = 0f;
            foreach (var e in items) v += table.Value(e.id) * e.amount;
            return v;
        }

        public void Clear()
        {
            if (items.Count == 0) return;
            items.Clear();
            Changed?.Invoke();
        }
    }
}

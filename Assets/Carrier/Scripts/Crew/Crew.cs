using System;
using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// The people who work a frigate or an installation (CrewTable). Hull damage kills them: a
    /// full hull's worth of damage (all its hulls', for an installation with several guns) kills
    /// `casualties` of the complement, never below `survivors` of it. Short of `threshold` of its
    /// complement it works less well (Efficiency): a frigate's numbers drop (ShipFittings), an
    /// installation's guns fire less often. Repairs don't bring crew back; recruiting does (the
    /// frigate, at places that recruit: IndustryLedger.Recruit), or reinforcements (a hostile
    /// installation, at its place's refill rate).
    /// </summary>
    [DisallowMultipleComponent]
    public class Crew : MonoBehaviour
    {
        [Tooltip("Its full crew.")]
        public int complement = 100;
        [Tooltip("Reinforcements a minute (a hostile installation's), 0 for none.")]
        public float reinforcePerMinute;

        float current = -1f;
        /// <summary>Crew aboard (fractional losses add up).</summary>
        public float Current => current < 0f ? complement : current;
        public int Aboard => Mathf.Clamp(Mathf.RoundToInt(Current), 0, complement);
        public int Missing => Mathf.Max(0, complement - Aboard);
        public float Share => complement > 0 ? Mathf.Clamp01(Current / complement) : 1f;
        /// <summary>How well it works with the crew it has (1 at full strength).</summary>
        public float Efficiency => complement > 0 ? CrewTable.Get().Efficiency(Share) : 1f;
        /// <summary>Crew killed so far.</summary>
        public float TotalLost { get; private set; }

        public event Action Changed;

        readonly List<Health> hulls = new List<Health>();
        // each hull's strength as last seen (a destroyed one keeps its share), and their total
        readonly List<float> hullMax = new List<float>();
        float totalHull;
        int totalFrame = -1;
        EnemyWeapons[] weapons;

        void Start()
        {
            if (current < 0f) current = complement;
            foreach (var h in GetComponentsInChildren<Health>(true))
            {
                h.HullDamaged += OnHull;
                hulls.Add(h);
                hullMax.Add(Mathf.Max(1f, h.maxHealth));
            }
            weapons = GetComponentsInChildren<EnemyWeapons>(true);
            ApplyToWeapons();
        }

        void OnDestroy()
        {
            foreach (var h in hulls) if (h != null) h.HullDamaged -= OnHull;
            hulls.Clear();
        }

        void OnHull(float applied)
        {
            if (complement <= 0 || applied <= 0f) return;
            // (against all its hulls as they are now, co-op scaling and armour making them stronger,
            // and the ones destroyed outright as they were: a hull's worth always costs the same)
            if (totalFrame != Time.frameCount)
            {
                totalFrame = Time.frameCount;
                totalHull = 0f;
                for (int i = 0; i < hulls.Count; i++)
                {
                    if (hulls[i] != null) hullMax[i] = Mathf.Max(1f, hulls[i].maxHealth);
                    totalHull += hullMax[i];
                }
            }
            Lose(complement * CrewTable.Get().casualties * applied / Mathf.Max(1f, totalHull));
        }

        /// <summary>Lose `n` crew (never below the survivors).</summary>
        public void Lose(float n)
        {
            if (complement <= 0 || n <= 0f) return;
            float floor = complement * CrewTable.Get().survivors;
            float before = Current;
            float after = before <= floor ? before : Mathf.Max(floor, before - n);
            if (after >= before) return;
            current = after;
            TotalLost += before - after;
            Changed?.Invoke();
            ApplyToWeapons();
        }

        /// <summary>Take on up to `n` crew: returns how many there was room for.</summary>
        public int Add(int n)
        {
            int room = complement - Aboard;
            int k = Mathf.Clamp(n, 0, room);
            if (k == 0) return 0;
            current = Mathf.Min(complement, Aboard + k);
            Changed?.Invoke();
            ApplyToWeapons();
            return k;
        }

        /// <summary>Set how many are aboard (what was saved).</summary>
        public void SetAboard(float n)
        {
            current = Mathf.Clamp(n, 0f, complement);
            Changed?.Invoke();
            ApplyToWeapons();
        }

        void Update()
        {
            if (reinforcePerMinute <= 0f || current < 0f || current >= complement) return;
            int before = Aboard;
            current = Mathf.Min(complement, current + reinforcePerMinute * Time.deltaTime / 60f);
            if (Aboard != before) { Changed?.Invoke(); ApplyToWeapons(); }
        }

        void ApplyToWeapons()
        {
            if (weapons == null) return;
            float e = Efficiency;
            foreach (var w in weapons) if (w != null) w.efficiency = e;
        }

        /// <summary>The crew a ship or installation belongs to (on it or above it), or null.</summary>
        public static Crew Of(Component c) => c != null ? c.GetComponentInParent<Crew>() : null;
    }
}

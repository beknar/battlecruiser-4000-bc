using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// The parts fitted to a frigate (IndustryTable parts): it has `slots` for them, each part
    /// can be fitted once, and each multiplies some of the ship's numbers (its hull, shield,
    /// speed, guns, beam, hold, mining laser). The numbers it was built with are kept, so taking
    /// a part off puts them back. Parts are fitted and taken off at a factory (IndustryPanel).
    /// </summary>
    [DisallowMultipleComponent]
    public class ShipFittings : MonoBehaviour
    {
        [Tooltip("How many parts it can have fitted.")]
        public int slots = 4;

        readonly List<string> fitted = new List<string>();
        public IReadOnlyList<string> Fitted => fitted;
        public int Free => Mathf.Max(0, slots - fitted.Count);
        public event Action Changed;

        // the numbers as built
        bool captured;
        float hull, shield, recharge, speed, acceleration, turn, cargo, beamDamage, beamCharge, miningRate;
        readonly List<(CarrierTurret gun, float range, float interval)> guns = new List<(CarrierTurret, float, float)>();

        Health health;
        Crew crew;
        float crewEff = 1f;
        Shield shieldC;
        CarrierController cc;
        CargoHold hold;
        MainBeam beam;
        MiningLaser laser;

        void Awake() => Capture();

        void Capture()
        {
            if (captured) return;
            captured = true;
            health = GetComponent<Health>();
            crew = GetComponent<Crew>();
            if (crew != null) crew.Changed += CrewChanged;
            shieldC = GetComponentInChildren<Shield>(true);
            cc = GetComponent<CarrierController>();
            hold = GetComponent<CargoHold>();
            beam = GetComponentInChildren<MainBeam>(true);
            laser = GetComponentInChildren<MiningLaser>(true);
            hull = health != null ? health.maxHealth : 0f;
            if (shieldC != null) { shield = shieldC.capacity; recharge = shieldC.rechargeRate; }
            if (cc != null)
            {
                speed = cc.maxSpeed; acceleration = cc.acceleration; turn = cc.turnRate;
                if (cc.mainTurrets != null)
                    foreach (var t in cc.mainTurrets) if (t != null) guns.Add((t, t.range, t.fireInterval));
            }
            cargo = hold != null ? hold.capacity : 0f;
            if (beam != null) { beamDamage = beam.damagePerSecond; beamCharge = beam.chargeTime; }
            miningRate = laser != null ? laser.rate : 0f;
        }

        void OnDestroy()
        {
            if (crew != null) crew.Changed -= CrewChanged;
        }

        // (a short crew: everything it works slows down; only its numbers are looked at again)
        void CrewChanged()
        {
            float e = crew != null ? crew.Efficiency : 1f;
            if (Mathf.Abs(e - crewEff) > 0.001f) ApplyCrew();
        }

        /// <summary>What a short crew does to a stat (1 at full strength): top speed,
        /// acceleration, turning, the shield and its recharge, the guns' rate of fire, the main
        /// gun's charge time and the mining laser all suffer; the hull, the hold and what each
        /// shot does don't.</summary>
        public float CrewFactor(string stat)
        {
            float e = crew != null ? crew.Efficiency : 1f;
            switch (stat)
            {
                case "speed": case "acceleration": case "turn": case "shield": case "recharge":
                case "gunRate": case "miningRate":
                    return e;
                case "beamCharge":
                    return 1f / Mathf.Max(0.05f, e);
            }
            return 1f;
        }

        /// <summary>The product of the fitted parts' factors for one stat (1 with none).</summary>
        public float Factor(string stat) => FactorOf(fitted, stat);

        public static float FactorOf(IEnumerable<string> parts, string stat)
        {
            var table = IndustryTable.Get();
            float f = 1f;
            foreach (var id in parts)
            {
                var p = table.Part(id);
                if (p?.effects == null) continue;
                foreach (var e in p.effects) if (e.stat == stat) f *= e.factor;
            }
            return f;
        }

        /// <summary>A stat as built (before any parts).</summary>
        public float Base(string stat)
        {
            Capture();
            switch (stat)
            {
                case "hull": return hull;
                case "shield": return shield;
                case "recharge": return recharge;
                case "speed": return speed;
                case "acceleration": return acceleration;
                case "turn": return turn;
                case "cargo": return cargo;
                case "gunDamage": return 1f;
                case "gunRange": return guns.Count > 0 ? guns[0].range : 0f;
                case "gunRate": return guns.Count > 0 && guns[0].interval > 0f ? 60f / guns[0].interval : 0f;
                case "beamDamage": return beamDamage;
                case "beamCharge": return beamCharge;
                case "miningRate": return miningRate;
            }
            return 0f;
        }

        /// <summary>A stat with the parts fitted now, and the crew it has.</summary>
        public float Value(string stat) => Base(stat) * Factor(stat) * CrewFactor(stat);

        public bool IsFitted(string id) => fitted.Contains(id);

        public bool CanFit(string id, out string why)
        {
            var p = IndustryTable.Get().Part(id);
            why = p == null ? "not a ship part" : fitted.Contains(id) ? "already fitted" : Free == 0 ? $"all {slots} slots are taken" : "";
            return why == "";
        }

        public bool Fit(string id)
        {
            if (!CanFit(id, out _)) return false;
            fitted.Add(id);
            Apply();
            return true;
        }

        /// <summary>Whether a part can come off: not if what is aboard wouldn't fit the hold without it.</summary>
        public bool CanUnfit(string id, out string why)
        {
            Capture();
            why = "";
            if (!fitted.Contains(id)) why = "not fitted";
            else if (hold != null)
            {
                float cap = cargo * FactorOf(fitted.Where(p => p != id), "cargo");
                if (hold.Used > cap + 0.01f) why = $"{hold.Used:F0} t aboard, and the hold takes {cap:F0} t without it";
            }
            return why == "";
        }

        public bool Unfit(string id)
        {
            if (!CanUnfit(id, out _)) return false;
            fitted.Remove(id);
            Apply();
            return true;
        }

        /// <summary>Fit these parts (what was saved), in place of any fitted now.</summary>
        public void SetFitted(IEnumerable<string> parts)
        {
            fitted.Clear();
            var table = IndustryTable.Get();
            if (parts != null)
                foreach (var id in parts)
                    if (table.Part(id) != null && !fitted.Contains(id) && fitted.Count < slots) fitted.Add(id);
            Apply();
        }

        /// <summary>Set the ship's numbers from what it was built with, the parts fitted and its crew.</summary>
        void Apply()
        {
            Capture();
            // (the hull and the hold: the parts' alone, a crew never changes them)
            if (health != null) health.SetMax(hull * Factor("hull"));
            if (hold != null) hold.capacity = cargo * Factor("cargo");
            ApplyCrew();
            Changed?.Invoke();
        }

        /// <summary>The numbers a crew's efficiency changes (CrewFactor), from the parts fitted and
        /// the crew aboard: done alone on a hit mid-fight, when nothing else has changed.</summary>
        void ApplyCrew()
        {
            Capture();
            crewEff = crew != null ? crew.Efficiency : 1f;
            float F(string stat) => Factor(stat) * CrewFactor(stat);
            if (shieldC != null)
            {
                shieldC.SetCapacity(shield * F("shield"));
                shieldC.rechargeRate = recharge * F("recharge");
            }
            if (cc != null)
            {
                cc.maxSpeed = speed * F("speed");
                cc.acceleration = acceleration * F("acceleration");
                cc.turnRate = turn * F("turn");
            }
            float dmg = F("gunDamage"), rng = F("gunRange"), rate = F("gunRate");
            foreach (var (gun, range, interval) in guns)
            {
                if (gun == null) continue;
                gun.range = range * rng;
                gun.fireInterval = interval / Mathf.Max(0.01f, rate);
                gun.damageScale = dmg;
            }
            if (beam != null)
            {
                beam.damagePerSecond = beamDamage * F("beamDamage");
                beam.chargeTime = beamCharge * F("beamCharge");
            }
            if (laser != null) laser.rate = miningRate * F("miningRate");
        }
    }
}

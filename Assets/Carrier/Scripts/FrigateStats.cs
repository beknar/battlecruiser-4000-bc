using UnityEngine;

namespace CarrierGame
{
    /// <summary>What a frigate (a prefab, or a ship in the level) has and how good it is, read off
    /// its parts: for the start menu's bars, and for the tests that every frigate has what every
    /// frigate must (its turrets, a CIWS mount or more, one main gun).</summary>
    public struct FrigateStats
    {
        public float speed, acceleration, turnRate;
        public float hull, shield, shieldRecharge;
        public int turrets, barrels, ciws, mainGuns;
        /// <summary>Damage a second from every gun firing (each barrel: its shot's damage over the
        /// gun's interval).</summary>
        public float gunDps, gunRange;
        /// <summary>One bow-beam shot's damage, and how long it takes to charge.</summary>
        public float beamDamage, beamCharge;
        /// <summary>The hold (tonnes), and how many ship parts it can have fitted.</summary>
        public float cargo;
        public int slots;

        public static FrigateStats Of(GameObject ship)
        {
            var s = new FrigateStats();
            if (ship == null) return s;
            var cc = ship.GetComponent<CarrierController>();
            if (cc != null)
            {
                s.speed = cc.maxSpeed;
                s.acceleration = cc.acceleration;
                s.turnRate = cc.turnRate;
                if (cc.mainTurrets != null)
                    foreach (var t in cc.mainTurrets)
                    {
                        if (t == null) continue;
                        s.turrets++;
                        int n = t.muzzles != null ? t.muzzles.Length : 0;
                        s.barrels += n;
                        float dmg = t.projectilePrefab != null ? t.projectilePrefab.damage : 0f;
                        s.gunDps += n * dmg / Mathf.Max(0.05f, t.fireInterval);
                        s.gunRange = Mathf.Max(s.gunRange, t.range);
                    }
            }
            var h = ship.GetComponent<Health>();
            if (h != null) s.hull = h.maxHealth;
            var sh = ship.GetComponentInChildren<Shield>(true);
            if (sh != null) { s.shield = sh.capacity; s.shieldRecharge = sh.rechargeRate; }
            var ciws = ship.GetComponent<CiwsDirector>();
            if (ciws != null && ciws.mounts != null) foreach (var m in ciws.mounts) if (m != null) s.ciws++;
            var beams = ship.GetComponentsInChildren<MainBeam>(true);
            s.mainGuns = beams.Length;
            if (beams.Length > 0)
            {
                s.beamDamage = beams[0].damagePerSecond * beams[0].fireDuration;
                s.beamCharge = beams[0].chargeTime;
            }
            var hold = ship.GetComponent<CargoHold>();
            if (hold != null) s.cargo = hold.capacity;
            var fit = ship.GetComponent<ShipFittings>();
            if (fit != null) s.slots = fit.slots;
            return s;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// The guns of a hostile installation (an orbital weapons battery, a pirate asteroid base).
    /// They are cold until the player comes within `engageRange` of one of them in plain sight;
    /// then every gun opens fire (each only while it can see the frigate). They go cold again
    /// once the player is beyond `disengageRange` of all of them, or destroyed. Each gun or
    /// platform is a target of its own: it can be destroyed, and is rebuilt after a while.
    /// While the grid is firing, the warp drive counts the frigate as in combat.
    /// </summary>
    public class DefenseGrid : MonoBehaviour
    {
        public string gridName = "Defences";
        public EnemyWeapons[] weapons;
        public float engageRange = 6000f;
        public float disengageRange = 9500f;

        public bool Engaged { get; private set; }
        public int TimesEngaged { get; private set; }
        public float EngagedAt { get; private set; } = -1f;

        public static readonly List<DefenseGrid> All = new List<DefenseGrid>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();
        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() => All.Remove(this);

        Transform player;
        Health playerHealth;
        Health[] healths;

        void Start()
        {
            var cc = FindAnyObjectByType<CarrierController>();
            if (cc != null) { player = cc.transform; playerHealth = cc.GetComponent<Health>(); }
            healths = new Health[weapons != null ? weapons.Length : 0];
            for (int i = 0; i < healths.Length; i++)
            {
                if (weapons[i] == null) continue;
                healths[i] = weapons[i].GetComponent<Health>();
                weapons[i].weaponsFree = false;
            }
        }

        bool Alive(int i) => weapons[i] != null && (healths[i] == null || !healths[i].Dead);

        /// <summary>How many of its guns are still in action.</summary>
        public int GunsLeft
        {
            get
            {
                int n = 0;
                if (weapons != null) for (int i = 0; i < weapons.Length; i++) if (Alive(i)) n++;
                return n;
            }
        }

        /// <summary>The distance from a point to the nearest gun still in action.</summary>
        public float NearestGun(Vector3 p, out EnemyWeapons gun)
        {
            gun = null;
            float best = float.MaxValue;
            if (weapons == null || healths == null) return best;
            for (int i = 0; i < weapons.Length; i++)
            {
                if (!Alive(i)) continue;
                float d = Vector3.Distance(p, weapons[i].transform.position);
                if (d < best) { best = d; gun = weapons[i]; }
            }
            return best;
        }

        void Update()
        {
            // with more than one player ship about (co-op): the one nearest the guns
            if (PlayerShips.Count > 1)
            {
                var nearest = PlayerShips.Nearest(transform.position);
                if (nearest != null && nearest != player) { player = nearest; playerHealth = nearest.GetComponentInParent<Health>(); }
            }
            if (player == null || weapons == null) return;
            bool playerAlive = playerHealth == null || !playerHealth.Dead;
            float d = NearestGun(player.position, out var gun);
            if (!Engaged)
            {
                if (playerAlive && gun != null && d < engageRange && Cover.Visible(gun.transform.position, player.position)) Engage();
            }
            else if (!playerAlive || gun == null || d > disengageRange) Disengage();
        }

        /// <summary>Open fire.</summary>
        public void Engage()
        {
            if (Engaged) return;
            Engaged = true;
            TimesEngaged++;
            EngagedAt = Time.time;
            for (int i = 0; i < weapons.Length; i++)
            {
                if (weapons[i] == null) continue;
                weapons[i].weaponsFree = true;
                weapons[i].OpenFireSoon(1f + i * 0.3f);
            }
            SoundFX.Ui(Sfx.UiAlert);
            Demo.CarrierDemoInput.Toast($"{gridName}: weapons locked on!");
        }

        void Disengage()
        {
            Engaged = false;
            foreach (var w in weapons) if (w != null) w.weaponsFree = false;
        }

        /// <summary>Is any grid firing with a gun within `range` of a point?</summary>
        public static bool AnyEngagedNear(Vector3 p, float range, out string name)
        {
            name = null;
            foreach (var g in All)
            {
                if (g == null || !g.Engaged) continue;
                if (g.NearestGun(p, out _) < range) { name = g.gridName; return true; }
            }
            return false;
        }
    }
}

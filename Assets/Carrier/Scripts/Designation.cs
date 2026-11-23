using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// Target designation: a ship painting another with a targeting laser (a Grey scout, an
    /// Imperial recon frigate: Designator) so the rest of its faction hit it better. A target
    /// painted for a faction is fired on by that faction's guns with a third of their spread, and
    /// their missiles go from half as far again (EnemyWeapons). A painted player's HUD warns them
    /// and says where the laser comes from (ShipHud). A paint lasts a moment after the laser
    /// leaves it, so it is held while it is kept on.
    /// </summary>
    public static class Designation
    {
        /// <summary>How long (s) a paint lasts once the laser is off it.</summary>
        public const float Hold = 1.5f;
        /// <summary>A painted target: its spread is this share for the painting faction's guns.</summary>
        public const float SpreadShare = 1f / 3f;
        /// <summary>A painted target: the painting faction's missiles reach this many times as far.</summary>
        public const float MissileReach = 1.5f;
        /// <summary>A painted target: the painting faction's turrets' shots, laid by the laser, hit
        /// this many times as hard (more of them on its vitals).</summary>
        public const float TurretShare = 1.25f;

        public struct Paint
        {
            public Transform painter;
            public string faction;
            public float until;
        }

        // (a paint for each faction on a target: a Grey and an Imperial laser on one ship both count)
        static readonly Dictionary<(Transform, string), Paint> paints = new Dictionary<(Transform, string), Paint>();
        static readonly List<(Transform, string)> worn = new List<(Transform, string)>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => paints.Clear();

        /// <summary>`painter` (of `faction`) has its laser on `target` now.</summary>
        public static void Mark(Transform painter, Transform target, string faction)
        {
            if (painter == null || target == null) return;
            faction ??= "";
            paints[(target.root, faction)] = new Paint { painter = painter, faction = faction, until = Time.time + Hold };
        }

        static bool Live(Paint p) => p.painter != null && Time.time <= p.until && !(p.painter.TryGetComponent(out Health h) && h.Dead);

        /// <summary>A paint on a target, any faction's (false: none, or worn off, or its painter gone).</summary>
        public static bool On(Transform target, out Paint paint)
        {
            paint = default;
            if (target == null || paints.Count == 0) return false;
            var root = target.root;
            bool found = false;
            worn.Clear();
            foreach (var kv in paints)
            {
                if (kv.Key.Item1 != root) continue;
                if (!Live(kv.Value)) { worn.Add(kv.Key); continue; }
                if (!found || kv.Value.until > paint.until) { paint = kv.Value; found = true; }
            }
            foreach (var k in worn) paints.Remove(k);
            return found;
        }

        /// <summary>Is the target painted for `faction` (any faction, if null)?</summary>
        public static bool PaintedFor(Transform target, string faction)
        {
            if (faction == null) return On(target, out _);
            if (target == null || !paints.TryGetValue((target.root, faction), out var p)) return false;
            if (Live(p)) return true;
            paints.Remove((target.root, faction));
            return false;
        }

        /// <summary>The ship painting a target, or null.</summary>
        public static Transform PainterOf(Transform target) => On(target, out var p) ? p.painter : null;

        /// <summary>What a painted ship's HUD says: who paints it (named only if its sensors have
        /// named the painter), from which bearing (degrees from north, round the up axis) and how
        /// far; "" when it isn't painted.</summary>
        public static string HudText(Transform ship)
        {
            if (ship == null || !On(ship, out var p)) return "";
            var info = p.painter.GetComponent<TargetInfo>();
            string who = Sensors.NameOf(p.painter, info != null ? info.Name : p.painter.name);
            Vector3 d = p.painter.position - ship.position;
            float bearing = (Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg + 360f) % 360f;
            return $"Targeting laser: {who}\nbearing {bearing:000}, {d.magnitude / 1000f:0.#} km";
        }

        /// <summary>Take the paint off (the painter's laser broke off: lost sight, destroyed).</summary>
        public static void Clear(Transform target)
        {
            if (target == null) return;
            worn.Clear();
            foreach (var k in paints.Keys) if (k.Item1 == target.root) worn.Add(k);
            foreach (var k in worn) paints.Remove(k);
        }
    }
}

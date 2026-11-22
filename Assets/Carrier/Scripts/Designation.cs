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

        public struct Paint
        {
            public Transform painter;
            public string faction;
            public float until;
        }

        static readonly Dictionary<Transform, Paint> paints = new Dictionary<Transform, Paint>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => paints.Clear();

        /// <summary>`painter` (of `faction`) has its laser on `target` now.</summary>
        public static void Mark(Transform painter, Transform target, string faction)
        {
            if (painter == null || target == null) return;
            paints[target.root] = new Paint { painter = painter, faction = faction ?? "", until = Time.time + Hold };
        }

        /// <summary>The paint on a target (false: none, or it has worn off, or its painter is gone).</summary>
        public static bool On(Transform target, out Paint paint)
        {
            paint = default;
            if (target == null || !paints.TryGetValue(target.root, out paint)) return false;
            if (paint.painter == null || Time.time > paint.until || (paint.painter.TryGetComponent(out Health h) && h.Dead))
            {
                paints.Remove(target.root);
                return false;
            }
            return true;
        }

        /// <summary>Is the target painted for `faction` (any faction, if null)?</summary>
        public static bool PaintedFor(Transform target, string faction) =>
            On(target, out var p) && (faction == null || p.faction == faction);

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
        public static void Clear(Transform target) { if (target != null) paints.Remove(target.root); }
    }
}

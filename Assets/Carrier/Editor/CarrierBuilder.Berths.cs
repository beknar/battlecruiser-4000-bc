using System.Collections.Generic;
using System.Linq;
using CarrierGame;
using UnityEngine;

namespace Carrier.EditorTools
{
    public static partial class CarrierBuilder
    {
        /// <summary>Berths at each installation a ship can dock at: its own, and moorings round it.</summary>
        public const int BerthsPerInstallation = 15;

        /// <summary>Moorings round every installation a ship can dock at (BerthRing).</summary>
        static void BerthRings(Transform sys, int layer)
        {
            int made = 0;
            foreach (var inst in sys.GetComponentsInChildren<Installation>(true))
            {
                if (inst == null || !inst.canDock || inst.hostile || inst.berth == null) continue;
                BerthRing(inst, layer);
                made++;
            }
            Log($"berths: {made} installations, {BerthsPerInstallation} berths each (their own and {BerthsPerInstallation - 1} moorings round them)");
        }

        /// <summary>
        /// Moorings round an installation, so that more than one ship can dock at once: a ring of
        /// them out past the station (or the rock it is built into) in its own level, each a
        /// cradle the ship lies over, on a tower, tethered back to the station by a truss, with a
        /// green light at its outer end and a red at its inner. A ship moored at one lies along
        /// the line out from the middle, her nose to the way out, so she comes in from outside the
        /// ring. The installation's own berth stays the first; the moorings are spaced round the
        /// rest of the circle.
        /// </summary>
        static void BerthRing(Installation inst, int layer)
        {
            var t = inst.transform;
            Vector3 c = t.position;
            var ob = inst.GetComponent<Obstacle>();
            float rock = ob != null ? ob.radius : 0f;
            float r0 = Vector3.Distance(inst.berth.position, c);
            // (far enough out to clear the station and its rock, and to space the ships ~500 m apart)
            float R = Mathf.Max(Mathf.Max(r0, rock) + 700f, 1150f);
            Vector3 b0 = Vector3.ProjectOnPlane(inst.berth.position - c, t.up);
            float a0 = b0.sqrMagnitude > 1f ? Mathf.Atan2(Vector3.Dot(b0, t.right), Vector3.Dot(b0, t.forward)) : 0f;
            var list = new List<Transform> { inst.berth };
            var g = new Geo(t, "Moorings");
            int n = BerthsPerInstallation - 1;
            float inner = Mathf.Max(rock * 0.85f, r0 * 0.5f, 150f);
            for (int k = 0; k < n; k++)
            {
                // (round the rest of the circle: the installation's own berth has the first place)
                float a = a0 + Mathf.PI * 2f * (k + 1) / (n + 1);
                Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                Vector3 p = dir * R;
                var rot = Quaternion.LookRotation(dir, Vector3.up);
                Vector3 cradle = p - Vector3.up * 70f;
                Vector3 foot = cradle - Vector3.up * 220f;
                g.Box(sm.dark, cradle, new Vector3(50f, 10f, 240f), rot);                                      // the cradle she lies over
                g.Box(sm.hazard, cradle + Vector3.up * 5.6f + dir * 117f, new Vector3(52f, 1.2f, 6f), rot);    // its outer edge
                g.Box(sm.hazard, cradle + Vector3.up * 5.6f - dir * 117f, new Vector3(52f, 1.2f, 6f), rot);
                g.Box(sm.dark, cradle + Vector3.up * 14f + dir * 60f, new Vector3(12f, 18f, 10f), rot);        // the clamps
                g.Box(sm.dark, cradle + Vector3.up * 14f - dir * 60f, new Vector3(12f, 18f, 10f), rot);
                g.Cyl(sm.hull, cradle - Vector3.up * 5f, foot, 9f);                                            // the tower
                g.Cyl(sm.dark, foot, foot - Vector3.up * 30f, 16f);                                            // its foot
                g.Truss(sm.hull, foot, dir * inner - Vector3.up * 290f, 14f, 1.5f);                            // tethered to the station
                var node = Node($"Berth {k + 2}", t, p, rot).transform;
                list.Add(node);
                NavLight(t, cradle + dir * 122f + Vector3.up * 8f, sm.green, 6f, 2f, k * 0.17f);
                NavLight(t, cradle - dir * 122f + Vector3.up * 8f, sm.red, 6f, 2f, k * 0.17f + 1f);
            }
            string asset = new string(inst.displayName.Where(ch => char.IsLetterOrDigit(ch)).ToArray()) + "_Moorings";
            Bake(g, asset, true, layer);
            inst.berths = list.ToArray();
        }
    }
}

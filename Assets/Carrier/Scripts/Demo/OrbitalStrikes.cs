using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// What a strike from orbit does where it lands, whether anyone is down there or not. The
    /// ground forces and the buildings of a planet are where its surface says they are
    /// (SurfaceWorld: each area's the same every time it is worked out), so a strike works out
    /// from orbit which of them are inside its blast: those ground forces are destroyed (each till
    /// it is rebuilt in its time, WorldState), a settlement's buildings ruined (rebuilt in an hour)
    /// and its people killed (fewer recruits there), and a crater is left in the ground (the
    /// ground seen from space and the surface landed on are built with it). All of it is saved.
    /// </summary>
    public static class OrbitalStrikes
    {
        public struct Result
        {
            public int units, buildings, casualties;
            public string place;
        }

        /// <summary>Minutes a ruined settlement building takes to be rebuilt.</summary>
        public const float BuildingRebuildMinutes = 60f;
        /// <summary>People killed for each building ruined.</summary>
        public const int CasualtiesPerBuilding = 12;

        /// <summary>A body's surface place (u, v) under a world point.</summary>
        public static void PlaceOf(WarpPoint body, Vector3 world, out double u, out double v)
        {
            var w = PlanetLanding.Instance.WorldFor(body);
            Vector3 local = body.transform.InverseTransformDirection(world - body.Position);
            w.FromDirection(local.sqrMagnitude > 1f ? local.normalized : Vector3.up, out u, out v);
        }

        /// <summary>The world point of a place on a body's ground (its height there included).</summary>
        public static Vector3 GroundPoint(WarpPoint body, double u, double v)
        {
            var w = PlanetLanding.Instance.WorldFor(body);
            Vector3 dir = body.transform.TransformDirection(w.Direction(u, v)).normalized;
            float h = PlanetTerrain.GroundAt(body.transform, body.Position + dir * body.bodyRadius);
            return body.Position + dir * (body.bodyRadius + h);
        }

        /// <summary>The site (u, v, the way it faces) of a named settlement or base on its body.</summary>
        public static bool SiteOf(WarpPoint place, out WarpPoint body, out TileFeatures site)
        {
            body = place != null ? place.landsOn : null;
            site = null;
            if (body == null) return false;
            var w = PlanetLanding.Instance.WorldFor(body);
            PlaceOf(body, place.Position, out double u, out double v);
            TileFeatures found = null;
            ForAreas(w, u, v, 6000f, f => { if (found == null && f.baseName == place.displayName) found = f; });
            site = found;
            return site != null;
        }

        /// <summary>Every area within `reach` of a place.</summary>
        static void ForAreas(SurfaceWorld w, double u, double v, float reach, System.Action<TileFeatures> each)
        {
            int I0 = (int)System.Math.Floor((u - reach) / SurfaceWorld.T), I1 = (int)System.Math.Floor((u + reach) / SurfaceWorld.T);
            int J0 = (int)System.Math.Floor((v - reach + w.V) / SurfaceWorld.T), J1 = (int)System.Math.Floor((v + reach + w.V) / SurfaceWorld.T);
            var seen = new HashSet<long>();
            for (int J = J0; J <= J1; J++)
                for (int I = I0; I <= I1; I++)
                {
                    w.CanonicalTile(I, J, out int ci, out int cj);
                    if (!seen.Add(((long)ci << 32) | (uint)cj)) continue;
                    var f = w.Features(ci, cj);
                    if (f != null) each(f);
                }
        }

        /// <summary>The ground forces left alive within `reach` of a place, and the nearest of them
        /// (or a named base) as a target: its place, and what it is.</summary>
        public static bool NearestTarget(WarpPoint body, double u, double v, float reach, out double tu, out double tv, out string what, out int count)
        {
            var w = PlanetLanding.Instance.WorldFor(body);
            var mem = WorldState.Body(body.displayName);
            double bu = 0, bv = 0, best = double.MaxValue;
            string name = null;
            int n = 0;
            ForAreas(w, u, v, reach, f =>
            {
                long area = (long)f.i * w.M + f.j;
                foreach (var s in f.units)
                {
                    if (mem.destroyed.Contains((area << 8) | (uint)s.id)) continue;
                    double du = SurfaceWorld.WrapDelta(s.u - u, w.C), dv = s.v - v, d = du * du + dv * dv;
                    if (d > (double)reach * reach) continue;
                    n++;
                    if (d < best) { best = d; bu = s.u; bv = s.v; name = f.baseName ?? Kind(s.kind); }
                }
            });
            tu = bu; tv = bv; what = name; count = n;
            return name != null;
        }

        static string Kind(UnitKind k)
        {
            switch (k)
            {
                case UnitKind.Battery: return "a gun battery";
                case UnitKind.MissileSite: return "a missile site";
                case UnitKind.Tank: return "hover tanks";
                case UnitKind.SeaBase: return "a sea base";
                case UnitKind.Submarine: return "submarines";
                default: return "a base";
            }
        }

        /// <summary>A strike lands on a body at (u, v): everything within `radius` of it is hit.</summary>
        public static Result Hit(WarpPoint body, double u, double v, float radius, float craterDepth, string weapon, bool seen)
        {
            var res = new Result();
            var landing = PlanetLanding.Instance;
            if (landing == null || body == null) return res;
            var w = landing.WorldFor(body);
            var mem = WorldState.Body(body.displayName);
            var units = new HashSet<long>();
            var ruined = new List<long>();
            ForAreas(w, u, v, radius + 400f, f =>
            {
                long area = (long)f.i * w.M + f.j;
                // its ground forces
                foreach (var s in f.units)
                {
                    long key = (area << 8) | (uint)s.id;
                    double du = SurfaceWorld.WrapDelta(s.u - u, w.C), dv = s.v - v;
                    if (du * du + dv * dv > (double)radius * radius || mem.destroyed.Contains(key)) continue;
                    mem.destroyed.Add(key, GroundForces.RebuildMinutesOf(s.kind));
                    units.Add(key);
                    res.units++;
                }
                // a settlement's buildings, where its layout has them (and the ground holds them up)
                var layout = f.baseName != null && f.site != SiteKind.Base ? landing.kit.Settlement(f.baseName) : null;
                if (layout == null) return;
                var turn = Quaternion.Euler(0f, f.siteYaw, 0f);
                for (int i = 0; i < layout.transform.childCount; i++)
                {
                    var c = layout.transform.GetChild(i);
                    if (c.name.StartsWith("Outpost")) continue;
                    Vector3 off = turn * c.localPosition;
                    double du = SurfaceWorld.WrapDelta(f.su + off.x - u, w.C), dv = f.sv + off.z - v;
                    long key = WorldState.BuildingKey(f.baseName, i);
                    if (du * du + dv * dv > (double)radius * radius || mem.destroyed.Contains(key)) continue;
                    if (!w.Stands(f, f.su + off.x, f.sv + off.z)) continue;
                    mem.destroyed.Add(key, BuildingRebuildMinutes);
                    ruined.Add(key);
                    res.buildings++;
                    res.place = f.baseName;
                }
            });
            if (res.buildings > 0)
            {
                res.casualties = Mathf.Min(res.buildings * CasualtiesPerBuilding, Mathf.FloorToInt(IndustryLedger.Pool(res.place)));
                IndustryLedger.Lose(res.place, res.casualties);
            }
            // the ship down on that body: what the strike hit there goes now
            if (PlanetLanding.OnSurface && landing.Body == body && landing.Surface != null)
            {
                foreach (var gu in landing.Surface.GetComponentsInChildren<GroundUnit>())
                    if (gu.Alive && units.Contains(gu.key) && gu.TryGetComponent(out Health h)) h.Damage(h.maxHealth * 50f + 1e6f, gu.transform.position, HitKind.Beam);
                foreach (var key in ruined) landing.Surface.Ruin(key);
            }
            WorldState.AddScar(body.displayName, new WorldState.Scar { u = u, v = v, radius = radius * 0.8f, depth = craterDepth, at = WorldState.Clock });
            WorldState.LogStrike(new WorldState.Strike
            {
                at = WorldState.Clock, body = body.displayName, place = res.place ?? "", weapon = weapon,
                units = res.units, buildings = res.buildings, casualties = res.casualties, seen = seen,
            });
            IndustryLedger.Touch();      // (saved soon: the craters, the losses)
            return res;
        }
    }
}

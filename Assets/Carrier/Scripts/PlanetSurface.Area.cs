using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CarrierGame
{
    /// <summary>One area of ground: its mesh, colliders, boulders, landing pads and ground forces.</summary>
    public partial class PlanetSurface
    {
        /// <summary>What Apply is doing (named in the log if it fails).</summary>
        string stage = "";

        void Apply(TileJob job)
        {
            try { ApplyNow(job); }
            catch (System.Exception e)
            {
                Debug.LogError($"[PlanetSurface] {World.name}: area {job.I},{job.J} ({job.ci},{job.cj}, detail {job.lod}) failed at '{stage}': {e}");
            }
        }

        void ApplyNow(TileJob job)
        {
            stage = "terrain";
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long key = Key(job.I, job.J);
            if (tiles.TryGetValue(key, out var old))
            {
                DestroyTile(old);
                tiles.Remove(key);
                HandOverGarrison(old);
            }
            var w = World;
            var td = new TerrainData();
            td.heightmapResolution = job.res;
            td.size = new Vector3(T, SurfaceWorld.Range, T);
            td.SetHeights(0, 0, job.norm);
            td.alphamapResolution = job.splatRes;
            td.baseMapResolution = job.lod == 0 ? 512 : 128;
            var layers = SurfaceGenerator.Layers(w.kind);
            td.terrainLayers = layers;
            td.SetAlphamaps(0, 0, job.splat);
            var go = Terrain.CreateTerrainGameObject(td);
            go.name = $"Area {job.ci},{job.cj}{(job.flipped ? " (over the pole)" : "")}{(job.lod == 0 ? "" : " (far)")}";
            go.layer = layer;
            go.transform.SetParent(transform, false);
            go.transform.position = Corner(job.I, job.J);
            var terrain = go.GetComponent<Terrain>();
            if (Kit.terrain != null) terrain.materialTemplate = Kit.terrain;
            terrain.heightmapPixelError = job.lod == 0 ? 4f : 10f;
            terrain.basemapDistance = 3000f;
            terrain.shadowCastingMode = job.lod == 0 ? ShadowCastingMode.TwoSided : ShadowCastingMode.Off;
            terrain.drawTreesAndFoliage = false;
            terrain.groupingID = 100 + job.lod;    // full-detail areas join up with each other, distant ones likewise
            terrain.allowAutoConnect = true;
            var tc = go.GetComponent<TerrainCollider>();
            bool solid = w.solid && job.lod == 0;
            if (tc != null) tc.enabled = solid;
            if (solid)
            {
                // so the hull collides with it (Cover.Active); not cover, and no core sphere for pilots
                var ob = go.AddComponent<Obstacle>();
                ob.cover = false; ob.radius = 0f; ob.coreRadius = 0f;
            }
            float hi = job.maxHeight;
            var tile = new Tile
            {
                I = job.I, J = job.J, lod = job.lod, ci = job.ci, cj = job.cj, flipped = job.flipped,
                go = go, terrain = terrain, data = td, features = job.ctx[4], maxHeight = hi,
            };
            tiles[key] = tile;
            if (job.lod == 0)
            {
                Explored.Add(job.ci * w.M + job.cj);
                stage = "furnish";
                Furnish(tile);
            }
            TilesBuilt++;
            LastJobSeconds = job.seconds;
            job.Release();
            ringDirty = true;
            LastApplyMs = (float)sw.Elapsed.TotalMilliseconds;
        }

        void DestroyTile(Tile t)
        {
            if (t.go != null) Destroy(t.go);
            if (t.data != null) Destroy(t.data);
        }

        /// <summary>Where a planet position in this area's own (canonical) coordinates is, in the
        /// world: over a pole the area is turned round.</summary>
        Vector3 Place(Tile t, double cu, double cv, out float yawTurn)
        {
            float lx = (float)SurfaceWorld.WrapDelta(cu - t.ci * (double)T, World.C);
            float lz = (float)(cv - (t.cj * (double)T - World.V));
            if (lx < -T * 0.5f) lx += (float)World.C;
            yawTurn = 0f;
            if (t.flipped) { lx = T - lx; lz = T - lz; yawTurn = 180f; }
            Vector3 c = t.go.transform.position;
            var p = new Vector3(c.x + lx, 0f, c.z + lz);
            p.y = t.terrain.transform.position.y + t.terrain.SampleHeight(p);
            return p;
        }

        /// <summary>A full-detail area's boulders, landing pad and ground forces.</summary>
        void Furnish(Tile t)
        {
            var w = World;
            var f = t.features;
            var root = t.go.transform;
            stage = "pad";
            // a settlement (a city, a military or industrial base): its own layout round its pad
            var layout = f != null && f.baseName != null && f.site != SiteKind.Base ? Layout(f.baseName) : null;
            // an industrial site: its own pad, with a refinery and a factory beside it
            var industry = f != null && f.baseName != null ? IndustryTable.Get().Site(f.baseName) : null;
            if (industry != null && (!industry.OnGround || Kit.industryPad == null)) industry = null;
            if (layout != null) BuildSettlement(t, f, layout);
            else if (industry != null) BuildIndustry(t, f, industry);
            // the landing pad (a base's launch pad on a hostile world)
            else if (f != null && f.site != SiteKind.None && Kit.outpost != null)
            {
                Vector3 p = Place(t, f.su, f.sv, out float turn);
                p.y = Center.y + f.siteHeight;
                float lift = w.kind == SurfaceKind.GasGiant ? w.platformLift : 0f;
                var o = Instantiate(Kit.outpost, root);
                o.name = f.site == SiteKind.Base ? "Outpost (hostile base)" : "Outpost";
                o.transform.SetPositionAndRotation(p + Vector3.up * (lift + SurfaceWorld.PadLift), Quaternion.Euler(0f, f.siteYaw + turn, 0f));
                foreach (var c in o.GetComponentsInChildren<Collider>(true)) c.gameObject.layer = layer;
                if (o.GetComponent<Obstacle>() == null) { var ob = o.AddComponent<Obstacle>(); ob.cover = false; ob.radius = 0f; ob.coreRadius = 0f; }
                if (f.site == SiteKind.Base)
                    foreach (Transform c in o.transform)
                        if (c.name == "Parked craft") Destroy(c.gameObject);    // room for its own ships
                t.outpost = o.transform;
                // a named base that recruits crew: landed on its pad, its screen opens (the crew page)
                if (f.baseName != null && f.site != SiteKind.Base && (CrewTable.Get().Place(f.baseName)?.Recruits ?? false))
                {
                    var port = o.AddComponent<IndustrySite>();
                    port.siteName = f.baseName;
                    port.pad = o.transform;
                    port.padRadius = SurfaceWorld.PadRadius * 0.55f;
                }
            }
            stage = "base";
            if (f != null && f.site != SiteKind.None && industry == null && layout == null) BuildBase(t, f);

            stage = "boulders";
            // boulders
            var rng = new System.Random(f != null ? (f.i * 7919 + f.j * 104729 + w.seed) : 1);
            float Rn(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            if (w.boulders > 0 && Kit.rock != null)
            {
                var rockMat = RockMaterial();
                var rocks = new GameObject("Boulders").transform;
                rocks.SetParent(root, false);
                float slopeLimit = Mathf.Atan(w.boulderSlope) * Mathf.Rad2Deg;
                for (int i = 0, tries = 0; i < w.boulders && tries < w.boulders * 4; tries++)
                {
                    double cu = t.ci * (double)T + Rn(60f, T - 60f), cv = t.cj * (double)T - w.V + Rn(60f, T - 60f);
                    if (Mathf.Abs((float)cv) > w.V - w.polarBand) continue;
                    if (f != null && f.site != SiteKind.None && Dist(cu, cv, f.su, f.sv) < (layout != null ? 1900f : industry != null ? 760f : SurfaceWorld.PadRadius + 140f)) continue;
                    bool nearUnit = false;
                    if (f != null) foreach (var us in f.units) if (Dist(cu, cv, us.u, us.v) < 60f) { nearUnit = true; break; }
                    float size = Mathf.Lerp(w.boulderSize.x, w.boulderSize.y, Mathf.Pow(Rn(0f, 1f), 2.2f));
                    float yaw = Rn(0f, 360f), flat = Rn(0.55f, 0.9f);
                    if (nearUnit) continue;
                    Vector3 p = Place(t, cu, cv, out _);
                    if (p.y < SeaLevel + 2f) continue;
                    var n = (p - t.go.transform.position) / T;
                    if (t.data.GetSteepness(Mathf.Clamp01(n.x), Mathf.Clamp01(n.z)) > slopeLimit) continue;
                    SurfaceGenerator.Boulder(rocks, p + Vector3.down * size * 0.3f, size, 600 + (i % 6), rockMat, layer, yaw, flat);
                    i++;
                }
            }
            stage = "floating rocks";
            // the rest of an asteroid field, drifting overhead
            if (w.floatingRocks > 0 && Kit.rock != null)
            {
                var rockMat = RockMaterial();
                for (int i = 0; i < w.floatingRocks; i++)
                {
                    var pos = t.go.transform.position + new Vector3(Rn(0f, T), 0f, Rn(0f, T));
                    pos.y = Center.y + Rn(1500f, 5500f);
                    SurfaceGenerator.Boulder(root, pos, Rn(60f, 380f), 620 + (i % 5), rockMat, layer, Rn(0f, 360f), Rn(0.6f, 0.95f))
                        .AddComponent<Drift>().spin = new Vector3(Rn(-2f, 2f), Rn(-2f, 2f), Rn(-2f, 2f));
                }
            }
            stage = "ground forces";
            // ground forces
            Garrison(t);
        }

        /// <summary>An industrial site (IndustryTable): its landing pad, and its refinery and factory
        /// (as it has them) on either side, facing the pad. The pad carries the IndustrySite, so a
        /// frigate landed on it can use the site.</summary>
        void BuildIndustry(Tile t, TileFeatures f, SiteSpec spec)
        {
            var root = t.go.transform;
            Vector3 p = Place(t, f.su, f.sv, out float turn);
            p.y = Center.y + f.siteHeight;
            float yaw = f.siteYaw + turn;
            var o = Instantiate(Kit.industryPad, root);
            o.name = "Outpost (industry)";
            o.transform.SetPositionAndRotation(p + Vector3.up * SurfaceWorld.PadLift, Quaternion.Euler(0f, yaw, 0f));
            foreach (var c in o.GetComponentsInChildren<Collider>(true)) c.gameObject.layer = layer;
            if (o.GetComponent<Obstacle>() == null) { var ob = o.AddComponent<Obstacle>(); ob.cover = false; ob.radius = 0f; ob.coreRadius = 0f; }
            t.outpost = o.transform;
            var site = o.AddComponent<IndustrySite>();
            site.siteName = f.baseName;
            site.padRadius = Kit.industryPadRadius;
            site.pad = new GameObject("Pad centre").transform;
            site.pad.SetParent(o.transform, false);
            site.pad.localPosition = Vector3.up * Kit.industryDeck;
            if (!World.solid) return;
            int built = 0;
            void Beside(GameObject prefab, float side)
            {
                if (prefab == null) return;
                // to one side of the pad, square to its heading
                float a = (f.siteYaw + side) * Mathf.Deg2Rad;
                float r = SurfaceWorld.PadRadius + 230f;
                double cu = f.su + Mathf.Cos(a) * r, cv = f.sv + Mathf.Sin(a) * r;
                Vector3 q = Place(t, cu, cv, out _);
                if (q.y < SeaLevel + 3f) return;
                var b = Instantiate(prefab, root);
                b.name = prefab.name;
                Vector3 toPad = p - q; toPad.y = 0f;
                b.transform.SetPositionAndRotation(q + Vector3.down * 2f, Quaternion.LookRotation(toPad.sqrMagnitude > 1f ? toPad.normalized : Vector3.forward, Vector3.up));
                foreach (var c in b.GetComponentsInChildren<Collider>(true)) c.gameObject.layer = layer;
                built++;
            }
            if (spec.CanRefine) Beside(Kit.industryRefinery, 90f);
            if (spec.CanBuild) Beside(Kit.industryFactory, -90f);
            BuildingsBuilt += built;
        }

        /// <summary>A settlement (SurfaceKit.settlements): its layout put down at the site, the pad
        /// (its "Outpost") on the flattened site and each building on the ground where it falls
        /// (none in the sea, off the area or on a steep slope). The pad is a port: landed on it,
        /// the frigate can recruit crew there, and use it as an industrial site if it is one.</summary>
        /// <summary>Settlement buildings in rubble on this surface (struck from orbit), for the self-test.</summary>
        public int Ruined { get; private set; }

        /// <summary>A heap of broken blocks where a building stood.</summary>
        void Rubble(Transform parent, Vector3 at, int seed)
        {
            var rng = new System.Random(seed * 7919 + 17);
            var heap = new GameObject("Rubble");
            heap.transform.SetParent(parent, true);
            heap.transform.position = at;
            for (int k = 0; k < 7; k++)
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(b.GetComponent<Collider>());
                b.transform.SetParent(heap.transform, false);
                float s = 6f + (float)rng.NextDouble() * 16f;
                b.transform.localScale = new Vector3(s, s * 0.45f, s * (0.6f + (float)rng.NextDouble()));
                b.transform.localPosition = new Vector3((float)rng.NextDouble() * 30f - 15f, s * 0.15f, (float)rng.NextDouble() * 30f - 15f);
                b.transform.localRotation = Quaternion.Euler((float)rng.NextDouble() * 30f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 30f);
                var r = b.GetComponent<MeshRenderer>();
                if (Kit.rubble != null) r.sharedMaterial = Kit.rubble;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        void BuildSettlement(Tile t, TileFeatures f, GameObject layout)
        {
            var root = t.go.transform;
            Vector3 p = Place(t, f.su, f.sv, out float turn);
            p.y = Center.y + f.siteHeight;
            var group = Instantiate(layout, root);
            group.name = "Settlement: " + f.baseName;
            group.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, f.siteYaw + turn, 0f));
            int built = 0;
            Transform pad = null;
            var pieces = group.transform.Cast<Transform>().ToArray();
            for (int i = 0; i < pieces.Length; i++)
            {
                var c = pieces[i];
                if (!c.name.StartsWith("Outpost") && Destroyed.Contains(WorldState.BuildingKey(f.baseName, i)))
                {
                    // (struck from orbit: rubble where it stood, till it is rebuilt)
                    Vector3 rp = c.position;
                    float rg = t.terrain.transform.position.y + t.terrain.SampleHeight(rp);
                    Rubble(group.transform, new Vector3(rp.x, rg, rp.z), i);
                    Destroy(c.gameObject);
                    Ruined++;
                    continue;
                }
                if (c.name.StartsWith("Outpost"))
                {
                    pad = c;
                    c.position = new Vector3(c.position.x, p.y + SurfaceWorld.PadLift, c.position.z);
                    continue;
                }
                // on the ground where it falls (its own sink below its pivot, kept from the layout)
                Vector3 at = c.position;
                var n = (at - t.go.transform.position) / T;
                float ground = t.terrain.transform.position.y + t.terrain.SampleHeight(at);
                bool off = n.x < 0f || n.x > 1f || n.z < 0f || n.z > 1f || ground < SeaLevel + 3f
                           || t.data.GetSteepness(Mathf.Clamp01(n.x), Mathf.Clamp01(n.z)) > 28f;
                if (off) { Destroy(c.gameObject); continue; }
                c.position = new Vector3(at.x, ground + (c.localPosition.y), at.z);
                built++;
            }
            foreach (var col in group.GetComponentsInChildren<Collider>(true)) col.gameObject.layer = layer;
            if (pad != null)
            {
                t.outpost = pad;
                if (pad.GetComponent<Obstacle>() == null) { var ob = pad.gameObject.AddComponent<Obstacle>(); ob.cover = false; ob.radius = 0f; ob.coreRadius = 0f; }
                var port = pad.gameObject.AddComponent<IndustrySite>();
                port.siteName = f.baseName;
                port.padRadius = Kit.industryPadRadius;
                port.pad = new GameObject("Pad centre").transform;
                port.pad.SetParent(pad, false);
                port.pad.localPosition = Vector3.up * Kit.industryDeck;
            }
            BuildingsBuilt += built;
        }

        /// <summary>Buildings put up round landing pads so far (for the self-test).</summary>
        public int BuildingsBuilt { get; private set; }

        /// <summary>The buildings round a site's pad: a colony (domes, habitats, a control tower,
        /// tanks, solar fields, a comms mast, a hangar) where people live, a fortress (bunkers, a
        /// command post, radar, walls, watchtowers) on a hostile world. A named base has two rings
        /// of them. They stand on the ground, never in the sea or lava, never on steep slopes, and
        /// keep clear of the ground forces' positions. Not on a gas giant's floating platforms.</summary>
        void BuildBase(Tile t, TileFeatures f)
        {
            var parts = f.site == SiteKind.Base ? Kit.fort : Kit.colony;
            if (parts == null || parts.Length == 0 || !World.solid) return;
            bool major = f.baseName != null;
            var group = new GameObject("Base: " + (f.baseName ?? (f.site == SiteKind.Base ? "hostile base" : "colony"))).transform;
            group.SetParent(t.go.transform, false);
            var rng = new System.Random(f.i * 15485863 + f.j * 32452843 + World.seed);
            float Rn(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float slopeLimit = 20f;
            int rings = major ? 2 : 1;
            int built = 0;
            for (int ring = 0; ring < rings; ring++)
            {
                int count = major ? (ring == 0 ? 8 : 12) : 5 + rng.Next(0, 3);
                float radius = SurfaceWorld.PadRadius + 90f + ring * 210f;
                for (int k = 0; k < count; k++)
                {
                    // round the pad, the first one at the pad's own heading, a little out of line
                    float a = (f.siteYaw + (k + 0.5f * ring) * 360f / count + Rn(-9f, 9f)) * Mathf.Deg2Rad;
                    float r = radius + Rn(-25f, 40f);
                    double cu = f.su + Mathf.Cos(a) * r, cv = f.sv + Mathf.Sin(a) * r;
                    if (Mathf.Abs((float)cv) > World.V - World.polarBand) continue;
                    bool nearUnit = false;
                    foreach (var us in f.units) if (Dist(cu, cv, us.u, us.v) < 75f) { nearUnit = true; break; }
                    if (nearUnit) continue;
                    Vector3 p = Place(t, cu, cv, out float turn);
                    if (p.y < SeaLevel + 3f) continue;
                    var n = (p - t.go.transform.position) / T;
                    if (n.x < 0f || n.x > 1f || n.z < 0f || n.z > 1f) continue;
                    if (t.data.GetSteepness(n.x, n.z) > slopeLimit) continue;
                    // every kind once before any repeats (a named base has them all)
                    var prefab = parts[(k + ring * count + (major ? 0 : rng.Next(0, parts.Length))) % parts.Length];
                    if (prefab == null) continue;
                    var b = Instantiate(prefab, group);
                    b.name = prefab.name;
                    // facing the pad (its front, +Z, toward the middle)
                    Vector3 toPad = new Vector3(-Mathf.Cos(a), 0f, -Mathf.Sin(a));
                    if (t.flipped) toPad = -toPad;
                    b.transform.SetPositionAndRotation(p + Vector3.down * 1.5f, Quaternion.LookRotation(toPad, Vector3.up));
                    foreach (var c in b.GetComponentsInChildren<Collider>(true)) c.gameObject.layer = layer;
                    built++;
                }
            }
            BuildingsBuilt += built;
        }

        /// <summary>Put an area's ground forces in it, unless another built copy of the same area
        /// (over a pole) already has them.</summary>
        void Garrison(Tile t)
        {
            var f = t.features;
            if (f == null || f.units.Count == 0 || t.go == null) return;
            int area = t.ci * World.M + t.cj;
            if (garrisonOf.TryGetValue(area, out var holder) && holder != t && holder.go != null) return;
            garrisonOf[area] = t;
            t.garrison = GroundForces.Deploy(this, t.go.transform, t.outpost, f, (cu, cv) => { var p = Place(t, cu, cv, out float turn); return (p, turn); },
                                             (long)area << 8);
        }

        /// <summary>The copy of an area that held its forces has gone: if another copy of it is
        /// still built in full, the forces (those not destroyed) move there.</summary>
        void HandOverGarrison(Tile gone)
        {
            int area = gone.ci * World.M + gone.cj;
            if (!garrisonOf.TryGetValue(area, out var holder) || holder != gone) return;
            garrisonOf.Remove(area);
            foreach (var t in tiles.Values)
                if (t != gone && t.lod == 0 && t.ci == gone.ci && t.cj == gone.cj) { Garrison(t); break; }
        }

        static float Dist(double a, double b, double c, double d) => (float)System.Math.Sqrt((a - c) * (a - c) + (b - d) * (b - d));

        Material rockMatCache;
        Material RockMaterial()
        {
            if (rockMatCache != null) return rockMatCache;
            rockMatCache = new Material(Kit.rock);
            rockMatCache.SetColor("_BaseColor", World.rockTint);
            owned.Add(rockMatCache);
            return rockMatCache;
        }
    }
}

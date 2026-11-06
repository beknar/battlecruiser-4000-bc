using CarrierGame;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Carrier.EditorTools
{
    /// <summary>
    /// Mining: the frigate's mining laser (a turret under the bow), mineral sensor and cargo hold;
    /// and the comets of the star system, each an icy nucleus in a glowing coma with a straight
    /// blue ion tail, a curved dust tail and a trail of debris, all turned away from the star.
    /// </summary>
    public static partial class CarrierBuilder
    {
        // ------------------------------------------------------------------ textures and materials

        /// <summary>A soft line: bright along the middle, fading to nothing at both edges (for
        /// line renderers stretched along their length).</summary>
        static Texture2D SoftLineTexture()
        {
            const int W = 16, H = 64;
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
            {
                float v = (y + 0.5f) / H * 2f - 1f;
                float k = Mathf.Exp(-v * v * 9f) + 0.6f * Mathf.Exp(-v * v * 60f);
                for (int x = 0; x < W; x++) px[y * W + x] = new Color(k, k, k) / 1.6f;
            }
            return SaveTexture("T_SoftLine", px, W, H, false, true);
        }

        /// <summary>A soft, slightly clumpy puff of gas or dust (black outside).</summary>
        static Texture2D SoftPuffTexture()
        {
            const int N = 128;
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float n = ProcNoise.Fbm(new Vector3(dx * 2.5f, dy * 2.5f, 0.5f), 4321, 4) * 0.5f + 0.5f;
                    float k = Mathf.Clamp01(1f - r);
                    float v = Mathf.Pow(k, 2.2f) * (0.65f + 0.7f * n);
                    px[y * N + x] = new Color(v, v, v);
                }
            return SaveTexture("T_SoftPuff", px, N, N, false, true);
        }

        static void MiningMaterials()
        {
            var line = SoftLineTexture();
            var puff = SoftPuffTexture();
            ParticleAdditive("FX_MiningBeam", line, new Color(4f, 2.3f, 0.8f));
            ParticleAdditive("FX_MiningGlow", line, new Color(1.5f, 0.75f, 0.25f));
            ParticleAdditive("FX_SensorRing", line, new Color(0.45f, 1.7f, 1.4f));
            ParticleAdditive("FX_MiningSpark", Load<Texture2D>(ArtDir + "/flare_01.png"), new Color(2.2f, 1.4f, 0.6f));
            ParticleAdditive("FX_OreStream", puff, new Color(1.6f, 1.4f, 1.1f));
            ParticleAdditive("FX_CometComa", puff, new Color(0.2f, 0.27f, 0.38f));
            ParticleAdditive("FX_CometIon", puff, new Color(0.08f, 0.2f, 0.6f));
            ParticleAdditive("FX_CometDust", puff, new Color(0.3f, 0.25f, 0.17f));
            Lit("Mining_Lens", new Color(0.2f, 0.12f, 0.06f), metallic: 0.2f, smooth: 0.9f, emission: new Color(2.4f, 1.3f, 0.4f));
        }

        static LineRenderer Line(string name, Transform parent, Material mat, int points, bool loop)
        {
            var lr = Node(name, parent).AddComponent<LineRenderer>();
            lr.sharedMaterial = mat;
            lr.positionCount = points;
            lr.loop = loop;
            lr.useWorldSpace = true;
            lr.textureMode = LineTextureMode.Stretch;
            lr.alignment = LineAlignment.View;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.widthMultiplier = 2f;
            lr.startColor = lr.endColor = Color.white;
            lr.enabled = false;
            return lr;
        }

        // ------------------------------------------------------------------ the frigate's mining gear

        /// <summary>The cargo hold, the mineral sensor, and the mining laser: a small turret on
        /// the hull's belly behind the bow gun. Returns the turret (it breaks off with the hull).</summary>
        static Transform AddMiningGear(GameObject root, MeshCollider hullCol)
        {
            MiningMaterials();
            var hold = root.AddComponent<CargoHold>();
            hold.capacity = 600f;

            var sensorGo = Node("MineralSensor", root.transform);
            var sensor = sensorGo.AddComponent<MineralSensor>();
            sensor.spaceRange = 12000f;
            sensor.surfaceRange = 5000f;
            sensor.sweepTime = 2f;
            sensor.showFor = 60f;
            sensor.ringFlat = Line("PulseFlat", sensorGo.transform, Mats["FX_SensorRing"], 96, true);
            sensor.ringUpright = Line("PulseUpright", sensorGo.transform, Mats["FX_SensorRing"], 96, true);

            // the belly, a little behind the bow gun
            var gun = root.GetComponentInChildren<MainBeam>();
            float z = gun != null ? gun.transform.localPosition.z - 24f : 64f;
            float belly = -12f;
            var from = root.transform.TransformPoint(new Vector3(0f, -300f, z));
            if (hullCol != null && hullCol.Raycast(new Ray(from, root.transform.up), out var hit, 600f))
                belly = root.transform.InverseTransformPoint(hit.point).y;
            else Log("mining laser: belly not found, using a default spot");

            var node = Node("MiningLaser", root.transform, new Vector3(0f, belly + 0.6f, z));
            node.transform.localScale = Vector3.one * 2f;   // big enough to see on a 180 m hull
            GameObject Part(string name, PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Quaternion rot, string mat)
            {
                var go = GameObject.CreatePrimitive(type);
                StripColliders(go);
                go.name = name;
                go.transform.SetParent(parent, false);
                go.transform.localPosition = pos;
                go.transform.localRotation = rot;
                go.transform.localScale = scale;
                var r = go.GetComponent<Renderer>();
                r.sharedMaterial = Mats[mat];
                r.shadowCastingMode = ShadowCastingMode.Off;
                return go;
            }
            Part("Mount", PrimitiveType.Cylinder, node.transform, new Vector3(0f, -0.6f, 0f), new Vector3(4.4f, 0.6f, 4.4f), Quaternion.identity, "Hull_Dark");
            var head = Node("Head", node.transform, new Vector3(0f, -2.4f, 0f), Quaternion.Euler(25f, 0f, 0f));
            Part("Housing", PrimitiveType.Sphere, head.transform, Vector3.zero, new Vector3(3.2f, 2.6f, 3.6f), Quaternion.identity, "Hull_Dark");
            Part("Barrel", PrimitiveType.Cylinder, head.transform, new Vector3(0f, 0f, 2.6f), new Vector3(1.1f, 1.8f, 1.1f), Quaternion.Euler(90f, 0f, 0f), "Hull_Main");
            Part("Lens", PrimitiveType.Sphere, head.transform, new Vector3(0f, 0f, 4.4f), new Vector3(0.9f, 0.9f, 0.5f), Quaternion.identity, "Mining_Lens");
            var muzzle = Node("Muzzle", head.transform, new Vector3(0f, 0f, 4.8f)).transform;

            var laser = node.AddComponent<MiningLaser>();
            laser.range = 1500f;
            laser.rate = 8f;
            laser.head = head.transform;
            laser.muzzle = muzzle;
            laser.beamGlow = Line("BeamGlow", node.transform, Mats["FX_MiningGlow"], 2, false);
            laser.beamCore = Line("BeamCore", node.transform, Mats["FX_MiningBeam"], 2, false);

            // sparks thrown back off the rock where the beam cuts
            var sparks = Ps("CutSparks", node.transform, Mats["FX_MiningSpark"], 1500, true);
            {
                var m = sparks.main;
                m.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.9f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(25f, 110f);
                m.startSize = new ParticleSystem.MinMaxCurve(1.2f, 3.5f);
                m.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 0.4f), new Color(1f, 0.95f, 0.8f));
                var sh = sparks.shape;
                sh.shapeType = ParticleSystemShapeType.Cone;
                sh.angle = 55f;
                sh.radius = 3f;
                FadeInOut(sparks, 0.05f);
                var em = sparks.emission;
                em.rateOverTime = 450f;
                em.enabled = false;
            }
            // the ore: a stream of glowing grit drawn back up the beam into the ship
            var stream = Ps("OreStream", node.transform, Mats["FX_OreStream"], 2500, true);
            {
                var m = stream.main;
                m.startLifetime = 1f;   // set from the beam's length
                m.startSpeed = 500f;
                m.startSize = new ParticleSystem.MinMaxCurve(1.5f, 4f);
                var sh = stream.shape;
                sh.shapeType = ParticleSystemShapeType.Cone;
                sh.angle = 2.5f;
                sh.radius = 5f;
                FadeInOut(stream, 0.15f);
                var em = stream.emission;
                em.rateOverTime = 260f;
                em.enabled = false;
            }
            laser.impact = sparks;
            laser.collect = stream;
            var light = Node("Glow", muzzle, new Vector3(0f, 0f, 3f)).AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.65f, 0.3f);
            light.range = 140f;
            light.intensity = 0f;
            light.shadows = LightShadows.None;
            laser.glow = light;
            laser.glowIntensity = 2500f;
            Log($"mining laser on the belly at {node.transform.localPosition:F1}; cargo hold {hold.capacity:F0} t; " +
                $"mineral sensor {sensor.spaceRange / 1000f:F0} km in space, {sensor.surfaceRange / 1000f:F0} km on a surface");
            return node.transform;
        }

        // ------------------------------------------------------------------ comets

        /// <summary>A comet at `pos`: nucleus, coma, ion tail, dust tail and a debris trail, the
        /// tails `tailLength` long (the dust tail shorter), dimmer when `activity` is low (far
        /// from the star). `orbitDir` is the way it is going, which the dust tail curves away from.</summary>
        static WarpPoint MakeComet(string name, Transform parent, Vector3 pos, Vector3 orbitDir, float radius, float tailLength, float activity,
                                   int seed, Transform star, Material nucleusMat, Material iceMat, int layer, string desc)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.position = pos;
            var rng = new System.Random(seed);
            float R01() => (float)rng.NextDouble();

            // the nucleus: a dark, lumpy, potato-shaped lump of dirty ice
            var nucleus = MakeAsteroid("Nucleus", root, pos, Quaternion.Euler(R01() * 360f, R01() * 360f, R01() * 360f), 1f, seed, 5, 3,
                new Vector3(radius, radius * 0.72f, radius * 0.86f), 0.32f, 18, radius / 5f, nucleusMat, layer, radius * 1.15f, radius * 0.7f);
            nucleus.GetComponent<MineralDeposit>().body = BodyClass.Comet;
            nucleus.GetComponent<Obstacle>().cover = true;
            nucleus.AddComponent<Drift>().spin = new Vector3(0.4f, 1.1f, 0.2f);

            var comet = root.gameObject.AddComponent<Comet>();
            comet.star = star;
            comet.orbitDirection = orbitDir.normalized;
            var tails = Node("Tails", root).transform;
            tails.rotation = Comet.TailRotation(pos, star.position, orbitDir);
            comet.tails = tails;

            // the coma: a glowing haze of gas and dust round the nucleus
            var coma = Ps("Coma", root, Mats["FX_CometComa"], 600, false);
            {
                var m = coma.main;
                m.duration = 10f;
                m.prewarm = true;
                m.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(radius * 0.05f, radius * 0.18f);
                m.startSize = new ParticleSystem.MinMaxCurve(radius * 1.5f, radius * 3f);
                m.startColor = new Color(1f, 1f, 1f, 0.12f + 0.12f * activity);
                m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                var sh = coma.shape;
                sh.shapeType = ParticleSystemShapeType.Sphere;
                sh.radius = radius * 1.4f;
                FadeInOut(coma, 0.3f);
                var csz = coma.sizeOverLifetime;
                csz.enabled = true;
                csz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.8f));
                var em = coma.emission;
                em.rateOverTime = 14f;
            }

            // the ion tail: straight, blue, streaming directly away from the star at speed
            float ionLife = 24f;
            var ion = Ps("IonTail", tails, Mats["FX_CometIon"], 3500, false);
            {
                var m = ion.main;
                m.duration = ionLife;
                m.prewarm = true;
                m.startLifetime = new ParticleSystem.MinMaxCurve(ionLife * 0.8f, ionLife);
                m.startSpeed = new ParticleSystem.MinMaxCurve(tailLength / ionLife * 0.9f, tailLength / ionLife * 1.1f);
                m.startSize = new ParticleSystem.MinMaxCurve(radius * 0.5f, radius * 0.9f);
                m.startColor = new Color(1f, 1f, 1f, 0.4f + 0.4f * activity);
                m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                var sh = ion.shape;
                sh.shapeType = ParticleSystemShapeType.Cone;
                sh.angle = 1.2f;
                sh.radius = radius * 0.5f;
                var sz = ion.sizeOverLifetime;
                sz.enabled = true;
                sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 4.5f));
                var col = ion.colorOverLifetime;
                col.enabled = true;
                col.color = Fade(new[] { 0f, 1f, 0.6f, 0f }, new[] { 0f, 0.04f, 0.55f, 1f });
                // streamers: the solar wind ripples the plasma
                var nz = ion.noise;
                nz.enabled = true;
                nz.strength = new ParticleSystem.MinMaxCurve(tailLength * 0.0035f);
                nz.frequency = 6f / tailLength;   // ripples a few kilometres across
                nz.scrollSpeed = 0.05f;
                nz.quality = ParticleSystemNoiseQuality.Low;
                var em = ion.emission;
                em.rateOverTime = 22f + 38f * activity;
            }

            // the dust tail: broader, yellow-white, slower, curving back along the orbit
            float dustLife = 40f, dustLength = tailLength * 0.7f;
            float speed = dustLength / dustLife;
            float bend = 0.28f / dustLength;   // the curve: x = bend * z^2 (a quarter of its length aside at the end)
            var dust = Ps("DustTail", tails, Mats["FX_CometDust"], 5000, false);
            {
                var m = dust.main;
                m.duration = dustLife;
                m.prewarm = true;
                m.startLifetime = new ParticleSystem.MinMaxCurve(dustLife * 0.75f, dustLife);
                m.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.85f, speed * 1.1f);
                m.startSize = new ParticleSystem.MinMaxCurve(radius * 0.6f, radius * 1.1f);
                m.startColor = new Color(1f, 1f, 1f, 0.35f + 0.35f * activity);
                m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                var sh = dust.shape;
                sh.shapeType = ParticleSystemShapeType.Cone;
                sh.angle = 5f;
                sh.radius = radius * 0.9f;
                var force = dust.forceOverLifetime;
                force.enabled = true;
                force.space = ParticleSystemSimulationSpace.Local;
                float ax = 2f * bend * speed * speed;   // x = a t^2 / 2 with z = v t gives x = (a / 2v^2) z^2
                force.x = new ParticleSystem.MinMaxCurve(ax);
                force.y = new ParticleSystem.MinMaxCurve(0f);
                force.z = new ParticleSystem.MinMaxCurve(0f);
                var sz = dust.sizeOverLifetime;
                sz.enabled = true;
                sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 7f));
                var col = dust.colorOverLifetime;
                col.enabled = true;
                col.color = Fade(new[] { 0f, 1f, 0.55f, 0f }, new[] { 0f, 0.05f, 0.6f, 1f });
                var em = dust.emission;
                em.rateOverTime = 22f + 38f * activity;
            }
            comet.coma = coma;
            comet.ionTail = ion;
            comet.dustTail = dust;

            // the debris trail: lumps of ice and grit along the start of the dust tail's curve
            var debris = Node("Debris", root).transform;
            int count = 30;
            float near = radius * 2.2f, far = Mathf.Min(dustLength * 0.3f, 14000f);
            int placed = 0;
            var spots = new System.Collections.Generic.List<(Vector3 p, float r)>();
            for (int attempt = 0; attempt < count * 30 && placed < count; attempt++)
            {
                float along = Mathf.Lerp(near, far, Mathf.Pow(R01(), 1.4f));
                float spread = 120f + along * 0.06f;
                Vector3 local = Comet.DustCurve(along, bend) + new Vector3((R01() * 2f - 1f) * spread, (R01() * 2f - 1f) * spread * 0.6f, (R01() * 2f - 1f) * spread);
                Vector3 wp = pos + tails.rotation * local;
                float t = R01();
                float size = t < 0.7f ? Mathf.Lerp(8f, 35f, R01()) : t < 0.95f ? Mathf.Lerp(35f, 80f, R01()) : Mathf.Lerp(90f, 150f, R01());
                bool clear = true;
                foreach (var (sp, sr) in spots) if (Vector3.Distance(sp, wp) < (sr + size) * 1.6f) { clear = false; break; }
                if (!clear) continue;
                spots.Add((wp, size));
                var axes = new Vector3(1f, Mathf.Lerp(0.55f, 0.9f, R01()), Mathf.Lerp(0.7f, 1f, R01()));
                var lump = MakeAsteroid("Ice", debris, wp, Quaternion.Euler(R01() * 360f, R01() * 360f, R01() * 360f), size,
                    seed + 50 + placed % 6, 3, 2, axes, 0.3f, 4, 0.45f, iceMat, layer, 1.3f, 0.5f);
                lump.GetComponent<MineralDeposit>().body = BodyClass.CometDebris;
                lump.AddComponent<Drift>().spin = new Vector3(R01() * 6f - 3f, R01() * 6f - 3f, R01() * 6f - 3f);
                placed++;
            }

            var wpnt = MakeWarpPoint(root.gameObject, name, WarpPoint.Kind.Comet, desc, radius, radius * 4f + 4500f, new Color(0.7f, 0.92f, 1f));
            Log($"{name}: nucleus {radius:F0} m, tails {tailLength / 1000f:F0} km (dust {dustLength / 1000f:F0} km) pointing " +
                $"{Vector3.Angle(tails.forward, pos - star.position):F1} deg from straight away from the star, {placed} lumps of debris out to {far / 1000f:F1} km");
            return wpnt;
        }
    }
}

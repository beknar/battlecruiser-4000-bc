using System.Collections.Generic;
using System.Linq;
using CarrierGame;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Carrier.EditorTools
{
    /// <summary>
    /// Ways off the planets and moons, each where it suits the world:
    ///
    /// - Thalassa Space Elevator: Thalassa turns fast enough for a geostationary orbit 10 km
    ///   out, so a tether runs from a sea platform on the equator up to the Highport there, and
    ///   on to a counterweight, with climbers riding it.
    /// - Thalassa Launch Loop: a loop of cable held up by its own moving rotor, an elevated track
    ///   along the equator from a station at each end; payloads ride along the top and are let go.
    /// - Ochre StarTram: a maglev tube on the desert floor that rises in a long curve out of the
    ///   thin air, held up magnetically and tethered down; capsules leave its mouth at speed.
    /// - Ochre Spin Launcher: a vacuum centrifuge whose arm spins a payload up and lets it go
    ///   into a launch tube.
    /// - Selene Mass Driver: an electromagnetic catapult (a maglev track of coils) across the
    ///   airless moon, flinging ore buckets off into space.
    /// - Steropes Slingatron: a spiral track on a platform on Jovara's largest moon; payloads
    ///   spiral outward, faster and faster, and leave along the exit ramp.
    ///
    /// Each turns with its body (a child of it), launches payloads (LaunchSystem) and is a warp
    /// destination arrived at from above, with a landing pad by it on the surface.
    /// </summary>
    public static partial class CarrierBuilder
    {
        static Material payloadMat;

        /// <summary>A launcher's frame: at the body's centre, turning with it, its +Y up through
        /// the site and its +Z along the track's way (a great circle in its YZ plane).</summary>
        static Transform BodyFrame(Transform body, string name, Vector3 radial, Vector3 along)
        {
            var go = new GameObject(name);
            go.transform.SetParent(body, false);
            go.transform.localScale = Vector3.one / Mathf.Max(1e-6f, body.lossyScale.x);
            along = Vector3.ProjectOnPlane(along, radial).normalized;
            go.transform.SetPositionAndRotation(body.position, Quaternion.LookRotation(along, radial.normalized));
            return go.transform;
        }

        /// <summary>A point `h` above the surface (radius R), `theta` radians along the frame's great circle.</summary>
        static Vector3 Arc(float R, float theta, float h, float x = 0f) =>
            new Vector3(x, (R + h) * Mathf.Cos(theta), (R + h) * Mathf.Sin(theta));

        static Quaternion ArcRot(float theta) => Quaternion.AngleAxis(theta * Mathf.Rad2Deg, Vector3.right);

        /// <summary>The launcher as a destination: a point on the surface at `theta`, arrived at from above.</summary>
        static WarpPoint LauncherSite(Transform frame, float R, float theta, WarpPoint body, string name, string label, string desc, float arrival, float side = 0.15f)
        {
            var site = Node(name, frame, Arc(R, theta, 0f), ArcRot(theta));
            var wp = Destination(site, name, WarpPoint.Kind.SurfaceBase, desc, 400f, arrival, FriendlyWorks, body, false, label);
            wp.landsOn = body;
            wp.overhead = true;
            wp.overheadSide = side;
            wp.surface = body.surface;
            Facility(site, name, WarpPoint.Kind.SurfaceBase, false);
            return wp;
        }

        static LaunchSystem Launcher(Transform frame, List<Vector3> track, float trackTime, float exitSpeed, float interval, float first)
        {
            var ls = frame.gameObject.AddComponent<LaunchSystem>();
            ls.track = track.ToArray();
            ls.trackTime = trackTime;
            ls.exitSpeed = exitSpeed;
            ls.interval = interval;
            ls.firstLaunch = first;
            ls.payloadMesh = Prim(PrimitiveType.Capsule);
            ls.payloadMaterial = payloadMat;
            ls.trailMaterial = Mats["FX_MissileTrail"];
            return ls;
        }

        static void BuildLaunchers(SystemRefs s, Built b)
        {
            payloadMat = Lit("Launch_Payload", new Color(0.9f, 0.95f, 1f), metallic: 0.3f, smooth: 0.6f, emission: new Color(0.5f, 0.85f, 1.2f) * 6f);
            var sys = s.sys;
            WarpPoint W(string n) => sys.GetComponentsInChildren<WarpPoint>(true).First(w => w.displayName == n);
            void Shot(string name, WarpPoint wp, float up, float sideways)
            {
                Vector3 u = wp.transform.up, r = wp.transform.right;
                b.shots.Add(($"launch_{name}", wp.Position + u * up + r * sideways + wp.transform.forward * sideways * 0.4f, wp.Position));
            }

            // ---------------------------------------------------------------- Thalassa
            var th = s.thalassa.transform;
            var wThalassa = s.wThalassa;
            float RT = wThalassa.bodyRadius;
            var highport = sys.GetComponentsInChildren<Installation>(true).First(i => i.displayName == "Thalassa Highport").transform;
            Vector3 radial = (highport.position - th.position).normalized;
            Vector3 east = Vector3.Cross(th.up, radial).normalized;
            {
                var f = BodyFrame(th, "Thalassa Space Elevator", radial, th.up);
                float geo = Vector3.Distance(highport.position, th.position), top = geo - 240f, cw = 13500f;
                var solid = new Geo(f, "Platform and counterweight");
                solid.Box(sm.dark, new Vector3(0f, RT + 8f, 0f), new Vector3(280f, 16f, 280f));
                solid.Box(sm.hull, new Vector3(0f, RT + 24f, 0f), new Vector3(120f, 18f, 120f));
                solid.Box(sm.window, new Vector3(0f, RT + 26f, 60.3f), new Vector3(100f, 5f, 1f));
                foreach (var (x, z) in new[] { (-110f, -110f), (110f, -110f), (110f, 110f), (-110f, 110f) })
                    solid.Cyl(sm.hull, new Vector3(x, RT - 40f, z), new Vector3(x, RT + 2f, z), 10f);
                solid.Cyl(sm.hazard, new Vector3(0f, RT + 33f, 0f), new Vector3(0f, RT + 70f, 0f), 26f);
                solid.Box(sm.dark, new Vector3(0f, cw + 70f, 0f), new Vector3(170f, 140f, 170f));
                solid.Box(sm.hull, new Vector3(0f, cw + 150f, 0f), new Vector3(120f, 20f, 120f));
                Bake(solid, "Elevator_Platform", true, s.layer);
                var tether = new Geo(f, "Tether");
                tether.Cyl(sm.dark, new Vector3(0f, RT + 70f, 0f), new Vector3(0f, top, 0f), 7f);
                tether.Cyl(sm.dark, new Vector3(0f, geo + 670f, 0f), new Vector3(0f, cw, 0f), 7f);
                for (float y = RT + 400f; y < cw; y += 380f)
                    if (y < top - 50f || y > geo + 700f) tether.Box(sm.work, new Vector3(0f, y, 0f), new Vector3(20f, 36f, 20f));
                Bake(tether, "Elevator_Tether", false, s.layer, false);
                NavLight(f, new Vector3(0f, cw + 165f, 0f), sm.red, 16f, 1.6f, 0f);
                NavLight(f, new Vector3(0f, RT + 74f, 0f), sm.red, 10f, 1.6f, 0.5f);
                // the climbers
                var cg = new Geo(f, "Climber");
                cg.Box(sm.hull, Vector3.zero, new Vector3(34f, 56f, 34f));
                cg.Box(sm.window, new Vector3(0f, 6f, 17.3f), new Vector3(28f, 6f, 0.6f));
                cg.Box(sm.window, new Vector3(0f, 6f, -17.3f), new Vector3(28f, 6f, 0.6f));
                cg.Box(sm.dark, new Vector3(0f, 32f, 0f), new Vector3(40f, 8f, 40f));
                var climber = Bake(cg, "Elevator_Climber", false, s.layer);
                var climbers = new List<Transform> { climber.transform };
                for (int i = 1; i < 4; i++) climbers.Add(Object.Instantiate(climber, f).transform);
                var ec = f.gameObject.AddComponent<ElevatorClimbers>();
                ec.bottom = new Vector3(0f, RT + 110f, 0f);
                ec.top = new Vector3(0f, top - 60f, 0f);
                ec.climbers = climbers.ToArray();
                ec.speed = 110f;
                var wp = LauncherSite(f, RT, 0f, wThalassa, "Thalassa Space Elevator", "Space elevator",
                    "A space elevator: Thalassa turns once in 11 minutes, so an orbit 10 km from its centre keeps pace with the ground. " +
                    "A tether runs from a sea platform on the equator up to the Highport there, and on to a counterweight beyond, " +
                    "and climbers ride it up and down.", 1500f, 0.9f);
                Shot("elevator", wp, 1800f, 3500f);
                b.shots.Add(("launch_elevator_whole", th.position + radial * 9000f + east * 16000f + th.up * 3000f, th.position + radial * 9500f));
            }
            {
                // the launch loop, on the far side of the equator
                Vector3 r2 = -radial;
                var f = BodyFrame(th, "Thalassa Launch Loop", r2, Vector3.Cross(th.up, r2));
                const float H = 350f, end = 0.35f, ramp = 0.07f;
                var g = new Geo(f, "Loop");
                var path = new List<Vector3>();
                Vector3 Loop(float t)
                {
                    float a = Mathf.Abs(t);
                    float h = a <= end - ramp ? H : Mathf.Lerp(H, 12f, Mathf.SmoothStep(0f, 1f, (a - (end - ramp)) / ramp));
                    return Arc(RT, t, h);
                }
                for (float t = -end; t < end; t += 0.01f) g.Rod(sm.hull, Loop(t), Loop(Mathf.Min(end, t + 0.01f)), 9f);
                for (float t = -end + ramp; t <= end - ramp; t += 0.04f)
                {
                    g.Rod(sm.dark, Loop(t), Arc(RT, t, 0f), 1.6f);       // the cables holding it down
                    g.Box(sm.work, Loop(t) + Arc(RT, t, 0f).normalized * 8f, new Vector3(12f, 6f, 12f), ArcRot(t));
                }
                foreach (float t in new[] { -end, end })
                {
                    g.Box(sm.hull, Arc(RT, t, 30f), new Vector3(220f, 60f, 170f), ArcRot(t));
                    g.Box(sm.window, Arc(RT, t, 34f) + ArcRot(t) * new Vector3(110.4f, 0f, 0f), new Vector3(1f, 6f, 150f), ArcRot(t));
                }
                Bake(g, "LaunchLoop", true, s.layer);
                for (float t = -end; t <= end - ramp * 0.6f; t += 0.01f) path.Add(Loop(t));
                Launcher(f, path, 12f, 1300f, 24f, 4f);
                var wp = LauncherSite(f, RT, 0f, wThalassa, "Thalassa Launch Loop", "Launch loop",
                    "A launch loop: a loop of iron cable running at faster than orbital speed inside a sheath, which holds itself up " +
                    "350 m over the sea between two stations 4.5 km apart. Payloads ride the top on magnets and are let go at the east end.", 2600f);
                Shot("loop", wp, 2200f, 1500f);
            }

            // ---------------------------------------------------------------- Ochre
            var oc = s.ochre.transform;
            var wOchre = oc.GetComponent<WarpPoint>();
            float RO = wOchre.bodyRadius;
            Vector3 sun = (s.star.position - oc.position).normalized;
            {
                var f = BodyFrame(oc, "Ochre StarTram", sun, Vector3.Cross(Vector3.up, sun));
                var g = new Geo(f, "Tube");
                var path = new List<Vector3>();
                Vector3 Tram(float t) => Arc(RO, t, t < 0.2f ? 14f : 14f + 900f * Mathf.Pow((t - 0.2f) / 0.28f, 2f));
                for (float t = 0f; t < 0.48f; t += 0.008f)
                {
                    g.Cyl(sm.hull, Tram(t), Tram(t + 0.008f), 11f);
                    path.Add(Tram(t));
                }
                path.Add(Tram(0.48f));
                for (float t = 0.02f; t < 0.48f; t += 0.03f) g.Torus(sm.dark, Tram(t), Tram(t + 0.001f) - Tram(t), 13f, 2f);
                for (float t = 0.22f; t < 0.48f; t += 0.035f) g.Rod(sm.dark, Tram(t), Arc(RO, t + 0.01f, -10f), 1.4f);   // tethers
                g.Box(sm.hull, Arc(RO, -0.02f, 20f), new Vector3(140f, 40f, 160f), ArcRot(-0.02f));
                g.Box(sm.window, Arc(RO, -0.02f, 24f) + ArcRot(-0.02f) * new Vector3(70.4f, 0f, 0f), new Vector3(1f, 6f, 130f), ArcRot(-0.02f));
                Bake(g, "StarTram", true, s.layer);
                NavLight(f, Tram(0.48f) + Tram(0.48f).normalized * 16f, sm.red, 8f, 1.5f, 0f);
                Launcher(f, path, 10f, 1500f, 22f, 6f);
                var wp = LauncherSite(f, RO, 0.25f, wOchre, "Ochre StarTram", "StarTram (maglev launch tube)",
                    "A StarTram: an evacuated maglev tube 1 km along the desert floor, then rising in a long curve to leave Ochre's thin " +
                    "air 900 m up, held up magnetically and tethered down. Capsules leave its mouth at 1.5 km/s.", 2600f);
                Shot("startram", wp, 1800f, 2000f);
            }
            {
                Vector3 r = Quaternion.AngleAxis(48f, oc.up) * sun;
                var f = BodyFrame(oc, "Ochre Spin Launcher", r, Vector3.Cross(Vector3.up, r));
                var g = new Geo(f, "Centrifuge");
                g.Cyl(sm.hull, new Vector3(0f, RO - 10f, 0f), new Vector3(0f, RO + 45f, 0f), 75f);
                g.Torus(sm.dark, new Vector3(0f, RO + 45f, 0f), Vector3.up, 75f, 4f);
                g.Box(sm.dark, new Vector3(-110f, RO + 12f, 0f), new Vector3(60f, 30f, 50f));
                g.Box(sm.window, new Vector3(-80f, RO + 16f, 0f), new Vector3(1f, 5f, 40f));
                Vector3 t0 = new Vector3(0f, RO + 46f, 60f), t1 = new Vector3(0f, RO + 330f, 150f);
                g.Cyl(sm.dark, t0, t1, 7f);
                for (float k = 0.25f; k < 1f; k += 0.25f)
                {
                    Vector3 p = Vector3.Lerp(t0, t1, k);
                    g.Rod(sm.hull, p, new Vector3(p.x - 30f, RO - 5f, p.z + 30f), 2f);
                    g.Rod(sm.hull, p, new Vector3(p.x + 30f, RO - 5f, p.z + 30f), 2f);
                }
                Bake(g, "SpinLauncher", true, s.layer);
                var arm = Node("Arm", f, new Vector3(0f, RO + 47f, 0f));
                var ag = new Geo(arm.transform, "Mesh");
                ag.Box(sm.hull, Vector3.zero, new Vector3(140f, 4f, 10f));
                ag.Box(sm.dark, new Vector3(68f, 0f, 0f), new Vector3(10f, 8f, 14f));
                ag.Box(sm.dark, new Vector3(-68f, 0f, 0f), new Vector3(10f, 8f, 14f));
                ag.Cyl(sm.hazard, new Vector3(0f, -3f, 0f), new Vector3(0f, 3f, 0f), 9f);
                Bake(ag, "SpinLauncher_Arm", false, s.layer);
                var ls = Launcher(f, new List<Vector3> { new Vector3(0f, RO + 48f, 0f), t0, Vector3.Lerp(t0, t1, 0.05f), t1 }, 1.1f, 1900f, 20f, 9f);
                ls.spinner = arm.transform;
                ls.spinnerRate = 540f;
                var wp = LauncherSite(f, RO, 0f, wOchre, "Ochre Spin Launcher", "Spin launcher (centrifuge)",
                    "A spin launcher: a 150 m vacuum chamber in which an arm whirls a payload round at thousands of km/h, then lets it go " +
                    "up a slanting tube and out through Ochre's thin air.", 2000f);
                Shot("spin", wp, 900f, 900f);
            }

            // ---------------------------------------------------------------- Selene
            {
                var wSelene = s.wSelene;
                var sel = wSelene.transform;
                float RS = wSelene.bodyRadius;
                var baseT = sys.GetComponentsInChildren<Installation>(true).First(i => i.displayName == "Selene Base").transform;
                Vector3 baseDir = (baseT.position - sel.position).normalized;
                // 45 degrees round from the base: still in daylight (Selene doesn't turn), clear of it
                Vector3 r = (Quaternion.AngleAxis(-45f, Vector3.up) * baseDir).normalized;
                var f = BodyFrame(sel, "Selene Mass Driver", r, Vector3.up);
                var g = new Geo(f, "Track");
                var path = new List<Vector3>();
                const float a = 0.6f, h = 95f;
                for (float t = -a; t < a; t += 0.02f)
                {
                    foreach (float x in new[] { -7f, 7f }) g.Rod(sm.dark, Arc(RS, t, h, x), Arc(RS, t + 0.02f, h, x), 3f);
                    path.Add(Arc(RS, t, h + 6f));
                }
                path.Add(Arc(RS, a, h + 6f));
                for (float t = -a; t <= a; t += 0.05f) g.Torus(sm.hull, Arc(RS, t, h + 6f), Arc(RS, t + 0.001f, h + 6f) - Arc(RS, t, h + 6f), 15f, 2.5f);
                for (float t = -a; t <= a; t += 0.1f) g.Rod(sm.hull, Arc(RS, t, h - 2f), Arc(RS, t, -90f), 4f);
                g.Box(sm.hull, Arc(RS, -a - 0.04f, 40f), new Vector3(90f, 60f, 110f), ArcRot(-a - 0.04f));
                g.Box(sm.window, Arc(RS, -a - 0.04f, 50f) + ArcRot(-a - 0.04f) * new Vector3(45.4f, 0f, 0f), new Vector3(1f, 6f, 90f), ArcRot(-a - 0.04f));
                Bake(g, "MassDriver", false, s.layer);
                Launcher(f, path, 5f, 1100f, 15f, 2f);
                var wp = LauncherSite(f, RS, 0f, wSelene, "Selene Mass Driver", "Mass driver (maglev catapult)",
                    "A mass driver: an electromagnetic catapult, a 1.7 km maglev track of coils on pylons across the airless moon. " +
                    "It flings buckets of ore off Selene to the Highport's smelters, one every 15 seconds.", 2200f);
                Shot("massdriver", wp, 1500f, 1200f);
            }

            // ---------------------------------------------------------------- Steropes
            {
                var wSt = W("Steropes");
                var st = wSt.transform;
                float RS = wSt.bodyRadius;
                Vector3 r = (s.jovara.transform.position - st.position).normalized;
                var f = BodyFrame(st, "Steropes Slingatron", r, Vector3.up);
                var g = new Geo(f, "Slingatron");
                float y = RS + 40f;
                g.Disc(sm.dark, new Vector3(0f, RS + 30f, 0f), Vector3.up, 380f, 10f);
                for (int k = 0; k < 6; k++)
                {
                    float q = k * Mathf.PI / 3f;
                    g.Cyl(sm.hull, new Vector3(Mathf.Cos(q) * 300f, RS - 120f, Mathf.Sin(q) * 300f), new Vector3(Mathf.Cos(q) * 300f, RS + 26f, Mathf.Sin(q) * 300f), 12f);
                    g.Box(sm.hull, new Vector3(Mathf.Cos(q) * 360f, RS + 50f, Mathf.Sin(q) * 360f), new Vector3(30f, 40f, 30f), Quaternion.Euler(0f, -q * Mathf.Rad2Deg, 0f));
                }
                g.Cyl(sm.hull, new Vector3(0f, RS + 30f, 0f), new Vector3(0f, RS + 70f, 0f), 26f);
                var path = new List<Vector3>();
                const float turns = 2.5f;
                float phiEnd = turns * Mathf.PI * 2f;
                Vector3 Spiral(float phi) { float rr = 30f + 300f * phi / phiEnd; return new Vector3(rr * Mathf.Cos(phi), y, rr * Mathf.Sin(phi)); }
                for (float phi = 0f; phi < phiEnd; phi += 0.1f)
                {
                    g.Rod(sm.hazard, Spiral(phi), Spiral(Mathf.Min(phiEnd, phi + 0.1f)), 5f);
                    path.Add(Spiral(phi));
                }
                Vector3 last = Spiral(phiEnd), dir = (Spiral(phiEnd) - Spiral(phiEnd - 0.05f)).normalized;
                Vector3 exit = last + dir * 160f + Vector3.up * 80f;
                g.Rod(sm.hazard, last, exit, 6f);
                g.Rod(sm.hull, exit, new Vector3(exit.x, RS + 26f, exit.z), 4f);
                path.Add(last);
                path.Add(exit);
                Bake(g, "Slingatron", true, s.layer);
                Launcher(f, path, 8f, 1000f, 20f, 5f);
                var wp = LauncherSite(f, RS, 0f, wSt, "Steropes Slingatron", "Slingatron (spiral launcher)",
                    "A slingatron: a spiral track 700 m across on a platform on Jovara's largest moon. The whole spiral gyrates, " +
                    "and payloads run round it outward, faster each turn, and leave along the ramp at 1 km/s.", 2200f);
                Shot("slingatron", wp, 1500f, 700f);
            }
            Log("launchers: Thalassa space elevator and launch loop, Ochre StarTram and spin launcher, Selene mass driver, Steropes slingatron");
        }
    }
}

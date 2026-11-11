using System;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The Star System feature reel: StarSystem.exe -reel &lt;folder&gt; -features. It shows, in
    /// order: mining (an asteroid in the Inner Belt, Comet Iskra, a vein in Ochre's desert);
    /// combat (a pirate ambush in the Outer Drift, ground forces on the Jovara Trojans, the
    /// dreadnought battle group at Glacis); landing (down onto Thalassa's pad, then lava, ice,
    /// cloud tops and a moon); and a run of mini-warps across the system, ending back at Thalassa.
    /// Between scenes the picture fades through black and recording pauses, so the frigate can
    /// be moved (or fly a long approach) off camera. Each section has a caption.
    /// </summary>
    public partial class DemoReel
    {
        /// <summary>Recording the feature reel (-features).</summary>
        public static bool Features;

        bool recording = true, finished;
        float fade;                       // 0 clear, 1 black (the sound fades with it)
        Action perFrame;                  // steering and camera work run from Update
        string capTitle, capSub;
        float capAt = -100f, capFor;
        Health hullF;
        Shield shieldF;

        /// <summary>Seconds into the finished reel.</summary>
        float R => recorded / (float)Fps;
        float D => demo.ShipViewDistance;

        void Note(string s) => Log($"{R,6:F1}s  {s}");

        IEnumerator FeatureReel()
        {
            recording = false;
            while (frame < Warmup) yield return null;
            landing = PlanetLanding.Instance;
            sysDrive = cc.GetComponent<WarpDrive>();
            hullF = cc.GetComponent<Health>();
            shieldF = cc.GetComponentInChildren<Shield>(true);
            if (hullF != null) hullF.cannotDie = true;     // the reel must not end in a wreck
            SideGuns(false);
            Note($"feature reel: {MineralDeposit.All.Count} bodies with minerals, {WarpPoint.All.Count} places");

            yield return Intro();
            yield return MineAsteroid();
            yield return MineComet();
            yield return MineVein();
            yield return Ambush();
            yield return GroundForces();
            yield return CapitalShips();
            yield return Landings();
            yield return Warps();

            ReportSystems();
            Note($"the end: warp trips {sysDrive.Jumps}, landings {landing.Landings}, mined {demo.Cargo.TotalMined:F0} t");
            finished = true;
        }

        // ------------------------------------------------------------------ helpers

        IEnumerator FadeOut(float s = 0.5f)
        {
            for (float k = 0f; k < s; k += Time.deltaTime) { fade = k / s; yield return null; }
            fade = 1f;
            yield return null;
            recording = false;
            capTitle = null;
        }

        IEnumerator FadeIn(float s = 0.5f)
        {
            fade = 1f;
            recording = true;
            for (float k = 0f; k < s; k += Time.deltaTime) { fade = 1f - k / s; yield return null; }
            fade = 0f;
        }

        static WaitForSeconds Wait(float s) => new WaitForSeconds(s);

        void Caption(string title, string sub, float seconds)
        {
            capTitle = title;
            capSub = sub;
            capAt = Time.time;
            capFor = seconds;
            Note($"caption: {title} / {sub}");
        }

        /// <summary>Put the ship somewhere (off camera), facing a point, stopped.</summary>
        void Place(Vector3 pos, Vector3 facing, bool spawn = true)
        {
            Clear();
            cc.transform.position = pos;
            Vector3 d = facing - pos;
            cc.SetAttitude(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg);
            cc.SetMotion(0f);
            cc.throttle = 0f;
            if (spawn) cc.SetSpawn(pos, cc.transform.rotation);
        }

        /// <summary>To where a warp from `from` (or from here) would drop the ship.</summary>
        void Jump(WarpPoint wp, Vector3? from = null) => Place(wp.ArrivalPoint(from ?? cc.transform.position), wp.Position);

        /// <summary>Hands off the controls, the laser off, the map and cargo list closed.</summary>
        void Clear()
        {
            perFrame = null;
            demo.autoRudder = null; demo.autoPitch = null; demo.autoElevator = null;
            if (demo.Laser != null && demo.Laser.On) demo.Laser.TurnOff();
            if (MiningDisplay.ManifestOpen) demo.ToggleManifest();
            if (SystemMap.IsOpen) sysMap.Close();
        }

        void Repair()
        {
            if (hullF != null) hullF.Repair();
            if (shieldF != null) shieldF.SetOffline(false);
        }

        /// <summary>The camera behind the ship, looking past it at a point.</summary>
        void LookPast(int index, Vector3 p, float distance, float extraYaw = 0f, float pitch = float.NaN, float spin = 0f)
        {
            Vector3 d = p - cc.transform.position;
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg + extraYaw;
            float pt = float.IsNaN(pitch) ? Mathf.Clamp(-Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg * 0.7f + 6f, -35f, 45f) : pitch;
            Shot(index, cc.transform, demo.ShipViewOffset, distance, yaw, pt, spin);
        }

        /// <summary>The camera on a target, looking along `look`.</summary>
        void ShotAlong(int index, Transform target, Vector3 offset, Vector3 look, float distance, float spin = 0f)
        {
            float yaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(look.y, new Vector2(look.x, look.z).magnitude) * Mathf.Rad2Deg;
            Shot(index, target, offset, distance, yaw, pitch, spin);
        }

        /// <summary>Of two camera headings, the one looking less into the star (its glare
        /// would wash the picture out).</summary>
        float AwayFromStar(float yawA, float yawB, float pitch)
        {
            var star = WP("Kestrel");
            if (star == null) return yawA;
            Vector3 toStar = (star.Position - cc.transform.position).normalized;
            float a = Vector3.Dot(Quaternion.Euler(pitch, yawA, 0f) * Vector3.forward, toStar);
            float b = Vector3.Dot(Quaternion.Euler(pitch, yawB, 0f) * Vector3.forward, toStar);
            return a <= b ? yawA : yawB;
        }

        /// <summary>From the side of the mining beam (the side away from the star), far enough
        /// back to take in the ship and where it lands.</summary>
        void BeamSide(int index, Vector3 hit, float pitch = 12f)
        {
            Vector3 beam = hit - cc.transform.position;
            float along = Mathf.Atan2(beam.x, beam.z) * Mathf.Rad2Deg;
            float yaw = AwayFromStar(along + 90f, along - 90f, pitch);
            Shot(index, cc.transform, demo.ShipViewOffset + beam * 0.4f, Mathf.Max(D * 1.3f, beam.magnitude * 1.25f), yaw, pitch, 2f);
        }

        IEnumerator WaitLanding(PlanetLanding.State want, float seconds)
        {
            float until = Time.time + seconds;
            while (landing.Current != want && Time.time < until) yield return null;
        }

        IEnumerator TakeOffNow()
        {
            if (landing.Current == PlanetLanding.State.OnSurface) landing.TakeOff();
            yield return WaitLanding(PlanetLanding.State.InSpace, 15f);
        }

        static Vector3 ClearSpot(Vector3 p, Vector3 back)
        {
            for (int i = 0; i < 30 && Cover.Mask != 0 && Physics.CheckSphere(p, 180f, Cover.Mask, QueryTriggerInteraction.Ignore); i++)
                p += back * 120f + Vector3.up * 40f;
            return p;
        }

        void OnGUI()
        {
            if (!Features) return;
            GUI.depth = -1000;
            float s = Hud.S;
            if (capTitle != null)
            {
                float age = Time.time - capAt;
                float a = Mathf.Clamp01(age / 0.6f) * Mathf.Clamp01((capFor - age) / 0.8f);
                if (a > 0.01f)
                {
                    var big = Hud.Text(34, true);
                    var small = Hud.Text(20);
                    var c1 = new GUIContent(capTitle);
                    var c2 = new GUIContent(capSub ?? "");
                    Vector2 s1 = big.CalcSize(c1), s2 = small.CalcSize(c2);
                    // just above the ship's console (bottom left), or near the bottom without one
                    float bottom = ShipConsole.Rect.height > 0f ? ShipConsole.Rect.yMin - 30f * s : Screen.height - 150f * s;
                    float x = 44f * s, y = bottom - s1.y - s2.y;
                    float w = Mathf.Max(s1.x, s2.x);
                    var old = GUI.color;
                    GUI.color = new Color(1f, 1f, 1f, a);
                    Hud.Panel(new Rect(x - 18f * s, y - 12f * s, w + 36f * s, s1.y + s2.y + 24f * s));
                    Hud.Fill(new Rect(x - 18f * s, y - 12f * s, 4f * s, s1.y + s2.y + 24f * s), new Color(1f, 0.72f, 0.3f, a));
                    GUI.color = new Color(1f, 1f, 1f, a);
                    GUI.Label(new Rect(x, y, s1.x, s1.y), c1, big);
                    GUI.Label(new Rect(x, y + s1.y + 2f * s, s2.x, s2.y), c2, small);
                    GUI.color = old;
                }
            }
            if (fade > 0.001f) Hud.Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, fade));
        }

        // ------------------------------------------------------------------ the scenes

        IEnumerator Intro()
        {
            var home = WP("Thalassa");
            cc.throttle = 0.15f;
            LookPast(300, home.Position, D * 2.2f, 25f, float.NaN, 3f);
            yield return Wait(1f);
            yield return FadeIn(1f);
            Caption("BATTLECRUISER 4000 BC", introLine ?? "Star System: mining, combat, landing and mini-warps", 5.5f);
            yield return Wait(5.5f);
            yield return FadeOut();
        }

        // ---------------------------------------------------- mining

        IEnumerator MineAsteroid()
        {
            var belt = WP("Inner Belt");
            var laser = demo.Laser;
            var sensor = demo.Sensor;
            Jump(belt);
            yield return Wait(1.5f);

            // the scan: the pulse sweeping out through the field, then the finds marked
            var kestrel = WP("Kestrel");
            float away = kestrel != null ? Bearing(kestrel.Position, cc.transform.position) : cc.Heading;
            Shot(200, cc.transform, demo.ShipViewOffset, D * 3f, away, 30f, 3f);
            yield return FadeIn();
            Caption("MINING", "A mineral scan of the Inner Belt", 6f);
            yield return Wait(0.6f);
            demo.MineralScan();
            float t0 = Time.time;
            perFrame = () => cam.distance = Mathf.Lerp(D * 3f, D * 7f, Smooth((Time.time - t0) / 2.4f));
            yield return Wait(2.6f);
            perFrame = null;
            Note($"scan: {sensor.Finds.Count} deposits within {sensor.ScanRange / 1000f:F0} km");
            LookPast(201, belt.Position, D * 1.3f, -12f, 7f);
            yield return Wait(3.4f);
            yield return FadeOut();

            // a small rock: cut until it breaks up
            var rock = sensor.Finds.Select(f => f.source).OfType<MineralDeposit>()
                .Where(d => d.Valid && !d.Depleted && d.body == BodyClass.Asteroid && d.Radius > 14f && d.Radius <= 38f)
                .OrderBy(d => Vector3.Distance(d.Position, cc.transform.position)).FirstOrDefault()
                ?? MineralDeposit.All.Where(d => d != null && d.Valid && d.body == BodyClass.Asteroid && d.Radius <= 38f)
                .OrderBy(d => Vector3.Distance(d.Position, cc.transform.position)).FirstOrDefault();
            if (rock == null) { Note("no rock to mine"); yield break; }
            Vector3 dir = (rock.Position - cc.transform.position).normalized;
            Place(ClearSpot(rock.Position - dir * (rock.Radius + 520f), -dir), rock.Position);
            yield return Wait(1f);
            demo.ToggleMiningLaser();
            yield return Wait(0.8f);
            if (laser.Source is MineralDeposit cut && cut != rock) { Note($"the laser chose another rock ({cut.Radius:F0} m)"); rock = cut; }
            Note($"mining {rock.Label}, {rock.Radius:F0} m, {rock.Remaining:F0} t: {laser.Status}");
            BeamSide(202, laser.Cutting ? laser.HitPoint : rock.Position);
            yield return FadeIn();
            Caption("MINING", $"Cutting ore from an asteroid: {rock.Label}", 5f);
            yield return Wait(5f);

            // on the rock: the cut glowing, ore streaming off it
            Vector3 me = cc.transform.position;
            Shot(203, rock.transform, Vector3.zero, rock.Radius * 6f + 160f, Bearing(me, rock.Position) + 55f, 10f, 4f);
            yield return Wait(4f);

            // the ship from ahead, the beam reaching out, the cargo list filling
            demo.ToggleManifest();
            Shot(204, cc.transform, demo.ShipViewOffset, D * 1.5f, Bearing(rock.Position, me) - 35f, 12f, 3f);
            int out0 = laser.DepositsMinedOut;
            float until = Time.time + 30f;
            bool onRock = false;
            while (laser.DepositsMinedOut == out0 && Time.time < until)
            {
                // the last few seconds: back on the rock to see it break up
                if (!onRock && rock.Remaining < laser.rate * 2.5f)
                {
                    onRock = true;
                    Shot(205, rock.transform, Vector3.zero, rock.Radius * 9f + 260f, Bearing(me, rock.Position) + 70f, 12f, 3f);
                }
                yield return null;
            }
            Note($"the rock {(rock.Valid ? "is still there" : "broke up")}; hold {demo.Cargo.Used:F0} t");
            yield return Wait(2.6f);
            yield return FadeOut();
            Clear();
        }

        IEnumerator MineComet()
        {
            var wp = WP("Comet Iskra");
            var comet = FindObjectsByType<Comet>(FindObjectsSortMode.None).FirstOrDefault(c => c.name == "Comet Iskra");
            if (wp == null || comet == null) { Note("no Comet Iskra"); yield break; }
            var laser = demo.Laser;
            Jump(wp);
            yield return Wait(1.5f);

            // the whole comet from above: the coma, the straight ion tail, the curved dust tail
            Vector3 tail = comet.tails.forward;
            Vector3 look = -(comet.tails.up * 60000f - comet.tails.right * 8000f);
            ShotAlong(210, comet.transform, tail * 20000f, look, look.magnitude);
            float t0 = Time.time;
            float dist0 = look.magnitude;
            perFrame = () => cam.distance = Mathf.Lerp(dist0, dist0 * 0.8f, Smooth((Time.time - t0) / 6f));
            yield return FadeIn();
            Caption("MINING", "Comet Iskra: its ion and dust tails stream away from the star", 6f);
            yield return Wait(5.5f);
            perFrame = null;
            yield return FadeOut();

            // in close to the nucleus: ice and frozen gases
            var nucleus = comet.GetComponentsInChildren<MineralDeposit>().FirstOrDefault(d => d.body == BodyClass.Comet);
            if (nucleus == null) { Note("the comet has no nucleus deposit"); yield break; }
            Vector3 side = Vector3.Cross(tail, Vector3.up).normalized;
            Vector3 outward = (side - tail * 0.35f).normalized;
            Place(ClearSpot(nucleus.Position + outward * (nucleus.Radius + 600f), outward), nucleus.Position);
            yield return Wait(1f);
            demo.ToggleMiningLaser();
            yield return Wait(0.8f);
            Note($"comet: {laser.Status}");
            BeamSide(211, laser.Cutting ? laser.HitPoint : nucleus.Position, 10f);
            yield return FadeIn();
            Caption("MINING", "Water ice, frozen gases and deuterium from the nucleus", 5f);
            yield return Wait(5f);
            Vector3 me = cc.transform.position;
            Shot(212, cc.transform, demo.ShipViewOffset, D * 1.4f, Bearing(me, nucleus.Position) - 30f, 9f, 2f);
            yield return Wait(4f);
            yield return FadeOut();
            Clear();
        }

        IEnumerator MineVein()
        {
            var ochre = WP("Ochre");
            var laser = demo.Laser;
            var sensor = demo.Sensor;
            if (ochre == null || !landing.LandOn(ochre)) { Note("couldn't land on Ochre"); yield break; }
            yield return WaitLanding(PlanetLanding.State.OnSurface, 60f);
            yield return Wait(2f);
            var surf = landing.Surface;

            // the scan, out across the desert
            Shot(220, cc.transform, demo.ShipViewOffset, D * 3.2f, cc.Heading + 150f, 30f, 3f);
            yield return FadeIn();
            Caption("MINING", "Ochre: scanning the desert for veins of ore", 5.5f);
            yield return Wait(0.5f);
            demo.MineralScan();
            yield return Wait(2.6f);
            var vein = sensor.Finds.Select(f => f.source).OfType<SurfaceVein>().FirstOrDefault(v => v.Valid && !v.Depleted);
            Note($"surface scan: {sensor.Finds.Count} veins; nearest {(vein != null ? vein.Label : "none")}");
            if (vein != null) LookPast(221, vein.Position, D * 1.5f, -10f, 14f);
            yield return Wait(3f);
            yield return FadeOut();
            if (vein == null) { yield return TakeOffNow(); yield break; }

            // over the vein, the laser cutting into the ground
            Vector3 vp = vein.Position;
            Vector3 flat = vp - cc.transform.position;
            flat.y = 0f;
            Vector3 back = flat.sqrMagnitude > 1f ? -flat.normalized : Vector3.back;
            Vector3 spot = vp + back * 420f;
            float until = Time.time + 20f;
            while (!surf.Ready(spot) && Time.time < until) yield return null;
            spot.y = Mathf.Max(surf.FloorAt(spot), vp.y) + 260f;
            Place(spot, new Vector3(vp.x, spot.y, vp.z), false);   // level, hovering
            yield return Wait(1.5f);
            vp = vein.Position;
            demo.ToggleMiningLaser();
            yield return Wait(0.8f);
            Note($"vein: {laser.Status}, {laser.Reach(vein):F0} m");
            BeamSide(222, laser.Cutting ? laser.HitPoint : vp, 8f);
            yield return FadeIn();
            Caption("MINING", $"Cutting a vein in Ochre's desert floor: {vein.Label}", 5.5f);
            yield return Wait(5.5f);

            // the cargo list: what it has brought aboard from three kinds of body
            demo.ToggleManifest();
            Shot(223, cc.transform, demo.ShipViewOffset, D * 1.5f, Bearing(cc.transform.position, vp) + 30f, 22f, 2f);
            yield return Wait(4.5f);
            Note($"hold: {demo.Cargo.Used:F0} t worth {demo.Cargo.Value:N0} cr");
            yield return FadeOut();
            Clear();
            yield return TakeOffNow();
        }

        // ---------------------------------------------------- combat

        /// <summary>The Outer Drift ambush, played by the star system shot list's own scene.</summary>
        IEnumerator Ambush()
        {
            var drift = WP("Outer Drift");
            if (drift == null) yield break;
            Repair();
            Jump(drift);
            yield return Wait(1f);
            SideGuns(false);
            Phase(5, T);
            perFrame = () => SystemShots(T);
            yield return Wait(0.2f);
            yield return FadeIn();
            Caption("COMBAT", "A pirate ambush in the Outer Drift asteroid field", 5.5f);
            float until = Time.time + 90f;
            bool skipped = false;
            while (sysPhase == 5 && Time.time < until)
            {
                // closing on the cruiser from far off: cut most of the way in
                if (!skipped && pirateCruiser && T - phaseAt > 21f && demo.MainGun != null && demo.MainGun.Current == MainBeam.State.Idle &&
                    Vector3.Distance(cc.transform.position, pirateCruiser.position) > 7500f)
                {
                    skipped = true;
                    yield return FadeOut(0.4f);
                    float stop = Time.time + 30f;
                    while (pirateCruiser && Vector3.Distance(cc.transform.position, pirateCruiser.position) > 6600f && Time.time < stop) yield return null;
                    Note($"cut in to {Vector3.Distance(cc.transform.position, pirateCruiser.position) / 1000f:F1} km of the cruiser");
                    yield return FadeIn(0.4f);
                }
                yield return null;
            }
            perFrame = null;
            Note(sysPhase == 6 ? "the pirate cruiser is down" : "the ambush ran out of time");
            Clear();
            SideGuns(false);
            Shot(240, cc.transform, demo.ShipViewOffset, D * 1.4f, cc.Heading + 60f, 16f, 6f);
            float t0 = Time.time;
            perFrame = () => cam.distance = Mathf.Lerp(D * 1.4f, D * 3.2f, Smooth((Time.time - t0) / 4f));
            yield return Wait(4f);
            yield return FadeOut();
            Clear();
        }

        /// <summary>Down among the Jovara Trojans' ground forces (the star system scene's own battle).</summary>
        IEnumerator GroundForces()
        {
            var trojans = WP("Jovara Trojans");
            if (trojans == null) yield break;
            Repair();
            // from just off the field, so taking off brings it back out there
            Jump(trojans);
            yield return Wait(0.5f);
            if (!landing.LandOn(trojans)) { Note("couldn't land on the Trojans"); yield break; }
            yield return WaitLanding(PlanetLanding.State.OnSurface, 60f);
            Phase(9, T);
            perFrame = () => SystemShots(T);
            yield return Wait(0.2f);
            yield return FadeIn();
            Caption("COMBAT", "Ground forces on the Jovara Trojans: a gun battery, missile sites, fighters", 6f);
            float until = Time.time + 80f;
            while (!leaving && sysPhase == 9 && Time.time < until) yield return null;
            // stop the old shot list here: next it would open the map for its own next scene
            perFrame = null;
            yield return Wait(2f);
            yield return FadeOut();
            Clear();
            SideGuns(false);
            yield return WaitLanding(PlanetLanding.State.InSpace, 15f);
        }

        IEnumerator CapitalShips()
        {
            var glacis = WP("Glacis");
            var dread = GameObject.Find("Pirate Dreadnought")?.transform;
            var fleet = GameObject.Find("Dreadnought battle group")?.GetComponent<EncounterGroup>();
            if (glacis == null || dread == null) { Note("no dreadnought at Glacis"); yield break; }
            Repair();
            // as if from Thalassa: the battle group waits on that way in
            Jump(glacis, WP("Thalassa").Position);
            // (the planets have moved since the group took up its post on that line: come in on
            // the dreadnought from the arrival side, 8 km out)
            Vector3 toShip = cc.transform.position - dread.position;
            if (toShip.magnitude > 9000f) Place(dread.position + toShip.normalized * 8000f, dread.position);
            else Place(cc.transform.position, dread.position);
            yield return Wait(1f);
            demo.SelectTarget(dread);
            SideGuns(true);
            perFrame = () =>
            {
                var v = ShipAutopilot.BowOn(cc, dread.position);
                demo.autoRudder = v.x;
                demo.autoPitch = v.y;
                cc.throttle = Vector3.Distance(cc.transform.position, dread.position) > 5200f ? 0.6f : 0f;
            };
            var dh = dread.GetComponent<Health>();
            float took0 = dh != null ? dh.TotalDamage : 0f;
            LookPast(230, dread.position, D * 1.8f, 14f, 6f);
            yield return FadeIn();
            Caption("COMBAT", "The pirate dreadnought and its escorts at Glacis", 5.5f);
            Note($"dreadnought {Vector3.Distance(cc.transform.position, dread.position) / 1000f:F1} km away");
            yield return Wait(4.5f);
            Note($"battle group attacking {fleet != null && fleet.Active}");

            // the dreadnought's guns, with the frigate beyond
            float bd = Bearing(cc.transform.position, dread.position);
            Shot(231, dread, Vector3.zero, 2600f, AwayFromStar(bd + 150f, bd - 150f, 9f), 9f, 3f);
            yield return Wait(5f);
            // the frigate's shield taking hits
            Shot(232, cc.transform, demo.ShipViewOffset, D * 1.3f, Bearing(dread.position, cc.transform.position) + 35f, 10f, 2f);
            yield return Wait(4f);

            // the bow gun on the dreadnought
            var gun = demo.MainGun;
            float until = Time.time + 20f;
            while (Time.time < until && gun != null && !(gun.Current == MainBeam.State.Idle &&
                   Vector3.Distance(cc.transform.position, dread.position) < 6000f &&
                   Vector3.Angle(cc.transform.forward, dread.position - cc.transform.position) < 6f)) yield return null;
            if (gun != null && gun.Current == MainBeam.State.Idle)
            {
                demo.SelectTarget(dread);
                demo.ChargeMainGun();
                Note($"bow gun charging on the dreadnought, {Vector3.Distance(cc.transform.position, dread.position) / 1000f:F1} km");
                Shot(233, gun.transform, Vector3.zero, 75f, cc.Heading + 150f, 8f, 0f);
                while (gun.Current == MainBeam.State.Charging && gun.ChargeTimeLeft > 3.5f)
                {
                    cam.yaw = Mathf.LerpAngle(cam.yaw, cc.Heading + 150f, Time.deltaTime * 2f);
                    yield return null;
                }
                Shot(234, cc.transform, demo.ShipViewOffset, D * 0.85f, cc.Heading, 9f, 0f);
                while (gun.Current == MainBeam.State.Charging) { FollowHeading(0f, 9f); yield return null; }
                Shot(235, dread, Vector3.zero, 1900f, Bearing(cc.transform.position, dread.position) + 70f, 8f, 0f);
                while (gun.Current == MainBeam.State.Firing) yield return null;
                yield return Wait(1.5f);
            }
            Note($"the dreadnought took {(dh != null ? dh.TotalDamage - took0 : 0f):F0}; frigate hull {hullF.Current:F0}, shield {(shieldF != null ? shieldF.Fraction : 0f):P0}");
            Shot(236, cc.transform, demo.ShipViewOffset, D * 1.4f, cc.Heading + 60f, 16f, 6f);
            float t0 = Time.time;
            var steer = perFrame;
            perFrame = () => { steer(); cam.distance = Mathf.Lerp(D * 1.4f, D * 4f, Smooth((Time.time - t0) / 4f)); };
            yield return Wait(4f);
            yield return FadeOut();
            SideGuns(false);
            Clear();
        }

        // ---------------------------------------------------- landing

        IEnumerator Landings()
        {
            var home = WP("Thalassa");
            Repair();
            Jump(home);
            // the drive and the landing gear stay locked a little while after a fight
            float until = Time.time + 25f;
            while (!landing.CanLand(out _, out _) && Time.time < until) yield return null;
            cc.throttle = 0.12f;
            LookPast(250, home.Position, D * 1.8f, 30f, float.NaN, 3f);
            yield return FadeIn();
            Caption("LANDING", "Thalassa: down to an island landing pad", 6f);
            yield return Wait(1.5f);
            bool down = landing.Land() || landing.LandOn(home);
            Note($"landing on Thalassa: {down}");
            yield return WaitLanding(PlanetLanding.State.OnSurface, 60f);
            var site = landing.Site;
            if (site != null)
            {
                // in over the island toward the pad
                Shot(251, cc.transform, demo.ShipViewOffset, D * 1.5f, cc.Heading + 150f, 11f, 2f);
                perFrame = () =>
                {
                    Vector3 f = site.pad - cc.transform.position;
                    f.y = 0f;
                    var v = ShipAutopilot.BowOn(cc, site.pad + Vector3.up * (cc.transform.position.y - site.pad.y));
                    demo.autoRudder = f.magnitude > 60f ? v.x : 0f;
                    demo.autoPitch = 0f;
                    cc.throttle = f.magnitude > 380f ? 0.6f : 0.2f;
                    demo.autoElevator = cc.transform.position.y - site.pad.y > 130f ? -0.7f : 0f;
                };
                yield return Wait(5.5f);
                yield return FadeOut();
                // then hovering over it, and down onto it
                Vector3 over = site.pad + Vector3.up * 80f;
                Place(over, over + cc.transform.forward * 100f, false);
                perFrame = () => { demo.autoElevator = -1f; demo.autoRudder = 0f; demo.autoPitch = 0f; cc.throttle = 0f; };
                Shot(252, cc.transform, demo.ShipViewOffset, D * 1.2f, cc.Heading + 75f, 6f, 1.5f);
                yield return Wait(0.6f);
                yield return FadeIn();
                until = Time.time + 25f;
                while (!landing.Landed && Time.time < until) yield return null;
                Note($"touchdown on Thalassa: landed {landing.Landed}, on the pad {landing.OnPad}");
                Shot(253, cc.transform, demo.ShipViewOffset, D * 1.9f, cc.Heading - 140f, 18f, 5f);
                yield return Wait(4f);
            }
            perFrame = null;
            Clear();
            cc.throttle = 0f;
            yield return FadeOut();
            yield return TakeOffNow();

            // and elsewhere: lava, ice, cloud tops, a moon
            (string name, string sub)[] worlds =
            {
                ("Cinder", "Cinder: lava fields and cinder cones"),
                ("Glacis", "Glacis: ice ridges and crevasses"),
                ("Jovara", "Jovara: a gas giant's cloud tops, with a floating mining platform"),
                ("Selene", "Selene: a cratered moon, its planet in the sky"),
            };
            int n = 0;
            foreach (var (name, sub) in worlds)
            {
                var wp = WP(name);
                if (wp == null || !landing.LandOn(wp)) { Note($"couldn't land on {name}"); continue; }
                yield return WaitLanding(PlanetLanding.State.OnSurface, 60f);
                yield return Wait(1.5f);
                var surf = landing.Surface;
                site = landing.Site;
                perFrame = () =>
                {
                    if (surf != null) HoldHeight(surf, 200f);
                    demo.autoPitch = 0f;
                    demo.autoRudder = 0f;
                    cc.throttle = 0.45f;
                };
                Shot(260 + n * 2, cc.transform, demo.ShipViewOffset, D * 2.3f, cc.Heading + 140f, 9f, 4f);
                yield return FadeIn();
                Caption("LANDING", sub, 5f);
                Note($"on {name}: {site?.description}");
                yield return Wait(4.5f);
                var planet = site != null ? site.root.transform.Find("Parent planet") : null;
                if (planet != null)
                {
                    // look up past the ship at the planet the moon circles
                    Vector3 up = planet.position - cc.transform.position;
                    Shot(261 + n * 2, cc.transform, demo.ShipViewOffset, D * 1.3f, Mathf.Atan2(up.x, up.z) * Mathf.Rad2Deg,
                         -Mathf.Atan2(up.y, new Vector2(up.x, up.z).magnitude) * Mathf.Rad2Deg * 0.75f, 0f);
                }
                else if (site != null && site.outpost != null)
                    Shot(261 + n * 2, site.outpost, Vector3.zero, 900f, Bearing(site.outpost.position, cc.transform.position) + 40f, 12f, 4f);
                else
                    Shot(261 + n * 2, cc.transform, demo.ShipViewOffset, D * 6f, cc.Heading - 60f, 22f, 3f);
                yield return Wait(4f);
                yield return FadeOut();
                perFrame = null;
                Clear();
                yield return TakeOffNow();
                n++;
            }
        }

        // ---------------------------------------------------- mini-warps

        IEnumerator Warps()
        {
            (string name, string sub)[] trips =
            {
                ("Jovara", "The gas giant"),
                ("Comet Mirren", "A faint comet far out in the cold"),
                ("Kestrel", "The star"),
                ("Thalassa", "Home: the ocean world"),
            };
            Repair();
            float until = Time.time + 25f;
            while (!sysDrive.CanWarp(out _) && Time.time < until) yield return null;
            cc.throttle = 0.12f;
            Shot(280, cc.transform, demo.ShipViewOffset, D * 1.7f, cc.Heading + 150f, 7f, 3f);
            yield return FadeIn();
            Caption("MINI-WARP", "Choose a destination on the system map, and jump", 5.5f);
            yield return Wait(1.5f);
            bool first = true;
            foreach (var (name, sub) in trips)
            {
                var to = WP(name);
                if (to == null) continue;
                sysMap.Open();
                yield return Wait(first ? 1.1f : 0.8f);
                if (first)
                {
                    sysMap.Select(WP("Ochre"));
                    yield return Wait(0.8f);
                    sysMap.Select(WP("Comet Iskra"));
                    yield return Wait(0.8f);
                }
                sysMap.Select(to);
                yield return Wait(first ? 1.3f : 1f);
                bool ok = false;
                for (int i = 0; i < 6 && !(ok = sysMap.WarpToSelected()); i++) yield return Wait(1f);
                Note($"warp to {name}: {(ok ? "engaged" : "refused: " + sysDrive.LastMessage)}, {sysDrive.TravelTime:F1} s in the tunnel");
                if (!ok) { sysMap.Close(); continue; }
                Phase(1, T);
                Shot(11, cc.transform, demo.ShipViewOffset, D, cc.Heading + 110f, 8f, 0f);
                arrivedLogged = false;
                perFrame = () => WarpShots(T, T - phaseAt, D);
                while (sysDrive.Current == WarpDrive.State.Charging) yield return null;
                // a long trip: cut from the first few seconds of the tunnel to its end
                yield return Wait(Mathf.Min(3.5f, sysDrive.TravelTime));
                if (sysDrive.Current == WarpDrive.State.Warping && sysDrive.TravelTime - sysDrive.StateTime > 2f)
                {
                    recording = false;
                    while (sysDrive.Current == WarpDrive.State.Warping && sysDrive.TravelTime - sysDrive.StateTime > 1.4f) yield return null;
                    recording = true;
                }
                while (sysDrive.Current != WarpDrive.State.Ready) yield return null;
                perFrame = null;
                // the place, beyond the ship
                LookPast(290, to.Position, D * 2f, 18f, float.NaN, 2.5f);
                Caption(name.ToUpper(), sub, 3.8f);
                yield return Wait(name == "Thalassa" ? 2.5f : 3.6f);
                first = false;
            }

            // the end: pull away from the frigate over Thalassa
            Shot(299, cc.transform, demo.ShipViewOffset, D * 1.6f, cam.yaw, cam.pitch, 4f);
            float t0 = Time.time;
            perFrame = () => cam.distance = Mathf.Lerp(D * 1.6f, D * 5f, Smooth((Time.time - t0) / 6f));
            Caption("BATTLECRUISER 4000 BC", endLine ?? "Mining  ·  combat  ·  landing  ·  mini-warps", 6f);
            yield return Wait(4.5f);
            yield return FadeOut(1.5f);
            perFrame = null;
        }
    }
}

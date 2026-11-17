using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The Vrask reel: StarSystem.exe -reel &lt;folder&gt; -vrask. What the fourth round of
    /// improvements added, scene by scene: a flown docking at Hollow Rock (auto-travel, then K, and
    /// the docking computer flies her in with no fade); ships keeping clear (the fleet's Halberd
    /// steering round the leader as she turns hard across it); then the Vrask at Comet Mirren:
    /// each of their four ships close up, damage to components (J aims at a Fang's engines and the
    /// guns knock them out), the Snares' ion fire and tractor beam, the Fang's boarding pods, a
    /// breach and its clamp, the Maw's artillery and a nuclear missile in space, the Skinks'
    /// point defence, and the whole group fighting as a fleet, phase by phase; last the Vrask
    /// siege of Port Ochre (a nuclear raid from orbit, and the ruins). It uses the feature reel's
    /// captions, fades and camera helpers, and the self-tests' ways of setting each scene up
    /// (SystemTest.Vrask, .Tractor, .Boarding, .Artillery, .VraskFleet, .Fleet, .Navigation).
    /// Every scene runs under a time limit and is stepped by hand (Safe), so one that throws or
    /// stalls is cut short and the reel goes on to its end.
    /// </summary>
    public partial class DemoReel
    {
        /// <summary>Recording the Vrask reel (-vrask).</summary>
        public static bool VraskReel;

        // the raiders round Comet Mirren, and the frigate's own systems as they were
        EncounterGroup vGroup;
        readonly List<VraskShip> vShips = new List<VraskShip>();
        VraskFleet vFleet;
        WarpPoint vPlace;
        Vector3 vStageOff;             // the frigate's spot for the Vrask scenes, from the comet
        ShipSystems sysF;
        Crew crewF;
        CiwsDirector ciwsF;
        float vCap0 = 6000f, vTough0 = 0.12f, vBombard0 = 45f;
        int vComplement0, vNukeAfter0 = 3, vBunched0 = 3;
        int skinkDowns;

        Vector3 VStage => vPlace != null ? vPlace.Position + vStageOff : cc.transform.position;

        IEnumerator VraskShowReel()
        {
            recording = false;
            while (frame < Warmup) yield return null;
            landing = PlanetLanding.Instance;
            Arm();                              // the hull can't be lost in the reel
            SideGuns(false);
            sysF = cc.GetComponent<ShipSystems>();
            crewF = cc.GetComponent<Crew>();
            ciwsF = cc.GetComponent<CiwsDirector>();
            if (shieldF != null) vCap0 = shieldF.capacity;
            if (sysF != null) vTough0 = sysF.toughness;
            if (crewF != null) vComplement0 = crewF.complement;
            Note($"the Vrask reel: flying the {PlayerFrigate.Flying}, shield {vCap0:F0}, crew {vComplement0}");

            yield return Safe("title", VraskTitle(), 40f);
            yield return Safe("flown docking", FlownDocking(), 420f);
            yield return Safe("after the docking", AfterDocking(), 30f);
            yield return Safe("ships keep clear", KeepClear(), 150f);
            if (FindVrask())
            {
                yield return Safe("the Vrask revealed", VraskReveal(), 100f);
                yield return Safe("components", ComponentsShow(), 100f);
                yield return Safe("the Snare", SnareShow(), 160f);
                yield return Safe("the Fang", FangShow(), 330f);
                yield return Safe("the Maw", MawShow(), 220f);
                yield return Safe("the Skink", SkinkShow(), 100f);
                yield return Safe("the fleet", VraskFleetShow(), 480f);
                VraskTidy();
            }
            else Note("no Vrask raiders (with a Snare, a Fang, a Maw and a Skink) in the scene: their scenes are left out");
            yield return Safe("the siege", SiegeShow(), 330f);
            yield return Safe("the end", VraskEnd(), 40f);

            ReportSystems();
            Note($"the end: dockings {(Docking.Instance != null ? Docking.Instance.Dockings : 0)} ({Docking.Flown} flown in, {Docking.FadedIn} faded in), " +
                 $"landings {(landing != null ? landing.Landings : 0)}");
            finished = true;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>A scene run under a time limit (game time), stepped here rather than by Unity
        /// so that an exception in it ends only that scene: what it yields (other coroutines
        /// nested in it, waits) is run the same way. Cut short, the picture fades out and the
        /// controls are let go, and the reel goes on.</summary>
        IEnumerator Safe(string name, IEnumerator scene, float limit)
        {
            float until = Time.time + limit;
            var stack = new Stack<IEnumerator>();
            stack.Push(scene);
            while (stack.Count > 0)
            {
                if (Time.time > until) { Note($"{name}: cut short after {limit:F0} s"); break; }
                var top = stack.Peek();
                bool more;
                try { more = top.MoveNext(); }
                catch (Exception e)
                {
                    Note($"{name}: stopped by {e.GetType().Name}: {e.Message}");
                    Debug.LogException(e);
                    break;
                }
                if (!more) { stack.Pop(); continue; }
                if (top.Current is IEnumerator inner) { stack.Push(inner); continue; }
                yield return top.Current;
            }
            if (recording) yield return FadeOut(0.3f);
            perFrame = null;
            try { Clear(); SideGuns(false); } catch (Exception e) { Note($"{name}: tidying: {e.Message}"); }
        }

        /// <summary>A rough size for a ship (across its meshes), for the camera's distance.</summary>
        static float SizeOf(Transform t)
        {
            bool any = false;
            var b = new Bounds(t.position, Vector3.zero);
            foreach (var r in t.GetComponentsInChildren<Renderer>())
            {
                if (!(r is MeshRenderer || r is SkinnedMeshRenderer) || !r.enabled) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any ? Mathf.Clamp(b.extents.magnitude * 2f, 80f, 500f) : 180f;
        }

        /// <summary>The frigate whole and ready for the next scene: hull, shield and components
        /// repaired, nobody aboard who shouldn't be, nothing holding her, her own guns quiet.</summary>
        void FrigateWhole()
        {
            Repair();
            if (hullF != null) hullF.cannotDie = true;
            if (shieldF != null) { shieldF.SetCapacity(vCap0); shieldF.SetOffline(false); }
            if (sysF != null) { sysF.toughness = vTough0; sysF.RepairAll(); }
            var b = Boarders.On(cc.transform);
            if (b != null) b.Clear();
            foreach (var beam in TractorBeam.BeamsHolding(cc.transform).ToList()) if (beam != null) beam.Release("the reel");
            if (crewF != null) { crewF.complement = vComplement0; crewF.SetAboard(vComplement0); }
            if (ciwsF != null) ciwsF.active = true;
            cc.throttle = 0f;
            cc.SetTarget(null);
            SideGuns(false);
        }

        /// <summary>Off camera, until the frigate is back from being destroyed or captured.</summary>
        IEnumerator FrigateReady()
        {
            float until = Time.time + 15f;
            while (hullF != null && hullF.Dead && Time.time < until) yield return null;
        }

        bool FindVrask()
        {
            vGroup = GameObject.Find("Vrask raiders")?.GetComponent<EncounterGroup>();
            vShips.Clear();
            if (vGroup != null && vGroup.pilots != null)
                vShips.AddRange(vGroup.pilots.Where(p => p != null).Select(p => p.GetComponent<VraskShip>()).Where(v => v != null));
            vFleet = vGroup != null ? vGroup.GetComponent<VraskFleet>() : null;
            Note($"the Vrask raiders: {string.Join(", ", vShips.Select(v => $"{v.name} ({v.role})"))}");
            return vGroup != null && VOne(VraskShip.Role.Snare) != null && VOne(VraskShip.Role.Fang) != null &&
                   VOne(VraskShip.Role.Maw) != null && VOne(VraskShip.Role.Skink) != null;
        }

        VraskShip VOne(VraskShip.Role r) => vShips.FirstOrDefault(v => v != null && v.role == r);
        List<VraskShip> VOfRole(VraskShip.Role r) => vShips.Where(v => v != null && v.role == r).ToList();

        /// <summary>A Vrask ship's pilot, tactics and pods (what makes it act by itself).</summary>
        static IEnumerable<Behaviour> Brains(VraskShip v) =>
            new Behaviour[] { v.GetComponent<CombatPilot>(), v.GetComponent<SnareTactics>(), v.GetComponent<FangTactics>(), v.GetComponent<MawTactics>(),
                              v.GetComponent<SkinkTactics>(), v.GetComponent<PointDefence>(), v.GetComponent<PodLauncher>() }.Where(b => b != null);

        /// <summary>A Vrask ship still and silent (its beam let go, a clamp cast off), and if
        /// `far`, parked out of the way, beyond the target display's 30 km.</summary>
        void VHold(VraskShip v, bool far)
        {
            if (v == null) return;
            foreach (var b in Brains(v)) b.enabled = false;
            var beam = v.GetComponent<TractorBeam>();
            if (beam != null && beam.Locked) beam.Release("the reel");
            var w = v.GetComponent<EnemyWeapons>();
            if (w != null) w.weaponsFree = false;
            if (far)
            {
                Vector3 away = vPlace != null ? VStage - vPlace.Position : Vector3.forward;
                away.y = 0f;
                away = away.sqrMagnitude > 1f ? away.normalized : Vector3.forward;
                int i = Mathf.Max(0, vShips.IndexOf(v));
                VPut(v, VStage + Quaternion.AngleAxis(55f * i, Vector3.up) * away * 32000f + Vector3.up * (400f * i));
            }
        }

        void VHoldAll(bool far = true)
        {
            foreach (var v in vShips) VHold(v, far);
            if (vFleet != null) vFleet.enabled = false;
        }

        /// <summary>A Vrask ship let loose on the frigate: its pilot and tactics on (its pods if
        /// `pods`), its guns free.</summary>
        void VFree(VraskShip v, bool pods)
        {
            if (v == null) return;
            foreach (var b in Brains(v)) b.enabled = !(b is PodLauncher) || pods;
            var w = v.GetComponent<EnemyWeapons>();
            if (w != null) { w.weaponsFree = true; w.SetTarget(cc.transform); }
            var pl = v.GetComponent<CombatPilot>();
            if (pl != null)
            {
                pl.target = cc.transform;
                if (pl.Current == CombatPilot.State.Lurk) pl.Engage();
            }
        }

        /// <summary>A Vrask ship whole: back if destroyed, hull, components and shield.</summary>
        void VWhole(VraskShip v)
        {
            if (v == null) return;
            var h = v.GetComponent<Health>();
            if (h != null)
            {
                if (h.Dead) h.RespawnNow();
                h.Repair();
            }
            var sys = v.GetComponent<ShipSystems>();
            if (sys != null) sys.RepairAll();
            var sh = v.GetComponentInChildren<Shield>(true);
            if (sh != null) sh.SetOffline(false);
        }

        /// <summary>A Vrask ship put somewhere, bow on to the frigate.</summary>
        void VPut(VraskShip v, Vector3 at)
        {
            if (v == null) return;
            Vector3 look = cc.transform.position - at;
            v.transform.SetPositionAndRotation(at, look.sqrMagnitude > 1f ? Quaternion.LookRotation(look) : v.transform.rotation);
        }

        /// <summary>Back to the frigate's spot at the comet, facing it, whole; every Vrask ship
        /// still and parked out of the way.</summary>
        IEnumerator VStageUp()
        {
            yield return FrigateReady();
            VHoldAll();
            FrigateWhole();
            if (vPlace != null) Place(VStage, vPlace.Position);
            yield return Wait(0.3f);
        }

        void VraskTidy()
        {
            try
            {
                Missile.Downed -= SkinkDowned;
                if (vFleet != null) vFleet.bombardTime = vBombard0;
                VHoldAll(false);
                var maw = VOne(VraskShip.Role.Maw);
                var mt = maw != null ? maw.GetComponent<MawTactics>() : null;
                if (mt != null) { mt.shellsBeforeNuke = vNukeAfter0; mt.bunched = vBunched0; }
                FrigateWhole();
            }
            catch (Exception e) { Note($"tidying after the Vrask: {e.Message}"); }
        }

        void SkinkDowned(Missile m, Transform by)
        {
            if (m == null || by == null || m.owner != cc.transform) return;
            var v = by.GetComponentInParent<VraskShip>();
            if (v != null && v.role == VraskShip.Role.Skink) skinkDowns++;
        }

        static bool PodsFlying() => Missile.Active.Any(m => m != null && m is BoardingPod);

        /// <summary>Boarding pods at the frigate from `km` off her port beam, level with her
        /// (where her CIWS bear), spread along her (as SystemTest.Boarding).</summary>
        IEnumerator PodWave(BoardingPod prefab, Transform owner, int n, float km)
        {
            for (int i = 0; i < n; i++)
            {
                var f = cc.transform;
                Vector3 at = f.position - f.right * km * 1000f + f.up * 60f + f.forward * (i - (n - 1) * 0.5f) * 150f;
                PodLauncher.Fire(prefab, at, Quaternion.LookRotation(f.position - at), owner, f);
                yield return Wait(0.4f);
            }
        }

        // ------------------------------------------------------------------ the scenes

        IEnumerator VraskTitle()
        {
            var home = WP("Thalassa");
            if (home == null) yield break;
            Place(DaySide(home, home.arrivalDistance), home.Position);
            cc.throttle = 0.15f;
            LookPast(900, home.Position, D * 2.2f, 25f, float.NaN, 3f);
            yield return Wait(1f);
            yield return FadeIn(1f);
            Caption("NEW IN BATTLECRUISER 4000 BC", "The Vrask, and more", 6f);
            yield return Wait(6f);
            yield return FadeOut();
            cc.throttle = 0f;
        }

        // ---------------------------------------------------- docking, flown in

        IEnumerator FlownDocking()
        {
            var inst = Inst("Hollow Rock");
            var wp = WP("Hollow Rock");
            var belt = WP("Inner Belt");
            var dock = Docking.Instance;
            var nav = demo.Navigation;
            if (inst == null || wp == null || dock == null || nav == null) { Note("no Hollow Rock to dock at"); yield break; }
            // (the reels dock the quick way, with a fade: here the docking computer flies her in)
            Docking.QuickDock = false;
            Repair();
            Jump(wp);
            yield return Wait(1f);
            // out on the Inner Belt's side of it, some 5 km off (as SystemTest.Navigation)
            Vector3 toBelt = belt != null ? (belt.Position - wp.Position).normalized : -cc.transform.forward;
            Vector3 across = Vector3.Cross(toBelt, Vector3.up);
            across = across.sqrMagnitude > 1e-4f ? across.normalized : Vector3.right;
            Vector3 from = wp.Position + toBelt * 4000f + across * 3000f;
            Place(ClearSpot(from, (from - wp.Position).normalized), inst.BerthPosition);
            yield return Wait(1.5f);
            Modes.Set(ModeOfPlay.Navigation);
            bool going = nav.TravelTo(wp, NavigationMode.Then.Arrive);
            Note($"auto-travel to Hollow Rock from {Vector3.Distance(cc.transform.position, inst.BerthPosition) / 1000f:F1} km: {going} ('{Notices.Last}')");
            if (!going) yield break;
            yield return Wait(1f);

            Shot(901, cc.transform, demo.ShipViewOffset, D * 1.8f, cc.Heading + 150f, 12f, 0f);
            perFrame = () => FollowHeading(150f, 12f);
            yield return FadeIn();
            Caption("AUTO-TRAVEL", "Navigation mode: choose Hollow Rock, and the ship flies herself there in real time", 5f);
            yield return Wait(3f);
            // K: dock (out here it flies her in first, ending docked)
            bool asked = dock.Dock();
            Note($"K at {Vector3.Distance(cc.transform.position, inst.BerthPosition) / 1000f:F1} km from the berth: {asked} ('{Notices.Last}')");
            Caption("K: DOCK", "Still a few kilometres out: K sets her flying in to dock", 4.5f);
            yield return Wait(4.5f);

            // the rest of the way in, off camera, till the docking computer has her
            yield return FadeOut(0.4f);
            float until = Time.time + 200f, retry = Time.time + 3f;
            while (Time.time < until && !dock.Approaching && !Docking.Docked)
            {
                // (K again if the trip in isn't under way: refused, or the trip ended short)
                if (Time.time > retry && dock.Current == Docking.State.Free && !dock.FlyingIn(inst))
                {
                    retry = Time.time + 3f;
                    asked = dock.Dock();
                    if (!Modes.Is(ModeOfPlay.Navigation)) Modes.Set(ModeOfPlay.Navigation);
                    Note($"K again, {Vector3.Distance(cc.transform.position, inst.BerthPosition):F0} m from the berth: {asked} ('{Notices.Last}')");
                }
                yield return null;
            }
            Note($"the docking computer has her: approaching {dock.Approaching}, docked {Docking.Docked}, {Vector3.Distance(cc.transform.position, inst.BerthPosition):F0} m from the berth");
            if (!dock.Approaching && !Docking.Docked) { perFrame = null; yield break; }

            if (!Docking.Docked)
            {
                Shot(902, cc.transform, demo.ShipViewOffset, D * 1.5f, cc.Heading + 125f, 16f, 0f);
                perFrame = () => FollowHeading(125f, 16f);
                yield return FadeIn(0.4f);
                Caption("THE DOCKING COMPUTER", "It flies her in along the berth's approach and down onto the pad: no fade", 6f);
                until = Time.time + 22f;
                while (Time.time < until && !Docking.Docked && dock.Current != Docking.State.Free) yield return null;
                if (!Docking.Docked)
                {
                    // a long approach: cut to the end of it
                    yield return FadeOut(0.4f);
                    until = Time.time + 120f;
                    while (Time.time < until && !Docking.Docked && dock.Current != Docking.State.Free) yield return null;
                }
            }
            perFrame = null;
            Note($"Hollow Rock: docked {Docking.Docked} ({Docking.Flown} flown in, {Docking.FadedIn} faded in), " +
                 $"{Vector3.Distance(cc.transform.position, inst.BerthPosition):F1} m from the berth");
            if (!Docking.Docked) yield break;
            Shot(903, cc.transform, demo.ShipViewOffset, D * 2.2f, cc.Heading - 140f, 20f, 2f);
            if (!recording) yield return FadeIn(0.4f);
            Caption("DOCKED", "On Hollow Rock's pad, flown all the way in; the engines out, the hull repaired", 5f);
            yield return Wait(5f);
            yield return FadeOut();
        }

        /// <summary>Undocked, out of navigation's trip, and the reels' quick docking back.</summary>
        IEnumerator AfterDocking()
        {
            var nav = demo.Navigation;
            if (nav != null && nav.Travelling) nav.Stop();
            if (Docking.Docked) Docking.Instance.Undock();
            float until = Time.time + 20f;
            while (Docking.Busy && Time.time < until) yield return null;
            Docking.QuickDock = true;
            Modes.Set(ModeOfPlay.Combat);
            cc.throttle = 0f;
        }

        // ---------------------------------------------------- ships keep clear

        IEnumerator KeepClear()
        {
            var fleet = FleetDirector.Instance;
            var home = WP("Thalassa");
            if (fleet == null || home == null || demo.Cargo == null) { Note("no fleet to show"); yield break; }
            const string yard = "Kestrel Deep Yard";
            Repair();
            // over Thalassa's day side, heading across the planet
            Vector3 at = DaySide(home, home.arrivalDistance * 1.05f);
            Vector3 along = Vector3.Cross(Vector3.up, home.Position - at);
            along = along.sqrMagnitude > 1f ? along.normalized : Vector3.forward;
            Place(at, at + along * 3000f);
            yield return Wait(0.5f);
            // a Halberd from the yard joins her, crewed (as SystemTest.Fleet)
            IndustryLedger.Launch("Halberd", yard);
            IndustryLedger.SetCrew("Halberd", FleetDirector.Complement("Halberd"));
            demo.Cargo.Earn(500000f);
            bool joined = fleet.Join("Halberd", yard, false, out string why);
            var halberd = fleet.Ships.FirstOrDefault(s => s != null && s.entry != null && s.entry.id == "Halberd");
            if (!joined || halberd == null) { Note($"the Halberd couldn't join the fleet: {why}"); yield break; }
            fleet.Order(FleetPilot.Order.Follow);
            cc.throttle = 0.5f;
            // off camera, while she takes up her station
            float until = Time.time + 14f;
            while (Time.time < until && (Time.time < until - 8f || Vector3.Distance(halberd.transform.position, halberd.Station(false)) > 500f)) yield return null;

            float Gap()
            {
                if (halberd == null) return float.MaxValue;
                ShipAvoidance.CapsuleOf(cc.transform, out var c1, out var a1, out float h1, out float r1);
                ShipAvoidance.CapsuleOf(halberd.transform, out var c2, out var a2, out float h2, out float r2);
                ShipAvoidance.ClosestPoints(c1 - a1 * h1, c1 + a1 * h1, c2 - a2 * h2, c2 + a2 * h2, out var p1, out var p2);
                return Vector3.Distance(p1, p2) - r1 - r2;
            }
            // the hard turn: toward the side she is on, across her
            float side = Vector3.Dot(halberd.transform.position - cc.transform.position, cc.transform.right) >= 0f ? 1f : -1f;
            float minGap = float.MaxValue;
            int bumps0 = CarrierController.ShipBumps;
            float turnFrom = Time.time + 2f, turnTo = Time.time + 14f;
            Shot(905, cc.transform, demo.ShipViewOffset, D * 4.5f, cc.Heading, 62f, 0f);
            perFrame = () =>
            {
                if (halberd == null) return;
                // looking down on the two of them, framed on the middle
                Vector3 mid = (halberd.transform.position - cc.transform.position) * 0.5f;
                cam.offset = demo.ShipViewOffset + Quaternion.Inverse(cc.upFrame) * mid;
                cam.distance = Mathf.Max(D * 4.5f, mid.magnitude * 3.2f);
                cam.yaw = Mathf.LerpAngle(cam.yaw, cc.Heading, Time.deltaTime * 0.6f);
                demo.autoRudder = Time.time >= turnFrom && Time.time < turnTo ? side : (float?)null;
                minGap = Mathf.Min(minGap, Gap());
            };
            yield return FadeIn();
            Caption("SHIPS KEEP CLEAR", "Following her, the fleet's Halberd steers round the leader as she turns hard across it: ships fly round each other, never through", 7f);
            yield return Wait(15f);
            perFrame = null;
            demo.autoRudder = null;
            Note($"the turn across the Halberd: {minGap:F0} m between the hulls at the closest, pushed apart in {CarrierController.ShipBumps - bumps0} frames");
            yield return FadeOut();
            cc.throttle = 0f;
            if (halberd != null) fleet.LeaveAt(halberd, yard);
        }

        // ---------------------------------------------------- the Vrask

        IEnumerator VraskReveal()
        {
            vPlace = WP("Comet Mirren") ?? WarpPoint.Nearest(vShips[0].transform.position);
            if (vPlace == null) { Note("nowhere for the Vrask"); yield break; }
            FrigateWhole();
            // each section on its own (the fleet's coordination and the Skinks' escort work off
            // till the fleet scene, as in SystemTest.Vrask); no nuclear missile of the Maw's own choosing
            if (vFleet != null) { vFleet.enabled = false; vBombard0 = vFleet.bombardTime; }
            foreach (var v in vShips)
                foreach (var b in new Behaviour[] { v.GetComponent<SkinkTactics>(), v.GetComponent<PointDefence>() })
                    if (b != null) b.enabled = false;
            var mt = VOne(VraskShip.Role.Maw).GetComponent<MawTactics>();
            if (mt != null) { vNukeAfter0 = mt.shellsBeforeNuke; vBunched0 = mt.bunched; mt.shellsBeforeNuke = 1000; mt.bunched = 1000; }

            // at the comet, as a warp leaves her: they lie in wait 2 to 3 km from it, she 8.5 km out
            Jump(vPlace);
            vStageOff = cc.transform.position - vPlace.Position;
            yield return Wait(1.5f);
            Note($"at {vPlace.displayName}: the nearest Vrask {vShips.Min(v => Vector3.Distance(v.transform.position, cc.transform.position)) / 1000f:F1} km off, attacking {vGroup.Active}");
            LookPast(910, vPlace.Position, D * 2.4f, 22f, 8f, 2f);
            yield return FadeIn();
            Caption("THE VRASK", "An alien faction, lizard-like: they take prisoners, drain their life and capture ships. Their raiders lie in wait round Comet Mirren", 6f);
            yield return Wait(5.5f);

            (VraskShip.Role role, string line)[] ships =
            {
                (VraskShip.Role.Snare, "Capture frigate: ion fire on your shield generator and engines, then its tractor beam holds you"),
                (VraskShip.Role.Fang, "Assault frigate: heavy guns and missiles, boarding pods, and a prow that clamps to your hull"),
                (VraskShip.Role.Maw, "Siege frigate: spinal artillery to 13 km and two nuclear missiles; it fires at what the others see"),
                (VraskShip.Role.Skink, "Escort frigate: eight point-defence guns that shoot your missiles down; the group's eyes"),
            };
            int n = 0;
            foreach (var (role, line) in ships)
            {
                var v = VOne(role);
                if (v == null) continue;
                float size = SizeOf(v.transform);
                Shot(911 + n, v.transform, Vector3.zero, Mathf.Clamp(size * 1.7f, 220f, 800f), SunBehind(v.transform.position, n % 2 == 0 ? 35f : -35f), 10f, n % 2 == 0 ? 4f : -4f);
                var info = v.GetComponent<TargetInfo>();
                Caption((info != null ? info.Name : "Vrask " + role).ToUpperInvariant(), line, 5f);
                Note($"  {v.name}: about {size:F0} m");
                yield return Wait(5f);
                n++;
            }
            yield return FadeOut();
            // they break cover; held still off camera till each scene wants them
            vGroup.Spring();
            VHoldAll();
        }

        /// <summary>Damage kinds, armour and components: the Fang held still, its shield down,
        /// the target panel showing its components; J aims at its engines, the guns open up, and
        /// the engines are knocked out.</summary>
        IEnumerator ComponentsShow()
        {
            var fang = VOne(VraskShip.Role.Fang);
            yield return VStageUp();
            var fh = fang.GetComponent<Health>();
            var fsys = fang.GetComponent<ShipSystems>();
            var fsh = fang.GetComponentInChildren<Shield>(true);
            if (fsys == null) { Note("the Fang has no components"); yield break; }
            bool cd0 = fh != null && fh.cannotDie;
            if (fh != null) fh.cannotDie = true;
            VWhole(fang);
            Transform f = cc.transform;
            VPut(fang, f.position + f.forward * 1300f + f.right * 150f + f.up * 120f);
            if (fsh != null) fsh.SetOffline(true);
            yield return Wait(0.5f);
            demo.SelectTarget(fang.transform);
            LookPast(920, fang.transform.position, D * 1.5f, 18f, 8f, 0f);
            yield return FadeIn();
            Caption("DAMAGE KINDS AND ARMOUR", "Ion is hard on shields and components, a penetrator goes through armour, a nullifier strips shields; armour stops a share of each hit", 6.5f);
            yield return Wait(4f);
            demo.CycleAim();              // J: its weapons
            yield return Wait(1.3f);
            demo.CycleAim();              // J: its engines
            Caption("COMPONENTS", "The target panel shows the Fang's weapons, engines and shield generator: J aims at its engines, and the guns open up", 6f);
            yield return Wait(1f);
            SideGuns(true);
            float until = Time.time + 14f;
            while (Time.time < until && !fsys.EnginesDown) yield return null;
            if (!fsys.EnginesDown) { Note($"the Fang's engines at {fsys.Condition(ShipSystem.Engines):P0} after the guns: knocked out by hand"); fsys.SetCondition(ShipSystem.Engines, 0.1f); }
            else Note($"the guns knocked out the Fang's engines (aimed at its {ShipSystems.Label(ShipSystems.AimOf(cc.transform.root))})");
            yield return Wait(1f);
            Shot(921, fang.transform, Vector3.zero, Mathf.Clamp(SizeOf(fang.transform) * 2.2f, 300f, 900f), Bearing(cc.transform.position, fang.transform.position) + 125f, 12f, 3f);
            Caption("ENGINES KNOCKED OUT", "Below 25% a component is out until it is repaired above half: the Fang's engines give 15% of top speed", 5.5f);
            yield return Wait(5.5f);
            yield return FadeOut();
            SideGuns(false);
            cc.SetTarget(null);
            if (fh != null) fh.cannotDie = cd0;
            VWhole(fang);
        }

        /// <summary>The Snares: ion fire knocks out the frigate's shield generator, then her
        /// engines; a tractor beam locks and pulls her in (as SystemTest.Tractor).</summary>
        IEnumerator SnareShow()
        {
            var snares = VOfRole(VraskShip.Role.Snare);
            yield return VStageUp();
            if (sysF == null) { Note("the frigate has no components to knock out"); yield break; }
            // (her components a little softer than in play, so it doesn't take a minute)
            sysF.toughness = vTough0 * 0.6f;
            Transform f = cc.transform;
            for (int i = 0; i < snares.Count; i++)
            {
                VWhole(snares[i]);
                VPut(snares[i], f.position + f.forward * (i == 0 ? 1900f : 1700f) + f.right * (i == 0 ? 600f : -700f) + f.up * (i == 0 ? 150f : -100f));
                VFree(snares[i], pods: false);
            }
            yield return Wait(0.3f);
            LookPast(930, snares[0].transform.position, D * 1.6f, 20f, 8f, 0f);
            yield return FadeIn();
            Caption("THE SNARE", "Two Snares stand off 2 km, bow on: their ion fire goes for your shield generator", 6f);
            float until = Time.time + 22f;
            while (Time.time < until && !sysF.IsDown(ShipSystem.Shields)) yield return null;
            if (!sysF.IsDown(ShipSystem.Shields)) { Note("the shield generator knocked out by hand"); sysF.SetCondition(ShipSystem.Shields, 0.1f); }
            else Note($"ion fire knocked out the shield generator ({22f - (until - Time.time):F0} s)");
            Shot(931, cc.transform, demo.ShipViewOffset, D * 1.5f, FrameBearing(snares[0].transform.position, cc.transform.position) + 30f, 10f, 0f);
            Caption("SHIELD GENERATOR DOWN", "Ion gets through to the generator even while the shield holds: the shield drops, and next the engines", 5.5f);
            until = Time.time + 16f;
            while (Time.time < until && !sysF.EnginesDown) yield return null;
            if (!sysF.EnginesDown) { Note("the engines knocked out by hand"); sysF.SetCondition(ShipSystem.Engines, 0.1f); }
            Caption("ENGINES DOWN", "Crawling at 15% of top speed: now the tractor beam", 4f);
            until = Time.time + 12f;
            while (Time.time < until && !cc.Tractored) yield return null;
            if (!cc.Tractored)
            {
                var near = snares.OrderBy(s => Vector3.Distance(s.transform.position, cc.transform.position)).Select(s => s.GetComponent<TractorBeam>()).FirstOrDefault(b => b != null);
                string why = "no beam";
                bool locked = near != null && near.Lock(cc.transform, out why);
                Note($"the tractor locked by hand: {locked} ({why})");
            }
            var holder = TractorBeam.BeamsHolding(cc.transform).FirstOrDefault();
            if (holder != null)
            {
                BeamSide(932, holder.From, 10f);
                float pulled0 = cc.TractorPulled;
                Caption("TRACTOR LOCK", "A Snare's beam locks on and pulls her in, to be held 1.5 km off: no warp, docking or landing while held", 7f);
                yield return Wait(7.5f);
                Note($"the tractor: {holder.name} pulled her {cc.TractorPulled - pulled0:F0} m, {cc.TractorBeams} beam(s), held fast {cc.TractorFirm}");
            }
            yield return FadeOut();
            VHoldAll();
            FrigateWhole();
        }

        /// <summary>The Fang: boarding pods (the CIWS shooting them down), a breach with the
        /// shield down and the crew fighting the boarders, and the Fang clamping on (as
        /// SystemTest.Boarding).</summary>
        IEnumerator FangShow()
        {
            var fang = VOne(VraskShip.Role.Fang);
            var launcher = fang.GetComponent<PodLauncher>();
            var tactics = fang.GetComponent<FangTactics>();
            if (launcher == null || launcher.podPrefab == null || shieldF == null) { Note("the Fang has no boarding pods"); yield break; }
            var prefab = launcher.podPrefab;
            yield return VStageUp();
            Transform f = cc.transform;
            VWhole(fang);
            VPut(fang, f.position - f.right * 2200f + f.up * 120f);

            // ---- pods at a shield that holds: the CIWS shoot them down
            shieldF.SetCapacity(1e7f);
            shieldF.SetOffline(false);
            if (ciwsF != null) ciwsF.active = true;
            int down0 = Missile.ShotDownCount, burst0 = BoardingPod.Burst;
            LookPast(940, fang.transform.position, D * 1.7f, 14f, 8f, 0f);
            yield return FadeIn();
            Caption("BOARDING PODS", "The Fang's pods fly at you like missiles: the CIWS shoot them down, and a shield that holds bursts them", 6f);
            yield return Wait(1f);
            yield return PodWave(prefab, fang.transform, 3, 1.8f);
            float until = Time.time + 14f;
            while (Time.time < until && PodsFlying()) yield return null;
            Note($"pods at a shield that holds: {Missile.ShotDownCount - down0} shot down, {BoardingPod.Burst - burst0} burst on the shield");
            yield return Wait(1.5f);
            yield return FadeOut(0.4f);

            // ---- the shield down, the CIWS off: a pod breaches the hull, and the crew fight them
            if (ciwsF != null) ciwsF.active = false;
            shieldF.SetCapacity(vCap0);
            shieldF.SetOffline(true);
            if (crewF != null) crewF.SetAboard(crewF.complement);
            Shot(941, cc.transform, demo.ShipViewOffset, D * 1.4f, FrameBearing(cc.transform.position - cc.transform.right * 1000f, cc.transform.position) + 25f, 9f, 0f);
            yield return FadeIn(0.4f);
            Caption("BREACH", "With the shield down a pod gets through the hull: 8 to 12 breachers aboard, killing the crew they meet", 6f);
            yield return PodWave(prefab, fang.transform, 1, 0.6f);
            Boarders b = null;
            until = Time.time + 15f;
            while (Time.time < until && ((b = Boarders.On(cc.transform)) == null || b.Aboard == 0)) yield return null;
            Note($"the breach: {(b != null ? b.Aboard : 0)} boarders aboard; the HUD: '{Boarders.HudText(cc.transform).Replace("\n", " / ")}'");
            yield return Wait(5f);
            if (b != null && b.Result == Boarders.Outcome.Fighting)
            {
                // the fight, off camera, to its end
                yield return FadeOut(0.4f);
                until = Time.time + 60f;
                while (Time.time < until && b != null && b.Result == Boarders.Outcome.Fighting) yield return null;
                if (b != null && b.Result == Boarders.Outcome.Repelled)
                {
                    yield return FadeIn(0.4f);
                    Caption("REPELLED", $"A full crew wears a small party down: {b.BoardersKilled:F0} boarders killed, {b.CrewKilled:F0} of the crew lost, {b.Freed} freed", 5f);
                    yield return Wait(5f);
                }
                Note($"the fight: {b?.Result}, crew {(crewF != null ? crewF.Current : 0f):F0}");
            }
            yield return FadeOut(0.4f);
            b = Boarders.On(cc.transform);
            if (b != null) b.Clear();
            yield return FrigateReady();

            // ---- the Fang's own way in: from an odd angle, guns on her weapons, then the clamp
            // (a crew too big for its party to take: the scene is about the clamp)
            if (tactics == null) yield break;
            if (crewF != null) { crewF.complement = Mathf.Max(vComplement0, 1000); crewF.SetAboard(crewF.complement); }
            if (hullF != null) hullF.Repair();
            shieldF.SetOffline(true);
            if (ciwsF != null) ciwsF.active = true;
            VWhole(fang);
            VPut(fang, f.position + f.up * 1600f - f.forward * 2600f + f.right * 900f);
            int clamps0 = tactics.Clamps;
            VFree(fang, pods: true);
            until = Time.time + 40f;
            while (Time.time < until && tactics.Clamps == clamps0 && tactics.Current != FangTactics.Phase.Run &&
                   Vector3.Distance(fang.transform.position, cc.transform.position) > 1500f) yield return null;
            if (tactics.Clamps == clamps0)
            {
                Shot(942, fang.transform, Vector3.zero, 450f, Bearing(fang.transform.position, cc.transform.position) + 20f, 10f, 0f);
                perFrame = () =>
                {
                    if (fang != null) cam.yaw = Mathf.LerpAngle(cam.yaw, Bearing(fang.transform.position, cc.transform.position) + 20f, Time.deltaTime * 2f);
                };
                yield return FadeIn(0.4f);
                Caption("THE FANG", $"In fast from {(tactics.Vector.Length > 0 ? tactics.Vector : "an odd angle")}, its guns on your weapons: with your shield down it clamps its prow to your hull", 6.5f);
                until = Time.time + 12f;
                while (Time.time < until && tactics.Clamps == clamps0) yield return null;
                perFrame = null;
                if (tactics.Clamps == clamps0)
                {
                    yield return FadeOut(0.4f);
                    until = Time.time + 45f;
                    while (Time.time < until && tactics.Clamps == clamps0) yield return null;
                }
            }
            Note($"the Fang: {tactics.Clamps - clamps0} clamp(s), {tactics.Current}, came in {tactics.Vector}");
            if (tactics.IsClamped)
            {
                Shot(943, cc.transform, demo.ShipViewOffset, D * 1.6f, FrameBearing(fang.transform.position, cc.transform.position) + 70f, 16f, 2f);
                if (!recording) yield return FadeIn(0.4f);
                Caption("CLAMPED ON", "Its party of 12 goes in through the ram. Clamped, you crawl at 4 m/s and can't warp; it casts off when its boarders are done", 6.5f);
                yield return Wait(6.5f);
            }
            yield return FadeOut();
            VHold(fang, true);          // (casts off)
            FrigateWhole();
        }

        /// <summary>The Maw: its artillery holds till a Skink spots for it, a shell's long flight
        /// and wide blast; then a nuclear missile in space (as SystemTest.Artillery).</summary>
        IEnumerator MawShow()
        {
            var maw = VOne(VraskShip.Role.Maw);
            var spotter = VOne(VraskShip.Role.Skink);
            var gun = maw.GetComponentInChildren<ArtilleryGun>(true);
            var tactics = maw.GetComponent<MawTactics>();
            var silos = maw.GetComponent<NukeLauncher>();
            var arms = maw.GetComponent<EnemyWeapons>();
            if (gun == null || tactics == null) { Note("the Maw has no artillery"); yield break; }
            yield return VStageUp();
            Transform f = cc.transform;
            float size = SizeOf(maw.transform);

            // ---- the artillery: the Maw 10 km astern of her, a Skink 3 km off her to spot
            VWhole(maw);
            VPut(maw, f.position - f.forward * 10000f + f.up * 300f);
            VPut(spotter, f.position + f.right * 3000f);
            VFree(maw, pods: false);
            gun.HoldUntil(Time.time + 4f);
            int fired0 = gun.Fired;
            float until = Time.time + 15f;
            while (Time.time < until && !tactics.Spotted) yield return null;
            Note($"the Maw {Vector3.Distance(maw.transform.position, cc.transform.position) / 1000f:F1} km off: spotted {tactics.Spotted} by {tactics.Spotter?.name}");
            Shot(950, maw.transform, Vector3.zero, Mathf.Clamp(size * 2f, 300f, 900f), Bearing(maw.transform.position, cc.transform.position) + 30f, 8f, 0f);
            yield return FadeIn();
            Caption("THE MAW", "Spinal artillery to 13 km, fired only at what another Vrask ship sees: here a Skink 3 km from her", 6f);
            until = Time.time + 25f;
            while (Time.time < until && gun.Fired == fired0) yield return null;
            var shell = gun.LastShell;
            if (gun.Fired > fired0 && shell != null)
            {
                float flight = gun.LastFlight, t0 = Time.time;
                Note($"the Maw fires a {gun.LastRound} round from {gun.LastRange / 1000f:F1} km: {flight:F1} s in flight");
                yield return Wait(0.5f);
                if (shell != null)
                {
                    Shot(951, shell.transform, Vector3.zero, 110f, Bearing(maw.transform.position, cc.transform.position) + 12f, 6f, 0f);
                    Caption("ARTILLERY", $"A slow shell, {flight:F0} s on its way, aimed at where she will be when it gets there", 5f);
                    yield return Wait(Mathf.Clamp(flight - 6f, 1f, 5f));
                }
                if (shell != null && !shell.Burst && Time.time < t0 + flight - 4f)
                {
                    yield return FadeOut(0.3f);
                    until = t0 + flight - 3f;
                    while (Time.time < until && shell != null && !shell.Burst) yield return null;
                }
                Shot(952, cc.transform, demo.ShipViewOffset, Mathf.Max(D * 6f, 1800f), cc.Heading + 70f, 20f, 1f);
                if (!recording) yield return FadeIn(0.3f);
                int blasts0 = ArtilleryShell.Blasts;
                until = Time.time + 8f;
                while (Time.time < until && shell != null && !shell.Burst) yield return null;
                Caption("A WIDE BLAST", "It bursts 500 m round, on every ship in reach: a shield nullifier at a shield that is up, a penetrator at armour", 5f);
                yield return Wait(4.5f);
                Note($"the shell: burst {ArtilleryShell.Blasts > blasts0 || shell == null}; frigate shield {(shieldF != null ? shieldF.Fraction : 0f):P0}");
            }
            else Note("the Maw didn't fire");
            yield return FadeOut();

            // ---- a nuclear missile in space, from 7 km (the CIWS off, or they would take it)
            if (silos == null) yield break;
            if (arms != null) arms.weaponsFree = false;
            if (ciwsF != null) ciwsF.active = false;
            FrigateWhole();
            if (ciwsF != null) ciwsF.active = false;
            silos.Restock();
            VPut(spotter, f.position + f.right * 25000f);
            VPut(maw, f.position - f.forward * 7000f + f.up * 300f);
            yield return Wait(0.5f);
            Shot(953, maw.transform, Vector3.zero, Mathf.Clamp(size * 2.2f, 300f, 900f), Bearing(maw.transform.position, cc.transform.position) - 35f, 8f, 0f);
            yield return FadeIn();
            int det0 = NukeMissile.Detonations;
            float sh0 = shieldF != null ? shieldF.Strength : 0f, hu0 = hullF != null ? hullF.Current : 0f;
            var nuke = silos.LaunchNow(cc.transform);
            Caption("NUCLEAR MISSILE", "The Maw carries two: slow, at 320 m/s, and the CIWS can shoot one down", 5f);
            Note($"a nuclear missile at the frigate from {Vector3.Distance(maw.transform.position, cc.transform.position) / 1000f:F1} km: {nuke != null}, {silos.Stock} left");
            yield return Wait(2.5f);
            if (nuke != null)
            {
                Shot(954, nuke.transform, Vector3.zero, 80f, Bearing(maw.transform.position, cc.transform.position) + 10f, 6f, 0f);
                yield return Wait(3f);
                yield return FadeOut(0.3f);
                until = Time.time + nuke.lifetime + 5f;
                while (Time.time < until && nuke != null && NukeMissile.Detonations == det0 && Vector3.Distance(nuke.transform.position, cc.transform.position) > 1600f) yield return null;
                Shot(955, cc.transform, demo.ShipViewOffset, Mathf.Max(D * 7f, 2600f), cc.Heading + 60f, 18f, 1.5f);
                yield return FadeIn(0.3f);
                until = Time.time + 10f;
                while (Time.time < until && NukeMissile.Detonations == det0) yield return null;
                yield return Wait(1f);
                Caption("IN SPACE", "With no air to carry a shock wave it breaks a frigate's shield, but doesn't destroy her", 5f);
                yield return Wait(5f);
                Note($"the nuke: went off {NukeMissile.Detonations > det0}; shield {sh0:F0} -> {(shieldF != null ? shieldF.Strength : 0f):F0}, hull {hu0:F0} -> {(hullF != null ? hullF.Current : 0f):F0}");
            }
            yield return FadeOut();
            silos.Restock();
            if (arms != null) arms.weaponsFree = true;
            VHoldAll();
            FrigateWhole();
        }

        /// <summary>The Skinks: point defence shooting down the frigate's missiles on their way
        /// to the Maw (as SystemTest.VraskFleet's screen).</summary>
        IEnumerator SkinkShow()
        {
            var maw = VOne(VraskShip.Role.Maw);
            var skinks = VOfRole(VraskShip.Role.Skink);
            yield return VStageUp();
            // the Maw 4.5 km off, out to one side away from the comet; the Skinks between, screening it
            Transform f = cc.transform;
            Vector3 dir = (f.right - f.forward * 0.6f).normalized;
            Vector3 mawAt = f.position + dir * 4500f + Vector3.up * 150f;
            Place(f.position, mawAt);
            yield return null;
            VWhole(maw);
            VPut(maw, mawAt);
            Vector3 across = Vector3.Cross(dir, Vector3.up).normalized;
            for (int i = 0; i < skinks.Count; i++)
            {
                VWhole(skinks[i]);
                VPut(skinks[i], mawAt - dir * 900f + across * (i % 2 == 0 ? 450f : -450f) * (1 + i / 2) + Vector3.up * 80f * i);
                var pd = skinks[i].GetComponent<PointDefence>();
                if (pd != null) pd.enabled = true;
                var w = skinks[i].GetComponent<EnemyWeapons>();
                if (w != null) { w.weaponsFree = true; w.SetTarget(cc.transform); }
            }
            var mh = maw.GetComponent<Health>();
            bool cd0 = mh != null && mh.cannotDie;
            if (mh != null) mh.cannotDie = true;
            demo.SelectTarget(maw.transform);
            yield return Wait(0.5f);
            var skink = skinks.FirstOrDefault();
            Shot(960, skink.transform, Vector3.zero, Mathf.Clamp(SizeOf(skink.transform) * 2.6f, 300f, 700f), Bearing(skink.transform.position, cc.transform.position) + 25f, 8f, 0f);
            yield return FadeIn();
            Caption("THE SKINK", "Escort frigate: it screens the Maw, and its eight needle guns take your missiles first", 6f);
            yield return Wait(1.5f);

            skinkDowns = 0;
            Missile.Downed -= SkinkDowned;
            Missile.Downed += SkinkDowned;
            int fired = 0;
            var prefab = cc.missiles != null ? cc.missiles.missilePrefab : null;
            if (prefab == null) prefab = VOne(VraskShip.Role.Fang).GetComponent<EnemyWeapons>()?.missilePrefab;
            for (int salvo = 0; salvo < 3 && skinkDowns == 0; salvo++)
            {
                int before = cc.HasMissiles ? cc.missiles.Stock : 0;
                if (cc.HasMissiles && cc.missiles.Stock != 0) demo.FireMissiles();
                int launched = cc.HasMissiles ? before - cc.missiles.Stock : 0;
                if (launched <= 0 && prefab != null)
                {
                    // (a frigate without missiles, or out of them: a salvo of three all the same)
                    for (int i = 0; i < 3; i++)
                    {
                        Vector3 up = (f.forward + f.up * 0.3f + f.right * (i - 1) * 0.3f).normalized;
                        var m = Instantiate(prefab, f.position + up * 60f, Quaternion.LookRotation(up));
                        m.owner = f;
                        m.target = maw.transform;
                    }
                    launched = 3;
                }
                fired += launched;
                Note($"a salvo of {launched} missiles at the Maw");
                if (launched <= 0) break;
                float until = Time.time + 10f;
                yield return Wait(1f);
                while (Time.time < until && skinkDowns == 0 && Missile.Active.Any(m => m != null && m.owner == f && !(m is BoardingPod))) yield return null;
            }
            yield return Wait(2f);
            Missile.Downed -= SkinkDowned;
            Note($"point defence: {skinkDowns} of {fired} missiles shot down by a Skink");
            if (skinkDowns > 0)
            {
                Caption("SHOT DOWN", $"{skinkDowns} of {fired} missiles brought down by the Skinks' needles: two to a missile", 4.5f);
                yield return Wait(4.5f);
            }
            yield return FadeOut();
            cc.SetTarget(null);
            if (cc.missiles != null) cc.missiles.Rearm();
            if (mh != null) mh.cannotDie = cd0;
            VHoldAll();
        }

        /// <summary>The whole group as a fleet, phase by phase (as SystemTest.VraskFleet): picket,
        /// bombard, screen and disable, board; then the Maw and both Snares destroyed, and the
        /// rest withdraw.</summary>
        IEnumerator VraskFleetShow()
        {
            if (vFleet == null) { Note("no Vrask fleet coordination"); yield break; }
            var maw = VOne(VraskShip.Role.Maw);
            var fang = VOne(VraskShip.Role.Fang);
            var snares = VOfRole(VraskShip.Role.Snare);
            var skinks = VOfRole(VraskShip.Role.Skink);
            var gun = maw.GetComponentInChildren<ArtilleryGun>(true);
            yield return VStageUp();
            foreach (var v in vShips) VWhole(v);
            yield return null;
            // a crew too big for their boarders to take
            if (crewF != null) { crewF.complement = Mathf.Max(vComplement0, 3000); crewF.SetAboard(crewF.complement); }
            // the group 11 km off on the side away from the comet, the Maw behind it, bow on
            Transform f = cc.transform;
            Vector3 away = f.position - vPlace.Position;
            away = away.sqrMagnitude > 1f ? away.normalized : -f.forward;
            Vector3 side = Vector3.Cross(away, Vector3.up);
            side = side.sqrMagnitude > 0.01f ? side.normalized : Vector3.right;
            Vector3 c = f.position + away * 11000f;
            VPut(maw, c + away * 1500f + Vector3.up * 200f);
            for (int i = 0; i < snares.Count; i++) VPut(snares[i], c + side * (i % 2 == 0 ? 700f : -700f) * (1 + i / 2));
            VPut(fang, c + away * 400f + Vector3.up * 300f);
            for (int i = 0; i < skinks.Count; i++) VPut(skinks[i], c + away * 300f + side * (i % 2 == 0 ? 1400f : -1400f) * (1 + i / 2) - Vector3.up * 200f);
            // (her nose to them)
            Place(f.position, c);
            vFleet.bombardTime = Mathf.Min(vBombard0, 14f);
            foreach (var v in vShips) VFree(v, pods: true);
            vFleet.enabled = true;
            int phases0 = vFleet.Phases.Count;

            // ---- approach and picket
            var lead = skinks.FirstOrDefault();
            Shot(970, lead.transform, Vector3.zero, 420f, Bearing(lead.transform.position, cc.transform.position) + 20f, 9f, 0f);
            perFrame = () => { if (lead != null) cam.yaw = Mathf.LerpAngle(cam.yaw, Bearing(lead.transform.position, cc.transform.position) + 20f, Time.deltaTime * 2f); };
            yield return Wait(1f);
            yield return FadeIn();
            Caption("THE VRASK FIGHT TOGETHER", "Approach and picket: the Skinks run ahead as the group's eyes; the Snares and the Fang hold back", 6f);
            yield return Wait(6f);
            yield return FadeOut();
            perFrame = null;
            float until = Time.time + 60f;
            while (Time.time < until && (vFleet.Current == VraskFleet.Phase.Idle || vFleet.Current == VraskFleet.Phase.Picket)) yield return null;
            Note($"the fleet: {VraskFleet.Name(vFleet.Current)} ({vFleet.Why})");

            // ---- bombard
            if (vFleet.Current == VraskFleet.Phase.Bombard)
            {
                LookPast(971, maw.transform.position, D * 1.8f, 16f, 7f, 0f);
                yield return FadeIn();
                Caption("BOMBARD", "Contact: the Maw's artillery opens at long range on what the Skinks see, while the rest hold the line", 6f);
                yield return Wait(6.5f);
                yield return FadeOut();
                until = Time.time + 40f;
                while (Time.time < until && vFleet.Current == VraskFleet.Phase.Bombard) yield return null;
                Note($"the fleet: {VraskFleet.Name(vFleet.Current)} ({vFleet.Why}); the Maw fired {(gun != null ? gun.Fired : 0)} shells in all");
            }

            // ---- screen and disable: the Snares go in
            until = Time.time + 50f;
            while (Time.time < until && vFleet.Current == VraskFleet.Phase.Disable &&
                   snares.All(s => s == null || Vector3.Distance(s.transform.position, cc.transform.position) > 3000f)) yield return null;
            if (vFleet.Current == VraskFleet.Phase.Disable || vFleet.Current == VraskFleet.Phase.Board)
            {
                var near = snares.Where(s => s != null).OrderBy(s => Vector3.Distance(s.transform.position, cc.transform.position)).First();
                LookPast(972, near.transform.position, D * 1.6f, -18f, 8f, 0f);
                yield return FadeIn();
                Caption("SCREEN AND DISABLE", "The Snares go in: ion on her shield generator and engines, then the tractor; the Skinks screen the Maw", 6f);
                yield return Wait(6.5f);
                yield return FadeOut();
            }
            until = Time.time + 75f;
            while (Time.time < until && vFleet.Current != VraskFleet.Phase.Board &&
                   (vFleet.Current == VraskFleet.Phase.Disable || vFleet.Current == VraskFleet.Phase.Bombard)) yield return null;
            Note($"the fleet: {VraskFleet.Name(vFleet.Current)} ({vFleet.Why}); beams on her {TractorBeam.BeamsOn(cc.transform)}");

            // ---- board
            if (vFleet.Current == VraskFleet.Phase.Board && fang != null)
            {
                Shot(973, fang.transform, Vector3.zero, 450f, Bearing(fang.transform.position, cc.transform.position) + 20f, 10f, 0f);
                perFrame = () => { if (fang != null) cam.yaw = Mathf.LerpAngle(cam.yaw, Bearing(fang.transform.position, cc.transform.position) + 20f, Time.deltaTime * 2f); };
                yield return FadeIn();
                Caption("BOARD", "Disabled and held: the Fang surges in with its pods and its clamp", 6f);
                yield return Wait(6.5f);
                perFrame = null;
            }

            // ---- the Maw and both Snares destroyed: the rest withdraw
            var mark = snares.Where(s => s != null && !s.GetComponent<Health>().Dead)
                             .OrderBy(s => Vector3.Distance(s.transform.position, cc.transform.position)).FirstOrDefault();
            if (mark != null)
                Shot(974, mark.transform, Vector3.zero, Mathf.Clamp(SizeOf(mark.transform) * 3.2f, 450f, 1100f), Bearing(cc.transform.position, mark.transform.position) + 60f, 10f, 3f);
            if (!recording) yield return FadeIn();
            yield return Wait(0.8f);
            foreach (var v in snares.Append(maw))
            {
                var h = v != null ? v.GetComponent<Health>() : null;
                for (int i = 0; i < 4 && h != null && !h.Dead; i++) h.Damage(h.maxHealth * 20f, v.transform.position, HitKind.Penetrator);
            }
            Caption("WITHDRAW", "Lose the Maw and both Snares, and the rest break off and go back into hiding", 6f);
            yield return Wait(4f);
            Shot(975, cc.transform, demo.ShipViewOffset, D * 2.6f, cc.Heading + 120f, 14f, 3f);
            yield return Wait(3f);
            Note($"the fleet: {VraskFleet.Name(vFleet.Current)} ({vFleet.Why}); phases {string.Join(" > ", vFleet.Phases.Skip(phases0).Select(VraskFleet.Name))}");
            yield return FadeOut();
        }

        // ---------------------------------------------------- the siege

        /// <summary>The Vrask siege of Port Ochre: a nuclear raid from the Maw 160 km up, seen from
        /// above it, then down to the city's ruins (as SystemTest.Artillery's Bombardment and the
        /// strikes reel's ruins).</summary>
        IEnumerator SiegeShow()
        {
            var siege = VraskBombardment.All.FirstOrDefault(x => x != null);
            WarpPoint place = null, body = null;
            TileFeatures site = null;
            if (siege == null || !siege.Site(out place, out body, out site)) { Note("no Vrask siege to show"); yield break; }
            yield return FrigateReady();
            FrigateWhole();
            // her well clear of the siege group's guns (12 km), near enough to see the raid
            Vector3 sp = siege.transform.position;
            Vector3 up = (sp - body.Position).normalized;
            Vector3 across = Vector3.Cross(up, Vector3.up);
            across = across.sqrMagnitude > 1e-4f ? across.normalized : Vector3.right;
            Place(sp + up * 15000f + across * 15000f, sp);
            yield return Wait(1.5f);
            sp = siege.transform.position;
            Vector3 city = OrbitalStrikes.GroundPoint(body, site.su, site.sv);
            float fall = Vector3.Distance(sp, city);
            float size = SizeOf(siege.transform);
            // behind and above the Maw, looking down past it at the city
            ShotAlong(980, siege.transform, Vector3.zero, (city - sp).normalized, Mathf.Clamp(size * 2.4f, 350f, 1000f), 0f);
            yield return FadeIn();
            Caption("THE SIEGE OF PORT OCHRE", $"A Vrask Maw and two Skinks hold station {(Vector3.Distance(sp, body.Position) - body.bodyRadius) / 1000f:F0} km over Ochre's city, and shell it every two hours of the game's clock", 6.5f);
            yield return Wait(3.5f);
            int strikes0 = WorldState.Strikes.Count;
            float flight = Mathf.Clamp(fall / 4000f, 3f, 30f), t0 = Time.time;
            siege.RaidNow(nuke: true, force: true);
            Note($"a nuclear raid on {siege.target} from {fall / 1000f:F0} km: {flight:F0} s to fall");
            Caption("A NUCLEAR RAID", "Rarely the Maw sends a nuclear missile down instead of its shells", 5f);
            yield return Wait(Mathf.Clamp(flight - 4f, 0.5f, 5f));
            if (Time.time < t0 + flight - 4f && WorldState.Strikes.Count == strikes0)
            {
                yield return FadeOut(0.3f);
                float until = t0 + flight - 3f;
                while (Time.time < until && WorldState.Strikes.Count == strikes0) yield return null;
                yield return FadeIn(0.3f);
            }
            float done = Time.time + 12f;
            while (Time.time < done && WorldState.Strikes.Count == strikes0) yield return null;
            yield return Wait(1f);
            Caption("PORT OCHRE", "On a world with air the blast takes the whole city: every building ruined for eight hours, and half its people killed", 6f);
            Note($"the raid: {siege.LastRaid}");
            yield return Wait(5.5f);
            yield return FadeOut();

            // ---- down to the ruins
            if (landing == null || place == null) yield break;
            Jump(place);
            yield return Wait(1f);
            float wait = Time.time + 20f;
            while (Time.time < wait && sysDrive != null && sysDrive.InCombat(out _)) yield return null;
            if (!landing.LandOn(place)) { Note("couldn't land at Port Ochre"); yield break; }
            yield return WaitLanding(PlanetLanding.State.OnSurface, 90f);
            if (landing.Current != PlanetLanding.State.OnSurface) { Note($"didn't get down at Port Ochre: {landing.Current}"); yield break; }
            yield return Wait(2f);
            var surf = landing.Surface;
            var group = surf != null ? surf.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Settlement: " + place.displayName) : null;
            if (group != null)
            {
                perFrame = () =>
                {
                    if (surf != null) HoldHeight(surf, 220f);
                    demo.autoPitch = 0f;
                    demo.autoRudder = 0f;
                };
                cc.throttle = 0.1f;
                Shot(981, group, Vector3.zero, 1900f, Bearing(cc.transform.position, group.position) + 30f, 24f, 4f);
                yield return FadeIn();
                Caption("THE WORLD REMEMBERS", $"Port Ochre after the raid: {surf.Ruined} buildings in rubble, saved with the world, until they are rebuilt", 6f);
                Note($"Port Ochre: {surf.Ruined} buildings ruined");
                yield return Wait(6f);
                yield return FadeOut();
            }
            perFrame = null;
            Clear();
            cc.throttle = 0f;
            yield return TakeOffNow();
        }

        IEnumerator VraskEnd()
        {
            if (landing != null && landing.Current != PlanetLanding.State.InSpace) yield return TakeOffNow();
            var home = WP("Thalassa");
            if (home == null) yield break;
            FrigateWhole();
            Place(DaySide(home, home.arrivalDistance), home.Position);
            yield return Wait(1f);
            cc.throttle = 0.12f;
            Shot(999, cc.transform, demo.ShipViewOffset, D * 1.6f, FrameBearing(cc.transform.position, home.Position) + 160f, 8f, 3f);
            yield return FadeIn();
            float t0 = Time.time;
            perFrame = () => cam.distance = Mathf.Lerp(D * 1.6f, D * 6f, Smooth((Time.time - t0) / 6f));
            Caption("BATTLECRUISER 4000 BC", "The Vrask  ·  components  ·  tractor beams  ·  boarding  ·  artillery  ·  the siege  ·  flown docking", 6f);
            yield return Wait(5f);
            yield return FadeOut(1.5f);
            perFrame = null;
            cc.throttle = 0f;
        }
    }
}

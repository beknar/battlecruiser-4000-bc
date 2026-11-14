using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>The star system reel's shot list (StarSystem.exe -reel).</summary>
    public partial class DemoReel
    {
        // ------------------------------------------------------------------ star system shot list

        SystemMap sysMap;
        WarpDrive sysDrive;
        EncounterGroup pirates;
        Transform pirateCruiser;
        int sysPhase;
        float phaseAt, nextChargeAt = -1f, nextCut;
        bool arrivedLogged;

        void Phase(int p, float t)
        {
            sysPhase = p;
            phaseAt = t;
            visitStarted = leaving = false;
            landedAt = -1f;
        }

        PlanetLanding landing;
        bool visitStarted, leaving;
        float visitAt, landedAt = -1f;

        /// <summary>A landing: wait out the descent, fly the site (to the pad and down onto it, or
        /// just across it), take off, and on to `next` once back in orbit. With `stayPhase` it
        /// doesn't take off but goes on to that phase, still on the surface.</summary>
        void SurfaceVisit(float t, float d, int b, bool setDown, float stay, int next, int stayPhase = -1)
        {
            if (landing == null) { Phase(next, t); return; }
            if (leaving)
            {
                if (landing.Current == PlanetLanding.State.InSpace) Phase(next, t);
                return;
            }
            if (landing.Current != PlanetLanding.State.OnSurface) return;   // still coming down
            var site = landing.Site;
            Vector3 me = cc.transform.position;
            Vector3 to = site.pad - me;
            to.y = 0f;
            float dist = to.magnitude;
            if (!visitStarted)
            {
                visitStarted = true;
                visitAt = t;
                Log($"{T,5:F1}s   on the surface of {landing.Body?.displayName}: {site.description} (built in {landing.LastBuildSeconds * 1000f:F0} ms), {dist:F0} m from the pad");
                Shot(b, cc.transform, demo.ShipViewOffset, d * 1.5f, cc.Heading + 150f, 11f, 2f);
            }
            float s = t - visitAt;
            var v = ShipAutopilot.BowOn(cc, site.pad + Vector3.up * (me.y - site.pad.y));
            demo.autoRudder = dist > 60f ? v.x : 0f;
            demo.autoPitch = 0f;
            if (setDown)
            {
                // come in over the pad, dropping to about 90 m above it, stop over it, then set down
                cc.throttle = dist > 380f ? 0.6f : dist > 160f ? 0.25f : dist > 70f ? 0.1f : 0f;
                bool over = dist < 110f && cc.Speed < 14f;
                float above = me.y - site.pad.y;
                demo.autoElevator = over ? -1f : above > 130f ? -0.7f : 0f;
                // in close: low and to the side as it comes down onto the pad
                if (shot == b && dist < 430f) Shot(b + 1, cc.transform, demo.ShipViewOffset, d * 1.2f, cc.Heading + 75f, 6f, 1.5f);
                if (landing.Landed && landedAt < 0f)
                {
                    landedAt = t;
                    Log($"{T,5:F1}s   touchdown {(landing.OnPad ? "on the pad" : "")} after {s:F1} s on the surface");
                    Shot(b + 2, cc.transform, demo.ShipViewOffset, d * 1.9f, cc.Heading - 140f, 18f, 5f);
                }
            }
            else
            {
                cc.throttle = 0.32f;
                demo.autoElevator = 0f;
                // the platform as the frigate passes over it
                var outpost = site.outpost;
                if (shot == b && s > 4f && outpost != null) Shot(b + 1, outpost, Vector3.zero, 700f, Bearing(outpost.position, me) + 40f, 12f, 3f);
            }
            bool done = setDown ? landedAt > 0f && t - landedAt > 4.5f : s > stay;
            if ((done || s > 45f) && stayPhase >= 0)
            {
                Phase(stayPhase, t);
                return;
            }
            if (done || s > 45f)
            {
                demo.autoRudder = null; demo.autoPitch = null; demo.autoElevator = null;
                cc.throttle = 0.15f;
                Log($"{T,5:F1}s   taking off from {landing.Body?.displayName}");
                landing.TakeOff();
                leaving = true;
            }
        }

        WarpPoint WP(string name) => WarpPoint.All.FirstOrDefault(w => w.displayName == name);

        /// <summary>Hold the ship about `height` above the ground under it and ahead of it.</summary>
        void HoldHeight(PlanetSurface surf, float height)
        {
            Vector3 me = cc.transform.position;
            Vector3 fwd = cc.transform.forward;
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            float floor = Mathf.Max(surf.FloorAt(me), Mathf.Max(surf.FloorAt(me + fwd * 600f), surf.FloorAt(me + fwd * 1300f)));
            float above = me.y - floor;
            demo.autoElevator = above < height * 0.8f ? 1f : above > height * 1.3f ? -0.6f : 0f;
        }

        int runBuilt, runExplored;
        float runLat, runLon;

        /// <summary>After setting down on Thalassa's pad: lift off and fly on across the planet at
        /// full speed, low over the islands and the sea, the camera craning up to show the ground
        /// going on to the horizon, built round the ship as it goes; then back up to orbit.</summary>
        void SurfaceRun(float t, float d)
        {
            var surf = landing != null ? landing.Surface : null;
            if (leaving)
            {
                if (landing.Current == PlanetLanding.State.InSpace) Phase(8, t);
                return;
            }
            if (surf == null) { Phase(8, t); return; }
            if (!visitStarted)
            {
                visitStarted = true;
                phaseAt = t;
                runBuilt = surf.TilesBuilt;
                runExplored = surf.Explored.Count;
                surf.LatLon(cc.transform.position, out runLat, out runLon);
                Log($"{T,5:F1}s   off across {landing.Body?.displayName} from {PlanetLanding.LatLonText(runLat, runLon)}: {surf.TilesLoaded} areas built round the ship, {runExplored} of {landing.World.AreaCount} explored");
                Shot(110, cc.transform, demo.ShipViewOffset, d * 1.4f, cc.Heading + 160f, 7f, 2f);
            }
            float s = t - phaseAt;
            HoldHeight(surf, 230f);
            demo.autoPitch = 0f;
            demo.autoRudder = 0f;
            cc.throttle = s > 2.5f ? 1f : 0.3f;

            // low chase over the ground rushing by
            if (Cue(phaseAt + 7f)) Shot(111, cc.transform, demo.ShipViewOffset, d * 0.8f, cc.Heading, 4f, 0f);
            if (shot == 111) FollowHeading(0f, 4f);
            // from the side, craning up and back: the ground goes on to the horizon all round
            if (Cue(phaseAt + 13f)) Shot(112, cc.transform, demo.ShipViewOffset, d * 1.6f, cc.Heading - 70f, 10f, 0f);
            if (shot == 112)
            {
                float k = Smooth((s - 13f) / 10f);
                cam.yaw = Mathf.LerpAngle(cam.yaw, cc.transform.eulerAngles.y - 70f + 40f * k, Time.deltaTime * 1.5f);
                cam.distance = Mathf.Lerp(d * 1.6f, d * 9f, k);
                cam.pitch = Mathf.Lerp(10f, 34f, k);
            }
            if (Cue(phaseAt + 24f))
            {
                surf.LatLon(cc.transform.position, out float lat, out float lon);
                Log($"{T,5:F1}s   now at {PlanetLanding.LatLonText(lat, lon)}: {surf.TilesBuilt - runBuilt} areas built on the way, {surf.TilesUnloaded} thrown away, " +
                    $"{surf.Explored.Count} of {landing.World.AreaCount} explored, world shifted {surf.Shifts} times");
                demo.autoRudder = null; demo.autoPitch = null; demo.autoElevator = null;
                cc.throttle = 0.15f;
                Log($"{T,5:F1}s   taking off from {landing.Body?.displayName}");
                landing.TakeOff();
                leaving = true;
            }
        }

        GroundUnit gTarget;
        string gTargetName = "";
        Health myHull;
        bool groundBeam;
        float beamDoneAt = -1f;

        /// <summary>The ground unit to go for: the nearest battery (or failing that anything on
        /// the ground) that is still there.</summary>
        GroundUnit PickGroundTarget(PlanetSurface surf)
        {
            GroundUnit pick = null;
            float best = float.MaxValue;
            Vector3 me = cc.transform.position;
            foreach (var g in surf.Garrisons)
                foreach (var un in g.units)
                {
                    if (un == null || !un.Alive || un.GetComponent<CombatPilot>() != null) continue;
                    float dd = (un.transform.position - me).sqrMagnitude * (un.name == "Ground Battery" ? 0.3f : 1f);
                    if (dd < best) { best = dd; pick = un; }
                }
            return pick;
        }

        /// <summary>Down on the hostile Jovara Trojans: the ground forces go to action
        /// stations. The camera watches a gun battery open up on the frigate and a fighter lift off
        /// its pad; the frigate's side guns and CIWS fire back, then it turns its bow gun on the
        /// battery, and climbs back out to orbit.</summary>
        void GroundBattle(float t, float d)
        {
            var surf = landing != null ? landing.Surface : null;
            if (leaving)
            {
                if (landing.Current == PlanetLanding.State.InSpace) Phase(10, t);
                return;
            }
            if (landing == null) { Phase(10, t); return; }
            if (landing.Current != PlanetLanding.State.OnSurface || surf == null) return;   // still coming down
            Vector3 me = cc.transform.position;
            if (!visitStarted)
            {
                visitStarted = true;
                phaseAt = t;
                myHull = cc.GetComponent<Health>();
                gTarget = PickGroundTarget(surf);
                int units = 0, garrisons = 0;
                foreach (var g in surf.Garrisons) { garrisons++; units += g.AliveCount; }
                Log($"{T,5:F1}s   on the surface of {landing.Body?.displayName}: {landing.Site.description}; ground forces {units} in {garrisons} garrisons; " +
                    $"going for a {(gTarget ? gTarget.name : "nothing")} {(gTarget ? Vector3.Distance(gTarget.transform.position, me) : 0f):F0} m away");
                Shot(120, cc.transform, demo.ShipViewOffset, d * 1.6f, cc.Heading + 150f, 9f, 2f);
                cc.throttle = 0.4f;
            }
            float s = t - phaseAt;
            if (gTarget == null || !gTarget.Alive)
            {
                var next = PickGroundTarget(surf);
                if (gTargetName != "") Log($"{T,5:F1}s   the {gTargetName} is destroyed; on to {(next ? "a " + next.name : "nothing")}");
                gTarget = next;
                gTargetName = gTarget ? gTarget.name : "";
                if (gTarget) demo.SelectTarget(gTarget.transform);
            }
            Vector3 tp = gTarget ? gTarget.transform.position : me + cc.transform.forward * 1000f;
            Vector3 flat = tp - me;
            flat.y = 0f;
            float range = flat.magnitude;
            bool inSight = gTarget && Cover.Visible(me, tp + Vector3.up * 8f);
            // high enough to see over the hills to it
            HoldHeight(surf, inSight ? 260f : 420f);
            var gun = demo.MainGun;
            if (groundBeam)
            {
                var v = ShipAutopilot.BowOn(cc, tp + Vector3.up * 6f);
                demo.autoRudder = v.x;
                demo.autoPitch = v.y;
            }
            else
            {
                var v = ShipAutopilot.BowOn(cc, new Vector3(tp.x, me.y, tp.z));
                demo.autoRudder = range > 60f ? v.x : 0f;
                demo.autoPitch = 0f;
            }
            // close in until it is in sight and within about 1.5 km, then hold there
            cc.throttle = groundBeam ? 0f : range > 3200f ? 0.7f : range > 1500f || !inSight ? 0.3f : 0f;

            // action stations (the side guns hold fire for now, so the battery is whole for the bow gun)
            if (Cue(phaseAt + 3f) && gTarget)
            {
                demo.SelectTarget(gTarget.transform);
                gTarget.garrison?.Raise();
                Log($"{T,5:F1}s   the garrison is on alert ({gTarget.garrison?.AliveCount} units); {range:F0} m to the {gTarget.name}");
            }
            // the battery opening up on the frigate
            if (Cue(phaseAt + 5f) && gTarget) Shot(121, gTarget.transform, new Vector3(0f, 10f, 0f), 120f, Bearing(tp, me) + 25f, 9f, 0f);
            // a fighter climbing off its pad, or else the frigate under fire
            if (Cue(phaseAt + 11f))
            {
                Transform lifted = null;
                foreach (var g in surf.Garrisons)
                    foreach (var un in g.units)
                        if (un != null && un.Alive && un.TryGetComponent(out PadLaunch pl) && pl.Launched) { lifted = un.transform; break; }
                follow = lifted;
                if (lifted) Shot(122, lifted, Vector3.zero, 80f, lifted.eulerAngles.y + 20f, 8f, 0f);
                else Shot(123, cc.transform, demo.ShipViewOffset, d * 1.3f, FrameBearing(tp, me) + 200f, 8f, 0f);
                Log($"{T,5:F1}s   {(lifted ? $"a {lifted.name} lifting off its pad" : "no pad launch yet")}; frigate hull {myHull?.Current:F0}");
            }
            if (shot == 122 && follow != null)
            {
                cam.yaw = Mathf.LerpAngle(cam.yaw, follow.eulerAngles.y + 18f, Time.deltaTime * 1.5f);
                cam.pitch = Mathf.Lerp(cam.pitch, 8f, Time.deltaTime);
            }
            // the frigate under fire, from beyond the battery
            if (Cue(phaseAt + 17f)) Shot(123, cc.transform, demo.ShipViewOffset, d * 1.4f, FrameBearing(tp, me) + 200f, 10f, 3f);
            // the bow gun on the battery, once it's in sight: charging, firing, hitting
            if (!groundBeam && beamDoneAt < 0f && s >= 22f && gTarget && gun && gun.Current == MainBeam.State.Idle &&
                ((inSight && range < 2200f) || s > 38f))
            {
                groundBeam = true;
                demo.SelectTarget(gTarget.transform);
                demo.ChargeMainGun();
                Log($"{T,5:F1}s   bow gun charging on the {gTarget.name}: {Vector3.Distance(me, tp):F0} m, in sight {inSight}, its hull {gTarget.GetComponent<Health>()?.Current:F0}");
                Shot(124, gun.transform, Vector3.zero, 75f, cc.Heading + 150f, 8f, 0f);
            }
            if (shot == 124) cam.yaw = Mathf.LerpAngle(cam.yaw, cc.Heading + 150f, Time.deltaTime * 2f);
            if (gun && gun.Current == MainBeam.State.Charging && gun.ChargeTimeLeft < 3.5f && shot == 124)
                Shot(125, cc.transform, demo.ShipViewOffset, d * 0.85f, cc.Heading, 9f, 0f);
            if (shot == 125) FollowHeading(0f, 9f);
            if (gun && gun.Current == MainBeam.State.Firing && shot == 125)
            {
                Log($"{T,5:F1}s   bow gun firing, {ShipAutopilot.BowError(cc, tp):F0} deg off the bow");
                Shot(126, gTarget ? gTarget.transform : cc.transform, new Vector3(0f, 10f, 0f), 520f, Bearing(me, tp) + 70f, 14f, 0f);
            }
            if (groundBeam && gun && gun.Current == MainBeam.State.Idle && s > 24f && beamDoneAt < 0f)
            {
                beamDoneAt = t;
                groundBeam = false;
                Log($"{T,5:F1}s   the beam is done: the {gTargetName} it was aimed at is {(gTarget && gTarget.Alive ? "still standing" : "destroyed")}; ground units destroyed so far {surf.Destroyed.Count}");
                SideGuns(true);   // now everything on whatever is left
            }
            // pull back as the frigate climbs away
            if (beamDoneAt > 0f && Cue(beamDoneAt + 0.8f)) Shot(127, cc.transform, demo.ShipViewOffset, d * 1.8f, cc.Heading - 130f, 16f, 4f);
            bool hurt = myHull != null && myHull.Current < myHull.maxHealth * 0.45f;
            if ((beamDoneAt > 0f && t > beamDoneAt + 7f) || s > 62f || hurt)
            {
                int units = 0;
                foreach (var g in surf.Garrisons) units += g.AliveCount;
                Log($"{T,5:F1}s   taking off from {landing.Body?.displayName}{(hurt ? " (hull down to " + myHull.Current.ToString("F0") + ")" : "")}; ground units left {units}, destroyed {surf.Destroyed.Count}");
                demo.autoRudder = null; demo.autoPitch = null; demo.autoElevator = null;
                SideGuns(false);
                cc.throttle = 0.15f;
                landing.TakeOff();
                leaving = true;
            }
        }

        /// <summary>The frigate in orbit over Thalassa lands: it flies in over the island and sets
        /// down on the landing pad, then lifts off and flies on across the planet, low and fast,
        /// before climbing back to orbit. It opens the system map, browses it and warps to the
        /// hostile Jovara Trojans, lands, and fights the ground forces there (a gun battery,
        /// missile sites, fighters off their pads), turning its bow gun on the battery before
        /// climbing back out. It opens the map again and warps across the system to the
        /// Outer Drift, where a pirate cruiser and its drones ambush it from the rocks; it fights
        /// the drones off with its CIWS and side guns and turns its bow gun on the cruiser until it
        /// is destroyed.</summary>
        void SystemShots(float t)
        {
            float d = demo.ShipViewDistance;
            if (sysDrive == null) sysDrive = cc.GetComponent<WarpDrive>();
            if (landing == null) landing = PlanetLanding.Instance;
            if (pirates == null)
            {
                pirates = GameObject.Find("Pirates")?.GetComponent<EncounterGroup>();
                pirateCruiser = pirates != null ? pirates.pilots.FirstOrDefault(pl => pl != null && pl.style == CombatPilot.Style.Capital)?.transform : null;
            }
            float u = t - phaseAt;   // time in the current phase
            if (retryWarp >= 0 && t >= retryWarpAt)
            {
                int next = retryWarp;
                retryWarp = -1;
                Warp(t, next);
            }

            switch (sysPhase)
            {
                case 0:   // in orbit over Thalassa
                    if (Cue(0f))
                    {
                        cc.throttle = 0.15f;
                        SideGuns(false);
                        Shot(0, cc.transform, demo.ShipViewOffset, d * 1.7f, cc.Heading + 150f, 6f, 3f);
                    }
                    if (Cue(3.5f))
                    {
                        bool down = landing != null && landing.Land();
                        Log($"{T,5:F1}s   landing on Thalassa: {(down ? "descending" : "refused")}");
                        Phase(down ? 7 : 8, t);
                    }
                    break;

                case 7:   // Thalassa: in over the island and down onto the pad
                    SurfaceVisit(t, d, 70, true, 0f, 8, 11);
                    break;

                case 11:  // Thalassa: off across the planet, then back up
                    SurfaceRun(t, d);
                    break;

                case 8:   // back in orbit: the map, and on to the Jovara Trojans
                    if (Cue(phaseAt + 0.05f)) Shot(80, cc.transform, demo.ShipViewOffset, d * 1.6f, cc.Heading + 150f, 6f, 3f);
                    if (Cue(phaseAt + 1.6f)) { sysMap.Open(); Log($"{T,5:F1}s   map opened (selected {sysMap.Selected?.displayName})"); }
                    if (Cue(phaseAt + 2.7f)) sysMap.Select(WP("Steropes"));
                    if (Cue(phaseAt + 3.7f)) sysMap.Select(WP("Jovara"));
                    if (Cue(phaseAt + 4.7f)) sysMap.Select(WP("Jovara Trojans"));
                    if (Cue(phaseAt + 6.3f)) Warp(t, 1);
                    break;

                case 9:   // the Jovara Trojans: the ground forces, then back up
                    GroundBattle(t, d);
                    break;

                case 10:  // back over the Trojans: the map again
                    if (Cue(phaseAt + 0.05f)) Shot(100, cc.transform, demo.ShipViewOffset, d * 1.6f, cc.Heading + 25f, 8f, 2f);
                    if (Cue(phaseAt + 2f)) { sysMap.Open(); Phase(3, t); }
                    break;

                case 1:   // the first jump: charging, the jump, the tunnel
                case 4:
                    WarpShots(t, u, d);
                    if (sysDrive.Current == WarpDrive.State.Ready && u > 2f) Phase(sysPhase == 1 ? 2 : 5, t);
                    break;

                case 2:   // at the Jovara Trojans: a look at the field
                    if (Cue(phaseAt + 0.1f)) Shot(10, cc.transform, demo.ShipViewOffset, d * 1.6f, cc.Heading + 25f, 8f, 0f);
                    if (sysPhase == 2) FollowHeading(25f, 8f);
                    if (Cue(phaseAt + 4.5f))
                    {
                        bool down = landing != null && landing.Land();
                        string why = "";
                        if (!down) landing?.Candidate(out why);
                        Log($"{T,5:F1}s   landing on {sysDrive.Destination?.displayName}: {(down ? "descending" : "refused: " + why)}");
                        if (down) Phase(9, t);
                        else { sysMap.Open(); Phase(3, t); }
                    }
                    break;

                case 3:   // the map again: on to the Outer Drift
                    if (Cue(phaseAt + 1.0f)) sysMap.Select(WP("Arges"));
                    if (Cue(phaseAt + 2.0f)) sysMap.Select(WP("Kestrel"));
                    if (Cue(phaseAt + 3.0f)) sysMap.Select(WP("Outer Drift"));
                    if (Cue(phaseAt + 4.6f)) Warp(t, 4);
                    break;

                case 5:   // dropped out at the Outer Drift: the ambush
                    if (Cue(phaseAt + 0.1f)) Shot(20, cc.transform, demo.ShipViewOffset, d * 1.8f, cc.Heading + 140f, 10f, 4f);
                    if (Cue(phaseAt + 2.5f))
                    {
                        cc.throttle = 0.5f;
                        // for the reel the cruiser stands and fights rather than retreating behind a rock
                        var cp = pirateCruiser ? pirateCruiser.GetComponent<CombatPilot>() : null;
                        if (cp) cp.hideBelow = 0.15f;
                        if (pirates != null && !pirates.Active)
                        {
                            Log($"{T,5:F1}s   pirates not in sight yet: they break cover");
                            pirates.Spring();
                        }
                        Log($"{T,5:F1}s   AMBUSH at the Outer Drift; warp drive now: {(sysDrive.InCombat(out string why) ? "locked, " + why : "ready")}");
                    }
                    if (Cue(phaseAt + 3.5f) && pirateCruiser) Shot(21, pirateCruiser, Vector3.zero, 1100f, Bearing(pirateCruiser.position, cc.transform.position) + 35f, 9f, 3f);
                    if (Cue(phaseAt + 9.5f))
                    {
                        // CIWS and side guns on the drones
                        var drone = CombatPilot.Nearest(pirates.pilots.Where(pl => pl != null && pl.style == CombatPilot.Style.Fighter), cc.transform.position);
                        if (drone) demo.SelectTarget(drone.transform);
                        SideGuns(true);
                        ciwsSide = NearestDroneSide();
                        ciwsCam = ClusterMount(ciwsSide);
                        if (ciwsCam) Shot(22, ciwsCam.transform, new Vector3(0f, 3f, 0f), 30f, cc.Heading + 180f + ciwsSide * 50f, 22f, 0f);
                    }
                    if (shot == 22) cam.yaw = Mathf.LerpAngle(cam.yaw, cc.Heading + 180f + ciwsSide * 50f, Time.deltaTime * 2f);
                    if (Cue(phaseAt + 15.5f)) Shot(23, cc.transform, demo.ShipViewOffset, d * 1.5f, cc.Heading - 120f, 14f, 5f);
                    // turn the bow onto the cruiser and close in
                    if (u > 15.5f && pirateCruiser)
                    {
                        if (nextChargeAt < float.MaxValue) cc.throttle = 1f;     // close in fast
                        // while closing, cut between the cruiser and the frigate
                        if (nextChargeAt < float.MaxValue && u > 21.5f && t >= nextCut)
                        {
                            nextCut = t + 7f;
                            bool onCruiser = shot != 28;
                            if (onCruiser) Shot(28, pirateCruiser, Vector3.zero, 1000f, Bearing(pirateCruiser.position, cc.transform.position) - 30f, 8f, 4f);
                            else Shot(23, cc.transform, demo.ShipViewOffset, d * 1.3f, cc.Heading + 150f, 10f, 4f);
                        }
                        if (cc.target != pirateCruiser) demo.SelectTarget(pirateCruiser);
                        var v = ShipAutopilot.BowOn(cc, pirateCruiser.position);
                        demo.autoRudder = v.x;
                        demo.autoPitch = v.y;
                        if (Cue(phaseAt + 16f)) { Log($"{T,5:F1}s   closing on the cruiser ({Vector3.Distance(cc.transform.position, pirateCruiser.position) / 1000f:F1} km)"); nextChargeAt = t + 3f; }
                    }
                    // the bow gun: charge, fire, again if it's still flying
                    var gun = demo.MainGun;
                    var ch = pirateCruiser ? pirateCruiser.GetComponent<Health>() : null;
                    if (gun && ch && !ch.Dead && nextChargeAt > 0f && t >= nextChargeAt && gun.Current == MainBeam.State.Idle)
                    {
                        float km = Vector3.Distance(cc.transform.position, pirateCruiser.position) / 1000f;
                        if (km < 5.8f && Cover.Visible(cc.transform.position, pirateCruiser.position))
                        {
                            demo.ChargeMainGun();
                            nextChargeAt = float.MaxValue;
                            Log($"{T,5:F1}s   bow gun charging on the cruiser: {km:F1} km, {ShipAutopilot.BowError(cc, pirateCruiser.position):F0} deg off the bow, cruiser hull {ch.Current:F0}");
                            Shot(24, gun.transform, Vector3.zero, 75f, cc.Heading + 150f, 8f, 0f);
                        }
                        else nextChargeAt = t + 0.5f;
                    }
                    if (shot == 24) cam.yaw = Mathf.LerpAngle(cam.yaw, cc.Heading + 150f, Time.deltaTime * 2f);
                    if (gun && gun.Current == MainBeam.State.Charging && gun.ChargeTimeLeft < 3.5f && shot == 24)
                        Shot(25, cc.transform, demo.ShipViewOffset, d * 0.85f, cc.Heading, 9f, 0f);
                    if (shot == 25) FollowHeading(0f, 9f);
                    if (gun && gun.Current == MainBeam.State.Firing && shot == 25 && pirateCruiser)
                        Shot(26, pirateCruiser, Vector3.zero, 800f, Bearing(cc.transform.position, pirateCruiser.position) + 70f, 6f, 0f);
                    if (gun && gun.Current == MainBeam.State.Idle && nextChargeAt == float.MaxValue && ch && !ch.Dead)
                    {
                        Log($"{T,5:F1}s   the cruiser survived the beam (hull {ch.Current:F0}): charging again");
                        nextChargeAt = t + 1.5f;
                        Shot(23, cc.transform, demo.ShipViewOffset, d * 1.5f, cc.Heading - 120f, 14f, 5f);
                    }
                    if (ch && ch.Dead)
                    {
                        Log($"{T,5:F1}s   the pirate cruiser is destroyed");
                        demo.autoPitch = null;
                        Phase(6, t);
                    }
                    break;

                case 6:   // the end: pull back over the wreckage
                    if (Cue(phaseAt + 1.5f)) Shot(27, cc.transform, demo.ShipViewOffset, d * 1.4f, cc.Heading + 60f, 16f, 6f);
                    if (shot == 27) cam.distance = Mathf.Lerp(d * 1.4f, d * 3.5f, Smooth((t - phaseAt - 1.5f) / 5f));
                    if (Cue(phaseAt + 6.5f))
                    {
                        ReportSystems();
                        Log($"  warp trips: {sysDrive.Jumps}, last {sysDrive.LastTravelSeconds:F1} s over {sysDrive.LastDistance / 1000f:F0} km");
                        seconds = Mathf.Min(seconds, t + 0.5f);
                    }
                    break;
            }
        }

        void Warp(float t, int nextPhase)
        {
            var to = sysMap.Selected;
            float km = sysDrive.DistanceTo(to) / 1000f, eta = sysDrive.chargeTime + sysDrive.TravelTimeTo(to);
            bool ok = sysMap.WarpToSelected();
            Log($"{T,5:F1}s   warp to {to?.displayName}: {km:F0} km, {eta:F1} s ({(ok ? "engaged" : "refused: " + sysDrive.LastMessage)})");
            if (!ok)
            {
                // try again in a moment (still fighting something off, say)
                retryWarp = nextPhase;
                retryWarpAt = t + 1.5f;
                return;
            }
            Phase(nextPhase, t);
            Shot(nextPhase * 10 + 1, cc.transform, demo.ShipViewOffset, d0 * 1.0f, cc.Heading + 110f, 8f, 0f);
            arrivedLogged = false;
        }

        float d0 => demo.ShipViewDistance;
        int retryWarp = -1;
        float retryWarpAt;

        /// <summary>Charging from the side, the tunnel from behind, then from off the quarter.</summary>
        void WarpShots(float t, float u, float d)
        {
            int b = sysPhase * 10;
            if (sysDrive.Current == WarpDrive.State.Charging) cam.yaw = Mathf.LerpAngle(cam.yaw, cc.Heading + 110f, Time.deltaTime * 2f);
            if (sysDrive.Current == WarpDrive.State.Warping && shot == b + 1) Shot(b + 2, cc.transform, demo.ShipViewOffset, d * 0.9f, cc.Heading, 6f, 0f);
            if (shot == b + 2)
            {
                FollowHeading(0f, 6f);
                if (sysDrive.StateTime > Mathf.Min(4f, sysDrive.TravelTime * 0.5f)) Shot(b + 3, cc.transform, demo.ShipViewOffset, d * 1.1f, cc.Heading + 60f, 10f, 0f);
            }
            if (shot == b + 3) FollowHeading(60f, 10f);
            if (sysDrive.Current == WarpDrive.State.Arriving && !arrivedLogged)
            {
                arrivedLogged = true;
                Log($"{T,5:F1}s   dropped out of warp at {sysDrive.Destination?.displayName} after {sysDrive.LastTravelSeconds:F1} s in the tunnel");
            }
        }
    }
}

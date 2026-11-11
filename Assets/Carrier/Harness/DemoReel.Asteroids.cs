using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>The asteroid ambush reel's shot list (AsteroidAmbush.exe -reel).</summary>
    public partial class DemoReel
    {
        // ------------------------------------------------------------------ asteroid ambush shot list

        int wp = 1;
        float ambushT = -1f, minClearance = float.MaxValue;
        bool rounded;

        /// <summary>Steer along the route over the big asteroid and round its end.</summary>
        void FollowRoute(AmbushDirector amb)
        {
            var r = amb.route;
            if (r == null || r.Length < 2) return;
            Vector3 me = cc.transform.position;
            while (wp < r.Length - 1 && (Vector3.Distance(me, r[wp]) < 320f || Vector3.Dot(r[wp] - me, r[wp] - r[wp - 1]) < 0f)) wp++;
            var v = ShipAutopilot.BowOn(cc, r[wp]);
            demo.autoRudder = v.x;
            demo.autoPitch = v.y;
        }

        /// <summary>The frigate crosses the big asteroid with the travelling music, comes round its
        /// far end and is ambushed: fighters from under the rock (the music switches to the battle
        /// track), then the heavy cruiser and its drones from behind a boulder. The frigate fights
        /// back with its CIWS and side guns, then turns its bow gun on the cruiser.</summary>
        void AsteroidShots(float t, AmbushDirector amb)
        {
            float d = demo.ShipViewDistance;
            Vector3 me = cc.transform.position;
            var cruiser = Targets.All.Count > 0 ? Targets.All[0] : null;
            if (cruiserGuns == null && cruiser) cruiserGuns = cruiser.GetComponent<EnemyWeapons>();
            if (frame % 15 == 0 && !amb.Sprung) minClearance = Mathf.Min(minClearance, cc.ObstacleClearance(800f));

            if (bowOn && cc.target)
            {
                var v = ShipAutopilot.BowOn(cc, cc.target.position);
                demo.autoRudder = v.x;
                demo.autoPitch = v.y;
            }
            else FollowRoute(amb);

            // 0-7: under way over the asteroid, the gas giant ahead
            if (Cue(0f))
            {
                cc.throttle = 1f;
                SideGuns(false);
                Shot(0, cc.transform, demo.ShipViewOffset, d * 1.6f, cc.Heading + 25f, 7f, 0f);
            }
            if (shot == 0) { FollowHeading(25f, 7f); cam.distance = Mathf.Lerp(d * 1.6f, d * 1.15f, Smooth(t / 7f)); }

            // 7-13: low chase, the surface rushing by underneath
            if (Cue(7f)) Shot(1, cc.transform, demo.ShipViewOffset, d * 0.8f, cc.Heading, 3f, 0f);
            if (shot == 1) FollowHeading(0f, 3f);

            // 13 on: wide from the side as the end of the asteroid comes up
            if (Cue(13f)) Shot(2, cc.transform, demo.ShipViewOffset, d * 2.8f, cc.Heading - 70f, 18f, 0f);
            if (shot == 2) FollowHeading(-70f, 18f);

            // diving round the end, seen from below
            if (!rounded && wp >= amb.roundingIndex - 1 && !amb.Sprung)
            {
                rounded = true;
                Log($"{T,5:F1}s   rounding the end of the asteroid; closest approach to rock on the way across {minClearance:F0} m, collisions {cc.Collisions}");
                Shot(3, cc.transform, demo.ShipViewOffset, d * 1.5f, cc.Heading + 120f, -14f, 0f);
            }
            if (shot == 3) FollowHeading(120f, -14f);
            if (!amb.Sprung && t >= 34f) amb.Spring("the reel's fallback cue");

            // the ambush: the alarm on the frigate, then on one of the fighters coming out from under the rock
            if (amb.Sprung && ambushT < 0f)
            {
                ambushT = t;
                Log($"{T,5:F1}s   AMBUSH sprung ({amb.SprungBy})");
                cc.throttle = 0.45f;
                SideGuns(true);
            }
            if (ambushT < 0f) return;
            if (Cue(ambushT + 1.8f))
            {
                follow = NearestPilot(amb.firstWave);
                if (follow != null) Shot(4, follow, Vector3.zero, 90f, follow.eulerAngles.y + 20f, 8f, 0f);
                else Shot(4, cc.transform, demo.ShipViewOffset, d * 1.3f, cc.Heading + 140f, 10f, 0f);
            }
            float a = ambushT;
            if (shot == 4 && follow != null)
            {
                cam.yaw = Mathf.LerpAngle(cam.yaw, follow.eulerAngles.y + 18f, Time.deltaTime * 1.5f);
                cam.pitch = Mathf.Lerp(cam.pitch, 8f, Time.deltaTime);
            }

            // the CIWS cluster facing the fighters opens up
            if (Cue(a + 6f))
            {
                ciwsSide = NearestDroneSide();
                ciwsCam = ClusterMount(ciwsSide);
                if (ciwsCam) Shot(5, ciwsCam.transform, new Vector3(0f, 3f, 0f), 30f, cc.Heading + 180f + ciwsSide * 50f, 22f, 0f);
                Log($"  CIWS engaging: {CiwsStatus()}");
            }
            if (shot == 5) cam.yaw = Mathf.LerpAngle(cam.yaw, cc.Heading + 180f + ciwsSide * 50f, Time.deltaTime * 2f);

            // the second wave: the heavy cruiser rounding its boulder
            if (Cue(a + 10.5f) && cruiser) Shot(6, cruiser, Vector3.zero, 950f, Bearing(cruiser.position, me) + 35f, 9f, 3f);
            if (Cue(a + 13f)) Log($"  second wave in: {amb.SecondWaveIn}; cruiser in sight of the frigate: {(cruiser ? Cover.Visible(cruiser.position, me) : false)}");

            // in the thick of it
            if (Cue(a + 16.5f)) Shot(7, cc.transform, demo.ShipViewOffset, d * 1.8f, cc.Heading + 130f, 14f, 5f);

            // the bow gun on the cruiser: charging, firing, hitting
            if (Cue(a + 22f) && cruiser)
            {
                bowOn = true;
                Pick(0);
                demo.ChargeMainGun();
                var gun = demo.MainGun;
                if (gun) Shot(8, gun.transform, Vector3.zero, 75f, cc.Heading + 150f, 8f, 0f);
                Log($"  bow gun charging on the cruiser ({ShipAutopilot.BowError(cc, cruiser.position):F0} deg off the bow, in sight: {Cover.Visible(me, cruiser.position)})");
            }
            if (shot == 8) cam.yaw = Mathf.LerpAngle(cam.yaw, cc.Heading + 150f, Time.deltaTime * 2f);
            if (Cue(a + 28f)) Shot(9, cc.transform, demo.ShipViewOffset, d * 0.85f, cc.Heading, 9f, 0f);
            if (shot == 9) FollowHeading(0f, 9f);
            if (Cue(a + 32.6f) && cruiser) Shot(10, cruiser, Vector3.zero, 700f, Bearing(me, cruiser.position) + 70f, 6f, 0f);

            // pull back over the battle
            if (Cue(a + 38f))
            {
                bowOn = false;
                demo.autoPitch = null;
                demo.LevelOff();
                var f = NearestPilot(amb.firstWave);
                if (f) demo.SelectTarget(f);
                Shot(11, cc.transform, demo.ShipViewOffset, d * 1.4f, cc.Heading + 60f, 16f, 6f);
            }
            if (shot == 11) cam.distance = Mathf.Lerp(d * 1.4f, d * 4f, Smooth((t - (a + 38f)) / 8f));

            if (Cue(seconds - 0.5f)) ReportAmbush(amb);
        }

        Transform NearestPilot(CombatPilot[] pilots) => CombatPilot.Nearest(pilots, cc.transform.position)?.transform;

        void ReportAmbush(AmbushDirector amb)
        {
            ReportSystems();
            var hull = cc.GetComponent<Health>();
            if (hull) Log($"  frigate hull {hull.Current:F0}/{hull.maxHealth:F0}, destroyed {hull.TimesDestroyed}x, collisions with rock {cc.Collisions}");
            foreach (var pl in amb.firstWave.Concat(amb.secondWave))
            {
                if (pl == null) continue;
                var h = pl.GetComponent<Health>();
                var w = pl.GetComponent<EnemyWeapons>();
                Log($"  {pl.name}: {pl.Current}, destroyed {(h ? h.TimesDestroyed : 0)}x, hid {pl.TimesHidden}x, shots held for cover {(w ? w.HeldForCover : 0)}");
            }
            Log($"  music at the end: {MusicDirector.NowPlaying} (combat mix {MusicDirector.CombatMix:F2})");
        }
    }
}

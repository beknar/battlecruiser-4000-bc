using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>The frigate reel's shot list (CarrierDemo.exe -reel).</summary>
    public partial class DemoReel
    {
        // ------------------------------------------------------------------ frigate shot list

        int side = 1;
        bool bowOn;
        CarrierTurret ciwsCam;
        EnemyWeapons cruiserGuns;

        static float Bearing(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        }

        /// <summary>The frigate's weapons and shield: cannon shells and missiles on the shield,
        /// a CIWS mount shooting a drone, the cruiser's beam on the shield, the bow gun charging,
        /// firing and killing the cruiser, then everything at once on a drone. The side guns hold
        /// fire until the finale so the cruiser is whole when the bow gun fires.</summary>
        void FrigateShots(float t)
        {
            float d = demo.ShipViewDistance;
            var cruiser = Targets.All[0];
            if (cruiserGuns == null && cruiser) cruiserGuns = cruiser.GetComponent<EnemyWeapons>();
            Vector3 me = cc.transform.position;

            // steering: bow onto the target for the bow gun, otherwise keep a broadside
            if (bowOn && cc.target)
            {
                var v = ShipAutopilot.BowOn(cc, cc.target.position);
                demo.autoRudder = v.x;
                demo.autoPitch = v.y;
            }
            else if (cc.target) demo.autoRudder = ShipAutopilot.Broadside(cc, cc.target.position, side, 1300f);

            // 0-6: the frigate under way, its shield shimmering
            if (Cue(0f))
            {
                cc.throttle = 0.35f;
                SideGuns(false);
                Shot(0, cc.transform, demo.ShipViewOffset, d * 1.7f, 150f, 14f, -8f);
            }
            if (shot == 0) cam.distance = Mathf.Lerp(d * 1.7f, d * 1.1f, Smooth(t / 6f));

            // 6-14: the cruiser's cannon shells and missiles on the shield, seen from its side
            if (Cue(6f)) Shot(1, cc.transform, demo.ShipViewOffset, d * 1.25f, Bearing(cruiser.position, me) + 40f, 10f, 0f);
            if (shot == 1) cam.yaw = Mathf.LerpAngle(cam.yaw, Bearing(cruiser.position, me) + 40f, Time.deltaTime);
            if (Cue(5.5f) || Cue(9f)) cruiserGuns?.FireBurstNow();
            if (Cue(5f)) cruiserGuns?.FireSalvoNow();

            // 14-22: the CIWS cluster on the side facing the nearest drone, seen from outboard and
            // ahead of it with the conning tower behind, the mounts swinging out to shoot
            if (Cue(14f))
            {
                ciwsSide = NearestDroneSide();
                ciwsCam = ClusterMount(ciwsSide);
                if (ciwsCam) Shot(2, ciwsCam.transform, new Vector3(0f, 3f, 0f), 30f, cc.Heading + 180f + ciwsSide * 50f, 22f, 0f);
                Log($"  CIWS shot on the {(ciwsSide > 0 ? "starboard" : "port")} cluster ({(ciwsCam ? ciwsCam.Label : "none")})");
            }
            if (shot == 2)
            {
                cam.yaw = Mathf.LerpAngle(cam.yaw, cc.Heading + 180f + ciwsSide * 50f, Time.deltaTime * 2f);
                // if this cluster sits idle while the other one is shooting, cut across to it
                if (!switchedCluster && !ClusterBusy(ciwsSide) && ClusterBusy(-ciwsSide))
                {
                    switchedCluster = true;
                    ciwsSide = -ciwsSide;
                    ciwsCam = ClusterMount(ciwsSide);
                    if (ciwsCam) Shot(2, ciwsCam.transform, new Vector3(0f, 3f, 0f), 30f, cc.Heading + 180f + ciwsSide * 50f, 22f, 0f);
                    Log($"  CIWS shot cut to the {(ciwsSide > 0 ? "starboard" : "port")} cluster");
                }
            }
            if (Cue(16f) || Cue(18f) || Cue(20f) || Cue(21.9f)) Log($"  CIWS engaging: {CiwsStatus()}");

            // 22-30: the cruiser's beam on the shield, while the frigate swings its bow round
            if (Cue(22f))
            {
                bowOn = true;
                Pick(0);
                bool charging = cruiserGuns != null && cruiserGuns.ChargeBeamNow();
                Log($"  cruiser beam charging: {charging}");
                Shot(3, cc.transform, demo.ShipViewOffset, d * 1.15f, Bearing(me, cruiser.position) + 35f, 9f, 0f);
            }
            if (shot == 3) cam.yaw = Mathf.LerpAngle(cam.yaw, Bearing(me, cruiser.position) + 35f, Time.deltaTime);

            // 30-46: the bow gun: charging (close on the bow), firing (chase cam), hitting (on the cruiser)
            if (Cue(30f))
            {
                demo.ChargeMainGun();
                var gun = demo.MainGun;
                if (gun) Shot(4, gun.transform, Vector3.zero, 75f, cc.Heading + 150f, 8f, 0f);
            }
            if (shot == 4) cam.yaw = Mathf.LerpAngle(cam.yaw, cc.Heading + 150f, Time.deltaTime * 2f);
            if (Cue(36f)) Shot(5, cc.transform, demo.ShipViewOffset, d * 0.85f, cc.Heading, 9f, 0f);
            if (shot == 5) FollowHeading(0f, 9f);
            if (Cue(40.6f)) Shot(6, cruiser, Vector3.zero, 700f, Bearing(me, cruiser.position) + 70f, 6f, 0f);

            // 46-54: everything on a drone: side guns, CIWS, and its blasters on the shield
            if (Cue(46f))
            {
                bowOn = false;
                demo.autoPitch = null;
                demo.LevelOff();
                Pick(1);
                SideGuns(true);
                side = 1;
                cc.throttle = 0.6f;
                Shot(7, cc.transform, demo.ShipViewOffset, d * 1.2f, cc.Heading - 120f, 18f, 9f);
            }

            // 54-60: pull back
            if (Cue(54f)) Shot(8, cc.transform, demo.ShipViewOffset, d * 1.2f, cc.Heading + 60f, 16f, 6f);
            if (shot == 8) cam.distance = Mathf.Lerp(d * 1.2f, d * 3.5f, Smooth((t - 54f) / 6f));

            if (Cue(59.5f)) ReportSystems();
        }
    }
}

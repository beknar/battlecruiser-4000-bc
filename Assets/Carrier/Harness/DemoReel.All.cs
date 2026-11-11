using System.Collections;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The whole Star System in one reel: StarSystem.exe -reel &lt;folder&gt; -all. The scenes of
    /// the feature reel and the orbits and stations reel, in one story: the orbits and the
    /// Lagrange points, the Highport and its elevator, the ship's console and its captain, mining,
    /// then docking to sell the ore, the launch systems and stations, co-op (with -reelcoop),
    /// the fights (the ambush, the ground forces, the capital ships,
    /// the armed installations), the bases and landings, and the mini-warps home.
    /// </summary>
    public partial class DemoReel
    {
        /// <summary>Recording the reel of everything (-all).</summary>
        public static bool All;

        // the opening and closing lines (the feature reel's own unless a reel sets them)
        string introLine, endLine;

        IEnumerator AllReel()
        {
            recording = false;
            while (frame < Warmup) yield return null;
            landing = PlanetLanding.Instance;
            sysDrive = cc.GetComponent<WarpDrive>();
            hullF = cc.GetComponent<Health>();
            shieldF = cc.GetComponentInChildren<Shield>(true);
            orbitSys = OrbitalSystem.Instance;
            if (hullF != null) hullF.cannotDie = true;     // the reel must not end in a wreck
            SideGuns(false);
            bool coop = ReelCoopServer() != null;
            introLine = coop ? "Star System: orbits, stations, mining, combat, landing, mini-warps and co-op"
                             : "Star System: orbits, stations, mining, combat, landing and mini-warps";
            endLine = coop ? "Orbits  ·  stations  ·  mining  ·  combat  ·  landing  ·  co-op"
                           : "Orbits  ·  stations  ·  mining  ·  combat  ·  landing  ·  mini-warps";
            Note($"the whole reel: {MineralDeposit.All.Count} bodies with minerals, {WarpPoint.All.Count} places, " +
                 $"{Installation.All.Count} installations");
            if (orbitSys == null) { Note("no orbital system in the scene"); finished = true; yield break; }

            yield return Intro();
            // the system and how it moves
            yield return MapTimeLapse();
            yield return MoonTimeLapse();
            yield return LagrangeL1();
            yield return LagrangeJovara();
            yield return HighportAndElevator();
            yield return Bridge();
            // mining, and selling what it mined
            yield return MineAsteroid();
            yield return MineComet();
            yield return MineVein();
            yield return DockAtHighport();
            // what has been built round the system
            yield return Launchers();
            yield return Stations();
            // flying together
            yield return CoopFlight();
            // combat
            yield return Ambush();
            yield return GroundForces();
            yield return CapitalShips();
            yield return HostileBase("Corsair Haven", "ASTEROID BASE", "Corsair Haven: a pirate base in an asteroid, and its six gun emplacements", 4300f, 470);
            yield return HostileBase("Cinder Orbital Battery", "ORBITAL WEAPONS", "The Cinder Orbital Battery: gun platforms and a beam lance over Cinder's pole", 5600f, 475);
            // down to the surfaces, then home by mini-warp
            yield return SurfaceBases();
            yield return Landings();
            yield return Warps();

            ReportSystems();
            Note($"the end: warp trips {sysDrive.Jumps}, landings {landing.Landings}, dockings {(Docking.Instance != null ? Docking.Instance.Dockings : 0)}, " +
                 $"mined {demo.Cargo.TotalMined:F0} t");
            finished = true;
        }

        /// <summary>The ship's console, and the captain reacting to a hit on the shield.</summary>
        IEnumerator Bridge()
        {
            var hp = Inst("Thalassa Highport");
            if (hp == null || hullF == null) yield break;
            Repair();
            Place(hp.transform.TransformPoint(new Vector3(-2600f, 250f, 1400f)), hp.transform.position);
            cc.throttle = 0.15f;
            yield return Wait(1f);
            LookPast(430, hp.transform.position, D * 1.5f, -20f, 10f, 0.5f);
            cam.offset = ClearOfCaption(cam.yaw, D * 1.5f);
            yield return FadeIn();
            Caption("THE BRIDGE", "The console: shield, hull, weapons and alerts, and a captain who reacts to what happens, and says so", 7f);
            yield return Wait(2.5f);
            // a hit on the shield, off the bow
            hullF.Damage(450f, cc.transform.position + cc.transform.forward * 60f + cc.transform.right * 20f, HitKind.Cannon);
            yield return Wait(4.5f);
            yield return FadeOut();
            Repair();
            Clear();
            cc.throttle = 0f;
        }
    }
}

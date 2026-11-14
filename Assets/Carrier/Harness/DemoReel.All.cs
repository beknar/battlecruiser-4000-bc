using System.Collections;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The whole Star System in one reel: StarSystem.exe -reel &lt;folder&gt; -all. The scenes of
    /// the feature, orbits and frigates reels, and the newer ones, in one story: the ships on the
    /// start menu and each frigate close up; the orbits and the Lagrange points, the Highport and
    /// its elevator, the ship's console and its captain; the real-size worlds and the flight down
    /// from orbit to a pad; mining, then docking to sell the ore, refining, building and fitting a
    /// part, the crew and recruiting; a cruiser built at a shipyard, and the cruiser and the
    /// destroyers close up with their missiles; the launch systems, stations and the ten
    /// settlements; co-op (with -reelcoop) and the AI wingman (with -reelbot); the fights (the
    /// ambush, the ground forces, the capital ships, the armed installations); the bases and
    /// landings; the orbital strikes; a run on Pelagia's sea, and the mini-warps home.
    /// </summary>
    public partial class DemoReel
    {
        /// <summary>Recording the reel of everything (-all).</summary>
        public static bool All;

        // the opening and closing lines (the feature reel's own unless a reel sets them)
        string introLine, endLine;

        /// <summary>-reelscenes A,B,...: only those scenes (to try them out); all of them without it.</summary>
        static string[] onlyScenes;

        static bool Want(string scene)
        {
            if (onlyScenes == null)
            {
                string v = LaunchArgs.ValueOf("-reelscenes");
                onlyScenes = string.IsNullOrEmpty(v) ? new string[0] : v.Split(',');
            }
            if (onlyScenes.Length == 0) return true;
            foreach (var n in onlyScenes) if (scene == n.Trim() || scene.StartsWith(n.Trim() + ":")) return true;
            return false;
        }

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
            Captain.Index = 2;                              // Yuki Tanaka, as in the other reels
            bool coop = ReelCoopServer() != null;
            introLine = "Star System: ships, orbits, real-size worlds, mining, industry, shipyards, crew, settlements, combat, orbital strikes, landing, the sea" +
                        (coop ? " and co-op" : "");
            endLine = "Ships  ·  orbits  ·  mining  ·  industry  ·  shipyards  ·  combat  ·  strikes  ·  landing  ·  " + (coop ? "co-op" : "the sea");
            Note($"the whole reel: {MineralDeposit.All.Count} bodies with minerals, {WarpPoint.All.Count} places, " +
                 $"{Installation.All.Count} installations");
            if (orbitSys == null) { Note("no orbital system in the scene"); finished = true; yield break; }

            if (Want("Intro")) yield return Intro();
            // the ships: the start menu, and each frigate close up
            if (Want("ShipsMenu")) yield return ShipsMenu();
            if (Want("FrigatesShow")) yield return FrigatesShow();
            // the system and how it moves
            if (Want("MapTimeLapse")) yield return MapTimeLapse();
            if (Want("MoonTimeLapse")) yield return MoonTimeLapse();
            if (Want("LagrangeL1")) yield return LagrangeL1();
            if (Want("LagrangeJovara")) yield return LagrangeJovara();
            if (Want("HighportAndElevator")) yield return HighportAndElevator();
            if (Want("Bridge")) yield return Bridge();
            // the worlds at their real size, and the flight down from orbit to a pad
            if (Want("RealWorlds")) yield return RealWorlds();
            // mining, and selling what it mined
            if (Want("MineAsteroid")) yield return MineAsteroid();
            if (Want("MineComet")) yield return MineComet();
            if (Want("MineVein")) yield return MineVein();
            if (Want("DockAtHighport")) yield return DockAtHighport();
            // the ore refined, built into a part and fitted; the crew and recruiting
            if (Want("IndustryShow")) yield return IndustryShow();
            if (Want("CrewShow")) yield return CrewShow();
            // a cruiser built at a shipyard; the cruiser and the destroyers close up
            if (Want("ShipyardShow")) yield return ShipyardShow();
            if (Want("BigShipsShow")) yield return BigShipsShow();
            // what has been built round the system, and where people live
            if (Want("Launchers")) yield return Launchers();
            if (Want("Stations")) yield return Stations();
            if (Want("SettlementsShow")) yield return SettlementsShow();
            // flying together
            if (Want("CoopFlight")) yield return CoopFlight();
            // combat
            if (Want("Ambush")) yield return Ambush();
            if (Want("GroundForces")) yield return GroundForces();
            if (Want("CapitalShips")) yield return CapitalShips();
            if (Want("HostileBase:CorsairHaven")) yield return HostileBase("Corsair Haven", "ASTEROID BASE", "Corsair Haven: a pirate base in an asteroid, and its six gun emplacements", 4300f, 470);
            if (Want("HostileBase:CinderOrbitalBattery")) yield return HostileBase("Cinder Orbital Battery", "ORBITAL WEAPONS", "The Cinder Orbital Battery: gun platforms and a beam lance over Cinder's pole", 5600f, 475);
            // down to the surfaces, then home by mini-warp
            if (Want("SurfaceBases")) yield return SurfaceBases();
            if (Want("Landings")) yield return Landings();
            // strikes from orbit, and what they leave
            if (Want("StrikesShow")) yield return StrikesShow();
            if (Want("SeaShow")) yield return SeaShow();
            if (Want("Warps")) yield return Warps();

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

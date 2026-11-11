namespace CarrierGame.Demo
{
    /// <summary>
    /// What the captain reacts to and talks about: the headings of Resources/Captains/lines.txt.
    /// Each is also what the console shows as her reason, unless a reaction gives more detail
    /// ("6 HOSTILES NEARBY", "DOCKED: REPAIRED").
    /// </summary>
    public static class CaptainEvents
    {
        public const string
            ReportingForDuty = "REPORTING FOR DUTY", AllQuiet = "ALL QUIET", OnTheSurface = "ON THE SURFACE",
            Contact = "CONTACT", HostilesNearby = "HOSTILES NEARBY", ShieldsTakingFire = "SHIELDS TAKING FIRE",
            ReturningFire = "RETURNING FIRE", ShieldsDown = "SHIELDS DOWN", HullHit = "HULL HIT", HullCritical = "HULL CRITICAL",
            MissileInbound = "MISSILE INBOUND", PointDefenceFiring = "POINT DEFENCE FIRING", GunsFiring = "GUNS FIRING",
            MissilesAway = "MISSILES AWAY", LaunchingTheWing = "LAUNCHING THE WING", TargetLocked = "TARGET LOCKED",
            MainGunCharging = "MAIN GUN CHARGING", MainGunFiring = "MAIN GUN FIRING", TargetDestroyed = "TARGET DESTROYED",
            WarpDriveCharging = "WARP DRIVE CHARGING", InWarp = "IN WARP", Arrived = "ARRIVED", GoingDown = "GOING DOWN",
            Touchdown = "TOUCHDOWN", TakingOff = "TAKING OFF", Docking = "DOCKING", Docked = "DOCKED", Mining = "MINING",
            RockMinedOut = "ROCK MINED OUT", HoldFull = "HOLD FULL", Scanning = "SCANNING", ShipLost = "SHIP LOST",
            BackInAction = "BACK IN ACTION";

        /// <summary>All of them.</summary>
        public static readonly string[] All =
        {
            ReportingForDuty, AllQuiet, OnTheSurface, Contact, HostilesNearby, ShieldsTakingFire,
            ReturningFire, ShieldsDown, HullHit, HullCritical, MissileInbound, PointDefenceFiring,
            GunsFiring, MissilesAway, LaunchingTheWing, TargetLocked, MainGunCharging, MainGunFiring,
            TargetDestroyed, WarpDriveCharging, InWarp, Arrived, GoingDown, Touchdown, TakingOff,
            Docking, Docked, Mining, RockMinedOut, HoldFull, Scanning, ShipLost, BackInAction,
        };

        /// <summary>News that can't wait: she may cut in on herself to say it.</summary>
        public static readonly System.Collections.Generic.HashSet<string> Urgent = new System.Collections.Generic.HashSet<string>
        {
            HullHit, ShieldsDown, HullCritical, MissileInbound, ShipLost, Contact, TargetDestroyed,
            MainGunFiring, BackInAction, ReportingForDuty, Arrived,
        };

        /// <summary>Nothing is going on: she remarks on it now and then, unprompted.</summary>
        public static bool Idle(string ev) => ev == AllQuiet || ev == OnTheSurface;
    }
}

namespace CarrierGame
{
    /// <summary>
    /// How each kind of hostile flies: its speeds, how hard it turns and accelerates, how it
    /// attacks (fighters make passes, capital ships stand off and circle) and when it goes to
    /// ground in the rocks. One place for these numbers, used by the level builders and by the
    /// ground forces built at run time. A level may change a setting afterwards (how far a
    /// pilot looks for cover, say).
    /// </summary>
    public static class PilotPresets
    {
        /// <summary>A raider fighter: fast passes, breaking off close and extending to come round again.</summary>
        public static void Fighter(CombatPilot p)
        {
            p.style = CombatPilot.Style.Fighter;
            p.cruiseSpeed = 230f; p.attackSpeed = 320f; p.turnRate = 90f; p.acceleration = 150f;
            p.breakOffDistance = 420f; p.extendDistance = 1400f;
            p.hideBelow = 0.3f; p.hideTime = 5f;
        }

        /// <summary>A drone: slower than a fighter but turns tighter, with shorter passes.</summary>
        public static void Drone(CombatPilot p)
        {
            p.style = CombatPilot.Style.Fighter;
            p.cruiseSpeed = 190f; p.attackSpeed = 250f; p.turnRate = 110f; p.acceleration = 130f;
            p.breakOffDistance = 350f; p.extendDistance = 1000f;
            p.hideBelow = 0.35f; p.hideTime = 5f;
        }

        /// <summary>The cruiser lurking in the rocks: slow, standing off to shoot, hiding when hurt.</summary>
        public static void Cruiser(CombatPilot p)
        {
            p.style = CombatPilot.Style.Capital;
            p.cruiseSpeed = 75f; p.attackSpeed = 90f; p.turnRate = 12f; p.acceleration = 18f;
            p.standoff = 1800f; p.circleSpeed = 3f;
            p.hideBelow = 0.5f; p.hideTime = 8f; p.hideCooldown = 30f; p.hideSearch = 3000f;
        }

        /// <summary>A planet's gunship, off its landing pad.</summary>
        public static void GroundGunship(CombatPilot p)
        {
            p.style = CombatPilot.Style.Capital;
            p.cruiseSpeed = 100f; p.attackSpeed = 130f; p.turnRate = 18f; p.acceleration = 25f;
            p.standoff = 1800f; p.circleSpeed = 4f;
        }

        /// <summary>A planet's fighter (on patrol or off a pad): a little slower than a raider.</summary>
        public static void GroundFighter(CombatPilot p)
        {
            p.style = CombatPilot.Style.Fighter;
            p.cruiseSpeed = 200f; p.attackSpeed = 290f; p.turnRate = 85f; p.acceleration = 140f;
            p.breakOffDistance = 420f; p.extendDistance = 1300f;
        }
    }
}

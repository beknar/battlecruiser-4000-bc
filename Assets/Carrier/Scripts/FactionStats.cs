using System;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// A faction's numbers, from its file in Data/ (vrask.json, greys.json, empire.json): its
    /// bolts, each of its ships (its model, hull, shield, armour, how it flies and its weapons),
    /// the group it puts in a star system, how its ships fight together and, for the Vrask, the
    /// siege of a city. The scene builder reads it (CarrierBuilder.Faction) and bakes the numbers
    /// into the prefabs and the level, as it does Data/frigates.json, so a change shows after a
    /// rebuild. A field a faction has no use for is left out of its file.
    /// </summary>
    [Serializable]
    public class FactionTable
    {
        /// <summary>The faction's name, as TargetInfo.group has it ("Vrask", "Greys", "Empire");
        /// the Vrask's file, from before there were others, leaves it out.</summary>
        public string faction;
        /// <summary>What its builder's assets are named with ("Vrask": VraskBoardingPod,
        /// FX_VraskTractor); its name if left out.</summary>
        public string prefix;
        /// <summary>Its colours: its shield bubble, its beams (tractor), its pods' trails (r, g, b,
        /// brightness); the Vrask's green if left out.</summary>
        public float[] shieldColor, beamColor, trailColor;

        public FactionBolt[] bolts;
        public FactionShipStats[] ships;
        public FactionGroup group;
        /// <summary>The siege of a city from orbit (VraskBombardment).</summary>
        public FactionSiege siege;
        /// <summary>How a group of them fights together (VraskFleet, for the Vrask).</summary>
        public FactionFleetStats fleet;

        public static T Parse<T>(string json) where T : FactionTable => JsonUtility.FromJson<T>(json);
        public static FactionTable Parse(string json) => JsonUtility.FromJson<FactionTable>(json);

        /// <summary>A ship by its id ("Snare", "Fang", "Maw", "Skink"...), or null.</summary>
        public FactionShipStats Find(string id) => ships == null ? null : Array.Find(ships, s => s.id == id);

        /// <summary>A bolt by its name, or null.</summary>
        public FactionBolt Bolt(string name) => bolts == null ? null : Array.Find(bolts, b => b.name == name);

        /// <summary>Its name, the Vrask's if its file leaves it out.</summary>
        public string Name => string.IsNullOrEmpty(faction) ? VraskShip.Faction : faction;
        /// <summary>What its assets are named with.</summary>
        public string Prefix => string.IsNullOrEmpty(prefix) ? Name : prefix;

        public Color ShieldTint => Glow(shieldColor, new Color(0.45f, 1.6f, 0.55f));
        public Color BeamGlow => Glow(beamColor, new Color(0.35f, 1.4f, 0.6f) * 2.5f);
        public Color TrailGlow => Glow(trailColor, Color.clear);

        static Color Glow(float[] c, Color fallback) =>
            c != null && c.Length >= 3 ? new Color(c[0], c[1], c[2]) * (c.Length >= 4 ? c[3] : 1f) : fallback;
    }

    /// <summary>The Vrask's table (Data/vrask.json).</summary>
    [Serializable]
    public class VraskTable : FactionTable
    {
        public const string DataPath = "Assets/Carrier/Data/vrask.json";

        public static new VraskTable Parse(string json) => JsonUtility.FromJson<VraskTable>(json);
    }

    /// <summary>One kind of shot: what it does (`kind`, a HitKind: "Ion", "Cannon", "Penetrator"...),
    /// how hard and how fast, and how it looks (an additive glow of `color`: r, g, b, brightness).</summary>
    [Serializable]
    public class FactionBolt
    {
        public string name, material, kind;
        public float damage, speed;
        public float[] size, color;

        public HitKind Kind => Enum.TryParse(kind, out HitKind k) ? k : HitKind.Projectile;
        public Color Glow => color != null && color.Length >= 4 ? new Color(color[0], color[1], color[2]) * color[3] : Color.white;
    }

    /// <summary>
    /// A set of guns on a ship, all firing one bolt. `on` says where they are, from the model's
    /// hardpoints: "turrets" (the turrets cut out of the model, which traverse), "guns" (the fixed
    /// guns left in the hull: each an aiming joint at its mount that can swing its line of fire
    /// `cone` degrees), or a kind of point ("ion_muzzle", "spinal_muzzle", "pd"): guns there that
    /// swing `cone` degrees. `volley`: the points are one gun firing from all of them at once.
    /// </summary>
    [Serializable]
    public class FactionWeapon
    {
        public string label, on, bolt;
        public float interval, range, cone, yawSpeed;
        public bool volley;
    }

    /// <summary>One of the four Vrask ships.</summary>
    [Serializable]
    public class FactionShipStats
    {
        /// <summary>"Snare", "Fang", "Maw" or "Skink" (VraskShip.Role).</summary>
        public string id;
        public string prefab, model, texture, hardpoints;
        public string displayName, shipClass, threat, weapons, role;

        // toughness: the hull, the shield (the Vrask are shielded) and the armour plating between
        // them (Armor: the share of a hit it stops when whole, and how much it can stop)
        public float hull, shield, shieldRecharge, armor, plating;
        public float explosion, respawn;

        // flying (CombatPilot): "Capital" stands off and circles, "Fighter" makes attack runs
        public string style;
        public float cruiseSpeed, attackSpeed, turnRate, acceleration;
        public float standoff, circleSpeed, breakOffDistance, extendDistance, hideBelow;
        /// <summary>At its stand-off range it stops and holds its bow on the target (bow guns).</summary>
        public bool holdFacing;
        /// <summary>The ship (by id) it screens, and how far from it it fights.</summary>
        public string guard;
        public float guardLeash;
        /// <summary>Its tractor beam's reach (m), at its "tractor" point, and the Snare's tactics
        /// with it (SnareTactics); 0: none.</summary>
        public float tractorRange;
        /// <summary>Boarding pods (PodLauncher): how many a wave, how often (s), how far (m); 0: none.
        /// The Fang's come from its boarding_pod hatches as it closes (FangTactics); the Snare's from
        /// its tractor projector, at the ship its beam holds.</summary>
        public int pods;
        public float podInterval, podRange;
        /// <summary>Long-range artillery (ArtilleryGun: the Maw's spinal gun), and nuclear missiles
        /// (NukeLauncher: the Maw's silos); a range of 0, or a stock of 0: none.</summary>
        public FactionArtillery artillery;
        public FactionNukes nukes;
        /// <summary>An escort's work (the Skink: SkinkTactics, PointDefence): how far its sensors
        /// reach (m; the Maw fires at what they see), its patrol orbit round the ships it screens
        /// (m: nearest, farthest), how near the formation (m) it runs a threat down, and how many
        /// times as hard its point-defence shots are on a missile; 0 or none: not an escort.</summary>
        public float sensorRange;
        public float[] cap;
        public float interceptRange, pdMissileScale;

        // missiles (EnemyWeapons): none if salvoSize is 0
        public int salvoSize;
        public float salvoInterval, missileRange;

        public float[] engineColor;
        public FactionWeapon[] guns;

        // what the factions after the Vrask carry (0 or none: not this ship)
        /// <summary>How much of a sensor's range it is seen at (Signature; 0: none, seen as any
        /// ship), and kept to this share of it cloaked (Cloak; 0: no cloak).</summary>
        public float signature, cloak;
        /// <summary>A targeting laser's reach (m) from its "designator" point (Designator).</summary>
        public float designatorRange;
        /// <summary>A warp inhibitor's field (m) (Interdictor).</summary>
        public float interdict;
        /// <summary>A jammer (EcmEmitter), an EMP burst (EmpBurst), a drone bay (DroneBay).</summary>
        public FactionEcm ecm;
        public FactionEmp emp;
        public FactionDrones drones;

        public Color EngineGlow => engineColor != null && engineColor.Length >= 3 ? new Color(engineColor[0], engineColor[1], engineColor[2]) : new Color(0.45f, 1f, 0.6f);
    }

    /// <summary>A jammer: its field (m), its strength at its heart (0 to 1), how near (m) it
    /// scrambles the players' missiles and drones, and the ghost contacts it makes.</summary>
    [Serializable]
    public class FactionEcm
    {
        public float radius, strength, pdRadius;
        public int ghosts;
    }

    /// <summary>An EMP burst: its reach (m), its pulse (hull points), its charge and cooldown (s).</summary>
    [Serializable]
    public class FactionEmp
    {
        public float radius, strength, chargeTime, cooldown;
    }

    /// <summary>A drone bay: its stock, the most out at once, how many a launch, how often (s),
    /// and how near (m) a player's ship is when it launches.</summary>
    [Serializable]
    public class FactionDrones
    {
        public int stock, maxOut, perLaunch;
        public float launchInterval, launchRange;
    }

    /// <summary>Artillery: where it fires from (a kind of point), how often (s), how far (m: at
    /// most, at least), how far off the bow it can lay a shell (degrees); its shells' speed (m/s),
    /// damage at the centre of a blast `blastRadius` wide (m) and the share at its edge, and their
    /// glow (r, g, b, brightness).</summary>
    [Serializable]
    public class FactionArtillery
    {
        public string label, on;
        public float interval, range, minRange, cone, shellSpeed, damage, blastRadius, edge;
        public float[] color;
    }

    /// <summary>Nuclear missiles: how many the silos hold (filled again on respawn), the blast in
    /// space (damage at its centre, its reach in m, the share at its edge), how far (m) and how
    /// often (s) one is launched, how fast it flies (m/s) and how much point defence it takes.</summary>
    [Serializable]
    public class FactionNukes
    {
        public int stock;
        public float damage, blastRadius, edge, range, interval, speed, toughness;
    }

    /// <summary>The siege of a city from orbit (VraskBombardment): the group (`ship` and `escorts`
    /// of `escort`) on station `altitude` m over the settlement `at` (on a world with air), `side`
    /// m along; its raids every `intervalMinutes`, `shells` shells of `shellRadius` m on the
    /// defences within `siegeReach` m and then the city; a nuclear strike in `nukeChance` of the
    /// raids on a fortified city (defences round it, or `fortified`), at most once in
    /// `nukeRestockMinutes`, its ruins lasting `nukeRebuildMinutes` and killing
    /// `nukePopulationShare` of the people left. `note`: the destination's description;
    /// `cityNote`: added to the city's.</summary>
    [Serializable]
    public class FactionSiege
    {
        public string name, at, body, ship, escort, note, cityNote;
        public int escorts, shells;
        public float altitude, side, arrival, engageRange, respawnMinutes, intervalMinutes, shellRadius, siegeReach;
        public bool fortified;
        public float nukeChance, nukeRestockMinutes, nukeRebuildMinutes, nukeMargin, nukePopulationShare;
    }

    /// <summary>How a group fights together (VraskFleet), phase by phase: the line (m) the Snares
    /// and the Fang hold until their phase, the longest the bombardment goes on alone (s) and the
    /// target's shield share that ends it, how near a Snare or the Fang (m) the target is reacting,
    /// how long (s) a disabled target is free before the boarding is off, how long (s) the
    /// pursuit goes on before the next target, how far ahead of the rest (m) the Skinks picket and
    /// no nearer the target than (m), how far (m) any of its ships sees, and how near the group
    /// (m) a ship can be its target.</summary>
    [Serializable]
    public class FactionFleetStats
    {
        public string note;
        public float holdLine, bombardTime, bombardShield, reactRange, recoverTime, pursueTime;
        public float picketLead, picketStandoff, sensorRange, engageRange;
    }

    /// <summary>One ship's place in the raiding group, round the place it hunts at (degrees round
    /// from the side away from the star, metres out, metres up).</summary>
    [Serializable]
    public class FactionSlot
    {
        public string ship, name;
        public float angle, distance, height;
    }

    /// <summary>The raiding group: where it lies in wait (a warp point by name), how near it
    /// spots a ship, what the place's description adds, and its ships.</summary>
    [Serializable]
    public class FactionGroup
    {
        public string name, at, note;
        public float spotRange;
        public FactionSlot[] slots;
    }
}

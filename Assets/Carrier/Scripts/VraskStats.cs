using System;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// The Vrask's numbers, from Data/vrask.json: their bolts, each of their four ships (its
    /// model, hull, shield, armour, how it flies and its weapons) and the raiding group the star
    /// system is given. The scene builder reads it (CarrierBuilder.Vrask) and bakes the numbers into
    /// the prefabs and the level, as it does Data/frigates.json, so a change shows after a rebuild.
    /// </summary>
    [Serializable]
    public class VraskTable
    {
        public const string DataPath = "Assets/Carrier/Data/vrask.json";

        public VraskBolt[] bolts;
        public VraskShipStats[] ships;
        public VraskGroup group;

        public static VraskTable Parse(string json) => JsonUtility.FromJson<VraskTable>(json);

        /// <summary>A ship by its id ("Snare", "Fang", "Maw", "Skink"), or null.</summary>
        public VraskShipStats Find(string id) => ships == null ? null : Array.Find(ships, s => s.id == id);

        /// <summary>A bolt by its name, or null.</summary>
        public VraskBolt Bolt(string name) => bolts == null ? null : Array.Find(bolts, b => b.name == name);
    }

    /// <summary>One kind of shot: what it does (`kind`, a HitKind: "Ion", "Cannon", "Penetrator"...),
    /// how hard and how fast, and how it looks (an additive glow of `color`: r, g, b, brightness).</summary>
    [Serializable]
    public class VraskBolt
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
    public class VraskWeapon
    {
        public string label, on, bolt;
        public float interval, range, cone, yawSpeed;
        public bool volley;
    }

    /// <summary>One of the four Vrask ships.</summary>
    [Serializable]
    public class VraskShipStats
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

        // missiles (EnemyWeapons): none if salvoSize is 0
        public int salvoSize;
        public float salvoInterval, missileRange;

        public float[] engineColor;
        public VraskWeapon[] guns;

        public Color EngineGlow => engineColor != null && engineColor.Length >= 3 ? new Color(engineColor[0], engineColor[1], engineColor[2]) : new Color(0.45f, 1f, 0.6f);
    }

    /// <summary>One ship's place in the raiding group, round the place it hunts at (degrees round
    /// from the side away from the star, metres out, metres up).</summary>
    [Serializable]
    public class VraskSlot
    {
        public string ship, name;
        public float angle, distance, height;
    }

    /// <summary>The raiding group: where it lies in wait (a warp point by name), how near it
    /// spots a ship, what the place's description adds, and its ships.</summary>
    [Serializable]
    public class VraskGroup
    {
        public string name, at, note;
        public float spotRange;
        public VraskSlot[] slots;
    }
}

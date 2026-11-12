using System;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// One kind of ground unit's numbers, from Resources/Data/ground_units.json: what the target
    /// panel calls it, how much it takes to destroy and how big the blast, and its weapons (or,
    /// for the planet's fighters and gunships, how they fly). A kind leaves out what it hasn't
    /// got. The shapes the units are built from stay in GroundForces.
    /// </summary>
    [Serializable]
    public class GroundUnitStats
    {
        public string name, shipClass, weapons, threat;
        public float hull, explosion;

        // a turret (the battery)
        public float range, fireInterval, fireCone, yawLimit, minPitch, maxPitch, yawSpeed, pitchSpeed;
        // missiles (the missile site)
        public float missileRange, salvoInterval;
        public int salvoSize;
        // a gun firing bursts (the tank), and how far off its nose a unit fires
        public float gunRange, burstInterval, shotSpacing, spread, fireArc;
        public int burstShots;

        // flying (fighters and gunships)
        public float cruiseSpeed, attackSpeed, turnRate, acceleration;
        public float breakOffDistance, extendDistance, standoff, circleSpeed;
        /// <summary>How far it chases from home, how wide it circles on patrol, how high it
        /// patrols, and how fast it climbs off its pad.</summary>
        public float leashDistance, loiterRadius, patrolHeight, climb;

        // under the sea (sea bases and submarines): the least depth of water it is built in, and a
        // submarine's speed and room under its keel
        public float minDepth, keelClearance;

        /// <summary>How long it takes to be rebuilt once destroyed, in minutes of game time (0:
        /// never). It goes on off-screen: an area built again has back whatever has had its time.</summary>
        public float rebuildMinutes;
    }

    /// <summary>Every kind of ground unit's numbers (Resources/Data/ground_units.json).</summary>
    [Serializable]
    public class GroundUnitTable
    {
        public GroundUnitStats battery, missileSite, tank, fighter, gunship, seaBase, submarine;

        public const string Resource = "Data/ground_units";
        static GroundUnitTable loaded;

        /// <summary>The table, read the first time it is asked for.</summary>
        public static GroundUnitTable Get()
        {
            if (loaded != null) return loaded;
            var text = Resources.Load<TextAsset>(Resource);
            if (text == null) throw new InvalidOperationException($"Resources/{Resource}.json is missing");
            return loaded = Parse(text.text);
        }

        public static GroundUnitTable Parse(string json) => JsonUtility.FromJson<GroundUnitTable>(json);
    }
}

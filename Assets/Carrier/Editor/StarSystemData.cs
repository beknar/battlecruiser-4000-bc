using System;
using System.Collections.Generic;
using System.IO;
using CarrierGame;
using UnityEngine;

namespace Carrier.EditorTools
{
    /// <summary>
    /// The Kestrel system's planets, moons and asteroid fields, from Data/star_system.json, for
    /// the scene builder to place (CarrierBuilder.StarSystem) and set going (the masses,
    /// CarrierBuilder.Orbits). Only the builder reads it: the game gets the built scene.
    /// </summary>
    [Serializable]
    public class StarSystemData
    {
        /// <summary>A body placed round the star.</summary>
        [Serializable]
        public class Placed
        {
            public string name, description;
            public float angle, distanceKm, heightKm, radius, arrival;
            public float[] mapColor;
            public bool hostile;

            /// <summary>Where it is: `angle` degrees round the star, `distanceKm` out and `heightKm` up.</summary>
            public Vector3 Position
            {
                get
                {
                    float a = angle * Mathf.Deg2Rad;
                    return new Vector3(Mathf.Cos(a) * distanceKm, heightKm, Mathf.Sin(a) * distanceKm) * 1000f;
                }
            }

            public Color MapColor => new Color(mapColor[0], mapColor[1], mapColor[2]);
        }

        [Serializable]
        public class Planet : Placed
        {
            /// <summary>Its kind of world (a SurfaceKind), which also picks its look.</summary>
            public string surface;
            public float atmosphereScale, mass;
            /// <summary>Degrees a second it turns, unless it keeps pace with its elevator's top.</summary>
            public float spin;
            public bool spinMatchesElevator, yearSetsStarMass;

            public SurfaceKind Surface => (SurfaceKind)Enum.Parse(typeof(SurfaceKind), surface);
        }

        [Serializable]
        public class Moon
        {
            public string name, planet, description;
            public float angle, distanceKm, radius, mass;
            public int seed;
        }

        [Serializable]
        public class Field : Placed
        {
            public int rocks, seed;
        }

        public Planet[] planets;
        public Moon[] moons;
        public Field[] fields;

        public static string FilePath => Path.Combine(Application.dataPath, "Carrier", "Data", "star_system.json");

        static StarSystemData cached;
        static DateTime cachedAt;

        /// <summary>The file, read again whenever it has changed.</summary>
        public static StarSystemData Load()
        {
            var at = File.GetLastWriteTimeUtc(FilePath);
            if (cached == null || at != cachedAt)
            {
                cached = Parse(File.ReadAllText(FilePath));
                cachedAt = at;
            }
            return cached;
        }

        public static StarSystemData Parse(string json) => JsonUtility.FromJson<StarSystemData>(json);

        /// <summary>Each body's mass as a fraction of what it goes round, by name.</summary>
        public Dictionary<string, float> Masses
        {
            get
            {
                var m = new Dictionary<string, float>();
                foreach (var p in planets) m[p.name] = p.mass;
                foreach (var moon in moons) m[moon.name] = moon.mass;
                return m;
            }
        }

        /// <summary>The planet whose year sets the star's mass.</summary>
        public Planet YearPlanet => Array.Find(planets, p => p.yearSetsStarMass);
    }
}

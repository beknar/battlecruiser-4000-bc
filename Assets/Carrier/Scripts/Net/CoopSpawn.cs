using System;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// Enemies made during play (a drone launched from a bay), for co-op: what makes one tells
    /// this, and the co-op world (CoopWorld) sends it to the other players' games, where it is
    /// made again from the prefab its key names (CoopSpawnables) and follows the host's.
    /// Alone, or before co-op, nothing happens. Only the game that runs the enemies makes them
    /// (the host's, or one alone): what makes them is a host-only behaviour, off on a guest.
    /// </summary>
    public static class CoopSpawn
    {
        /// <summary>Set by the co-op world while it is up: an enemy made (its instance, its key).</summary>
        public static Action<GameObject, string> OnSpawned;
        /// <summary>Set by the co-op world: an enemy made during play is gone for good (taken
        /// back aboard, or destroyed with no respawn).</summary>
        public static Action<GameObject> OnGone;

        /// <summary>An enemy has just been made from the prefab under `key` in CoopSpawnables.</summary>
        public static void Spawned(GameObject enemy, string key)
        {
            if (enemy != null && !string.IsNullOrEmpty(key)) OnSpawned?.Invoke(enemy, key);
        }

        /// <summary>An enemy made during play is about to go for good.</summary>
        public static void Gone(GameObject enemy)
        {
            if (enemy != null) OnGone?.Invoke(enemy);
        }
    }
}

using System;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// The ships a player can fly (Resources/FrigateRoster.asset, made by the scene builder from
    /// Data/frigates.json): each one's name, what it is good at, its class (frigate, destroyer or
    /// cruiser), whether it has to be built first, and its prefab. The start menu
    /// offers them, the level puts the chosen one in (PlayerFrigate), and in co-op the others'
    /// ships are built from them.
    /// </summary>
    [CreateAssetMenu(menuName = "Carrier/Frigate roster")]
    public class FrigateRoster : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public string id;
            public string displayName;
            /// <summary>A few words: what kind of ship it is.</summary>
            public string role;
            public string description;
            public GameObject prefab;
            /// <summary>Frigate, Destroyer or Cruiser.</summary>
            public string category = Frigate;
            /// <summary>Built at a shipyard (industry.json ships) before it can be flown: the start
            /// menu offers it only once the player has one (IndustryLedger.Owns).</summary>
            public bool buildable;
        }

        public const string Frigate = "Frigate", Destroyer = "Destroyer", Cruiser = "Cruiser";

        public Entry[] frigates = new Entry[0];

        public const string Resource = "FrigateRoster";
        static FrigateRoster loaded;

        /// <summary>The roster (null in a build without one).</summary>
        public static FrigateRoster Get() => loaded != null ? loaded : (loaded = Resources.Load<FrigateRoster>(Resource));

        /// <summary>The starting frigate: the first in the roster.</summary>
        public Entry Default => frigates.Length > 0 ? frigates[0] : null;

        public Entry Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var e in frigates)
                if (e != null && string.Equals(e.id, id, StringComparison.OrdinalIgnoreCase)) return e;
            return null;
        }

        public int IndexOf(string id)
        {
            for (int i = 0; i < frigates.Length; i++)
                if (frigates[i] != null && string.Equals(frigates[i].id, id, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }
    }
}

using UnityEngine;

namespace CarrierGame
{
    /// <summary>What the target display says about a ship: its name, class and threat, and what
    /// size of ship it is (CIWS only engage drones and fighters).</summary>
    public class TargetInfo : MonoBehaviour
    {
        public enum Kind { Capital, Fighter, Drone }

        public string displayName;
        public string shipClass = "Unknown";
        public string faction = "Hostile";
        public string weapons = "None detected";
        public string threat = "Low";
        public Kind kind = Kind.Capital;

        public string Name => string.IsNullOrEmpty(displayName) ? gameObject.name : displayName;
        public bool IsSmallCraft => kind == Kind.Fighter || kind == Kind.Drone;
        public bool Hostile => faction == "Hostile";

        /// <summary>Every ship with target info in the scene.</summary>
        public static readonly System.Collections.Generic.List<TargetInfo> All = new System.Collections.Generic.List<TargetInfo>();
        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);
    }
}

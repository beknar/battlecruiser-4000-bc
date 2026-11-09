using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// How much harder the enemies are with more players in a level. Every game in a level's room
    /// works it out from the same list of players, so they agree: an enemy's hull and shields are
    /// <see cref="Toughness"/> times as strong, and its guns, missiles and beams hit
    /// <see cref="Firepower"/> times as hard. Toughness grows more slowly than the players' total
    /// firepower (each extra player adds most of a ship's worth, not all of it) and the enemies'
    /// hits grow a little, since their fire is split between more ships. Alone, both are 1.
    /// </summary>
    public static class CoopScaling
    {
        /// <summary>Extra hull and shields for each player after the first.</summary>
        public const float ToughnessPerPlayer = 0.6f;
        /// <summary>Extra damage from the enemies for each player after the first.</summary>
        public const float FirepowerPerPlayer = 0.12f;

        public static float Toughness(int players) => 1f + ToughnessPerPlayer * (Mathf.Max(1, players) - 1);
        public static float Firepower(int players) => 1f + FirepowerPerPlayer * (Mathf.Max(1, players) - 1);
    }
}

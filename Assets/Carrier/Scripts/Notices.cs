using System;

namespace CarrierGame
{
    /// <summary>
    /// Short messages for the player ("Mining laser on", "AMBUSH! Fighters inbound"). Gameplay
    /// posts them here without knowing who shows them: the HUD puts each one at the bottom of
    /// the screen for a moment, and the captain mouths it.
    /// </summary>
    public static class Notices
    {
        /// <summary>A message was posted (the HUD and the captain listen).</summary>
        public static event Action<string> Posted;

        /// <summary>The latest message, and how many there have been.</summary>
        public static string Last { get; private set; }
        public static int Count { get; private set; }

        public static void Post(string message)
        {
            Last = message;
            Count++;
            Posted?.Invoke(message);
        }
    }
}

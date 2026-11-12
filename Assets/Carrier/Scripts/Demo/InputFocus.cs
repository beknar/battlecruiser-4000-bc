using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// Who has the keys, mouse and pad: the game, or one panel over it (the start menu, the pause
    /// menu, the co-op panel, the system map or an industrial site's screen). Only one panel holds them at a time. A panel that has just
    /// let go still counts for that frame and the next, so the key that closed it isn't also read
    /// by the game.
    /// </summary>
    public static class InputFocus
    {
        public enum Panel { None, PauseMenu, CoopPanel, SystemMap, StartMenu, Industry }

        /// <summary>The panel that has the input (None: the game has it).</summary>
        public static Panel Holder { get; private set; }
        static Panel released;
        static int releasedFrame = -10;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Holder = Panel.None; released = Panel.None; releasedFrame = -10; }

        /// <summary>Take the input for `p`, unless another panel has it.</summary>
        public static bool Take(Panel p)
        {
            if (Holder != Panel.None && Holder != p) return false;
            Holder = p;
            return true;
        }

        /// <summary>Give the input back to the game (if `p` has it).</summary>
        public static void Release(Panel p)
        {
            if (Holder != p) return;
            Holder = Panel.None;
            released = p;
            releasedFrame = Time.frameCount;
        }

        public static bool Has(Panel p) => Holder == p;

        /// <summary>`p` let go this frame or the last.</summary>
        public static bool JustReleased(Panel p) => released == p && Time.frameCount - releasedFrame <= 1;

        /// <summary>A panel has the input, or has only just let go of it: the game reads none.</summary>
        public static bool PanelUp => Holder != Panel.None || Time.frameCount - releasedFrame <= 1;
    }
}

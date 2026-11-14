using System;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>The modes of play: each has its own set of keys (and pad buttons), so the same
    /// keys can do different things in each. Flying the ship is the same in every mode.</summary>
    public enum ModeOfPlay { Combat, Mining, Navigation, Services, Fleet }

    /// <summary>
    /// The mode the player is in (1-5 on the keyboard, Back + LB/RB on the pad), and its menu (Tab,
    /// Back + Y): the combat mode's targets, the mining mode's deposits, the navigation mode's
    /// points of interest, the services a site offers, the fleet. Every mode is in real time: the
    /// game never stops for one, nor for its menu. Some modes are taken up by themselves: the
    /// system map is navigation, arriving at a site is services.
    /// </summary>
    public static class Modes
    {
        public static readonly ModeOfPlay[] All = { ModeOfPlay.Combat, ModeOfPlay.Mining, ModeOfPlay.Navigation, ModeOfPlay.Services, ModeOfPlay.Fleet };

        public static ModeOfPlay Current { get; private set; } = ModeOfPlay.Combat;
        /// <summary>The mode before the last one taken up by itself (services, on arriving at a
        /// site): it goes back to it on leaving.</summary>
        public static ModeOfPlay Before { get; private set; } = ModeOfPlay.Combat;

        /// <summary>Raised when the mode changes (the new one).</summary>
        public static event Action<ModeOfPlay> Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Current = ModeOfPlay.Combat; Before = ModeOfPlay.Combat; Changed = null; }

        public static bool Is(ModeOfPlay m) => Current == m;

        /// <summary>Its number key (1-5).</summary>
        public static int Number(ModeOfPlay m) => Array.IndexOf(All, m) + 1;

        public static string Name(ModeOfPlay m)
        {
            switch (m)
            {
                case ModeOfPlay.Combat: return "COMBAT";
                case ModeOfPlay.Mining: return "MINING";
                case ModeOfPlay.Navigation: return "NAVIGATION";
                case ModeOfPlay.Services: return "SERVICES";
                default: return "FLEET";
            }
        }

        /// <summary>What it is for, in a few words.</summary>
        public static string Blurb(ModeOfPlay m)
        {
            switch (m)
            {
                case ModeOfPlay.Combat: return "targets, weapons, salvage";
                case ModeOfPlay.Mining: return "scan, choose a deposit, cut ore";
                case ModeOfPlay.Navigation: return "the map, points of interest, auto-travel";
                case ModeOfPlay.Services: return "what the station or base offers";
                default: return "your other ships";
            }
        }

        /// <summary>Change mode (the player's choice, or `auto`: taken up by itself, to go back
        /// from with Return). Closes the old mode's menu.</summary>
        public static void Set(ModeOfPlay m, bool auto = false)
        {
            if (m == Current) return;
            if (auto) Before = Current;
            Current = m;
            ModeMenu.Close();
            if (!auto) SoundFX.Ui(Sfx.UiLock);
            Notices.Post($"{Name(m)} mode: {Blurb(m)}");
            Changed?.Invoke(m);
        }

        /// <summary>Back to the mode before one taken up by itself (leaving a site).</summary>
        public static void Return(ModeOfPlay from)
        {
            if (Current == from && Before != from) Set(Before, false);
        }

        /// <summary>The next mode (+1) or the one before (-1).</summary>
        public static void Step(int dir)
        {
            int i = Array.IndexOf(All, Current);
            Set(All[(i + dir + All.Length) % All.Length]);
        }
    }
}

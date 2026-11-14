using System;
using System.Collections.Generic;
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
    /// system map is navigation while it is open, a site services while the ship is there; each
    /// goes back to the mode before when it is done (one inside another unwinding in turn), unless
    /// the player has chosen a mode since. A level starts in combat mode.
    /// </summary>
    public static class Modes
    {
        public static readonly ModeOfPlay[] All = { ModeOfPlay.Combat, ModeOfPlay.Mining, ModeOfPlay.Navigation, ModeOfPlay.Services, ModeOfPlay.Fleet };

        public static ModeOfPlay Current { get; private set; } = ModeOfPlay.Combat;

        // the modes taken up by themselves, and the mode each was taken up from (latest last)
        static readonly List<(ModeOfPlay taken, ModeOfPlay before)> auto = new List<(ModeOfPlay, ModeOfPlay)>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Reset();

        /// <summary>Combat mode, nothing to go back to (a level starting, or starting again).</summary>
        public static void Reset()
        {
            Current = ModeOfPlay.Combat;
            auto.Clear();
        }

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

        /// <summary>Change mode: the player's choice (which forgets the modes taken up by
        /// themselves), or `auto`, taken up by itself, to go back from with Return. Closes the old
        /// mode's menu.</summary>
        public static void Set(ModeOfPlay m, bool auto = false)
        {
            if (auto) Modes.auto.Add((m, Current));
            else Modes.auto.Clear();
            if (m == Current) return;
            Current = m;
            ModeMenu.Close();
            if (!auto) SoundFX.Ui(Sfx.UiLock);
            Notices.Post($"{Name(m)} mode: {Blurb(m)}");
        }

        /// <summary>Done with a mode taken up by itself (the map closed, the site left): back to
        /// the mode it was taken up from, if the game is still in it.</summary>
        public static void Return(ModeOfPlay from)
        {
            int i = auto.FindLastIndex(a => a.taken == from);
            if (i < 0) return;
            var before = auto[i].before;
            auto.RemoveAt(i);
            if (Current != from) return;
            // (and if the one before was itself taken up by something now done, further back)
            Current = before;
            ModeMenu.Close();
            Notices.Post($"{Name(before)} mode: {Blurb(before)}");
        }

        /// <summary>The next mode (+1) or the one before (-1).</summary>
        public static void Step(int dir)
        {
            int i = Array.IndexOf(All, Current);
            Set(All[(i + dir + All.Length) % All.Length]);
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The all-in-one reel's ships: the start menu stepping through every ship (the frigates, the
    /// destroyers and the cruiser) and the captains; each frigate close up, its guns on a target
    /// drone circling it; a heavy cruiser laid down at the Kestrel Deep Yard's shipyard, built and
    /// taken command of; then the cruiser and each destroyer shown the same way, with a missile
    /// salvo at the drone. The reel goes on in the starting frigate.
    /// </summary>
    public partial class DemoReel
    {
        /// <summary>A ship's line under its name: what it is, and what it carries.</summary>
        static string ShipLine(FrigateRoster.Entry e, FrigateStats st)
        {
            var parts = new List<string> { $"{e.role}: {st.turrets} turrets", $"{st.ciws} CIWS" };
            if (st.mainGuns == 0) parts.Add("no main gun");
            if (st.missiles > 0) parts.Add($"{st.missiles} missiles");
            parts.Add($"{st.speed:F0} m/s");
            parts.Add($"crew {st.crew}");
            return string.Join(", ", parts);
        }

        /// <summary>The start menu: every ship, then the captains.</summary>
        IEnumerator ShipsMenu()
        {
            var roster = FrigateRoster.Get();
            var menu = FindAnyObjectByType<StartMenu>();
            if (roster == null || roster.frigates.Length == 0 || menu == null) { Note("no ship roster or start menu"); yield break; }
            StartMenu.LiveSwap = true;
            var ships = roster.frigates;
            Note($"ships: {string.Join(", ", ships.Select(f => $"{f.displayName} ({f.category})"))}");
            Place(cc.transform.position, cc.transform.position + cc.transform.forward * 1000f);
            menu.Open();
            menu.Select(0);
            yield return FadeIn();
            int frigates = ships.Count(f => f.category == FrigateRoster.Frigate);
            Caption("THE SHIPS", $"Choose a ship and a captain: {frigates} frigates to start in, and destroyers and a cruiser to build at a shipyard", 5.5f);
            yield return Wait(3f);
            for (int i = 1; i < ships.Length; i++)
            {
                var e = ships[i];
                string was = ships[i - 1].category;
                if (e.category == FrigateRoster.Destroyer && was != FrigateRoster.Destroyer)
                    Caption("DESTROYERS", "Tougher and slower than any frigate: no main gun, more turrets, fewer CIWS, a magazine of missiles", 6f);
                else if (e.category == FrigateRoster.Cruiser && was != FrigateRoster.Cruiser)
                    Caption("THE CRUISER", "Tougher again, with more turrets and CIWS, a main gun, missiles and the biggest crew", 4.5f);
                menu.Select(i);
                yield return Wait(e.category == FrigateRoster.Frigate ? 1.5f : 1.9f);
            }
            yield return Wait(1f);
            menu.Select(0);
            yield return Wait(1f);
            int captain = Captain.Index;
            for (int k = 1; k <= Captain.Roster.Length; k++)
            {
                Captain.Index = (captain + k) % Captain.Roster.Length;
                yield return Wait(0.8f);
            }
            yield return Wait(0.6f);
            menu.Launch();          // the starting frigate, as it was
            StartMenu.LiveSwap = false;
            Arm();
            yield return FadeOut(0.4f);
        }

        /// <summary>Each frigate in turn, close up, its guns on a target drone; the starting one
        /// last (the reel goes on in it).</summary>
        IEnumerator FrigatesShow()
        {
            var roster = FrigateRoster.Get();
            if (roster == null) yield break;
            var frigates = roster.frigates.Where(f => f.category == FrigateRoster.Frigate).ToList();
            var order = frigates.Skip(1).Concat(frigates.Take(1)).ToList();
            for (int i = 0; i < order.Count; i++)
            {
                yield return Fly(order[i]);
                yield return Showcase(i, order[i], 4.5f, 4f);
                yield return FadeOut(0.4f);
            }
        }

        /// <summary>Swap a ship in where the player's is (off camera), crewed and armed.</summary>
        IEnumerator Fly(FrigateRoster.Entry e)
        {
            if (!string.Equals(PlayerFrigate.Flying, e.id, System.StringComparison.OrdinalIgnoreCase))
            {
                PlayerFrigate.SwapTo(e.id);
                yield return null;
            }
            Arm();
            var crew = cc.GetComponent<Crew>();
            if (crew != null) crew.SetAboard(crew.complement);
            if (cc.missiles != null) cc.missiles.Rearm();
            Note($"flying the {PlayerFrigate.Flying} ({FrigateClass.Of(cc)})");
        }

        /// <summary>A heavy cruiser laid down at the Kestrel Deep Yard, built, and taken command of.</summary>
        IEnumerator ShipyardShow()
        {
            var yard = Inst("Kestrel Deep Yard");
            var panel = IndustryPanel.Instance;
            var table = IndustryTable.Get();
            var cruiser = FrigateRoster.Get()?.frigates.FirstOrDefault(f => f.category == FrigateRoster.Cruiser);
            var recipe = cruiser != null ? table?.Ship(cruiser.id) : null;
            if (yard == null || panel == null || recipe == null || Docking.Instance == null) { Note("no shipyard to show"); yield break; }
            Repair();
            yield return DockAtPlace(yard, 640, "SHIPYARDS", "Destroyers and the cruiser are built at shipyards: in deep space, in orbit, and on planets, moons and an asteroid");
            if (Docking.Instance.Current != Docking.State.Docked) { Note("not docked at the yard: no shipyard"); yield return Leave(); yield break; }
            // the sections and parts, built at the factories beforehand (it takes a while), in the yard's storage
            var store = IndustryLedger.Site(yard.displayName).stock;
            foreach (var i in recipe.inputs) store.Add(i.item, i.tonnes);
            panel.Open(IndustryPanel.Page.Shipyard);
            panel.Show(IndustryPanel.Page.Shipyard);
            panel.SelectItem(cruiser.id);
            Caption("THE SHIPYARD", "Lay a ship down: hull frames, turret mounts, missile cells, a bridge, crew quarters and parts, from the yard's storage", 6f);
            yield return Wait(3f);
            panel.ActOnSelected(IndustryPanel.Action("queue"));
            Note($"laid down a {cruiser.displayName}: on the slips {IndustryLedger.Building(cruiser.id)}");
            yield return Wait(2f);
            Caption("ON THE SLIPS", $"The {cruiser.displayName} takes {recipe.seconds / 60f:F0} minutes to build, in game time, whether the ship stays or not: here sped up", 5f);
            IndustryLedger.SpeedUp = 300f;
            float until = Time.time + 12f;
            while (!IndustryLedger.Owns(cruiser.id) && Time.time < until) yield return null;
            IndustryLedger.SpeedUp = 1f;
            Note($"the {cruiser.displayName} built: {IndustryLedger.Owns(cruiser.id)}");
            yield return Wait(2f);
            panel.SelectItem("*berth:" + cruiser.id);
            var st = FrigateStats.Of(cruiser.prefab);
            Caption("TAKE COMMAND", $"Built and berthed with nobody aboard: take command, and the crew come across. The {cruiser.displayName} needs {st.crew}", 5.5f);
            yield return Wait(2.5f);
            panel.ActOnSelected(IndustryPanel.Action("command"));
            yield return Wait(2.5f);
            Arm();
            Note($"in command of the {PlayerFrigate.Flying} ({FrigateClass.Of(cc)}), crew {cc.GetComponent<Crew>()?.Aboard ?? 0}");
            yield return Leave();
        }

        /// <summary>The cruiser just built, then each destroyer: close up, guns and a missile
        /// salvo on the drone. Back to the starting frigate after.</summary>
        IEnumerator BigShipsShow()
        {
            var roster = FrigateRoster.Get();
            if (roster == null) yield break;
            var big = roster.frigates.Where(f => f.category == FrigateRoster.Cruiser)
                .Concat(roster.frigates.Where(f => f.category == FrigateRoster.Destroyer)).ToList();
            for (int i = 0; i < big.Count; i++)
            {
                yield return Fly(big[i]);
                yield return Showcase(10 + i, big[i], 4.5f, 5f);
                yield return FadeOut(0.4f);
            }
            // the rest of the reel in the starting frigate (the cruiser goes back to its berth)
            IndustryLedger.ReleaseCommand();
            yield return Fly(roster.frigates[0]);
        }
    }
}

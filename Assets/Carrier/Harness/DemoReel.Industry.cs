using System.Collections;
using System.Linq;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The all-in-one reel's newer scenes: the refineries and factories (refining at the Belt
    /// Smelter, building and fitting a part at the Kestrel Deep Yard, on the sites' screen), the
    /// crew (a heavy hit, the crew short and the frigate slowed, recruits signing on at Concord
    /// Colony), and the settlements (the colony stations, cities, military and industrial bases,
    /// in space and on the ground).
    /// </summary>
    public partial class DemoReel
    {
        /// <summary>Dock at a place, from off camera; returns once docked (or not).</summary>
        IEnumerator DockAtPlace(Installation st, int shot, string title, string sub)
        {
            var dock = Docking.Instance;
            Vector3 berth = st.BerthPosition;
            Vector3 way = st.berth != null ? st.berth.forward : st.transform.forward;
            Place(berth + way * 900f + Vector3.up * 120f, berth);
            yield return Wait(0.8f);
            LookPast(shot, st.transform.position, D * 2.2f, 26f, 12f, 1.5f);
            yield return FadeIn();
            Caption(title, sub, 5f);
            yield return Wait(2.5f);
            dock.Dock();
            float until = Time.time + 8f;
            while (dock.Current != Docking.State.Docked && Time.time < until) yield return null;
            yield return Wait(0.6f);
        }

        IEnumerator Leave()
        {
            var panel = IndustryPanel.Instance;
            if (panel != null && IndustryPanel.IsOpen) panel.Close();
            var dock = Docking.Instance;
            if (dock.Current == Docking.State.Docked) dock.Undock();
            yield return Wait(2f);
            yield return FadeOut();
            Clear();
        }

        IEnumerator IndustryShow()
        {
            var panel = IndustryPanel.Instance;
            var smelter = Inst("Belt Smelter");
            var yard = Inst("Kestrel Deep Yard");
            if (panel == null || smelter == null || yard == null || Docking.Instance == null) { Note("no industry to show"); yield break; }
            IndustryLedger.SpeedUp = 30f;
            Repair();
            var hold = demo.Cargo;
            hold.Restore(null, hold.Credits);
            hold.Add(Mineral.Iron, 14f);
            hold.Add(Mineral.Carbon, 2f);
            hold.Add(Mineral.Aluminium, 32f);

            // ---- the smelter: the ore stored and refined
            yield return DockAtPlace(smelter, 600, "REFINERIES", "The Belt Smelter, on a rock of the Inner Belt: mined ore refined into steel, alloys, wire and glass");
            panel.Open(IndustryPanel.Page.Storage);
            yield return Wait(0.3f);
            Caption("THE SITE'S SCREEN", "Docked or on its pad: move ore, refined goods and parts between the hold and the site's own storage", 4.5f);
            panel.SelectItem("*all");
            yield return Wait(1.5f);
            panel.ActOnSelected(IndustryPanel.Action("storeAll"));
            yield return Wait(3f);
            panel.Show(IndustryPanel.Page.Refine);
            Caption("REFINING", "Jobs take their ore from storage and run in game time, one after another, whether the ship stays or not", 6f);
            panel.SelectItem("SteelPlate");
            yield return Wait(1f);
            for (int i = 0; i < 2; i++) { panel.ActOnSelected(IndustryPanel.Action("queue")); yield return Wait(0.4f); }
            panel.SelectItem("AluminiumSheet");
            yield return Wait(0.6f);
            panel.ActOnSelected(IndustryPanel.Action("queue5"));
            panel.ActOnSelected(IndustryPanel.Action("queue"));
            yield return Wait(5f);
            var store = IndustryLedger.Site("Belt Smelter");
            float until = Time.time + 6f;
            while (store.refining.Count > 0 && Time.time < until) yield return null;
            panel.Show(IndustryPanel.Page.Storage);
            foreach (var id in new[] { "SteelPlate", "AluminiumSheet" }) { panel.SelectItem(id); panel.ActOnSelected(IndustryPanel.Action("loadAll")); }
            Caption("STORAGE", "Each site keeps its own; the frigate's hold is what is limited, so the goods are carried where they are needed", 4f);
            yield return Wait(4f);
            Note($"smelter: steel {hold.Amount("SteelPlate"):F0} t and sheet {hold.Amount("AluminiumSheet"):F0} t aboard");
            yield return Leave();

            // ---- the deep yard: a cargo pod built and fitted
            yield return DockAtPlace(yard, 602, "FACTORIES", "Kestrel Deep Yard, alone in deep space: refined materials built into ship parts");
            panel.Open(IndustryPanel.Page.Storage);
            panel.SelectItem("*all");
            panel.ActOnSelected(IndustryPanel.Action("storeAll"));
            panel.Show(IndustryPanel.Page.Build);
            Caption("BUILDING", "Thirteen ship parts: armour, shield emitters, fusion thrusters, rail barrels, a cargo pod...", 5f);
            panel.SelectItem("CargoPod");
            yield return Wait(1.5f);
            panel.ActOnSelected(IndustryPanel.Action("queue"));
            var site = IndustryLedger.Site("Kestrel Deep Yard");
            until = Time.time + 6f;
            while (site.stock.Amount("CargoPod") < 1f && Time.time < until) yield return null;
            yield return Wait(1.5f);
            panel.Show(IndustryPanel.Page.Fit);
            panel.SelectItem("CargoPod");
            Caption("FITTING", "Parts are fitted to the frigate's slots: the cargo pod makes the hold 30% bigger", 5f);
            yield return Wait(1.5f);
            panel.ActOnSelected(IndustryPanel.Action("fit"));
            yield return Wait(4f);
            Note($"yard: cargo pod fitted {cc.GetComponent<ShipFittings>()?.IsFitted("CargoPod")}, hold {hold.capacity:F0} t");
            yield return Leave();
            IndustryLedger.SpeedUp = 1f;
        }

        IEnumerator CrewShow()
        {
            var crew = cc.GetComponent<Crew>();
            var colony = Inst("Concord Colony");
            var panel = IndustryPanel.Instance;
            if (crew == null || colony == null || panel == null) { Note("no crew to show"); yield break; }
            Repair();
            crew.SetAboard(crew.complement);
            // near the colony: a heavy hit, and its cost
            Place(colony.BerthPosition + colony.transform.right * 2200f + Vector3.up * 300f, colony.transform.position);
            cc.throttle = 0.6f;
            yield return Wait(1f);
            Shot(610, cc.transform, demo.ShipViewOffset, D * 1.5f, Bearing(cc.transform.position, colony.transform.position) + 150f, 8f, 2f);
            yield return FadeIn();
            Caption("CREW", $"Every frigate has its crew: the {PlayerFrigate.Flying} {crew.complement}. Hull damage kills them", 5f);
            yield return Wait(2f);
            hullF.Damage(hullF.maxHealth * 0.55f);
            yield return Wait(3f);
            Caption("SHORT-HANDED", $"{crew.Aboard} of {crew.complement} left: working at {crew.Efficiency:P0}. Top speed, turning, shield and guns all suffer, and repairs don't bring them back", 6f);
            yield return Wait(5.5f);
            Note($"crew: {crew.Aboard} of {crew.complement}, {crew.Efficiency:P0}; top speed {cc.maxSpeed:F0} m/s");
            yield return FadeOut();
            Repair();
            cc.throttle = 0f;
            // (out of combat before docking: the hit counts as being under fire for a while)
            yield return Wait(sysDrive.recentHitSeconds + 0.5f);
            demo.Cargo.Restore(demo.Cargo.Contents(), Mathf.Max(demo.Cargo.Credits, CrewTable.Get().recruitCost * (crew.Missing + 10)));
            yield return DockAtPlace(colony, 612, "RECRUITING", "At the colonies, cities and bases, recruits sign on for credits, from a pool that refills over time");
            panel.Open(IndustryPanel.Page.Crew);
            panel.Show(IndustryPanel.Page.Crew);
            panel.SelectItem("*rall");
            yield return Wait(2f);
            panel.ActOnSelected(IndustryPanel.Action("recruit"));
            yield return Wait(3.5f);
            Note($"recruited at {colony.displayName}: {crew.Aboard} of {crew.complement}");
            yield return Leave();
        }

        IEnumerator SettlementsShow()
        {
            (string name, string title, string sub)[] space =
            {
                ("Concord Colony", "COLONY STATIONS", "Concord Colony: a habitat ring 1.2 km across round its hub, in orbit of Thalassa"),
                ("Arges Station", "COLONY STATIONS", "Arges Station: drum habitats on a spine, round Jovara's moon Arges"),
                ("Lantern Arcology", "POPULATION CENTRES", "The Lantern Arcology: towers 2.6 km across, alone in deep space"),
                ("Hollow Rock", "POPULATION CENTRES", "Hollow Rock: a city of towers and domes on a rock of the Inner Belt"),
                ("Aegis Fleet Base", "MILITARY BASES", "The Aegis Fleet Base at Thalassa's L4: hangars, armoured blocks and gun turrets"),
                ("Ochre Orbital Works", "INDUSTRIAL BASES", "Ochre Orbital Works: a refinery and a factory yard in orbit of Ochre"),
            };
            int n = 0;
            foreach (var (name, title, sub) in space)
            {
                var wp = WP(name);
                if (wp == null) { Note($"no {name}"); continue; }
                Jump(wp);
                yield return Wait(1f);
                Shot(620 + n, wp.transform, Vector3.zero, Mathf.Max(1800f, wp.bodyRadius * 3.2f), SunBehind(wp.Position, n % 2 == 0 ? 35f : -35f), 14f, 3f);
                yield return FadeIn();
                Caption(title, sub, 5f);
                yield return Wait(5f);
                yield return FadeOut();
                n++;
            }
            Clear();
            (string name, string title, string sub)[] ground =
            {
                ("Port Ochre", "POPULATION CENTRES", "Port Ochre: a city of towers round its spaceport on the desert world Ochre"),
                ("Pyrois Domes", "POPULATION CENTRES", "Pyrois Domes: a town under glass on Jovara's moon Pyrois"),
                ("Fort Brontes", "MILITARY BASES", "Fort Brontes: walls and gun towers round a spaceport, hangars and radar, on Jovara's moon Brontes"),
                ("Steropes Heavy Industries", "INDUSTRIAL BASES", "Steropes Heavy Industries: a power plant, refinery, factory and crane yards on Jovara's moon Steropes"),
            };
            n = 0;
            foreach (var (name, title, sub) in ground)
            {
                var wp = WP(name);
                if (wp == null) continue;
                Jump(wp);
                yield return Wait(1f);
                if (!landing.LandOn(wp)) { Note($"couldn't land at {name}"); continue; }
                yield return WaitLanding(PlanetLanding.State.OnSurface, 90f);
                if (landing.Current != PlanetLanding.State.OnSurface) { Note($"didn't get down at {name}: {landing.Current}"); continue; }
                yield return Wait(2f);
                var surf = landing.Surface;
                var group = surf != null ? surf.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Settlement: " + name) : null;
                if (group == null) { Note($"no settlement at {name}"); yield return TakeOffNow(); continue; }
                perFrame = () =>
                {
                    if (surf != null) HoldHeight(surf, 220f);
                    demo.autoPitch = 0f;
                    demo.autoRudder = 0f;
                };
                cc.throttle = 0.15f;
                Shot(630 + n * 2, group, Vector3.zero, 2300f, Bearing(cc.transform.position, group.position) + 35f, 22f, 4f);
                yield return FadeIn();
                Caption(title, sub, 6f);
                Note($"at {name}: {group.childCount} pieces");
                yield return Wait(5.5f);
                Shot(631 + n * 2, cc.transform, demo.ShipViewOffset, D * 2.4f, Bearing(cc.transform.position, group.position) - 30f, 12f, 3f);
                yield return Wait(4f);
                yield return FadeOut();
                perFrame = null;
                Clear();
                cc.throttle = 0f;
                yield return TakeOffNow();
                n++;
            }
        }

        /// <summary>One frigate's run on Pelagia's sea (the sea reel's).</summary>
        IEnumerator SeaShow()
        {
            var pelagia = WP("Pelagia");
            var entry = FrigateRoster.Get()?.Find(PlayerFrigate.Flying);
            if (pelagia == null || entry == null) { Note("no sea to show"); yield break; }
            Arm();
            yield return SeaRun(0, entry, pelagia);
            Repair();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using CarrierGame;
using CarrierGame.Demo;
using UnityEngine;

namespace Carrier.EditorTools
{
    /// <summary>
    /// The rest of the Nine Hells' fighting, for a frigate or two: 18 bands of pirates and Vrask,
    /// six easy, four harder, four hard and four bosses. Some lie in wait round a body, a belt, a
    /// comet or a station; others wander on a circuit round a world (EncounterPatrol), a hostile
    /// place of their own seen moving on the system map.
    /// </summary>
    public static partial class CarrierBuilder
    {
        /// <summary>A meteoroid stream (Havilah, Sheba): a field 50 km long and a couple wide.</summary>
        static bool IsStream(WarpPoint w) =>
            w != null && w.kind == WarpPoint.Kind.AsteroidField && w.description != null && w.description.StartsWith("A meteoroid stream");

        static void NineHellsEncounters(NineHellsRefs R)
        {
            var p = R.prefabs;
            void Fighter(CombatPilot pl) { PilotPresets.Fighter(pl); pl.hideSearch = 2500f; }
            void Drone(CombatPilot pl) { PilotPresets.Drone(pl); pl.hideSearch = 2500f; }
            Action<CombatPilot> Cruiser = pl => NhCapital(pl, 75f, 90f, 12f, 18f, 1900f, 3f);
            Action<CombatPilot> Frigate = pl => NhCapital(pl, 110f, 140f, 18f, 25f, 2300f, 4f);
            Action<CombatPilot> Dread = pl => NhCapital(pl, 45f, 55f, 6f, 8f, 3300f, 1.5f);
            (GameObject, string, Action<CombatPilot>, float, float) F(string n) => (p.enemyFighter, n, Fighter, 45f, 18f);
            (GameObject, string, Action<CombatPilot>, float, float) D(string n) => (p.enemyDrone, n, Drone, 40f, 35f);
            (GameObject, string, Action<CombatPilot>, float, float) C(string n) => (p.enemyCruiser, n, Cruiser, 120f, 180f);
            (GameObject, string, Action<CombatPilot>, float, float) Fr(string n) => (p.enemyDestroyer, n, Frigate, 90f, 180f);
            (GameObject, string, Action<CombatPilot>, float, float) Dd(string n) => (p.enemyDreadnought, n, Dread, 180f, 180f);
            WarpPoint W(string n) => R.sys.GetComponentsInChildren<WarpPoint>(true).FirstOrDefault(w => w.displayName == n);

            // where a band lies in wait by a place: off its side away from the star, a little way in;
            // in a field, `frac` of its radius from its middle (0: 0.4); in a meteoroid stream, among
            // its rocks, along it (it is only a couple of km wide, but 50 km long)
            Vector3 By(WarpPoint at, float frac, float turn = 0f)
            {
                if (at == null) return Vector3.zero;
                Vector3 c = at.Position;
                if (IsStream(at)) return c + at.transform.forward * (at.bodyRadius * (frac > 0f ? frac : 0.2f)) * (turn > 90f ? -1f : 1f) + Vector3.up * 300f;
                Vector3 away = Vector3.ProjectOnPlane(c - R.sys.position, Vector3.up);
                away = away.sqrMagnitude > 1f ? away.normalized : Vector3.forward;
                float r = at.kind == WarpPoint.Kind.AsteroidField ? at.bodyRadius * (frac > 0f ? frac : 0.4f) : at.bodyRadius + (at.arrivalDistance - at.bodyRadius) * frac;
                return c + Quaternion.AngleAxis(turn, Vector3.up) * away * r;
            }
            int made = 0;
            void Lying(string name, string tier, WarpPoint at, float frac, float turn, (GameObject, string, Action<CombatPilot>, float, float)[] ships, float spot = 6000f)
            {
                if (at == null) { Log($"nine hells: no place for {name}"); return; }
                var g = NhPirates(R, name, By(at, frac, turn), at.Position, spot, ships);
                // (the danger near it, not the place made the enemy's: a friendly world's surface
                // stays friendly, a friendly station stays one to dock at)
                at.dangerNear = true;
                at.description = (at.description + $" ({tier}: {name} lies in wait here.)").Trim();
                made++;
            }
            void LyingVrask(string name, string tier, WarpPoint at, float frac, float turn, (string, string, float, float, float)[] slots, float spot = 7000f)
            {
                if (at == null) { Log($"nine hells: no place for {name}"); return; }
                var g = NhVrask(R, name, By(at, frac, turn), spot, slots);
                at.dangerNear = true;
                at.description = (at.description + $" ({tier}: {name}, Vrask, lie in wait here.)").Trim();
                made++;
            }
            // a band on patrol round a world: its circuit `round` times the world's arrival distance out
            void Wander(EncounterGroup g, string name, string tier, WarpPoint round, float times, float hours, float phase, string desc)
            {
                if (g == null || round == null) return;
                var pat = g.gameObject.AddComponent<EncounterPatrol>();
                pat.body = round;
                pat.radius = round.arrivalDistance * times;
                pat.period = hours * 3600f;
                pat.phase = phase;
                pat.group = g;
                MakeWarpPoint(g.gameObject, name, WarpPoint.Kind.Patrol, $"{tier}: {desc} It patrols round {round.displayName}, seen on the system map.",
                              0f, 5000f, HostileWorks, null, true).kindLabel = "hostile patrol";
                made++;
            }
            EncounterGroup PatrolOf(string name, WarpPoint round, float times, float phase, (GameObject, string, Action<CombatPilot>, float, float)[] ships)
            {
                if (round == null) return null;
                Vector3 n = Vector3.up;
                Vector3 axis0 = Vector3.Cross(n, Vector3.right).normalized;
                Vector3 c = round.Position + Quaternion.AngleAxis(phase, n) * axis0 * round.arrivalDistance * times;
                return NhPirates(R, name, c, round.Position, 7000f, ships);
            }
            // a band of the Greys lying in wait by a place (CarrierBuilder.Greys)
            void LyingGreys(string name, string tier, WarpPoint at, float frac, float turn, (string, string, float, float, float)[] slots, float spot, string raids = null, string raidBody = null)
            {
                if (at == null) { Log($"nine hells: no place for {name}"); return; }
                GameObject Make(GameObject prefab, string n, Vector3 pos, Vector3 face, Action<CombatPilot> setup, float respawn) =>
                    NhEnemy(R, prefab, n, pos, face, setup, 180f, respawn);
                var g = FactionBand(greys, R.prefabs.greys, R.sys, R.sys, name, By(at, frac, turn), spot, slots, Make, AddGreysFleet);
                if (g == null) return;
                Warn(at, "", tier, name);
                if (!string.IsNullOrEmpty(raids))
                {
                    // its raids on a place for people (GreysRaid), told of in the place's description
                    var raid = g.gameObject.AddComponent<GreysRaid>();
                    raid.place = raids;
                    raid.body = raidBody;
                    raid.group = g;
                    var target = W(raids);
                    if (target != null && (target.description == null || !target.description.Contains("the Greys")))
                        target.description = (target.description + " The Greys raid it for people now and then: some of its staff have gone missing.").Trim();
                }
                made++;
            }
            // an Imperial band by a place, or on patrol round a world (CarrierBuilder.Empire)
            EncounterGroup EmpireAt(string name, Vector3 centre, (string, string, float, float, float)[] slots, float spot)
            {
                GameObject Make(GameObject prefab, string n, Vector3 pos, Vector3 face, Action<CombatPilot> setup, float respawn) =>
                    NhEnemy(R, prefab, n, pos, face, setup, 180f, respawn);
                return FactionBand(empire, R.prefabs.empire, R.sys, R.sys, name, centre, spot, slots, Make, AddEmpireFleet);
            }
            EncounterGroup VraskPatrolOf(string name, WarpPoint round, float times, float phase, (string, string, float, float, float)[] slots)
            {
                if (round == null) return null;
                Vector3 axis0 = Vector3.Cross(Vector3.up, Vector3.right).normalized;
                Vector3 c = round.Position + Quaternion.AngleAxis(phase, Vector3.up) * axis0 * round.arrivalDistance * times;
                return NhVrask(R, name, c, 8000f, slots);
            }

            // ---- easy (six)
            Lying("Scavenger skiffs", "Easy", W("Inner Belt"), 0f, 0f, new[] { F("Scavenger A"), F("Scavenger B") });
            Lying("Belt claim-jumpers", "Easy", W("Inner Belt"), 0f, 140f, new[] { F("Claim-jumper"), D("Claim-jumper Drone A"), D("Claim-jumper Drone B"), D("Claim-jumper Drone C") });
            Lying("Patmos ice thieves", "Easy", W("Comet Patmos"), 0.6f, 0f, new[] { F("Ice Thief A"), F("Ice Thief B") });
            LyingVrask("Vrask scout", "Easy", W("Paradise"), 0.7f, 60f, new[] { ("Skink", "Scout", 0f, 0f, 0f) });
            // (out at the belt's edge, across it from the Vrask brood: an easy fight that doesn't
            // wake the boss, 9 km off)
            Lying("Outer Belt lurkers", "Easy", W("Outer Belt"), 1.15f, 20f, new[] { F("Lurker A"), F("Lurker B"), F("Lurker C") });
            Wander(PatrolOf("Forge smugglers", W("The Forge"), 2.2f, 30f, new[] { F("Smuggler A"), F("Smuggler B"), D("Smuggler Drone") }),
                   "Forge smugglers", "Easy", W("The Forge"), 2.2f, 2f, 30f, "Two smugglers' fighters and a drone running contraband round The Forge.");

            // ---- harder (four)
            Lying("Anak frigate pair", "Harder", W("Anak"), 0.6f, 0f, new[] { Fr("Pirate Frigate Anak A"), Fr("Pirate Frigate Anak B") });
            LyingVrask("Vrask hunting pair", "Harder", W("Havilah"), 0f, 0f, new[] { ("Fang", "Hunter", 0f, 0f, 0f), ("Skink", "Hunter", 60f, 900f, 100f) });
            Lying("Fuel station raiders", "Harder", W("Rephaim Fuel Station"), 2.5f, 90f, new[] { F("Raider A"), F("Raider B"), F("Raider C"), F("Raider D"), F("Raider E"), F("Raider F") });
            Wander(PatrolOf("Rephaim corsair patrol", W("Rephaim"), 1.6f, 200f, new[] { C("Corsair Cruiser Rephaim"), F("Corsair Escort A"), F("Corsair Escort B") }),
                   "Rephaim corsair patrol", "Harder", W("Rephaim"), 1.6f, 3f, 200f, "A corsair cruiser and two fighters patrolling round Rephaim.");

            // ---- hard (four)
            Wander(PatrolOf("Judecca blockade", W("Judecca"), 1.5f, 90f, new[] { C("Blockade Cruiser"), Fr("Blockade Frigate A"), Fr("Blockade Frigate B"), F("Blockade Fighter A"), F("Blockade Fighter B"), F("Blockade Fighter C"), F("Blockade Fighter D") }),
                   "Judecca blockade", "Hard", W("Judecca"), 1.5f, 2.5f, 90f, "A pirate cruiser, two frigates and four fighters enforcing the pirates' blockade of their world.");
            Wander(VraskPatrolOf("Vrask strike group", W("Goliath"), 1.7f, 300f, new[] { ("Snare", "Striker", 0f, 0f, 0f), ("Fang", "Striker", 50f, 1600f, 150f), ("Skink", "Striker", -50f, 1200f, -100f) }),
                   "Vrask strike group", "Hard", W("Goliath"), 1.7f, 4f, 300f, "A Vrask Snare, Fang and Skink hunting round Goliath for ships to take.");
            Lying("Sheba destroyer squadron", "Hard", W("Sheba"), 0f, 0f, new[] { Fr("Squadron Destroyer A"), Fr("Squadron Destroyer B"), Fr("Squadron Destroyer C") });
            Lying("Ophir ambush", "Hard", W("Comet Ophir"), 0.6f, 0f, new[] { C("Ambush Cruiser"), D("Ambush Drone A"), D("Ambush Drone B"), D("Ambush Drone C"), D("Ambush Drone D") });

            // ---- bosses (four)
            Wander(PatrolOf("The Butcher's flagship", W("Nineveh"), 1.8f, 120f, new[] { Dd("Pirate Dreadnought Butcher"), C("Butcher's Cruiser A"), C("Butcher's Cruiser B"), F("Butcher's Fighter A"), F("Butcher's Fighter B") }),
                   "The Butcher's flagship", "Boss", W("Nineveh"), 1.8f, 5f, 120f, "The Butcher, the most feared of the pirate captains, in his dreadnought with two cruisers and fighters, prowling round Nineveh.");
            LyingVrask("Vrask brood", "Boss", W("Outer Belt"), 0f, 200f, new[]
            {
                ("Maw", "Brood", 0f, 0f, 0f), ("Maw", "Brood Two", 180f, 2400f, 300f), ("Snare", "Brood", 40f, 2200f, 150f), ("Snare", "Brood Two", -40f, 2200f, -150f),
                ("Fang", "Brood", 90f, 1800f, -200f), ("Skink", "Brood", 20f, 1100f, 100f), ("Skink", "Brood Two", -20f, 1100f, -100f),
            });
            Wander(PatrolOf("Behemoth battle group", W("Gilgal"), 2.4f, 250f, new[] { Dd("Pirate Dreadnought Behemoth"), Fr("Behemoth Escort A"), Fr("Behemoth Escort B"), Fr("Behemoth Escort C"), Fr("Behemoth Escort D") }),
                   "Behemoth battle group", "Boss", W("Gilgal"), 2.4f, 6f, 250f, "The dreadnought Behemoth and four escort frigates sweeping wide round Gilgal.");
            Wander(VraskPatrolOf("Vrask war fleet", W("Paradise"), 1.9f, 0f, new[]
                   {
                       ("Maw", "War", 0f, 0f, 0f), ("Snare", "War", 40f, 2200f, 150f), ("Snare", "War Two", -40f, 2200f, -150f), ("Fang", "War", 90f, 1800f, -200f),
                       ("Fang", "War Two", -90f, 1800f, 200f), ("Skink", "War", 20f, 1100f, 100f), ("Skink", "War Two", -20f, 1100f, -100f), ("Skink", "War Three", 200f, 1300f, 0f),
                   }),
                   "Vrask war fleet", "Boss", W("Paradise"), 1.9f, 4.5f, 0f, "Eight Vrask ships of all four kinds on the prowl round Paradise, the nearest the Vrask come to Heaven.");
            // ---- the Greys: a cell picking over the dead race's ruins on Goliath, a watcher at Ptolomea
            LyingGreys("Greys cell", "Harder", W("Goliath"), 0.5f, 140f, new[]
            {
                ("Reaper", "", 180f, 3000f, 200f), ("Shroud", "", 160f, 2200f, -150f), ("Lance", "A", 40f, 2400f, 250f),
                ("Lance", "B", -40f, 2400f, -250f), ("Scout", "", 0f, 4500f, 400f),
            }, 9000f, "Nineveh Observatory", "Nineveh");
            LyingGreys("Greys watcher", "Easy", W("Ptolomea"), 0.6f, 220f, new[] { ("Scout", "Ptolomea", 0f, 0f, 0f) }, 12000f);

            // ---- the Empire: a forward base's picket holding Emim (Rephaim's moon), and a battle group
            // on patrol round Rephaim, out towards Paradise
            var emim = W("Emim");
            if (emim != null && EmpireAt("Imperial forward base", By(emim, 0.5f, 30f), new[]
                {
                    ("Assault", "Emim", 0f, 0f, 0f), ("Assault", "Emim Two", 30f, 2500f, 200f), ("Patrol", "Emim", -30f, 2200f, -150f),
                    ("Patrol", "Emim Two", 60f, 2600f, 150f), ("Raider", "Emim", -70f, 4000f, 300f), ("Recon", "Emim", 0f, 9000f, 500f),
                }, 14000f) != null)
            {
                WarnEmpire(emim, "An Imperial forward base holds Emim, its picket in a disciplined line.", "Boss", "Imperial forward base");
                made++;
            }
            var rephaim = W("Rephaim");
            if (rephaim != null)
            {
                Vector3 axis0 = Vector3.Cross(Vector3.up, Vector3.right).normalized;
                Vector3 c = rephaim.Position + Quaternion.AngleAxis(120f, Vector3.up) * axis0 * rephaim.arrivalDistance * 1.7f;
                Wander(EmpireAt("Imperial battle group", c, new[]
                       {
                           ("Assault", "Battle", 0f, 0f, 0f), ("Patrol", "Battle", 40f, 2000f, 150f), ("Patrol", "Battle Two", -40f, 2000f, -150f),
                           ("Raider", "Battle", 90f, 3500f, 200f), ("Raider", "Battle Two", -90f, 3500f, -200f), ("Recon", "Battle", 0f, 8500f, 500f),
                       }, 12000f),
                       "Imperial battle group", "Harder", rephaim, 1.7f, 5f, 120f, "An Imperial battle group on patrol round Rephaim and out towards Paradise: an Assault frigate in a line with its escorts, Raiders and a Recon frigate.");
            }
            Log($"nine hells: {made} encounters");
        }
    }
}

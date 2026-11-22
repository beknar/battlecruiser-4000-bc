using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace CarrierGame.Tests
{
    /// <summary>The factions as data (FactionTable): the Vrask's file read as any faction's is,
    /// and the prisoners each faction holds, kept apart and saved, an old save's one count read as
    /// the Vrask's.</summary>
    public class FactionTests
    {
        [SetUp]
        public void SetUp() => WorldState.Clear();

        [TearDown]
        public void TearDown() => WorldState.Clear();

        [Test]
        public void TheVraskFileReadsAsAnyFactionsDoes()
        {
            string json = File.ReadAllText(VraskTable.DataPath);
            var asVrask = VraskTable.Parse(json);
            var asAny = FactionTable.Parse(json);
            Assert.That(asAny.Name, Is.EqualTo(VraskShip.Faction), "a file without a faction name is the Vrask's");
            Assert.That(asAny.Prefix, Is.EqualTo("Vrask"));
            Assert.That(asAny.ships.Length, Is.EqualTo(asVrask.ships.Length));
            for (int i = 0; i < asAny.ships.Length; i++)
            {
                Assert.That(asAny.ships[i].id, Is.EqualTo(asVrask.ships[i].id));
                Assert.That(asAny.ships[i].hull, Is.EqualTo(asVrask.ships[i].hull));
                Assert.That(asAny.ships[i].guns.Length, Is.EqualTo(asVrask.ships[i].guns.Length));
            }
            Assert.That(asAny.group.slots.Length, Is.EqualTo(asVrask.group.slots.Length));
            // (the Vrask's colours, as the builder had them before there were other factions)
            Assert.That(asAny.ShieldTint, Is.EqualTo(new Color(0.45f, 1.6f, 0.55f)));
            Assert.That(asAny.BeamGlow, Is.EqualTo(new Color(0.35f, 1.4f, 0.6f) * 2.5f));
        }

        [Test]
        public void AFactionsNameAndColoursComeFromItsFile()
        {
            var t = FactionTable.Parse("{\"faction\":\"Greys\",\"shieldColor\":[0.6,0.4,1.4,2]}");
            Assert.That(t.Name, Is.EqualTo("Greys"));
            Assert.That(t.Prefix, Is.EqualTo("Greys"));
            Assert.That(t.ShieldTint, Is.EqualTo(new Color(0.6f, 0.4f, 1.4f) * 2f));
        }

        [Test]
        public void EachFactionsPrisonersAreKeptApartAndSaved()
        {
            WorldState.TakePrisoners(12);                 // (no faction named: the Vrask's)
            WorldState.TakePrisoners(5, "Empire");
            WorldState.TakePrisoners(3, "Greys");
            WorldState.TakePrisoners(2, "Empire");
            Assert.That(WorldState.PrisonersOf(VraskShip.Faction), Is.EqualTo(12));
            Assert.That(WorldState.PrisonersOf("Empire"), Is.EqualTo(7));
            Assert.That(WorldState.PrisonersOf("Greys"), Is.EqualTo(3));
            Assert.That(WorldState.Prisoners, Is.EqualTo(22));

            var json = IndustrySave.ToJson(new IndustrySave.Data { world = WorldState.Snapshot() });
            WorldState.Clear();
            Assert.That(WorldState.Prisoners, Is.EqualTo(0));
            WorldState.Restore(IndustrySave.FromJson(json).world);
            Assert.That(WorldState.PrisonersOf(VraskShip.Faction), Is.EqualTo(12));
            Assert.That(WorldState.PrisonersOf("Empire"), Is.EqualTo(7));
            Assert.That(WorldState.PrisonersOf("Greys"), Is.EqualTo(3));
        }

        [Test]
        public void AnOldSavesPrisonersAreTheVrasks()
        {
            // (a save from before the other factions: one count, and no list)
            WorldState.Restore(new WorldState.Saved { prisoners = 40, shipsTaken = 2, held = null });
            Assert.That(WorldState.PrisonersOf(VraskShip.Faction), Is.EqualTo(40));
            Assert.That(WorldState.Prisoners, Is.EqualTo(40));
            Assert.That(WorldState.ShipsTaken, Is.EqualTo(2));
            // saved again: still in the old count, where an older game reads them, and not in the
            // list too (so they aren't counted twice)
            var s = WorldState.Snapshot();
            Assert.That(s.prisoners, Is.EqualTo(40));
            Assert.That(s.held, Is.Empty);
            WorldState.Restore(s);
            Assert.That(WorldState.PrisonersOf(VraskShip.Faction), Is.EqualTo(40));
        }

        [Test]
        public void BoardersWithoutAFactionAreTheVrask()
        {
            Assert.That(Boarders.FactionOr(null), Is.EqualTo(VraskShip.Faction));
            Assert.That(Boarders.FactionOr(""), Is.EqualTo(VraskShip.Faction));
            Assert.That(Boarders.FactionOr("Empire"), Is.EqualTo("Empire"));
            Assert.That(Boarders.FactionOf(null), Is.EqualTo(VraskShip.Faction));
        }
    }
}

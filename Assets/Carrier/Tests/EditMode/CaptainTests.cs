using System;
using System.Linq;
using CarrierGame.Demo;
using NUnit.Framework;

namespace CarrierGame.Tests
{
    /// <summary>The captain: her portraits and what she says.</summary>
    public class CaptainTests
    {
        [Test]
        public void EachMoodHasItsSteps()
        {
            Assert.That(Captain.Steps(Mood.Neutral), Is.EqualTo(0));
            Assert.That(Captain.Steps(Mood.Blink), Is.EqualTo(2));
            Assert.That(Captain.Steps(Mood.Talk), Is.EqualTo(2));
            foreach (Mood m in Enum.GetValues(typeof(Mood)))
                if (m != Mood.Neutral && m != Mood.Blink && m != Mood.Talk) Assert.That(Captain.Steps(m), Is.EqualTo(3), m.ToString());
            Assert.That(Captain.FacesWanted, Is.EqualTo(26));
        }

        [Test]
        public void FiveCaptainsHaveAllTheirPortraits()
        {
            Assert.That(Captain.Roster.Length, Is.EqualTo(5));
            for (int c = 0; c < Captain.Roster.Length; c++)
                Assert.That(Captain.FacesFound(c), Is.EqualTo(Captain.FacesWanted), Captain.Roster[c].name);
        }

        [Test]
        public void AMissingStepFallsBackToAFace()
        {
            for (int c = 0; c < Captain.Roster.Length; c++)
                foreach (Mood m in Enum.GetValues(typeof(Mood)))
                {
                    Assert.That(Captain.Face(c, m), Is.Not.Null);
                    Assert.That(Captain.Face(c, m, 99), Is.SameAs(Captain.Face(c, m)), "past the last step: the last");
                }
        }

        [Test]
        public void ThereAreEnoughLinesForEveryEvent()
        {
            Assert.That(CaptainLines.Count, Is.GreaterThanOrEqualTo(250));
            foreach (var ev in CaptainLines.Events)
                Assert.That(CaptainLines.CountFor(ev), Is.GreaterThanOrEqualTo(5), ev);
            Assert.That(CaptainLines.Events.Distinct().Count(), Is.EqualTo(CaptainLines.Events.Length));
        }

        [TestCase("6 HOSTILES NEARBY", "HOSTILES NEARBY")]
        [TestCase("HOSTILE NEARBY", "HOSTILES NEARBY")]
        [TestCase("DOCKED: REPAIRED", "DOCKED")]
        [TestCase("  ARRIVED ", "ARRIVED")]
        [TestCase("", "")]
        [TestCase(null, "")]
        public void AReasonBelongsToItsEvent(string reason, string ev) =>
            Assert.That(CaptainLines.EventOf(reason), Is.EqualTo(ev));

        [Test]
        public void EveryLineHasItsBlanksFilled()
        {
            // lines are drawn from a shuffled bag without repeats: twice as many draws as there are
            // lines empties whatever is left of the bag and then a full one, so every line is said
            foreach (var ev in CaptainLines.Events)
            {
                int n = CaptainLines.CountFor(ev);
                var said = new System.Collections.Generic.HashSet<string>();
                for (int i = 0; i < 2 * n; i++)
                {
                    string line = CaptainLines.Pick(ev, key => $"<{key}>");
                    Assert.That(line, Is.Not.Null.And.Not.Empty, ev);
                    StringAssert.DoesNotContain("{", line, ev);
                    StringAssert.DoesNotContain("}", line, ev);
                    said.Add(line);
                }
                Assert.That(said.Count, Is.EqualTo(n), $"{ev}: every line said (and no two the same)");
            }
        }

        [Test]
        public void ALineThatCantBeFilledIsntSaid()
        {
            foreach (var ev in CaptainLines.Events)
                for (int i = 0; i < 3; i++)
                {
                    string said = CaptainLines.Pick(ev, _ => null);
                    if (said != null) StringAssert.DoesNotContain("{", said, ev);
                }
            Assert.That(CaptainLines.Pick("NO SUCH EVENT", k => k), Is.Null);
        }
    }
}

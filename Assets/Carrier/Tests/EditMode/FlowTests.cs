using System.Collections.Generic;
using CarrierGame.Demo;
using NUnit.Framework;
using UnityEngine;

namespace CarrierGame.Tests
{
    /// <summary>How gameplay talks to the HUD and the controls without calling them: the target
    /// list, the messages, and which panel has the input.</summary>
    public class FlowTests
    {
        readonly List<GameObject> made = new List<GameObject>();

        Transform Thing(string name)
        {
            var go = new GameObject(name);
            made.Add(go);
            return go.transform;
        }

        [TearDown]
        public void Clear()
        {
            foreach (var go in made) if (go != null) Object.DestroyImmediate(go);
            made.Clear();
            Targets.Reset(null);
            foreach (var p in new[] { InputFocus.Panel.PauseMenu, InputFocus.Panel.CoopPanel, InputFocus.Panel.SystemMap })
                InputFocus.Release(p);
        }

        // ------------------------------------------------------------ Targets

        [Test]
        public void ALevelsTargetsStartFromItsScene()
        {
            Transform a = Thing("A"), b = Thing("B");
            Targets.Add(Thing("left over from the last level"));
            Targets.Reset(new[] { a, b });
            Assert.That(Targets.All, Is.EqualTo(new[] { a, b }));
        }

        [Test]
        public void SomethingIsOnlyListedOnce()
        {
            var a = Thing("A");
            Targets.Reset(null);
            Targets.Add(a);
            Targets.Add(a);
            Targets.Add(null);
            Assert.That(Targets.All.Count, Is.EqualTo(1));
        }

        [Test]
        public void WhoeverHoldsATargetHearsWhenItGoes()
        {
            Transform a = Thing("A"), b = Thing("B"), c = Thing("C");
            Targets.Reset(new[] { a, b, c });
            Transform gone = null;
            int at = -1;
            void Heard(Transform t, int i) { gone = t; at = i; }
            Targets.Removed += Heard;
            try
            {
                Targets.Remove(b);
                Targets.Remove(Thing("never listed"));   // nothing to hear
            }
            finally { Targets.Removed -= Heard; }
            Assert.That(gone, Is.SameAs(b));
            Assert.That(at, Is.EqualTo(1));
            Assert.That(Targets.All, Is.EqualTo(new[] { a, c }));
        }

        // ------------------------------------------------------------ Notices

        [Test]
        public void AMessageReachesEveryoneListening()
        {
            var heard = new List<string>();
            void Heard(string m) => heard.Add(m);
            int before = Notices.Count;
            Notices.Posted += Heard;
            try { Notices.Post("Mining laser on"); }
            finally { Notices.Posted -= Heard; }
            Notices.Post("after the HUD stopped listening");
            Assert.That(heard, Is.EqualTo(new[] { "Mining laser on" }));
            Assert.That(Notices.Count, Is.EqualTo(before + 2));
            Assert.That(Notices.Last, Is.EqualTo("after the HUD stopped listening"));
        }

        // ------------------------------------------------------------ InputFocus

        [Test]
        public void OnePanelHasTheInputAtATime()
        {
            Assert.That(InputFocus.Take(InputFocus.Panel.SystemMap), Is.True);
            Assert.That(InputFocus.Take(InputFocus.Panel.PauseMenu), Is.False, "not over the map");
            Assert.That(InputFocus.Has(InputFocus.Panel.SystemMap), Is.True);
            InputFocus.Release(InputFocus.Panel.PauseMenu);                     // not its to give back
            Assert.That(InputFocus.Has(InputFocus.Panel.SystemMap), Is.True);
            InputFocus.Release(InputFocus.Panel.SystemMap);
            Assert.That(InputFocus.Take(InputFocus.Panel.PauseMenu), Is.True);
        }

        [Test]
        public void TheGameWaitsAFrameAfterAPanelCloses()
        {
            InputFocus.Take(InputFocus.Panel.PauseMenu);
            Assert.That(InputFocus.PanelUp, Is.True);
            InputFocus.Release(InputFocus.Panel.PauseMenu);
            // the key that closed it is still down this frame
            Assert.That(InputFocus.Holder, Is.EqualTo(InputFocus.Panel.None));
            Assert.That(InputFocus.PanelUp, Is.True);
            Assert.That(InputFocus.JustReleased(InputFocus.Panel.PauseMenu), Is.True);
            Assert.That(InputFocus.JustReleased(InputFocus.Panel.SystemMap), Is.False);
        }

        [Test]
        public void ThePauseMenuCountsTheCoopPanelAsOpen()
        {
            Assert.That(GameMenu.IsOpen, Is.False);
            InputFocus.Take(InputFocus.Panel.CoopPanel);
            Assert.That(GameMenu.IsOpen, Is.True, "the co-op panel takes the keys and mouse too");
            Assert.That(SystemMap.IsOpen, Is.False);
        }
    }
}

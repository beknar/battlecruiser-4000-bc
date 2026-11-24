using System.Collections;
using CarrierGame.Demo;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CarrierGame.Tests
{
    /// <summary>The wingman's voice in play: a stock line is played from its clip, lines wait their
    /// turn, and the voice turned off says nothing.</summary>
    public class WingmanVoicePlayTests
    {
        GameObject go;
        bool was;

        [SetUp] public void SetUp() { was = WingmanVoice.Enabled; WingmanVoice.Enabled = true; }

        [TearDown]
        public void TearDown()
        {
            WingmanVoice.Enabled = was;
            if (go != null) Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator AStockLineIsPlayedFromItsClipAndTheNextWaits()
        {
            go = new GameObject("voice test");
            var v = go.AddComponent<WingmanVoice>();
            v.Say("holding position.");
            v.Say("docking.");
            yield return null;
            Assert.That(v.StockSpoken, Is.EqualTo(1), "the first at once");
            Assert.That(v.Speaking, Is.EqualTo("holding position."));
            var src = go.GetComponent<AudioSource>();
            Assert.That(src.clip, Is.Not.Null);
            // (the second once the first is over; it's short)
            float until = Time.realtimeSinceStartup + 6f;
            while (v.StockSpoken < 2 && Time.realtimeSinceStartup < until) yield return null;
            Assert.That(v.StockSpoken, Is.EqualTo(2), "then the next");
            v.Hush();
            WingmanVoice.Enabled = false;
            v.Say("holding position.");
            yield return null;
            Assert.That(v.StockSpoken, Is.EqualTo(2), "off: nothing said");
        }
    }
}

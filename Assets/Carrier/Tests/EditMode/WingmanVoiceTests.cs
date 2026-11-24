using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CarrierGame.Demo;
using NUnit.Framework;
using UnityEngine;

namespace CarrierGame.Tests
{
    /// <summary>The wingman's stock voice (Resources/WingmanVoice): the game matches a line to its
    /// clip just as the recording tool keyed it, every fixed reply has a clip, and every clip loads.</summary>
    public class WingmanVoiceTests
    {
        [System.Serializable] class Clip { public string key, clip, text; }
        [System.Serializable] class Index { public string voice; public Clip[] clips; }

        static Index Read()
        {
            var ta = Resources.Load<TextAsset>(WingmanVoice.Folder + "/index");
            Assert.That(ta, Is.Not.Null, "the index is there");
            return JsonUtility.FromJson<Index>(ta.text);
        }

        [Test]
        public void TheGameKeysALineAsTheToolDid()
        {
            var index = Read();
            Assert.That(index.clips.Length, Is.GreaterThan(100));
            foreach (var c in index.clips)
                Assert.That(WingmanVoice.Key(c.text), Is.EqualTo(c.key), $"'{c.text}'");
            Assert.That(WingmanVoice.Key("Holding  position!"), Is.EqualTo("holding position"));
            Assert.That(WingmanVoice.Key("I'll sell; the ORE"), Is.EqualTo("ill sell the ore"));
        }

        [Test]
        public void EveryFixedReplyAndEveryPlaceHasAClip()
        {
            foreach (var kv in WingmanReplies.All)
            {
                if (kv.Key == "help" || Regex.IsMatch(kv.Value, @"\{\w+\}")) continue;
                Assert.That(WingmanVoice.StockClip(kv.Value), Is.Not.Null, $"{kv.Key}: '{kv.Value}'");
            }
            Assert.That(WingmanVoice.StockClip(WingmanReplies.Get("heading", ("place", "Thalassa"))), Is.Not.Null);
            Assert.That(WingmanVoice.StockClip(WingmanReplies.Get("mine_field", ("field", "Outer Drift"))), Is.Not.Null);
            Assert.That(WingmanVoice.StockClip("back to mining."), Is.Not.Null);
            Assert.That(WingmanVoice.StockClip("transferred 500 cr to Ann; 1,200 cr left."), Is.Null, "numbers and names: the live voice's");
        }

        [Test]
        public void EveryClipLoads()
        {
            foreach (var c in Read().clips)
            {
                var clip = Resources.Load<AudioClip>(WingmanVoice.Folder + "/" + c.clip);
                Assert.That(clip, Is.Not.Null, c.text);
                Assert.That(clip.length, Is.InRange(0.3f, 20f), c.text);
            }
        }
    }
}

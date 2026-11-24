using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CarrierGame.Demo;
using NUnit.Framework;
using UnityEngine;

namespace CarrierGame.Tests
{
    /// <summary>The AI wingman's fixed replies (Resources/Data/wingman_replies.json): every one the
    /// bot asks for is in the file, every one in the file is asked for, and their blanks are the
    /// ones the stock voice knows how to record (places and fields).</summary>
    public class WingmanRepliesTests
    {
        [Test]
        public void EveryReplyTheBotSaysIsInTheFileAndEveryOneInTheFileIsSaid()
        {
            string src = File.ReadAllText(Path.Combine(Application.dataPath, "Carrier/Scripts/Net/CoopBot.cs"));
            var used = Regex.Matches(src, @"WingmanReplies\.Get\(""(\w+)""").Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToList();
            Assert.That(used.Count, Is.GreaterThan(10));
            foreach (var k in used) Assert.That(WingmanReplies.All.ContainsKey(k), Is.True, $"'{k}' is in the file");
            foreach (var k in WingmanReplies.All.Keys) Assert.That(used, Does.Contain(k), $"'{k}' is said by the bot");
        }

        [Test]
        public void TheBlanksArePlacesOrFields()
        {
            foreach (var kv in WingmanReplies.All)
                foreach (Match m in Regex.Matches(kv.Value, @"\{(\w+)\}"))
                    Assert.That(new[] { "place", "field" }, Does.Contain(m.Groups[1].Value), $"{kv.Key}: {{{m.Groups[1].Value}}}");
            Assert.That(WingmanReplies.Get("heading", ("place", "Heaven")), Is.EqualTo("heading for Heaven."));
            Assert.That(WingmanReplies.Get("mine_field", ("field", "Outer Drift")), Does.StartWith("mining in Outer Drift;"));
            Assert.That(WingmanReplies.Get("holding"), Is.EqualTo("holding position."));
        }
    }
}

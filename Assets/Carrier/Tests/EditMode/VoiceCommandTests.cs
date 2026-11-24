using System.Collections.Generic;
using System.Linq;
using CarrierGame.Demo;
using NUnit.Framework;

namespace CarrierGame.Tests
{
    /// <summary>Voice commands to the AI wingman: what was heard, as the order sent on the comms
    /// channel (filler left out, numbers as figures, the wingman's name put first), and the
    /// offline keyword list.</summary>
    public class VoiceCommandTests
    {
        static readonly List<string> One = new List<string> { "Wingman" };

        [TestCase("mine", "Wingman, mine")]
        [TestCase("um mine some asteroids please.", "Wingman, mine some asteroids please")]
        [TestCase("Wingman follow me", "Wingman follow me")]
        [TestCase("hey wingman, hold", "Hey wingman, hold")]
        [TestCase("all ships regroup", "All ships regroup")]
        [TestCase("warp to Heaven then dock", "Wingman, warp to Heaven then dock")]
        [TestCase("give me two thousand five hundred credits", "Wingman, give me 2500 credits")]
        [TestCase("give me a hundred credits", "Wingman, give me 100 credits")]
        [TestCase("pay me forty two", "Wingman, pay me 42")]
        [TestCase("transfer three thousand and fifty credits to Ann", "Wingman, transfer 3050 credits to Ann")]
        public void WhatWasSaidGoesToTheWingman(string heard, string sent)
        {
            Assert.That(VoiceCommand.ToChat(heard, One), Is.EqualTo(sent));
        }

        [Test]
        public void EveryOrderWordStillReadsAsAnOrder()
        {
            foreach (var said in new[] { "follow me", "regroup", "hold", "attack the corsair", "warp to Heaven", "dock", "mine at the Inner Belt",
                                         "sell", "cargo", "status", "help", "give me 500 credits", "chat off" })
            {
                string sent = VoiceCommand.ToChat(said, One);
                string rest = CoopBot.AddressedPart(sent, "Wingman");
                Assert.That(rest, Is.Not.Null, sent);
                Assert.That(CoopBot.ParseOrder(rest).HasValue, Is.True, $"\"{said}\" sent as \"{sent}\" is an order");
            }
        }

        [Test]
        public void NothingHeardOrNoWingmanSendsNothing()
        {
            Assert.That(VoiceCommand.ToChat("", One), Is.Null);
            Assert.That(VoiceCommand.ToChat("um", One), Is.Null);
            Assert.That(VoiceCommand.ToChat("mine", new List<string>()), Is.Null);
            Assert.That(VoiceCommand.ToChat("mine", null), Is.Null);
        }

        [Test]
        public void WithSeveralWingmenItGoesToTheOneLastSpokenTo()
        {
            var two = new List<string> { "Wingman", "Bravo" };
            Assert.That(VoiceCommand.ToChat("dock", two), Is.EqualTo("Wingman, dock"), "the first, before any");
            Assert.That(VoiceCommand.ToChat("dock", two, "Bravo"), Is.EqualTo("Bravo, dock"));
            Assert.That(VoiceCommand.ToChat("Wingman dock", two, "Bravo"), Is.EqualTo("Wingman dock"), "named: that one");
        }

        [Test]
        public void NumbersThatArentNumbersAreLeftAlone()
        {
            Assert.That(VoiceCommand.Numbers("attack a corsair"), Is.EqualTo("attack a corsair"));
            Assert.That(VoiceCommand.Numbers("follow the one in front"), Is.EqualTo("follow the 1 in front"));
            Assert.That(VoiceCommand.Numbers("two, three"), Is.EqualTo("2, 3"));
            Assert.That(VoiceCommand.Numbers("twenty five thousand"), Is.EqualTo("25000"));
        }

        [Test]
        public void TheKeywordListHasEveryOrderForEveryWingmanAndPlace()
        {
            var phrases = VoiceCommand.Phrases(new[] { "Wingman" }, new[] { "Inner Belt", "Heaven" });
            foreach (var want in new[] { "mine", "Wingman mine", "all ships regroup", "Wingman warp to Heaven", "mine at Inner Belt", "Wingman follow me" })
                Assert.That(phrases, Does.Contain(want));
            Assert.That(phrases.Distinct().Count(), Is.EqualTo(phrases.Length));
        }
    }
}

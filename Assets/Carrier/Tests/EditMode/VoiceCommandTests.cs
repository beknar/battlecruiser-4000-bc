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
        public void NumbersAreFiguresOnlyInACreditOrder()
        {
            Assert.That(VoiceCommand.ToChat("attack Snare Two", One), Is.EqualTo("Wingman, attack Snare Two"), "a ship's name keeps its number word");
            Assert.That(VoiceCommand.ToChat("follow the one in front", One), Is.EqualTo("Wingman, follow the one in front"));
            Assert.That(VoiceCommand.ToChat("Wingman give me two hundred", One), Is.EqualTo("Wingman give me 200"));
            Assert.That(VoiceCommand.Numbers("attack a corsair"), Is.EqualTo("attack a corsair"));
            Assert.That(VoiceCommand.Numbers("two, three"), Is.EqualTo("2, 3"));
            Assert.That(VoiceCommand.Numbers("twenty five thousand"), Is.EqualTo("25000"));
            Assert.That(VoiceCommand.Numbers("one two three"), Is.EqualTo("1 2 3"), "units after units aren't added");
            Assert.That(VoiceCommand.Numbers("five twenty"), Is.EqualTo("5 20"));
            Assert.That(VoiceCommand.Numbers("two and three"), Is.EqualTo("2 and 3"), "'and' joins only after a hundred or a thousand");
            Assert.That(VoiceCommand.Numbers("two hundred?"), Is.EqualTo("200?"));
        }

        [Test]
        public void AWingmanNamedIsTheOneTheNextOrderGoesTo()
        {
            var two = new List<string> { "Wingman", "Bravo" };
            Assert.That(VoiceCommand.ToChat("Bravo dock", two, null, out string to), Is.EqualTo("Bravo dock"));
            Assert.That(to, Is.EqualTo("Bravo"));
            Assert.That(VoiceCommand.ToChat("dock", two, to, out to), Is.EqualTo("Bravo, dock"), "then an order naming no one goes to Bravo");
            Assert.That(VoiceCommand.ToChat("hey Wingman, hold", two, to, out to), Is.EqualTo("Hey Wingman, hold"));
            Assert.That(to, Is.EqualTo("Wingman"));
            Assert.That(VoiceCommand.ToChat("all ships regroup", two, to, out to), Is.EqualTo("All ships regroup"));
            Assert.That(to, Is.Null, "to them all: no one in particular");
        }

        [Test]
        public void AMessageToAPersonGoesAsItWasSaid()
        {
            Assert.That(VoiceCommand.ToChat("Ann, follow me", One, null, out string to, new List<string> { "Ann" }), Is.EqualTo("Ann, follow me"));
            Assert.That(to, Is.Null);
        }

        [Test]
        public void TheKeywordListHasEveryOrderForEveryWingmanAndPlace()
        {
            var phrases = VoiceCommand.Phrases(new[] { "Wingman" }, new[] { "Inner Belt", "Heaven" });
            foreach (var want in new[] { "mine", "Wingman mine", "all ships regroup", "Wingman warp to Heaven", "mine at Inner Belt", "mine at the Inner Belt", "Wingman follow me" })
                Assert.That(phrases, Does.Contain(want));
            Assert.That(phrases.All(p => p.Trim().Length > 0), Is.True, "no empty phrase");
        }
    }
}

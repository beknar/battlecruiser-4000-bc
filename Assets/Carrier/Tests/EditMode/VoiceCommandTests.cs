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
        public void TheFightOrdersAreOrdersAndInTheKeywordList()
        {
            var phrases = VoiceCommand.Phrases(One, new string[0]);
            foreach (var said in new[] { "attack my target", "defend me", "cover me", "help me", "I'm under attack", "where are you" })
            {
                Assert.That(VoiceCommand.IsOrder(VoiceCommand.ToChat(said, One), One), Is.True, said);
                Assert.That(phrases, Does.Contain("Wingman " + said));
            }
            // (of the browser's guesses, the one that's an order)
            Assert.That(VoiceCommand.Choose(new List<string> { "Wingman, where are you going", "Wingman, where are you" }, One, null, out _, out _),
                        Is.EqualTo("Wingman, where are you"));
        }

        [Test]
        public void TheOreOrdersAreOrdersAndInTheKeywordList()
        {
            var phrases = VoiceCommand.Phrases(One, new string[0]);
            foreach (var said in new[] { "mine platinum", "mine gold", "mine helium three", "mine the most valuable ore", "mine the best rocks" })
            {
                string sent = VoiceCommand.ToChat(said, One);
                Assert.That(VoiceCommand.IsOrder(sent, One), Is.True, said);
                var p = CoopBot.ParseOrder(CoopBot.AddressedPart(sent, "Wingman"));
                Assert.That(p.HasValue && p.Value.kind == "mine", Is.True, sent);
                CoopBot.MineTarget(p.Value.arg, out var choice);
                Assert.That(choice.kind, Is.Not.EqualTo(Wingman.OreChoice.Kind.Nearest), $"\"{said}\": rocks chosen");
                Assert.That(phrases, Does.Contain("Wingman " + said));
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

        // (every one of these was heard in the co-op session of 24-25 Nov 2026: docs/coop-session-2026-11-24.md)
        [TestCase("only man mine", "Wingman, mine")]
        [TestCase("women mine", "Wingman, mine")]
        [TestCase("women status", "Wingman, status")]
        [TestCase("amen scavenge", "Wingman, scavenge")]
        [TestCase("hey man mine", "Wingman, mine")]
        [TestCase("hey man I'm under attack", "Wingman, I'm under attack")]
        [TestCase("wing man follow me", "Wingman, follow me")]
        [TestCase("cellar", "Wingman, sell ore")]
        [TestCase("cell or", "Wingman, sell ore")]
        [TestCase("pack my target", "Wingman, attack my target")]
        [TestCase("morning at the Inner Belt", "Wingman, mine at the Inner Belt")]
        [TestCase("Wingman, cellar", "Wingman, sell ore")]
        public void TheSessionsMishearingsArePutRight(string heard, string sent)
        {
            Assert.That(VoiceCommand.ToChat(heard, One, null, out string to, out string corrected), Is.EqualTo(sent));
            Assert.That(to, Is.EqualTo("Wingman"));
            Assert.That(corrected, Is.Not.Null, "what was put right is said");
        }

        [TestCase("morning")]                         // (a greeting, alone)
        [TestCase("how are you holding up")]
        [TestCase("where are you")]
        [TestCase("nice shooting")]
        [TestCase("mine at the Inner Belt")]           // (already an order)
        public void TalkAndOrdersAreLeftAsTheyWere(string heard)
        {
            Assert.That(VoiceCommand.ToChat(heard, One, null, out _, out string corrected), Is.EqualTo("Wingman, " + heard));
            Assert.That(corrected, Is.Null);
        }

        [Test]
        public void AnOrdersOwnWordIsNeverTakenForTheName()
        {
            // ("mine" sounds like the end of "Wingman"; it's still the order)
            Assert.That(VoiceCommand.NameHeardAs("mine at the Inner Belt", One, out _, out _), Is.Null);
            Assert.That(VoiceCommand.NameHeardAs("main", One, out _, out _), Is.Null, "nothing after it");
            Assert.That(VoiceCommand.ToChat("mine", One), Is.EqualTo("Wingman, mine"));
        }

        [Test]
        public void NamesAreMatchedBySound()
        {
            Assert.That(VoiceCommand.SoundKey("Wingman"), Is.EqualTo("WNKMN"));
            Assert.That(VoiceCommand.SoundKey("women"), Is.EqualTo("WMN"));
            foreach (var heard in new[] { "women", "amen", "onlyman", "wingmen", "wingman" })
                Assert.That(VoiceCommand.SoundsLike(heard, "Wingman"), Is.True, heard);
            foreach (var heard in new[] { "mine", "status", "follow", "Ann", "dock" })
                Assert.That(VoiceCommand.SoundsLike(heard, "Wingman"), Is.False, heard);
            // (another name works the same way, without a list)
            Assert.That(VoiceCommand.SoundsLike("ace", "Ace"), Is.True);
            Assert.That(VoiceCommand.ToChat("maverik dock", new List<string> { "Maverick" }), Is.EqualTo("Maverick, dock"));
        }

        [Test]
        public void OfTheBrowsersGuessesTheFirstOrderIsSent()
        {
            var guesses = new List<string> { "Wingman morning", "Wingman mining", "Wingman mine" };
            Assert.That(VoiceCommand.Choose(guesses, One, null, out string to, out string corrected), Is.EqualTo("Wingman mine"));
            Assert.That(to, Is.EqualTo("Wingman"));
            Assert.That(corrected, Does.Contain("Wingman morning"));
            // (none an order: the first, as it was heard)
            Assert.That(VoiceCommand.Choose(new List<string> { "how's it going", "house it going" }, One, null, out _, out corrected), Is.EqualTo("Wingman, how's it going"));
            Assert.That(corrected, Is.Null);
        }

        [Test]
        public void TheMicrophonesStepRoundWithTheDefaultFirst()
        {
            var ids = new List<string> { "a1", "b2" };
            Assert.That(VoiceInput.NextMic(ids, "", +1), Is.EqualTo("a1"));
            Assert.That(VoiceInput.NextMic(ids, "b2", +1), Is.EqualTo(""));
            Assert.That(VoiceInput.NextMic(ids, "", -1), Is.EqualTo("b2"));
            Assert.That(VoiceInput.NextMic(ids, "gone", +1), Is.EqualTo("a1"), "one unplugged: from the default");
            var mics = VoiceInput.ParseMics("default\tDefault\na1\tHeadset (USB)\nb2\t");
            Assert.That(mics.Select(m => m.id), Is.EqualTo(new[] { "a1", "b2" }));
            Assert.That(mics[0].name, Is.EqualTo("Headset (USB)"));
        }
    }
}

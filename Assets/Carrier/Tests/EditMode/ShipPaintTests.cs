using System.Linq;
using CarrierGame.Demo;
using NUnit.Framework;
using UnityEngine;

namespace CarrierGame.Tests
{
    /// <summary>Ship paint (ShipPaint, PaintScheme): its codes, presets, patterns and lettering.</summary>
    public class ShipPaintTests
    {
        [SetUp]
        public void Fresh()
        {
            // (a self-test's command line: nothing is saved to the player's own settings)
            LaunchArgs.TestArgs = new[] { "Game.exe", "-systemtest" };
            ShipPaint.ResetStatics();
        }

        [TearDown]
        public void Restore()
        {
            LaunchArgs.TestArgs = null;
            LaunchArgs.TestUrl = null;
            ShipPaint.ResetStatics();
        }

        static void AssertSame(PaintScheme a, PaintScheme b)
        {
            Assert.That(b.pattern, Is.EqualTo(a.pattern));
            Assert.That(b.scale, Is.EqualTo(a.scale));
            Assert.That(b.angle, Is.EqualTo(a.angle));
            Assert.That(b.logo, Is.EqualTo(a.logo));
            Assert.That(b.text, Is.EqualTo(a.text));
            for (int slot = 0; slot < 4; slot++)
            {
                Color32 x = a.GetColor(slot), y = b.GetColor(slot);
                Assert.That((y.r, y.g, y.b), Is.EqualTo((x.r, x.g, x.b)), $"colour {slot}");
            }
        }

        [Test]
        public void ACodeReadsBackAsTheSameScheme()
        {
            var rng = new System.Random(7);
            for (int i = 0; i < 200; i++)
            {
                var s = ShipPaint.Random(rng);
                s.text = i % 3 == 0 ? "" : i % 3 == 1 ? "BC-4000" : "NOVA 7";
                s.angle = rng.Next(360);
                s.logo = rng.Next(-1, ShipPaint.Logos);
                string code = s.ToCode();
                Assert.That(code, Does.Not.Contain(" "), "no spaces: it goes on a command line and a page address");
                Assert.That(PaintScheme.TryParse(code, out var back), Is.True, code);
                AssertSame(s, back);
                Assert.That(back.ToCode(), Is.EqualTo(code));
            }
            var tiger = new PaintScheme { pattern = 90, scale = 100, angle = 0, primary = PaintScheme.C("d9731a"), secondary = PaintScheme.C("1a1a1a"),
                                          trim = PaintScheme.C("e8dcc0"), letters = PaintScheme.C("1a1a1a"), logo = 8, text = "TIGER-01" };
            Assert.That(tiger.ToCode(), Is.EqualTo("1.90.100.0.d9731a.1a1a1a.e8dcc0.1a1a1a.8.TIGER-01"));
        }

        [Test]
        public void WhatIsntACodeIsntRead()
        {
            foreach (var bad in new[] { null, "", "tiger", "1.2.3", "2.0.100.0.ffffff.ffffff.ffffff.ffffff.0.", "1.x.100.0.ffffff.ffffff.ffffff.ffffff.0.",
                                        "1.0.100.0.fffff.ffffff.ffffff.ffffff.0.", "1.0.100.0.gggggg.ffffff.ffffff.ffffff.0.", "1.0.100.0.ffffff.ffffff.ffffff.ffffff.0.A.B" })
                Assert.That(PaintScheme.TryParse(bad, out _), Is.False, bad ?? "(null)");
            // (no lettering: the last part can be left off)
            Assert.That(PaintScheme.TryParse("1.5.100.0.ffffff.000000.ff0000.ffffff.-1", out var s), Is.True);
            Assert.That(s.text, Is.EqualTo(""));
            Assert.That(s.logo, Is.EqualTo(-1));
        }

        [Test]
        public void TenPresetsEachAGoodScheme()
        {
            Assert.That(ShipPaint.Presets.Length, Is.EqualTo(10));
            var names = ShipPaint.Presets.Select(p => p.name).ToArray();
            Assert.That(names.Distinct().Count(), Is.EqualTo(10), "every preset its own name");
            foreach (var want in new[] { "Fleet grey", "Tiger stripes", "Arctic splinter", "Corsair red", "Night hunter", "Desert digital",
                                         "Checker", "Chevrons", "Racing stripe", "Hex armour" })
                Assert.That(names, Does.Contain(want));
            foreach (var p in ShipPaint.Presets)
            {
                Assert.That(PaintScheme.TryParse(p.code, out var s), Is.True, p.name);
                Assert.That(s.ToCode(), Is.EqualTo(p.code), $"{p.name}: its code is already in range");
                Assert.That(s.pattern, Is.InRange(0, ShipPaint.Patterns - 1), p.name);
                Assert.That(s.logo, Is.InRange(0, ShipPaint.Logos - 1), $"{p.name} has a logo");
                Assert.That(s.primary, Is.Not.EqualTo(s.secondary), $"{p.name}: the pattern shows");
            }
            Assert.That(ShipPaint.Presets.Select(p => p.Scheme.pattern).Distinct().Count(), Is.EqualTo(10), "each preset its own pattern");
        }

        [Test]
        public void APresetKeepsTheLettering()
        {
            var mine = new PaintScheme { text = "HMS RAZOR" };
            var s = ShipPaint.FromPreset(3, mine);
            Assert.That(s.text, Is.EqualTo("HMS RAZOR"));
            Assert.That(s.pattern, Is.EqualTo(ShipPaint.Presets[3].Scheme.pattern));
            Assert.That(ShipPaint.FromPreset(99).pattern, Is.EqualTo(ShipPaint.Presets[9].Scheme.pattern), "past the last: the last");
        }

        [Test]
        public void PatternsAreHundredAndKeptInRange()
        {
            Assert.That(ShipPaint.Patterns, Is.EqualTo(100));
            Assert.That(ShipPaint.FamilyNames.Length, Is.EqualTo(ShipPaint.Families));
            for (int f = 0; f < ShipPaint.Families; f++)
            {
                var variants = Enumerable.Range(f * 10, 10).Select(ShipPaint.PatternName).ToArray();
                Assert.That(variants.All(n => !string.IsNullOrEmpty(n)), Is.True);
                Assert.That(variants.Distinct().Count(), Is.EqualTo(10), $"{ShipPaint.FamilyNames[f]}: every variant its own name");
            }
            Assert.That(ShipPaint.PatternName(-5), Is.EqualTo(ShipPaint.PatternName(0)));
            Assert.That(ShipPaint.PatternName(250), Is.EqualTo(ShipPaint.PatternName(99)));

            Assert.That(PaintScheme.TryParse("1.150.900.-30.ffffff.000000.ff0000.ffffff.40.", out var s), Is.True);
            Assert.That(s.pattern, Is.EqualTo(99));
            Assert.That(s.scale, Is.EqualTo(PaintScheme.MaxScale));
            Assert.That(s.angle, Is.EqualTo(330));
            Assert.That(s.logo, Is.EqualTo(-1), "a logo there isn't: none");
            Assert.That(PaintScheme.TryParse("1.-4.1.0.ffffff.000000.ff0000.ffffff.0.", out s), Is.True);
            Assert.That(s.pattern, Is.EqualTo(0));
            Assert.That(s.scale, Is.EqualTo(PaintScheme.MinScale));
            // -1: the factory finish, with a logo and lettering on it, kept as it is
            Assert.That(PaintScheme.TryParse("1.-1.100.0.ffffff.000000.ff0000.ffd040.3.BC_4000", out s), Is.True);
            Assert.That(s.pattern, Is.EqualTo(ShipPaint.FactoryPattern));
            Assert.That(s.logo, Is.EqualTo(3));
            Assert.That(s.text, Is.EqualTo("BC 4000"));
            Assert.That(PaintScheme.TryParse(s.ToCode(), out var again) && again.pattern == ShipPaint.FactoryPattern && again.text == "BC 4000", Is.True, "and back");
            Assert.That(ShipPaint.FamilyOf(ShipPaint.FactoryPattern), Is.EqualTo("Factory finish"));
        }

        [Test]
        public void LetteringIsCleaned()
        {
            Assert.That(PaintScheme.SanitizeText("bc-01 alpha!"), Is.EqualTo("BC-01 ALPHA"));
            Assert.That(PaintScheme.SanitizeText("  nova  "), Is.EqualTo("NOVA"));
            Assert.That(PaintScheme.SanitizeText("red_baron"), Is.EqualTo("RED BARON"));
            Assert.That(PaintScheme.SanitizeText("ABCDEFGHIJKLMNOP"), Is.EqualTo("ABCDEFGHIJKL"));
            Assert.That(PaintScheme.SanitizeText("ÆØ$%.:"), Is.EqualTo(""));
            Assert.That(PaintScheme.SanitizeText(null), Is.EqualTo(""));
            var s = new PaintScheme { text = "my.ship:1" };
            Assert.That(PaintScheme.TryParse(s.ToCode(), out var back), Is.True, "no dots or colons get into the code");
            Assert.That(back.text, Is.EqualTo("MYSHIP1"));
        }

        [Test]
        public void LettersGoToTheFontAtlasCells()
        {
            Assert.That(PaintScheme.LetterCode('A'), Is.EqualTo(1));
            Assert.That(PaintScheme.LetterCode('Z'), Is.EqualTo(26));
            Assert.That(PaintScheme.LetterCode('0'), Is.EqualTo(27));
            Assert.That(PaintScheme.LetterCode('9'), Is.EqualTo(36));
            Assert.That(PaintScheme.LetterCode('-'), Is.EqualTo(37));
            Assert.That(PaintScheme.LetterCode(' '), Is.EqualTo(0));

            var bounds = new Bounds(Vector3.zero, new Vector3(50f, 60f, 180f));
            var d = ShipPaint.Decals(new PaintScheme { text = "AB-1", logo = 2 }, bounds, ShipPaint.Fallback);
            Assert.That(d.text.w, Is.EqualTo(4f));
            Assert.That(d.letters[0], Is.EqualTo(new Vector4(1, 2, 37, 28)));
            Assert.That(d.letters[1], Is.EqualTo(Vector4.zero));
            Assert.That(d.logo.w, Is.EqualTo(2f));
            Assert.That(d.logo.x, Is.EqualTo(Mathf.Lerp(-90f, 90f, ShipPaint.Fallback.logoZ)).Within(1e-3f), "the logo 30% back from the bow");
            Assert.That(d.logo.z, Is.GreaterThan(0f));
            // twelve letters fit in about half the ship
            d = ShipPaint.Decals(new PaintScheme { text = "ABCDEFGHIJKL" }, bounds, ShipPaint.Fallback);
            Assert.That(d.text.z * 0.75f * 12f, Is.LessThanOrEqualTo(90f + 1e-3f));
            Assert.That(d.text.w, Is.EqualTo(12f));
        }

        [Test]
        public void EveryClassHasAPlace()
        {
            foreach (var id in new[] { "Sentinel", "Lancer", "Warden", "Talon", "Vanguard", "Halberd", "Rapier", "Claymore", "Bulwark", "Stiletto", "Sovereign", "Dominion", "Unknown", "" })
            {
                var p = ShipPaint.PlacementFor(id);
                Assert.That(p.logoZ, Is.InRange(0.5f, 0.9f), id);
                Assert.That(p.textZ, Is.InRange(0.2f, 0.7f), id);
                Assert.That(p.logoSize, Is.GreaterThan(0f), id);
                Assert.That(p.textSize, Is.GreaterThan(0f), id);
            }
        }

        [Test]
        public void TextGivesASchemeAPresetOrNone()
        {
            Assert.That(ShipPaint.FromText("tiger").ToCode(), Is.EqualTo(ShipPaint.Presets[1].code));
            Assert.That(ShipPaint.FromText("Tiger stripes").ToCode(), Is.EqualTo(ShipPaint.Presets[1].code));
            Assert.That(ShipPaint.FromText("HEX-ARMOUR").ToCode(), Is.EqualTo(ShipPaint.Presets[9].code));
            Assert.That(ShipPaint.FromText("3").ToCode(), Is.EqualTo(ShipPaint.Presets[3].code));
            Assert.That(ShipPaint.FromText(ShipPaint.Presets[5].code).ToCode(), Is.EqualTo(ShipPaint.Presets[5].code));
            Assert.That(ShipPaint.FromText("none"), Is.Null);
            Assert.That(ShipPaint.FromText("factory"), Is.Null);
            Assert.That(ShipPaint.FromText(""), Is.Null);
            Assert.That(ShipPaint.FromText(null), Is.Null);
        }

        [Test]
        public void TheLaunchFlagAndAChoiceThisRun()
        {
            Assert.That(ShipPaint.ForPlayer("Sentinel"), Is.Null, "a self-test flies unpainted");
            LaunchArgs.TestArgs = new[] { "Game.exe", "-systemtest", "-paint", "chevrons" };
            Assert.That(ShipPaint.ForPlayer("Sentinel").ToCode(), Is.EqualTo(ShipPaint.Presets[7].code), "unless given -paint");
            ShipPaint.Set("Sentinel", ShipPaint.FromPreset(2));
            Assert.That(ShipPaint.ForPlayer("Sentinel").ToCode(), Is.EqualTo(ShipPaint.Presets[2].code), "a choice this run wins");
            Assert.That(ShipPaint.ForPlayer("Lancer").ToCode(), Is.EqualTo(ShipPaint.Presets[7].code), "for its own class");
            ShipPaint.Set("Lancer", null);
            Assert.That(ShipPaint.ForPlayer("Lancer"), Is.Null, "the factory finish chosen");
        }

        [Test]
        public void ThePaintGoesWithTheShipInCoop()
        {
            string code = ShipPaint.Presets[4].code;
            var m = JsonUtility.FromJson<CoopMsg>(JsonUtility.ToJson(new ShipMsg { fr = "Lancer", pt = code }));
            Assert.That(m.pt, Is.EqualTo(code));
            Assert.That(ShipPaint.FromText(m.pt).ToCode(), Is.EqualTo(code));
            // an older game's ship: no paint at all
            var old = JsonUtility.FromJson<CoopMsg>("{\"t\":\"ps\",\"fr\":\"Lancer\",\"x\":1}");
            Assert.That(string.IsNullOrEmpty(old.pt), Is.True);
            Assert.That(ShipPaint.FromText(old.pt), Is.Null);
        }

        [Test]
        public void OnlyHullMaterialsArePainted()
        {
            var hull = new Material(Shader.Find("Hidden/InternalErrorShader")) { name = "Frigate_Lancer_Hull" };
            var glow = new Material(Shader.Find("Hidden/InternalErrorShader")) { name = "Engine_Glow" };
            try
            {
                Assert.That(ShipPaint.IsHull(hull), Is.True);
                Assert.That(ShipPaint.IsHull(glow), Is.False);
                Assert.That(ShipPaint.IsHull(null), Is.False);
                Assert.That(ShipPaint.IsPainted(hull), Is.False);
                Assert.That(ShipPaint.Original(hull), Is.SameAs(hull));
            }
            finally
            {
                Object.DestroyImmediate(hull);
                Object.DestroyImmediate(glow);
            }
        }
    }
}

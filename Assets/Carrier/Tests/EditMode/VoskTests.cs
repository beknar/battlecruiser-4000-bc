using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CarrierGame.Demo;
using NUnit.Framework;
using UnityEngine;

namespace CarrierGame.Tests
{
    /// <summary>The offline voice commands on Windows and Linux (Vosk): the grammar it listens for
    /// (the model's words only, "[unk]" for the rest), what it gives back, the microphones stepped through
    /// in the pause menu, and recorded orders (in Piper's en_US-norman-medium, the stock lines'
    /// voice: Tools/Voice/testdata) heard as those orders.</summary>
    public class VoskTests
    {
        static readonly List<string> One = new List<string> { "Wingman" };
        static readonly string[] Places = { "Inner Belt", "Outer Drift", "Thalassa", "Gilgal", "Heaven", "The Forge", "Jericho" };

        [Test]
        public void TheGrammarHasOnlyTheModelsWordsAndUnk()
        {
            var known = new HashSet<string> { "wingman", "warp", "to", "the", "mine", "at", "inner", "belt", "what's", "in", "your", "hold" };
            var phrases = new[] { "Wingman warp to Gilgal", "mine at the Inner Belt", "K'rzyn", "What's in your hold?", "Wingman, warp to Zuzim", "wingman warp to the Gilgal", "mine at Gilgal Zuzim" };
            var g = VoskRecognizer.GrammarPhrases(phrases, known.Contains);
            Assert.That(g, Is.EqualTo(new[] { "wingman warp to [unk]", "mine at the inner belt", "what's in your hold", "wingman warp to the [unk]", "mine at [unk]", "[unk]" }),
                        "unknown words as [unk] (once for several), each phrase once, [unk] alone last");
            Assert.That(VoskRecognizer.GrammarJson(new[] { "Mine at the Inner Belt" }, known.Contains), Is.EqualTo("[\"mine at the inner belt\", \"[unk]\"]"));
            Assert.That(VoskRecognizer.GrammarJson(new[] { "Gilgal" }, known.Contains), Is.EqualTo("[\"[unk]\"]"), "nothing known: [unk] alone");
            Assert.That(VoskRecognizer.Words("What's in your hold?"), Is.EqualTo(new[] { "what's", "in", "your", "hold" }));
        }

        [Test]
        public void TheGrammarHasTheOrdersAmountsAndThen()
        {
            var phrases = VoiceCommand.GrammarPhrases(One, Places);
            foreach (var p in VoiceCommand.Phrases(One, Places)) Assert.That(phrases, Does.Contain(p));
            Assert.That(phrases, Does.Contain("give me two thousand credits"));
            Assert.That(phrases, Does.Contain("give me a hundred credits"));
            Assert.That(phrases, Does.Contain("then dock"));
            Assert.That(phrases, Does.Contain("Wingman warp to the Thalassa"));
        }

        [Test]
        public void WhatVoskGivesBackIsReadAndItsUnknownWordsSorted()
        {
            Assert.That(VoskRecognizer.TextOf("{\n  \"text\" : \"wingman follow me\"\n}"), Is.EqualTo("wingman follow me"));
            Assert.That(VoskRecognizer.TextOf("{\"text\" : \"\"}"), Is.EqualTo(""));
            Assert.That(VoskRecognizer.TextOf(null), Is.EqualTo(""));
            Assert.That(VoskRecognizer.TextOf("{\"text\" : \"say \\\"hi\\\" \\u0041\"}"), Is.EqualTo("say \"hi\" A"));

            Assert.That(VoskRecognizer.WithoutUnknowns("warp to [unk]", out bool unknown, out bool missing), Is.EqualTo("warp to"));
            Assert.That(unknown && missing, Is.True, "a place it doesn't know");
            VoskRecognizer.WithoutUnknowns("mine at the [unk]", out _, out missing);
            Assert.That(missing, Is.True);
            Assert.That(VoskRecognizer.WithoutUnknowns("[unk] wingman dock", out unknown, out missing), Is.EqualTo("wingman dock"));
            Assert.That(unknown && !missing, Is.True, "a breath before the order: left out");
            Assert.That(VoskRecognizer.WithoutUnknowns("follow me", out unknown, out missing), Is.EqualTo("follow me"));
            Assert.That(unknown || missing, Is.False);
        }

        [Test]
        public void AnOrderWithAnUnknownPlaceIsntSent()
        {
            var go = new GameObject("Voice input (test)");
            try
            {
                var v = go.AddComponent<VoiceInput>();
                v.HeardOffline("wingman follow me", One);
                Assert.That(VoiceInput.LastSent, Is.EqualTo("Wingman follow me"));
                v.HeardOffline("[unk] wingman dock", One);
                Assert.That(VoiceInput.LastSent, Is.EqualTo("Wingman dock"));
                int before = Notices.Count;
                v.HeardOffline("warp to [unk]", One);
                Assert.That(VoiceInput.LastSent, Is.EqualTo("Wingman dock"), "not sent");
                Assert.That(Notices.Count, Is.EqualTo(before + 1));
                Assert.That(Notices.Last, Does.Contain("warp to ..."));
                Assert.That(Notices.Last, Does.Contain("Place names may need typing"));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void TheDevicesStepRoundWithWindowsDefaultOrWithout()
        {
            var d = new List<string> { "Microphone (USB)", "Headset" };
            // (Windows: "Windows default" first)
            Assert.That(VoiceInput.NextMic(d, "", +1), Is.EqualTo("Microphone (USB)"));
            Assert.That(VoiceInput.NextMic(d, "Headset", +1), Is.EqualTo(""));
            Assert.That(VoiceInput.NextMic(d, "", -1), Is.EqualTo("Headset"));
            // (Linux: the devices only; none chosen, or one unplugged, is the first)
            Assert.That(VoiceInput.NextMic(d, "", +1, false), Is.EqualTo("Headset"));
            Assert.That(VoiceInput.NextMic(d, "Headset", +1, false), Is.EqualTo("Microphone (USB)"));
            Assert.That(VoiceInput.NextMic(d, "Microphone (USB)", -1, false), Is.EqualTo("Headset"));
            Assert.That(VoiceInput.NextMic(d, "gone", +1, false), Is.EqualTo("Headset"));
            Assert.That(VoiceInput.NextMic(new List<string>(), "", +1, false), Is.EqualTo(""));
            LaunchArgs.TestUrl = null;
            Assert.That(VoiceInput.MicMenu, Is.True, "the Microphone line is in the pause menu (the editor, as Windows or Linux)");
        }

        [Test]
        public void TheModelIsFoundBesideThePlayerButNotAsLfsPointers()
        {
            string root = Path.Combine(Path.GetTempPath(), "vosk_find_" + Guid.NewGuid().ToString("N"));
            try
            {
                string data = Path.Combine(root, "StarSystem_Data");
                Directory.CreateDirectory(data);
                string am = Path.Combine(root, VoskRecognizer.ModelFolder, "am");
                Directory.CreateDirectory(am);
                File.WriteAllText(Path.Combine(am, "final.mdl"), "version https://git-lfs.github.com/spec/v1\n");
                Assert.That(VoskRecognizer.FindModel(data, false), Is.Null, "a pointer isn't the model");
                File.WriteAllBytes(Path.Combine(am, "final.mdl"), new byte[200000]);
                Assert.That(VoskRecognizer.FindModel(data, false), Is.EqualTo(Path.Combine(root, VoskRecognizer.ModelFolder)));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        // (each recording, and the message it should send)
        static readonly (string wav, string sent)[] Orders =
        {
            ("order_follow_me", "Wingman follow me"),
            ("order_mine_inner_belt", "Wingman, mine at the inner belt"),
            ("order_all_ships_regroup", "All ships regroup"),
            ("order_dock", "Wingman dock"),
            ("order_warp_thalassa", "Wingman, warp to thalassa"),
            ("order_sell_ore", "Wingman, sell the ore"),
            ("order_give_credits", "Wingman, give me 2000 credits"),
        };

        [Test]
        public void RecordedOrdersAreHeardAsThoseOrders()
        {
            NeedTheRecogniser();
            var grammar = VoiceCommand.GrammarPhrases(One, Places);
            var bad = new List<string>();
            foreach (var (wav, sent) in Orders)
            {
                var pcm = Recording(wav, out int rate);
                string heard = VoskRecognizer.RecognizeNow(pcm, pcm.Length, rate, grammar);
                Assert.That(heard, Is.Not.Null, VoskRecognizer.Error);
                string said = VoskRecognizer.WithoutUnknowns(heard, out _, out bool missing);
                string msg = missing ? null : VoiceCommand.ToChat(said, One);
                if (msg != sent) bad.Add($"{wav}: heard \"{heard}\", sent \"{msg}\", not \"{sent}\"");
            }
            Assert.That(bad, Is.Empty, string.Join("\n", bad));

            // (a place the model doesn't know: [unk], and not sent)
            var gilgal = Recording("order_warp_gilgal", out int r);
            string g = VoskRecognizer.RecognizeNow(gilgal, gilgal.Length, r, grammar);
            VoskRecognizer.WithoutUnknowns(g, out bool unk, out bool nameMissing);
            Assert.That(nameMissing, Is.True, $"\"warp to Gilgal\" heard as \"{g}\"");
            Assert.That(unk, Is.True);
        }

        [Test]
        public void TheModelKnowsTheOrdersWordsButNotEveryPlace()
        {
            NeedTheRecogniser();
            foreach (var w in new[] { "wingman", "follow", "regroup", "dock", "thalassa", "[unk]" })
                Assert.That(VoskRecognizer.Knows(w), Is.True, w);
            foreach (var w in new[] { "gilgal", "vrask", "Wingman" })
                Assert.That(VoskRecognizer.Knows(w), Is.False, w + " (and only lower case)");
        }

        static void NeedTheRecogniser()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor && Application.platform != RuntimePlatform.LinuxEditor)
                Assert.Ignore("Vosk is built for Windows and Linux");
            if (!VoskRecognizer.Present)
                Assert.Ignore("no Vosk model in Tools/Voice/vosk-model (or only Git LFS's pointers: git lfs pull)");
            if (!VoskRecognizer.Knows("the"))
            {
                if (VoskRecognizer.LibraryMissing) Assert.Ignore($"no Vosk library: {VoskRecognizer.Error}");
                Assert.Fail($"Vosk didn't start: {VoskRecognizer.Error}");
            }
        }

        /// <summary>A recording in Tools/Voice/testdata, as 16-bit mono samples (ignored if it isn't there).</summary>
        static short[] Recording(string name, out int rate)
        {
            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Tools", "Voice", "testdata", name + ".wav");
            if (!File.Exists(path)) Assert.Ignore($"no recording {path}");
            var pcm = ReadWav(File.ReadAllBytes(path), out rate);
            if (pcm == null) Assert.Ignore($"{name}.wav isn't 16-bit mono (or is Git LFS's pointer: git lfs pull)");
            return pcm;
        }

        static short[] ReadWav(byte[] b, out int rate)
        {
            rate = 0;
            if (b.Length < 44 || Encoding.ASCII.GetString(b, 0, 4) != "RIFF" || Encoding.ASCII.GetString(b, 8, 4) != "WAVE") return null;
            int channels = 0, bits = 0, i = 12;
            short[] pcm = null;
            while (i + 8 <= b.Length)
            {
                string id = Encoding.ASCII.GetString(b, i, 4);
                int size = BitConverter.ToInt32(b, i + 4), body = i + 8;
                if (id == "fmt " && size >= 16)
                {
                    channels = BitConverter.ToInt16(b, body + 2);
                    rate = BitConverter.ToInt32(b, body + 4);
                    bits = BitConverter.ToInt16(b, body + 14);
                }
                else if (id == "data")
                {
                    int n = Math.Min(size, b.Length - body) / 2;
                    pcm = new short[n];
                    Buffer.BlockCopy(b, body, pcm, 0, n * 2);
                }
                i = body + size + (size & 1);
            }
            return channels == 1 && bits == 16 ? pcm : null;
        }
    }
}

using CarrierGame.Demo;
using NUnit.Framework;
using UnityEngine;

namespace CarrierGame.Tests
{
    /// <summary>The wingman's live voice (Flite, for lines with no stock clip): a sentence comes out
    /// as speech of a sensible length, with the radio's sound on it, and what's written for reading
    /// is made speakable first.</summary>
    public class WingmanLiveVoiceTests
    {
        [Test]
        public void ASentenceComesOutAsSpeech()
        {
            var samples = WingmanVoice.SpeakNow("Transferred 500 credits to Ann.", out int rate);
            if (Application.platform != RuntimePlatform.WindowsEditor && Application.platform != RuntimePlatform.LinuxEditor)
                Assert.Ignore("the live voice is built for Windows and Linux");
            Assert.That(samples, Is.Not.Null, "the speech engine (Plugins/x86_64) is there");
            Assert.That(rate, Is.EqualTo(16000));
            float seconds = samples.Length / (float)rate;
            Assert.That(seconds, Is.InRange(1.2f, 5f), "about two seconds of speech");
            double sum = 0;
            float peak = 0f;
            foreach (var v in samples) { sum += v * v; peak = Mathf.Max(peak, Mathf.Abs(v)); }
            Assert.That(System.Math.Sqrt(sum / samples.Length), Is.GreaterThan(0.05), "not silence");
            Assert.That(peak, Is.LessThanOrEqualTo(1f), "no clipping past full scale");
        }

        [Test]
        public void WhatsWrittenForReadingIsMadeSpeakable()
        {
            Assert.That(WingmanVoice.Speakable("taking 34 t of ore; 1,200 cr left."), Is.EqualTo("taking 34 tonnes of ore, 1200 credits left."));
            Assert.That(WingmanVoice.Speakable("hull 62%"), Is.EqualTo("hull 62 percent"));
            Assert.That(WingmanVoice.Speakable("3.4 km off"), Is.EqualTo("3.4 kilometres off"));
        }

        [Test]
        public void TheRadioSoundAddsTheSquelchesAndKeepsTheLevel()
        {
            var pcm = new short[16000];
            for (int i = 0; i < pcm.Length; i++) pcm[i] = (short)(8000 * Mathf.Sin(i * 2f * Mathf.PI * 800f / 16000f));
            var o = WingmanVoice.Radio(pcm, 16000);
            Assert.That(o.Length, Is.EqualTo(16000 + 1120 + 800), "70 ms of squelch before, 50 ms after");
            float peak = 0f;
            foreach (var v in o) peak = Mathf.Max(peak, Mathf.Abs(v));
            Assert.That(peak, Is.InRange(0.5f, 0.91f));
        }
    }
}

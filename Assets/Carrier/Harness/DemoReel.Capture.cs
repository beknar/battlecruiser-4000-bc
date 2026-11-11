using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>Recording a reel: the frames as JPEGs and the game's sound as a WAV file.</summary>
    public partial class DemoReel
    {
        // ------------------------------------------------------------------ capture

        void SaveFrame()
        {
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            try { File.WriteAllBytes(Path.Combine(dir, $"f{recorded:D5}.jpg"), tex.EncodeToJPG(92)); }
            catch (Exception e) { Log("frame " + recorded + ": " + e.Message); }
            Destroy(tex);
        }

        void StartAudio()
        {
            channels = AudioSettings.speakerMode switch
            {
                AudioSpeakerMode.Mono => 1, AudioSpeakerMode.Quad => 4, AudioSpeakerMode.Surround => 5,
                AudioSpeakerMode.Mode5point1 => 6, AudioSpeakerMode.Mode7point1 => 8, _ => 2,
            };
            audioOk = AudioRenderer.Start();
            if (!audioOk) Log("audio capture not available: the reel will be silent");
            else
            {
                // throw away anything rendered before the first recorded frame
                int n = AudioRenderer.GetSampleCountForCaptureFrame();
                if (n > 0) { var junk = new NativeArray<float>(n * channels, Allocator.Temp); AudioRenderer.Render(junk); junk.Dispose(); }
                wav = new BinaryWriter(File.Create(Path.Combine(dir, "reel.wav")));
                WavHeader(0);
            }
        }

        /// <summary>Render this frame's share of the game's sound; keep it (faded with the
        /// picture) or, between scenes, throw it away.</summary>
        void CaptureAudio(bool keep)
        {
            if (!audioOk) return;
            int n = AudioRenderer.GetSampleCountForCaptureFrame();
            if (n <= 0) return;
            var buf = new NativeArray<float>(n * channels, Allocator.Temp);
            AudioRenderer.Render(buf);
            if (keep)
            {
                float g1 = Mathf.Clamp01(1f - fade);
                for (int i = 0; i < buf.Length; i++)
                {
                    float g = Mathf.Lerp(gain, g1, (i / channels) / (float)n);   // ramp across the frame: no clicks
                    wav.Write((short)Mathf.RoundToInt(Mathf.Clamp(buf[i] * g, -1f, 1f) * 32767f));
                }
                wavSamples += buf.Length;
                gain = g1;
            }
            buf.Dispose();
        }

        void WavHeader(long samples)
        {
            int rate = AudioSettings.outputSampleRate;
            int bytes = (int)(samples * 2);
            wav.Write(new[] { 'R', 'I', 'F', 'F' }); wav.Write(36 + bytes);
            wav.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' }); wav.Write(16);
            wav.Write((short)1); wav.Write((short)channels); wav.Write(rate);
            wav.Write(rate * channels * 2); wav.Write((short)(channels * 2)); wav.Write((short)16);
            wav.Write(new[] { 'd', 'a', 't', 'a' }); wav.Write(bytes);
        }

        /// <summary>Fill in the sizes now the length is known, and close the file.</summary>
        void FinishWav()
        {
            if (wav == null) return;
            wav.Flush();
            wav.Seek(0, SeekOrigin.Begin);
            WavHeader(wavSamples);
            wav.Close();
            wav = null;
        }
    }
}

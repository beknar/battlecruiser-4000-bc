using UnityEditor;
using UnityEngine;

namespace Carrier.EditorTools
{
    /// <summary>Import settings for the game's library sounds (Resources/CarrierAudio):
    /// effects are mono and decompressed on load so they start instantly; the engine and ambience
    /// loops and the music stay compressed in memory (music in stereo); the playlist's tracks
    /// stream (a web build loads them as the page does any audio).</summary>
    public class CarrierAudioImport : AssetPostprocessor
    {
        const string Root = "Assets/Carrier/Resources/CarrierAudio/";

        // (raised when the settings change, so every sound is imported again with them)
        public override uint GetVersion() => 2;

        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(Root)) return;
            var importer = (AudioImporter)assetImporter;
            bool music = assetPath.StartsWith(Root + "Music/");
            bool loop = music || assetPath.Contains("/EngineLoop/") || assetPath.Contains("/AmbientLoop/");

            // the playlist (Music/Calm, Music/Combat: fifty tracks): streamed from the disk, only
            // what is playing in memory, not loaded until played
            bool playlist = music && (assetPath.Contains("/Music/Calm/") || assetPath.Contains("/Music/Combat/"));
            importer.forceToMono = !music;
            importer.loadInBackground = playlist;
            var s = importer.defaultSampleSettings;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = playlist ? 0.45f : music ? 0.6f : 0.75f;
            s.loadType = playlist ? AudioClipLoadType.Streaming : loop ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            s.preloadAudioData = !playlist;
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings = s;
        }
    }
}

using UnityEditor;
using UnityEngine;

namespace Carrier.EditorTools
{
    /// <summary>Import settings for the game's library sounds (Resources/CarrierAudio):
    /// effects are mono and decompressed on load so they start instantly; the engine and ambience
    /// loops and the music stay compressed in memory (music in stereo).</summary>
    public class CarrierAudioImport : AssetPostprocessor
    {
        const string Root = "Assets/Carrier/Resources/CarrierAudio/";

        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(Root)) return;
            var importer = (AudioImporter)assetImporter;
            bool music = assetPath.StartsWith(Root + "Music/");
            bool loop = music || assetPath.Contains("/EngineLoop/") || assetPath.Contains("/AmbientLoop/");

            importer.forceToMono = !music;
            importer.loadInBackground = false;
            var s = importer.defaultSampleSettings;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = music ? 0.6f : 0.75f;
            s.loadType = loop ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            s.preloadAudioData = true;
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings = s;
        }
    }
}

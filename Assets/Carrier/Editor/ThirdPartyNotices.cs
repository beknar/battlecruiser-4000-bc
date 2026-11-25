using System.IO;
using CarrierGame.Demo;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Carrier.EditorTools
{
    /// <summary>The licences of what the game ships from others, put beside the Windows and Linux
    /// players: CMU Flite's (the wingman's live voice, Plugins/x86_64), whose licence asks for its
    /// notice to go with the library, and Vosk's (the offline voice commands: its library in
    /// Plugins/x86_64 with the MinGW runtime, and its model). The model goes beside the players
    /// here too (vosk-model, 40 MB): it's kept in Tools/Voice, not in the project's assets, so the
    /// web build never has it.</summary>
    public class ThirdPartyNotices : IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;
        const string Flite = "Assets/Carrier/Plugins/x86_64/FLITE_LICENSE.txt";
        const string Vosk = "Tools/Voice/VOSK_LICENSE.txt";
        const string VoskModel = "Tools/Voice/" + VoskRecognizer.ModelFolder;

        public void OnPostprocessBuild(BuildReport report)
        {
            var t = report.summary.platform;
            if (t != BuildTarget.StandaloneWindows64 && t != BuildTarget.StandaloneLinux64) return;
            string dir = Path.GetDirectoryName(report.summary.outputPath);
            if (string.IsNullOrEmpty(dir)) return;
            if (File.Exists(Flite))
            {
                File.Copy(Flite, Path.Combine(dir, "THIRD_PARTY_FLITE.txt"), true);
                Debug.Log($"[Build] Flite's licence put beside the player ({dir})");
            }
            if (File.Exists(Vosk)) File.Copy(Vosk, Path.Combine(dir, "THIRD_PARTY_VOSK.txt"), true);
            CopyModel(dir);
        }

        /// <summary>Vosk's model beside the player, as the game looks for it (VoskRecognizer.FindModel).</summary>
        static void CopyModel(string dir)
        {
            string to = Path.Combine(dir, VoskRecognizer.ModelFolder);
            var mdl = new FileInfo(Path.Combine(VoskModel, "am", "final.mdl"));
            if (!mdl.Exists || mdl.Length < 100000)
            {
                Debug.LogWarning($"[Build] no Vosk model in {VoskModel} (or only Git LFS's pointers to it: git lfs pull): " +
                                 "the player's voice commands have only Windows' own recognition (none on Linux)");
                return;
            }
            if (Directory.Exists(to)) Directory.Delete(to, true);
            long bytes = 0;
            int files = 0;
            foreach (var f in Directory.GetFiles(VoskModel, "*", SearchOption.AllDirectories))
            {
                string rel = f.Substring(VoskModel.Length).TrimStart('/', '\\');
                string dst = Path.Combine(to, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                File.Copy(f, dst, true);
                bytes += new FileInfo(f).Length;
                files++;
            }
            Debug.Log($"[Build] Vosk's licence and model ({files} files, {bytes / (1024 * 1024)} MB) put beside the player ({dir})");
        }
    }
}

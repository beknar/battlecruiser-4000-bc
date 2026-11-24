using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Carrier.EditorTools
{
    /// <summary>The licences of what the game ships from others, put beside the Windows and Linux
    /// players: CMU Flite's (the wingman's live voice, Plugins/x86_64), whose licence asks for its
    /// notice to go with the library.</summary>
    public class ThirdPartyNotices : IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;
        const string Flite = "Assets/Carrier/Plugins/x86_64/FLITE_LICENSE.txt";

        public void OnPostprocessBuild(BuildReport report)
        {
            var t = report.summary.platform;
            if (t != BuildTarget.StandaloneWindows64 && t != BuildTarget.StandaloneLinux64) return;
            string dir = Path.GetDirectoryName(report.summary.outputPath);
            if (string.IsNullOrEmpty(dir) || !File.Exists(Flite)) return;
            File.Copy(Flite, Path.Combine(dir, "THIRD_PARTY_FLITE.txt"), true);
            Debug.Log($"[Build] Flite's licence put beside the player ({dir})");
        }
    }
}

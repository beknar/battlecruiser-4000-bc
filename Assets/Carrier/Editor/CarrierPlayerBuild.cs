using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Carrier.EditorTools
{
    /// <summary>Builds the carrier demo as a release Windows (64-bit) executable.</summary>
    public static class CarrierPlayerBuild
    {
        const string Scene = "Assets/Carrier/Scenes/CarrierShowcase.unity";
        public const string OutDir = "../Builds/CarrierDemo";   // next to the Unity project
        const string Exe = "CarrierDemo.exe";

        [MenuItem("Tools/Carrier/Build Windows Demo")]
        public static void Build()
        {
            PlayerSettings.productName = "Carrier Demo";
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.runInBackground = true;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.allowFullscreenSwitch = true;
            // DirectX 11: the mature path on hybrid (AMD + NVIDIA) laptops; DX12 hung the GPU driver here
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 });
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);

            if (EditorUtility.scriptCompilationFailed)
            {
                File.WriteAllText("Temp/carrier_player_build.log", "not built: scripts have compile errors (old build left in place)");
                return;
            }
            string outDir = Path.GetFullPath(OutDir);
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            Directory.CreateDirectory(outDir);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                locationPathName = Path.Combine(outDir, Exe),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            string msg = $"result {s.result} errors {s.totalErrors} warnings {s.totalWarnings} " +
                         $"size {s.totalSize / (1024f * 1024f):F0} MB time {s.totalTime.TotalSeconds:F0}s\n{options.locationPathName}";
            if (s.result != BuildResult.Succeeded)
                msg += "\n" + string.Join("\n", report.steps.SelectMany(st => st.messages)
                    .Where(m => m.type == LogType.Error || m.type == LogType.Exception).Select(m => m.content).Take(30));
            File.WriteAllText("Temp/carrier_player_build.log", msg);
            Debug.Log("[CarrierPlayerBuild] " + msg);
        }
    }
}

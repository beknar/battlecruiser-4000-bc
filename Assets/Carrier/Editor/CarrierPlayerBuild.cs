using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Carrier.EditorTools
{
    /// <summary>Builds the demos as release Windows (64-bit) executables: the open-space carrier
    /// demo, and the asteroid-field ambush as a second, separate game.</summary>
    public static class CarrierPlayerBuild
    {
        const string Scene = "Assets/Carrier/Scenes/CarrierShowcase.unity";
        public const string OutDir = "../Builds/CarrierDemo";   // next to the Unity project
        const string Exe = "CarrierDemo.exe";
        const string ProductName = "Carrier Demo";

        public const string AsteroidOutDir = "../Builds/AsteroidAmbush";
        const string AsteroidExe = "AsteroidAmbush.exe";
        const string AsteroidProductName = "Asteroid Ambush";

        [MenuItem("Tools/Carrier/Build Windows Demo")]
        public static void Build() => Build(Scene, OutDir, Exe, ProductName, "Temp/carrier_player_build.log");

        [MenuItem("Tools/Carrier/Build Asteroid Ambush")]
        public static void BuildAsteroids()
        {
            try { Build(CarrierBuilder.AsteroidScenePath, AsteroidOutDir, AsteroidExe, AsteroidProductName, "Temp/carrier_player2_build.log"); }
            finally
            {
                PlayerSettings.productName = ProductName;   // the project keeps the first demo's name
                AssetDatabase.SaveAssets();
            }
        }

        public const string StarSystemOutDir = "../Builds/StarSystem";

        [MenuItem("Tools/Carrier/Build Star System")]
        public static void BuildStarSystem()
        {
            try { Build(CarrierBuilder.StarSystemScenePath, StarSystemOutDir, "StarSystem.exe", "Star System", "Temp/carrier_player3_build.log"); }
            finally
            {
                PlayerSettings.productName = ProductName;
                AssetDatabase.SaveAssets();
            }
        }

        static void Build(string scene, string outDirRel, string exe, string productName, string logPath)
        {
            PlayerSettings.productName = productName;
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
                File.WriteAllText(logPath, "not built: scripts have compile errors (old build left in place)");
                return;
            }
            if (!File.Exists(scene))
            {
                File.WriteAllText(logPath, $"not built: {scene} is missing (run the asset build first)");
                return;
            }
            string outDir = Path.GetFullPath(outDirRel);
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            Directory.CreateDirectory(outDir);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { scene },
                locationPathName = Path.Combine(outDir, exe),
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
            File.WriteAllText(logPath, msg);
            Debug.Log("[CarrierPlayerBuild] " + msg);
        }
    }
}

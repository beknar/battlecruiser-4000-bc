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

        public const string StarSystemWebDir = "../Builds/StarSystemWeb";
        const string WebLog = "Temp/carrier_web_build.log";

        /// <summary>The star system as a web build (a folder to put on a web site). It uses WebGPU
        /// where the browser offers it, and falls back to WebGL 2 elsewhere. The files are
        /// Brotli-compressed, with the loader able to unpack them itself, so the site needs no
        /// special server setup (WebGPU does need the page served over https).</summary>
        [MenuItem("Tools/Carrier/Build Star System (Web)")]
        public static void BuildStarSystemWeb()
        {
            var before = EditorUserBuildSettings.activeBuildTarget;
            var beforeGroup = BuildPipeline.GetBuildTargetGroup(before);
            try
            {
                if (EditorUtility.scriptCompilationFailed) { File.WriteAllText(WebLog, "not built: scripts have compile errors"); return; }
                string scene = CarrierBuilder.StarSystemScenePath;
                if (!File.Exists(scene)) { File.WriteAllText(WebLog, $"not built: {scene} is missing (run the asset build first)"); return; }

                PlayerSettings.productName = "Star System";
                PlayerSettings.runInBackground = true;
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.WebGL, false);
                PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[]
                {
                    UnityEngine.Rendering.GraphicsDeviceType.WebGPU,      // tried first
                    UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3,   // WebGL 2
                });
                PlayerSettings.WebGL.template = "PROJECT:StarSystem";
                PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
                PlayerSettings.WebGL.decompressionFallback = true;
                PlayerSettings.WebGL.dataCaching = true;
                PlayerSettings.WebGL.nameFilesAsHashes = false;
                PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
                PlayerSettings.WebGL.showDiagnostics = false;
                // The App UI package's web plugin is left out of every web build by
                // WebBuildPlugins (it can't be linked with this Unity's Emscripten).
                // Only the "PC" quality level, like the Windows games (set in QualitySettings): with
                // "Mobile", URP's shader stripping removed every variant of the main
                // post-processing shader, so the game had no bloom or colour grading.
                var levels = QualitySettings.GetActiveQualityLevelsForPlatform("WebGL");
                if (levels.Length != 1 || QualitySettings.names[levels[0]] != "PC")
                {
                    File.WriteAllText(WebLog, "not built: the web should use only the PC quality level (Project Settings > Quality)");
                    return;
                }
                // the Input System and the game find some types by reflection: strip lightly
                PlayerSettings.SetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget.WebGL, ManagedStrippingLevel.Minimal);

                string outDir = Path.GetFullPath(StarSystemWebDir);
                if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
                Directory.CreateDirectory(outDir);
                var options = new BuildPlayerOptions
                {
                    scenes = new[] { scene },
                    locationPathName = outDir,
                    target = BuildTarget.WebGL,
                    options = BuildOptions.None,
                };
                BuildReport report = BuildPipeline.BuildPlayer(options);
                var s = report.summary;
                string msg = $"result {s.result} errors {s.totalErrors} warnings {s.totalWarnings} " +
                             $"size {s.totalSize / (1024f * 1024f):F0} MB time {s.totalTime.TotalSeconds:F0}s\n{outDir}";
                if (s.result != BuildResult.Succeeded)
                    msg += "\n" + string.Join("\n", report.steps.SelectMany(st => st.messages)
                        .Where(m => m.type == LogType.Error || m.type == LogType.Exception).Select(m => m.content).Take(30));
                File.WriteAllText(WebLog, msg);
                Debug.Log("[CarrierPlayerBuild] " + msg);
            }
            finally
            {
                PlayerSettings.productName = ProductName;
                AssetDatabase.SaveAssets();
                // back to Windows, so the other builds and play mode carry on as before
                if (EditorUserBuildSettings.activeBuildTarget != before)
                    EditorUserBuildSettings.SwitchActiveBuildTarget(beforeGroup, before);
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

using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Carrier.EditorTools
{
    /// <summary>Builds the games: three Windows (64-bit) executables (the open-space demo, the
    /// asteroid-field ambush and the star system) and the star system for web browsers. Each is
    /// a Profile run the same way: the same checks, a fresh folder, a report in its log, and the
    /// project put back as it was (its product name, and the active platform).</summary>
    public static class CarrierPlayerBuild
    {
        const string Scene = "Assets/Carrier/Scenes/CarrierShowcase.unity";
        const string ProductName = "Carrier Demo";      // the project's own name between builds

        public const string OutDir = "../Builds/CarrierDemo";   // next to the Unity project
        public const string AsteroidOutDir = "../Builds/AsteroidAmbush";
        public const string StarSystemOutDir = "../Builds/StarSystem";
        public const string StarSystemWebDir = "../Builds/StarSystemWeb";

        sealed class Profile
        {
            public string Scene, OutDir, ProductName, Log;
            /// <summary>The executable's name (none for the web: the build is the folder).</summary>
            public string Exe;
            public BuildTarget Target = BuildTarget.StandaloneWindows64;
            /// <summary>Sets the platform's settings; returns why it can't be built, or null.</summary>
            public Func<string> Configure = Windows;
        }

        static readonly Profile Demo = new Profile
        {
            Scene = Scene, OutDir = OutDir, Exe = "CarrierDemo.exe", ProductName = ProductName, Log = "Temp/carrier_player_build.log",
        };
        static readonly Profile Asteroids = new Profile
        {
            Scene = CarrierBuilder.AsteroidScenePath, OutDir = AsteroidOutDir, Exe = "AsteroidAmbush.exe", ProductName = "Asteroid Ambush",
            Log = "Temp/carrier_player2_build.log",
        };
        static readonly Profile StarSystem = new Profile
        {
            Scene = CarrierBuilder.StarSystemScenePath, OutDir = StarSystemOutDir, Exe = "StarSystem.exe", ProductName = "Star System",
            Log = "Temp/carrier_player3_build.log",
        };
        static readonly Profile StarSystemWeb = new Profile
        {
            Scene = CarrierBuilder.StarSystemScenePath, OutDir = StarSystemWebDir, ProductName = "Star System", Log = "Temp/carrier_web_build.log",
            Target = BuildTarget.WebGL, Configure = Web,
        };

        [MenuItem("Tools/Carrier/Build Windows Demo")]
        public static void Build() => Run(Demo);

        [MenuItem("Tools/Carrier/Build Asteroid Ambush")]
        public static void BuildAsteroids() => Run(Asteroids);

        [MenuItem("Tools/Carrier/Build Star System")]
        public static void BuildStarSystem() => Run(StarSystem);

        /// <summary>The star system as a web build (a folder to put on a web site). It uses WebGPU
        /// where the browser offers it, and falls back to WebGL 2 elsewhere. The files are
        /// Brotli-compressed, with the loader able to unpack them itself, so the site needs no
        /// special server setup (WebGPU does need the page served over https).</summary>
        [MenuItem("Tools/Carrier/Build Star System (Web)")]
        public static void BuildStarSystemWeb() => Run(StarSystemWeb);

        static string Windows()
        {
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.runInBackground = true;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.allowFullscreenSwitch = true;
            // DirectX 11: the mature path on hybrid (AMD + NVIDIA) laptops; DX12 hung the GPU driver here
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 });
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            return null;
        }

        static string Web()
        {
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
            // Only the "PC" quality level, like the Windows games (set in QualitySettings): with
            // "Mobile", URP's shader stripping removed every variant of the main
            // post-processing shader, so the game had no bloom or colour grading.
            var levels = QualitySettings.GetActiveQualityLevelsForPlatform("WebGL");
            if (levels.Length != 1 || QualitySettings.names[levels[0]] != "PC")
                return "the web should use only the PC quality level (Project Settings > Quality)";
            // the Input System and the game find some types by reflection: strip lightly
            PlayerSettings.SetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget.WebGL, ManagedStrippingLevel.Minimal);
            return null;
        }

        static void Run(Profile p)
        {
            var before = EditorUserBuildSettings.activeBuildTarget;
            var beforeGroup = BuildPipeline.GetBuildTargetGroup(before);
            try
            {
                if (EditorUtility.scriptCompilationFailed) { File.WriteAllText(p.Log, "not built: scripts have compile errors (old build left in place)"); return; }
                if (!File.Exists(p.Scene)) { File.WriteAllText(p.Log, $"not built: {p.Scene} is missing (run the asset build first)"); return; }
                PlayerSettings.productName = p.ProductName;
                string why = p.Configure();
                if (why != null) { File.WriteAllText(p.Log, "not built: " + why); return; }

                string outDir = Path.GetFullPath(p.OutDir);
                if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
                Directory.CreateDirectory(outDir);
                var options = new BuildPlayerOptions
                {
                    scenes = new[] { p.Scene },
                    locationPathName = p.Exe != null ? Path.Combine(outDir, p.Exe) : outDir,
                    target = p.Target,
                    options = BuildOptions.None,
                };
                BuildReport report = BuildPipeline.BuildPlayer(options);
                var s = report.summary;
                string msg = $"result {s.result} errors {s.totalErrors} warnings {s.totalWarnings} " +
                             $"size {s.totalSize / (1024f * 1024f):F0} MB time {s.totalTime.TotalSeconds:F0}s\n{options.locationPathName}";
                if (s.result != BuildResult.Succeeded)
                    msg += "\n" + string.Join("\n", report.steps.SelectMany(st => st.messages)
                        .Where(m => m.type == LogType.Error || m.type == LogType.Exception).Select(m => m.content).Take(30));
                File.WriteAllText(p.Log, msg);
                Debug.Log("[CarrierPlayerBuild] " + msg);
            }
            finally
            {
                PlayerSettings.productName = ProductName;   // the project keeps the first demo's name
                AssetDatabase.SaveAssets();
                // back to the platform it was on, so the other builds and play mode carry on as before
                if (EditorUserBuildSettings.activeBuildTarget != before)
                    EditorUserBuildSettings.SwitchActiveBuildTarget(beforeGroup, before);
            }
        }
    }
}

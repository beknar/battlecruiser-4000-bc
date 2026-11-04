using System;
using System.IO;
using System.Linq;
using System.Text;
using CarrierGame;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace Carrier.EditorTools
{
    /// <summary>
    /// Lets tools outside Unity drive the carrier build while the editor is open: write a command
    /// ("build", "shots", "probe", "playshots") to Temp/carrier_cmd.txt; the result goes to
    /// Temp/carrier_cmd_done.txt. Compiler errors are written to Temp/carrier_compile.log.
    /// </summary>
    [InitializeOnLoad]
    static class CarrierCommandWatcher
    {
        const string Cmd = "Temp/carrier_cmd.txt";
        const string Done = "Temp/carrier_cmd_done.txt";
        const string PlayKey = "carrier_playshots_start";
        static double next, refreshAt = -1;

        static CarrierCommandWatcher()
        {
            EditorApplication.update += Tick;
            CompilationPipeline.assemblyCompilationFinished += (asm, msgs) =>
            {
                var errs = msgs.Where(m => m.type == CompilerMessageType.Error).Select(m => $"{m.file}({m.line}): {m.message}").ToArray();
                if (errs.Length > 0) File.AppendAllText("Temp/carrier_compile.log", $"{DateTime.Now} {asm}\n{string.Join("\n", errs)}\n");
            };
            File.WriteAllText("Temp/carrier_watcher_alive.txt", DateTime.Now.ToString("O"));
        }

        static void Tick()
        {
            // the editor only runs play-mode frames while focused; keep them coming during the test
            if (EditorApplication.isPlaying && !float.IsNaN(SessionState.GetFloat(PlayKey, float.NaN)))
                EditorApplication.QueuePlayerLoopUpdate();
            double now = EditorApplication.timeSinceStartup;
            if (now < next) return;
            next = now + 0.5;
            if (EditorApplication.isPlaying) { PlayTick(); return; }
            if (!File.Exists(Cmd)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (refreshAt < 0) { refreshAt = now; AssetDatabase.Refresh(); return; }
            if (now - refreshAt < 3 || EditorApplication.isCompiling) return;
            refreshAt = -1;
            string cmd = File.ReadAllText(Cmd).Trim();
            File.Delete(Cmd);
            try
            {
                switch (cmd)
                {
                    case "build": CarrierBuilder.BuildAll(); break;
                    case "shots": CarrierBuilder.TakeScreenshots(); break;
                    case "probe": CarrierProbe.Probe(); break;
                    case "player": CarrierPlayerBuild.Build(); break;
                    case "player2": CarrierPlayerBuild.BuildAsteroids(); break;
                    case "player3": CarrierPlayerBuild.BuildStarSystem(); break;
                    case "playshots":
                        SessionState.SetFloat(PlayKey, -1f);
                        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Carrier/Scenes/CarrierShowcase.unity");
                        EditorApplication.EnterPlaymode();
                        File.WriteAllText(Done, "playshots started " + DateTime.Now);
                        return;
                }
                File.WriteAllText(Done, $"{cmd} ok {DateTime.Now}");
            }
            catch (Exception e)
            {
                File.WriteAllText(Done, $"{cmd} FAILED {DateTime.Now}\n{e}");
            }
        }

        // ------------------------------------------------------------ play-mode action shots
        static int stage;

        static void PlayTick()
        {
            float start = SessionState.GetFloat(PlayKey, float.NaN);
            if (float.IsNaN(start)) return;
            if (start < 0f) { SessionState.SetFloat(PlayKey, Time.time); stage = 0; return; }
            float t = Time.time - start;
            var cc = UnityEngine.Object.FindAnyObjectByType<CarrierController>();
            if (cc == null) return;
            var log = new StringBuilder();
            if (stage == 0 && t > 1f)
            {
                cc.throttle = 0.3f;
                cc.LaunchFighters(2);
                cc.LaunchDrones(1);
                cc.LaunchDropships(2);
                cc.FireMissiles(12);
                foreach (var tu in cc.mainTurrets) tu.Fire();
                stage = 1;
            }
            else if (stage == 1 && t > 4f)
            {
                Shot(cc, "play_launch", cc.transform.TransformPoint(new Vector3(420, 120, 260)), cc.transform.position + Vector3.up * 30f);
                stage = 2;
            }
            else if (stage == 2 && t > 12f)
            {
                Shot(cc, "play_wing", cc.transform.TransformPoint(new Vector3(1300, 500, 900)), cc.transform.position);
                var craft = cc.Wing.FirstOrDefault(c => c != null && c.kind == Craft.Kind.Fighter);
                if (craft) Shot(cc, "play_fighter", craft.transform.position - craft.transform.forward * 45f + craft.transform.up * 12f, craft.transform.position);
                var drop = cc.Wing.FirstOrDefault(c => c != null && c.kind == Craft.Kind.Dropship);
                if (drop) Shot(cc, "play_dropship", drop.transform.position + new Vector3(90, -30, 90), drop.transform.position);
                File.WriteAllText("Temp/carrier_play.log",
                    $"t={t:F1} wing={cc.Wing.Count} fighters={cc.fighterStock} drones={cc.droneStock} dropships={cc.dropshipStock}\n" +
                    string.Join("\n", cc.Wing.Where(c => c).Select(c => $"{c.name} {c.order} at {c.transform.position:F0} speed {c.speed:F0}")));
                stage = 3;
            }
            else if (stage == 3 && t > 13f)
            {
                SessionState.EraseFloat(PlayKey);
                EditorApplication.ExitPlaymode();
                File.WriteAllText(Done, "playshots ok " + DateTime.Now);
            }
        }

        static void Shot(CarrierController cc, string name, Vector3 pos, Vector3 look)
        {
            var go = new GameObject("PlayShotCam");
            var cam = go.AddComponent<Camera>();
            cam.farClipPlane = 30000f;
            cam.fieldOfView = 45f;
            var data = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(cam);
            data.renderPostProcessing = true;
            go.transform.position = pos;
            go.transform.LookAt(look);
            var rt = new RenderTexture(1600, 900, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
            Directory.CreateDirectory("Temp/carrier_shots");
            File.WriteAllBytes($"Temp/carrier_shots/{name}.png", tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            UnityEngine.Object.Destroy(rt);
            UnityEngine.Object.Destroy(tex);
            UnityEngine.Object.Destroy(go);
        }
    }
}

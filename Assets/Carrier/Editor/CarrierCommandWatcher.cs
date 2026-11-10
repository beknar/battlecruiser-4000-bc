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
    /// ("build", "shots", "probe", "player", "player2", "player3", "web") to Temp/carrier_cmd.txt; the result goes to
    /// Temp/carrier_cmd_done.txt. Compiler errors are written to Temp/carrier_compile.log.
    /// </summary>
    [InitializeOnLoad]
    static class CarrierCommandWatcher
    {
        const string Cmd = "Temp/carrier_cmd.txt";
        const string Done = "Temp/carrier_cmd_done.txt";
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
            double now = EditorApplication.timeSinceStartup;
            if (now < next) return;
            next = now + 0.5;
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
                    case "web": CarrierPlayerBuild.BuildStarSystemWeb(); break;
                }
                File.WriteAllText(Done, $"{cmd} ok {DateTime.Now}");
            }
            catch (Exception e)
            {
                File.WriteAllText(Done, $"{cmd} FAILED {DateTime.Now}\n{e}");
            }
        }
    }
}

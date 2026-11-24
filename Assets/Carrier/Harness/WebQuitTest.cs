using System.Collections;
using System.IO;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// Quit check for the web build: open the page with ?quittest=TOKEN. The game is flown for a
    /// few seconds (the test page moves and clicks the mouse over it meanwhile), writes TOKEN to a
    /// file under persistentDataPath, and quits through the pause menu's own Quit. The page should
    /// then leave for its "ended" page with no errors in the console. Opened again with
    /// ?quittestcheck=TOKEN, it reports whether the file came back from the browser's storage.
    /// Everything goes to the console as [QuitTest] lines (Tools/WebTest/quittest.sh reads them).
    /// </summary>
    public class WebQuitTest : MonoBehaviour
    {
        const string FileName = "quittest.txt";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!LaunchArgs.PageHas("quittest") && !LaunchArgs.PageHas("quittestcheck")) return;
            if (FindAnyObjectByType<WebQuitTest>() != null) return;
            var go = new GameObject("WebQuitTest");
            DontDestroyOnLoad(go);
            go.AddComponent<WebQuitTest>();
        }

        static void Log(string s) => Debug.Log("[QuitTest] " + s);

        static string SavePath => Path.Combine(Application.persistentDataPath, FileName);

        IEnumerator Start()
        {
            string check = LaunchArgs.PageOption("quittestcheck");
            if (check != null)
            {
                yield return new WaitForSecondsRealtime(1f);
                string found = File.Exists(SavePath) ? File.ReadAllText(SavePath).Trim() : null;
                Log(found == null ? "FAIL: no save after the reload"
                  : found == check ? "found " + found : $"FAIL: the save has '{found}', not '{check}'");
                yield break;
            }
            string token = LaunchArgs.PageOption("quittest");
            if (string.IsNullOrEmpty(token)) token = Random.Range(100000, 999999).ToString();
            Log("ready");
            yield return new WaitForSecondsRealtime(8f);
            File.WriteAllText(SavePath, token);
            Log("wrote " + token);
            // quit inside a frame, as the menu's Quit does (from its Update)
            Log("quitting");
            GameMenu.Quit();
        }
    }
}

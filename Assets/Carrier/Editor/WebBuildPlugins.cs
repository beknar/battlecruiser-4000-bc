using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Carrier.EditorTools
{
    /// <summary>
    /// The App UI package (an editor tool, pulled in by the AI Assistant) ships a web plugin for
    /// code syntax highlighting, libAppUITextMateLib.a, compiled with an older Emscripten than
    /// this Unity uses: it needs functions Emscripten 4 no longer has, so a web build can't link.
    /// The game never highlights code, so web builds leave the plugin out (the package itself is
    /// untouched), and Plugins/WebGL/AppUITextMateStubs.jslib stands in for the C functions its
    /// C# side declares. Every other build is left as it was.
    /// </summary>
    class WebBuildPlugins : IPreprocessBuildWithReport
    {
        const string Plugin = "/com.unity.dt.app-ui/Runtime/Core/Platform/WebGL/Plugins/libAppUITextMateLib.a";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            bool web = report.summary.platform == BuildTarget.WebGL;
            foreach (var pi in PluginImporter.GetAllImporters())
                if (pi.assetPath.EndsWith(Plugin)) pi.SetIncludeInBuildDelegate(_ => !web);
        }
    }
}

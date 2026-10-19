using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;

namespace PFDReader.Setup
{
    /// <summary>
    /// Eigene Assembly ohne Abhaengigkeiten, damit sie auch kompiliert, wenn der Rest wegen fehlender Pakete scheitert:
    /// setzt PFD_VUPLEX, sobald Vuplex 3D WebView im Projekt liegt (Fernseher), und entfernt es sonst.
    /// </summary>
    [InitializeOnLoad]
    static class FeatureDefines
    {
        const string Define = "PFD_VUPLEX";

        static FeatureDefines()
        {
            bool has = Directory.Exists("Assets/Vuplex/WebView");
            foreach (var target in new[] { NamedBuildTarget.Android, NamedBuildTarget.Standalone })
            {
                var defs = PlayerSettings.GetScriptingDefineSymbols(target).Split(';').Where(d => d.Length > 0).ToList();
                bool set = defs.Contains(Define);
                if (has == set) continue;
                if (has) defs.Add(Define); else defs.Remove(Define);
                PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defs));
            }
        }
    }
}

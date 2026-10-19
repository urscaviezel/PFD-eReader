using System.IO;
using UnityEditor.Android;
using UnityEngine;

namespace PFDReader.EditorTools
{
    /// <summary>
    /// Unity 6.6 erzeugt nur noch adaptive Icons (+ ein Standard-Unity-Bild als mdpi-Fallback). Der PFDMR-Launcher
    /// liest das klassische Bitmap -> zeigte das Unity-Logo. Hier: eigenes app_icon.png in alle mipmap-Dichten,
    /// adaptive XML entfernen, damit ueberall unser Logo erscheint.
    /// </summary>
    public sealed class AppIconPatcher : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 1001;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var src = Path.GetFullPath("Assets/PFDReader/Icons/app_icon.png");
            if (!File.Exists(src)) return;
            foreach (var module in new[] { path, Path.Combine(path, "..", "launcher") })
            {
                var res = Path.Combine(module, "src", "main", "res");
                if (!Directory.Exists(res)) continue;
                foreach (var dpi in new[] { "mdpi", "hdpi", "xhdpi", "xxhdpi", "xxxhdpi" })
                {
                    var dir = Path.Combine(res, "mipmap-" + dpi);
                    Directory.CreateDirectory(dir);
                    File.Copy(src, Path.Combine(dir, "app_icon.png"), true);
                    File.Copy(src, Path.Combine(dir, "app_icon_round.png"), true);
                }
                var any = Path.Combine(res, "mipmap-anydpi-v26");
                foreach (var f in new[] { "app_icon.xml", "app_icon_round.xml" })
                {
                    var x = Path.Combine(any, f);
                    if (File.Exists(x)) File.Delete(x);
                }
                Debug.Log("[PFD] App-Icon in " + res + " eingesetzt");
            }
        }
    }
}

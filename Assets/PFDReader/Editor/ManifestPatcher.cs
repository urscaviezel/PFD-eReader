using System.IO;
using UnityEditor.Android;
using UnityEngine;

namespace PFDReader.EditorTools
{
    /// <summary>Ergaenzt das Android-Manifest fuer die Update-Funktion: Installationsrecht + Dateianbieter fuer das Update-APK.</summary>
    public sealed class ManifestPatcher : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 1002;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var manifest = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifest)) return;
            var xml = File.ReadAllText(manifest);
            const string perm = "<uses-permission android:name=\"android.permission.REQUEST_INSTALL_PACKAGES\" />";
            if (!xml.Contains("REQUEST_INSTALL_PACKAGES"))
                xml = xml.Replace("<application", perm + "\n  <application");
            const string provider =
                "<provider android:name=\"org.pfd.reader.unity.PfdApkProvider\" android:authorities=\"${applicationId}.apk\" " +
                "android:exported=\"false\" android:grantUriPermissions=\"true\" />";
            if (!xml.Contains("PfdApkProvider"))
                xml = xml.Replace("</application>", "  " + provider + "\n  </application>");
            File.WriteAllText(manifest, xml);
            Debug.Log("[PFD] Manifest: Update-Installation ergaenzt");
        }
    }
}

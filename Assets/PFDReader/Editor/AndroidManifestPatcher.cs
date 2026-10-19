using System.IO;
using System.Xml;
using UnityEditor.Android;

namespace PFDReader.EditorTools
{
    /// <summary>Ergaenzt das erzeugte Android-Manifest um den Zugriff auf den gemeinsamen Buecherordner.</summary>
    public sealed class AndroidManifestPatcher : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 1000;
        const string Ns = "http://schemas.android.com/apk/res/android";

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var manifest = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifest)) return;
            var doc = new XmlDocument();
            doc.Load(manifest);
            var root = doc.DocumentElement;
            AddPermission(doc, root, "android.permission.MANAGE_EXTERNAL_STORAGE");
            AddPermission(doc, root, "android.permission.READ_EXTERNAL_STORAGE");
            AddPermission(doc, root, "android.permission.INTERNET");
            AddPermission(doc, root, "android.permission.ACCESS_NETWORK_STATE");
            AddPermission(doc, root, "android.permission.ACCESS_WIFI_STATE");
            AddPermission(doc, root, "android.permission.CHANGE_WIFI_MULTICAST_STATE");
            var app = root.SelectSingleNode("application") as XmlElement;
            if (app != null)
            {
                app.SetAttribute("requestLegacyExternalStorage", Ns, "true");
                app.SetAttribute("allowBackup", Ns, "false");
                // Android-Dateiauswahl (Import, auch Cloud-Anbieter)
                bool has = false;
                foreach (XmlNode n in app.SelectNodes("activity"))
                    if (n is XmlElement a && a.GetAttribute("name", Ns) == "org.pfd.reader.unity.PfdPicker") has = true;
                if (false && !has) // Android-Dateiauswahl vorerst nicht genutzt (fror die VR-App nach dem Schliessen ein)
                {
                    var act = doc.CreateElement("activity");
                    act.SetAttribute("name", Ns, "org.pfd.reader.unity.PfdPicker");
                    act.SetAttribute("exported", Ns, "false");
                    act.SetAttribute("theme", Ns, "@android:style/Theme.Translucent.NoTitleBar");
                    act.SetAttribute("configChanges", Ns, "orientation|screenSize|keyboardHidden");
                    app.AppendChild(act);
                }
            }
            doc.Save(manifest);
        }

        static void AddPermission(XmlDocument doc, XmlElement root, string name)
        {
            foreach (XmlNode n in root.SelectNodes("uses-permission"))
                if (n is XmlElement e && e.GetAttribute("name", Ns) == name) return;
            var el = doc.CreateElement("uses-permission");
            el.SetAttribute("name", Ns, name);
            root.PrependChild(el);
        }
    }
}

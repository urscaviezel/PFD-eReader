using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using PFDReader.Core;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using YVR.Core;

namespace PFDReader.EditorTools
{
    /// <summary>Einmalige Projekteinrichtung (ueber die Claude Bridge aufrufbar).</summary>
    public static class ProjectSetup
    {
        const string Root = "Assets/PFDReader";
        const string ScenePath = "Assets/Scenes/Main.unity";

        public static string All()
        {
            var sb = new StringBuilder();
            sb.AppendLine(ImportTmpEssentials());
            sb.AppendLine(Fonts());
            sb.AppendLine(Player());
            sb.AppendLine(Scene());
            return sb.ToString();
        }

        public static string ImportTmpEssentials()
        {
            if (Shader.Find("TextMeshPro/Distance Field") != null && File.Exists("Assets/TextMesh Pro/Resources/TMP Settings.asset"))
                return "TMP Essentials vorhanden.";
            var t = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("TMPro.TMP_PackageResourceImporter")).FirstOrDefault(x => x != null);
            var m = t?.GetMethod("ImportResources", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (m == null) return "TMP_PackageResourceImporter nicht gefunden.";
            var ps = m.GetParameters();
            var args = ps.Select((p, i) => (object)(i == 0)).ToArray(); // (essentials=true, examples=false, interactive=false)
            m.Invoke(null, args);
            return "TMP Essentials Import angestossen (" + ps.Length + " Parameter).";
        }

        public static string Fonts()
        {
            var sb = new StringBuilder();
            Directory.CreateDirectory(Root + "/Resources/Fonts");
            var inter = MakeFont(Root + "/Fonts/Inter-Regular.otf", Root + "/Resources/Fonts/Inter SDF.asset", 1024, sb);
            var lora = MakeFont(Root + "/Fonts/Lora-Regular.ttf", Root + "/Resources/Fonts/Lora SDF.asset", 2048, sb);
            if (lora != null && inter != null)
            {
                lora.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset> { inter };
                inter.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset> { lora };
                EditorUtility.SetDirty(lora); EditorUtility.SetDirty(inter);
            }
            AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        static TMP_FontAsset MakeFont(string src, string dst, int atlas, StringBuilder sb)
        {
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(dst);
            if (existing != null) { sb.AppendLine("vorhanden: " + dst); return existing; }
            var font = AssetDatabase.LoadAssetAtPath<Font>(src);
            if (font == null) { sb.AppendLine("FEHLT: " + src); return null; }
            var fa = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, atlas, atlas, AtlasPopulationMode.Dynamic, true);
            if (fa == null) { sb.AppendLine("CreateFontAsset fehlgeschlagen: " + src); return null; }
            fa.name = Path.GetFileNameWithoutExtension(dst);
            AssetDatabase.CreateAsset(fa, dst);
            if (fa.atlasTextures != null)
                foreach (var tex in fa.atlasTextures) { tex.name = fa.name + " Atlas"; AssetDatabase.AddObjectToAsset(tex, fa); }
            fa.material.name = fa.name + " Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);
            // haeufige Zeichen vorab ins Atlas (spart Ruckler beim ersten Anzeigen)
            fa.TryAddCharacters(" !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~ÄÖÜäöüßéèàçâêîôûëïÿœæ«»„“”‘’–—…·×›‹−", out _);
            EditorUtility.SetDirty(fa);
            sb.AppendLine("erstellt: " + dst);
            return fa;
        }

        public static string Player()
        {
            PlayerSettings.productName = "PFD eReader";
            PlayerSettings.companyName = "Cavi";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "org.pfd.reader.pageturn");
            PlayerSettings.bundleVersion = "1.0.0";
            PlayerSettings.Android.bundleVersionCode = Math.Max(PlayerSettings.Android.bundleVersionCode, 100);
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;

            var ks = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".android", "debug.keystore");
            if (File.Exists(ks))
            {
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = ks;
                PlayerSettings.Android.keystorePass = "android";
                PlayerSettings.Android.keyaliasName = "androiddebugkey";
                PlayerSettings.Android.keyaliasPass = "android";
            }
            AssetDatabase.SaveAssets();
            return "Player: org.pfd.reader.pageturn, versionCode " + PlayerSettings.Android.bundleVersionCode + ", Keystore " + (File.Exists(ks) ? ks : "Standard");
        }

        /// <summary>Unity speichert Keystore-Passwoerter nicht im Projekt; vor jedem Build erneut setzen.</summary>
        /// <summary>Gradle-Vorlagen aus der Unity-Installation auflisten (Diagnose).</summary>
        public static string GradleTemplates()
        {
            var dir = System.IO.Path.Combine(UnityEditor.EditorApplication.applicationContentsPath, "PlaybackEngines/AndroidPlayer/Tools/GradleTemplates");
            if (!System.IO.Directory.Exists(dir))
            {
                var alt = System.IO.Path.Combine(UnityEditor.BuildPipeline.GetPlaybackEngineDirectory(UnityEditor.BuildTarget.Android, UnityEditor.BuildOptions.None), "Tools/GradleTemplates");
                dir = alt;
            }
            return dir + "\n" + string.Join("\n", System.IO.Directory.GetFiles(dir));
        }

        /// <summary>
        /// Eigene mainTemplate.gradle anlegen (aus der Unity-Vorlage) und Bibliotheken eintragen:
        /// SMBJ (SMB2/3), DCERPC (Freigabeliste). Gradle laedt sie beim Build von Maven Central.
        /// </summary>
        public static string MainGradle()
        {
            var dir = System.IO.Path.Combine(UnityEditor.BuildPipeline.GetPlaybackEngineDirectory(UnityEditor.BuildTarget.Android, UnityEditor.BuildOptions.None), "Tools/GradleTemplates");
            var src = System.IO.Path.Combine(dir, "mainTemplate.gradle");
            var text = System.IO.File.ReadAllText(src);
            const string deps = "    implementation 'com.hierynomus:smbj:0.14.0'\n" +
                                "    implementation('com.rapid7.client:dcerpc:0.12.0') { exclude group: 'org.bouncycastle', module: 'bcprov-jdk15on'; exclude group: 'com.google.guava', module: 'guava' }\n" +
                                "    implementation 'com.google.guava:guava:32.1.3-android'\n";
            if (text.Contains("**DEPS**")) text = text.Replace("**DEPS**", deps + "**DEPS**");
            else throw new System.Exception("Platzhalter **DEPS** fehlt in " + src);
            if (!text.Contains("pickFirst") && text.Contains("android {"))
                text = text.Replace("android {", "android {\n    packagingOptions {\n        resources { excludes += ['META-INF/versions/9/OSGI-INF/MANIFEST.MF', 'META-INF/DEPENDENCIES', 'META-INF/LICENSE*', 'META-INF/NOTICE*', 'META-INF/INDEX.LIST'] }\n    }");
            System.IO.Directory.CreateDirectory("Assets/Plugins/Android");
            System.IO.File.WriteAllText("Assets/Plugins/Android/mainTemplate.gradle", text);
            UnityEditor.AssetDatabase.ImportAsset("Assets/Plugins/Android/mainTemplate.gradle");
            return src + " -> Assets/Plugins/Android/mainTemplate.gradle";
        }

        /// <summary>Unity-Paket (.unitypackage) ohne Dialog importieren.</summary>
        public static string ImportPkg(string path)
        {
            UnityEditor.AssetDatabase.ImportPackage(path, false);
            return "Import angestossen: " + path;
        }

        /// <summary>App-Symbol (PFD eReader) fuer Android setzen: klassisch, rund und adaptiv.</summary>
        public static string SetAppIcon()
        {
            const string dir = "Assets/PFDReader/Icons/";
            var icon = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "app_icon.png");
            var fg = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "app_icon_fg.png");
            var bg = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "app_icon_bg.png");
            if (icon == null) return "app_icon.png fehlt";
            var t = UnityEditor.Build.NamedBuildTarget.Android;
            UnityEditor.PlayerSettings.SetIcons(UnityEditor.Build.NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            var sb = new System.Text.StringBuilder();
            foreach (var kind in new[] { UnityEditor.Android.AndroidPlatformIconKind.Adaptive })
            {
                var icons = UnityEditor.PlayerSettings.GetPlatformIcons(t, kind);
                foreach (var pi in icons)
                {
                    if (kind == UnityEditor.Android.AndroidPlatformIconKind.Adaptive) pi.SetTextures(bg, fg);
                    else pi.SetTexture(icon);
                }
                UnityEditor.PlayerSettings.SetPlatformIcons(t, kind, icons);
                sb.Append(kind + ":" + icons.Length + " ");
            }
            UnityEditor.AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        // ---------------------------------------------------------------- Signatur
        /// <summary>Release-Schluessel liegt bewusst ausserhalb des Repos (nie veroeffentlichen, gut sichern!).</summary>
        public const string KeyDir = "D:/Unity/_Shared/keys";
        public const string ReleaseKeystore = KeyDir + "/pfd-ereader-release.keystore";
        public const string ReleasePassFile = KeyDir + "/pfd-ereader-release.password.txt";
        public const string ReleaseAlias = "pfd-ereader";

        public static void EnsureKeystorePasswords()
        {
            if (!PlayerSettings.Android.useCustomKeystore) return;
            if (PlayerSettings.Android.keystoreName.Replace('\\', '/').EndsWith("pfd-ereader-release.keystore") && File.Exists(ReleasePassFile))
            {
                var pw = File.ReadAllText(ReleasePassFile).Trim();
                PlayerSettings.Android.keystorePass = pw;
                PlayerSettings.Android.keyaliasPass = pw;
                return;
            }
            PlayerSettings.Android.keystorePass = "android";
            PlayerSettings.Android.keyaliasPass = "android";
        }

        static string JdkRoot()
        {
            try
            {
                var t = System.Type.GetType("UnityEditor.Android.AndroidExternalToolsSettings, UnityEditor.Android.Extensions");
                var p = t?.GetProperty("jdkRootPath", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                var v = p?.GetValue(null) as string;
                if (!string.IsNullOrEmpty(v) && Directory.Exists(v)) return v;
            }
            catch { }
            var guess = Path.Combine(Path.GetDirectoryName(EditorApplication.applicationPath), "Data/PlaybackEngines/AndroidPlayer/OpenJDK");
            return guess;
        }

        /// <summary>Erzeugt einmalig den Release-Schluessel (RSA 4096, 100 Jahre) und stellt das Projekt darauf um.</summary>
        public static string CreateReleaseKeystore()
        {
            Directory.CreateDirectory(KeyDir);
            if (!File.Exists(ReleaseKeystore))
            {
                var bytes = new byte[18];
                using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create()) rng.GetBytes(bytes);
                var pw = System.Convert.ToBase64String(bytes).Replace('+', 'x').Replace('/', 'y').Replace("=", "");
                var keytool = Path.Combine(JdkRoot(), "bin", Application.platform == RuntimePlatform.WindowsEditor ? "keytool.exe" : "keytool");
                if (!File.Exists(keytool)) return "keytool nicht gefunden: " + keytool;
                var psi = new System.Diagnostics.ProcessStartInfo(keytool,
                    $"-genkeypair -v -keystore \"{ReleaseKeystore}\" -storetype PKCS12 -alias {ReleaseAlias} -keyalg RSA -keysize 4096 -validity 36500 " +
                    $"-storepass {pw} -keypass {pw} -dname \"CN=PFD eReader, O=Cavi, C=CH\"")
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                using (var proc = System.Diagnostics.Process.Start(psi))
                {
                    var err = proc.StandardError.ReadToEnd() + proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit();
                    if (proc.ExitCode != 0 || !File.Exists(ReleaseKeystore)) return "keytool fehlgeschlagen: " + err;
                }
                File.WriteAllText(ReleasePassFile, pw);
            }
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = ReleaseKeystore;
            PlayerSettings.Android.keyaliasName = ReleaseAlias;
            EnsureKeystorePasswords();
            AssetDatabase.SaveAssets();
            return "Release-Schluessel aktiv: " + ReleaseKeystore;
        }

        public static string Defines()
        {
            AssetDatabase.SaveAssets();
            return "Android: " + PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android) + " | Vuplex-Ordner: " + Directory.Exists("Assets/Vuplex/WebView")
#if PFD_VUPLEX
                + " | Editor kompiliert MIT PFD_VUPLEX";
#else
                + " | Editor kompiliert OHNE PFD_VUPLEX";
#endif
        }

        /// <summary>Versionsname (z. B. 0.2.0) und versionCode setzen.</summary>
        public static string SetVersion(string name, string code)
        {
            PlayerSettings.bundleVersion = name;
            PlayerSettings.Android.bundleVersionCode = int.Parse(code);
            AssetDatabase.SaveAssets();
            return "Version " + PlayerSettings.bundleVersion + " (" + PlayerSettings.Android.bundleVersionCode + ")";
        }

        public static string Scene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.yvr.core/Prefabs/YVRCameraRig.prefab");
            if (prefab == null) return "FEHLER: YVRCameraRig.prefab nicht gefunden.";
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            rig.transform.position = Vector3.zero;
            if (UnityEngine.Object.FindFirstObjectByType<YVRManager>() == null) rig.AddComponent<YVRManager>();
            var cam = rig.GetComponentsInChildren<Camera>(true).FirstOrDefault(c => c.name == "CenterEyeAnchor") ?? rig.GetComponentInChildren<Camera>(true);
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0, 0, 0, 0);
                cam.allowHDR = false;
                cam.nearClipPlane = 0.05f;
                cam.tag = "MainCamera";
            }
            new GameObject("PFD App").AddComponent<App>();
            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            return "Szene erstellt: " + ScenePath + " (Kamera: " + (cam ? cam.name : "keine") + ")";
        }
    }
}

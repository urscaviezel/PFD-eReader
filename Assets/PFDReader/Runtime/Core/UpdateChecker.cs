using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace PFDReader.Core
{
    /// <summary>Sucht neue Versionen in den GitHub-Releases, laedt das APK und startet den Android-Installer (Bestaetigung durch Android).</summary>
    public static class UpdateChecker
    {
        [Serializable] class Asset { public string name; public string browser_download_url; public long size; }
        [Serializable] class Release { public string tag_name; public string name; public string body; public string html_url; public bool draft; public Asset[] assets; }
        [Serializable] class ReleaseList { public Release[] items; }

        public sealed class Info
        {
            public string Version, Title, Notes, PageUrl, ApkUrl;
            public long ApkSize;
        }

        public static bool Busy { get; private set; }
        public static float DownloadProgress { get; private set; } = -1f;

        /// <summary>done(info, fehler): info == null und fehler == null =&gt; aktuell.</summary>
        public static IEnumerator Check(Action<Info, string> done)
        {
            if (!AppInfo.HasRepo) { done(null, "Update-Suche ist noch nicht eingerichtet."); yield break; }
            Busy = true;
            using (var req = UnityWebRequest.Get(AppInfo.ApiReleases))
            {
                req.SetRequestHeader("User-Agent", "PFD-eReader/" + AppInfo.Version);
                req.SetRequestHeader("Accept", "application/vnd.github+json");
                req.timeout = 20;
                yield return req.SendWebRequest();
                Busy = false;
                if (req.result != UnityWebRequest.Result.Success) { done(null, "Keine Verbindung zu GitHub."); yield break; }
                Release best = null;
                try
                {
                    var list = JsonUtility.FromJson<ReleaseList>("{\"items\":" + req.downloadHandler.text + "}");
                    foreach (var r in list.items ?? new Release[0])
                    {
                        if (r.draft || r.assets == null || !r.assets.Any(a => a.name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))) continue;
                        if (best == null || AppInfo.Compare(r.tag_name, best.tag_name) > 0) best = r;
                    }
                }
                catch (Exception e) { Debug.LogWarning("[PFD] Update: " + e.Message); done(null, "Antwort von GitHub nicht lesbar."); yield break; }
                if (best == null || AppInfo.Compare(best.tag_name, AppInfo.Version) <= 0) { done(null, null); yield break; }
                var apk = best.assets.First(a => a.name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase));
                var m = System.Text.RegularExpressions.Regex.Match(best.tag_name ?? "", @"\d+(\.\d+)*");
                done(new Info
                {
                    Version = m.Success ? m.Value : best.tag_name,
                    Title = string.IsNullOrEmpty(best.name) ? best.tag_name : best.name,
                    Notes = best.body ?? "",
                    PageUrl = best.html_url,
                    ApkUrl = apk.browser_download_url,
                    ApkSize = apk.size
                }, null);
            }
        }

        /// <summary>APK herunterladen und Installer starten. done(fehler oder null).</summary>
        public static IEnumerator DownloadAndInstall(Info info, Action<string> done)
        {
            string path = ApkPath();
            if (path == null) { done("Nur auf dem Headset moeglich."); yield break; }
            Busy = true; DownloadProgress = 0f;
            try { if (File.Exists(path)) File.Delete(path); } catch { }
            using (var req = new UnityWebRequest(info.ApkUrl, UnityWebRequest.kHttpVerbGET))
            {
                req.downloadHandler = new DownloadHandlerFile(path) { removeFileOnAbort = true };
                req.SetRequestHeader("User-Agent", "PFD-eReader/" + AppInfo.Version);
                var op = req.SendWebRequest();
                while (!op.isDone) { DownloadProgress = req.downloadProgress; yield return null; }
                Busy = false; DownloadProgress = -1f;
                if (req.result != UnityWebRequest.Result.Success) { done("Download fehlgeschlagen."); yield break; }
            }
            if (info.ApkSize > 0 && new FileInfo(path).Length != info.ApkSize) { done("Download unvollstaendig."); yield break; }
            done(Install());
        }

        static string ApkPath()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var act = up.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var b = new AndroidJavaClass("org.pfd.reader.unity.PfdBridge"))
                return b.CallStatic<string>("updateApkPath", act);
#else
            return null;
#endif
        }

        /// <summary>null = Installer geoeffnet; sonst Hinweis/Fehler.</summary>
        public static string Install()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var act = up.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var b = new AndroidJavaClass("org.pfd.reader.unity.PfdBridge"))
            {
                var r = b.CallStatic<string>("installApk", act);
                if (r == "ok") return null;
                if (r == "permission") return "Bitte \"Unbekannte Apps installieren\" für PFD eReader erlauben und danach erneut auf Installieren tippen.";
                return "Installation konnte nicht gestartet werden.";
            }
#else
            return "Nur auf dem Headset moeglich.";
#endif
        }

        public static void OpenUrl(string url)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var act = up.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var b = new AndroidJavaClass("org.pfd.reader.unity.PfdBridge"))
                b.CallStatic("openUrl", act, url);
#else
            Application.OpenURL(url);
#endif
        }
    }
}

using UnityEngine;

namespace PFDReader.Core
{
    /// <summary>Feste Angaben zur App (App-Info, Update-Suche).</summary>
    public static class AppInfo
    {
        public const string Name = "PFD eReader";
        public const string Author = "Cavi";
        public const string Year = "2026";
        public const string License = "MIT";

        /// <summary>GitHub-Repository "besitzer/name". Leer = Update-Suche noch nicht eingerichtet.</summary>
        public const string GitHubRepo = "urscaviezel/PFD-eReader";

        public static string Version => Application.version;                 // z. B. 0.2.0
        public static string DisplayVersion => "Beta v" + Version;
        public static bool HasRepo => !string.IsNullOrEmpty(GitHubRepo);
        public static string RepoUrl => "https://github.com/" + GitHubRepo;
        public static string ReleasesUrl => RepoUrl + "/releases";
        public static string ApiReleases => "https://api.github.com/repos/" + GitHubRepo + "/releases?per_page=10";

        /// <summary>Vergleicht Versionen wie "0.2.0", "v0.2.1", "Beta v0.10.0" (Zahlen, sonst 0). &gt;0 wenn a neuer ist.</summary>
        public static int Compare(string a, string b)
        {
            var pa = Parts(a); var pb = Parts(b);
            for (int i = 0; i < 4; i++) { int c = pa[i].CompareTo(pb[i]); if (c != 0) return c; }
            return 0;
        }

        static int[] Parts(string v)
        {
            var r = new int[4];
            if (string.IsNullOrEmpty(v)) return r;
            var m = System.Text.RegularExpressions.Regex.Match(v, @"(\d+)(?:\.(\d+))?(?:\.(\d+))?(?:\.(\d+))?");
            for (int i = 0; i < 4; i++) if (m.Groups[i + 1].Success) int.TryParse(m.Groups[i + 1].Value, out r[i]);
            return r;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace PFDReader.Core
{
    /// <summary>Uebersetzungen DE/EN/FR. Schluessel ist der deutsche Text (wie in der alten App).</summary>
    public static class Loc
    {
        public static readonly string[] Codes = { "DE", "EN", "FR" };
        static Dictionary<string, string[]> s_Table;
        public static int Language => Mathf.Clamp(Storage.Settings?.language ?? 0, 0, 2);
        public static event Action Changed;

        public static string T(string de)
        {
            if (string.IsNullOrEmpty(de) || Language == 0) return de;
            Load();
            return s_Table.TryGetValue(de, out var v) && v.Length >= Language ? v[Language - 1] : de;
        }

        /// <summary>
        /// Meldung uebersetzen, auch zusammengesetzte ("Prefix: Detail", Java-Ausnahmen "java.io.IOException: Text").
        /// Schon uebersetzte oder unbekannte Texte bleiben unveraendert.
        /// </summary>
        public static string Msg(string s)
        {
            if (string.IsNullOrEmpty(s) || Language == 0) return s;
            Load();
            if (s_Table.TryGetValue(s, out var v) && v.Length >= Language) return v[Language - 1];
            // laengster bekannter Anfang ("PDF konnte nicht geoeffnet werden: " + Detail)
            string best = null;
            foreach (var k in s_Table.Keys)
                if (k.Length > 3 && (k.EndsWith(": ") || k.EndsWith(" ")) && s.StartsWith(k, StringComparison.Ordinal) && (best == null || k.Length > best.Length)) best = k;
            if (best != null) return s_Table[best][Language - 1] + Msg(s.Substring(best.Length));
            int i = s.LastIndexOf(": ", StringComparison.Ordinal);
            if (i > 0 && i + 2 < s.Length) { var tail = s.Substring(i + 2); var t = Msg(tail); if (t != tail) return s.Substring(0, i + 2) + t; }
            return s;
        }

        public static void SetLanguage(int lang)
        {
            Storage.Settings.language = Mathf.Clamp(lang, 0, 2);
            Storage.Settings.languageSet = true;
            Storage.SaveSettings();
            Changed?.Invoke();
        }

        static void Load()
        {
            if (s_Table != null) return;
            s_Table = new Dictionary<string, string[]>();
            var asset = Resources.Load<TextAsset>("i18n");
            if (asset == null) return;
            // Einfacher Parser fuer {"de": ["en", "fr"], ...}
            foreach (Match m in Regex.Matches(asset.text, "\"((?:[^\"\\\\]|\\\\.)*)\"\\s*:\\s*\\[\\s*\"((?:[^\"\\\\]|\\\\.)*)\"\\s*,\\s*\"((?:[^\"\\\\]|\\\\.)*)\"\\s*\\]"))
                s_Table[Unescape(m.Groups[1].Value)] = new[] { Unescape(m.Groups[2].Value), Unescape(m.Groups[3].Value) };
        }

        static string Unescape(string s) => s.Replace("\\\"", "\"").Replace("\\\\", "\\");
    }
}

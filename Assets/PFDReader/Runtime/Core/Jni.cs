using System;
using System.Threading.Tasks;
using UnityEngine;

namespace PFDReader.Core
{
    /// <summary>Kleine Helfer fuer Java-Aufrufe (auch von Hintergrund-Threads).</summary>
    public static class Jni
    {
        public static bool Available => Application.platform == RuntimePlatform.Android;

        public static AndroidJavaObject Activity()
        {
            using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                return up.GetStatic<AndroidJavaObject>("currentActivity");
        }

        public static void Call(string cls, string method, params object[] args)
        {
            if (!Available) return;
            using (var c = new AndroidJavaClass(cls)) c.CallStatic(method, args);
        }

        public static T Call<T>(string cls, string method, params object[] args)
        {
            if (!Available) return default;
            using (var c = new AndroidJavaClass(cls)) return c.CallStatic<T>(method, args);
        }

        public static T Get<T>(string cls, string field)
        {
            if (!Available) return default;
            using (var c = new AndroidJavaClass(cls)) return c.GetStatic<T>(field);
        }

        /// <summary>Fuehrt eine (blockierende) Java-Funktion auf einem Hintergrund-Thread aus; Java-Ausnahmen werden zu Exception mit lesbarer Meldung.</summary>
        public static Task<T> Background<T>(Func<T> work)
        {
            return Task.Run(() =>
            {
                bool attached = false;
                if (Available) { AndroidJNI.AttachCurrentThread(); attached = true; }
                try { return work(); }
                catch (AndroidJavaException e) { throw new Exception(Clean(e.Message)); }
                finally { if (attached) AndroidJNI.DetachCurrentThread(); }
            });
        }

        static string Clean(string m)
        {
            if (string.IsNullOrEmpty(m)) return Loc.T("Fehler");
            // "java.io.IOException: Text" -> "Text"
            int i = m.IndexOf(": ", StringComparison.Ordinal);
            var first = m.Split('\n')[0];
            i = first.IndexOf(": ", StringComparison.Ordinal);
            return i > 0 && first.Substring(0, i).Contains(".") ? first.Substring(i + 2) : first;
        }
    }
}

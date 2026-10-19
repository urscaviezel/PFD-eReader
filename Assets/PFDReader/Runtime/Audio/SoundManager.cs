using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PFDReader.Books;
using PFDReader.Core;
using UnityEngine;

namespace PFDReader.Audio
{
    /// <summary>
    /// Ambient-Klaenge (Endlosschleifen, je eigene Lautstaerke, einzeln ein/aus) und Musik (Ordner oder Playlist)
    /// mit Master-Lautstaerke und Hauptschalter. Abgespielt wird ueber Android-MediaPlayer (PfdAudio.java).
    /// Dateien werden nie geloescht: "Entfernen" nimmt einen Klang nur aus der Liste.
    /// </summary>
    public sealed class SoundManager : MonoBehaviour
    {
        const string J = "org.pfd.reader.unity.PfdAudio";
        const string MusicId = "music";
        public static readonly string[] AudioExtensions = { ".mp3", ".m4a", ".aac", ".ogg", ".oga", ".opus", ".flac", ".wav" };
        public static bool IsAudio(string path) => AudioExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());
        public static bool IsPlaylist(string path) { var e = Path.GetExtension(path).ToLowerInvariant(); return e == ".m3u" || e == ".m3u8"; }

        public static string AmbientDir => Path.Combine(Storage.ExternalRoot, "Music", "Ambient Sounds");
        public static string MusicDir => Path.Combine(Storage.ExternalRoot, "Music");

        readonly HashSet<string> m_Running = new HashSet<string>();
        List<string> m_Playlist = new List<string>();
        bool m_MusicPlaying, m_MusicPaused;
        float m_NextPoll;
        public event Action Changed;
        string m_Error; float m_ErrorTime;
        /// <summary>Letzte Fehlermeldung, nur kurz sichtbar (8 s) und bei der naechsten erfolgreichen Aktion geloescht.</summary>
        public string LastError
        {
            get => m_Error != null && Time.unscaledTime - m_ErrorTime < 8f ? m_Error : null;
            private set { m_Error = value; m_ErrorTime = Time.unscaledTime; }
        }

        static AppSettings S => Storage.Settings;
        public IReadOnlyList<string> Playlist => m_Playlist;
        public bool MusicPlaying => m_MusicPlaying;
        public bool MusicPaused => m_MusicPaused;
        public int MusicIndex => Mathf.Clamp(S.musicIndex, 0, Mathf.Max(0, m_Playlist.Count - 1));
        public string CurrentTrack => m_Playlist.Count == 0 ? "" : Path.GetFileNameWithoutExtension(m_Playlist[MusicIndex]);

        void Start()
        {
            try { Directory.CreateDirectory(AmbientDir); } catch { }
            LoadPlaylist(S.musicSource, false);
            ApplyAmbient();
        }

        float Gain(float own) => S.soundOn ? Mathf.Clamp01(S.masterVolume) * Mathf.Clamp01(own) : 0f;

        // ---------------------------------------------------------------- Ambient
        public static string AmbientId(string path) => "amb:" + path;

        public void AddAmbient(string path)
        {
            m_Error = null;
            if (S.ambient.Any(a => a.path == path)) return;
            S.ambient.Add(new AmbientEntry { path = path, volume = 0.7f, on = true });
            Storage.SaveSettings(); ApplyAmbient(); Changed?.Invoke();
        }

        public void RemoveAmbient(string path)
        {
            S.ambient.RemoveAll(a => a.path == path);
            Storage.SaveSettings(); ApplyAmbient(); Changed?.Invoke();
        }

        public void SetAmbientOn(string path, bool on)
        {
            m_Error = null;
            var e = S.ambient.FirstOrDefault(a => a.path == path); if (e == null) return;
            e.on = on; Storage.SaveSettings(); ApplyAmbient(); Changed?.Invoke();
        }

        public void SetAmbientVolume(string path, float v, bool save)
        {
            var e = S.ambient.FirstOrDefault(a => a.path == path); if (e == null) return;
            e.volume = Mathf.Clamp01(v);
            if (m_Running.Contains(AmbientId(path))) Jni.Call(J, "setVolume", AmbientId(path), Gain(e.volume));
            if (save) Storage.SaveSettings();
        }

        public void PlayAmbient(bool play)
        {
            m_Error = null;
            S.ambientPlaying = play; Storage.SaveSettings(); ApplyAmbient(); Changed?.Invoke();
        }

        /// <summary>Gewuenschten Zustand herstellen: laufende Spuren starten/stoppen, Lautstaerken setzen.</summary>
        void ApplyAmbient()
        {
            var want = new HashSet<string>();
            if (S.soundOn && S.ambientPlaying)
                foreach (var a in S.ambient) if (a.on && File.Exists(a.path)) want.Add(AmbientId(a.path));
            foreach (var id in m_Running.Where(i => i.StartsWith("amb:") && !want.Contains(i)).ToList()) { Jni.Call(J, "stop", id); m_Running.Remove(id); }
            foreach (var a in S.ambient)
            {
                var id = AmbientId(a.path);
                if (!want.Contains(id)) continue;
                if (m_Running.Add(id)) Jni.Call(J, "play", id, a.path, true, Gain(a.volume));
                else Jni.Call(J, "setVolume", id, Gain(a.volume));
            }
        }

        /// <summary>Audiodateien im Ordner (fuer die Auswahl), ohne bereits hinzugefuegte.</summary>
        public static List<string> AudioFiles(string dir)
        {
            try { return Directory.GetFiles(dir).Where(IsAudio).OrderBy(f => Path.GetFileName(f), NaturalComparer.Instance).ToList(); }
            catch { return new List<string>(); }
        }

        // ---------------------------------------------------------------- Master
        public void SetMaster(float v, bool save)
        {
            S.masterVolume = Mathf.Clamp01(v);
            RefreshVolumes();
            if (save) Storage.SaveSettings();
        }

        public void SetSoundOn(bool on)
        {
            S.soundOn = on; Storage.SaveSettings();
            ApplyAmbient();
            if (m_MusicPlaying) Jni.Call(J, "pause", MusicId, !on || m_MusicPaused);
            RefreshVolumes();
            Changed?.Invoke();
        }

        void RefreshVolumes()
        {
            foreach (var a in S.ambient) if (m_Running.Contains(AmbientId(a.path))) Jni.Call(J, "setVolume", AmbientId(a.path), Gain(a.volume));
            if (m_MusicPlaying) Jni.Call(J, "setVolume", MusicId, Gain(S.musicVolume));
        }

        // ---------------------------------------------------------------- Musik
        /// <summary>Ordner (alle Audiodateien, alphabetisch) oder Playlist (.m3u/.m3u8) laden.</summary>
        public bool LoadPlaylist(string source, bool save = true)
        {
            var list = new List<string>();
            try
            {
                if (string.IsNullOrEmpty(source)) { }
                else if (Directory.Exists(source)) list = AudioFiles(source);
                else if (File.Exists(source) && IsPlaylist(source))
                {
                    var baseDir = Path.GetDirectoryName(source);
                    foreach (var raw in File.ReadAllLines(source))
                    {
                        var line = raw.Trim().TrimStart('﻿');
                        if (line.Length == 0 || line.StartsWith("#")) continue;
                        line = line.Replace('\\', '/');
                        var full = Path.IsPathRooted(line) ? line : Path.GetFullPath(Path.Combine(baseDir, line));
                        if (File.Exists(full) && IsAudio(full)) list.Add(full);
                    }
                }
            }
            catch (Exception e) { LastError = Loc.Msg(e.Message); }
            bool changedSource = source != S.musicSource;
            if (save) { StopMusic(); S.musicSource = source; if (changedSource) S.musicIndex = 0; Storage.SaveSettings(); }
            m_Playlist = list;
            if (list.Count > 0) m_Error = null;
            Changed?.Invoke();
            return list.Count > 0;
        }

        public void SetMusicVolume(float v, bool save)
        {
            S.musicVolume = Mathf.Clamp01(v);
            if (m_MusicPlaying) Jni.Call(J, "setVolume", MusicId, Gain(S.musicVolume));
            if (save) Storage.SaveSettings();
        }

        public void PlayMusic()
        {
            if (m_Playlist.Count == 0) { LastError = Loc.T("Keine Audiodateien gefunden."); Changed?.Invoke(); return; }
            if (m_MusicPlaying && m_MusicPaused) { m_MusicPaused = false; Jni.Call(J, "pause", MusicId, !S.soundOn); Changed?.Invoke(); return; }
            if (m_MusicPlaying) return;
            StartTrack(MusicIndex);
        }

        void StartTrack(int index)
        {
            if (m_Playlist.Count == 0) return;
            S.musicIndex = (index % m_Playlist.Count + m_Playlist.Count) % m_Playlist.Count;
            Storage.SaveSettings();
            m_MusicPlaying = true; m_MusicPaused = false; m_Error = null;
            Jni.Call(J, "play", MusicId, m_Playlist[S.musicIndex], false, Gain(S.musicVolume));
            if (!S.soundOn) Jni.Call(J, "pause", MusicId, true);
            Changed?.Invoke();
        }

        public void PauseMusic()
        {
            if (!m_MusicPlaying) return;
            m_MusicPaused = !m_MusicPaused;
            Jni.Call(J, "pause", MusicId, m_MusicPaused || !S.soundOn);
            Changed?.Invoke();
        }

        public void StopMusic()
        {
            m_Error = null;
            if (m_MusicPlaying) Jni.Call(J, "stop", MusicId);
            m_MusicPlaying = false; m_MusicPaused = false;
            Changed?.Invoke();
        }

        public void Next() { if (m_Playlist.Count == 0) return; if (m_MusicPlaying) StartTrack(MusicIndex + 1); else { S.musicIndex = (MusicIndex + 1) % m_Playlist.Count; Changed?.Invoke(); } }
        public void Prev() { if (m_Playlist.Count == 0) return; if (m_MusicPlaying) StartTrack(MusicIndex - 1); else { S.musicIndex = (MusicIndex - 1 + m_Playlist.Count) % m_Playlist.Count; Changed?.Invoke(); } }

        // ---------------------------------------------------------------- Laufzeit
        void Update()
        {
            if (!Jni.Available || Time.unscaledTime < m_NextPoll) return;
            m_NextPoll = Time.unscaledTime + 0.5f;
            var fin = Jni.Call<string>(J, "pollFinished");
            if (!string.IsNullOrEmpty(fin) && fin.Split('\n').Contains(MusicId) && m_MusicPlaying) StartTrack(MusicIndex + 1);
            var err = Jni.Call<string>(J, "pollErrors");
            if (!string.IsNullOrEmpty(err))
            {
                foreach (var id in err.Split('\n'))
                {
                    if (id == MusicId) { LastError = Loc.T("Audio nicht abspielbar: ") + CurrentTrack; m_MusicPlaying = false; }
                    else if (id.StartsWith("amb:")) { m_Running.Remove(id); LastError = Loc.T("Audio nicht abspielbar: ") + Path.GetFileName(id.Substring(4)); }
                }
                Changed?.Invoke();
            }
        }

        void OnApplicationPause(bool paused)
        {
            // Headset abgenommen / App im Hintergrund: alles anhalten, danach Zustand wiederherstellen
            foreach (var id in m_Running) Jni.Call(J, "pause", id, paused || !S.soundOn);
            if (m_MusicPlaying) Jni.Call(J, "pause", MusicId, paused || m_MusicPaused || !S.soundOn);
        }

        void OnDestroy() => Jni.Call(J, "stopAll");
    }
}

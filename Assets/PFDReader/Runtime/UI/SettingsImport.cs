using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PFDReader.Books;
using PFDReader.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PFDReader.UI
{
    /// <summary>
    /// Einstellungsseite "Import": Netzwerk (SMB mit Geraetesuche, Anmeldung, Passwort anzeigen/speichern),
    /// Speicher des Headsets (durchsuchbar) und Android-Dateiauswahl (inkl. Cloud-Apps wie Google Drive/Dropbox).
    /// Alles wird nach eBooks kopiert; Originale bleiben unveraendert.
    /// </summary>
    public sealed partial class SettingsPopup
    {
        enum ImportMode { Form, Devices, Shares, Remote }
        const string JSmb = "org.pfd.reader.unity.PfdSmb";
        const string JPicker = "org.pfd.reader.unity.PfdPicker";

        int m_ImportTab;                       // 0 Netzwerk, 1 Headset, 2 Android-Auswahl
        ImportMode m_ImportMode;
        string m_Password = "";
        bool m_PasswordLoaded, m_ShowPassword;
        bool m_Busy, m_Searching, m_PickerRunning, m_SmbReady;
        string m_StatusText = "";
        TextMeshProUGUI m_Status;
        float m_NextPoll;
        readonly List<KeyValuePair<string, string>> m_Devices = new List<KeyValuePair<string, string>>();
        List<string> m_ShareNames = new List<string>();
        readonly List<(bool dir, string name, long size)> m_Remote = new List<(bool, string, long)>();
        string m_LocalDir;

        static AppSettings S => Storage.Settings;
        static string Identity => S.smbServer + "\n" + S.smbUser + "\n" + S.smbDomain;

        void EnsureSmb()
        {
            if (m_SmbReady || !Jni.Available) return;
            try { using (var act = Jni.Activity()) Jni.Call(JSmb, "init", act); m_SmbReady = true; }
            catch (Exception e) { m_StatusText = "!" + e.Message; }
        }

        // ---------------------------------------------------------------- Aufbau
        void BuildImportPage()
        {
            var tabs = RowRect("Tabs", 34);
            UiIcons.Segmented(tabs, new[] { Loc.T("Netzwerk"), Loc.T("Headset") }, m_ImportTab,
                i => { m_ImportTab = i; m_ImportMode = ImportMode.Form; StopSearch(); ShowPage(m_Page); }, 34, 150);

            if (m_ImportTab == 0)
            {
                EnsureSmb();
                if (!m_PasswordLoaded) { m_PasswordLoaded = true; if (S.smbRemember && Jni.Available) m_Password = Jni.Call<string>(JSmb, "loadPassword", Identity) ?? ""; }
                switch (m_ImportMode)
                {
                    case ImportMode.Form: BuildSmbForm(); break;
                    case ImportMode.Devices: BuildDeviceList(); break;
                    case ImportMode.Shares: BuildShareList(); break;
                    case ImportMode.Remote: BuildRemote(); break;
                }
            }
            else BuildLocal();

            m_Status = SmallLabel(m_Content, "", 0, 15);
            m_Status.Height(22);
            UpdateStatus();
        }

        void Field(string label, string value, bool password, Action<string> set)
        {
            var r = RowRect("F", 34);
            SmallLabel(r, label, 112, 16);
            string shown = string.IsNullOrEmpty(value) ? Loc.T("Zum Eingeben auswählen") : (password && !m_ShowPassword ? new string('*', Math.Min(12, value.Length)) : value);
            var b = UiKit.Button("V", r, null, 0, () => EditText(label, value, password, set));
            b.Width(0, 1);
            var l = UiKit.Label("T", b.transform, shown, 16, string.IsNullOrEmpty(value) ? UiKit.TextDim : UiKit.Text);
            l.rectTransform.Stretch(10, 0, 10, 0); l.overflowMode = TextOverflowModes.Ellipsis; l.textWrappingMode = TextWrappingModes.NoWrap;
            if (password) UiIcons.IconButton(m_ShowPassword ? "eye_off" : "eye", r, 34, () => { m_ShowPassword = !m_ShowPassword; ShowPage(m_Page); });
        }

        void SetIdentity(Action change)
        {
            bool had = S.smbRemember && Jni.Available && Jni.Call<bool>(JSmb, "hasPassword", Identity);
            var old = Identity;
            change();
            if (old != Identity && had) { Jni.Call(JSmb, "forgetPassword", old); if (S.smbRemember && m_Password.Length > 0) Jni.Call<bool>(JSmb, "savePassword", Identity, m_Password); }
            Storage.SaveSettings();
        }

        void BuildSmbForm()
        {
            Field(Loc.T("Server / IP"), S.smbServer, false, v => SetIdentity(() => S.smbServer = v.Trim()));
            Field(Loc.T("Freigabe"), S.smbShare, false, v => { S.smbShare = v.Trim(); S.smbFolder = ""; Storage.SaveSettings(); });
            Field(Loc.T("Benutzer"), S.smbUser, false, v => SetIdentity(() => S.smbUser = v.Trim()));
            Field(Loc.T("Passwort"), m_Password, true, v => { m_Password = v; if (S.smbRemember) SavePassword(); });
            Field(Loc.T("Domäne"), S.smbDomain, false, v => SetIdentity(() => S.smbDomain = v.Trim()));

            var r = RowRect("Remember", 34);
            var chk = UiIcons.IconButton(S.smbRemember ? "box_checked" : "box", r, 34, () =>
            {
                S.smbRemember = !S.smbRemember; Storage.SaveSettings();
                if (S.smbRemember) SavePassword(); else if (Jni.Available) Jni.Call(JSmb, "forgetPassword", Identity);
                ShowPage(m_Page);
            });
            SmallLabel(r, Loc.T("Passwort speichern (verschlüsselt auf dem Headset)"), 0, 15);

            var a = RowRect("Actions", 38);
            UiIcons.IconTextButton("refresh", Loc.T("Suchen"), a, 38, 15, StartSearch).Width(0, 1);
            UiIcons.IconTextButton("server", Loc.T("Freigaben"), a, 38, 15, LoadShares).Width(0, 1);
            var go = UiIcons.IconTextButton("next", Loc.T("Verbinden"), a, 38, 15, Connect); go.Width(0, 1);
            UiKit.SetToggled(go, true);
        }

        void SavePassword()
        {
            if (!Jni.Available || string.IsNullOrEmpty(m_Password)) return;
            if (!Jni.Call<bool>(JSmb, "savePassword", Identity, m_Password)) m_StatusText = "!" + Loc.T("Passwort konnte nicht gespeichert werden.");
        }

        void BuildDeviceList()
        {
            var nav = RowRect("Nav", 34);
            UiIcons.IconButton("back", nav, 34, () => { StopSearch(); m_ImportMode = ImportMode.Form; ShowPage(m_Page); });
            SmallLabel(nav, m_Searching ? Loc.T("Suche im lokalen Netz …") : Loc.T("Gerät auswählen"), 0, 16, UiKit.Text);
            if (!m_Searching) UiIcons.IconButton("refresh", nav, 34, StartSearch);
            List(150, out var list, 4);
            foreach (var d in m_Devices)
            {
                var addr = d.Key;
                Entry(list, "server", d.Value == addr ? addr : d.Value + "  ·  " + addr, null, () =>
                {
                    StopSearch();
                    SetIdentity(() => S.smbServer = addr);
                    S.smbShare = ""; S.smbFolder = ""; Storage.SaveSettings();
                    if (string.IsNullOrEmpty(S.smbUser) || string.IsNullOrEmpty(m_Password))
                    {
                        // keine anonyme Anmeldung versuchen (z. B. QNAP): erst Zugangsdaten eingeben
                        m_ImportMode = ImportMode.Form;
                        m_StatusText = Loc.T("Server übernommen. Benutzer und Passwort eingeben, dann „Freigaben“.");
                        ShowPage(m_Page);
                    }
                    else LoadShares();
                });
            }
            if (m_Devices.Count == 0 && !m_Searching)
                SmallLabel(list, Loc.T("Keine Geräte gefunden. Server manuell eingeben."), 0, 15).Height(24);
        }

        void BuildShareList()
        {
            var nav = RowRect("Nav", 34);
            UiIcons.IconButton("back", nav, 34, () => { m_ImportMode = ImportMode.Form; ShowPage(m_Page); });
            SmallLabel(nav, S.smbServer + "  ·  " + Loc.T("Freigabe auswählen"), 0, 16, UiKit.Text);
            List(150, out var list, 4);
            foreach (var s in m_ShareNames)
            {
                var name = s;
                Entry(list, "folder", name, null, () => { S.smbShare = name; S.smbFolder = ""; Storage.SaveSettings(); Connect(); });
            }
            if (m_ShareNames.Count == 0) SmallLabel(list, Loc.T("Keine Freigaben sichtbar. Namen manuell eingeben."), 0, 15).Height(24);
        }

        void BuildRemote()
        {
            var nav = RowRect("Nav", 34);
            UiIcons.IconButton("back", nav, 34, () => { m_ImportMode = ImportMode.Form; ShowPage(m_Page); });
            UiIcons.IconButton("up", nav, 34, () =>
            {
                if (string.IsNullOrEmpty(S.smbFolder)) return;
                int i = S.smbFolder.LastIndexOf('\\');
                S.smbFolder = i < 0 ? "" : S.smbFolder.Substring(0, i); Storage.SaveSettings(); LoadFolder();
            });
            UiIcons.IconButton("refresh", nav, 34, LoadFolder);
            SmallLabel(nav, "\\\\" + S.smbServer + "\\" + S.smbShare + (string.IsNullOrEmpty(S.smbFolder) ? "" : "\\" + S.smbFolder), 0, 15);

            List(180, out var list, 4);
            foreach (var e in m_Remote)
            {
                var item = e;
                if (item.dir) Entry(list, "folder", item.name, null, () => { S.smbFolder = string.IsNullOrEmpty(S.smbFolder) ? item.name : S.smbFolder + "\\" + item.name; Storage.SaveSettings(); LoadFolder(); });
                else
                {
                    bool ok = BookFormats.IsSupported(item.name);
                    UnityEngine.Events.UnityAction act = null;
                    if (ok) act = () => CopyRemote(item.name);
                    Entry(list, ok ? "import" : "file", item.name, Size(item.size), act);
                }
            }
            if (m_Remote.Count == 0 && !m_Busy) SmallLabel(list, Loc.T("Dieses Verzeichnis ist leer."), 0, 15).Height(24);
        }

        static string Size(long b) => b >= 1 << 20 ? (b / 1048576.0).ToString("0.0") + " MB" : Math.Max(1, b / 1024) + " KB";

        // ---------------------------------------------------------------- Netzwerk-Aktionen
        async void RunSmb(string busyText, Func<string> work, Action<string> done)
        {
            if (m_Busy) return;
            if (!Jni.Available) { m_StatusText = "!" + Loc.T("Netzwerkzugriff nur auf dem Headset."); UpdateStatus(); return; }
            EnsureSmb();
            m_Busy = true; m_StatusText = busyText; UpdateStatus();
            try
            {
                var result = await Jni.Background(work);
                m_Busy = false;
                done(result);
            }
            catch (Exception e) { m_StatusText = "!" + e.Message; }
            finally
            {
                m_Busy = false;
                if (Visible && m_Page == PageImport && m_Keyboard == null) ShowPage(m_Page);
            }
        }

        void LoadShares()
        {
            if (string.IsNullOrEmpty(S.smbServer)) { m_StatusText = "!" + Loc.T("Server zuerst eingeben oder suchen."); UpdateStatus(); return; }
            if (m_ImportMode == ImportMode.Devices) m_ImportMode = ImportMode.Form;
            string server = S.smbServer, user = S.smbUser, pw = m_Password, dom = S.smbDomain;
            RunSmb(Loc.T("Freigaben werden gelesen …"), () => Jni.Call<string>(JSmb, "shares", server, user, pw, dom) ?? "", r =>
            {
                m_ShareNames = r.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                m_ImportMode = ImportMode.Shares;
                m_StatusText = m_ShareNames.Count == 0 ? Loc.T("Keine Freigaben sichtbar. Namen manuell eingeben.") : Loc.T("Freigabe auswählen");
                if (S.smbRemember) SavePassword();
            });
        }

        void Connect()
        {
            if (string.IsNullOrEmpty(S.smbServer)) { m_StatusText = "!" + Loc.T("Server zuerst eingeben oder suchen."); UpdateStatus(); return; }
            // \\server\freigabe\ordner oder smb://… in einem Feld erlauben
            if (Jni.Available)
            {
                try
                {
                    var a = Jni.Call<string[]>(JSmb, "parse", S.smbServer, S.smbShare ?? "");
                    if (a != null && a.Length == 4)
                    {
                        var server = a[1] == "445" ? a[0] : a[0] + ":" + a[1];
                        if (server != S.smbServer) SetIdentity(() => S.smbServer = server);
                        if (!string.IsNullOrEmpty(a[2])) S.smbShare = a[2];
                        if (!string.IsNullOrEmpty(a[3])) S.smbFolder = a[3];
                        Storage.SaveSettings();
                    }
                }
                catch (Exception e) { m_StatusText = "!" + Loc.T("Ungültiger Server oder Netzwerkpfad") + " (" + e.Message + ")"; UpdateStatus(); return; }
            }
            if (string.IsNullOrEmpty(S.smbShare)) { LoadShares(); return; }
            LoadFolder();
        }

        void LoadFolder()
        {
            string server = S.smbServer, share = S.smbShare, folder = S.smbFolder ?? "", user = S.smbUser, pw = m_Password, dom = S.smbDomain;
            RunSmb(Loc.T("Verbinde und lese Ordner …"), () => Jni.Call<string>(JSmb, "list", server, share, folder, user, pw, dom) ?? "", r =>
            {
                m_Remote.Clear();
                foreach (var line in r.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var p = line.Split('|'); if (p.Length < 3) continue;
                    long.TryParse(p[p.Length - 1], out var size);
                    var name = string.Join("|", p.Skip(1).Take(p.Length - 2));
                    m_Remote.Add((p[0] == "D", name, size));
                }
                m_ImportMode = ImportMode.Remote;
                m_StatusText = Loc.T("Datei auswählen, um sie nach eBooks zu kopieren.");
                if (S.smbRemember) SavePassword();
            });
        }

        void CopyRemote(string name)
        {
            string server = S.smbServer, share = S.smbShare, folder = S.smbFolder ?? "", user = S.smbUser, pw = m_Password, dom = S.smbDomain, dest = Storage.SharedBooksDir;
            RunSmb(Loc.T("Kopieren: ") + name, () => Jni.Call<string>(JSmb, "copy", server, share, folder, name, user, pw, dom, dest), path =>
            {
                m_StatusText = Loc.T("In eBooks gespeichert: ") + Path.GetFileName(path);
                Imported?.Invoke(path);
            });
        }

        void StartSearch()
        {
            if (!Jni.Available) { m_StatusText = "!" + Loc.T("Netzwerkzugriff nur auf dem Headset."); UpdateStatus(); return; }
            EnsureSmb();
            m_Devices.Clear();
            Jni.Call(JSmb, "startDiscovery");
            m_Searching = true; m_ImportMode = ImportMode.Devices; m_NextPoll = Time.unscaledTime + 0.6f;
            m_StatusText = Loc.T("Suche im lokalen Netz …");
            ShowPage(m_Page);
        }

        void StopSearch()
        {
            if (!m_Searching) return;
            m_Searching = false;
            Jni.Call(JSmb, "stopDiscovery");
        }

        // ---------------------------------------------------------------- Speicher des Headsets
        void BuildLocal()
        {
            var root = Storage.ExternalRoot;
            if (string.IsNullOrEmpty(m_LocalDir)) m_LocalDir = Directory.Exists(S.importLastDir) ? S.importLastDir : Path.Combine(root, "Download");
            if (!Directory.Exists(m_LocalDir)) m_LocalDir = root;

            var nav = RowRect("Nav", 34);
            UiIcons.IconButton("up", nav, 34, () => { var p = Path.GetDirectoryName(m_LocalDir); if (p != null && p.Length >= root.Length) { m_LocalDir = p; ShowPage(m_Page); } });
            UiIcons.IconButton("device", nav, 34, () => { m_LocalDir = root; ShowPage(m_Page); });
            SmallLabel(nav, Display(m_LocalDir), 0, 15);

            List(200, out var list, 4);
            string[] dirs = new string[0], files = new string[0];
            try
            {
                dirs = Directory.GetDirectories(m_LocalDir).Where(d => !Path.GetFileName(d).StartsWith(".") && Path.GetFileName(d) != "Android").OrderBy(d => Path.GetFileName(d), NaturalComparer.Instance).ToArray();
                files = Directory.GetFiles(m_LocalDir).Where(BookFormats.IsSupported).OrderBy(f => Path.GetFileName(f), NaturalComparer.Instance).ToArray();
            }
            catch (Exception e) { SmallLabel(list, Loc.Msg(e.Message), 0, 15, UiKit.Danger).Height(24); }
            var books = Path.GetFullPath(Storage.SharedBooksDir).TrimEnd('/', '\\');
            bool inBooks = Path.GetFullPath(m_LocalDir).TrimEnd('/', '\\') == books;
            foreach (var d in dirs) { var dir = d; Entry(list, "folder", Path.GetFileName(d), null, () => { m_LocalDir = dir; S.importLastDir = dir; Storage.SaveSettings(); ShowPage(m_Page); }); }
            foreach (var f in files)
            {
                var file = f; long size = 0; try { size = new FileInfo(f).Length; } catch { }
                if (inBooks) Entry(list, "check", Path.GetFileName(f), Loc.T("in eBooks"), null);
                else Entry(list, "import", Path.GetFileName(f), Size(size), () => CopyLocal(file));
            }
            if (dirs.Length == 0 && files.Length == 0) SmallLabel(list, Loc.T("Keine Bücher oder Comics in diesem Ordner."), 0, 15).Height(24);
        }

        async void CopyLocal(string file)
        {
            if (m_Busy) return;
            m_Busy = true; m_StatusText = Loc.T("Kopieren: ") + Path.GetFileName(file); UpdateStatus();
            try
            {
                var dest = Storage.SharedBooksDir;
                var target = await Task.Run(() => CopyUnique(file, dest));
                m_StatusText = Loc.T("In eBooks gespeichert: ") + Path.GetFileName(target);
                Imported?.Invoke(target);
            }
            catch (Exception e) { m_StatusText = "!" + e.Message; }
            finally { m_Busy = false; UpdateStatus(); }
        }

        /// <summary>Kopiert nach dir; vorhandene Dateien bleiben unberuehrt ("Name (1).epub").</summary>
        static string CopyUnique(string src, string dir)
        {
            Directory.CreateDirectory(dir);
            var name = Path.GetFileNameWithoutExtension(src); var ext = Path.GetExtension(src);
            for (int i = 0; i < 10000; i++)
            {
                var t = Path.Combine(dir, i == 0 ? name + ext : $"{name} ({i}){ext}");
                if (File.Exists(t)) continue;
                var tmp = t + ".part";
                File.Copy(src, tmp, true);
                File.Move(tmp, t);
                return t;
            }
            throw new IOException("Zu viele gleichnamige Dateien");
        }

        // ---------------------------------------------------------------- Android-Dateiauswahl
        void BuildPicker()
        {
            var t = UiKit.Label("T", m_Content, Loc.T("Öffnet die Android-Dateiauswahl. Dort erscheinen auch Google Drive, Dropbox und andere Cloud-Speicher, sofern deren App auf dem Headset installiert und angemeldet ist. Ausgewählte Bücher werden nach eBooks kopiert."), 16, UiKit.TextDim, TextAlignmentOptions.TopLeft);
            t.textWrappingMode = TextWrappingModes.Normal; t.Height(110);
            var r = RowRect("Open", 40);
            var b = UiIcons.IconTextButton("cloud", Loc.T("Dateiauswahl öffnen"), r, 40, 17, OpenPicker);
            b.Width(280); UiKit.SetToggled(b, true);
        }

        void OpenPicker()
        {
            if (!Jni.Available) { m_StatusText = "!" + Loc.T("Nur auf dem Headset verfügbar."); UpdateStatus(); return; }
            try
            {
                using (var act = Jni.Activity()) Jni.Call(JPicker, "open", act, Storage.SharedBooksDir);
                m_PickerRunning = true; m_StatusText = Loc.T("Dateiauswahl geöffnet …"); UpdateStatus();
            }
            catch (Exception e) { m_StatusText = "!" + e.Message; UpdateStatus(); }
        }

        // ---------------------------------------------------------------- Laufend
        void TickImport()
        {
            if (Time.unscaledTime < m_NextPoll) return;
            m_NextPoll = Time.unscaledTime + 0.3f;
            if (m_Searching)
            {
                var r = Jni.Call<string>(JSmb, "pollDiscovery") ?? "0";
                var lines = r.Split('\n');
                bool running = lines[0] == "1";
                var list = lines.Skip(1).Select(l => l.Split('\t')).Where(p => p.Length == 2).Select(p => new KeyValuePair<string, string>(p[0], p[1])).ToList();
                bool changed = list.Count != m_Devices.Count || !running;
                m_Devices.Clear(); m_Devices.AddRange(list);
                if (!running)
                {
                    m_Searching = false;
                    m_StatusText = m_Devices.Count == 0 ? Loc.T("Keine Geräte gefunden. Server manuell eingeben.") : Loc.T("Suche beendet");
                }
                if (changed && m_ImportMode == ImportMode.Devices && m_Keyboard == null) ShowPage(m_Page);
            }
            if (m_Busy && m_ImportTab == 0 && Jni.Available)
            {
                long copied = Jni.Get<long>(JSmb, "copied");
                var st = Jni.Get<string>(JSmb, "status");
                if (m_StatusText.StartsWith(Loc.T("Kopieren: ")) && copied > 0) SetStatusLine(m_StatusText + "  " + Size(copied));
                else if (!string.IsNullOrEmpty(st) && !m_StatusText.StartsWith(Loc.T("Kopieren: "))) SetStatusLine(Loc.T(st));
            }
            if (m_PickerRunning && Jni.Available)
            {
                var res = Jni.Get<string>(JPicker, "result");
                if (res != null)
                {
                    m_PickerRunning = false;
                    int ok = 0; var errors = new List<string>();
                    foreach (var line in res.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (line.StartsWith("!")) errors.Add(line.Substring(1));
                        else { ok++; Imported?.Invoke(line); }
                    }
                    m_StatusText = ok == 0 && errors.Count == 0 ? Loc.T("Keine Datei ausgewählt.")
                        : (ok > 0 ? ok + " " + Loc.T("Buch/Bücher nach eBooks kopiert.") : "") + (errors.Count > 0 ? (ok > 0 ? "  " : "!") + string.Join(", ", errors) : "");
                    UpdateStatus();
                }
            }
        }

        void SetStatusLine(string text)
        {
            if (m_Status == null) return;
            m_Status.text = text; m_Status.color = UiKit.TextDim;
        }

        void UpdateStatus()
        {
            if (m_Status == null) return;
            bool err = m_StatusText.StartsWith("!");
            m_Status.text = Loc.Msg(err ? m_StatusText.Substring(1) : m_StatusText);
            m_Status.color = err ? UiKit.Danger : UiKit.TextDim;
        }
    }
}

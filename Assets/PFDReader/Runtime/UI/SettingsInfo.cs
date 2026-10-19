using System.Collections;
using PFDReader.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PFDReader.UI
{
    /// <summary>Einstellungen → App-Info: Version, Lizenz, Drittanbieter, Update-Suche.</summary>
    public sealed partial class SettingsPopup
    {
        /// <summary>Startet Coroutinen (Update-Suche/Download); von der App gesetzt.</summary>
        public MonoBehaviour Host;
        public event System.Action ShowLicenses;

        UpdateChecker.Info m_Update;
        string m_UpdateMsg;
        bool m_ApkReady;
        TextMeshProUGUI m_InfoStatus;
        RectTransform m_InfoBar, m_InfoFill;

        void BuildInfoPage()
        {
            var t = UiKit.Label("Name", m_Content, AppInfo.Name, 30, UiKit.Text);
            t.fontStyle = FontStyles.Bold; t.Height(40);
            var v = UiKit.Label("Version", m_Content, AppInfo.DisplayVersion, 20, UiKit.Cyan); v.Height(26);
            var c = UiKit.Label("Copy", m_Content,
                "© " + AppInfo.Year + " " + AppInfo.Author + " · " + Loc.T("Freie Software unter der MIT-Lizenz") + "\n" +
                (AppInfo.HasRepo ? AppInfo.RepoUrl.Replace("https://", "") : Loc.T("GitHub-Seite folgt")), 17, UiKit.TextDim, TextAlignmentOptions.TopLeft);
            c.textWrappingMode = TextWrappingModes.Normal; c.Height(48);
            var lr = UiKit.Rect("LicRow", m_Content); lr.Height(36); UiKit.Row(lr, 0);
            UiIcons.IconTextButton("file", Loc.T("Lizenzen & Drittanbieter"), lr, 36, 17, () => { Hide(); ShowLicenses?.Invoke(); }).Width(340);

            Heading(Loc.T("Updates"));
            var ar = UiKit.Rect("AutoRow", m_Content); ar.Height(34); UiKit.Row(ar, 10);
            var al = UiKit.Label("L", ar, Loc.T("Automatisch suchen"), 18, UiKit.TextDim); al.Width(200);
            UiIcons.Segmented(ar, new[] { Loc.T("Ein"), Loc.T("Aus") }, Storage.Settings.autoUpdateCheck ? 0 : 1,
                i => { Storage.Settings.autoUpdateCheck = i == 0; Storage.SaveSettings(); });

            m_InfoStatus = UiKit.Label("Status", m_Content, "", 17, UiKit.TextDim, TextAlignmentOptions.TopLeft);
            m_InfoStatus.textWrappingMode = TextWrappingModes.Normal; m_InfoStatus.Height(44);

            var bar = UiKit.Panel("Progress", m_Content, UiKit.Line); bar.Height(5);
            m_InfoBar = bar.rectTransform;
            var fill = UiKit.Panel("Fill", m_InfoBar, UiKit.Cyan); UiGradient.Set(fill, true);
            m_InfoFill = fill.rectTransform;
            m_InfoFill.anchorMin = Vector2.zero; m_InfoFill.anchorMax = new Vector2(0, 1); m_InfoFill.pivot = new Vector2(0, 0.5f);
            m_InfoFill.offsetMin = m_InfoFill.offsetMax = Vector2.zero;
            bar.gameObject.SetActive(UpdateChecker.DownloadProgress >= 0f);

            var row = UiKit.Rect("Buttons", m_Content); row.Height(36); UiKit.Row(row, 10);
            if (m_Update == null)
                UiIcons.IconTextButton("refresh", Loc.T("Nach Updates suchen"), row, 36, 17, StartCheck).Width(260);
            else
            {
                UiIcons.IconTextButton("import", m_ApkReady ? Loc.T("Installieren") : Loc.T("Herunterladen & installieren"), row, 36, 17, StartInstall).Width(300);
                UiIcons.IconTextButton("globe", Loc.T("Release-Seite"), row, 36, 17, () => UpdateChecker.OpenUrl(m_Update.PageUrl)).Width(200);
            }
            ShowUpdateStatus();
        }

        /// <summary>Von der automatischen Suche: gefundenes Update in der App-Info anzeigen.</summary>
        public void ShowUpdate(UpdateChecker.Info info)
        {
            m_Update = info; m_ApkReady = false; m_UpdateMsg = null;
            Show(PageInfo);
        }

        void ShowUpdateStatus()
        {
            if (m_InfoStatus == null) return;
            string s;
            if (UpdateChecker.DownloadProgress >= 0f) s = Loc.T("Lade Update herunter …") + " " + Mathf.RoundToInt(UpdateChecker.DownloadProgress * 100) + " %";
            else if (UpdateChecker.Busy) s = Loc.T("Suche nach Updates …");
            else if (m_UpdateMsg != null) s = m_UpdateMsg;
            else if (m_Update != null) s = Loc.T("Neue Version verfügbar:") + " Beta v" + m_Update.Version + (m_Update.ApkSize > 0 ? "  (" + (m_Update.ApkSize / 1048576) + " MB)" : "");
            else s = Loc.T("Installiert:") + " " + AppInfo.DisplayVersion;
            m_InfoStatus.text = s;
            if (m_InfoBar != null)
            {
                bool on = UpdateChecker.DownloadProgress >= 0f;
                m_InfoBar.gameObject.SetActive(on);
                if (on) m_InfoFill.anchorMax = new Vector2(Mathf.Clamp01(UpdateChecker.DownloadProgress), 1f);
            }
        }

        void RefreshInfo() { if (Visible && m_Page == PageInfo) ShowPage(PageInfo); }

        void StartCheck()
        {
            if (Host == null || UpdateChecker.Busy) return;
            m_UpdateMsg = null;
            Host.StartCoroutine(CheckRoutine());
        }

        IEnumerator CheckRoutine()
        {
            var co = Host.StartCoroutine(UpdateChecker.Check((info, err) =>
            {
                m_Update = info;
                m_UpdateMsg = err != null ? Loc.Msg(err) : info == null ? Loc.T("Du hast die neueste Version.") + " (" + AppInfo.DisplayVersion + ")" : null;
                Storage.Settings.lastUpdateCheck = System.DateTime.UtcNow.Ticks; Storage.SaveSettings();
            }));
            ShowUpdateStatus();
            yield return co;
            RefreshInfo();
        }

        void StartInstall()
        {
            if (Host == null || UpdateChecker.Busy || m_Update == null) return;
            if (m_ApkReady)
            {
                var r = UpdateChecker.Install();
                m_UpdateMsg = r != null ? Loc.Msg(r) : Loc.T("Installer geöffnet – bitte dort bestätigen.");
                RefreshInfo();
                return;
            }
            Host.StartCoroutine(InstallRoutine());
        }

        IEnumerator InstallRoutine()
        {
            string result = "";
            var co = Host.StartCoroutine(UpdateChecker.DownloadAndInstall(m_Update, r => result = r));
            while (UpdateChecker.Busy || UpdateChecker.DownloadProgress >= 0f) { ShowUpdateStatus(); yield return new WaitForSecondsRealtime(0.2f); }
            yield return co;
            m_ApkReady = result == null || result.StartsWith("Bitte");
            m_UpdateMsg = result != null ? Loc.Msg(result) : Loc.T("Installer geöffnet – bitte dort bestätigen.");
            RefreshInfo();
        }
    }
}

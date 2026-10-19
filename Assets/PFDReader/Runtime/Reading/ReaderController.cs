using System;
using System.Collections.Generic;
using System.Linq;
using PFDReader.Books;
using PFDReader.Core;
using PFDReader.Rendering;
using PFDReader.View;
using UnityEngine;

namespace PFDReader.Reading
{
    /// <summary>Verbindet Dokument, Seitencache und 3D-Buch: Doppelseiten, Blaettern (animiert oder gezogen), Lesestand.</summary>
    public sealed class ReaderController : IDisposable
    {
        const float TurnSeconds = 0.42f;

        readonly BookView m_View;
        public ReaderDocument Doc { get; private set; }
        PageCache m_Cache;
        BookState m_State;

        PageRef m_Left;           // linke Seite der aktuellen Doppelseite
        // Blaettern
        int m_Dir;                // +1 vor, -1 zurueck, 0 keine
        float m_T, m_TargetT;     // 0 = Blatt rechts, 1 = Blatt links
        bool m_Dragging;
        PageRef? m_BL, m_BR, m_LF, m_LB, m_NewLeft;
        bool m_Dirty = true;

        public event Action Changed;

        readonly TMPro.TMP_FontAsset m_CoverFont;
        RenderTexture m_Cover, m_Spine;
        public bool Closed { get; private set; }

        public ReaderController(BookView view, TMPro.TMP_FontAsset coverFont) { m_View = view; m_CoverFont = coverFont; }

        /// <summary>Buch zuklappen (Deckel mit Titel sichtbar) bzw. wieder aufschlagen.</summary>
        public void SetClosed(bool closed)
        {
            if (Doc == null) return;
            if (closed) { if (m_Dragging) CancelDrag(); if (m_Dir != 0) { m_T = m_TargetT; FinishTurn(); } }
            Closed = closed;
            m_View.SetClosed(closed);
            Changed?.Invoke();
        }

        bool Busy => Closed || !m_View.IsOpenVisual;

        // ---------------------------------------------------------------- Zoom (PDF/Comic)
        public float Zoom { get; private set; } = 1f;
        public Vector2 Pan { get; private set; } = new Vector2(0.5f, 0.5f);
        float m_DetailAt; bool m_DetailWanted;
        public bool CanZoom => Doc != null && !Doc.IsText;

        public void StepZoom(int dir)
        {
            if (!CanZoom) return;
            Zoom = Mathf.Clamp(Mathf.Round((Zoom + (dir > 0 ? 0.25f : -0.25f)) * 4f) / 4f, 1f, 3f);
            if (Zoom <= 1f) Pan = new Vector2(0.5f, 0.5f);
            m_State.zoom = Zoom; Storage.SaveBooks();
            m_Cache.ClearDetail();
            RequestDetailSoon();
            Changed?.Invoke();
        }

        /// <summary>Ausschnitt verschieben (Stick). delta in Bildschirmrichtung: +x rechts, +y oben.</summary>
        public void PanBy(Vector2 delta)
        {
            if (!CanZoom || Zoom <= 1f) return;
            var p = Pan + new Vector2(delta.x, -delta.y) / Zoom;
            Pan = new Vector2(Mathf.Clamp01(p.x), Mathf.Clamp01(p.y));
            RequestDetailSoon();
        }

        void RequestDetailSoon() { m_DetailAt = Time.time + 0.3f; m_DetailWanted = true; m_Dirty = true; }

        Vector4 ZoomST => Zoom <= 1f ? new Vector4(1, 1, 0, 0)
            : new Vector4(1f / Zoom, 1f / Zoom, Pan.x * (1 - 1f / Zoom), (1 - Pan.y) * (1 - 1f / Zoom));

        public bool IsOpen => Doc != null;
        public bool IsTurning => m_Dir != 0;

        // ---------------------------------------------------------------- Oeffnen / Schliessen
        public void Open(ReaderDocument doc)
        {
            Close();
            Doc = doc;
            m_Cache = new PageCache(doc);
            m_Cache.PageReady += _ => m_Dirty = true;
            m_State = Storage.Book(doc.Key);
            m_State.lastOpened = DateTime.UtcNow.Ticks;
            BookPosition.TryParse(m_State.position, out var pos);
            Zoom = doc.IsText ? 1f : Mathf.Clamp(m_State.zoom, 1f, 3f);
            Pan = new Vector2(0.5f, 0.5f);
            m_Left = AlignLeft(doc.FromPosition(pos));
            ApplyTheme();
            m_Cover = Rendering.CoverRenderer.Render(doc.Title, doc.Author, m_CoverFont, m_Cover);
            m_View.SetCoverTexture(m_Cover);
            m_Spine = Rendering.CoverRenderer.RenderSpine(doc.Title, doc.Author, m_CoverFont, m_Spine);
            m_View.SetSpineTexture(m_Spine);
            // Neu geoeffnetes Buch erscheint zugeklappt und mittig; Aufschlagen per Trigger
            Closed = true;
            m_View.SetClosed(true, true);
            m_Dirty = true;
            Save();
        }

        public void Close()
        {
            if (Doc == null) return;
            Save();
            m_Dir = 0; m_Dragging = false;
            m_Cache?.Dispose(); m_Cache = null;
            Doc.Dispose(); Doc = null;
            m_View.SetStatic(null, null);
        }

        // Bei Text bleibt die Doppelseite an der gespeicherten Seite ausgerichtet; bei festen Seiten gerade Seitenzahl links.
        PageRef AlignLeft(PageRef r) => Doc.IsText ? r : new PageRef(0, r.page - (r.page % 2));

        PageRef? Next(PageRef? r) => r.HasValue && Doc.TryNext(r.Value, out var n) ? n : (PageRef?)null;
        PageRef? Prev(PageRef? r) => r.HasValue && Doc.TryPrev(r.Value, out var n) ? n : (PageRef?)null;
        PageRef? Right => Next(m_Left);

        // ---------------------------------------------------------------- Navigation
        public bool CanNext => Next(Right).HasValue;
        public bool CanPrev => Prev(m_Left).HasValue;

        /// <summary>Animiertes Blaettern (Stick, Pfeile).</summary>
        public void Turn(int dir)
        {
            if (Doc == null || Busy) return;
            if (m_Dir != 0) { if (!m_Dragging) FinishTurn(); else return; }
            if (!BeginTurn(dir)) return;
            m_TargetT = dir > 0 ? 1 : 0;
        }

        /// <summary>Gezogenes Blaettern: Start am Seitenrand.</summary>
        public bool BeginDrag(int dir, float t)
        {
            if (Doc == null || m_Dir != 0 || Busy) return false;
            if (!BeginTurn(dir)) return false;
            m_Dragging = true;
            m_T = m_TargetT = Mathf.Clamp01(t);
            return true;
        }

        public void UpdateDrag(float t) { if (m_Dragging) m_TargetT = Mathf.Clamp01(t); }

        public void EndDrag()
        {
            if (!m_Dragging) return;
            m_Dragging = false;
            if (m_Dir > 0) m_TargetT = m_T > 0.28f ? 1 : 0;
            else m_TargetT = m_T < 0.72f ? 0 : 1;
        }

        public void CancelDrag() { if (m_Dragging) { m_Dragging = false; m_TargetT = m_Dir > 0 ? 0 : 1; } }

        bool BeginTurn(int dir)
        {
            var right = Right;
            if (dir > 0)
            {
                var l1 = Next(right); if (!l1.HasValue) return false;
                m_BL = m_Left; m_LF = right; m_LB = l1; m_BR = Next(l1); m_NewLeft = l1;
                m_T = 0;
            }
            else
            {
                var r0 = Prev(m_Left); if (!r0.HasValue) return false;
                var l0 = Prev(r0);
                m_BL = l0; m_LF = r0; m_LB = m_Left; m_BR = right; m_NewLeft = l0 ?? r0;
                m_T = 1;
            }
            m_Dir = dir;
            m_Dirty = true;
            return true;
        }

        void FinishTurn()
        {
            bool committed = m_Dir > 0 ? m_T >= 0.999f : m_T <= 0.001f;
            if (committed && m_NewLeft.HasValue) { m_Left = m_NewLeft.Value; m_Cache.ClearDetail(); if (Zoom > 1f) RequestDetailSoon(); }
            m_Dir = 0; m_Dragging = false;
            m_Dirty = true;
            if (committed) Save();
            Changed?.Invoke();
        }

        public void GoTo(BookPosition pos)
        {
            if (Doc == null) return;
            m_Dir = 0; m_Dragging = false;
            m_Left = AlignLeft(Doc.FromPosition(pos));
            m_Dirty = true;
            Save();
            Changed?.Invoke();
        }

        // ---------------------------------------------------------------- Darstellung
        public void SetFontSize(float size)
        {
            Storage.Settings.fontSize = Mathf.Clamp(size, 30f, 84f);
            Storage.SaveSettings();
            if (Doc == null || !Doc.IsText) return;
            var pos = Doc.ToPosition(m_Left);
            m_Dir = 0; m_Dragging = false;
            Doc.Text.SetStyle(new TextStyle { fontSize = Storage.Settings.fontSize, dark = Storage.Settings.dark });
            m_Cache.Invalidate();
            m_Left = Doc.FromPosition(pos);
            m_Dirty = true;
            Save();
            Changed?.Invoke();
        }

        public void ApplyTheme()
        {
            if (Doc == null) return;
            if (Doc.IsText)
            {
                var pos = Doc.ToPosition(m_Left);
                var style = new TextStyle { fontSize = Storage.Settings.fontSize, dark = Storage.Settings.dark };
                if (!style.Equals(Doc.Text.Style)) { Doc.Text.SetStyle(style); m_Cache.Invalidate(); m_Left = Doc.FromPosition(pos); }
            }
            m_View.SetTint(Color.white);
            m_View.SetPaper(Storage.Settings.dark ? new Color(0.11f, 0.105f, 0.10f) : new Color(0.97f, 0.95f, 0.90f));
            m_Dirty = true;
        }

        // ---------------------------------------------------------------- Lesezeichen
        public string CurrentMark => Doc == null ? null : Doc.ToPosition(m_Left).ToString();
        public bool IsMarked => Doc != null && m_State.marks.Contains(CurrentMark);
        public List<string> Marks => m_State?.marks ?? new List<string>();

        public void ToggleMark()
        {
            if (Doc == null) return;
            var m = CurrentMark;
            if (!m_State.marks.Remove(m)) m_State.marks.Add(m);
            Storage.SaveBooks();
            Changed?.Invoke();
        }

        public void RemoveMark(string m) { if (m_State != null && m_State.marks.Remove(m)) { Storage.SaveBooks(); Changed?.Invoke(); } }

        public string DescribePosition(BookPosition p)
        {
            var r = Doc.FromPosition(p);
            return Doc.IsText ? Doc.Text.Book.Chapters[r.chapter].Title + "  ·  " + Mathf.RoundToInt(Doc.Progress(r) * 100) + " %" : Loc.T("Seite") + " " + (r.page + 1);
        }

        public int CurrentChapter => Doc == null ? 0 : (Doc.IsText ? m_Left.chapter : m_Left.page);
        public string Location => Doc == null ? "" : Doc.LocationText(m_Left);
        public float ProgressValue => Doc == null ? 0f : Doc.Progress(m_Left);

        void Save()
        {
            if (Doc == null || m_State == null) return;
            m_State.position = Doc.ToPosition(m_Left).ToString();
            Storage.Settings.lastBook = Doc.FilePath;
            Storage.SaveBooks();
            Storage.SaveSettings();
        }

        // ---------------------------------------------------------------- Frame
        public void Update()
        {
            if (Doc == null) return;
            m_Cache.Update();

            if (m_Dir != 0)
            {
                float speed = 1f / TurnSeconds;
                if (m_Dragging) m_T = Mathf.Lerp(m_T, m_TargetT, 1 - Mathf.Exp(-18f * Time.deltaTime));
                else
                {
                    // weich ein- und auslaufen
                    float d = m_TargetT - m_T;
                    m_T = Mathf.MoveTowards(m_T, m_TargetT, Time.deltaTime * speed * Mathf.Lerp(0.6f, 1.6f, Mathf.Clamp01(Mathf.Abs(d) * 2)));
                    if (Mathf.Approximately(m_T, m_TargetT)) { FinishTurn(); return; }
                }
                var st = ZoomST;
                for (int i = 0; i < 4; i++) m_View.SetPageST(i, st);
                Texture bl = Tex(m_BL, true), br = Tex(m_BR, true), lf = Tex(m_LF, true), lb = Tex(m_LB, true);
                bool fx = !Doc.IsText;
                m_View.FitContent(0, bl, fx); m_View.FitContent(1, br, fx); m_View.FitContent(2, lf, fx); m_View.FitContent(3, lb, fx);
                m_View.SetTurn(bl, br, lf, lb, Ease(m_T), m_Dir < 0);
                return;
            }

            if (m_DetailWanted && Time.time >= m_DetailAt) { m_DetailWanted = false; m_Dirty = true; }
            if (m_Dirty)
            {
                m_Dirty = false;
                var right = Right;
                var st = ZoomST;
                Texture lt = Tex(m_Left, true), rt = Tex(right, true);
                Vector4 lst = st, rst = st;
                if (Zoom > 1f && !m_DetailWanted && m_Cache.SupportsDetail)
                {
                    var ld = m_Cache.GetDetail(m_Left, Zoom, Pan);
                    if (ld != null) { lt = ld; lst = new Vector4(1, 1, 0, 0); }
                    if (right.HasValue)
                    {
                        var rd = m_Cache.GetDetail(right.Value, Zoom, Pan);
                        if (rd != null) { rt = rd; rst = new Vector4(1, 1, 0, 0); }
                    }
                }
                m_View.SetPageST(0, lst); m_View.SetPageST(1, rst);
                // Seitenverhaeltnis immer von der Grundtextur (Detailausschnitt hat dasselbe Verhaeltnis)
                m_View.FitContent(0, Tex(m_Left, true), !Doc.IsText);
                m_View.FitContent(1, Tex(right, true), !Doc.IsText);
                m_View.SetStatic(lt, rt);
                m_View.SetProgress(Doc.Progress(m_Left));
                Prefetch();
                // sichtbare Seiten nach dem Vorladen erneut beruehren (LRU)
                Tex(m_Left, false); Tex(right, false);
            }
        }

        static float Ease(float t) => t * t * (3 - 2 * t) * 0.35f + t * 0.65f;

        Texture Tex(PageRef? r, bool urgent) => r.HasValue ? m_Cache.Get(r.Value, urgent && Doc.IsText) : null;

        void Prefetch()
        {
            var r = Right;
            for (int i = 0; i < 4 && r.HasValue; i++) { r = Next(r); if (r.HasValue) m_Cache.Get(r.Value, false); }
            var l = m_Left as PageRef?;
            for (int i = 0; i < 2 && l.HasValue; i++) { l = Prev(l); if (l.HasValue) m_Cache.Get(l.Value, false); }
        }

        public string LastError => m_Cache?.LastError;

        public void Dispose() { Close(); if (m_Cover) UnityEngine.Object.Destroy(m_Cover); if (m_Spine) UnityEngine.Object.Destroy(m_Spine); }
    }
}

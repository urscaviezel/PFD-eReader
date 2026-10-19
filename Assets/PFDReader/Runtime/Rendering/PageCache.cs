using System;
using PFDReader.Core;
using System.Collections.Generic;
using System.Threading;
using PFDReader.Books;
using PFDReader.Reading;
using UnityEngine;

namespace PFDReader.Rendering
{
    /// <summary>
    /// Haelt Seitentexturen eines Dokuments (LRU). Textseiten werden auf dem Hauptthread in RenderTextures gerastert,
    /// PDF-/Comicseiten auf einem eigenen Worker-Thread dekodiert und hoechstens eine pro Frame hochgeladen.
    /// </summary>
    public sealed class PageCache : IDisposable
    {
        const int Capacity = 12;
        public const int FixedMaxW = 1600, FixedMaxH = 2134;

        readonly ReaderDocument m_Doc;
        readonly Dictionary<PageRef, Texture> m_Ready = new Dictionary<PageRef, Texture>();
        readonly LinkedList<PageRef> m_Lru = new LinkedList<PageRef>();
        readonly HashSet<PageRef> m_Pending = new HashSet<PageRef>();
        readonly Queue<PageRef> m_TextQueue = new Queue<PageRef>();
        readonly Stack<RenderTexture> m_FreeTargets = new Stack<RenderTexture>();

        // Worker
        readonly Thread m_Thread;
        readonly object m_Lock = new object();
        struct Job { public PageRef r; public bool detail; public float zoom; public Vector2 pan; }
        readonly List<Job> m_Jobs = new List<Job>();
        readonly Queue<(Job, PageImage, string)> m_Done = new Queue<(Job, PageImage, string)>();
        // Detailansicht (PDF-Zoom): hochaufgeloester Ausschnitt fuer sichtbare Seiten
        readonly Dictionary<PageRef, (Texture tex, float zoom, Vector2 pan)> m_Detail = new Dictionary<PageRef, (Texture, float, Vector2)>();
        readonly HashSet<PageRef> m_DetailPending = new HashSet<PageRef>();
        volatile bool m_Stop;
        int m_Generation;

        public event Action<PageRef> PageReady;
        public string LastError { get; private set; }

        public PageCache(ReaderDocument doc)
        {
            m_Doc = doc;
            if (!doc.IsText)
            {
                m_Thread = new Thread(WorkerLoop) { IsBackground = true, Name = "PFD Pages" };
                m_Thread.Start();
            }
        }

        /// <summary>Liefert die Textur oder null (dann wird sie angefordert). urgent: Textseite sofort rastern.</summary>
        public Texture Get(PageRef r, bool urgent)
        {
            if (!m_Doc.IsValid(r)) return null;
            if (m_Ready.TryGetValue(r, out var t)) { Touch(r); return t; }
            if (m_Doc.IsText)
            {
                if (urgent) { RenderText(r); return m_Ready[r]; }
                if (m_Pending.Add(r)) m_TextQueue.Enqueue(r);
                return null;
            }
            if (m_Pending.Add(r))
                lock (m_Lock) { var j = new Job { r = r, zoom = 1 }; if (urgent) m_Jobs.Insert(0, j); else m_Jobs.Add(j); Monitor.Pulse(m_Lock); }
            return null;
        }

        public bool SupportsDetail => m_Doc.Kind == BookKind.Pdf;

        /// <summary>PDF: scharfer Ausschnitt fuer zoom/pan (pan.y von oben). Liefert passende Textur oder null (wird angefordert).</summary>
        public Texture GetDetail(PageRef r, float zoom, Vector2 pan)
        {
            if (!SupportsDetail || zoom <= 1.01f || !m_Doc.IsValid(r)) return null;
            if (m_Detail.TryGetValue(r, out var d) && Mathf.Abs(d.zoom - zoom) < 0.001f && (d.pan - pan).sqrMagnitude < 1e-6f) return d.tex;
            if (m_DetailPending.Add(r))
                lock (m_Lock) { m_Jobs.Insert(0, new Job { r = r, detail = true, zoom = zoom, pan = pan }); Monitor.Pulse(m_Lock); }
            return null;
        }

        /// <summary>Verwirft Detailtexturen, die nicht mehr zu zoom/pan passen (Ausstehende laufen weiter).</summary>
        public void ClearDetail()
        {
            foreach (var kv in m_Detail) UnityEngine.Object.Destroy(kv.Value.tex);
            m_Detail.Clear();
        }

        public bool Has(PageRef r) => m_Ready.ContainsKey(r);

        /// <summary>Verwirft alle Seiten (z. B. nach Schriftgroessen-/Themenwechsel).</summary>
        public void Invalidate()
        {
            m_Generation++;
            lock (m_Lock) { m_Jobs.Clear(); m_Done.Clear(); }
            m_Pending.Clear(); m_TextQueue.Clear(); m_DetailPending.Clear(); ClearDetail();
            foreach (var kv in m_Ready) Release(kv.Value);
            m_Ready.Clear(); m_Lru.Clear();
        }

        /// <summary>Einmal pro Frame aufrufen: fertige Seiten hochladen, eine Textseite vorbereiten.</summary>
        public void Update()
        {
            (Job, PageImage, string) done = default; bool has = false;
            lock (m_Lock) if (m_Done.Count > 0) { done = m_Done.Dequeue(); has = true; }
            if (has)
            {
                var job = done.Item1;
                if (job.detail) m_DetailPending.Remove(job.r); else m_Pending.Remove(job.r);
                if (done.Item3 != null) { LastError = Loc.T("Seite") + " " + (job.r.page + 1) + ": " + Loc.Msg(done.Item3); Debug.LogWarning("[PFD] " + LastError); }
                else if (done.Item2 != null)
                {
                    Texture tex = null;
                    try { tex = Upload(done.Item2); }
                    catch (Exception e) { LastError = Loc.T("Seite") + " " + (job.r.page + 1) + ": " + Loc.Msg(e.Message); Debug.LogWarning("[PFD] " + LastError); }
                    if (tex != null)
                    {
                        if (job.detail)
                        {
                            if (m_Detail.TryGetValue(job.r, out var old)) UnityEngine.Object.Destroy(old.tex);
                            m_Detail[job.r] = (tex, job.zoom, job.pan);
                        }
                        else Store(job.r, tex);
                        PageReady?.Invoke(job.r);
                    }
                }
                return; // hoechstens ein Upload je Frame
            }
            while (m_TextQueue.Count > 0)
            {
                var r = m_TextQueue.Dequeue();
                m_Pending.Remove(r);
                if (m_Ready.ContainsKey(r) || !m_Doc.IsValid(r)) continue;
                RenderText(r);
                PageReady?.Invoke(r);
                break;
            }
        }

        // ---------------------------------------------------------------- Text
        void RenderText(PageRef r)
        {
            var range = m_Doc.Text.PagesOf(r.chapter)[r.page];
            var rt = m_FreeTargets.Count > 0 ? m_FreeTargets.Pop() : m_Doc.Text.CreateTarget();
            m_Doc.Text.Render(range, rt, m_Doc.Label(r));
            m_Pending.Remove(r);
            Store(r, rt);
        }

        // ---------------------------------------------------------------- Fest
        void WorkerLoop()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidJNI.AttachCurrentThread();
#endif
            try
            {
                while (!m_Stop)
                {
                    Job job; int gen;
                    lock (m_Lock)
                    {
                        while (m_Jobs.Count == 0 && !m_Stop) Monitor.Wait(m_Lock);
                        if (m_Stop) break;
                        job = m_Jobs[0]; m_Jobs.RemoveAt(0); gen = m_Generation;
                    }
                    PageImage img = null; string err = null;
                    try { img = m_Doc.Fixed.Render(job.r.page, FixedMaxW, FixedMaxH, job.detail ? job.zoom : 1f, job.detail ? job.pan : new Vector2(0.5f, 0.5f)); }
                    catch (Exception e) { err = e.Message; }
                    lock (m_Lock) if (gen == m_Generation) m_Done.Enqueue((job, img, err));
                }
            }
            finally
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                AndroidJNI.DetachCurrentThread();
#endif
            }
        }

        static Texture Upload(PageImage img)
        {
            Texture2D tex;
            if (img.encoded != null)
            {
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                if (!tex.LoadImage(img.encoded, false)) { UnityEngine.Object.Destroy(tex); return null; }
            }
            else
            {
                tex = new Texture2D(img.width, img.height, TextureFormat.RGBA32, true);
                tex.SetPixelData(img.rgba, 0); // nur Mip 0; Apply(true) erzeugt die restlichen Stufen
            }
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 8;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.Apply(true, true);
            return tex;
        }

        // ---------------------------------------------------------------- LRU
        void Store(PageRef r, Texture t)
        {
            m_Ready[r] = t;
            Touch(r);
            while (m_Lru.Count > Capacity)
            {
                var old = m_Lru.Last.Value; m_Lru.RemoveLast();
                if (m_Ready.TryGetValue(old, out var ot)) { m_Ready.Remove(old); Release(ot); }
            }
        }

        void Touch(PageRef r)
        {
            m_Lru.Remove(r);
            m_Lru.AddFirst(r);
        }

        void Release(Texture t)
        {
            if (t is RenderTexture rt && m_FreeTargets.Count < 4) m_FreeTargets.Push(rt);
            else if (t != null) UnityEngine.Object.Destroy(t);
        }

        public void Dispose()
        {
            m_Stop = true;
            lock (m_Lock) Monitor.PulseAll(m_Lock);
            m_Thread?.Join(2000);
            foreach (var kv in m_Ready) UnityEngine.Object.Destroy(kv.Value);
            ClearDetail();
            while (m_FreeTargets.Count > 0) UnityEngine.Object.Destroy(m_FreeTargets.Pop());
            m_Ready.Clear();
        }
    }
}

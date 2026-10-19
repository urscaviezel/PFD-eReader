using PFDReader.Core;
using PFDReader.UI;
using PFDReader.View;
using UnityEngine;
using UnityEngine.UI;
#if PFD_VUPLEX
using Vuplex.WebView;
#endif

#if PFD_VUPLEX
namespace PFDReader.Media
{
    /// <summary>
    /// Fernseher im Raum: Weltraum-Canvas auf dem Bildschirm. Aus = schwarzer Schirm mit Einschaltknopf,
    /// an = Vuplex-WebView mit YouTube fuer Fernseher (Anmeldung per Code auf youtube.com/activate).
    /// Bedienung: Pointer + Trigger; beim Zeigen: Stick = Pfeiltasten, Stick-Klick = OK, B/Y = Zurueck.
    /// Rechts daneben ein Bedienfeld (Lautstaerke senkrecht, Ausschalten), das nur beim Hinzeigen erscheint.
    /// Ton: per Web-Audio raeumlich (Stereo-Panning, CPU-schonend) am Fernseher positioniert; Lautstaerke inkl. Master.
    /// </summary>
    public sealed class TvController : MonoBehaviour
    {
        public const string TvUrl = "https://www.youtube.com/tv";
        // Geraetekennung fuer YouTube TV: nur mit der PS4-Kennung bietet YouTube alle Aufloesungen (bis 2160p60) an
        const string TvAgent = "Mozilla/5.0 (PS4; Leanback Shell) Gecko/20100101 Firefox/65.0 LeanbackShell/01.00.01.75 Sony PS4/ (PS4, , no, CH)";
        public static bool TvMode => true;
        public Camera EventCamera;
        public Transform Head;

        Canvas m_Screen, m_Panel;
        CanvasGroup m_Group;
        Image m_Off;
        CanvasWebViewPrefab m_Web;
        TMPro.TextMeshProUGUI m_VolLabel;
        float m_NextAudio, m_NextKey, m_HoverUntil;
        bool m_Ready;

        public bool On => m_Web != null;

        /// <summary>Gehoert das Objekt zum Fernseher (Bild oder Bedienfeld)?</summary>
        public bool Owns(GameObject go)
        {
            if (go == null) return false;
            var t = go.transform;
            return (m_Screen && t.IsChildOf(m_Screen.transform)) || (m_Panel && t.IsChildOf(m_Panel.transform));
        }

        const string PageScript = @"(function(){
 if(window.__pfdInit) return; window.__pfdInit=1;
 // Wiedergabefaehigkeit grosszuegig melden, damit YouTube hoehere Aufloesungen anbietet
 try{ if(window.MediaSource){ var o=MediaSource.isTypeSupported.bind(MediaSource); MediaSource.isTypeSupported=function(t){ if(/vp09|vp9|avc1|av01|mp4a|opus/.test(t)) return o(t.replace(/;\s*(width|height|framerate|bitrate)=[^;]*/g,'')); return o(t); }; } }catch(e){}
 try{ if(navigator.mediaCapabilities){ var dc=navigator.mediaCapabilities.decodingInfo.bind(navigator.mediaCapabilities); navigator.mediaCapabilities.decodingInfo=function(c){ return dc(c).then(function(r){ if(r.supported) return {supported:true,smooth:true,powerEfficient:true}; return r; }); }; } }catch(e){}
 try{ var d=function(o,k,v){ Object.defineProperty(o,k,{get:function(){return v;},configurable:true}); };
  d(screen,'width',3840); d(screen,'height',2160); d(screen,'availWidth',3840); d(screen,'availHeight',2160); }catch(e){}
 try{ if(screen.orientation){ screen.orientation.lock=function(){return Promise.resolve();}; } }catch(e){}
 var ctx=null, nodes=[], vol=0.7, pos=[0,0,-3], lp=20000;
 function apply(){
  nodes.forEach(function(n){ n.g.gain.value=vol; if(n.f) n.f.frequency.setTargetAtTime(lp, ctx.currentTime, 0.08); var P=n.p;
   if(P.positionX){P.positionX.value=pos[0];P.positionY.value=pos[1];P.positionZ.value=pos[2];} else P.setPosition(pos[0],pos[1],pos[2]); });
  if(!nodes.length) document.querySelectorAll('video,audio').forEach(function(v){ v.volume=Math.max(0,Math.min(1,vol)); });
 }
 function setup(v){
  if(v.__pfd) return; v.__pfd=1;
  try{
   if(!ctx) ctx=new (window.AudioContext||window.webkitAudioContext)();
   var s=ctx.createMediaElementSource(v), p=ctx.createPanner(), g=ctx.createGain(), f=ctx.createBiquadFilter();
   f.type='lowpass'; f.frequency.value=lp; f.Q.value=0.7;
   p.panningModel='equalpower'; p.distanceModel='inverse'; p.refDistance=1.5; p.rolloffFactor=0.5;
   s.connect(p); p.connect(f); f.connect(g); g.connect(ctx.destination); nodes.push({p:p,g:g,f:f}); apply();
  }catch(e){ v.volume=vol; }
 }
 function scan(){ document.querySelectorAll('video,audio').forEach(setup); }
 var t=0; new MutationObserver(function(){ if(t) return; t=setTimeout(function(){ t=0; scan(); },800); }).observe(document.documentElement,{childList:true,subtree:true}); scan();
 function wake(){ if(ctx&&ctx.state!=='running') ctx.resume(); }
 document.addEventListener('pointerdown',wake,true); document.addEventListener('touchstart',wake,true); document.addEventListener('keydown',wake,true);
 window.__pfdAudio=function(x,y,z,v,c){ pos=[x,y,z]; vol=v; if(c) lp=c; apply(); };
})();";

        public bool OwnsPanel(GameObject go) => go != null && m_Panel && go.transform.IsChildOf(m_Panel.transform);

        /// <summary>Raum gewechselt: Fernseher am Anker des Raums aufbauen (oder entfernen).</summary>
        public void Attach(RoomInfo room)
        {
            if (room == null || room.tv == null)
            {
                // Raum verlassen (z. B. Passthrough): Video pausieren und Fernseher nur ausblenden
                if (m_Ready) m_Web.WebView.ExecuteJavaScript("document.querySelectorAll('video').forEach(function(v){v.pause();})", null);
                SetVisible(false);
                return;
            }
            if (m_Screen != null && room.id == m_RoomId)
            {
                Place(room.tv);
                SetVisible(true);
                return;
            }
            if (m_Screen != null)
            {
                // anderer Raum: Fernseher samt laufendem Video mitnehmen (nur verschieben, Groesse anpassen, Bedienfeld neu)
                m_RoomId = room.id;
                Place(room.tv);
                ((RectTransform)m_Screen.transform).sizeDelta = m_Size * 1000f;
                if (m_Web) m_Web.Resolution = Mathf.Min(1f, 1920f / Mathf.Max(1f, m_Size.x * 1000f));
                if (m_Panel) Destroy(m_Panel.gameObject);
                BuildPanel();
                SetVisible(true);
                return;
            }
            m_RoomId = room.id;
            Place(room.tv);
            var size = m_Size * 1000f; // m -> mm
            m_Screen = UiKit.CreateCanvas("TV Screen", null, size, EventCamera);
            m_Screen.transform.SetPositionAndRotation(m_Pos, m_Rot);
            m_Off = UiKit.Panel("Off", m_Screen.transform, new Color(0.01f, 0.01f, 0.015f, 1f));
            m_Off.rectTransform.Stretch();
            var power = UiIcons.IconButton("power", m_Off.transform, 160, TurnOn);
            var prt = (RectTransform)power.transform; prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f); prt.anchoredPosition = Vector2.zero;
            power.GetComponent<Image>().color = new Color(1, 1, 1, 0.06f);

            BuildPanel();
        }

        /// <summary>Bedienfeld rechts neben dem Fernseher – nur sichtbar beim Hinzeigen.</summary>
        void BuildPanel()
        {
            float panelH = Mathf.Min(m_Size.y, 0.6f) * 1000f;
            m_Panel = UiKit.CreateCanvas("TV Panel", null, new Vector2(110, panelH), EventCamera);
            m_Panel.transform.SetPositionAndRotation(PanelPos, m_Rot);
            m_Group = m_Panel.gameObject.AddComponent<CanvasGroup>(); m_Group.alpha = 0f;
            var bg = UiKit.Panel("Bg", m_Panel.transform, UiKit.Bg); bg.rectTransform.Stretch();
            var off = UiIcons.IconButton("power", bg.transform, 70, () => { if (On) TurnOff(); else TurnOn(); });
            var ort = (RectTransform)off.transform; ort.anchorMin = ort.anchorMax = new Vector2(0.5f, 0f); ort.pivot = new Vector2(0.5f, 0f); ort.anchoredPosition = new Vector2(0, 16);
            m_VolLabel = UiKit.Label("Vol", bg.transform, "", 20, UiKit.Text, TMPro.TextAlignmentOptions.Center);
            var vr = m_VolLabel.rectTransform; vr.anchorMin = new Vector2(0, 1); vr.anchorMax = new Vector2(1, 1); vr.pivot = new Vector2(0.5f, 1); vr.anchoredPosition = new Vector2(0, -12); vr.sizeDelta = new Vector2(0, 30);
            // waagrechter Regler, um 90° gedreht = senkrecht (unten leise, oben laut)
            float len = panelH - 42 - 16 - 70 - 36;
            var holder = UiKit.Rect("SliderHolder", bg.transform);
            holder.anchorMin = holder.anchorMax = new Vector2(0.5f, 0f); holder.pivot = new Vector2(0.5f, 0.5f);
            holder.anchoredPosition = new Vector2(0, 16 + 70 + 18 + len * 0.5f); holder.sizeDelta = new Vector2(len, 60);
            holder.localRotation = Quaternion.Euler(0, 0, 90);
            var sl = UiKit.Slider(holder, Storage.Settings.tvVolume, v => { Storage.Settings.tvVolume = v; UpdateVolLabel(); m_NextAudio = 0; }, () => Storage.SaveSettings());
            sl.Stretch();
            UpdateVolLabel();

        }

        string m_RoomId;
        Vector3 m_Pos; Quaternion m_Rot = Quaternion.identity; Vector2 m_Size;

        void Place(Transform anchor)
        {
            m_Pos = anchor.position; m_Rot = anchor.rotation; m_Size = new Vector2(anchor.localScale.x, anchor.localScale.y);
            if (m_Screen) m_Screen.transform.SetPositionAndRotation(m_Pos, m_Rot);
            if (m_Panel) m_Panel.transform.SetPositionAndRotation(PanelPos, m_Rot);
        }

        Vector3 PanelPos
        {
            get
            {
                // rechts neben dem Bild; steckt dort etwas (Pfeiler, Wand), schrittweise nach vorne zum Betrachter ruecken
                var p0 = m_Pos + m_Rot * Vector3.right * (m_Size.x * 0.5f + 0.12f) - m_Rot * Vector3.forward * 0.02f;
                var half = new Vector3(0.06f, Mathf.Min(m_Size.y, 0.6f) * 0.5f, 0.015f);
                Physics.SyncTransforms();
                for (float d = 0f; d <= 0.8f; d += 0.04f)
                {
                    var p = p0 - m_Rot * Vector3.forward * d;
                    if (!Physics.CheckBox(p, half, m_Rot, ~0, QueryTriggerInteraction.Ignore)) return p;
                }
                return p0;
            }
        }

        void SetVisible(bool v)
        {
            if (m_Screen) m_Screen.gameObject.SetActive(v);
            if (m_Panel) m_Panel.gameObject.SetActive(v);
            m_Visible = v;
        }
        bool m_Visible = true;

        /// <summary>Zeigt der Strahl auf Fernseher oder Bedienfeld? Dann Bedienfeld einblenden (kurze Nachlaufzeit).</summary>
        public void SetHover(bool hover)
        {
            if (m_Group == null) return;
            if (hover) m_HoverUntil = Time.unscaledTime + 0.4f;
            bool show = Time.unscaledTime < m_HoverUntil;
            m_Group.alpha = Mathf.MoveTowards(m_Group.alpha, show ? 1f : 0f, Time.unscaledDeltaTime * 8f);
        }

        public void Detach()
        {
            TurnOff();
            if (m_Screen) Destroy(m_Screen.gameObject);
            if (m_Panel) Destroy(m_Panel.gameObject);
            m_Screen = null; m_Panel = null; m_Group = null; m_RoomId = null; m_Visible = true;
        }

        public async void TurnOn()
        {
            if (m_Screen == null || m_Web != null) return;
            try { Web.SetUserAgent(TvAgent); } catch { }
#if UNITY_ANDROID && !UNITY_EDITOR
            // sonst bleibt das erste Video schwarz, bis man einmal auf das Bild klickt
            try { AndroidWebView.SetForceDrawEnabled(true); } catch { }
#endif
            m_Web = CanvasWebViewPrefab.Instantiate();
            m_Web.InitialUrl = TvUrl;
            m_Web.Resolution = Mathf.Min(1f, 1920f / Mathf.Max(1f, m_Size.x * 1000f));   // max. 1920 px breit: weniger Kopier-/GPU-Last (Waerme -> Zittern)
            m_Web.Native2DModeEnabled = false;
            m_Web.NativeOnScreenKeyboardEnabled = false;
            // Klick = Maus-Klick genau an der Druckstelle: kein Touch-Wischen (das verschob in YouTube TV die Reihen
            // bzw. sprang zum naechsten Video), Zittern des Strahls waehrend des Drueckens wird ignoriert
            m_Web.DragMode = DragMode.DragWithinPage;
            m_Web.DragThreshold = 100000;
            m_Web.transform.SetParent(m_Screen.transform, false);
            ((RectTransform)m_Web.transform).Stretch();
            m_Off.gameObject.SetActive(false);
            var web = m_Web;
            await web.WaitUntilInitialized();
            if (web != m_Web || web.WebView == null) return;
            web.WebView.PageLoadScripts.Add(PageScript);
            web.WebView.LoadProgressChanged += (s, e) => { if (e.Type == ProgressChangeType.Finished) InjectAudio(); };
            m_Ready = true;
        }

        public void TurnOff()
        {
            m_Ready = false;
            if (m_Web) { m_Web.Destroy(); m_Web = null; }
            if (m_Off) m_Off.gameObject.SetActive(true);
        }

        /// <summary>Stick = Pfeiltasten (mit Wiederholung), Stick-Klick = OK, B/Y = Zurueck. true = Eingabe verbraucht.</summary>
        public bool HandleInput(Vector2 stick, bool stickClick, bool back)
        {
            if (!m_Ready) return false;
            var w = m_Web.WebView;
            if (stickClick) w.SendKey("Enter");
            if (back) w.SendKey("Escape");
            if (stick.magnitude > 0.75f)
            {
                if (Time.unscaledTime >= m_NextKey)
                {
                    string key = Mathf.Abs(stick.x) > Mathf.Abs(stick.y) ? (stick.x > 0 ? "ArrowRight" : "ArrowLeft") : (stick.y > 0 ? "ArrowUp" : "ArrowDown");
                    w.SendKey(key);
                    m_NextKey = Time.unscaledTime + (m_NextKey == 0 ? 0.4f : 0.18f);
                }
            }
            else m_NextKey = 0;
            return true;
        }

        void UpdateVolLabel() { if (m_VolLabel) m_VolLabel.text = Mathf.RoundToInt(Storage.Settings.tvVolume * 100) + "%"; }

        void InjectAudio() { if (m_Ready) m_Web.WebView.ExecuteJavaScript(PageScript, null); m_NextAudio = 0; }

        void Update()
        {
            if (!m_Ready || Head == null || m_Screen == null || Time.unscaledTime < m_NextAudio) return;
            m_NextAudio = Time.unscaledTime + 0.2f;
            // Fernseherposition im Kopfkoordinatensystem (Web Audio: rechtshaendig, -Z = vorne)
            var local = Quaternion.Inverse(Head.rotation) * (m_Pos - Head.position);
            var st = Storage.Settings;
            float vol = st.soundOn && m_Visible ? st.tvVolume * st.masterVolume : 0f;
            float cutoff = 20000f;   // Verdeckung durch Waende vorerst aus (zurueck zum urspruenglichen Klang)
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            m_Web.WebView.ExecuteJavaScript(string.Format(inv, "window.__pfdAudio&&window.__pfdAudio({0:F3},{1:F3},{2:F3},{3:F3},{4:F0})", local.x, local.y, -local.z, vol, cutoff), null);
        }

        float m_Occl;

        static readonly RaycastHit[] s_Hits = new RaycastHit[16];

        /// <summary>
        /// Verdeckung 0..1: Sichtlinien vom Kopf zu 9 Punkten in der Bildschirmebene (Bildschirm + 0.5 m Rand).
        /// Gezaehlt wird nur Raumgeometrie, die mehr als 0.3 m vor dem Ziel liegt (Fernseher/Wand dahinter nicht).
        /// Ein Gelaender oder eine Treppe verdeckt nur einzelne Linien, eine Wand alle.
        /// </summary>
        float Occlusion()
        {
            var head = Head.position;
            var ax = m_Rot * Vector3.right * (m_Size.x * 0.5f + 0.5f);
            var ay = m_Rot * Vector3.up * (m_Size.y * 0.5f);   // unten nicht ueber den Bildschirm hinaus (TV-Moebel)
            int blocked = 0, total = 0;
            for (int ix = -1; ix <= 1; ix++)
                for (int iy = -1; iy <= 1; iy++)
                {
                    var p = m_Pos + ax * ix + ay * iy;
                    var dir = p - head; float len = dir.magnitude;
                    if (len < 0.4f) continue;
                    total++;
                    int n = Physics.RaycastNonAlloc(head, dir / len, s_Hits, len - 0.3f, ~(1 << 2), QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < n; i++)
                        if (s_Hits[i].collider.gameObject.scene != gameObject.scene) { blocked++; break; }
                }
            return total == 0 ? 0f : blocked / (float)total;
        }

        void OnDestroy() => Detach();
    }
}
#endif

#if !PFD_VUPLEX
namespace PFDReader.Media
{
    /// <summary>
    /// Ersatz ohne Vuplex 3D WebView (kommerziell, nicht im oeffentlichen Repository): kein Fernseher.
    /// Mit importiertem Vuplex setzt das Editor-Skript FeatureDefines automatisch PFD_VUPLEX.
    /// </summary>
    public sealed class TvController : UnityEngine.MonoBehaviour
    {
        public const string TvUrl = "https://www.youtube.com/tv";
        public static bool TvMode => true;
        public UnityEngine.Camera EventCamera;
        public UnityEngine.Transform Head;
        public bool On => false;
        public bool Owns(UnityEngine.GameObject go) => false;
        public bool OwnsPanel(UnityEngine.GameObject go) => false;
        public void Attach(PFDReader.View.RoomInfo room) { }
        public void SetHover(bool hover) { }
        public void Detach() { }
        public void TurnOn() { }
        public void TurnOff() { }
        public bool HandleInput(UnityEngine.Vector2 stick, bool stickClick, bool back) => false;
    }
}
#endif

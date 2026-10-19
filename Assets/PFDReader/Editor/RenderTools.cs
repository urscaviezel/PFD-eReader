using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PFDReader.EditorTools
{
    /// <summary>Render-Einstellungen per Bridge umschalten (Quad Views / Eye Tracking) fuer Leistungsvergleiche.</summary>
    public static class RenderTools
    {
        /// <summary>mode: "multiview" oder "quadviews"; outer/inner = Renderskalen der beiden Durchgaenge.</summary>
        public static string SetStereoMode(string mode, string outer, string inner)
        {
            var guids = AssetDatabase.FindAssets("t:YVRXRSettings");
            if (guids.Length == 0) return "keine YVRXRSettings";
            var s = AssetDatabase.LoadAssetAtPath<YVR.Core.XR.YVRXRSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
            s.stereoRenderingMode = mode == "quadviews" ? YVR.Core.StereoRenderingMode.QuadViews : YVR.Core.StereoRenderingMode.Multiview;
            s.outerPassRenderScale = float.Parse(outer, System.Globalization.CultureInfo.InvariantCulture);
            s.innerPassRenderScale = float.Parse(inner, System.Globalization.CultureInfo.InvariantCulture);
            EditorUtility.SetDirty(s); AssetDatabase.SaveAssets();
            return "stereo=" + s.stereoRenderingMode + " outer=" + s.outerPassRenderScale + " inner=" + s.innerPassRenderScale;
        }

        /// <summary>support: 0 None, 1 Supported, 2 Required – setzt YVRManager in Main.unity und die Manifest-Eintraege des SDK.</summary>
        public static string SetEyeTracking(string support)
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);
            var mgr = Object.FindAnyObjectByType<YVR.Core.YVRManager>(FindObjectsInactive.Include);
            if (mgr == null) return "kein YVRManager";
            var ed = Editor.CreateEditor(mgr);
            var so = ed.serializedObject;
            so.Update();
            so.FindProperty("eyeTrackingSupport").intValue = int.Parse(support);
            so.ApplyModifiedProperties();
            var m = ed.GetType().GetMethod("SetEyeTrackingInfo", BindingFlags.NonPublic | BindingFlags.Instance);
            string res = "eyeTrackingSupport=" + mgr.eyeTrackingSupport;
            if (m != null)
            {
                var args = new object[] { null };
                m.Invoke(ed, args);
                if (args[0] is Object a) { EditorUtility.SetDirty(a); res += " manifest ok"; }
                AssetDatabase.SaveAssets();
            }
            else res += " (SetEyeTrackingInfo nicht gefunden)";
            Object.DestroyImmediate(ed);
            EditorUtility.SetDirty(mgr);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return res;
        }

        /// <summary>policy: 0 HIGH_QUALITY, 1 BALANCED, 2 BATTERY_SAVING (YVRQualityManager.powerSetting im YVRManager von Main.unity)</summary>
        public static string SetPowerSetting(string policy)
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);
            var mgr = Object.FindAnyObjectByType<YVR.Core.YVRManager>(FindObjectsInactive.Include);
            if (mgr == null) return "kein YVRManager";
            var so = new SerializedObject(mgr);
            var p = so.FindProperty("qualityManager._powerSetting");
            if (p == null) return "Feld nicht gefunden";
            p.enumValueIndex = int.Parse(policy);
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(mgr);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return "powerSetting=" + p.enumNames[p.enumValueIndex];
        }
    }
}

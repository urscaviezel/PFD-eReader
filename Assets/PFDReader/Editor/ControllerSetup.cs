using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PFDReader.EditorTools
{
    /// <summary>Kopiert die originalen PFD-MR-Controller (YVR SDK, Modell "D3") nach Resources, damit die App sie laden kann.</summary>
    public static class ControllerSetup
    {
        const string Src = "Packages/com.yvr.core/Prefabs/";  // Prefabs/YVR_*HandControllerD3 = reines Controller-Modell
        const string Dst = "Assets/PFDReader/Resources/Controllers/";

        /// <summary>Raum-Texturen: trilinear + leichter positiver Mip-Bias und Box-Mipfilter -> weniger Flimmern feiner Linien auf Distanz.</summary>
        public static string SmoothRoomTextures(string bias)
        {
            float b = float.Parse(bias, System.Globalization.CultureInfo.InvariantCulture); int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/PFDReader/Rooms" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("Lightmap") || path.Contains("RoomPreviews")) continue;
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti) || ti.textureType == TextureImporterType.Lightmap) continue;
                if (ti.mipmapEnabled && ti.filterMode == FilterMode.Trilinear && Mathf.Approximately(ti.mipMapBias, b) && ti.mipmapFilter == TextureImporterMipFilter.BoxFilter) continue;
                ti.mipmapEnabled = true; ti.filterMode = FilterMode.Trilinear; ti.mipMapBias = b; ti.mipmapFilter = TextureImporterMipFilter.BoxFilter;
                ti.SaveAndReimport(); n++;
            }
            return n + " Texturen angepasst";
        }

        public static string Copy()
        {
            Directory.CreateDirectory(Dst);
            string r = "";
            foreach (var side in new[] { "Left", "Right" })
            {
                var s = Src + "YVR_" + side + "HandControllerD3.prefab";
                var d = Dst + "Controller" + side + ".prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(s) == null) { r += "fehlt: " + s + "\n"; continue; }
                AssetDatabase.DeleteAsset(d);
                if (!AssetDatabase.CopyAsset(s, d)) { r += "Fehler " + d + "\n"; continue; }
                // Das im Prefab verlinkte Material fehlt im SDK -> D3-URP-Material setzen
                var mat = AssetDatabase.LoadAssetAtPath<Material>("Packages/com.yvr.core/Materials/TouchController/D3TouchController_URP.mat");
                var root = PrefabUtility.LoadPrefabContents(d);
                foreach (var mr in root.GetComponentsInChildren<Renderer>(true))
                {
                    var ms = mr.sharedMaterials; for (int i = 0; i < ms.Length; i++) ms[i] = mat; mr.sharedMaterials = ms;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r += mr.name + " ";
                }
                PrefabUtility.SaveAsPrefabAsset(root, d); PrefabUtility.UnloadPrefabContents(root);
                r += "ok " + d + " (" + (mat != null ? mat.shader.name : "Material fehlt") + ")\n";
            }
            AssetDatabase.Refresh();
            return r;
        }

        /// <summary>Rendert den rechten Controller so, wie er am Anker sitzt (Anker +Z = Strahlrichtung, nach rechts zeigend).</summary>
        public static string Preview(string side, string rx, string ry, string rz)
        {
            var prefab = Resources.Load<GameObject>("Controllers/Controller" + side);
            if (prefab == null) return "Prefab fehlt";
            var temp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var anchor = new GameObject("Anchor"); SceneManager.MoveGameObjectToScene(anchor, temp);
            var go = Object.Instantiate(prefab, anchor.transform); go.transform.localRotation = Quaternion.Euler(float.Parse(rx), float.Parse(ry), float.Parse(rz));
            var ray = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(ray, temp);
            ray.transform.localScale = new Vector3(0.004f, 0.004f, 0.3f); ray.transform.position = new Vector3(0, 0, 0.15f);
            var lgo = new GameObject("L"); SceneManager.MoveGameObjectToScene(lgo, temp);
            var l = lgo.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1.3f; lgo.transform.rotation = Quaternion.Euler(40, 60, 0);
            var camGo = new GameObject("Cam"); SceneManager.MoveGameObjectToScene(camGo, temp);
            var cam = camGo.AddComponent<Camera>(); cam.scene = temp; cam.fieldOfView = 40; cam.nearClipPlane = 0.01f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.5f, 0.55f, 0.6f);
            int w = 600, h = 600; var rt = new RenderTexture(w, h, 24) { antiAliasing = 4 }; cam.targetTexture = rt;
            var strip = new Texture2D(w * 2, h, TextureFormat.RGB24, false);
            // links: von der Seite (rechts), rechts: von hinten oben (Blick wie der Nutzer)
            var views = new[] { (new Vector3(0.45f, 0.02f, 0.02f), Vector3.up), (new Vector3(0, 0.25f, -0.35f), Vector3.up) };
            for (int i = 0; i < 2; i++)
            {
                camGo.transform.position = views[i].Item1; camGo.transform.LookAt(new Vector3(0, 0, 0.02f), views[i].Item2);
                cam.Render(); RenderTexture.active = rt;
                var tmp = new Texture2D(w, h, TextureFormat.RGB24, false); tmp.ReadPixels(new Rect(0, 0, w, h), 0, 0); tmp.Apply();
                strip.SetPixels(i * w, 0, w, h, tmp.GetPixels()); Object.DestroyImmediate(tmp);
            }
            strip.Apply(); RenderTexture.active = null;
            var outPath = Path.GetFullPath("Builds/controller.png"); Directory.CreateDirectory("Builds");
            File.WriteAllBytes(outPath, strip.EncodeToPNG());
            rt.Release(); Object.DestroyImmediate(rt);
            EditorSceneManager.CloseScene(temp, true);
            return outPath;
        }

        /// <summary>Beide Controller nebeneinander (Blick von oben/hinten auf die Tasten) fuer die Anleitung -> Builds/guide/controllers.png</summary>
        public static string GuideShot(string pitch, string dist)
        {
            var temp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var off = new Vector3(0, 100, 0);
            foreach (var side in new[] { "Left", "Right" })
            {
                var prefab = Resources.Load<GameObject>("Controllers/Controller" + side);
                var anchor = new GameObject("A" + side); SceneManager.MoveGameObjectToScene(anchor, temp);
                anchor.transform.position = off + new Vector3(side == "Left" ? -0.085f : 0.085f, 0, 0);
                var go = Object.Instantiate(prefab, anchor.transform);
                go.transform.localRotation = side == "Left" ? Quaternion.Euler(90, 180, -90) : Quaternion.Euler(90, 0, -90);
            }
            foreach (var (rot, inten) in new[] { (Quaternion.Euler(50, 20, 0), 1.2f), (Quaternion.Euler(20, 200, 0), 0.6f) })
            {
                var lgo = new GameObject("L"); SceneManager.MoveGameObjectToScene(lgo, temp);
                var l = lgo.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = inten; lgo.transform.rotation = rot;
            }
            var camGo = new GameObject("Cam"); SceneManager.MoveGameObjectToScene(camGo, temp);
            var cam = camGo.AddComponent<Camera>(); cam.scene = temp; cam.fieldOfView = 30; cam.nearClipPlane = 0.01f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.86f, 0.88f, 0.92f);
            float p = float.Parse(pitch, System.Globalization.CultureInfo.InvariantCulture), d = float.Parse(dist, System.Globalization.CultureInfo.InvariantCulture);
            var center = off + new Vector3(0, -0.01f, -0.02f);
            camGo.transform.position = center + Quaternion.Euler(p, 0, 0) * new Vector3(0, 0, -d);
            camGo.transform.LookAt(center);
            int w = 1600, h = 1000; var rt = new RenderTexture(w, h, 24) { antiAliasing = 8 }; cam.targetTexture = rt;
            cam.Render(); RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply(); RenderTexture.active = null;
            Directory.CreateDirectory("Builds/guide");
            var outPath = Path.GetFullPath("Builds/guide/controllers.png");
            File.WriteAllBytes(outPath, tex.EncodeToPNG());
            rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            EditorSceneManager.CloseScene(temp, true);
            return outPath;
        }
    }
}

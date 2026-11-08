using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace VintageJournal.Editor
{
    /// <summary>Bright, colour-rich daydream art pass (authoring only). Inventory first, then Apply.</summary>
    public static partial class DaydreamBrightPass
    {
        const string ScenePath = "Assets/Scenes/VintageJournalDesk.unity";
        static string Docs => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "docs", "brightpass");

        [MenuItem("Vintage Journal/Daydream/Bright pass inventory")]
        public static void Inventory()
        {
            EditorSceneManager.OpenScene(ScenePath);
            Directory.CreateDirectory(Docs);
            var lines = new List<string>();
            var cam = Object.FindFirstObjectByType<DeskCamera>().GetComponent<Camera>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).OrderBy(r => PathOf(r.transform)))
            {
                if (!r.enabled) continue;
                var vp = cam.WorldToViewportPoint(r.bounds.center);
                bool inView = vp.z > 0 && vp.x > -.1f && vp.x < 1.1f && vp.y > -.1f && vp.y < 1.1f;
                if (!inView) continue;
                foreach (var m in r.sharedMaterials)
                {
                    if (!m) continue;
                    var c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.HasProperty("_Color") ? m.color : Color.white;
                    var t = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
                    lines.Add(PathOf(r.transform) + " | " + m.name + " | " + m.shader.name.Replace("Universal Render Pipeline/", "") + " | #" + ColorUtility.ToHtmlStringRGBA(c) +
                        " | tex=" + (t ? t.name : "-") + " | vp=" + vp.x.ToString("F2") + "," + vp.y.ToString("F2") + " | size=" + r.bounds.size.ToString("F3"));
                }
            }
            lines.Add("");
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                lines.Add("LIGHT " + PathOf(l.transform) + " " + l.type + " #" + ColorUtility.ToHtmlStringRGB(l.color) + " i=" + l.intensity + " shadows=" + l.shadows + " rot=" + l.transform.eulerAngles);
            foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
                lines.Add("VOLUME " + PathOf(v.transform) + " w=" + v.weight + " global=" + v.isGlobal + " profile=" + (v.sharedProfile ? v.sharedProfile.name + ":" + string.Join(",", v.sharedProfile.components.Select(c => c.GetType().Name + (c.active ? "" : "(off)"))) : "none"));
            lines.Add("AMBIENT " + RenderSettings.ambientMode + " sky=#" + ColorUtility.ToHtmlStringRGB(RenderSettings.ambientSkyColor) + " eq=#" + ColorUtility.ToHtmlStringRGB(RenderSettings.ambientEquatorColor) + " gnd=#" + ColorUtility.ToHtmlStringRGB(RenderSettings.ambientGroundColor));
            lines.Add("CAMERA hdr=" + cam.allowHDR + " post=" + cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>()?.renderPostProcessing);
            File.WriteAllLines(Path.Combine(Docs, "inventory-before.txt"), lines);
            Debug.Log("[BrightPass] Inventory lines=" + lines.Count);
        }
        [MenuItem("Vintage Journal/Daydream/Bright pass preview render")]
        public static void Preview()
        {
            EditorSceneManager.OpenScene(ScenePath); Directory.CreateDirectory(Docs);
            var cam = Object.FindFirstObjectByType<DeskCamera>().GetComponent<Camera>();
            var profile = cam.GetComponent<DeskCamera>().profile;
            cam.transform.position = profile.seatedPosition; cam.transform.LookAt(profile.seatedTarget); cam.fieldOfView = profile.seatedFieldOfView;
            var rt = new RenderTexture(1920, 1080, 24); var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false); var prev = RenderTexture.active;
            try
            {
                cam.aspect = 16f / 9f;
                var request = new RenderPipeline.StandardRequest { destination = rt };
                RenderPipeline.SubmitRenderRequest(cam, request);
                RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); tex.Apply();
                string name = Environment.GetCommandLineArgs().SkipWhile(a => a != "--preview-name").Skip(1).FirstOrDefault() ?? "preview";
                File.WriteAllBytes(Path.Combine(Docs, name + ".png"), tex.EncodeToPNG());
            }
            finally { RenderTexture.active = prev; Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt); }
        }
        static string PathOf(Transform t) => t.parent ? PathOf(t.parent) + "/" + t.name : t.name;
    }
}

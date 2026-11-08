using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VintageJournal.Editor
{
    /// <summary>Read-only scene inventory used to plan the night dreamcore layer. Never saves the scene.</summary>
    public static class NightDreamcoreInspect
    {
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/VintageJournalDesk.unity");
            var sb = new StringBuilder();
            sb.AppendLine($"fog={RenderSettings.fog} mode={RenderSettings.fogMode} density={RenderSettings.fogDensity} start={RenderSettings.fogStartDistance} end={RenderSettings.fogEndDistance} color={RenderSettings.fogColor}");
            var cam = Object.FindFirstObjectByType<DeskCamera>();
            var data = cam.GetComponent<UniversalAdditionalCameraData>();
            sb.AppendLine($"camera post={data.renderPostProcessing} AA={data.antialiasing} hdr={cam.GetComponent<Camera>().allowHDR} far={cam.GetComponent<Camera>().farClipPlane} pos={cam.profile.seatedPosition} target={cam.profile.seatedTarget} fov={cam.profile.seatedFieldOfView}");
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            sb.AppendLine($"urp hdr={urp.supportsHDR} softShadows={urp.supportsSoftShadows} shadowDist={urp.shadowDistance}");
            foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                sb.AppendLine($"volume {Path(v.transform)} global={v.isGlobal} weight={v.weight} prio={v.priority} profile={AssetDatabase.GetAssetPath(v.sharedProfile)} comps=" + string.Join(",", v.sharedProfile.components.Select(c => c.GetType().Name + (c.active ? "" : "(off)"))));
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                sb.AppendLine($"light {Path(l.transform)} {l.type} col={ColorUtility.ToHtmlStringRGB(l.color)} i={l.intensity} range={l.range} shadows={l.shadows} str={l.shadowStrength} en={l.enabled} act={l.gameObject.activeInHierarchy}");
            foreach (var a in Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                sb.AppendLine($"audio {Path(a.transform)} clip={(a.clip ? a.clip.name : "-")} loop={a.loop} vol={a.volume}");
            var time = Object.FindFirstObjectByType<DreamTimeController>();
            sb.AppendLine($"time sky={Path(time.sky.transform)} shader={time.sky.sharedMaterial.shader.name} moon={(time.moon ? Path(time.moon.transform) : "-")} lamp={(time.workLamp ? Path(time.workLamp.transform) : "-")}");
            var sh = time.sky.sharedMaterial.shader;
            for (int i = 0; i < sh.GetPropertyCount(); i++) sb.Append(sh.GetPropertyName(i) + " ");
            sb.AppendLine();
            foreach (var m in Object.FindObjectsByType<DaydreamMotes>(FindObjectsInactive.Include, FindObjectsSortMode.None)) sb.AppendLine("motes " + Path(m.transform) + " " + EditorJsonUtility.ToJson(m));
            foreach (var p in Object.FindObjectsByType<DreamSkyPasser>(FindObjectsInactive.Include, FindObjectsSortMode.None)) sb.AppendLine("passer " + Path(p.transform) + " act=" + p.gameObject.activeInHierarchy + " " + EditorJsonUtility.ToJson(p));
            var keys = new[] { "moon", "airship", "star", "stamp", "tray", "rack", "card", "drawer", "cabinet", "mailbox", "post", "page", "cloud", "breath", "lantern", "platform", "studio" };
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                string n = t.name.ToLowerInvariant(); int depth = Path(t).Count(c => c == '/');
                if (depth <= 2 && keys.Any(n.Contains))
                {
                    var r = t.GetComponentsInChildren<Renderer>(true);
                    sb.AppendLine($"obj {Path(t)} act={t.gameObject.activeInHierarchy} pos={t.position} renderers={r.Length} mats=" + string.Join("|", r.Select(x => x.sharedMaterial ? x.sharedMaterial.name + ":" + x.sharedMaterial.shader.name : "-").Distinct().Take(6)));
                }
            }
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects()) sb.AppendLine("root " + root.name + " act=" + root.activeSelf + " children=" + root.transform.childCount);
            File.WriteAllText("docs/night-dreamcore/inventory.txt", sb.ToString());
        }
        public static void Targets()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/VintageJournalDesk.unity");
            var sb = new StringBuilder(); var names = new[] { "Cotton tray liner", "Card", "Wood rack label", "Rounded postbox", "Replaceable maple cabinet", "drawer-tape-top", "drawer-tape-middle", "drawer-release-books", "Moon over the cloud sea", "Left decorated page", "Right decorated page" };
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!names.Contains(t.name)) continue;
                var rs = t.GetComponentsInChildren<Renderer>(true); var b = rs.Length > 0 ? rs[0].bounds : new Bounds(t.position, Vector3.zero); foreach (var r in rs) b.Encapsulate(r.bounds);
                sb.AppendLine($"{Path(t)} act={t.gameObject.activeInHierarchy} pos={t.position.ToString("F3")} rot={t.eulerAngles.ToString("F1")} scale={t.lossyScale.ToString("F3")} bounds={b.center.ToString("F3")}/{b.size.ToString("F3")} colliders={t.GetComponentsInChildren<Collider>(true).Length} mats=" + string.Join("|", rs.Select(r => r.sharedMaterial ? r.sharedMaterial.name : "-").Distinct().Take(4)));
            }
            File.WriteAllText("docs/night-dreamcore/targets.txt", sb.ToString());
        }
        static string Path(Transform t) => t.parent ? Path(t.parent) + "/" + t.name : t.name;
    }
}

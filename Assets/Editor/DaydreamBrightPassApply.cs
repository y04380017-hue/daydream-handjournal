using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace VintageJournal.Editor
{
    /// <summary>
    /// Bright pass. New materials live in Assets/Daydream/Bright; the scene is re-pointed at them (old material files untouched).
    /// Clouds are deactivated, not deleted. Re-running rebuilds only "Daydream Bright" objects.
    /// Rollback: E:/TapeGestureDemo/backups/handbook-before-brightpass-20261105.
    /// </summary>
    public static partial class DaydreamBrightPass
    {
        const string Root = "Assets/Daydream/Bright/";
        const string ExtrasName = "Daydream Bright";
        static readonly List<string> log = new List<string>();
        static Color C(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out var c); return c; }

        // ---- One palette for the whole room ----
        static readonly Dictionary<string, Color> Palette = new Dictionary<string, Color>
        {
            {"cream", C("FFF9EF")}, {"paper", C("FFFDF7")}, {"maple", C("FFF9F1")},
            {"mint", C("7FD8BF")}, {"mintSoft", C("A9E4CF")}, {"sky", C("86C8EE")}, {"skySoft", C("D3ECFA")},
            {"coral", C("FF9F88")}, {"peachSoft", C("FFD9CC")}, {"lemon", C("FFD566")}, {"lilac", C("B8A5F2")},
            {"rose", C("F6A6C6")}, {"leaf", C("6FC285")}, {"gold", C("E9C87C")}, {"indigo", C("3F4F86")}, {"plum", C("5A4E7A")},
            {"blush", C("F7D3DA")}, {"aqua", C("7FD3D8")},
        };

        static Material Mat(string name, Color color, float smooth = .3f, float metal = 0, Texture tex = null, bool transparent = false, bool unlit = false, string shader = null)
        {
            string path = Root + "Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var sh = Shader.Find(shader ?? ("Universal Render Pipeline/" + (unlit ? "Unlit" : "Lit")));
            if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh; m.SetColor("_BaseColor", color); if (tex) m.SetTexture("_BaseMap", tex);
            if (!unlit && m.HasProperty("_Smoothness")) { m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal); }
            if (transparent)
            {
                m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0); m.SetOverrideTag("RenderType", "Transparent");
                m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha); m.SetInt("_ZWrite", 0);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = (int)RenderQueue.Transparent;
            }
            EditorUtility.SetDirty(m); return m;
        }
        static Material Tint(Material source, string name, Color color, float? smooth = null)
        {
            // Copy keeps transparency/texture setup of the source, only the colour changes.
            string path = Root + "Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(source); AssetDatabase.CreateAsset(m, path); }
            else { m.shader = source.shader; m.CopyPropertiesFromMaterial(source); m.shaderKeywords = source.shaderKeywords; m.renderQueue = source.renderQueue; m.SetOverrideTag("RenderType", source.GetTag("RenderType", false)); }
            m.SetColor("_BaseColor", color); if (smooth.HasValue && m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth.Value);
            EditorUtility.SetDirty(m); return m;
        }
        static Texture2D Gradient(string name, int w, int h, Func<float, float, Color> f, bool repeat = false)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) tex.SetPixel(x, y, f((x + .5f) / w, (y + .5f) / h));
            string path = Root + "Textures/" + name + ".png"; File.WriteAllBytes(path, tex.EncodeToPNG()); Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path); var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp; imp.alphaIsTransparency = true; imp.mipmapEnabled = repeat; imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        [MenuItem("Vintage Journal/Daydream/Apply bright pass")]
        public static void Apply()
        {
            log.Clear();
            var scene = EditorSceneManager.OpenScene(ScenePath);
            // The 2026-11-06 floating day/night pass (FloatingAtelierSetup) owns sky, lights, papers and DaydreamBreath wiring.
            // Re-running the whole bright pass would overwrite it; use the incremental menus (e.g. Apply studio P1) instead.
            if (GameObject.Find("Floating atelier cycle")) throw new InvalidOperationException("Floating day/night is installed; full bright pass refused. Use incremental passes.");
            Directory.CreateDirectory(Root + "Materials"); Directory.CreateDirectory(Root + "Textures"); AssetDatabase.Refresh();
            var old = GameObject.Find(ExtrasName); if (old) Object.DestroyImmediate(old);
            var extras = new GameObject(ExtrasName).transform;

            Recolor(); Sky(); FewerClouds(); Breathing(extras); Keepsakes(extras); Lighting(extras); ApplyPapers();

            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Directory.CreateDirectory(Docs); File.WriteAllLines(Path.Combine(Docs, "apply-log.txt"), log);
            Debug.Log("[BrightPass] Applied, log lines=" + log.Count);
        }

        static void Recolor()
        {
            Texture maple = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Daydream/Textures/Maple.png");
            Material Find(string n) => AssetDatabase.FindAssets("t:Material " + n).Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<Material>).FirstOrDefault(m => m && m.name == n);
            var map = new Dictionary<string, Material>();
            void To(string from, Material to) { map[from] = to; }

            var mint = Mat("Bright mint", Palette["mint"], .42f); var sky = Mat("Bright sky", Palette["sky"], .42f);
            var coral = Mat("Bright coral", Palette["coral"], .42f); var lemon = Mat("Bright lemon", Palette["lemon"], .42f);
            var lilac = Mat("Bright lilac", Palette["lilac"], .42f); var rose = Mat("Bright rose", Palette["rose"], .42f);
            var cream = Mat("Bright cream", Palette["cream"], .35f); var paper = Mat("Bright paper", Palette["paper"], .12f);
            var gold = Mat("Bright pearl gold", Palette["gold"], .62f, .55f);
            var indigo = Mat("Bright indigo", Palette["indigo"], .35f); var plum = Mat("Bright plum ink", Palette["plum"], .3f);
            var leaf = Mat("Bright leaf", Palette["leaf"], .3f);
            var mapleTop = Mat("Bright maple", Palette["maple"], .38f, 0, maple);
            var mat = Mat("Bright mat", Palette["mintSoft"], .15f);

            To("Milk paint", cream); To("Satin brass", gold); To("Apricot", coral); To("Pistachio", mint); To("Porcelain blue", sky);
            To("Wisteria", lilac); To("Pollen", lemon); To("Blue ink", indigo); To("Ink", plum); To("Paper", paper); To("Reuse_Paper", paper);
            To("Honey maple", mapleTop); To("Sage cloth", mat); To("LeafGreen", leaf); To("DaisyIvory", Mat("Bright petal", Color.white, .2f));
            To("DaisyGold", lemon); To("Porcelain", Mat("Bright porcelain", C("FFFFFF"), .7f)); To("WaxSeal", rose);
            To("Postcard string", Mat("Bright string", Palette["rose"], .2f));
            To("Mailbox mint", mint); To("Mailbox peach", coral); To("Mailbox pollen", lemon); To("Mailbox cream", cream);
            To("Floating peach", coral); To("Floating mint", mint); To("Floating sky", sky); To("Floating lilac", lilac); To("Floating paper", paper);
            var seaGlass = Find("Sea glass"); if (seaGlass) To("Sea glass", Tint(seaGlass, "Bright acrylic", new Color(.86f, .98f, 1f, .10f), .8f));
            var edges = Find("AcrylicEdges"); if (edges) To("AcrylicEdges", Tint(edges, "Bright acrylic edge", new Color(.55f, .88f, .92f, .55f)));
            var amber = Find("AmberGlass"); if (amber) To("AmberGlass", Tint(amber, "Bright vase glass", new Color(.86f, .95f, 1f, .28f)));
            var water = Find("VaseWater"); if (water) To("VaseWater", Tint(water, "Bright vase water", new Color(.75f, .92f, .98f, .25f)));
            var glassine = Find("Glassine"); if (glassine) To("Glassine", Tint(glassine, "Bright glassine", C("FBFBF2")));
            var washiEdge = Find("Reuse_WashiEdge_tape_01");

            int swapped = 0;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var mats = r.sharedMaterials; bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (!mats[i]) continue;
                    if (map.TryGetValue(mats[i].name, out var to)) { mats[i] = to; changed = true; swapped++; }
                    else if (mats[i].name.StartsWith("Reuse_WashiEdge_")) { mats[i] = Tint(mats[i], "Bright " + mats[i].name, C("FFF6EC")); changed = true; swapped++; }
                }
                if (changed) r.sharedMaterials = mats;
            }
            log.Add("Material slots re-pointed to bright palette: " + swapped);

            // Per-object accents so families read as a coordinated set rather than one flat tone.
            void Paint(string root, Func<Renderer, bool> pick, Material m)
            {
                var t = GameObject.Find(root); if (!t) { log.Add("Missing " + root); return; }
                int n = 0; foreach (var r in t.GetComponentsInChildren<Renderer>(true)) if (pick(r)) { var ms = r.sharedMaterials; for (int i = 0; i < ms.Length; i++) ms[i] = m; r.sharedMaterials = ms; n++; }
                log.Add("Accent " + root + " -> " + m.name + " (" + n + ")");
            }
            bool Is(Renderer r, Material m) => r.sharedMaterials.Contains(m);
            Paint("WorkspaceStage4B/SmallNoteBox_1", r => Is(r, coral), coral);
            Paint("WorkspaceStage4B/SmallNoteBox_2", r => Is(r, coral), mint);
            Paint("WorkspaceStage4B/SmallNoteBox_3", r => Is(r, coral), sky);
            var drawers = Object.FindObjectsByType<WorkspaceDrawer>(FindObjectsSortMode.None).OrderBy(d => d.drawerId).ToArray();
            var handleColors = new[] { lemon, coral, lilac };
            for (int i = 0; i < drawers.Length; i++)
                foreach (var r in drawers[i].GetComponentsInChildren<Renderer>(true).Where(r => r.name.Contains("handle") && Is(r, mint))) r.sharedMaterial = handleColors[i % 3];
            log.Add("Drawer handles: lemon / coral / lilac");
            var desk = GameObject.Find("Desk");
            if (desk) foreach (var r in desk.GetComponentsInChildren<Renderer>(true).Where(r => r.name.ToLowerInvariant().Contains("leg"))) r.sharedMaterial = Mat("Bright desk leg", Palette["mintSoft"], .45f);
            log.Add("Desk legs: soft mint lacquer");
            var lamp = GameObject.Find("GreenBankersLamp");
            if (lamp) foreach (var r in lamp.GetComponentsInChildren<Renderer>(true).Where(r => r.name.ToLowerInvariant().Contains("shade") && !r.name.ToLowerInvariant().Contains("rim"))) r.sharedMaterial = Mat("Bright lamp shade", Palette["lemon"], .55f);
            log.Add("Lamp shade: lemon");
        }

        static void Sky()
        {
            Color top = C("5FB4EE"), mid = C("A9DAF8"), horizon = C("FFF1E6"), blush = C("BFE1F8"), deep = C("B9C6F2");
            var tex = Gradient("SkyBright", 4, 512, (x, v) =>
            {
                if (v > .66f) return Color.Lerp(mid, top, Mathf.SmoothStep(0, 1, (v - .66f) / .34f));
                if (v > .50f) return Color.Lerp(horizon, mid, Mathf.SmoothStep(0, 1, (v - .50f) / .16f));
                if (v > .44f) return Color.Lerp(blush, horizon, Mathf.SmoothStep(0, 1, (v - .44f) / .06f));
                return Color.Lerp(deep, blush, Mathf.SmoothStep(0, 1, v / .44f));
            });
            var dome = GameObject.Find("Sky dome");
            if (dome) { dome.GetComponent<Renderer>().sharedMaterial = Mat("Bright sky dome", Color.white, 0, 0, tex, false, true); log.Add("Sky dome: bright gradient (sky / blush / lilac)"); }
            RenderSettings.fogColor = C("EAF4FB"); RenderSettings.fogStartDistance = 9; RenderSettings.fogEndDistance = 40;
            var cam = Object.FindFirstObjectByType<DeskCamera>()?.GetComponent<Camera>(); if (cam) { cam.backgroundColor = C("CDE9FA"); EditorUtility.SetDirty(cam); }
        }

        static void FewerClouds()
        {
            var clouds = GameObject.Find("Floating Sky Atelier/Clouds");
            if (!clouds) { log.Add("Clouds root missing"); return; }
            // Keep four distant, softer clouds; the rest are switched off (kept in the scene for rollback).
            var keep = new HashSet<string> { "Sky cloud 2", "Sky cloud 4", "Sky cloud 6" };
            var soft = Mat("Bright cloud", new Color(1, 1, 1, 1), .02f);
            int off = 0;
            foreach (Transform c in clouds.transform)
            {
                bool k = keep.Contains(c.name); c.gameObject.SetActive(k); if (!k) off++;
                if (k) { foreach (var r in c.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = soft; c.localScale = Vector3.one * .8f; var f = c.GetComponent<DaydreamFloat>(); if (f) { f.driftMetresPerSecond = .03f; f.bobPeriod = 9; } }
            }
            log.Add("Clouds: kept " + keep.Count + " distant, deactivated " + off + " (incl. desk cushions)");
            // Small helper clouds on the mailbox and postcard line become tiny puffs.
            foreach (var n in new[] { "CommissionDesk/Floating mailbox/Mailbox cloud", "CommissionDesk/Postcard line/Line cloud L", "CommissionDesk/Postcard line/Line cloud R" })
            { var t = GameObject.Find(n); if (t) { t.SetActive(false); log.Add("Deactivated " + n); } }
        }

        static void Breathing(Transform extras)
        {
            // Soft glow far below the desk: it reads as height and breathes with the room.
            var haloTex = Gradient("DeskHalo", 128, 128, (x, y) =>
            { float d = Vector2.Distance(new Vector2(x, y), new Vector2(.5f, .5f)) * 2; float a = Mathf.Clamp01(1 - d); a = a * a * (3 - 2 * a); return new Color(1, 1, 1, a); });
            var haloMat = Mat("Bright desk halo", new Color(1f, .84f, .9f, .55f), 0, 0, haloTex, true, true);
            var halo = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.DestroyImmediate(halo.GetComponent<Collider>());
            halo.name = "Desk halo"; halo.transform.SetParent(extras, false); halo.transform.position = new Vector3(.02f, -.45f, .05f);
            halo.transform.rotation = Quaternion.Euler(90, 0, 0); halo.transform.localScale = new Vector3(2.4f, 1.7f, 1);
            var hr = halo.GetComponent<Renderer>(); hr.sharedMaterial = haloMat; hr.shadowCastingMode = ShadowCastingMode.Off; hr.receiveShadows = false;

            // Drifting light motes: few, soft, rising slowly; behind and around the desk only.
            var moteTex = Gradient("Mote", 64, 64, (x, y) => { float d = Vector2.Distance(new Vector2(x, y), new Vector2(.5f, .5f)) * 2; float a = Mathf.Clamp01(1 - d); return new Color(1, 1, 1, a * a); });
            var moteMat = Mat("Bright mote", Color.white, 0, 0, moteTex, true, true);
            var motes = new GameObject("Light motes"); motes.transform.SetParent(extras, false); motes.transform.position = new Vector3(0, .62f, 1.15f);
            var field = motes.AddComponent<DaydreamMotes>(); field.material = moteMat; field.count = 42; field.size = new Vector3(2.6f, 1.1f, 1.2f);
            log.Add("Breathing: desk halo (blush, y=-.45) and 42 drifting light motes behind the desk (z~1.15, quads, no colliders)");
        }

        static void Keepsakes(Transform extras)
        {
            // With fewer clouds, a few colourful floating washi rolls and paper stars carry the weightless feel.
            var colors = new[] { "mint", "coral", "lemon", "lilac", "sky", "rose" };
            var spots = new[] { new Vector3(-1.05f, 1.18f, 1.35f), new Vector3(1.12f, 1.36f, 1.5f), new Vector3(-.35f, 1.55f, 1.9f), new Vector3(.55f, 1.62f, 2.1f), new Vector3(-1.45f, .82f, 1.9f), new Vector3(1.5f, .95f, 1.85f) };
            var paper = Mat("Bright paper", Palette["paper"], .12f);
            for (int i = 0; i < spots.Length; i++)
            {
                var roll = new GameObject("Floating washi " + (i + 1)).transform; roll.SetParent(extras, false); roll.position = spots[i];
                roll.rotation = Quaternion.Euler(70 + i * 13, i * 37, i * 21);
                var outer = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Object.DestroyImmediate(outer.GetComponent<Collider>());
                outer.transform.SetParent(roll, false); outer.transform.localScale = new Vector3(.05f, .008f, .05f); outer.GetComponent<Renderer>().sharedMaterial = Mat("Bright " + colors[i], Palette[colors[i]], .42f);
                var core = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Object.DestroyImmediate(core.GetComponent<Collider>());
                core.transform.SetParent(roll, false); core.transform.localScale = new Vector3(.028f, .0085f, .028f); core.GetComponent<Renderer>().sharedMaterial = paper;
                var f = roll.gameObject.AddComponent<DaydreamFloat>(); f.bobMetres = .035f; f.bobPeriod = 9; f.swayDegrees = 8; f.phase = i * .17f;
            }
            log.Add("Keepsakes: 6 floating washi rolls behind the desk");
            // Existing decorative floats share the same slow breath period.
            int n = 0; var commission = GameObject.Find("CommissionDesk")?.transform;
            foreach (var f in Object.FindObjectsByType<DaydreamFloat>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (f.transform.IsChildOf(extras) || (commission && f.transform.IsChildOf(commission)) || !f.transform.parent || f.transform.parent.name == "Clouds") continue;
                f.bobPeriod = 9; f.bobMetres = Mathf.Max(f.bobMetres, .015f); n++;
            }
            log.Add("Synchronised " + n + " decorative floats to a 9 s breath");
        }

        static void Lighting(Transform extras)
        {
            var sun = GameObject.Find("AfternoonSun")?.GetComponent<Light>();
            if (sun)
            {
                sun.color = C("FFF7EA"); sun.intensity = .98f; sun.shadowStrength = .42f;
                var old = sun.GetComponent<DaydreamLight>(); if (old) old.enabled = false;
                log.Add("Sun 0.69 -> 0.98, shadows .59 -> .42; DaydreamLight disabled in favour of DaydreamBreath");
            }
            var sky = GameObject.Find("RoomAndWindow/North skylight")?.GetComponent<Light>(); if (sky) { sky.intensity = .38f; sky.color = C("DCEBFF"); }
            var bounce = GameObject.Find("WindowBounce")?.GetComponent<Light>(); if (bounce) { bounce.intensity = .30f; bounce.color = C("FFE6EE"); }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = C("C6DDF2"); RenderSettings.ambientEquatorColor = C("E6DDE6"); RenderSettings.ambientGroundColor = C("E9D8D6");
            log.Add("Ambient trilight brightened (sky D2E6F7 / eq F1E7EF / ground F4E2E0)");

            var oldGrade = GameObject.Find("WarmColorGrade")?.GetComponent<Volume>(); if (oldGrade) { oldGrade.weight = 0; log.Add("WarmColorGrade weight -> 0 (kept)"); }
            string profilePath = Root + "DaydreamBrightGrade.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if (!profile) { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, profilePath); }
            foreach (var c in profile.components.ToArray()) { profile.Remove(c.GetType()); Object.DestroyImmediate(c, true); }
            var tone = profile.Add<Tonemapping>(true); tone.mode.Override(TonemappingMode.Neutral);
            var adj = profile.Add<ColorAdjustments>(true); adj.postExposure.Override(.12f); adj.contrast.Override(9); adj.saturation.Override(9);
            var bloom = profile.Add<Bloom>(true); bloom.intensity.Override(.2f); bloom.threshold.Override(1.05f); bloom.scatter.Override(.72f); bloom.tint.Override(C("FFF1F4"));
            var wb = profile.Add<WhiteBalance>(true); wb.temperature.Override(4); wb.tint.Override(3);
            foreach (var c in profile.components) { AssetDatabase.AddObjectToAsset(c, profile); c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy; }
            EditorUtility.SetDirty(profile);
            var go = new GameObject("Bright grade"); go.transform.SetParent(extras, false);
            var vol = go.AddComponent<Volume>(); vol.isGlobal = true; vol.priority = 10; vol.sharedProfile = profile;
            log.Add("Bright grade: neutral tonemap, exposure +.12, saturation +9, contrast +9, soft bloom .2");

            var breath = extras.gameObject.AddComponent<DaydreamBreath>(); breath.sun = sun; breath.grade = vol;
            breath.halo = extras.Find("Desk halo")?.GetComponent<Renderer>();
            log.Add("DaydreamBreath: 9 s shared rhythm on sun, exposure, bloom and halo");
        }
    }
}

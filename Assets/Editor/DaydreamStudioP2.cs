using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace VintageJournal.Editor
{
    /// <summary>
    /// Studio P2: three floating arch windows behind the desk, two arch-shaped light patches on the desk that drift
    /// with the breath, and the dusk sky texture for the optional pink-orange preset. Decorative only (no colliders).
    /// Rebuilds only the "Daydream Studio P2" root. Rollback: delete that root / handbook-before-studio-p2-20261106.
    /// </summary>
    public static partial class DaydreamBrightPass
    {
        const string P2Root = "Daydream Studio P2";
        public static readonly Vector3[] ArchPositions = { new Vector3(-1.32f, .92f, 2.3f), new Vector3(-.78f, .95f, 2.45f), new Vector3(-.24f, .92f, 2.3f) };

        static float ArchSdf(float x, float y, float halfW, float bottom, float top)
        {
            // Rectangle with a semicircular top; (x,y) in metres-like units, origin centre-bottom.
            float r = halfW, cy = top - r;
            float rect = Mathf.Max(Mathf.Abs(x) - halfW, Mathf.Max(bottom - y, y - cy));
            float circle = y > cy ? Mathf.Sqrt(x * x + (y - cy) * (y - cy)) - r : float.MaxValue;
            return Mathf.Min(rect, circle);
        }

        [MenuItem("Vintage Journal/Daydream/Apply studio P2")]
        public static void ApplyStudioP2Menu()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath); log.Clear(); ApplyStudioP2();
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Directory.CreateDirectory(Docs); File.AppendAllLines(Path.Combine(Docs, "apply-log.txt"), log);
        }

        static void ApplyStudioP2()
        {
            var old = GameObject.Find(P2Root); if (old) Object.DestroyImmediate(old);
            var root = new GameObject(P2Root).transform;
            var cam = Object.FindFirstObjectByType<DeskCamera>();
            Vector3 seat = cam && cam.profile ? cam.profile.seatedPosition : new Vector3(0, 1.45f, -1.03f);

            // ---- Arch textures (aspect 2:3) ----
            const float aw = .5f, ah = 1.5f; // half width / height in texture units (u: -.5..+.5, v: 0..1.5)
            var frameTex = Gradient("ArchFrame", 256, 384, (u, v) =>
            {
                float x = u - .5f, y = v * ah; float outer = ArchSdf(x, y, .48f, .02f, 1.48f), inner = ArchSdf(x, y, .40f, .08f, 1.40f);
                float a = Mathf.Clamp01(.5f - outer * 256) * Mathf.Clamp01(.5f + inner * 256);
                float shade = Mathf.Lerp(.92f, 1f, Mathf.Clamp01(-inner * 20));
                return new Color(shade, shade, shade, a);
            });
            var innerTex = Gradient("ArchInner", 256, 384, (u, v) =>
            {
                float x = u - .5f, y = v * ah; float inner = ArchSdf(x, y, .40f, .08f, 1.40f);
                float a = Mathf.Clamp01(.5f - inner * 256);
                float glow = Mathf.Lerp(1f, .86f, Mathf.Clamp01(Mathf.Abs(x) * 1.6f)); // brighter centre
                var c = Color.Lerp(new Color(1f, .97f, .95f), new Color(.86f, .92f, 1f), Mathf.SmoothStep(.1f, 1.35f, y)) * glow;
                return new Color(c.r, c.g, c.b, a);
            });
            var frameMat = Mat("Arch frame", C("F8E1E7"), 0, 0, frameTex, true, true);
            var depthMat = Mat("Arch depth", C("E2BCCB"), 0, 0, frameTex, true, true);
            var innerMat = Mat("Arch inner sky", Color.white, 0, 0, innerTex, true, true);
            for (int i = 0; i < ArchPositions.Length; i++)
            {
                var arch = new GameObject("Floating arch " + (i + 1)).transform; arch.SetParent(root, false); arch.position = ArchPositions[i];
                arch.rotation = Quaternion.LookRotation(arch.position - seat, Vector3.up);
                float h = i == 1 ? .52f : .46f, w = h * (2f / 3f);
                var inner = Piece(arch, "Inner sky", PrimitiveType.Quad, arch.position, new Vector3(w, h, 1), innerMat, arch.eulerAngles);
                var depth = Piece(arch, "Arch depth", PrimitiveType.Quad, arch.position + arch.forward * .012f + arch.right * .008f - arch.up * .006f, new Vector3(w * 1.03f, h * 1.02f, 1), depthMat, arch.eulerAngles);
                var frame = Piece(arch, "Arch frame", PrimitiveType.Quad, arch.position - arch.forward * .002f, new Vector3(w, h, 1), frameMat, arch.eulerAngles);
                foreach (var r in arch.GetComponentsInChildren<Renderer>()) { r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; }
                var tint = arch.gameObject.AddComponent<DreamTint>(); tint.targets = new[] { inner.GetComponent<Renderer>() };
                tint.day = new Color(.93f, .97f, 1f, .96f); tint.dusk = new Color(1f, .80f, .70f, .96f); tint.night = new Color(.30f, .33f, .60f, .90f); tint.breathAlpha = .04f;
                var f = arch.gameObject.AddComponent<DaydreamFloat>(); f.bobMetres = .012f; f.bobPeriod = 9; f.swayDegrees = .8f; f.phase = i * .3f;
            }
            log.Add("P2: 3 floating arch windows (frame + depth + tinted inner sky) facing the seat at " + string.Join(" ", System.Array.ConvertAll(ArchPositions, p => p.ToString("F2"))));

            // ---- Arch-shaped light patches on the desk front (sun from the left-back) ----
            var patchTex = Gradient("ArchLightPatch", 128, 192, (u, v) =>
            {
                float x = u - .5f, y = v * ah; float d = ArchSdf(x, y, .40f, .1f, 1.38f);
                float a = Mathf.Clamp01(.5f - d * 9); a = a * a * (3 - 2 * a);
                return new Color(1, 1, 1, a);
            });
            var patchMat = Mat("Arch light patch", new Color(1, .96f, .88f, .40f), 0, 0, patchTex, true, true);
            var patches = new[] { (new Vector3(-.40f, .7606f, -.285f), 28f), (new Vector3(.40f, .7606f, -.30f), 24f) };
            int n = 0;
            foreach (var (pos, yaw) in patches)
            {
                var p = Piece(root, "Arch light patch " + (++n), PrimitiveType.Quad, pos, new Vector3(.12f, .20f, 1), patchMat, new Vector3(90, yaw, 0));
                var r = p.GetComponent<Renderer>(); r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                var tint = p.gameObject.AddComponent<DreamTint>(); tint.targets = new[] { r };
                tint.day = new Color(1f, .97f, .90f, .40f); tint.dusk = new Color(1f, .78f, .66f, .45f); tint.night = new Color(.7f, .75f, 1f, .05f);
                tint.breathDrift = new Vector3(.025f, 0, .01f); tint.breathAlpha = .25f;
            }
            log.Add("P2: 2 arch light patches on the desk front, drifting ±2.5 cm with the breath");

            // ---- Dusk sky texture for the optional pink-orange preset ----
            const string duskPath = "Assets/Daydream/FloatingV2/Textures/SkyDusk.png";
            Color top = C("6A73C4"), upper = C("B79BDC"), glow = C("FFB7A2"), horizon = C("FFD2B4"), below = C("F2B2C4"), deep = C("9C86D2");
            var sky = new Texture2D(8, 1024, TextureFormat.RGB24, false);
            for (int y = 0; y < 1024; y++)
            {
                float v = y / 1023f; Color c;
                if (v > .74f) c = Color.Lerp(upper, top, Mathf.SmoothStep(0, 1, (v - .74f) / .26f));
                else if (v > .54f) c = Color.Lerp(glow, upper, Mathf.SmoothStep(0, 1, (v - .54f) / .20f));
                else if (v > .49f) c = Color.Lerp(horizon, glow, Mathf.SmoothStep(0, 1, (v - .49f) / .05f));
                else if (v > .40f) c = Color.Lerp(below, horizon, Mathf.SmoothStep(0, 1, (v - .40f) / .09f));
                else c = Color.Lerp(deep, below, Mathf.SmoothStep(0, 1, v / .40f));
                for (int x = 0; x < 8; x++) sky.SetPixel(x, y, c);
            }
            File.WriteAllBytes(duskPath, sky.EncodeToPNG()); Object.DestroyImmediate(sky); AssetDatabase.ImportAsset(duskPath);
            var imp = (TextureImporter)AssetImporter.GetAtPath(duskPath); imp.wrapMode = TextureWrapMode.Clamp; imp.mipmapEnabled = false; imp.SaveAndReimport();
            var skyMat = AssetDatabase.LoadAssetAtPath<Material>(SkyMat);
            skyMat.SetTexture("_DuskTex", AssetDatabase.LoadAssetAtPath<Texture2D>(duskPath)); EditorUtility.SetDirty(skyMat);
            var time = Object.FindFirstObjectByType<DreamTimeController>();
            if (time)
            {
                time.duskSun = C("FFDCC8"); time.duskFill = C("E3D3F2"); time.duskSunMultiplier = .82f;
                time.duskAmbientSky = C("BDB8DE"); time.duskAmbientEquator = C("D6C2C6"); time.duskAmbientGround = C("B7A7AE"); time.duskFog = C("F0D6D6");
                EditorUtility.SetDirty(time);
            }
            log.Add("P2: dusk sky texture SkyDusk.png bound to _DuskTex (day/night textures unchanged)");
        }
    }
}

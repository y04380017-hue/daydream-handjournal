using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VintageJournal.Editor
{
    /// <summary>
    /// Incremental: shifts only the DAY preset of the floating day/night system toward pink-purple.
    /// Writes a new SkyDayPink.png (SkyDay.png untouched) and sets DreamTimeController day colours.
    /// Rollback: menu "Restore original day sky", or restore from handbook-before-sky-shift-20261106.
    /// </summary>
    public static partial class DaydreamBrightPass
    {
        const string SkyMat = "Assets/Daydream/FloatingV2/Materials/Day and night sky.mat";
        const string PinkSky = "Assets/Daydream/FloatingV2/Textures/SkyDayPink.png";
        const string OriginalSky = "Assets/Daydream/FloatingV2/Textures/SkyDay.png";

        [MenuItem("Vintage Journal/Daydream/Apply pink-purple day sky")]
        public static void ApplyPinkSkyMenu()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath); log.Clear();
            // Same layout as SkyDay.png (2:1, horizon at the middle row) so the dome mapping is unchanged.
            const int w = 8, h = 1024;
            Color top = C("74AAE8"), upper = C("A9C9F2"), glow = C("EAD9F2"), horizon = C("FCE1E8"), below = C("D9D4F3"), deep = C("B9BDEE");
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            for (int y = 0; y < h; y++)
            {
                float v = y / (h - 1f); Color c;
                if (v > .78f) c = Color.Lerp(upper, top, Mathf.SmoothStep(0, 1, (v - .78f) / .22f));
                else if (v > .55f) c = Color.Lerp(glow, upper, Mathf.SmoothStep(0, 1, (v - .55f) / .23f));
                else if (v > .495f) c = Color.Lerp(horizon, glow, Mathf.SmoothStep(0, 1, (v - .495f) / .055f));
                else if (v > .43f) c = Color.Lerp(below, horizon, Mathf.SmoothStep(0, 1, (v - .43f) / .065f));
                else c = Color.Lerp(deep, below, Mathf.SmoothStep(0, 1, v / .43f));
                for (int x = 0; x < w; x++) tex.SetPixel(x, y, c);
            }
            File.WriteAllBytes(PinkSky, tex.EncodeToPNG()); Object.DestroyImmediate(tex); AssetDatabase.ImportAsset(PinkSky);
            var imp = (TextureImporter)AssetImporter.GetAtPath(PinkSky); imp.wrapMode = TextureWrapMode.Clamp; imp.mipmapEnabled = false; imp.SaveAndReimport();
            var mat = AssetDatabase.LoadAssetAtPath<Material>(SkyMat);
            mat.SetTexture("_DayTex", AssetDatabase.LoadAssetAtPath<Texture2D>(PinkSky)); EditorUtility.SetDirty(mat);
            log.Add("Day sky texture -> SkyDayPink.png (SkyDay.png kept); night texture unchanged");

            var time = Object.FindFirstObjectByType<DreamTimeController>();
            if (time)
            {
                time.daySun = C("FFF5EE"); time.dayFill = C("DCDDFF");
                time.dayAmbientSky = C("ABBCE2"); time.dayAmbientEquator = C("C9BFC8"); time.dayAmbientGround = C("BDB0B6");
                time.dayFog = C("E4E0F3");
                EditorUtility.SetDirty(time);
                log.Add("DreamTimeController day palette: sun FFF5EE, fill DCDDFF, ambient ABBCE2/C9BFC8/BDB0B6, fog E4E0F3 (night unchanged)");
            }
            var cam = Object.FindFirstObjectByType<DeskCamera>()?.GetComponent<Camera>();
            if (cam) { cam.backgroundColor = C("DCD8F3"); EditorUtility.SetDirty(cam); }
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Directory.CreateDirectory(Docs); File.AppendAllLines(Path.Combine(Docs, "apply-log.txt"), log);
        }

        [MenuItem("Vintage Journal/Daydream/Restore original day sky")]
        public static void RestoreDaySkyMenu()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(SkyMat);
            mat.SetTexture("_DayTex", AssetDatabase.LoadAssetAtPath<Texture2D>(OriginalSky)); EditorUtility.SetDirty(mat);
            var time = Object.FindFirstObjectByType<DreamTimeController>();
            if (time)
            {
                time.daySun = new Color(1, .97f, .91f); time.dayFill = new Color(.86f, .92f, 1);
                time.dayAmbientSky = new Color(.63f, .73f, .84f); time.dayAmbientEquator = new Color(.70f, .67f, .69f); time.dayAmbientGround = new Color(.65f, .59f, .57f);
                time.dayFog = new Color(.84f, .91f, .95f); EditorUtility.SetDirty(time);
            }
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        }
    }
}

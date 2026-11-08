using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VintageJournal.Editor
{
    /// <summary>
    /// Eight bright A5 papers drawn procedurally in the daydream palette. They re-skin only the "Replaceable artwork"
    /// of the physical pages (asset IDs, saves and the original PAGE_xx art stay untouched) and are added to the
    /// paper catalog as new, additive choices.
    /// </summary>
    public static partial class DaydreamBrightPass
    {
        const int PW = 840, PH = 1188;
        const string PaperDir = "Assets/Daydream/Bright/Papers/";

        sealed class Canvas
        {
            public readonly Color[] px = new Color[PW * PH];
            public Canvas(Color fill) { for (int i = 0; i < px.Length; i++) px[i] = fill; }
            // y measured from top for authoring convenience.
            public void Blend(int x, int y, Color c, float a)
            {
                if (x < 0 || y < 0 || x >= PW || y >= PH || a <= 0) return;
                int i = (PH - 1 - y) * PW + x; a = Mathf.Clamp01(a * c.a); var d = px[i];
                px[i] = new Color(d.r + (c.r - d.r) * a, d.g + (c.g - d.g) * a, d.b + (c.b - d.b) * a, 1);
            }
            public void Shape(float x0, float y0, float x1, float y1, Color c, Func<float, float, float> sdf)
            {
                for (int y = Mathf.Max(0, (int)y0 - 2); y <= Mathf.Min(PH - 1, (int)y1 + 2); y++)
                    for (int x = Mathf.Max(0, (int)x0 - 2); x <= Mathf.Min(PW - 1, (int)x1 + 2); x++)
                    { float d = sdf(x + .5f, y + .5f); if (d < 1) Blend(x, y, c, Mathf.Clamp01(.5f - d)); }
            }
            public void Circle(float cx, float cy, float r, Color c) => Shape(cx - r, cy - r, cx + r, cy + r, c, (x, y) => Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r);
            public void Ring(float cx, float cy, float r, float w, Color c) => Shape(cx - r - w, cy - r - w, cx + r + w, cy + r + w, c, (x, y) => Mathf.Abs(Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r) - w * .5f);
            public void Rect(float x, float y, float w, float h, float r, Color c) => Shape(x, y, x + w, y + h, c, (px_, py) =>
            { float qx = Mathf.Abs(px_ - (x + w * .5f)) - (w * .5f - r), qy = Mathf.Abs(py - (y + h * .5f)) - (h * .5f - r); return new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0) - r; });
            public void Frame(float x, float y, float w, float h, float r, float t, Color c) => Shape(x - t, y - t, x + w + t, y + h + t, c, (px_, py) =>
            { float qx = Mathf.Abs(px_ - (x + w * .5f)) - (w * .5f - r), qy = Mathf.Abs(py - (y + h * .5f)) - (h * .5f - r); float d = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0) - r; return Mathf.Abs(d) - t * .5f; });
            public void Line(float ax, float ay, float bx, float by, float w, Color c) => Shape(Mathf.Min(ax, bx) - w, Mathf.Min(ay, by) - w, Mathf.Max(ax, bx) + w, Mathf.Max(ay, by) + w, c, (x, y) =>
            { var p = new Vector2(x - ax, y - ay); var ba = new Vector2(bx - ax, by - ay); float h = Mathf.Clamp01(Vector2.Dot(p, ba) / Vector2.Dot(ba, ba)); return (p - ba * h).magnitude - w * .5f; });
            public void Dashed(float ax, float y, float bx, float w, float dash, float gap, Color c) { for (float x = ax; x < bx; x += dash + gap) Line(x, y, Mathf.Min(x + dash, bx), y, w, c); }
            public void Star(float cx, float cy, float r, Color c) => Shape(cx - r, cy - r, cx + r, cy + r, c, (x, y) =>
            { float a = Mathf.Atan2(y - cy, x - cx), d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)); float k = Mathf.Cos(Mathf.Repeat(a + Mathf.PI / 2, 2 * Mathf.PI / 5) - Mathf.PI / 5); return d - r * (.55f + .45f * k * k * k * k); });
            public void Flower(float cx, float cy, float r, Color petal, Color centre, float turn = 0)
            { for (int i = 0; i < 5; i++) { float a = turn + i * Mathf.PI * 2 / 5; Circle(cx + Mathf.Cos(a) * r * .55f, cy + Mathf.Sin(a) * r * .55f, r * .45f, petal); } Circle(cx, cy, r * .3f, centre); }
            public void Leaf(float cx, float cy, float len, float angle, Color c) => Shape(cx - len, cy - len, cx + len, cy + len, c, (x, y) =>
            { float s = Mathf.Sin(-angle), co = Mathf.Cos(-angle); float lx = (x - cx) * co - (y - cy) * s, ly = (x - cx) * s + (y - cy) * co; float t = Mathf.Clamp(lx / len, -1, 1); return Mathf.Abs(ly) - len * .32f * (1 - t * t) + (Mathf.Abs(lx) > len ? Mathf.Abs(lx) - len : 0); });
            public void Cloud(float cx, float cy, float s, Color c) { Circle(cx - s * .5f, cy, s * .38f, c); Circle(cx, cy - s * .18f, s * .5f, c); Circle(cx + s * .55f, cy + s * .02f, s * .36f, c); Rect(cx - s * .85f, cy, s * 1.75f, s * .36f, s * .18f, c); }
            public void Heart(float cx, float cy, float s, Color c) { Circle(cx - s * .28f, cy - s * .1f, s * .3f, c); Circle(cx + s * .28f, cy - s * .1f, s * .3f, c); Shape(cx - s * .6f, cy - s * .2f, cx + s * .6f, cy + s * .7f, c, (x, y) => Mathf.Abs(x - cx) + (y - cy) * .9f - s * .55f); }
            public void Grain(int seed, float amount)
            {
                var r = new System.Random(seed);
                for (int i = 0; i < px.Length; i++) { float n = ((float)r.NextDouble() - .5f) * amount; var d = px[i]; px[i] = new Color(d.r + n, d.g + n, d.b + n * .8f, 1); }
                for (int f = 0; f < 260; f++)
                { float x = (float)r.NextDouble() * PW, y = (float)r.NextDouble() * PH, a = (float)r.NextDouble() * 6.3f, l = 6 + (float)r.NextDouble() * 18; Line(x, y, x + Mathf.Cos(a) * l, y + Mathf.Sin(a) * l, 1.1f, new Color(.6f, .55f, .5f, .07f)); }
            }
        }

        static Color P(string k, float a = 1) { var c = Palette[k]; c.a = a; return c; }
        static readonly Color Ink = new Color(.42f, .5f, .62f);

        static void Ruled(Canvas c, float x, float y0, float w, float y1, float step, Color col) { col.a = Mathf.Min(1, col.a * 1.7f); for (float y = y0; y < y1; y += step) c.Line(x, y, x + w, y, 3.4f, col); }
        static void DotGrid(Canvas c, float x0, float y0, float x1, float y1, float step, Color col) { col.a = Mathf.Min(1, col.a * 1.6f); for (float y = y0; y <= y1; y += step) for (float x = x0; x <= x1; x += step) c.Circle(x, y, 3.6f, col); }
        static void Washi(Canvas c, float x, float y, float w, float h, float angle, Color col, Color stripe)
        {
            float cx = x + w / 2, cy = y + h / 2, s = Mathf.Sin(-angle), co = Mathf.Cos(-angle), R = Mathf.Max(w, h);
            c.Shape(cx - R, cy - R, cx + R, cy + R, col, (px_, py) => { float lx = (px_ - cx) * co - (py - cy) * s, ly = (px_ - cx) * s + (py - cy) * co; return Mathf.Max(Mathf.Abs(lx) - w / 2 + 3 * Mathf.Sin(ly * .9f), Mathf.Abs(ly) - h / 2); });
            c.Shape(cx - R, cy - R, cx + R, cy + R, stripe, (px_, py) => { float lx = (px_ - cx) * co - (py - cy) * s, ly = (px_ - cx) * s + (py - cy) * co; float band = Mathf.Abs(Mathf.Repeat(lx, 22) - 11) - 4; return Mathf.Max(band, Mathf.Max(Mathf.Abs(lx) - w / 2 + 6, Mathf.Abs(ly) - h / 2 + 2)); });
        }

        static readonly (string id, string name)[] PaperNames =
        {
            ("paper_bright_01", "晴空 · 云朵横线"), ("paper_bright_02", "薄荷 · 格纹信笺"), ("paper_bright_03", "珊瑚 · 小花园"), ("paper_bright_04", "柠檬 · 点阵"),
            ("paper_bright_05", "淡紫 · 星星夜"), ("paper_bright_06", "玫瑰 · 波点花边"), ("paper_bright_07", "彩虹 · 糖果条纹"), ("paper_bright_08", "航空 · 邮票信笺"),
        };

        static Canvas DrawPaper(int index)
        {
            var tints = new[] { "D9EEFB", "DDF4EA", "FFE6DC", "FFF2CC", "E9E2FB", "FCE3EE", "FFF4E8", "FFF6EC" };
            ColorUtility.TryParseHtmlString("#" + tints[index], out var bg); var c = new Canvas(bg);
            switch (index)
            {
                case 0: // sky + clouds + ruled lines
                    for (int y = 0; y < 300; y++) { var col = Color.Lerp(P("sky"), P("skySoft"), y / 300f); c.Line(0, y, PW, y, 1.6f, col); }
                    c.Cloud(170, 190, 150, Color.white); c.Cloud(560, 140, 120, Color.white); c.Cloud(720, 250, 80, P("paper", .88f));
                    foreach (var (x, y) in new[] { (380f, 110f), (430f, 160f) }) { c.Line(x - 16, y, x, y + 8, 3, Ink); c.Line(x, y + 8, x + 16, y, 3, Ink); }
                    c.Rect(70, 340, PW - 140, PH - 420, 36, P("paper", .9f)); c.Frame(70, 340, PW - 140, PH - 420, 36, 4, P("sky", .7f));
                    Ruled(c, 120, 440, PW - 240, PH - 120, 62, P("sky", .45f)); c.Line(150, 380, 150, PH - 110, 2.5f, P("coral", .5f));
                    c.Star(740, 1110, 22, P("lemon")); c.Star(100, 1120, 14, P("coral"));
                    break;
                case 1: // mint gingham + letter card
                    for (int x = 0; x < PW; x += 56) c.Rect(x, 0, 28, PH, 0, P("mint", .32f));
                    for (int y = 0; y < PH; y += 56) c.Rect(0, y, PW, 28, 0, P("mint", .32f));
                    c.Rect(84, 120, PW - 168, PH - 240, 28, P("cream")); c.Frame(100, 136, PW - 200, PH - 272, 20, 3, P("mint"));
                    c.Dashed(150, 300, PW - 150, 3, 16, 10, P("mint", .8f));
                    Ruled(c, 150, 380, PW - 300, PH - 190, 64, P("mint", .5f));
                    c.Heart(PW / 2f, 210, 46, P("coral")); Washi(c, 40, 70, 200, 52, -.5f, P("lemon", .85f), P("paper", .8f)); Washi(c, PW - 240, PH - 120, 200, 52, -.45f, P("rose", .85f), P("paper", .8f));
                    break;
                case 2: // coral garden
                    DotGrid(c, 80, 80, PW - 80, PH - 80, 44, P("coral", .28f));
                    c.Rect(120, 230, PW - 240, PH - 470, 30, P("paper", .88f));
                    var flowers = new[] { (110f, 130f, 70f, "coral"), (230f, 90f, 44f, "lemon"), (90f, 260f, 38f, "rose"), (PW - 120f, PH - 140f, 76f, "coral"), (PW - 250f, PH - 90f, 46f, "lilac"), (PW - 90f, PH - 280f, 40f, "lemon"), (PW - 140f, 140f, 34f, "rose"), (140f, PH - 150f, 36f, "lilac") };
                    foreach (var (x, y, r, k) in flowers) { c.Leaf(x + r * .9f, y + r * .7f, r * .7f, .6f, P("leaf")); c.Leaf(x - r * .9f, y + r * .6f, r * .6f, 2.5f, P("leaf", .85f)); c.Flower(x, y, r, P(k), P(k == "lemon" ? "coral" : "lemon"), x * .01f); }
                    Ruled(c, 170, 320, PW - 340, PH - 260, 60, P("coral", .35f));
                    break;
                case 3: // lemon dot grid
                    DotGrid(c, 70, 200, PW - 70, PH - 70, 40, P("sky", .45f));
                    c.Rect(0, 0, PW, 150, 0, P("lemon", .9f)); for (int x = 0; x < PW; x += 60) c.Circle(x + 30, 150, 30, P("lemon", .9f));
                    for (int i = 0; i < 4; i++) { float x = 140 + i * 190; c.Circle(x, 70, 40, P("paper")); c.Circle(x, 70, 33, P("lemon")); for (int s = 0; s < 6; s++) { float a = s * Mathf.PI / 3; c.Line(x, 70, x + Mathf.Cos(a) * 30, 70 + Mathf.Sin(a) * 30, 3, P("paper")); } }
                    Washi(c, 60, PH - 160, 230, 54, .35f, P("mint", .85f), P("paper", .7f)); Washi(c, PW - 290, 230, 230, 54, -.3f, P("coral", .85f), P("paper", .7f));
                    c.Rect(PW - 220, PH - 150, 150, 90, 14, P("rose", .25f)); c.Dashed(PW - 205, PH - 105, PW - 85, 3, 12, 8, P("rose"));
                    break;
                case 4: // lilac starry night
                    for (int y = 0; y < 420; y++) { var col = Color.Lerp(P("lilac"), P("paper"), Mathf.SmoothStep(0, 1, y / 420f)); c.Line(0, y, PW, y, 1.6f, col); }
                    c.Circle(660, 140, 70, P("lemon")); c.Circle(690, 120, 62, Color.Lerp(P("lilac"), P("paper"), .15f));
                    var rand = new System.Random(5); for (int i = 0; i < 26; i++) { float x = (float)rand.NextDouble() * PW, y = (float)rand.NextDouble() * 360; c.Star(x, y, 6 + (float)rand.NextDouble() * 10, P("paper", .88f)); }
                    c.Rect(90, 360, PW - 180, PH - 450, 32, P("cream")); c.Frame(106, 376, PW - 212, PH - 482, 22, 3, P("lilac"));
                    Ruled(c, 150, 470, PW - 300, PH - 130, 64, P("lilac", .5f));
                    c.Star(120, PH - 70, 20, P("lemon")); c.Star(170, PH - 50, 12, P("lilac"));
                    break;
                case 5: // rose polka + scallop frame
                    for (int y = 0; y < PH + 60; y += 60) for (int x = (y / 60) % 2 * 30; x < PW + 60; x += 60) c.Circle(x, y, 9, P("rose", .5f));
                    c.Rect(110, 140, PW - 220, PH - 280, 10, P("paper"));
                    for (float x = 110; x <= PW - 110; x += 40) { c.Circle(x, 140, 20, P("paper")); c.Circle(x, PH - 140, 20, P("paper")); }
                    for (float y = 140; y <= PH - 140; y += 40) { c.Circle(110, y, 20, P("paper")); c.Circle(PW - 110, y, 20, P("paper")); }
                    c.Frame(150, 180, PW - 300, PH - 360, 18, 3, P("rose"));
                    c.Heart(PW / 2f, 260, 40, P("rose")); c.Flower(PW / 2f - 80, 260, 26, P("lemon"), P("coral")); c.Flower(PW / 2f + 80, 260, 26, P("lemon"), P("coral"));
                    Ruled(c, 190, 360, PW - 380, PH - 230, 62, P("rose", .4f));
                    break;
                case 6: // candy stripes margin + tickets
                    var stripes = new[] { "coral", "lemon", "mint", "sky", "lilac", "rose" };
                    for (int i = 0; i < 6; i++) c.Rect(i * 22, 0, 22, PH, 0, P(stripes[i], .75f));
                    c.Rect(170, 90, PW - 230, 140, 22, P("sky", .25f)); c.Dashed(190, 160, PW - 80, 3, 14, 10, P("sky"));
                    for (int i = 0; i < 3; i++) { float y = PH - 330 + i * 92; c.Rect(PW - 300, y, 240, 72, 12, P(stripes[i * 2], .55f)); c.Circle(PW - 300, y + 36, 14, P("paper")); c.Circle(PW - 60, y + 36, 14, P("paper")); c.Dashed(PW - 270, y + 36, PW - 90, 3, 10, 8, P("paper")); }
                    Ruled(c, 190, 300, PW - 260, PH - 360, 60, P("lilac", .4f)); DotGrid(c, 200, PH - 330, PW - 360, PH - 90, 36, P("mint", .5f));
                    break;
                default: // airmail + stamps
                    for (int i = -PH; i < PW + PH; i += 64)
                    { c.Line(i, 0, i + 40, 40, 22, P("coral")); c.Line(i + 32, 0, i + 72, 40, 22, P("sky")); c.Line(i, PH - 40, i + 40, PH, 22, P("sky")); c.Line(i + 32, PH - 40, i + 72, PH, 22, P("coral")); }
                    for (int i = -PW; i < PH + PW; i += 64) { c.Line(0, i, 40, i + 40, 22, P("coral")); c.Line(0, i + 32, 40, i + 72, 22, P("sky")); c.Line(PW - 40, i, PW, i + 40, 22, P("sky")); c.Line(PW - 40, i + 32, PW, i + 72, 22, P("coral")); }
                    c.Rect(40, 40, PW - 80, PH - 80, 0, P("paper"));
                    var stamps = new[] { (PW - 220f, 80f, "mint"), (PW - 360f, 110f, "lemon") };
                    foreach (var (x, y, k) in stamps)
                    { c.Rect(x, y, 120, 150, 4, P("paper")); for (float s = x; s <= x + 120; s += 15) { c.Circle(s, y, 6, P("paper")); c.Circle(s, y + 150, 6, P("paper")); } c.Rect(x + 12, y + 12, 96, 126, 4, P(k, .8f)); c.Flower(x + 60, y + 70, 28, P("paper"), P("coral")); }
                    c.Ring(PW - 290, 230, 70, 4, P("indigo", .45f)); c.Line(PW - 420, 220, PW - 160, 240, 4, P("indigo", .35f)); c.Line(PW - 420, 245, PW - 160, 265, 4, P("indigo", .35f));
                    Ruled(c, 110, 380, PW - 220, PH - 140, 64, P("sky", .45f)); c.Line(110, 330, 420, 330, 3, P("coral", .6f));
                    break;
            }
            c.Grain(31 + index, .018f);
            return c;
        }

        [MenuItem("Vintage Journal/Daydream/Apply bright papers")]
        public static void ApplyPapersMenu()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath); log.Clear(); ApplyPapers();
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Directory.CreateDirectory(Docs); File.AppendAllLines(Path.Combine(Docs, "apply-log.txt"), log);
        }

        static void ApplyPapers()
        {
            Directory.CreateDirectory(PaperDir); AssetDatabase.Refresh();
            var textures = new List<Texture2D>();
            for (int i = 0; i < PaperNames.Length; i++)
            {
                var canvas = DrawPaper(i); var tex = new Texture2D(PW, PH, TextureFormat.RGB24, false); tex.SetPixels(canvas.px); tex.Apply();
                string path = PaperDir + "bright_paper_0" + (i + 1) + ".png"; File.WriteAllBytes(path, tex.EncodeToPNG()); Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path); var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                imp.wrapMode = TextureWrapMode.Clamp; imp.mipmapEnabled = true; imp.anisoLevel = 4; imp.maxTextureSize = 2048; imp.isReadable = false; imp.SaveAndReimport();
                textures.Add(AssetDatabase.LoadAssetAtPath<Texture2D>(path));
                // Docs copy for review.
                File.Copy(path, Path.Combine(Docs, "bright_paper_0" + (i + 1) + ".png"), true);
            }
            log.Add("Drew " + textures.Count + " bright A5 papers (" + PW + "x" + PH + ") in " + PaperDir);

            // Catalog: additive choices for 「更换内页」.
            var catalog = JournalPaperCatalog.Load();
            for (int i = 0; i < PaperNames.Length; i++)
            {
                var def = catalog.Find(PaperNames[i].id);
                if (def == null) { def = new JournalPaperDefinition { id = PaperNames[i].id }; ArrayUtility.Add(ref catalog.papers, def); }
                def.displayName = PaperNames[i].name; def.texture = textures[i];
            }
            EditorUtility.SetDirty(catalog);
            log.Add("Paper catalog: +" + PaperNames.Length + " bright papers (existing 25 papers and default unchanged)");

            // Re-skin the physical A5 pages' visual layer only, in a stable order.
            var pages = Object.FindObjectsByType<PhysicalInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(p => p.itemType == PhysicalItemType.LoosePage).OrderBy(p => p.assetId).ThenBy(p => p.itemInstanceId).ToArray();
            for (int i = 0; i < pages.Length; i++)
            {
                int k = i % PaperNames.Length; var page = pages[i];
                var art = page.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.sharedMaterial && (r.sharedMaterial.name.EndsWith("_art") || r.sharedMaterial.name.StartsWith("Bright page art")));
                if (!art) { log.Add("No artwork renderer on " + page.itemInstanceId); continue; }
                var source = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GetAssetPath(art.sharedMaterial)) ?? art.sharedMaterial;
                var m = Tint(source, "Bright page art " + (k + 1), new Color(.93f, .93f, .93f, 1)); m.SetTexture("_BaseMap", textures[k]); if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", textures[k]);
                m.name = "Bright page art " + (k + 1); EditorUtility.SetDirty(m);
                art.sharedMaterial = m; page.displayName = PaperNames[k].name; EditorUtility.SetDirty(page);
                log.Add("Page visual " + page.assetId + " (" + page.itemInstanceId + ") -> " + PaperNames[k].name);
            }
        }
    }
}

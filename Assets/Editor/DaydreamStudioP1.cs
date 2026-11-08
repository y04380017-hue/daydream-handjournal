using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace VintageJournal.Editor
{
    /// <summary>
    /// Studio P1 (art reference pass): tiled floating platform under the desk, two shallow water strips with
    /// drifting caustics, and a flat far cloud-sea layer below the horizon. Decorative only: no colliders,
    /// nothing touches the desk, storage, interactables or camera. Rebuilds only the "Daydream Studio" root.
    /// Rollback: delete that root, or restore E:/TapeGestureDemo/backups/handbook-before-studio-p1-20261106.
    /// </summary>
    public static partial class DaydreamBrightPass
    {
        const string StudioRoot = "Daydream Studio";

        [MenuItem("Vintage Journal/Daydream/Apply studio P1")]
        public static void ApplyStudioMenu()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath); log.Clear(); ApplyStudioP1();
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Directory.CreateDirectory(Docs); File.AppendAllLines(Path.Combine(Docs, "apply-log.txt"), log);
        }

        static Transform Piece(Transform parent, string name, PrimitiveType type, Vector3 pos, Vector3 scale, Material m, Vector3 euler = default)
        {
            var go = GameObject.CreatePrimitive(type); Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name; go.transform.SetParent(parent, false); go.transform.position = pos; go.transform.localScale = scale; go.transform.rotation = Quaternion.Euler(euler);
            var r = go.GetComponent<Renderer>(); r.sharedMaterial = m; return go.transform;
        }

        static void ApplyStudioP1()
        {
            Directory.CreateDirectory(Root + "Textures"); AssetDatabase.Refresh();
            var old = GameObject.Find(StudioRoot); if (old) Object.DestroyImmediate(old);
            var root = new GameObject(StudioRoot).transform;

            // ---- Tiles: 8x8 glazed squares per texture tile (2.5 cm each at 0.2 m repeat) ----
            Color grout = C("EBD9DD"), tileA = Palette["cream"], tileB = C("FFF4EE");
            var tiles = Gradient("StudioTile", 256, 256, (x, y) =>
            {
                float gx = Mathf.Repeat(x * 8, 1), gy = Mathf.Repeat(y * 8, 1);
                float edge = Mathf.Min(Mathf.Min(gx, 1 - gx), Mathf.Min(gy, 1 - gy));
                bool alt = ((int)(x * 8) + (int)(y * 8)) % 2 == 0; var face = alt ? tileA : tileB;
                face = Color.Lerp(face, Color.white, Mathf.Clamp01(.35f - Mathf.Abs(gx - .3f) - Mathf.Abs(gy - .3f)) * .6f); // soft glaze highlight
                return Color.Lerp(grout, face, Mathf.SmoothStep(.02f, .05f, edge));
            }, true);
            var mintTiles = Gradient("StudioTileMint", 256, 256, (x, y) =>
            {
                float gx = Mathf.Repeat(x * 8, 1), gy = Mathf.Repeat(y * 8, 1);
                float edge = Mathf.Min(Mathf.Min(gx, 1 - gx), Mathf.Min(gy, 1 - gy));
                return Color.Lerp(C("CFEFE6"), Palette["mint"], Mathf.SmoothStep(.02f, .05f, edge));
            }, true);
            var tileMat = Mat("Studio glazed tile", Color.white, .62f, 0, tiles); tileMat.mainTextureScale = new Vector2(2.8f / .2f, 2.2f / .2f);
            var mintTileMat = Mat("Studio pool tile", Color.white, .7f, 0, mintTiles); mintTileMat.mainTextureScale = new Vector2(.5f / .2f, 1.6f / .2f);
            var trim = Mat("Studio mint trim", Palette["mint"], .55f);
            var concrete = Mat("Studio pale concrete", C("E9E1DD"), .18f);

            // Platform: top surface exactly at the desk feet (y = 0); a thicker concrete body beneath shows it floats.
            Piece(root, "Tiled platform", PrimitiveType.Cube, new Vector3(.02f, -.02f, .05f), new Vector3(2.8f, .04f, 2.2f), tileMat);
            Piece(root, "Platform body", PrimitiveType.Cube, new Vector3(.02f, -.19f, .05f), new Vector3(2.74f, .30f, 2.14f), concrete);
            foreach (var (pos, scl) in new[] {
                (new Vector3(.02f, .004f, -1.05f), new Vector3(2.84f, .012f, .04f)), (new Vector3(.02f, .004f, 1.15f), new Vector3(2.84f, .012f, .04f)),
                (new Vector3(-1.4f, .004f, .05f), new Vector3(.04f, .012f, 2.24f)), (new Vector3(1.44f, .004f, .05f), new Vector3(.04f, .012f, 2.24f)) })
                Piece(root, "Mint trim", PrimitiveType.Cube, pos, scl, trim);
            log.Add("Studio: tiled platform 2.8 x 2.2 m (2.5 cm glazed tiles) on a pale concrete body, mint edge trim");

            // ---- Two symmetric shallow water strips beside the desk ----
            var caustic = Gradient("StudioCaustic", 256, 256, (x, y) =>
            {
                float a = 0; float px = x * 2 * Mathf.PI, py = y * 2 * Mathf.PI;
                a += Mathf.Sin(px * 3 + Mathf.Sin(py * 2) * 1.3f); a += Mathf.Sin(py * 4 + Mathf.Sin(px * 3) * 1.1f); a += Mathf.Sin((px + py) * 2 + Mathf.Sin(px * 5) * .7f);
                float line = Mathf.Pow(Mathf.Clamp01(1 - Mathf.Abs(a) / 1.1f), 6);
                return new Color(1, 1, 1, .18f + .6f * line);
            }, true);
            var water = Mat("Studio shallow water", new Color(.50f, .83f, .86f, .55f), .95f, 0, caustic, true);
            water.mainTextureScale = new Vector2(1.2f, 3.6f);
            foreach (var (name, x) in new[] { ("Water strip L", -1.06f), ("Water strip R", 1.1f) })
            {
                Piece(root, name + " basin", PrimitiveType.Cube, new Vector3(x, .002f, .05f), new Vector3(.5f, .006f, 1.6f), mintTileMat);
                var w = Piece(root, name, PrimitiveType.Quad, new Vector3(x, .012f, .05f), new Vector3(.5f, 1.6f, 1), water, new Vector3(90, 0, 0));
                var r = w.GetComponent<Renderer>(); r.shadowCastingMode = ShadowCastingMode.Off;
                var s = w.gameObject.AddComponent<DaydreamScroll>(); s.speed = new Vector2(.006f, .011f); s.secondSpeed = new Vector2(.02f, -.015f); s.breathAlpha = .18f;
            }
            log.Add("Studio: two shallow water strips (x=-1.06 / +1.10) with drifting caustics on mint pool tiles");

            // ---- Far cloud sea: one flat layer below the horizon instead of near cloud puffs ----
            var rand = new System.Random(11); var grid = new float[33, 33];
            for (int i = 0; i < 33; i++) for (int j = 0; j < 33; j++) grid[i, j] = (float)rand.NextDouble();
            float Noise(float u, float v, int f)
            {
                float x = Mathf.Repeat(u, 1) * f, y = Mathf.Repeat(v, 1) * f; int x0 = (int)x, y0 = (int)y; float tx = x - x0, ty = y - y0;
                tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
                float a = grid[x0 % f, y0 % f], b = grid[(x0 + 1) % f, y0 % f], c = grid[x0 % f, (y0 + 1) % f], d = grid[(x0 + 1) % f, (y0 + 1) % f];
                return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
            }
            var cloudTex = Gradient("StudioCloudSea", 256, 256, (u, v) =>
            {
                float n = Noise(u, v, 4) * .5f + Noise(u, v, 8) * .3f + Noise(u, v, 16) * .2f;
                float a = Mathf.SmoothStep(.42f, .72f, n); var tint = Color.Lerp(C("F3DDEA"), Color.white, Mathf.SmoothStep(.5f, .85f, n));
                return new Color(tint.r, tint.g, tint.b, a);
            }, true);
            var seaMat = Mat("Studio cloud sea", new Color(1, 1, 1, .92f), 0, 0, cloudTex, true, true); seaMat.mainTextureScale = new Vector2(7, 7);
            var sea = Piece(root, "Far cloud sea", PrimitiveType.Quad, new Vector3(0, -2.4f, 6f), new Vector3(70, 70, 1), seaMat, new Vector3(90, 0, 0));
            sea.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            var drift = sea.gameObject.AddComponent<DaydreamScroll>(); drift.speed = new Vector2(.0016f, .0008f); drift.breathAlpha = .05f;
            log.Add("Studio: far cloud sea plane at y=-2.4 (70 m, drifting, fog-faded) replaces near cloud puffs");

            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.receiveShadows = r.sharedMaterial != seaMat && r.sharedMaterial != water;
        }
    }
}

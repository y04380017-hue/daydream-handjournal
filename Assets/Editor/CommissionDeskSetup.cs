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
    /// <summary>
    /// Authoring only: floating sky atelier, commission mailbox and postcard line on the saved Stage04-B/daydream scene.
    /// Room shell renderers are disabled (not deleted). Re-running removes only the two roots this tool created.
    /// Rollback: delete "Floating Sky Atelier" and "CommissionDesk", re-enable renderers listed in docs/commission/setup-log.txt,
    /// or restore E:/TapeGestureDemo/backups/handbook-before-commission-20261105.
    /// </summary>
    public static class CommissionDeskSetup
    {
        const string ScenePath = "Assets/Scenes/VintageJournalDesk.unity";
        const string Root = "Assets/Daydream/Floating/";
        const string SkyRootName = "Floating Sky Atelier", DeskRootName = "CommissionDesk";
        static readonly List<string> log = new List<string>();

        static Color C(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out var c); return c; }
        static Material Mat(string name, Color color, float smooth = .2f, bool unlit = false, Texture tex = null, bool transparent = false)
        {
            string path = Root + "Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/" + (unlit ? "Unlit" : "Lit"))); AssetDatabase.CreateAsset(m, path); }
            m.shader = Shader.Find("Universal Render Pipeline/" + (unlit ? "Unlit" : "Lit"));
            m.SetColor("_BaseColor", color); m.SetTexture("_BaseMap", tex);
            if (!unlit) { m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", 0); }
            if (transparent)
            {
                m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0); m.SetOverrideTag("RenderType", "Transparent");
                m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha); m.SetInt("_ZWrite", 0);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = (int)RenderQueue.Transparent;
            }
            EditorUtility.SetDirty(m); return m;
        }
        static Transform Child(Transform parent, string n, Vector3 p, Vector3 euler = default)
        { var t = new GameObject(n).transform; t.SetParent(parent, false); t.localPosition = p; t.localEulerAngles = euler; return t; }
        static Transform Shape(Transform parent, string n, PrimitiveType type, Vector3 p, Vector3 s, Material m, Vector3 euler = default)
        {
            var t = GameObject.CreatePrimitive(type).transform; t.name = n; t.SetParent(parent, false);
            t.localPosition = p; t.localScale = s; t.localEulerAngles = euler;
            Object.DestroyImmediate(t.GetComponent<Collider>()); var r = t.GetComponent<Renderer>(); r.sharedMaterial = m; return t;
        }

        [MenuItem("Vintage Journal/Commission/Build floating sky and commission desk")]
        public static void Apply()
        {
            log.Clear();
            var scene = EditorSceneManager.OpenScene(ScenePath);
            foreach (var n in new[] { SkyRootName, DeskRootName }) { var old = GameObject.Find(n); if (old) { Object.DestroyImmediate(old); log.Add("Removed previous tool root " + n); } }
            Directory.CreateDirectory(Root + "Materials"); Directory.CreateDirectory(Root + "Meshes"); Directory.CreateDirectory(Root + "Textures");
            AssetDatabase.Refresh();

            var sky = new GameObject(SkyRootName).transform;
            Sky(sky); HideRoomShell(sky); Clouds(sky); FloatingDecor(sky); UnifyMaterials(); Light();
            Desk();

            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            string docs = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "docs", "commission");
            Directory.CreateDirectory(docs); File.WriteAllLines(Path.Combine(docs, "setup-log.txt"), log);
            Debug.Log("[CommissionSetup] Done. " + log.Count + " log lines.");
        }

        // ---------- Sky ----------
        static void Sky(Transform root)
        {
            const int h = 256;
            var tex = new Texture2D(4, h, TextureFormat.RGB24, false);
            Color top = C("9FCDEB"), mid = C("D8ECF7"), horizon = C("FDF1EA"), below = C("F6D9D3");
            for (int y = 0; y < h; y++)
            {
                float v = y / (h - 1f); Color c;
                if (v > .62f) c = Color.Lerp(mid, top, Mathf.SmoothStep(0, 1, (v - .62f) / .38f));
                else if (v > .48f) c = Color.Lerp(horizon, mid, Mathf.SmoothStep(0, 1, (v - .48f) / .14f));
                else c = Color.Lerp(below, horizon, Mathf.SmoothStep(0, 1, v / .48f));
                for (int x = 0; x < 4; x++) tex.SetPixel(x, y, c);
            }
            string texPath = Root + "Textures/SkyGradient.png";
            File.WriteAllBytes(texPath, tex.EncodeToPNG()); Object.DestroyImmediate(tex); AssetDatabase.ImportAsset(texPath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(texPath); importer.wrapMode = TextureWrapMode.Clamp; importer.mipmapEnabled = false; importer.SaveAndReimport();
            var gradient = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

            // Inward-facing sphere so the sky reads the same from the fixed seat on every renderer.
            var temp = GameObject.CreatePrimitive(PrimitiveType.Sphere); var src = temp.GetComponent<MeshFilter>().sharedMesh;
            var mesh = Object.Instantiate(src); Object.DestroyImmediate(temp);
            var tris = mesh.triangles; for (int i = 0; i < tris.Length; i += 3) { int t = tris[i]; tris[i] = tris[i + 1]; tris[i + 1] = t; }
            mesh.triangles = tris; mesh.name = "Inward sky dome"; mesh.RecalculateNormals();
            string meshPath = Root + "Meshes/InwardSkyDome.asset"; AssetDatabase.DeleteAsset(meshPath); AssetDatabase.CreateAsset(mesh, meshPath);
            var dome = Child(root, "Sky dome", new Vector3(0, 1.1f, 0)); dome.localScale = Vector3.one * 60;
            dome.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = dome.gameObject.AddComponent<MeshRenderer>(); r.sharedMaterial = Mat("Sky gradient", Color.white, 0, true, gradient);
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            var cam = Object.FindFirstObjectByType<DeskCamera>()?.GetComponent<Camera>();
            if (cam) { cam.farClipPlane = Mathf.Max(cam.farClipPlane, 45); cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = C("D8ECF7"); EditorUtility.SetDirty(cam); }
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogColor = C("E4F1F8");
            RenderSettings.fogStartDistance = 7; RenderSettings.fogEndDistance = 34;
            log.Add("Sky dome r=30, gradient " + texPath + ", linear fog 7-34m, camera far clip >=45");
        }

        static void HideRoomShell(Transform root)
        {
            var room = GameObject.Find("RoomAndWindow")?.transform;
            if (!room) { log.Add("RoomAndWindow not found; nothing hidden."); return; }
            var keep = new List<Transform>();
            foreach (Transform t in room) if (t.name.StartsWith("Field study") || t.name == "BotanicalFrame") keep.Add(t);
            foreach (var r in room.GetComponentsInChildren<Renderer>(true))
            {
                if (keep.Any(k => r.transform.IsChildOf(k))) continue;
                if (r.enabled) { r.enabled = false; log.Add("Hidden room renderer: " + PathOf(r.transform)); }
            }
            // Frames become floating keepsakes; they carry no colliders.
            foreach (var k in keep)
            {
                foreach (var c in k.GetComponentsInChildren<Collider>(true)) { c.enabled = false; log.Add("Disabled decorative collider: " + PathOf(c.transform)); }
                var f = k.gameObject.GetComponent<DaydreamFloat>();
                if (!f) f = k.gameObject.AddComponent<DaydreamFloat>();
                f.bobMetres = .012f; f.bobPeriod = 7.5f; f.swayDegrees = 1.2f;
                log.Add("Floating frame: " + PathOf(k));
            }
        }
        static string PathOf(Transform t) => t.parent ? PathOf(t.parent) + "/" + t.name : t.name;

        static Transform Cloud(Transform parent, string n, Vector3 p, float size, Material m, float drift, int seed)
        {
            var c = Child(parent, n, p); var rand = new System.Random(seed);
            int puffs = 6 + rand.Next(4);
            for (int i = 0; i < puffs; i++)
            {
                float a = (float)rand.NextDouble() * Mathf.PI * 2, d = (float)rand.NextDouble() * .9f;
                float s = size * (.45f + (float)rand.NextDouble() * .55f);
                var puff = Shape(c, "Puff", PrimitiveType.Sphere, new Vector3(Mathf.Cos(a) * d * size, (float)rand.NextDouble() * .25f * size, Mathf.Sin(a) * d * size * .6f),
                    new Vector3(s * 1.3f, s * .72f, s), m);
                var r = puff.GetComponent<Renderer>(); r.shadowCastingMode = ShadowCastingMode.Off;
            }
            var f = c.gameObject.AddComponent<DaydreamFloat>();
            f.bobMetres = size * .06f; f.bobPeriod = 9 + seed % 5; f.swayDegrees = 0; f.driftMetresPerSecond = drift; f.driftSpan = 3 + size * 2;
            return c;
        }

        static void Clouds(Transform root)
        {
            var white = Mat("Cloud cotton", C("FFFFFF"), .05f);
            var blush = Mat("Cloud blush", C("FBEDEA"), .05f);
            var mint = Mat("Cloud mint", C("EEF8F4"), .05f);
            var clouds = Child(root, "Clouds", Vector3.zero);
            // Cushion under the floating desk.
            Cloud(clouds, "Desk cushion cloud", new Vector3(.05f, .18f, .05f), .55f, white, 0, 3);
            Cloud(clouds, "Desk cushion cloud B", new Vector3(-.55f, .08f, -.3f), .38f, blush, 0, 7);
            Cloud(clouds, "Desk cushion cloud C", new Vector3(.65f, .12f, -.25f), .34f, mint, 0, 11);
            // Mid and far layers. All clouds are behind or below the fixed seat line; none can block storage.
            var spots = new (Vector3 p, float s)[]
            {
                (new Vector3(-2.6f, .6f, 3.2f), .9f), (new Vector3(2.4f, .9f, 3.6f), 1.0f), (new Vector3(-.4f, -.4f, 4.4f), 1.3f),
                (new Vector3(-5.5f, 1.8f, 7.5f), 1.8f), (new Vector3(5.2f, 2.2f, 8.0f), 2.0f), (new Vector3(.8f, 2.9f, 10.5f), 2.4f),
                (new Vector3(-8.5f, .2f, 9.5f), 2.2f), (new Vector3(8.8f, -.2f, 6.5f), 2.0f), (new Vector3(-1.8f, -1.6f, 6.0f), 2.2f),
                (new Vector3(3.5f, -1.2f, 2.2f), 1.2f), (new Vector3(-3.6f, -1.0f, 1.6f), 1.1f), (new Vector3(-3.9f, 2.6f, 5.0f), 1.2f)
            };
            for (int i = 0; i < spots.Length; i++)
                Cloud(clouds, "Sky cloud " + (i + 1), spots[i].p, spots[i].s, i % 3 == 0 ? blush : i % 3 == 1 ? white : mint, .05f + .02f * (i % 3), 20 + i);
            log.Add("Clouds: 3 desk cushions + " + spots.Length + " drifting sky clouds (no colliders, no shadows)");
        }

        static void FloatingDecor(Transform root)
        {
            var paper = Mat("Floating paper", C("FFFDF8"), .1f);
            var peach = Mat("Floating peach", C("F2B8A6"), .2f);
            var sky = Mat("Floating sky", C("A9D3EA"), .2f);
            var mint = Mat("Floating mint", C("A6DCCB"), .2f);
            var lilac = Mat("Floating lilac", C("CFC5EA"), .2f);
            var decor = Child(root, "Floating keepsakes", Vector3.zero);
            // Paper birds and envelopes drift behind the back fixtures (z >= .9), never in front of interactables.
            var birds = new[] { (new Vector3(-.85f, 1.32f, 1.05f), sky, 18f), (new Vector3(.95f, 1.42f, 1.25f), peach, -24f), (new Vector3(.2f, 1.62f, 1.6f), lilac, 8f), (new Vector3(-1.35f, 1.12f, 1.7f), mint, 30f) };
            int i = 0;
            foreach (var (p, m, yaw) in birds)
            {
                var bird = Child(decor, "Paper bird " + (++i), p, new Vector3(0, yaw, 0));
                Shape(bird, "Body", PrimitiveType.Cube, Vector3.zero, new Vector3(.018f, .012f, .07f), m, new Vector3(0, 0, 45));
                Shape(bird, "Wing L", PrimitiveType.Cube, new Vector3(-.03f, .008f, 0), new Vector3(.06f, .002f, .04f), paper, new Vector3(0, 0, 22));
                Shape(bird, "Wing R", PrimitiveType.Cube, new Vector3(.03f, .008f, 0), new Vector3(.06f, .002f, .04f), paper, new Vector3(0, 0, -22));
                var f = bird.gameObject.AddComponent<DaydreamFloat>(); f.bobMetres = .03f; f.bobPeriod = 4.5f + i; f.swayDegrees = 6;
            }
            var envelopes = new[] { (new Vector3(-.55f, 1.5f, 1.35f), peach), (new Vector3(.62f, 1.25f, 1.0f), mint), (new Vector3(1.25f, 1.05f, 1.5f), sky) };
            i = 0;
            foreach (var (p, m) in envelopes)
            {
                var env = Child(decor, "Floating envelope " + (++i), p, new Vector3(-8, 12 * i, 6 - 4 * i));
                Shape(env, "Envelope", PrimitiveType.Cube, Vector3.zero, new Vector3(.11f, .075f, .002f), paper);
                Shape(env, "Flap seal", PrimitiveType.Cylinder, new Vector3(0, 0, -.002f), new Vector3(.016f, .001f, .016f), m, new Vector3(90, 0, 0));
                var f = env.gameObject.AddComponent<DaydreamFloat>(); f.bobMetres = .025f; f.bobPeriod = 6 + i; f.swayDegrees = 5;
            }
            // Loose paper stars sprinkled low around the desk.
            var rand = new System.Random(5);
            for (int s = 0; s < 14; s++)
            {
                var p = new Vector3(-1.4f + (float)rand.NextDouble() * 2.8f, .15f + (float)rand.NextDouble() * .5f, .8f + (float)rand.NextDouble() * 1.4f);
                var star = Shape(decor, "Paper confetti " + (s + 1), PrimitiveType.Cube, p, new Vector3(.014f, .014f, .001f), new[] { peach, sky, mint, lilac }[s % 4], new Vector3(0, 0, 45));
                var f = star.gameObject.AddComponent<DaydreamFloat>(); f.bobMetres = .02f; f.bobPeriod = 3 + s % 5; f.swayDegrees = 25;
            }
            log.Add("Floating keepsakes: 4 paper birds, 3 envelopes, 14 confetti (decorative only)");
        }

        static void UnifyMaterials()
        {
            // Any renderer (active or not) still on a retro warm-brown material switches to its daydream counterpart.
            Material L(string n) => AssetDatabase.LoadAssetAtPath<Material>("Assets/Daydream/Materials/" + n + ".mat");
            var map = new Dictionary<string, Material>
            {
                {"Walnut", L("Honey maple")}, {"DarkWalnut", L("Honey maple")}, {"AgedBrass", L("Satin brass")}, {"OxbloodLeather", L("Apricot")},
                {"Terracotta", L("Apricot")}, {"BookOchre", L("Pollen")}, {"WarmPlaster", L("Milk paint")}, {"Linen", L("Sage cloth")},
                {"PuppyCaramel", L("Apricot")}, {"BirdSage", L("Pistachio")}, {"SageEnamel", L("Pistachio")}, {"LaceThread", L("Milk paint")}
            };
            int swapped = 0;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (r.transform.root.name == SkyRootName) continue;
                var mats = r.sharedMaterials; bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] && map.TryGetValue(mats[i].name, out var replacement) && replacement) { mats[i] = replacement; changed = true; swapped++; }
                if (changed) { r.sharedMaterials = mats; log.Add("Daydream material on " + PathOf(r.transform)); }
            }
            log.Add("Retro materials swapped: " + swapped);
        }

        static void Light()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = C("A9BCCC"); RenderSettings.ambientEquatorColor = C("A3A2A6"); RenderSettings.ambientGroundColor = C("9A8F92");
            var grade = GameObject.Find("WarmColorGrade")?.GetComponent<Volume>();
            if (grade) { grade.weight = .08f; log.Add("WarmColorGrade weight .18 -> .08 for the airy palette"); }
            log.Add("Ambient trilight raised for open sky (sun unchanged).");
        }

        // ---------- Commission mailbox and postcard line ----------
        static void Desk()
        {
            var cream = Mat("Mailbox cream", C("FFF8EE"), .35f);
            var mint = Mat("Mailbox mint", C("9FD8C6"), .35f);
            var peach = Mat("Mailbox peach", C("F1B39F"), .35f);
            var pollen = Mat("Mailbox pollen", C("F4D47A"), .35f);
            var ink = Mat("Mailbox slot", C("5A6B7A"), .2f);
            var paper = Mat("Postcard paper", C("FFFFFF"), .1f);
            var photo = Mat("Postcard photo", Color.white, 0, true);
            var cloud = Mat("Cloud cotton", C("FFFFFF"), .05f);
            var string_ = Mat("Postcard string", C("C9B9A6"), .1f);

            var workspace = Object.FindFirstObjectByType<WorkspaceGameplayController>();
            if (!workspace) throw new InvalidOperationException("WorkspaceGameplayController missing; install Stage04-B first.");
            var root = new GameObject(DeskRootName).transform;
            var controller = root.gameObject.AddComponent<CommissionDeskController>(); controller.workspace = workspace;

            // Floating mailbox beside the desk's right edge: visible from the seat, outside every storage reach.
            var box = Child(root, "Floating mailbox", new Vector3(.72f, .93f, .42f), new Vector3(0, -18, 0));
            Cloud(box, "Mailbox cloud", new Vector3(0, -.13f, 0), .09f, cloud, 0, 41).GetComponent<DaydreamFloat>().bobMetres = .004f;
            Shape(box, "Post", PrimitiveType.Cylinder, new Vector3(0, -.075f, 0), new Vector3(.018f, .05f, .018f), cream);
            Shape(box, "Body", PrimitiveType.Cube, new Vector3(0, .03f, 0), new Vector3(.13f, .11f, .10f), mint);
            Shape(box, "Roof", PrimitiveType.Cylinder, new Vector3(0, .085f, 0), new Vector3(.13f, .05f, .10f), peach, new Vector3(90, 0, 0)).localScale = new Vector3(.13f, .05f, .065f);
            Shape(box, "Slot", PrimitiveType.Cube, new Vector3(0, .055f, -.0505f), new Vector3(.07f, .008f, .002f), ink);
            Shape(box, "Door", PrimitiveType.Cube, new Vector3(0, .015f, -.0505f), new Vector3(.09f, .05f, .002f), cream);
            var flag = Child(box, "Flag pivot", new Vector3(.068f, .02f, .02f));
            Shape(flag, "Flag arm", PrimitiveType.Cube, new Vector3(0, .035f, 0), new Vector3(.006f, .07f, .006f), pollen);
            Shape(flag, "Flag", PrimitiveType.Cube, new Vector3(0, .062f, -.014f), new Vector3(.004f, .022f, .03f), peach);
            var envelope = Child(box, "Unread envelope", new Vector3(0, .17f, 0), Vector3.zero);
            Shape(envelope, "Envelope", PrimitiveType.Cube, Vector3.zero, new Vector3(.09f, .06f, .003f), paper);
            Shape(envelope, "Seal", PrimitiveType.Cylinder, new Vector3(0, 0, -.003f), new Vector3(.018f, .001f, .018f), peach, new Vector3(90, 0, 0));
            var collider = box.gameObject.AddComponent<BoxCollider>(); collider.center = new Vector3(0, .05f, 0); collider.size = new Vector3(.17f, .32f, .14f);
            var label = Child(box, "Stamp counter", new Vector3(0, -.032f, -.052f)).gameObject.AddComponent<TextMesh>();
            var reference = Object.FindObjectsByType<TextMesh>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(t => t != label && t.font);
            if (reference) { label.font = reference.font; label.GetComponent<MeshRenderer>().sharedMaterial = reference.GetComponent<MeshRenderer>().sharedMaterial; }
            label.text = "邮票 × 0"; label.characterSize = .0035f; label.fontSize = 64; label.anchor = TextAnchor.MiddleCenter; label.color = C("4E6170");
            controller.mailboxRoot = box; controller.mailboxCollider = collider; controller.flag = flag; controller.floatingEnvelope = envelope; controller.stampCounter = label;

            // Postcard line floating behind the back fixtures; photos of sent work hang here.
            var line = Child(root, "Postcard line", new Vector3(-.22f, 1.22f, .66f), new Vector3(-12, 0, 0));
            Cloud(line, "Line cloud L", new Vector3(-.52f, 0, 0), .07f, cloud, 0, 51);
            Cloud(line, "Line cloud R", new Vector3(.52f, 0, 0), .07f, cloud, 0, 53);
            Shape(line, "String", PrimitiveType.Cylinder, new Vector3(0, .005f, 0), new Vector3(.0025f, .52f, .0025f), string_, new Vector3(0, 0, 90));
            var frames = new List<Renderer>();
            for (int i = 0; i < 6; i++)
            {
                var card = Child(line, "Postcard " + (i + 1), new Vector3(-.375f + i * .15f, -.005f, 0), new Vector3(0, 0, (i % 2 == 0 ? 2.5f : -3f)));
                Shape(card, "Peg", PrimitiveType.Cube, new Vector3(0, .002f, -.002f), new Vector3(.008f, .022f, .006f), i % 2 == 0 ? mint : peach);
                Shape(card, "Card", PrimitiveType.Cube, new Vector3(0, -.066f, .001f), new Vector3(.096f, .124f, .0015f), paper);
                var pic = Shape(card, "Photo", PrimitiveType.Quad, new Vector3(0, -.068f, -.0002f), new Vector3(.084f, .112f, 1), photo);
                pic.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off; frames.Add(pic.GetComponent<Renderer>());
                var f = card.gameObject.AddComponent<DaydreamFloat>(); f.bobMetres = .002f; f.bobPeriod = 5 + i * .7f; f.swayDegrees = 2.5f;
                card.gameObject.SetActive(false);
            }
            controller.postcardFrames = frames.ToArray();
            log.Add("CommissionDesk: mailbox at (.72,.93,.42) collider .17x.32x.14; postcard line at (-.22,1.22,.66) with 6 frames");
        }
    }
}

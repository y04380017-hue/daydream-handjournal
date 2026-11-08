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
    /// Studio P3: the floating mailbox gets a pink mini-CRT "letter terminal" look (old mailbox renderers are only
    /// disabled; the collider, flag, unread envelope and click logic are untouched), plus two distant easter eggs
    /// in the cloud sea. Rebuilds only "Letter terminal visual" and "Daydream Studio P3".
    /// Rollback: menu "Restore mailbox look", or backup handbook-before-studio-p3-20261106.
    /// </summary>
    public static partial class DaydreamBrightPass
    {
        const string P3Root = "Daydream Studio P3", TerminalName = "Letter terminal visual";
        static readonly string[] MailboxParts = { "Post", "Body", "Roof", "Slot", "Door" };

        [MenuItem("Vintage Journal/Daydream/Apply studio P3")]
        public static void ApplyStudioP3Menu()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath); log.Clear(); ApplyStudioP3();
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Directory.CreateDirectory(Docs); File.AppendAllLines(Path.Combine(Docs, "apply-log.txt"), log);
        }

        [MenuItem("Vintage Journal/Daydream/Restore mailbox look")]
        public static void RestoreMailboxMenu()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var box = GameObject.Find("CommissionDesk/Floating mailbox")?.transform; if (!box) return;
            var t = box.Find(TerminalName); if (t) Object.DestroyImmediate(t.gameObject);
            foreach (var n in MailboxParts) { var p = box.Find(n); if (p) p.GetComponent<Renderer>().enabled = true; }
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        }

        static Transform LocalPiece(Transform parent, string name, PrimitiveType type, Vector3 local, Vector3 scale, Material m, Vector3 euler = default)
        {
            var go = GameObject.CreatePrimitive(type); Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name; go.transform.SetParent(parent, false); go.transform.localPosition = local; go.transform.localScale = scale; go.transform.localEulerAngles = euler;
            go.GetComponent<Renderer>().sharedMaterial = m; return go.transform;
        }

        static void ApplyStudioP3()
        {
            // ---- Pink CRT letter terminal ----
            var box = GameObject.Find("CommissionDesk/Floating mailbox")?.transform;
            if (!box) { log.Add("P3: mailbox not found, terminal skipped"); }
            else
            {
                var old = box.Find(TerminalName); if (old) Object.DestroyImmediate(old.gameObject);
                foreach (var n in MailboxParts) { var p = box.Find(n); if (p) p.GetComponent<Renderer>().enabled = false; }
                var shell = Mat("Terminal shell", C("F6C9D6"), .45f); var bezel = Mat("Terminal bezel", C("FDE9EF"), .4f);
                var keys = Mat("Terminal keys", C("FFF8F1"), .3f); var shadow = Mat("Terminal vent", C("D9A6B8"), .3f);
                var screenTex = Gradient("TerminalScreen", 256, 192, (u, v) =>
                {
                    var bg = Color.Lerp(C("8FA3E6"), C("C9B9F2"), v); float scan = Mathf.Repeat(v * 48, 1) < .18f ? .93f : 1f;
                    // Envelope icon in the middle.
                    float x = (u - .5f) * 2, y = (v - .5f) * 2; bool body = Mathf.Abs(x) < .42f && Mathf.Abs(y) < .30f;
                    bool flap = body && y > .30f - (.42f - Mathf.Abs(x)) * .72f && y > -.05f;
                    var c = bg; if (body) c = Color.Lerp(c, Color.white, .9f); if (flap) c = Color.Lerp(c, C("F3A6C4"), .55f);
                    float heart = Mathf.Pow(x * 1.6f, 2) + Mathf.Pow((y + .05f) * 1.9f - Mathf.Sqrt(Mathf.Abs(x * 1.6f)) * .6f, 2);
                    if (heart < .05f) c = C("FF8FB0");
                    return c * scan;
                });
                var screenMat = Mat("Terminal screen", Color.white, 0, 0, screenTex, false, true);
                var term = new GameObject(TerminalName).transform; term.SetParent(box, false);
                LocalPiece(term, "CRT shell", PrimitiveType.Cube, new Vector3(0, .035f, .005f), new Vector3(.15f, .125f, .12f), shell);
                LocalPiece(term, "CRT back taper", PrimitiveType.Cube, new Vector3(0, .03f, .07f), new Vector3(.11f, .09f, .05f), shell);
                LocalPiece(term, "Screen bezel", PrimitiveType.Cube, new Vector3(0, .04f, -.056f), new Vector3(.128f, .1f, .01f), bezel);
                var scr = LocalPiece(term, "Screen", PrimitiveType.Quad, new Vector3(0, .042f, -.0615f), new Vector3(.104f, .078f, 1), screenMat);
                scr.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                for (int i = 0; i < 3; i++) LocalPiece(term, "Vent", PrimitiveType.Cube, new Vector3(.058f, .085f - i * .012f, .03f), new Vector3(.004f, .004f, .05f), shadow);
                LocalPiece(term, "Stand", PrimitiveType.Cube, new Vector3(0, -.034f, .005f), new Vector3(.10f, .014f, .09f), bezel);
                var kb = LocalPiece(term, "Keyboard", PrimitiveType.Cube, new Vector3(0, -.045f, -.07f), new Vector3(.13f, .012f, .045f), shell, new Vector3(6, 0, 0));
                for (int r = 0; r < 2; r++) for (int k = 0; k < 7; k++)
                    LocalPiece(kb, "Key", PrimitiveType.Cube, new Vector3(-.39f + k * .13f, .7f, -.18f + r * .36f), new Vector3(.1f, .6f, .26f), keys);
                LocalPiece(term, "Power light", PrimitiveType.Sphere, new Vector3(.052f, -.004f, -.061f), new Vector3(.006f, .006f, .003f), Mat("Terminal light", C("9EDFCB"), .6f));
                var screenComp = term.gameObject.AddComponent<LetterTerminalScreen>(); screenComp.screen = scr.GetComponent<Renderer>();
                var label = box.Find("Stamp counter"); if (label) label.localPosition = new Vector3(0, -.072f, -.07f);
                log.Add("P3: mailbox re-skinned as a pink mini-CRT letter terminal (old parts disabled, collider/flag/envelope/click unchanged)");
            }

            // ---- Distant easter eggs floating in the cloud sea ----
            var oldRoot = GameObject.Find(P3Root); if (oldRoot) Object.DestroyImmediate(oldRoot);
            var root = new GameObject(P3Root).transform;
            // Place eggs by seated-view screen position so they land in open sky, not behind fixtures.
            var deskCam = Object.FindFirstObjectByType<DeskCamera>(); var lensCam = deskCam.GetComponent<Camera>();
            var savedPos = lensCam.transform.position; var savedRot = lensCam.transform.rotation; float savedFov = lensCam.fieldOfView;
            lensCam.transform.position = deskCam.profile.seatedPosition; lensCam.transform.LookAt(deskCam.profile.seatedTarget); lensCam.fieldOfView = deskCam.profile.seatedFieldOfView; lensCam.aspect = 16f / 9f;
            Vector3 AtScreen(float vx, float vy, float distance) => lensCam.ViewportPointToRay(new Vector3(vx, vy, 0)).GetPoint(distance);
            Vector3 chairSpot = AtScreen(.09f, .60f, 8f), monitorSpot = AtScreen(.91f, .52f, 7.5f);
            lensCam.transform.SetPositionAndRotation(savedPos, savedRot); lensCam.fieldOfView = savedFov; lensCam.ResetAspect();
            var white = Mat("Easter white plastic", C("FAF6F4"), .35f);
            var chair = new GameObject("Floating white chair").transform; chair.SetParent(root, false);
            chair.position = chairSpot; chair.rotation = Quaternion.Euler(-6, 28, 8); chair.localScale = Vector3.one * .9f;
            LocalPiece(chair, "Seat", PrimitiveType.Cube, new Vector3(0, .45f, 0), new Vector3(.46f, .05f, .44f), white);
            for (int i = 0; i < 5; i++) LocalPiece(chair, "Back slat", PrimitiveType.Cube, new Vector3(-.18f + i * .09f, .72f, .2f), new Vector3(.05f, .45f, .03f), white, new Vector3(-8, 0, 0));
            LocalPiece(chair, "Back rail", PrimitiveType.Cube, new Vector3(0, .95f, .23f), new Vector3(.48f, .05f, .04f), white, new Vector3(-8, 0, 0));
            foreach (var (x, z) in new[] { (-.2f, -.19f), (.2f, -.19f), (-.2f, .19f), (.2f, .19f) })
                LocalPiece(chair, "Leg", PrimitiveType.Cube, new Vector3(x, .22f, z), new Vector3(.04f, .45f, .04f), white);
            var cf = chair.gameObject.AddComponent<DaydreamFloat>(); cf.bobMetres = .08f; cf.bobPeriod = 9; cf.swayDegrees = 3;

            var shellMat = Mat("Easter monitor shell", C("D9D5EE"), .4f);
            var starTex = Gradient("EasterStarScreen", 128, 96, (u, v) =>
            {
                var c = Color.Lerp(C("3E5A8A"), C("6F86C8"), v);
                float n = Mathf.Sin(u * 91.7f + v * 13.1f) * Mathf.Sin(u * 17.3f - v * 77.9f); if (n > .97f) c = Color.white;
                return c;
            });
            var monitor = new GameObject("Floating cloud monitor").transform; monitor.SetParent(root, false);
            monitor.position = monitorSpot; monitor.rotation = Quaternion.Euler(4, -34, -6); monitor.localScale = Vector3.one * 1.2f;
            LocalPiece(monitor, "Monitor shell", PrimitiveType.Cube, new Vector3(0, .25f, 0), new Vector3(.5f, .4f, .06f), shellMat);
            LocalPiece(monitor, "Starry screen", PrimitiveType.Quad, new Vector3(0, .25f, -.031f), new Vector3(.44f, .34f, 1), Mat("Easter starry screen", Color.white, 0, 0, starTex, false, true));
            LocalPiece(monitor, "Neck", PrimitiveType.Cube, new Vector3(0, .02f, .02f), new Vector3(.06f, .08f, .04f), shellMat);
            LocalPiece(monitor, "Keyboard", PrimitiveType.Cube, new Vector3(0, -.03f, -.2f), new Vector3(.46f, .025f, .16f), shellMat, new Vector3(8, 0, 0));
            var mf = monitor.gameObject.AddComponent<DaydreamFloat>(); mf.bobMetres = .1f; mf.bobPeriod = 9; mf.swayDegrees = 2.5f; mf.phase = .5f;
            foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
            log.Add("P3: distant easter eggs — white chair at viewport (.09,.60) and starry cloud monitor at (.91,.52), 7.5–8 m away, floating, no colliders");
        }
    }
}

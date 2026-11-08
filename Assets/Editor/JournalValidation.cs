using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace VintageJournal.Editor
{
    /// <summary>Actual Play Mode integration tests. Native input and process restart are separate probes.</summary>
    [InitializeOnLoad]
    public static class JournalValidation
    {
        const string Pending = "VintageJournal.Stage2Validation.Pending";
        static readonly List<Result> results = new List<Result>();
        static readonly List<string> errors = new List<string>();
        static readonly Stack<IEnumerator> routines = new Stack<IEnumerator>();
        static JournalController journal;
        static DeskUI desk;
        static DeskInteraction interaction;
        static DeskCamera rig;
        static Camera lens;
        static bool preparing, running, finishing;
        static double preparationTime, started;
        static int lastFrame;
        static string root;
        static DateTime startedUtc;
        static Vector3 deskPosition;
        static Quaternion deskRotation;
        static float deskFov;

        [Serializable] public sealed class Result { public string test, name, detail; public bool passed; }
        [Serializable] public sealed class Report
        {
            public string timestampUtc, mode, inputScope, screenshotScope, unityVersion, gpu, graphicsApi, savePath;
            public int width, height, renderedFrames;
            public bool passed, actualPlayMode;
            public Result[] checks;
            public string[] consoleErrors, screenshots;
        }

        static JournalValidation()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += PlayChanged;
            if (SessionState.GetBool(Pending, false))
            {
                Application.logMessageReceived += CaptureLog;
                if (!EditorApplication.isPlayingOrWillChangePlaymode)
                { preparing = true; preparationTime = EditorApplication.timeSinceStartup; }
            }
        }

        [MenuItem("Vintage Journal/Stage 2/Validate journal in Play Mode")]
        public static void Begin()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Start in Edit Mode.");
            if (!Environment.GetCommandLineArgs().Contains("--journal-validation"))
                throw new InvalidOperationException("Use a dedicated validation Editor with --journal-validation to isolate player saves.");
            root = Directory.GetParent(Application.dataPath).FullName;
            Directory.CreateDirectory(Path.Combine(root, "docs/stage2-logs"));
            Directory.CreateDirectory(Path.Combine(root, "docs/stage2-screenshots"));
            if (!JournalDataTests.RunDataChecks())
                throw new InvalidOperationException("Journal data checks failed; see docs/stage2-logs/data-tests.json.");
            results.Clear(); errors.Clear();
            SessionState.SetBool(Pending, true);
            Application.logMessageReceived -= CaptureLog;
            Application.logMessageReceived += CaptureLog;
            EditorSceneManager.OpenScene("Assets/Scenes/VintageJournalDesk.unity", OpenSceneMode.Single);
            ConfigureFullHd();
            preparing = true;
            preparationTime = EditorApplication.timeSinceStartup;
        }

        static void PlayChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Pending, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                root = Directory.GetParent(Application.dataPath).FullName;
                results.Clear(); errors.Clear(); routines.Clear();
                started = EditorApplication.timeSinceStartup; startedUtc = DateTime.UtcNow;
                lastFrame = -1; running = true; finishing = false;
                Application.logMessageReceived -= CaptureLog;
                Application.logMessageReceived += CaptureLog;
                routines.Push(Execute());
            }
            else if (state == PlayModeStateChange.EnteredEditMode && !finishing)
            { Check("Harness", "Unexpected Play exit", false, "Validation did not complete."); Finish(); }
        }

        static void CaptureLog(string message, string stack, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message + "\n" + stack); }

        static void Check(string id, string name, bool pass, string detail)
        {
            results.Add(new Result { test = id, name = name, passed = pass, detail = detail });
            Debug.Log("[JournalValidation] " + id + " " + (pass ? "PASS " : "FAIL ") + name + " " + detail);
        }

        static void Tick()
        {
            if (preparing)
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                { preparationTime = EditorApplication.timeSinceStartup; return; }
                if (EditorApplication.timeSinceStartup - preparationTime < 3) return;
                preparing = false; EditorApplication.isPlaying = true; return;
            }
            if (!running || finishing || !EditorApplication.isPlaying || Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            if (EditorApplication.timeSinceStartup - started > 180)
            { Check("Harness", "Timeout", false, "180 seconds exceeded"); Finish(); return; }
            try
            {
                while (routines.Count > 0)
                {
                    var routine = routines.Peek();
                    if (!routine.MoveNext()) { routines.Pop(); continue; }
                    if (routine.Current is IEnumerator child) { routines.Push(child); continue; }
                    return;
                }
                Finish();
            }
            catch (Exception error)
            { errors.Add(error.ToString()); Check("Harness", "Exception", false, error.Message); Finish(); }
        }

        static IEnumerator Wait(double seconds)
        {
            double until = EditorApplication.timeSinceStartup + seconds;
            while (EditorApplication.timeSinceStartup < until) yield return null;
        }
        static IEnumerator Idle(double timeout = 5)
        {
            double until = EditorApplication.timeSinceStartup + timeout;
            while ((journal.IsBusy || rig.IsTransitioning) && EditorApplication.timeSinceStartup < until) yield return null;
            if (journal.IsBusy || rig.IsTransitioning) throw new TimeoutException("Journal did not settle; mode=" + journal.Mode);
            yield return null;
        }

        static IEnumerator Execute()
        {
            yield return Wait(.6);
            Check("Harness", "Actual Play", Application.isPlaying && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null,
                SystemInfo.graphicsDeviceType + " / " + SystemInfo.graphicsDeviceName);
            Check("Harness", "Actual resolution", Screen.width == 1920 && Screen.height == 1080, Screen.width + "x" + Screen.height);
            journal = UnityEngine.Object.FindFirstObjectByType<JournalController>();
            desk = UnityEngine.Object.FindFirstObjectByType<DeskUI>();
            interaction = UnityEngine.Object.FindFirstObjectByType<DeskInteraction>();
            rig = UnityEngine.Object.FindFirstObjectByType<DeskCamera>();
            lens = Camera.main;
            if (!journal || !desk || !interaction || !rig || !lens) throw new InvalidOperationException("Missing journal/desk runtime components.");
            Check("Harness", "24 paper definitions", journal.Catalog && journal.Catalog.papers.Length >= 24, "Data-driven paper catalog");
            Check("Harness", "24 independent page instances", journal.Document.pages.Length >= 24 && journal.Document.SpreadCount >= 12 &&
                journal.Document.pages.Select(page => page.pageId).Distinct().Count() == journal.Document.pages.Length, "Unique page instance IDs, twelve spreads");
            string persistent = Path.GetFullPath(Application.persistentDataPath) + Path.DirectorySeparatorChar;
            Check("Harness", "Isolated writable save", Path.GetFullPath(journal.SavePath).StartsWith(persistent, StringComparison.OrdinalIgnoreCase) &&
                journal.SavePath.Contains("Stage2Validation"), journal.SavePath);
            if (!journal.SavePath.Contains("Stage2Validation")) throw new InvalidOperationException("Refusing test writes to a normal player save.");
            desk.Begin();
            yield return Wait(.4);
            deskPosition = lens.transform.position; deskRotation = lens.transform.rotation; deskFov = lens.fieldOfView;
            Physics.SyncTransforms();
            var item = UnityEngine.Object.FindObjectsByType<DeskItem>(FindObjectsSortMode.None).First(value => value.kind == DeskItemKind.Journal);
            Vector2 hitPoint;
            bool hasPoint = FindHit(item, out hitPoint);
            bool clicked = hasPoint && interaction.TryActivateAt(hitPoint);
            Check("T01", "Journal ray enters editing", clicked && journal.IsEditing, "Actual physics ray; mode=" + journal.Mode);
            Check("T01", "Repeated entry rejected", !journal.EnterEditing(), "No competing camera transitions");
            yield return Idle();
            Check("T02", "Editor camera settles", journal.Mode == JournalMode.EditingMode && !rig.IsTransitioning &&
                Vector3.Distance(lens.transform.position, deskPosition) > .1f, lens.transform.position.ToString());
            Check("T02", "Both complete pages in view", BothPagesVisible(), "All eight normalized page corners in view, with top/bottom UI clearance");
            var pageView = UnityEngine.Object.FindFirstObjectByType<JournalPageView>();
            bool coordinatesRoundtrip = pageView != null;
            foreach (JournalSide side in new[] { JournalSide.Left, JournalSide.Right })
            {
                Vector2 recovered;
                var uv = new Vector2(.17f, .81f);
                coordinatesRoundtrip &= pageView && pageView.TryWorldToNormalized(side, pageView.WorldFromNormalized(side, uv), out recovered)
                    && Vector2.Distance(recovered, uv) < .0001f;
            }
            Check("T02", "Independent normalized page coordinates", coordinatesRoundtrip && pageView.GetSurface(JournalSide.Left) != pageView.GetSurface(JournalSide.Right),
                "Each page maps its own (0,0) bottom-left / (1,1) top-right through a distinct transform.");
            bool aspectCorrect = true;
            var artwork = pageView.leftRenderer.transform.Find("LeftPaperArtwork").GetComponent<MeshFilter>();
            foreach (var definition in journal.Catalog.papers)
            {
                pageView.SetPaper(JournalSide.Left, definition.texture);
                var bounds = artwork.sharedMesh.bounds.size;
                float expectedAspect = definition.texture.width / (float)definition.texture.height;
                aspectCorrect &= Mathf.Abs(bounds.x / bounds.z - expectedAspect) < .0001f && bounds.x <= .3931f && bounds.z <= .5201f;
            }
            journal.RefreshPages();
            Check("T07/T08", "All paper proportions fit without stretch", aspectCorrect,
                "24 original image ratios are letterboxed inside the physical paper bounds.");
            Capture(null); yield return Wait(3); Capture("edit.png");

            while (journal.Document.currentSpread > 0) { journal.Turn(-1); yield return Idle(); }
            Check("T05", "First spread clamps", !journal.Turn(-1) && journal.Document.currentSpread == 0, "Cannot go before pages1–2");
            bool firstTurn = journal.Turn(1), repeatedTurn = journal.Turn(1);
            Check("T06", "Flip rejects repeat", firstTurn && !repeatedTurn && journal.IsBusy, "Second input while flip active rejected");
            yield return Wait(.24); Capture("flip.png");
            yield return Idle();
            Check("T03", "Next spread", journal.Document.currentSpread == 1, "Pages1–2 to3–4");
            journal.Turn(-1); yield return Idle();
            Check("T04", "Previous spread", journal.Document.currentSpread == 0, "Pages3–4 to1–2");
            while (journal.Document.currentSpread < journal.Document.SpreadCount - 1) { journal.Turn(1); yield return Idle(); }
            int last = journal.Document.currentSpread;
            Check("T05", "Last spread clamps", !journal.Turn(1) && journal.Document.currentSpread == last, "Last spread=" + last);
            while (journal.Document.currentSpread > 0) { journal.Turn(-1); yield return Idle(); }

            string oldLeft = journal.Document.GetPage(JournalSide.Left).paperId;
            string oldRight = journal.Document.GetPage(JournalSide.Right).paperId;
            string leftChoice = journal.Catalog.papers.First(paper => paper.id != oldLeft).id;
            string rightChoice = journal.Catalog.papers.First(paper => paper.id != oldRight && paper.id != leftChoice).id;
            journal.BeginPaperSelection(JournalSide.Left); journal.SelectPaper(leftChoice); yield return Wait(.15);
            Check("T07", "Live preview opens", journal.HasPaperPreview, "Left preview active");
            Capture("paper-picker.png");
            journal.ConfirmPaper();
            Check("T07", "Left confirmed independently", journal.Document.GetPage(JournalSide.Left).paperId == leftChoice &&
                journal.Document.GetPage(JournalSide.Right).paperId == oldRight, "left=" + leftChoice + "; right unchanged=" + oldRight);
            journal.BeginPaperSelection(JournalSide.Right); journal.SelectPaper(rightChoice); journal.ConfirmPaper();
            Check("T08", "Right confirmed independently", journal.Document.GetPage(JournalSide.Right).paperId == rightChoice &&
                journal.Document.GetPage(JournalSide.Left).paperId == leftChoice, "right=" + rightChoice + "; left unchanged");
            journal.BeginPaperSelection(JournalSide.Left); journal.SelectPaper(oldLeft); journal.CancelPaper();
            Check("T09", "Cancel restores selection", !journal.HasPaperPreview && journal.Document.GetPage(JournalSide.Left).paperId == leftChoice &&
                journal.Document.GetPage(JournalSide.Right).paperId == rightChoice, "Both committed choices preserved");
            journal.Turn(1); yield return Idle(); journal.Turn(-1); yield return Idle();
            Check("T07/T08", "Choices survive turn", journal.Document.GetPage(JournalSide.Left).paperId == leftChoice &&
                journal.Document.GetPage(JournalSide.Right).paperId == rightChoice, "No accidental shared paper asset mutation");

            Vector3 editPosition = lens.transform.position; float editFov = lens.fieldOfView;
            journal.ZoomBy(.5f); yield return Wait(.35);
            bool zoomChanged = Vector3.Distance(editPosition, lens.transform.position) > .001f || Mathf.Abs(editFov - lens.fieldOfView) > .01f;
            Check("T10", "Zoom changes view", zoomChanged && journal.Mode == JournalMode.ZoomMode, "Mode=" + journal.Mode + "; fov=" + lens.fieldOfView);
            var beforePan = lens.transform.position;
            journal.Pan(new Vector2(.15f, .1f)); yield return Wait(.25);
            Check("T11", "Pan moves actual camera", rig.PanOffset.sqrMagnitude > .00001f &&
                Vector3.Distance(beforePan, lens.transform.position) > .002f,
                "Actual camera shift=" + Vector3.Distance(beforePan, lens.transform.position));
            Capture("zoom.png");
            Check("T11", "Zoom detail view remains above paper", lens.transform.position.y > journal.transform.position.y + .08f && lens.nearClipPlane > 0,
                "position=" + lens.transform.position);
            journal.ZoomBy(1000); yield return Wait(.6); Vector3 maxZoom = lens.transform.position; float maxFov = lens.fieldOfView;
            journal.ZoomBy(1000); yield return Wait(.25);
            Check("T10", "Zoom clamps", Vector3.Distance(maxZoom, lens.transform.position) < .002f && Mathf.Abs(maxFov - lens.fieldOfView) < .02f,
                "Repeated extreme input reaches a finite bound");
            journal.Pan(new Vector2(1000, 1000)); yield return Wait(.5); Vector3 farPan = lens.transform.position;
            journal.Pan(new Vector2(1000, 1000)); yield return Wait(.25);
            Check("T11", "Pan clamps", Vector3.Distance(farPan, lens.transform.position) < .002f, "Cannot lose the book to unlimited pan");
            journal.ZoomBy(-1000); yield return Wait(.2); journal.ResetView(); yield return Wait(.4);
            Check("T10", "Reset restores edit framing", journal.Mode == JournalMode.EditingMode && Vector3.Distance(editPosition, lens.transform.position) < .01f &&
                Mathf.Abs(editFov - lens.fieldOfView) < .1f && BothPagesVisible(), "Default two-page view restored");

            journal.Turn(1); yield return Idle();
            Check("T14a", "Save writes JSON", journal.Save() && File.Exists(journal.SavePath), journal.SavePath);
            var expected = JsonUtility.ToJson(journal.Document);
            var loaded = new JournalSaveService(journal.Catalog, journal.SavePath).LoadOrCreate();
            Check("T14a", "Versioned JSON roundtrip", JsonUtility.ToJson(loaded) == expected,
                "Actual disk read through save service. Process restart is separately verified in native test.");
            journal.ExitEditing(); yield return Idle();
            Check("T12", "Desk camera restored", journal.Mode == JournalMode.DeskMode && Vector3.Distance(lens.transform.position, deskPosition) < .003f &&
                Quaternion.Angle(lens.transform.rotation, deskRotation) < .05f && Mathf.Abs(lens.fieldOfView - deskFov) < .02f, lens.transform.position.ToString());
            var tape = UnityEngine.Object.FindObjectsByType<DeskItem>(FindObjectsSortMode.None).First(value => value.kind == DeskItemKind.Tape);
            bool tapeHit = FindHit(tape, out hitPoint) && interaction.TryActivateAt(hitPoint);
            Check("T13", "Legacy tape preview works", tapeHit && desk.CurrentPanel == "tape", desk.CurrentPanel);
            desk.Close();
            journal.EnterEditing(); yield return Idle();
            Check("T14a", "Re-entry preserves current page", journal.Document.currentSpread == 1 && journal.Document.pages[0].paperId == leftChoice &&
                journal.Document.pages[1].paperId == rightChoice, "Spread and independent paper choices retained");

            for (int cycle = 0; cycle < 3; cycle++)
            {
                journal.Turn(journal.Document.currentSpread == 0 ? 1 : -1);
                journal.ZoomBy(.3f); // This must be ignored safely while a page turn owns the mode.
                journal.ExitEditing(); yield return Idle();
                Check("T15", "Exit during flip " + cycle, journal.Mode == JournalMode.DeskMode && !journal.IsBusy && !rig.IsTransitioning,
                    "Pending page operation settled/cancelled, camera returned");
                journal.EnterEditing(); journal.ExitEditing(); yield return Idle();
                Check("T15", "Exit during entry " + cycle, journal.Mode == JournalMode.DeskMode && !journal.IsBusy,
                    "Transition interruption does not leave a stuck editing mode");
                journal.EnterEditing(); yield return Idle();
                journal.BeginPaperSelection(JournalSide.Right); journal.SelectPaper(leftChoice); journal.ExitEditing(); yield return Idle();
                Check("T15", "Exit cancels preview " + cycle, !journal.HasPaperPreview && journal.Mode == JournalMode.DeskMode,
                    "Uncommitted preview cancelled on exit");
                journal.EnterEditing(); yield return Idle();
            }
            journal.ExitEditing(); yield return Idle(); yield return Wait(.3);
            Check("T16", "Console has no blocking exceptions", errors.Count == 0, errors.Count + " errors/asserts/exceptions");
        }

        static bool FindHit(DeskItem item, out Vector2 point)
        {
            point = default;
            foreach (var collider in item.GetComponentsInChildren<Collider>())
            {
                var bounds = collider.bounds;
                for (int x = -2; x <= 2; x++) for (int z = -2; z <= 2; z++)
                {
                    var sample = lens.WorldToScreenPoint(bounds.center + new Vector3(bounds.extents.x * x / 3f, bounds.extents.y, bounds.extents.z * z / 3f));
                    if (sample.z <= 0 || sample.x < 20 || sample.y < 20 || sample.x > Screen.width - 20 || sample.y > Screen.height - 20 || desk.IsPointerOverInterface(sample)) continue;
                    if (Physics.Raycast(lens.ScreenPointToRay(sample), out var hit, interaction.maxDistance) && hit.collider.GetComponentInParent<DeskItem>() == item)
                    { point = sample; return true; }
                }
            }
            return false;
        }

        static bool BothPagesVisible()
        {
            var view = UnityEngine.Object.FindFirstObjectByType<JournalPageView>();
            if (!view) return false;
            foreach (JournalSide side in new[] { JournalSide.Left, JournalSide.Right })
                foreach (var uv in new[] { Vector2.zero, Vector2.one, Vector2.right, Vector2.up })
                {
                    var point = lens.WorldToViewportPoint(view.WorldFromNormalized(side, uv));
                    if (point.z <= 0 || point.x < .04f || point.x > .91f || point.y < .13f || point.y > .86f) return false;
                }
            return true;
        }

        static void Capture(string name)
        {
            var previous = RenderTexture.active; var target = new RenderTexture(1920, 1080, 24);
            var texture = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            bool asynchronous = ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                var request = new RenderPipeline.StandardRequest { destination = target };
                if (!RenderPipeline.SupportsRenderRequest(lens, request)) throw new InvalidOperationException("SRP capture unsupported.");
                RenderPipeline.SubmitRenderRequest(lens, request);
                RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); texture.Apply();
                if (name != null) File.WriteAllBytes(Path.Combine(root, "docs/stage2-screenshots", name), texture.EncodeToPNG());
            }
            finally
            { ShaderUtil.allowAsyncCompilation = asynchronous; RenderTexture.active = previous; UnityEngine.Object.Destroy(texture); target.Release(); UnityEngine.Object.Destroy(target); }
        }

        static void Finish()
        {
            if (finishing) return;
            finishing = true; running = false; SessionState.SetBool(Pending, false);
            Application.logMessageReceived -= CaptureLog;
            root = root ?? Directory.GetParent(Application.dataPath).FullName;
            var report = new Report
            {
                timestampUtc = DateTime.UtcNow.ToString("O"), mode = "Actual Unity Editor Play Mode",
                inputScope = "Physics ray + runtime APIs. T14a is actual file roundtrip, not process restart. Native mouse/keys and T14 restart require separate Windows evidence.",
                screenshotScope = "1920x1080 actual Play Mode SRP camera renders, without IMGUI; native player screenshots include visible toolbar/picker.",
                unityVersion = Application.unityVersion, gpu = SystemInfo.graphicsDeviceName, graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                savePath = journal ? journal.SavePath : "Unavailable", width = Screen.width, height = Screen.height, renderedFrames = Time.frameCount,
                actualPlayMode = Application.isPlaying, passed = results.Count > 0 && results.All(result => result.passed) && errors.Count == 0,
                checks = results.ToArray(), consoleErrors = errors.ToArray(),
                screenshots = new[] { "edit.png", "flip.png", "paper-picker.png", "zoom.png" }.Where(name =>
                    File.Exists(Path.Combine(root, "docs/stage2-screenshots", name)) && File.GetLastWriteTimeUtc(Path.Combine(root, "docs/stage2-screenshots", name)) >= startedUtc).ToArray()
            };
            Directory.CreateDirectory(Path.Combine(root, "docs/stage2-logs"));
            File.WriteAllText(Path.Combine(root, "docs/stage2-logs/play-tests.json"), JsonUtility.ToJson(report, true));
            Debug.Log("[JournalValidation] Completed " + (report.passed ? "PASS" : "FAIL"));
            if (Application.isBatchMode) EditorApplication.Exit(report.passed ? 0 : 1); else EditorApplication.isPlaying = false;
        }

        static void ConfigureFullHd()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var gameType = assemblies.Select(assembly => assembly.GetType("UnityEditor.GameView", false)).First(type => type != null);
            var sizesType = assemblies.Select(assembly => assembly.GetType("UnityEditor.GameViewSizes", false)).First(type => type != null);
            var sizes = sizesType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy).GetValue(null);
            var group = sizesType.GetProperty("currentGroup", flags).GetValue(sizes);
            int count = (int)group.GetType().GetMethod("GetTotalCount", flags).Invoke(group, null), selected = -1;
            for (int index = 0; index < count; index++)
            {
                var size = group.GetType().GetMethod("GetGameViewSize", flags).Invoke(group, new object[] { index });
                if ((int)size.GetType().GetProperty("width", flags).GetValue(size) == 1920 && (int)size.GetType().GetProperty("height", flags).GetValue(size) == 1080)
                { selected = index; break; }
            }
            if (selected < 0) throw new InvalidOperationException("Existing FullHD GameView preset unavailable.");
            var view = EditorWindow.GetWindow(gameType, false, "Game", false); view.Show();
            gameType.GetMethod("SizeSelectionCallback", flags).Invoke(view, new object[] { selected, null });
            gameType.BaseType.GetProperty("targetSize", flags).SetValue(view, new Vector2(1920, 1080));
            view.Repaint();
        }
    }
}

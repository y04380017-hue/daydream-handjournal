using System;
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
    /// <summary>Runs the actual scene in Play Mode. Does not simulate native mouse/keyboard events.</summary>
    [InitializeOnLoad]
    public static class DeskValidation
    {
        const string PendingKey = "VintageJournal.PlayValidation.Pending";
        const string ScenePath = "Assets/Scenes/VintageJournalDesk.unity";
        static readonly List<CheckResult> checks = new List<CheckResult>();
        static readonly List<string> errors = new List<string>();
        static readonly List<float> frameMs = new List<float>();
        static DeskUI ui;
        static DeskCamera rig;
        static DeskInteraction interaction;
        static Camera camera;
        static DeskItem[] items;
        static int stage, lastFrame = -1;
        static double startTime, stageTime;
        static bool running, finishing;
        static bool preparing, warmedDesk;
        static double preparationTime;
        static DateTime actualPlayStartUtc;
        static string gameViewSetup = "Not requested";
        static Vector3 defaultPosition;
        static Quaternion defaultRotation;
        static float defaultFov;
        static string root;

        [Serializable] public sealed class CheckResult
        {
            public string name;
            public bool passed;
            public string detail;
        }
        [Serializable] public sealed class Report
        {
            public string timestampUtc, unityVersion, scene, mode, inputScope, graphicsDevice, graphicsType;
            public bool passed, enteredActualPlayMode, batchMode;
            public int width, height, renderedFrames, sampleFrames;
            public float averageFrameMs, medianFrameMs, p95FrameMs, observedFps;
            public string performanceScope;
            public string gameViewSetup, screenshotScope;
            public string[] savedScreenshots;
            public CheckResult[] checks;
            public string[] consoleErrors;
        }

        static DeskValidation()
        {
            EditorApplication.playModeStateChanged += PlayChanged;
            EditorApplication.update += Tick;
            if (SessionState.GetBool(PendingKey, false))
            {
                Application.logMessageReceived += CaptureLog;
                if (!EditorApplication.isPlayingOrWillChangePlaymode)
                { preparing = true; preparationTime = EditorApplication.timeSinceStartup; }
            }
        }

        [MenuItem("Vintage Journal/Validate in Play Mode")]
        public static void BeginPlayValidation()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Run validation while the Editor is in Edit Mode.");
            root = Directory.GetParent(Application.dataPath).FullName;
            Directory.CreateDirectory(Path.Combine(root, "docs/logs"));
            Directory.CreateDirectory(Path.Combine(root, "docs/screenshots"));
            // A prior result is never treated as a result from this execution.
            SessionState.SetBool(PendingKey, true);
            errors.Clear();
            Application.logMessageReceived -= CaptureLog;
            Application.logMessageReceived += CaptureLog;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            ConfigureFullHdGameView();
            SessionState.SetString("VintageJournal.GameViewSetup", gameViewSetup);
            // Search creates its first project index in an Editor delayCall. Entering Play in
            // executeMethod itself can make that setup abort, then enumerate an empty database.
            // Let Editor startup finish in Edit Mode; all errors remain captured and fail the run.
            preparing = true;
            preparationTime = EditorApplication.timeSinceStartup;
        }

        static void PlayChanged(PlayModeStateChange change)
        {
            if (!SessionState.GetBool(PendingKey, false)) return;
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                root = Directory.GetParent(Application.dataPath).FullName;
                checks.Clear(); frameMs.Clear();
                stage = 0; lastFrame = -1; finishing = false; running = true; warmedDesk = false;
                gameViewSetup = SessionState.GetString("VintageJournal.GameViewSetup", "Unknown");
                startTime = stageTime = EditorApplication.timeSinceStartup;
                actualPlayStartUtc = DateTime.UtcNow;
                Application.logMessageReceived -= CaptureLog;
                Application.logMessageReceived += CaptureLog;
            }
            else if (change == PlayModeStateChange.EnteredEditMode && !finishing)
            {
                Check("Play Mode completed", false, "Editor left Play Mode before validation completed.");
                Finish();
            }
        }

        static void CaptureLog(string condition, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                errors.Add(condition + "\n" + stack);
        }

        static void Check(string name, bool passed, string detail)
        {
            checks.Add(new CheckResult { name = name, passed = passed, detail = detail });
            Debug.Log("[DeskValidation] " + (passed ? "PASS " : "FAIL ") + name + ": " + detail);
        }

        static void Next() { stage++; stageTime = EditorApplication.timeSinceStartup; }
        static double StageSeconds => EditorApplication.timeSinceStartup - stageTime;

        static void Tick()
        {
            if (preparing)
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                { preparationTime = EditorApplication.timeSinceStartup; return; }
                if (EditorApplication.timeSinceStartup - preparationTime < 3) return;
                preparing = false;
                EditorApplication.isPlaying = true;
                return;
            }
            if (!running || finishing || !EditorApplication.isPlaying) return;
            if (EditorApplication.timeSinceStartup - startTime > 150)
            {
                Check("Validation timeout", false, "No completion within 150 seconds; stage " + stage);
                Finish(); return;
            }
            if (lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            try { Step(); }
            catch (Exception exception)
            {
                errors.Add(exception.ToString());
                Check("Validation exception", false, exception.Message);
                Finish();
            }
        }

        static void Step()
        {
            switch (stage)
            {
                case 0:
                    if (Time.frameCount < 20) return;
                    Check("Actual Play Mode", Application.isPlaying, "Application.isPlaying=" + Application.isPlaying);
                    Check("Graphics device", SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null,
                        SystemInfo.graphicsDeviceType + " / " + SystemInfo.graphicsDeviceName);
                    Check("Actual Play resolution", Screen.width == 1920 && Screen.height == 1080,
                        Screen.width + "x" + Screen.height + "; " + gameViewSetup);
                    ui = UnityEngine.Object.FindFirstObjectByType<DeskUI>();
                    rig = UnityEngine.Object.FindFirstObjectByType<DeskCamera>();
                    interaction = UnityEngine.Object.FindFirstObjectByType<DeskInteraction>();
                    camera = Camera.main;
                    items = UnityEngine.Object.FindObjectsByType<DeskItem>(FindObjectsSortMode.None);
                    Check("Core scene components", ui && rig && interaction && camera,
                        "UI, camera rig, interaction and MainCamera must exist.");
                    if (!ui || !rig || !interaction || !camera) { Finish(); return; }
                    Check("Start screen", ui.CurrentPanel == "start", ui.CurrentPanel);
                    ui.Begin();
                    Check("Start consumes native pointer", !ui.SceneInputAllowed,
                        "UI activation cannot reuse the current native mouse edge in scene Update; OS regression tested separately.");
                    Check("Start button business action", !ui.IsBlocking && ui.CurrentPanel == "desk", ui.CurrentPanel);
                    defaultPosition = rig.transform.position;
                    defaultRotation = rig.transform.rotation;
                    defaultFov = camera.fieldOfView;
                    Physics.SyncTransforms();
                    ValidateSceneAssets();
                    Next(); return;
                case 1:
                    if (!warmedDesk)
                    {
                        // Exercise every visible shader variant before the deliverable capture.
                        CaptureCamera(null);
                        warmedDesk = true;
                        stageTime = EditorApplication.timeSinceStartup;
                        return;
                    }
                    if (StageSeconds < 3 || ShaderUtil.anythingCompiling) return;
                    CaptureCamera("desk-play.png");
                    if (!Application.isBatchMode)
                        ScreenCapture.CaptureScreenshot(Path.Combine(root, "docs/screenshots/desk-play-ui.png"));
                    Next(); return;
                case 2:
                    if (StageSeconds < .2) return;
                    ValidateCatalogRay(DeskItemKind.Tape, "tape");
                    Next(); return;
                case 3:
                    if (StageSeconds < .2) return;
                    if (!Application.isBatchMode)
                        ScreenCapture.CaptureScreenshot(Path.Combine(root, "docs/screenshots/tape-panel-play.png"));
                    Next(); return;
                case 4:
                    if (StageSeconds < .2) return;
                    ui.HandleBack();
                    Check("Close tape preview", !ui.IsBlocking && ui.CurrentPanel == "desk", ui.CurrentPanel);
                    ValidateCatalogRay(DeskItemKind.Sticker, "sticker");
                    Next(); return;
                case 5:
                    if (StageSeconds < .2) return;
                    ui.HandleBack();
                    ValidateCatalogRay(DeskItemKind.Pages, "page");
                    Next(); return;
                case 6:
                    if (StageSeconds < .2) return;
                    ui.HandleBack();
                    var journal = items.FirstOrDefault(item => item.kind == DeskItemKind.Journal);
                    Check("Journal exists", journal, journal ? journal.name : "Missing journal DeskItem");
                    Vector2 screen;
                    bool found = journal && FindClickablePoint(journal, out screen);
                    if (found)
                    {
                        FindClickablePoint(journal, out screen);
                        bool activated = interaction.TryActivateAt(screen);
                        Check("Journal ray activation", activated && rig.IsInspecting && rig.IsTransitioning,
                            "screen=" + screen + "; inspecting=" + rig.IsInspecting);
                    }
                    else
                    {
                        Check("Journal ray activation", false, "No unobstructed clickable screen point found.");
                        rig.Inspect();
                    }
                    Next(); return;
                case 7:
                    if (StageSeconds < rig.transitionSeconds * .35) return;
                    Check("Smooth inspection intermediate frame", rig.IsTransitioning &&
                        Vector3.Distance(rig.transform.position, defaultPosition) > .001f &&
                        Vector3.Distance(rig.transform.position, rig.inspectionPosition) > .001f,
                        "Intermediate camera=" + rig.transform.position + "; transitioning=" + rig.IsTransitioning);
                    Next(); return;
                case 8:
                    if (rig.IsTransitioning || StageSeconds < rig.transitionSeconds) return;
                    Check("Inspection settles", rig.IsInspecting &&
                        Vector3.Distance(rig.transform.position, rig.inspectionPosition) < .002f,
                        rig.transform.position.ToString());
                    CaptureCamera("journal-play.png");
                    if (!Application.isBatchMode)
                        ScreenCapture.CaptureScreenshot(Path.Combine(root, "docs/screenshots/journal-play-ui.png"));
                    Next(); return;
                case 9:
                    if (StageSeconds < .2) return;
                    ui.HandleBack();
                    Check("Back action starts return", !rig.IsInspecting && rig.IsTransitioning,
                        "Same business method used by ESC and back button; no OS key injection.");
                    Next(); return;
                case 10:
                    if (rig.IsTransitioning || StageSeconds < rig.transitionSeconds) return;
                    Check("Return restores camera", Vector3.Distance(rig.transform.position, defaultPosition) < .002f &&
                        Quaternion.Angle(rig.transform.rotation, defaultRotation) < .05f &&
                        Mathf.Abs(camera.fieldOfView - defaultFov) < .01f,
                        "position=" + rig.transform.position + "; fov=" + camera.fieldOfView);
                    ui.OpenSettings();
                    Check("Settings open", ui.IsBlocking && ui.CurrentPanel == "settings", ui.CurrentPanel);
                    ui.HandleBack();
                    Check("Settings return", !ui.IsBlocking && ui.CurrentPanel == "desk", ui.CurrentPanel);
                    ui.ShowCatalog(DeskItemKind.Tape);
                    Check("Preview opened before back priority", ui.IsBlocking && ui.CurrentPanel == "tape", ui.CurrentPanel);
                    ui.HandleBack();
                    Check("Back closes active panel", !ui.IsBlocking && !rig.IsInspecting, ui.CurrentPanel);
                    Next(); return;
                case 11:
                    if (StageSeconds < 1.5) return;
                    Next(); return;
                case 12:
                    frameMs.Add(Time.unscaledDeltaTime * 1000f);
                    if (frameMs.Count < 180) return;
                    Check("Runtime frames sampled", frameMs.All(ms => ms > 0 && !float.IsNaN(ms)),
                        frameMs.Count + " actual unscaledDeltaTime samples in Play Mode.");
                    Check("Console has no runtime errors", errors.Count == 0, errors.Count + " errors/asserts/exceptions");
                    Check("Play screenshots saved", File.Exists(Path.Combine(root, "docs/screenshots/desk-play.png")) &&
                        File.Exists(Path.Combine(root, "docs/screenshots/journal-play.png")),
                        "Camera-rendered PNGs captured while Application.isPlaying=true.");
                    Finish(); return;
            }
        }

        static void ValidateSceneAssets()
        {
            var renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            var materials = renderers.SelectMany(renderer => renderer.sharedMaterials).ToArray();
            var invalid = materials.Where(material => !material || !material.shader ||
                !material.shader.isSupported || material.shader.name.Contains("InternalErrorShader")).ToArray();
            Check("Renderer materials and shaders", invalid.Length == 0,
                renderers.Length + " renderers, " + materials.Length + " slots; invalid=" + invalid.Length);
            foreach (DeskItemKind kind in new[] { DeskItemKind.Tape, DeskItemKind.Sticker, DeskItemKind.Pages, DeskItemKind.Journal })
                Check("Interactive item " + kind, items.Any(item => item.kind == kind), "DeskItem kind=" + kind);
            var catalog = ItemCatalog.Load();
            Check("Catalog data loads", catalog.items != null && catalog.items.Length >= 12 + 24 + 20,
                (catalog.items == null ? 0 : catalog.items.Length) + " catalog records");
            if (catalog.items != null)
            {
                var missing = catalog.items.Where(item => !item.Thumbnail).Select(item => item.id).ToArray();
                Check("Catalog thumbnails load", missing.Length == 0,
                    missing.Length == 0 ? "All Resources textures resolve." : string.Join(",", missing));
            }
        }

        static void ValidateCatalogRay(DeskItemKind kind, string expectedPanel)
        {
            var item = items.FirstOrDefault(candidate => candidate.kind == kind);
            if (!item) { Check(kind + " ray", false, "Missing DeskItem"); return; }
            Vector2 point;
            if (!FindClickablePoint(item, out point))
            {
                Check(kind + " ray", false, "No unobstructed screen sample found.");
                ui.ShowCatalog(kind);
                return;
            }
            var renderers = item.GetComponentsInChildren<Renderer>();
            var sharedBefore = renderers.Select(renderer => renderer.sharedMaterials).ToArray();
            var materialJson = sharedBefore.SelectMany(materials => materials).Where(material => material)
                .Distinct().ToDictionary(material => material, material => EditorJsonUtility.ToJson(material));
            var hovered = interaction.HoverAt(point);
            Check(kind + " hover and label", hovered == item && item.Highlighted &&
                interaction.CurrentHover == item.displayName,
                item.name + " @ " + point + "; label=" + interaction.CurrentHover);
            bool hasPropertyBlock = false;
            foreach (var renderer in renderers)
            {
                for (int index = 0; index < renderer.sharedMaterials.Length; index++)
                {
                    var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block, index);
                    if (!block.isEmpty) hasPropertyBlock = true;
                }
            }
            Check(kind + " visual highlight block", hasPropertyBlock, "Highlight uses per-renderer property blocks.");
            item.SetHighlight(false);
            bool unchanged = renderers.Select((renderer, index) => renderer.sharedMaterials.SequenceEqual(sharedBefore[index])).All(value => value)
                && materialJson.All(pair => EditorJsonUtility.ToJson(pair.Key) == pair.Value);
            Check(kind + " preserves shared materials", unchanged, "Shared material references and serialized values unchanged.");
            bool activated = interaction.TryActivateAt(point);
            Check(kind + " ray opens preview", activated && ui.IsBlocking && ui.CurrentPanel == expectedPanel,
                "panel=" + ui.CurrentPanel + "; screen=" + point);
            int expectedCount = kind == DeskItemKind.Tape ? 12 : kind == DeskItemKind.Sticker ? 20 : 24;
            Check(kind + " catalog count", ui.VisibleItemCount >= expectedCount,
                ui.VisibleItemCount + " items; expected at least " + expectedCount);
            if (ui.VisibleItemCount > 1)
            {
                var first = ui.SelectedItem;
                ui.SelectItem(1);
                Check(kind + " selected item detail", ui.SelectedItem != null && ui.SelectedItem != first && ui.SelectedItem.Thumbnail,
                    ui.SelectedItem == null ? "No selection" : ui.SelectedItem.id);
            }
        }

        static bool FindClickablePoint(DeskItem item, out Vector2 point)
        {
            point = default;
            var candidates = new List<Vector2>();
            foreach (var collider in item.GetComponentsInChildren<Collider>())
            {
                var bounds = collider.bounds;
                Vector3 centre = camera.WorldToScreenPoint(bounds.center);
                if (centre.z > 0) candidates.Add(centre);
                foreach (float x in new[] { -.7f, 0f, .7f })
                    foreach (float y in new[] { -.7f, 0f, .7f })
                        foreach (float z in new[] { -.7f, 0f, .7f })
                        {
                            Vector3 world = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
                            Vector3 projected = camera.WorldToScreenPoint(world);
                            if (projected.z > 0) candidates.Add(projected);
                        }
            }
            foreach (Vector2 sample in candidates)
            {
                if (sample.x < 20 || sample.y < 20 || sample.x > Screen.width - 20 || sample.y > Screen.height - 20 ||
                    ui.IsPointerOverInterface(sample)) continue;
                if (Physics.Raycast(camera.ScreenPointToRay(sample), out RaycastHit hit, interaction.maxDistance) &&
                    hit.collider.GetComponentInParent<DeskItem>() == item)
                { point = sample; return true; }
            }
            return false;
        }

        static void CaptureCamera(string filename)
        {
            if (!Application.isPlaying || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Screenshots require actual Play Mode and a graphics device.");
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var target = new RenderTexture(1920, 1080, 24);
            var texture = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            bool asynchronousShaders = ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                if (GraphicsSettings.currentRenderPipeline)
                {
                    // SRP's supported explicit render request; Camera.Render is for the built-in pipeline.
                    // https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/rendering/renderpipeline/submitrenderrequest
                    var request = new RenderPipeline.StandardRequest { destination = target };
                    if (!RenderPipeline.SupportsRenderRequest(camera, request))
                        throw new InvalidOperationException("Current render pipeline does not support StandardRequest capture.");
                    RenderPipeline.SubmitRenderRequest(camera, request);
                }
                else
                {
                    camera.targetTexture = target;
                    camera.Render();
                }
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
                texture.Apply();
                if (!string.IsNullOrEmpty(filename))
                    File.WriteAllBytes(Path.Combine(root, "docs/screenshots", filename), texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
                ShaderUtil.allowAsyncCompilation = asynchronousShaders;
                UnityEngine.Object.Destroy(texture); target.Release(); UnityEngine.Object.Destroy(target);
            }
        }

        static void Finish()
        {
            if (finishing) return;
            finishing = true; running = false;
            SessionState.SetBool(PendingKey, false);
            Application.logMessageReceived -= CaptureLog;
            root = root ?? Directory.GetParent(Application.dataPath).FullName;
            var sorted = frameMs.OrderBy(value => value).ToArray();
            var average = sorted.Length > 0 ? sorted.Average() : 0;
            var report = new Report
            {
                timestampUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                scene = ScenePath, mode = "Unity Editor actual Play Mode",
                inputScope = "Actual physics rays and public runtime business methods. OS mouse/keyboard input is tested separately.",
                graphicsDevice = SystemInfo.graphicsDeviceName, graphicsType = SystemInfo.graphicsDeviceType.ToString(),
                passed = checks.Count > 0 && checks.All(check => check.passed) && errors.Count == 0,
                enteredActualPlayMode = checks.Any(check => check.name == "Actual Play Mode" && check.passed),
                batchMode = Application.isBatchMode, width = Screen.width, height = Screen.height,
                renderedFrames = Time.frameCount, sampleFrames = frameMs.Count,
                averageFrameMs = average, medianFrameMs = sorted.Length > 0 ? sorted[sorted.Length / 2] : 0,
                p95FrameMs = sorted.Length > 0 ? sorted[Mathf.Min(sorted.Length - 1, Mathf.FloorToInt(sorted.Length * .95f))] : 0,
                observedFps = average > 0 ? 1000f / average : 0,
                performanceScope = "180 idle-scene Play Mode frames after warmup; Editor/batch overhead included. This does not establish sustained 60 FPS in the standalone player.",
                gameViewSetup = gameViewSetup,
                screenshotScope = Application.isBatchMode
                    ? "1920x1080 SRP camera renders captured in actual Play Mode; no IMGUI. Native player captures test visible UI separately."
                    : "SRP camera renders and requested ScreenCapture images; only existing files listed.",
                savedScreenshots = new[] { "desk-play.png", "journal-play.png", "desk-play-ui.png",
                    "journal-play-ui.png", "tape-panel-play.png" }.Where(filename =>
                    {
                        var path = Path.Combine(root, "docs/screenshots", filename);
                        return File.Exists(path) && File.GetLastWriteTimeUtc(path) >= actualPlayStartUtc;
                    }).ToArray(),
                checks = checks.ToArray(), consoleErrors = errors.ToArray()
            };
            Directory.CreateDirectory(Path.Combine(root, "docs/logs"));
            File.WriteAllText(Path.Combine(root, "docs/logs/play-validation.json"), JsonUtility.ToJson(report, true));
            Debug.Log("[DeskValidation] Completed: " + (report.passed ? "PASS" : "FAIL"));
            if (Application.isBatchMode) EditorApplication.Exit(report.passed ? 0 : 1);
            else EditorApplication.isPlaying = false;
        }

        static Type FindEditorType(string name)
        {
            return AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(name, false))
                .FirstOrDefault(type => type != null);
        }

        static void ConfigureFullHdGameView()
        {
            const BindingFlags instanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var gameType = FindEditorType("UnityEditor.GameView");
            var sizesType = FindEditorType("UnityEditor.GameViewSizes");
            if (gameType == null || sizesType == null)
                throw new InvalidOperationException("Cannot locate Unity GameView types for the resolution check.");
            var singleton = sizesType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            var sizes = singleton.GetValue(null);
            var group = sizesType.GetProperty("currentGroup", instanceFlags).GetValue(sizes);
            int count = (int)group.GetType().GetMethod("GetTotalCount", instanceFlags).Invoke(group, null);
            int fullHdIndex = -1;
            for (int index = 0; index < count; index++)
            {
                var size = group.GetType().GetMethod("GetGameViewSize", instanceFlags).Invoke(group, new object[] { index });
                int width = (int)size.GetType().GetProperty("width", instanceFlags).GetValue(size);
                int height = (int)size.GetType().GetProperty("height", instanceFlags).GetValue(size);
                if (width == 1920 && height == 1080) { fullHdIndex = index; break; }
            }
            if (fullHdIndex < 0) throw new InvalidOperationException("Unity's existing Full HD GameView preset was not found.");
            var view = EditorWindow.GetWindow(gameType, false, "Game", false);
            view.Show();
            gameType.GetMethod("SizeSelectionCallback", instanceFlags).Invoke(view, new object[] { fullHdIndex, null });
            // Ensure the native player-loop receives the render size even in batch mode without GUI repaint.
            var playModeView = gameType.BaseType;
            playModeView.GetProperty("targetSize", instanceFlags).SetValue(view, new Vector2(1920, 1080));
            view.Repaint();
            gameViewSetup = "Selected existing Full HD preset index " + fullHdIndex + "; requested 1920x1080 target; no preference size added or saved.";
            Debug.Log("[DeskValidation] " + gameViewSetup);
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace VintageJournal.Editor
{
    /// <summary>
    /// Actual saved-scene Play Mode run of one commission loop. Uses the shared workspace actions plus real camera
    /// raycasts into page surfaces and the mailbox collider. No native OS input; IMGUI is not in GPU captures.
    /// </summary>
    [InitializeOnLoad]
    public static class CommissionValidation
    {
        const string Pending = "VintageJournal.CommissionValidation.Pending";
        static readonly List<Result> results = new List<Result>();
        static readonly List<string> errors = new List<string>(), screenshots = new List<string>();
        static readonly Stack<IEnumerator> routines = new Stack<IEnumerator>();
        static bool preparing, running, finishing;
        static double preparationTime, started;
        static int lastFrame;
        static string root;
        static WorkspaceGameplayController workspace;
        static CommissionDeskController commission;
        static JournalController journal;
        static Camera lens;
        [Serializable] public sealed class Result { public string test, name, status, detail; public bool passed; }
        [Serializable] public sealed class Report
        {
            public string timestampUtc, mode, inputScope, screenshotScope, unityVersion, gpu, graphicsApi, savePath;
            public bool passed, actualPlayMode, nativeInputValidated;
            public int renderedFrames; public double playSeconds;
            public Result[] checks; public string[] consoleErrors, screenshots;
        }
        static CommissionValidation()
        {
            EditorApplication.update += Tick; EditorApplication.playModeStateChanged += PlayChanged;
            if (SessionState.GetBool(Pending, false)) Application.logMessageReceived += CaptureLog;
        }
        [MenuItem("Vintage Journal/Commission/Validate commission loop in Play Mode")]
        public static void Begin()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Start in Edit Mode.");
            if (!Environment.GetCommandLineArgs().Contains("--commission-validation")) throw new InvalidOperationException("Use --commission-validation for isolated saves.");
            root = Directory.GetParent(Application.dataPath).FullName;
            Directory.CreateDirectory(Path.Combine(root, "docs/commission"));
            if (!CommissionDataTests.RunDataChecks() || !CraftDataTests.RunDataChecks() || !JournalDataTests.RunDataChecks() || !DecorationDataTests.RunDataChecks())
                throw new InvalidOperationException("Data suites failed before Play; inspect [CommissionData]/[CraftData] logs.");
            string isolated = Path.Combine(Application.persistentDataPath, "CommissionValidation", "journal-v1.json");
            Directory.CreateDirectory(Path.GetDirectoryName(isolated));
            if (File.Exists(isolated)) File.Copy(isolated, isolated + ".before-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff"), false);
            File.WriteAllText(isolated, JsonUtility.ToJson(JournalDocument.CreateDefault(JournalPaperCatalog.Load()), true));
            string photos = Path.Combine(Path.GetDirectoryName(isolated), "commission-photos");
            if (Directory.Exists(photos)) foreach (var f in Directory.GetFiles(photos, "*.png")) File.Delete(f);
            results.Clear(); errors.Clear(); screenshots.Clear();
            SessionState.SetBool(Pending, true); Application.logMessageReceived -= CaptureLog; Application.logMessageReceived += CaptureLog;
            EditorSceneManager.OpenScene("Assets/Scenes/VintageJournalDesk.unity", OpenSceneMode.Single);
            preparing = true; preparationTime = EditorApplication.timeSinceStartup;
        }
        static void PlayChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Pending, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                root = Directory.GetParent(Application.dataPath).FullName; results.Clear(); errors.Clear(); screenshots.Clear(); routines.Clear();
                started = EditorApplication.timeSinceStartup; lastFrame = -1; running = true; finishing = false;
                Application.logMessageReceived -= CaptureLog; Application.logMessageReceived += CaptureLog; routines.Push(Execute());
            }
            else if (state == PlayModeStateChange.EnteredEditMode && !finishing) { Check("Harness", "Unexpected Play exit", false, "Validation incomplete."); Finish(); }
        }
        static void CaptureLog(string message, string stack, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message + "\n" + stack); }
        static void Check(string id, string name, bool passed, string detail)
        { results.Add(new Result { test = id, name = name, passed = passed, status = passed ? "passed" : "failed", detail = detail }); Debug.Log("[CommissionValidation] " + id + " " + (passed ? "PASS " : "FAIL ") + name + " " + detail); }
        static void Manual(string id, string name, string detail)
        { results.Add(new Result { test = id, name = name, status = "requires_manual_verification", detail = detail }); Debug.Log("[CommissionValidation] " + id + " MANUAL " + name + " " + detail); }
        static void Tick()
        {
            if (preparing)
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) { preparationTime = EditorApplication.timeSinceStartup; return; }
                if (EditorApplication.timeSinceStartup - preparationTime < 2) return;
                preparing = false; EditorApplication.isPlaying = true; return;
            }
            if (!running || finishing || !EditorApplication.isPlaying || Time.frameCount == lastFrame) return; lastFrame = Time.frameCount;
            if (EditorApplication.timeSinceStartup - started > 240) { Check("Harness", "Timeout", false, "240 seconds exceeded."); Finish(); return; }
            try
            {
                while (routines.Count > 0)
                { var routine = routines.Peek(); if (!routine.MoveNext()) { routines.Pop(); continue; } if (routine.Current is IEnumerator child) { routines.Push(child); continue; } return; }
                Finish();
            }
            catch (Exception error) { errors.Add(error.ToString()); Check("Harness", "Exception", false, error.ToString()); Finish(); }
        }
        static IEnumerator Wait(double seconds) { double until = EditorApplication.timeSinceStartup + seconds; while (EditorApplication.timeSinceStartup < until) yield return null; }
        static IEnumerator Settled()
        {
            double until = EditorApplication.timeSinceStartup + 8;
            while (workspace.physical.IsSettling && EditorApplication.timeSinceStartup < until) yield return null;
            yield return null; workspace.coordinator.SyncPhysical(); yield return null;
        }
        static Ray PageRay(JournalSide side, Vector2 uv)
        {
            var surface = journal.pageView.GetSurface(side); var size = journal.pageView.SurfaceSize(side);
            var world = surface.TransformPoint(new Vector3(size.x * uv.x, size.y * uv.y, 0));
            return lens.ScreenPointToRay(lens.WorldToScreenPoint(world));
        }

        static IEnumerator Execute()
        {
            double readyUntil = EditorApplication.timeSinceStartup + 15;
            while ((workspace = UnityEngine.Object.FindFirstObjectByType<WorkspaceGameplayController>()) == null || !workspace.Ready ||
                   (commission = UnityEngine.Object.FindFirstObjectByType<CommissionDeskController>()) == null || !commission.Ready)
            { if (EditorApplication.timeSinceStartup > readyUntil) throw new TimeoutException("Workspace or commission desk never became ready."); yield return null; }
            journal = workspace.journal; lens = workspace.lens;
            foreach (var adapter in UnityEngine.Object.FindObjectsByType<WorkspaceInputRouter>(FindObjectsSortMode.None)) adapter.enabled = false;
            foreach (var adapter in UnityEngine.Object.FindObjectsByType<PhysicalMouseInputAdapter>(FindObjectsSortMode.None)) adapter.enabled = false;
            foreach (var adapter in UnityEngine.Object.FindObjectsByType<ScrapbookInput>(FindObjectsSortMode.None)) adapter.enabled = false;
            RuntimeInputGate.Reset(); workspace.desk.Begin(); yield return Wait(.4);

            // ---- Floating space ----
            var dome = GameObject.Find("Sky dome"); var roomRenderers = GameObject.Find("RoomAndWindow")?.GetComponentsInChildren<Renderer>(true) ?? new Renderer[0];
            int visibleShell = roomRenderers.Count(r => r.enabled && !r.GetComponentInParent<DaydreamFloat>());
            Check("F01", "Room shell hidden, sky dome present", dome && dome.GetComponent<Renderer>().enabled && visibleShell == 0, "Visible non-floating room renderers: " + visibleShell);
            var floats = UnityEngine.Object.FindObjectsByType<DaydreamFloat>(FindObjectsSortMode.None);
            bool floatsSafe = floats.Length > 10 && floats.All(f => !f.GetComponentsInChildren<Collider>().Any(c => c.enabled) && !f.GetComponentInParent<PhysicalInteractable>() &&
                !f.GetComponentInChildren<PhysicalInteractable>() && !f.GetComponentInParent<WorkspaceDrawer>() && !f.GetComponentInParent<JournalController>());
            Check("F02", "Floating motion only on decorative objects", floatsSafe, floats.Length + " DaydreamFloat components; none on colliders, interactables, drawers or the journal.");
            var homes = UnityEngine.Object.FindObjectsByType<PhysicalInteractable>(FindObjectsSortMode.None).Where(i => i.homeSlot).ToArray();
            var before = homes.ToDictionary(i => i, i => i.transform.position); yield return Wait(1.5);
            Check("F03", "Interactables remain still while the sky drifts", homes.All(i => Vector3.Distance(before[i], i.transform.position) < .0005f), homes.Length + " stored entities sampled across 1.5 s.");
            var breath = UnityEngine.Object.FindFirstObjectByType<DaydreamBreath>();
            if (breath)
            {
                float lo = float.MaxValue, hi = float.MinValue; double end = EditorApplication.timeSinceStartup + 5;
                while (EditorApplication.timeSinceStartup < end) { float v = breath.sun.intensity; lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v); yield return null; }
                var motes = UnityEngine.Object.FindObjectsByType<DaydreamMotes>(FindObjectsSortMode.None);
                int clouds = GameObject.Find("Clouds") ? GameObject.Find("Clouds").transform.Cast<Transform>().Count(t => t.gameObject.activeInHierarchy) : -1;
                Check("B01", "Room breathes: sunlight swells and settles on one slow rhythm", hi - lo > .03f * hi, "Sun intensity range over 5 s: " + lo.ToString("F3") + "–" + hi.ToString("F3"));
                var p2 = GameObject.Find("Daydream Studio P2");
                var time = UnityEngine.Object.FindFirstObjectByType<DreamTimeController>();
                if (p2 && time)
                {
                    int p2Colliders = p2.GetComponentsInChildren<Collider>(true).Length, arches = p2.GetComponentsInChildren<DaydreamFloat>().Length;
                    Capture("studio-p2-day.png");
                    time.SetDusk(true, false, true); yield return Wait(.6);
                    bool dusk = time.IsDusk && Mathf.Approximately(time.DuskAmount, 1) && !time.IsNight && Vector4.Distance(breath.sun.color, time.duskSun) < .05f;
                    Capture("studio-p2-dusk.png");
                    time.SetNight(true, false, true); yield return Wait(.6); Capture("studio-p2-night.png");
                    bool backToNight = !time.IsDusk && time.IsNight;
                    time.SetNight(false, false, true); yield return Wait(.6);
                    Check("P2a", "Arch windows and light patches are decorative only", p2Colliders == 0 && arches == 3, "colliders=" + p2Colliders + ", floating arches=" + arches);
                    Check("P2b", "Dusk preset switches sky and light, and night/day still switch afterwards", dusk && backToNight && !time.IsNight && !time.IsDusk,
                        "dusk=" + dusk + " nightAfter=" + backToNight + " sun=" + breath.sun.color);
                }
                var studio = GameObject.Find("Daydream Studio");
                if (studio)
                {
                    int studioColliders = studio.GetComponentsInChildren<Collider>(true).Length;
                    int water = studio.GetComponentsInChildren<DaydreamScroll>().Length;
                    Check("S01", "Studio platform, water strips and cloud sea are decorative only", studioColliders == 0 && water >= 3,
                        "colliders=" + studioColliders + ", drifting surfaces=" + water);
                }
                Check("B02","Light motes drift and clouds are reduced to a few distant ones", motes.Length == 1 && motes[0].ActiveCount > 0 && clouds >= 0 && clouds <= 3,
                    "motes systems=" + motes.Length + " alive=" + (motes.Length == 1 ? motes[0].ActiveCount : 0) + ", active clouds=" + clouds);
            }
            // Judge visibility at the shipped 16:9 target and at 4:3 (batch Game view); every bounds corner must be on screen.
            var bounds = commission.mailboxCollider.bounds; string sight = string.Empty; bool allVisible = true; Vector3 vp = default;
            foreach (float aspect in new[] { 16f / 9f, 4f / 3f })
            {
                lens.aspect = aspect; yield return null; bool inside = true;
                for (int c = 0; c < 8; c++)
                {
                    var corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    var v = lens.WorldToViewportPoint(corner); inside &= v.z > 0 && v.x > 0 && v.x < 1 && v.y > 0 && v.y < 1;
                }
                allVisible &= inside; sight += aspect.ToString("F2") + ":" + (inside ? "inside" : "clipped") + " ";
            }
            lens.aspect = 16f / 9f; vp = lens.WorldToViewportPoint(bounds.center);
            bool mailboxHit = Physics.Raycast(lens.ViewportPointToRay(vp), out var hit, 10) && hit.collider == commission.mailboxCollider;
            lens.ResetAspect();
            Check("F04", "Whole mailbox is on screen from the seat (16:9 and 4:3) and first-hit by its ray", allVisible && mailboxHit,
                sight + "; first hit " + (hit.collider ? hit.collider.name : "none"));
            lens.aspect = 16f / 9f; vp = lens.WorldToViewportPoint(bounds.center); var clickRay = lens.ViewportPointToRay(vp); lens.ResetAspect();
            Capture("floating-desk-default.png");

            // ---- First letter ----
            double until = EditorApplication.timeSinceStartup + 8;
            while (!commission.Service.OpenLetters.Any() && EditorApplication.timeSinceStartup < until) yield return null;
            var first = commission.Service.OpenLetters.FirstOrDefault();
            Check("C01", "First authored letter arrives in the floating mailbox", first != null && first.letterId == "letter-001" && commission.Service.UnreadCount > 0, first?.title ?? "none");
            yield return Wait(.6);
            Check("C02", "Unread flag and floating envelope are shown", commission.floatingEnvelope.gameObject.activeInHierarchy, "Envelope active with unread letters.");
            var terminal = UnityEngine.Object.FindFirstObjectByType<LetterTerminalScreen>();
            if (terminal)
            {
                var mailbox = commission.mailboxCollider.transform;
                bool oldHidden = new[] { "Body", "Roof", "Door" }.All(n => mailbox.Find(n) && !mailbox.Find(n).GetComponent<Renderer>().enabled);
                Check("P3a", "Pink CRT letter terminal replaces the mailbox look and lights up for unread letters",
                    terminal.Lit && oldHidden && terminal.GetComponentsInChildren<Collider>(true).Length == 0 && commission.mailboxCollider.enabled,
                    "lit=" + terminal.Lit + " oldPartsHidden=" + oldHidden + " collider kept=" + commission.mailboxCollider.enabled);
                var eggs = GameObject.Find("Daydream Studio P3");
                bool eggsOk = eggs && eggs.GetComponentsInChildren<Collider>(true).Length == 0 && eggs.transform.Cast<Transform>().All(t =>
                { lens.aspect = 16f / 9f; var v = lens.WorldToViewportPoint(t.position); lens.ResetAspect(); return v.z > 0 && v.x > 0 && v.x < 1 && v.y > 0 && v.y < 1; });
                Check("P3b", "Distant easter eggs float in view without colliders", eggsOk, eggs ? eggs.transform.childCount + " objects" : "missing");
                Capture("studio-p3-terminal.png");
            }
            Capture("mailbox-unread.png");
            bool clicked = commission.TryClick(clickRay);
            Check("C03", "Clicking the mailbox through its collider opens the letter panel", clicked && commission.PanelOpen && commission.ActiveLetterId == "letter-001" &&
                commission.Service.Data.letters[0].status == CommissionLetterStatus.Read, "Panel open; letter marked read.");
            if (first == null) yield break;

            // ---- Make the page: real tape cut + real sticker, placed through page raycasts ----
            var items = UnityEngine.Object.FindObjectsByType<PhysicalInteractable>(FindObjectsSortMode.None);
            var roll = items.Where(i => i.itemType == PhysicalItemType.Tape && i.homeSlot && i.homeSlot.container).OrderBy(i => i.assetId == "tape_01" ? 0 : i.assetId == "tape_11" ? 1 : 2).First();
            var drawer = UnityEngine.Object.FindObjectsByType<WorkspaceDrawer>(FindObjectsSortMode.None).First(d => d.container == roll.homeSlot.container);
            drawer.SetOpenAmount(3); yield return Wait(.2);
            bool selected = workspace.SelectPhysical(roll), lengthStart = workspace.StartTapeLength(roll.transform.position);
            workspace.SetTapeLength(.06f); bool cut = workspace.ConfirmTapeLength(); string strip = workspace.Service.HeldItem?.instanceId;
            bool tapePlaced = cut && workspace.TryPlacePointer(PageRay(JournalSide.Left, new Vector2(.5f, .78f)));
            Check("C04", "Cut 6 cm " + roll.assetId + " and place it on journal page 1 via page raycast", selected && lengthStart && tapePlaced &&
                workspace.Service.GetItem(strip)?.owner == CraftOwner.JournalPage, workspace.Service.LastMessage);
            drawer.SetOpenAmount(-1); yield return Settled();
            var sticker = items.First(i => i.itemType == PhysicalItemType.Sticker && i.assetId == "sticker_02" && i.gameObject.activeInHierarchy);
            bool grabbed = workspace.GrabPhysical(sticker); bool stickerPlaced = grabbed && workspace.TryPlacePointer(PageRay(JournalSide.Left, new Vector2(.42f, .45f)));
            Check("C05", "Take rose sticker from the hanging rack and stick it on the same page", stickerPlaced && workspace.Service.GetItem(sticker.itemInstanceId).owner == CraftOwner.JournalPage, workspace.Service.LastMessage);
            yield return Settled();

            string page1 = journal.Document.GetPage(JournalSide.Left).pageId;
            bool targeted = commission.SelectTarget(CraftOwner.JournalPage, "journal", page1);
            var preview = commission.Preview();
            Check("C06", "Live checklist reads page tags from data", targeted && preview != null && preview.itemCount == 2 && preview.matched.Contains("花草") && preview.tier >= 2,
                preview == null ? "no preview" : "matched=" + string.Join(",", preview.matched) + " tier=" + preview.tier);
            string ownership = JsonUtility.ToJson(new Snapshot { items = workspace.Service.Items.Select(i => i.Clone()).ToArray() });
            bool sent = commission.SendActive(); yield return Wait(.3);
            var sentLetter = commission.Service.Data.letters.First(l => l.letterId == "letter-001");
            string photoPath = Path.Combine(commission.PhotoFolder, sentLetter.photoFile ?? "");
            Check("C07", "Send takes a page photo and keeps every material where it was", sent && sentLetter.status == CommissionLetterStatus.Sent && File.Exists(photoPath) &&
                ownership == JsonUtility.ToJson(new Snapshot { items = workspace.Service.Items.Select(i => i.Clone()).ToArray() }), commission.Status);
            Check("C08", "Page photo is a real render (not blank)", File.Exists(photoPath) && PhotoVariance(photoPath) > 40, "Luma variance " + (File.Exists(photoPath) ? PhotoVariance(photoPath).ToString("F1") : "n/a"));
            if (File.Exists(photoPath)) { File.Copy(photoPath, Path.Combine(root, "docs/commission", "sent-photo-letter-001.png"), true); screenshots.Add("sent-photo-letter-001.png"); }
            yield return Wait(.5);
            var card = commission.postcardFrames.FirstOrDefault();
            Check("C09", "Sent photo hangs on the floating postcard line", card && card.transform.parent.gameObject.activeInHierarchy, "Postcard 1 visible.");
            Capture("postcard-line.png");

            // ---- Continuous rhythm ----
            until = EditorApplication.timeSinceStartup + 12;
            while (!commission.Service.OpenLetters.Any() && EditorApplication.timeSinceStartup < until) yield return null;
            var second = commission.Service.OpenLetters.FirstOrDefault();
            Check("C11", "Next commission arrives shortly after sending", second != null && second.letterId == "letter-002", second?.title ?? "none");
            if (second != null)
            {
                commission.SelectLetter(second.letterId); commission.SelectTarget(CraftOwner.JournalPage, "journal", page1);
                bool resend = commission.SendActive();
                Check("C10", "Sending the identical page again is refused", !resend && commission.Service.Data.letters.First(l => l.letterId == second.letterId).IsOpen, commission.Status);
            }
            until = EditorApplication.timeSinceStartup + 40;
            while (sentLetter.status != CommissionLetterStatus.Replied && EditorApplication.timeSinceStartup < until)
            { yield return null; sentLetter = commission.Service.Data.letters.First(l => l.letterId == "letter-001"); }
            Check("C12", "Reply arrives in real time with stamps", sentLetter.status == CommissionLetterStatus.Replied && commission.Service.Data.stamps == sentLetter.stamps && sentLetter.stamps >= 2,
                "tier=" + sentLetter.tier + " stamps=" + commission.Service.Data.stamps + " reply=" + sentLetter.replyText);

            // ---- Second letter on the right page with a postage sticker ----
            if (second != null)
            {
                commission.SelectLetter(second.letterId);
                var stamp = items.First(i => i.itemType == PhysicalItemType.Sticker && i.assetId == "sticker_03" && i.gameObject.activeInHierarchy);
                bool ok = workspace.GrabPhysical(stamp) && workspace.TryPlacePointer(PageRay(JournalSide.Right, new Vector2(.5f, .5f)));
                yield return Settled();
                commission.SelectTarget(CraftOwner.JournalPage, "journal", journal.Document.GetPage(JournalSide.Right).pageId);
                var p2 = commission.Preview(); bool sent2 = ok && commission.SendActive();
                Check("C13", "Second letter: tags matched but too few items gives the middle tier", sent2 && p2 != null && p2.missing.Length == 0 && p2.itemCount == 1 && p2.tier == 2, commission.Status);
            }

            // ---- Persistence ----
            workspace.Service.Flush(); yield return Wait(.5);
            var disk = new JournalSaveService(journal.Catalog, journal.SavePath).LoadOrCreate();
            // Compare through the same JSON round trip the save uses, so float text formatting is not mistaken for data loss.
            var live = JsonUtility.FromJson<JournalDocument>(JsonUtility.ToJson(journal.Document)); live.NormalizeOptionalData();
            string liveJson = JsonUtility.ToJson(live.commissionDesk), diskJson = JsonUtility.ToJson(disk.commissionDesk);
            if (liveJson != diskJson) Debug.Log("[CommissionValidation] live=" + liveJson + "\n disk=" + diskJson);
            Check("C14", "Saved file holds the same commission branch as the live desk", disk.commissionDeskEnabled && liveJson == diskJson, journal.SavePath);
            Check("C15", "Save keeps journal schema v1 and craft branch v1", disk.schemaVersion == 1 && disk.craftWorkspace.schemaVersion == 1 && disk.commissionDesk.schemaVersion == 1, "Additive branch only.");
            Manual("M01", "Letter panel look and readability", "IMGUI is excluded from GPU captures; see Windows player screenshots for the panel.");
            Manual("M02", "Floating sky feel and colour on other displays", "Art direction judgement needs a person.");
        }
        [Serializable] sealed class Snapshot { public CraftItemRecord[] items; }

        static double PhotoVariance(string path)
        {
            var tex = new Texture2D(2, 2); tex.LoadImage(File.ReadAllBytes(path));
            var px = tex.GetPixels32(); UnityEngine.Object.DestroyImmediate(tex);
            double mean = px.Average(p => (p.r + p.g + p.b) / 3.0);
            return px.Average(p => { double l = (p.r + p.g + p.b) / 3.0 - mean; return l * l; });
        }
        static void Capture(string name)
        {
            RenderTexture previous = RenderTexture.active; var target = new RenderTexture(1920, 1080, 24); var texture = new Texture2D(1920, 1080, TextureFormat.RGB24, false); bool async = ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation = false; var request = new RenderPipeline.StandardRequest { destination = target };
                if (!RenderPipeline.SupportsRenderRequest(lens, request)) throw new InvalidOperationException("GPU capture unsupported.");
                RenderPipeline.SubmitRenderRequest(lens, request); RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); texture.Apply();
                File.WriteAllBytes(Path.Combine(root, "docs/commission", name), texture.EncodeToPNG()); screenshots.Add(name);
            }
            finally { ShaderUtil.allowAsyncCompilation = async; RenderTexture.active = previous; UnityEngine.Object.Destroy(texture); target.Release(); UnityEngine.Object.Destroy(target); }
        }
        static void Finish()
        {
            if (finishing) return; finishing = true; running = false; SessionState.SetBool(Pending, false); Application.logMessageReceived -= CaptureLog;
            root = root ?? Directory.GetParent(Application.dataPath).FullName; Directory.CreateDirectory(Path.Combine(root, "docs/commission"));
            bool passed = results.Any(r => r.passed) && results.All(r => r.status != "failed") && errors.Count == 0;
            var report = new Report
            {
                timestampUtc = DateTime.UtcNow.ToString("O"), mode = "Actual rendered Unity Editor Play Mode", unityVersion = Application.unityVersion,
                inputScope = "Shared workspace actions with real camera raycasts into journal pages and the mailbox collider; no native OS input.",
                screenshotScope = "GPU camera captures 1920x1080, excluding IMGUI.", gpu = SystemInfo.graphicsDeviceName, graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                savePath = journal ? journal.SavePath : "Unavailable", passed = passed, actualPlayMode = Application.isPlaying, renderedFrames = Time.frameCount,
                playSeconds = EditorApplication.timeSinceStartup - started, checks = results.ToArray(), consoleErrors = errors.ToArray(), screenshots = screenshots.ToArray()
            };
            File.WriteAllText(Path.Combine(root, "docs/commission/play-tests.json"), JsonUtility.ToJson(report, true));
            Debug.Log("[CommissionValidation] Completed " + (passed ? "PASS" : "FAIL"));
            if (Application.isBatchMode) EditorApplication.Exit(passed ? 0 : 1); else EditorApplication.isPlaying = false;
        }
    }
}

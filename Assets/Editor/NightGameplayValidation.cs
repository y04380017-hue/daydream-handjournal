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
    /// <summary>Opt-in, isolated saved-scene Play acceptance. Actions use runtime APIs and camera rays; native OS input is not simulated.</summary>
    [InitializeOnLoad]
    public static class NightGameplayValidation
    {
        const string Pending = "VintageJournal.NightGameplayValidation.Pending";
        static bool preparing, running, finishing;
        static double prepareTime, started;
        static int lastFrame;
        static string output;
        static WorkspaceGameplayController workspace;
        static JournalController journal;
        static CommissionDeskController commission;
        static DreamGameplayController dream;
        static readonly List<CheckRow> checks = new List<CheckRow>();
        static readonly List<string> errors = new List<string>();
        static readonly List<string> captures = new List<string>();
        static readonly List<string> collectionDiagnostics = new List<string>();
        static readonly Stack<IEnumerator> routines = new Stack<IEnumerator>();
        [Serializable] public sealed class CheckRow { public string name, detail; public bool passed; }
        [Serializable] public sealed class Report
        {
            public string timestampUtc, unityVersion, gpu, savePath, inputScope, screenshotScope;
            public bool passed, actualPlayMode, nativeInputValidated, freshProcessRestart;
            public int renderedFrames;
            public double elapsedSeconds;
            public CheckRow[] checks;
            public string[] consoleErrors, screenshots, requiresManualVerification, collectionDiagnostics;
        }

        static NightGameplayValidation()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += PlayChanged;
        }

        [MenuItem("Vintage Journal/Night gameplay/Validate saved scene in Play Mode")]
        public static void Begin()
        {
            var args = Environment.GetCommandLineArgs();
            if (!args.Contains("--night-gameplay-validation") || !args.Contains("--desk-revision-validation"))
                throw new InvalidOperationException("Use --night-gameplay-validation and --desk-revision-validation for an isolated save.");
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Start in Edit Mode.");
            output = OutputFolder(); Directory.CreateDirectory(output);
            if (!NightGameplayDataChecks.RunDataChecks() || !NightCommissionChecks.RunDataChecks())
                throw new InvalidOperationException("Night data checks failed before Play; inspect [NightData] and [NightCommission] logs.");
            // A new run ID is supplied by the PowerShell wrapper. Never clear a player's save or an earlier evidence folder.
            EditorSceneManager.OpenScene("Assets/Scenes/VintageJournalDesk.unity", OpenSceneMode.Single);
            SessionState.SetBool(Pending, true); preparing = true; prepareTime = EditorApplication.timeSinceStartup;
        }

        static string OutputFolder() => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "docs", "night-gameplay", "play");
        static void PlayChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Pending, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                output = OutputFolder(); Directory.CreateDirectory(output);
                checks.Clear(); errors.Clear(); captures.Clear(); collectionDiagnostics.Clear(); routines.Clear();
                started = EditorApplication.timeSinceStartup; lastFrame = -1; running = true; finishing = false;
                Application.logMessageReceived -= Log; Application.logMessageReceived += Log;
                routines.Push(Execute());
            }
            else if (state == PlayModeStateChange.EnteredEditMode && !finishing)
            { Check("Unexpected Play exit", false, "Acceptance did not complete."); Finish(); }
        }

        static void Log(string message, string trace, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message + "\n" + trace); }
        static void Check(string name, bool pass, string detail = "")
        { checks.Add(new CheckRow { name = name, passed = pass, detail = detail }); Debug.Log("[NightGameplayValidation] " + (pass ? "PASS " : "FAIL ") + name + " " + detail); }
        static void Require(string name, bool pass, string detail = "")
        { Check(name, pass, detail); if (!pass) throw new InvalidOperationException(name + ": " + detail); }
        static void Tick()
        {
            if (preparing)
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) { prepareTime = EditorApplication.timeSinceStartup; return; }
                if (EditorApplication.timeSinceStartup - prepareTime < 2) return;
                preparing = false; EditorApplication.isPlaying = true; return;
            }
            if (!running || finishing || !EditorApplication.isPlaying || Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            if (EditorApplication.timeSinceStartup - started > 150) { Check("Harness timeout", false, "150 seconds"); Finish(); return; }
            try
            {
                while (routines.Count > 0)
                {
                    var iterator = routines.Peek();
                    if (!iterator.MoveNext()) { routines.Pop(); continue; }
                    if (iterator.Current is IEnumerator nested) { routines.Push(nested); continue; }
                    return;
                }
                Finish();
            }
            catch (Exception error) { errors.Add(error.ToString()); Check("Harness exception", false, error.Message); Finish(); }
        }
        static IEnumerator Wait(double seconds)
        { double until = EditorApplication.timeSinceStartup + seconds; while (EditorApplication.timeSinceStartup < until) yield return null; }
        static IEnumerator Idle()
        {
            double until = EditorApplication.timeSinceStartup + 8;
            while ((journal.IsBusy || workspace.physical.IsSettling || journal.cameraRig.IsTransitioning) && EditorApplication.timeSinceStartup < until) yield return null;
            yield return null;
        }
        static Ray PageRay(JournalSide side, Vector2 uv)
        {
            var anchor = journal.pageView.GetSurface(side); var size = journal.pageView.SurfaceSize(side);
            var world = anchor.TransformPoint(new Vector3(size.x * uv.x, size.y * uv.y, 0));
            return workspace.lens.ScreenPointToRay(workspace.lens.WorldToScreenPoint(world));
        }

        static IEnumerator Execute()
        {
            double until = EditorApplication.timeSinceStartup + 30;
            while (!(workspace = UnityEngine.Object.FindFirstObjectByType<WorkspaceGameplayController>()) || !workspace.Ready ||
                !(commission = UnityEngine.Object.FindFirstObjectByType<CommissionDeskController>()) || !commission.Ready ||
                !(dream = DreamGameplayController.Instance) || !dream.Ready)
            { if (EditorApplication.timeSinceStartup > until) throw new TimeoutException("Night gameplay scene bindings did not become ready."); yield return null; }
            journal = workspace.journal;
            foreach (var adapter in UnityEngine.Object.FindObjectsByType<WorkspaceInputRouter>(FindObjectsSortMode.None)) adapter.enabled = false;
            foreach (var adapter in UnityEngine.Object.FindObjectsByType<PhysicalMouseInputAdapter>(FindObjectsSortMode.None)) adapter.enabled = false;
            foreach (var adapter in UnityEngine.Object.FindObjectsByType<ScrapbookInput>(FindObjectsSortMode.None)) adapter.enabled = false;
            RuntimeInputGate.Reset(); workspace.desk.Begin(); yield return Wait(.4);
            Check("Player save isolated", journal.SavePath.Contains("DeskRevisionValidation"), journal.SavePath);
            Check("Legacy journal schema and original page IDs retained", journal.Document.schemaVersion == 1 && journal.Document.pages.Length == 24 && journal.Document.pages[0].pageId == "journal-page-001");
            var originalPageIds = journal.Document.pages.Select(p => p.pageId).ToArray();
            var catalog = CommissionCatalog.Load();
            var clients = catalog.nightClients;
            Check("Two recurring clients have 8 to 12 authored letters each", clients.Length == 2 && clients.All(c => c.chapters.Length >= 8 && c.chapters.Length <= 12), string.Join("; ", clients.Select(c => c.name + ":" + c.chapters.Length)));
            Check("Commission emotion targets remain data", clients.SelectMany(c => c.chapters).All(l => l.wanted != null && l.wanted.Length >= 2 && l.wanted.Length <= 4));
            var time = UnityEngine.Object.FindFirstObjectByType<DreamTimeController>();
            Require("Existing night lighting controller present", time);
            time.SetNight(false, false, true); yield return Wait(.2);
            string daytimeToken = dream.Data.pending.FirstOrDefault()?.tokenId;
            int daytimeCount = dream.Data.pending.Count;
            Check("Daylight hides collection and rejects catches", dream.collection.VisibleCount == 0 && !dream.TryCatch(daytimeToken) && dream.Data.pending.Count == daytimeCount);
            time.SetNight(true, false, true); yield return Wait(.5);
            Require("Night collection has pending material", dream.Data != null && dream.Data.pending.Count > 0);
            Require("Night collection visual binding present", dream.collection);
            dream.OpenBag();
            Check("Material bag blocks collection actions", dream.BagOpen && !dream.TryCatch(dream.Data.pending[0].tokenId));
            dream.CloseBag();
            Capture("night-collection.png");
            Physics.SyncTransforms();
            var beforeTokens = dream.Data.pending.Select(t => t.tokenId).ToArray();
            int stockBefore = workspace.Service.Items.Count(i => i.assetId.StartsWith("night-", StringComparison.Ordinal));
            bool caught = false;
            Ray? firstCollectionRay = null;
            int reachableTargets = 0;
            var input = workspace.GetComponent<WorkspaceInputRouter>();
            var targets = dream.collection.Collectibles.Where(v => v).ToArray();
            CollectionDiagnostic("CanCollect=" + dream.CanCollect + "; Ready=" + dream.Ready + "; IsNight=" + time.IsNight +
                "; NightAmount=" + time.NightAmount + "; IsNightVisible=" + dream.collection.IsNightVisible +
                "; HasBegun=" + workspace.desk.HasBegun + "; deskBlocking=" + workspace.desk.IsBlocking +
                "; BagOpen=" + dream.BagOpen + "; tutorial=" + AtelierTutorial.Blocking + "; InteractionBusy=" + workspace.InteractionBusy +
                "; editing=" + journal.IsEditing + "; mailPanel=" + commission.PanelOpen + "; mechanism=" + (input && input.IsDraggingMechanism) +
                "; targets=" + targets.Length + "; allChildColliders=" + dream.collection.GetComponentsInChildren<Collider>().Length +
                "; Screen=" + Screen.width + "x" + Screen.height + "; cameraAspect=" + workspace.lens.aspect + "; pixelRect=" + workspace.lens.pixelRect);
            foreach (var collectible in targets)
            {
                var collider = collectible.HitCollider;
                if (!collider) { CollectionDiagnostic("token=" + collectible.TokenId + "; HitCollider=null"); continue; }
                var screen = workspace.lens.WorldToScreenPoint(collider.bounds.center);
                var ray = workspace.lens.ScreenPointToRay(screen);
                bool onScreen = screen.z > 0 && screen.x > 0 && screen.y > 0 && screen.x < Screen.width && screen.y < Screen.height;
                bool ownRayHit = collider.Raycast(ray, out var ownHit, 20);
                bool nearest = Physics.Raycast(ray, out var hit, 3, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                CollectionDiagnostic("token=" + collectible.TokenId + "; kind=" + collectible.VisualKind + "; screen=" + screen.ToString("F4") +
                    "; world=" + collider.bounds.center.ToString("F4") + "; bounds=" + collider.bounds.size.ToString("F4") +
                    "; enabled=" + collider.enabled + "; active=" + collider.gameObject.activeInHierarchy + "; layer=" + collider.gameObject.layer +
                    "; trigger=" + collider.isTrigger + "; onScreen=" + onScreen + "; ownHit=" + ownRayHit +
                    "; ownDistance=" + (ownRayHit ? ownHit.distance.ToString("F4") : "none") +
                    "; nearest=" + (nearest ? hit.collider.GetType().Name + ":" + hit.collider.name + "@" + hit.distance.ToString("F4") + ":components=" +
                        string.Join(",", hit.collider.GetComponents<Component>().Where(c => c).Select(c => c.GetType().Name)) : "none") +
                    "; nearestIsTarget=" + (nearest && hit.collider == collider));
                if (!collider.enabled || !collider.gameObject.activeInHierarchy || !onScreen || !nearest || hit.collider != collider) continue;
                reachableTargets++;
                if (!firstCollectionRay.HasValue) firstCollectionRay = ray;
            }
            File.WriteAllLines(Path.Combine(output, "collection-ray-diagnostics.txt"), collectionDiagnostics);
            Require("Every starter catch is visible and directly reachable from seated camera", targets.Length == beforeTokens.Length && reachableTargets == targets.Length,
                "reachable=" + reachableTargets + "/" + targets.Length + "; pending=" + beforeTokens.Length + "\n" + string.Join("\n", collectionDiagnostics));
            if (firstCollectionRay.HasValue)
            {
                caught = dream.collection.TryClick(firstCollectionRay.Value);
                CollectionDiagnostic("TryClick=" + caught + "; CanCollectAfter=" + dream.CanCollect + "; pendingAfter=" + dream.Data.pending.Count);
                File.WriteAllLines(Path.Combine(output, "collection-ray-diagnostics.txt"), collectionDiagnostics);
            }
            Require("Seated camera ray catches an actual collection collider", caught, string.Join("\n", collectionDiagnostics));
            string removedToken = beforeTokens.FirstOrDefault(id => dream.Data.pending.All(t => t.tokenId != id));
            Check("One click consumes exactly one pending token", removedToken != null && dream.Data.pending.Count == beforeTokens.Length - 1);
            Check("Collection grants one real finite stock instance", workspace.Service.Items.Count(i => i.assetId.StartsWith("night-", StringComparison.Ordinal)) == stockBefore + 1);
            int itemCount = workspace.Service.Items.Count;
            Check("Collected token cannot be claimed twice", !dream.TryCatch(removedToken) && workspace.Service.Items.Count == itemCount);
            var source = workspace.Service.Items.Last(i => i.assetId.StartsWith("night-", StringComparison.Ordinal) && i.owner == CraftOwner.Stock);
            string sourceId = source.instanceId, assetId = source.assetId;
            Require("Material bag starts a real held transaction", dream.UseMaterial(assetId) && workspace.Service.HeldItem != null);
            sourceId = workspace.Service.HeldItem.instanceId;
            Check("Cancel restores the same stock instance", workspace.Cancel() && workspace.Service.GetItem(sourceId)?.owner == CraftOwner.Stock && workspace.Service.Items.Count(i => i.instanceId == sourceId) == 1);

            Require("Enter the existing journal editor", journal.EnterEditing()); yield return Idle();
            Require("Material bag can take restored stock", dream.UseMaterial(assetId));
            sourceId = workspace.Service.HeldItem.instanceId;
            var left = journal.Document.GetPage(JournalSide.Left).pageId;
            var right = journal.Document.GetPage(JournalSide.Right).pageId;
            Require("Collected material places via actual page ray", workspace.TryPlacePointer(PageRay(JournalSide.Left, new Vector2(.45f, .5f))), workspace.Status);
            Check("Placed material keeps its unique source ID", workspace.Service.GetItem(sourceId)?.pageId == left && journal.Document.GetPage(JournalSide.Left).decorations.Count(d => d.instanceId == sourceId) == 1);
            Require("Peel and move collected material across pages", workspace.BeginPeel(sourceId) && workspace.TryPlacePointer(PageRay(JournalSide.Right, new Vector2(.5f, .5f))), workspace.Status);
            Check("Cross-page transfer moves one instance", workspace.Service.GetItem(sourceId)?.pageId == right && journal.Document.AllPages.Sum(p => p.decorations.Count(d => d.instanceId == sourceId)) == 1);
            Check("Global undo and redo preserve one material", workspace.Undo() && workspace.Service.GetItem(sourceId)?.pageId == left && workspace.Redo() && workspace.Service.GetItem(sourceId)?.pageId == right);
            workspace.ClearSelection();
            Capture("collected-material-page.png");

            commission.OpenPanel();
            var letter = commission.Service.OpenLetters.FirstOrDefault();
            if (letter == null) { commission.Service.RequestLetterNow(); letter = commission.Service.OpenLetters.FirstOrDefault(); }
            Require("A story commission is available", letter != null);
            commission.SelectLetter(letter.letterId);
            Require("Select collected material page for a photo", commission.SelectTarget(CraftOwner.JournalPage, "journal", right));
            int coinsBefore = dream.Data.coins;
            int claimsBefore = dream.Data.claimedRewardLetters.Count;
            int inventoryBeforeReply = workspace.Service.Items.Count;
            Require("Send real page photo through commission action", commission.SendActive(), commission.Status);
            Check("Sending preserves material ownership", workspace.Service.GetItem(sourceId)?.pageId == right && journal.Document.AllPages.Sum(p => p.decorations.Count(d => d.instanceId == sourceId)) == 1);
            var sent = commission.Service.Data.letters.First(l => l.letterId == letter.letterId);
            Check("Photo is a persisted PNG", !string.IsNullOrEmpty(sent.photoFile) && File.Exists(Path.Combine(commission.PhotoFolder, sent.photoFile)));
            commission.Service.FastForwardReplies(); yield return null;
            Require("Reply awards coins and one claimed receipt", dream.Data.coins > coinsBefore && dream.Data.claimedRewardLetters.Count == claimsBefore + 1);
            Check("Reply grants usable finite stock", workspace.Service.Items.Count > inventoryBeforeReply && workspace.Service.Items.Any(i => i.owner == CraftOwner.Stock && i.assetId.StartsWith("night-", StringComparison.Ordinal)));
            int coinsAfter = dream.Data.coins, claimedAfter = dream.Data.claimedRewardLetters.Count, inventoryAfter = workspace.Service.Items.Count;
            commission.Service.FastForwardReplies(); yield return null;
            Check("Repeated reply tick does not duplicate rewards", dream.Data.coins == coinsAfter && dream.Data.claimedRewardLetters.Count == claimedAfter && workspace.Service.Items.Count == inventoryAfter);
            Check("Creative undo preserves economy and reward instances", workspace.Undo() && dream.Data.coins == coinsAfter && dream.Data.claimedRewardLetters.Count == claimedAfter && workspace.Service.Items.Count == inventoryAfter);
            Check("Creative redo preserves economy", workspace.Redo() && dream.Data.coins == coinsAfter && dream.Data.claimedRewardLetters.Count == claimedAfter);
            commission.ClosePanel();
            Require("Delete consumed collected material", workspace.Service.DeleteAttached(sourceId));
            Require("Flush whole journal and night branch", journal.Save() && workspace.Service.Flush());
            var loaded = new JournalSaveService(journal.Catalog, journal.SavePath).LoadOrCreate();
            Check("Disk readback preserves economy and collection", loaded.dreamGameplay != null && loaded.dreamGameplay.coins == coinsAfter && loaded.dreamGameplay.claimedRewardLetters.Count == claimedAfter && loaded.dreamGameplay.pending.Count == dream.Data.pending.Count);
            Check("Disk readback retains finite-material deletion tombstone", loaded.craftWorkspace.items.Count(i => i.instanceId == sourceId) == 1 && loaded.craftWorkspace.items.First(i => i.instanceId == sourceId).owner == CraftOwner.Deleted);
            Check("Disk readback retains original schema and pages", loaded.schemaVersion == 1 && loaded.pages.Select(p => p.pageId).SequenceEqual(originalPageIds));
            Check("Atomic ownership validates after night loop", CraftWorkspaceData.Validate(loaded, out string craftError), craftError);
            Check("Commission ledger validates after night loop", CommissionDeskData.Validate(loaded, out string commissionError), commissionError);
            Capture("night-loop-complete.png");
        }

        static void Capture(string name)
        {
            var target = new RenderTexture(1920, 1080, 24);
            var pixels = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                RenderPipeline.SubmitRenderRequest(workspace.lens, new RenderPipeline.StandardRequest { destination = target });
                RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
                var path = Path.Combine(output, name); File.WriteAllBytes(path, pixels.EncodeToPNG()); captures.Add(path);
            }
            finally { RenderTexture.active = previous; UnityEngine.Object.Destroy(pixels); target.Release(); UnityEngine.Object.Destroy(target); }
        }

        static void CollectionDiagnostic(string message)
        { collectionDiagnostics.Add(message); Debug.Log("[NightCollectionRay] " + message); }

        static void Finish()
        {
            if (finishing) return; finishing = true; running = false; SessionState.SetBool(Pending, false);
            Application.logMessageReceived -= Log;
            bool pass = checks.Count > 0 && checks.All(c => c.passed) && errors.Count == 0;
            var report = new Report
            {
                timestampUtc = DateTime.UtcNow.ToString("o"), unityVersion = Application.unityVersion, gpu = SystemInfo.graphicsDeviceName,
                savePath = journal ? journal.SavePath : null, passed = pass, actualPlayMode = Application.isPlaying,
                nativeInputValidated = false, freshProcessRestart = false, renderedFrames = Time.frameCount,
                elapsedSeconds = EditorApplication.timeSinceStartup - started,
                inputScope = "Runtime actions and actual camera/collider rays in saved-scene Unity Play Mode; no native mouse input.",
                screenshotScope = "GPU scene captures exclude IMGUI. Disk readback is not a fresh-process restart.",
                checks = checks.ToArray(), consoleErrors = errors.ToArray(), screenshots = captures.ToArray(), collectionDiagnostics = collectionDiagnostics.ToArray(),
                requiresManualVerification = new[] { "Native mouse click and drag feel", "IMGUI text layout at supported resolutions", "Multi-hour real-world replenishment pacing", "Fresh standalone process restart", "Full 20-letter narrative playthrough" }
            };
            Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "result.json"), JsonUtility.ToJson(report, true));
            Debug.Log("[NightGameplayValidation] Completed " + (pass ? "PASS" : "FAIL"));
            if (Application.isBatchMode) EditorApplication.Exit(pass ? 0 : 1); else EditorApplication.isPlaying = false;
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace VintageJournal
{
    /// <summary>Opt-in Windows two-process smoke evidence. Runtime actions and real rays; never native mouse input.</summary>
    public sealed class NightGameplayPlayerProbe : MonoBehaviour
    {
        const string Flag = "--night-gameplay-player-probe";
        string output, runId;
        bool reload, finished;
        float started;
        WorkspaceGameplayController workspace;
        JournalController journal;
        CommissionDeskController commission;
        DreamGameplayController dream;
        readonly Stack<IEnumerator> routines = new Stack<IEnumerator>();
        readonly List<CheckRow> checks = new List<CheckRow>();
        readonly List<string> errors = new List<string>();
        readonly List<string> screenshots = new List<string>();

        [Serializable] public sealed class CheckRow { public string name, detail; public bool passed; }
        [Serializable] public sealed class Expected
        {
            public string savePath, runId, sourceId, pageId, assetId, letterId, photoFile;
            public int coins, stamps, totalItemCount;
            public string[] pendingTokens, claimedRewards, dreamItems;
        }
        [Serializable] public sealed class Report
        {
            public bool passed, actualWindowsPlayer, nativeInputValidated, freshProcessReload;
            public string timestampUtc, unityVersion, gpu, savePath, runId, scope;
            public int renderedFrames;
            public float elapsedSeconds;
            public CheckRow[] checks;
            public string[] errors, screenshots;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, Flag);
            if (index < 0 || Application.isEditor) return;
            try
            {
                if (Application.platform != RuntimePlatform.WindowsPlayer || index + 1 >= args.Length ||
                    !args.Contains("--desk-revision-validation") || !args.Contains("--night-gameplay-validation"))
                    throw new ArgumentException("Windows probe needs output folder, --desk-revision-validation and --night-gameplay-validation.");
                string argument = args.FirstOrDefault(a => a.StartsWith("--revision-run=", StringComparison.Ordinal));
                if (argument == null || !Guid.TryParseExact(argument.Substring(15), "N", out var guid))
                    throw new ArgumentException("A 32-digit --revision-run=<GUID> is required; reuse it only for this probe's reload process.");
                string folder = Path.GetFullPath(args[index + 1]);
                // Evidence writes stay in the specifically authorized workspace, independently of the build's location.
                string allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "docs", "night-gameplay", "player"));
                if (!string.Equals(folder.TrimEnd(Path.DirectorySeparatorChar), allowed, StringComparison.OrdinalIgnoreCase) &&
                    !folder.StartsWith(allowed + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Probe evidence output must be inside " + allowed);
                var probe = new GameObject("Opt-in night gameplay player evidence").AddComponent<NightGameplayPlayerProbe>();
                probe.output = folder; probe.runId = guid.ToString("N"); probe.reload = args.Contains("--probe-reload");
                Application.runInBackground = true;
            }
            catch (Exception error)
            { Debug.LogError("[NightPlayerProbe] Refused probe configuration: " + error.Message); Application.Quit(1); }
        }

        void Start()
        {
            Directory.CreateDirectory(output); started = Time.realtimeSinceStartup;
            Application.logMessageReceived += Log;
            routines.Push(Execute());
        }
        void Log(string message, string trace, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message + "\n" + trace); }
        void Update()
        {
            if (finished) return;
            if (Time.realtimeSinceStartup - started > 120) { Check("Probe timeout", false, "120 seconds"); Finish(); return; }
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
            catch (Exception error)
            { errors.Add(error.ToString()); Check("Probe exception", false, error.Message); Finish(); }
        }
        void Check(string name, bool passed, string detail = "")
        { checks.Add(new CheckRow { name = name, passed = passed, detail = detail }); Debug.Log("[NightPlayerProbe] " + (passed ? "PASS " : "FAIL ") + name + " " + detail); }
        void Require(string name, bool passed, string detail = "")
        { Check(name, passed, detail); if (!passed) throw new InvalidOperationException(name + ": " + detail); }
        IEnumerator Wait(float seconds)
        { float until = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < until) yield return null; }
        IEnumerator Idle()
        {
            float until = Time.realtimeSinceStartup + 9;
            while ((journal.IsBusy || workspace.physical.IsSettling || journal.cameraRig.IsTransitioning) && Time.realtimeSinceStartup < until) yield return null;
            Require("Camera and workspace settled", !journal.IsBusy && !workspace.physical.IsSettling && !journal.cameraRig.IsTransitioning);
            yield return null;
        }
        IEnumerator Shot(string filename)
        {
            yield return Wait(.3f);
            string path = Path.Combine(output, filename + ".png");
            ScreenCapture.CaptureScreenshot(path);
            float until = Time.realtimeSinceStartup + 5;
            // CaptureScreenshot runs at frame end and includes IMGUI in the actual player.
            yield return Wait(.3f);
            while (!File.Exists(path) && Time.realtimeSinceStartup < until) yield return null;
            Require("Standalone UI screenshot exists: " + filename, File.Exists(path)); screenshots.Add(path);
        }
        Ray PageRay(JournalSide side, Vector2 uv)
        {
            var anchor = journal.pageView.GetSurface(side); var size = journal.pageView.SurfaceSize(side);
            var world = anchor.TransformPoint(new Vector3(size.x * uv.x, size.y * uv.y, 0));
            return workspace.lens.ScreenPointToRay(workspace.lens.WorldToScreenPoint(world));
        }
        static bool Same(string[] a, string[] b) => a != null && b != null && a.SequenceEqual(b, StringComparer.Ordinal);
        string[] PendingTokens() => dream.Data.pending.OrderBy(t => t.tokenId, StringComparer.Ordinal).Select(t => JsonUtility.ToJson(t)).ToArray();
        string[] DreamItems() => workspace.Service.Items.Where(i => i.instanceId.StartsWith("dream-", StringComparison.Ordinal))
            .OrderBy(i => i.instanceId, StringComparer.Ordinal).Select(i => JsonUtility.ToJson(i)).ToArray();
        string[] Claims() => dream.Data.claimedRewardLetters.OrderBy(id => id, StringComparer.Ordinal).ToArray();

        IEnumerator Execute()
        {
            float until = Time.realtimeSinceStartup + 30;
            while (!(workspace = FindFirstObjectByType<WorkspaceGameplayController>()) || !workspace.Ready ||
                !(commission = FindFirstObjectByType<CommissionDeskController>()) || !commission.Ready ||
                !(dream = DreamGameplayController.Instance) || !dream.Ready)
            { if (Time.realtimeSinceStartup > until) throw new TimeoutException("Night gameplay runtime bindings not ready."); yield return null; }
            journal = workspace.journal;
            string expectedSave = Path.GetFullPath(Path.Combine(Application.persistentDataPath, "DeskRevisionValidation", runId, "journal-v1.json"));
            Require("Actual Windows standalone process", !Application.isEditor && Application.platform == RuntimePlatform.WindowsPlayer);
            Require("Save is the exact isolated GUID path", string.Equals(journal.SavePath, expectedSave, StringComparison.OrdinalIgnoreCase), journal.SavePath);
            foreach (var adapter in FindObjectsByType<WorkspaceInputRouter>(FindObjectsSortMode.None)) adapter.enabled = false;
            foreach (var adapter in FindObjectsByType<PhysicalMouseInputAdapter>(FindObjectsSortMode.None)) adapter.enabled = false;
            foreach (var adapter in FindObjectsByType<ScrapbookInput>(FindObjectsSortMode.None)) adapter.enabled = false;
            RuntimeInputGate.Reset(); workspace.desk.Begin();
            var time = FindFirstObjectByType<DreamTimeController>(); Require("Existing time-of-day controller", time);
            time.SetNight(true, false, true); yield return Wait(.5f);
            if (reload) { yield return ReloadChecks(); yield break; }
            Require("Fresh isolated slice contains starter catch tokens", dream.Data.pending.Count == 3 && dream.Data.coins == 0 && dream.Data.claimedRewardLetters.Count == 0);
            yield return Shot("night-desk-ui");

            Physics.SyncTransforms();
            var targets = dream.collection.Collectibles.Where(v => v && v.HitCollider && v.HitCollider.enabled).ToArray();
            Ray? catchRay = null;
            int reachable = 0;
            foreach (var target in targets)
            {
                var screen = workspace.lens.WorldToScreenPoint(target.HitCollider.bounds.center);
                var ray = workspace.lens.ScreenPointToRay(screen);
                bool visible = screen.z > 0 && screen.x > 0 && screen.y > 0 && screen.x < Screen.width && screen.y < Screen.height;
                bool first = Physics.Raycast(ray, out var hit, 3, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && hit.collider == target.HitCollider;
                Check("Seated catch collider: " + target.VisualKind, visible && first, "screen=" + screen.ToString("F2") + "; token=" + target.TokenId);
                if (visible && first) { reachable++; if (!catchRay.HasValue) catchRay = ray; }
            }
            Require("Every starter catch has a clear actual camera ray", targets.Length == 3 && reachable == 3);
            string[] tokensBefore = dream.Data.pending.Select(t => t.tokenId).ToArray();
            Require("Catch through actual collider ray", catchRay.HasValue && dream.collection.TryClick(catchRay.Value));
            string removedToken = tokensBefore.FirstOrDefault(id => dream.Data.pending.All(t => t.tokenId != id));
            Require("Exactly one pending token consumed", removedToken != null && dream.Data.pending.Count == tokensBefore.Length - 1);
            int itemCount = workspace.Service.Items.Count;
            Check("Second catch of consumed token is rejected", !dream.TryCatch(removedToken) && workspace.Service.Items.Count == itemCount);
            var source = workspace.Service.Items.Last(i => i.instanceId.StartsWith("dream-catch-", StringComparison.Ordinal));
            string sourceId = source.instanceId, assetId = source.assetId;
            dream.OpenBag(); yield return Shot("night-material-bag-ui"); dream.CloseBag();

            Require("Enter existing journal editor", journal.EnterEditing()); yield return Idle();
            Require("Take finite collected material", dream.UseMaterial(assetId), dream.Status);
            sourceId = workspace.Service.HeldItem.instanceId;
            string pageId = journal.Document.GetPage(JournalSide.Left).pageId;
            Require("Place via actual page ray", workspace.TryPlacePointer(PageRay(JournalSide.Left, new Vector2(.45f, .5f))), workspace.Status);
            Require("Unique collected instance is on page", workspace.Service.GetItem(sourceId)?.pageId == pageId &&
                journal.Document.AllPages.Sum(p => p.decorations.Count(d => d.instanceId == sourceId)) == 1);
            workspace.ClearSelection(); yield return Shot("night-crafted-page-ui");

            commission.OpenPanel();
            var letter = commission.Service.OpenLetters.FirstOrDefault();
            if (letter == null) { commission.Service.RequestLetterNow(); letter = commission.Service.OpenLetters.FirstOrDefault(); }
            Require("Recurring night client is available", letter != null && !string.IsNullOrEmpty(letter.clientId));
            commission.SelectLetter(letter.letterId);
            Require("Select crafted page", commission.SelectTarget(CraftOwner.JournalPage, "journal", pageId));
            Require("Send real page photo", commission.SendActive(), commission.Status);
            var sent = commission.Service.Data.letters.First(l => l.letterId == letter.letterId);
            string photo = Path.Combine(commission.PhotoFolder, sent.photoFile ?? string.Empty);
            Require("Sent PNG exists", !string.IsNullOrEmpty(sent.photoFile) && File.Exists(photo), photo);
            int coinsBefore = dream.Data.coins, claimsBefore = dream.Data.claimedRewardLetters.Count;
            commission.Service.FastForwardReplies(); yield return null;
            Require("Reply grants coins and one atomic reward receipt", dream.Data.coins > coinsBefore && dream.Data.claimedRewardLetters.Count == claimsBefore + 1);
            int coinsAfter = dream.Data.coins, claimsAfter = dream.Data.claimedRewardLetters.Count, itemsAfter = workspace.Service.Items.Count;
            commission.Service.FastForwardReplies(); yield return null;
            Require("Repeated reply processing does not duplicate rewards", dream.Data.coins == coinsAfter && dream.Data.claimedRewardLetters.Count == claimsAfter && workspace.Service.Items.Count == itemsAfter);
            commission.ClosePanel(); commission.OpenPanel(); yield return Shot("night-client-reply-ui");
            Require("Original crafted page remains owned", workspace.Service.GetItem(sourceId)?.owner == CraftOwner.JournalPage && workspace.Service.GetItem(sourceId)?.pageId == pageId);
            Require("Atomic save flushed", journal.Save() && workspace.Service.Flush());
            Require("Registry validates before process exit", CraftWorkspaceData.Validate(journal.Document, out string registryMessage), registryMessage);
            Require("Gameplay ledger validates before process exit", DreamGameplayData.Validate(journal.Document, out string dreamMessage), dreamMessage);
            var expected = new Expected { savePath = journal.SavePath, runId = runId, sourceId = sourceId, assetId = assetId, pageId = pageId,
                letterId = letter.letterId, photoFile = photo, coins = dream.Data.coins, stamps = commission.Service.Data.stamps,
                totalItemCount = workspace.Service.Items.Count, pendingTokens = PendingTokens(), claimedRewards = Claims(), dreamItems = DreamItems() };
            File.WriteAllText(Path.Combine(output, "expected.json"), JsonUtility.ToJson(expected, true));
            Check("Expected state saved for separate process", true);
        }

        IEnumerator ReloadChecks()
        {
            string path = Path.Combine(output, "expected.json");
            Require("First-process expected evidence exists", File.Exists(path));
            var expected = JsonUtility.FromJson<Expected>(File.ReadAllText(path));
            Require("Reload uses the same isolated run and save", expected != null && expected.runId == runId && string.Equals(expected.savePath, journal.SavePath, StringComparison.OrdinalIgnoreCase));
            Require("Coins and stamps survive fresh process", dream.Data.coins == expected.coins && commission.Service.Data.stamps == expected.stamps);
            Require("Reward receipts survive without duplication", Same(Claims(), expected.claimedRewards));
            Require("Consumed tokens remain consumed", Same(PendingTokens(), expected.pendingTokens));
            Require("Finite material IDs and ownership survive exactly", Same(DreamItems(), expected.dreamItems) && workspace.Service.Items.Count == expected.totalItemCount);
            var source = workspace.Service.GetItem(expected.sourceId);
            Require("Same crafted instance restored on same page", source != null && source.assetId == expected.assetId && source.owner == CraftOwner.JournalPage &&
                source.pageId == expected.pageId && journal.Document.AllPages.Sum(p => p.decorations.Count(d => d.instanceId == expected.sourceId)) == 1);
            var letter = commission.Service.Data.letters.FirstOrDefault(l => l.letterId == expected.letterId);
            Require("Reply and photo survive process restart", letter != null && letter.status == CommissionLetterStatus.Replied && File.Exists(expected.photoFile));
            Require("Reloaded registry is valid", CraftWorkspaceData.Validate(journal.Document, out string registryMessage), registryMessage);
            Require("Reloaded ledger is valid", DreamGameplayData.Validate(journal.Document, out string dreamMessage), dreamMessage);
            dream.OpenBag(); yield return Shot("night-reloaded-bag-ui"); dream.CloseBag();
            Require("Reload UI did not award new rewards", dream.Data.coins == expected.coins && Same(Claims(), expected.claimedRewards) && Same(DreamItems(), expected.dreamItems));
            Require("Reload snapshot can be saved atomically", journal.Save() && workspace.Service.Flush());
        }

        void Finish()
        {
            if (finished) return; finished = true;
            bool passed = checks.Count > 0 && checks.All(c => c.passed) && errors.Count == 0;
            var report = new Report { passed = passed, actualWindowsPlayer = !Application.isEditor && Application.platform == RuntimePlatform.WindowsPlayer,
                nativeInputValidated = false, freshProcessReload = reload && !Application.isEditor, timestampUtc = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion, gpu = SystemInfo.graphicsDeviceName, savePath = journal ? journal.SavePath : null, runId = runId,
                scope = "Actual Windows player GPU frames with IMGUI; input-independent runtime actions and real camera/collider rays. No native mouse input.",
                renderedFrames = Time.frameCount, elapsedSeconds = Time.realtimeSinceStartup - started, checks = checks.ToArray(), errors = errors.ToArray(), screenshots = screenshots.ToArray() };
            File.WriteAllText(Path.Combine(output, reload ? "reload.json" : "result.json"), JsonUtility.ToJson(report, true));
            Application.logMessageReceived -= Log;
            Debug.Log("[NightPlayerProbe] Completed " + (passed ? "PASS" : "FAIL")); Application.Quit(passed ? 0 : 1);
        }
    }
}

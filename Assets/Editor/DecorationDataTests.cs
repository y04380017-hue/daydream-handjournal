using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VintageJournal.Editor
{
    /// <summary>Real Unity serialization/filesystem and page-command checks. Never writes the player save.</summary>
    public static class DecorationDataTests
    {
        [Serializable] public sealed class Result { public string name, detail; public bool passed; }
        [Serializable] public sealed class Report
        {
            public string utc, unityVersion, directory;
            public bool passed;
            public Result[] checks;
        }
        [Serializable] sealed class LegacyDecoration
        {
            public string instanceId, assetId, type;
            public Vector2 position, scale;
            public float rotationDegrees;
            public int sortOrder;
            public string payloadJson;
        }
        [Serializable] sealed class LegacyPage { public string pageId, paperId; public List<LegacyDecoration> decorations; }
        [Serializable] sealed class LegacyDocument { public int schemaVersion = 1, currentSpread; public LegacyPage[] pages; }

        [MenuItem("Vintage Journal/Stage 3/Run decoration data checks")]
        public static void RunFromMenu()
        { if (!RunDataChecks()) throw new InvalidOperationException("Decoration data checks failed; see docs/stage3-logs/data-tests.json."); }

        public static bool RunDataChecks()
        {
            var results = new List<Result>();
            string directory = Path.Combine(Application.persistentDataPath, "Stage3Validation", "DataChecks",
                DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            Action<string, Func<bool>, string> check = (name, action, detail) => {
                try { results.Add(new Result { name = name, passed = action(), detail = detail }); }
                catch (Exception error) { results.Add(new Result { name = name, passed = false, detail = error.ToString() }); }
            };
            var catalog = JournalPaperCatalog.Load();
            var document = JournalDocument.CreateDefault(catalog);
            var legacy = new LegacyDocument { currentSpread = 3, pages = document.pages.Select(page => new LegacyPage {
                pageId = page.pageId, paperId = page.paperId, decorations = new List<LegacyDecoration>() }).ToArray() };
            legacy.pages[0].decorations.Add(new LegacyDecoration {
                instanceId = "legacy-element", assetId = "future-resource-not-installed", type = "future-type",
                position = new Vector2(.3f, .65f), scale = new Vector2(.7f, .7f), rotationDegrees = 17.5f,
                sortOrder = -2, payloadJson = "{\"text\":\"保留旧手账\",\"futureField\":19}"
            });
            string legacyText = JsonUtility.ToJson(legacy, true);
            string path = Path.Combine(directory, "legacy-v1.json");
            File.WriteAllText(path, legacyText);
            var store = new JournalSaveService(catalog, path);
            var loaded = store.LoadOrCreate();
            check("Schema v1 remains backward-compatible", () => loaded.schemaVersion == 1 && JournalDocument.CurrentSchemaVersion == 1,
                "Stage 3 adds reserved decoration fields instead of invalidating Stage 2 saves.");
            check("Legacy paper and current spread survive", () => loaded.currentSpread == 3 && loaded.pages.Length == 24 &&
                loaded.pages.Select(page => page.paperId).SequenceEqual(document.pages.Select(page => page.paperId)),
                "All page instances and paper references retained.");
            check("Legacy decoration receives containing page owner", () => loaded.pages[0].decorations[0].pageId == loaded.pages[0].pageId,
                "An absent additive owner field comes from the containing page, never screen pixels.");
            check("Legacy size and tape length default safely", () => loaded.pages[0].decorations[0].size == Vector2.zero &&
                Mathf.Approximately(loaded.pages[0].decorations[0].length, 1), "Zero legacy size delegates to catalogue defaults; length defaults to one.");
            check("Unknown assets and future payload are not deleted", () => loaded.pages[0].decorations[0].assetId == "future-resource-not-installed" &&
                loaded.pages[0].decorations[0].type == "future-type" && loaded.pages[0].decorations[0].payloadJson.Contains("保留旧手账"),
                "Unavailable resources remain data so a later install can recover them.");
            check("Reading legacy JSON never rewrites original bytes", () => File.ReadAllText(path) == legacyText && string.IsNullOrEmpty(store.PreservedFilePath),
                "Additive migration is in memory until a deliberate save.");
            check("Saving migrated v1 preserves exact old backup", () => store.Save(loaded) && File.ReadAllText(path + ".backup") == legacyText,
                "Existing EFS-compatible backup protocol remains byte-exact.");
            check("Old reserved records with no ID migrate deterministically", () => {
                var old = JsonUtility.FromJson<LegacyDocument>(legacyText);
                old.pages[0].decorations[0].instanceId = string.Empty;
                old.pages[1].decorations.Add(new LegacyDecoration {
                    instanceId = "legacy-" + old.pages[0].pageId + "-000", assetId = "future-resource",
                    type = "future-type", position = new Vector2(.2f, .7f), scale = Vector2.one, payloadJson = "{\"retain\":true}" });
                string raw = JsonUtility.ToJson(old), p = Path.Combine(directory, "legacy-no-element-id.json");
                File.WriteAllText(p, raw);
                var a = new JournalSaveService(catalog, p).LoadOrCreate();
                var b = new JournalSaveService(catalog, p).LoadOrCreate();
                var migrated = a.pages[0].decorations[0];
                return migrated.instanceId == b.pages[0].decorations[0].instanceId &&
                    migrated.instanceId.StartsWith("legacy-" + old.pages[0].pageId + "-000", StringComparison.Ordinal) &&
                    migrated.instanceId != a.pages[1].decorations[0].instanceId && migrated.pageId == a.pages[0].pageId &&
                    migrated.assetId == "future-resource-not-installed" && migrated.payloadJson.Contains("保留旧手账") &&
                    File.ReadAllText(p) == raw;
            }, "Legacy IDs are stable across reads and avoid explicit IDs on later pages, without rewriting the old file.");

            var placed = document.pages[0];
            placed.decorations.Add(Item(placed.pageId, "tape-roundtrip"));
            placed.decorations[0].size = new Vector2(.14f, .055f);
            placed.decorations[0].scale = new Vector2(1.25f, 1.25f);
            placed.decorations[0].position = new Vector2(.36f, .64f);
            placed.decorations[0].rotationDegrees = 37.5f;
            placed.decorations[0].length = 2.2f;
            placed.decorations[0].sortOrder = 9;
            string roundtripPath = Path.Combine(directory, "decorations.json");
            var writer = new JournalSaveService(catalog, roundtripPath);
            check("Placed decoration writes to isolated persistent data", () => writer.Save(document) && File.Exists(roundtripPath) &&
                roundtripPath.StartsWith(Application.persistentDataPath, StringComparison.OrdinalIgnoreCase),
                "No test writes Application.dataPath or the ordinary player journal.");
            var reloaded = new JournalSaveService(catalog, roundtripPath).LoadOrCreate();
            check("Owner, local position and resource survive disk load", () => reloaded.pages[0].decorations[0].pageId == placed.pageId &&
                reloaded.pages[0].decorations[0].position == placed.decorations[0].position &&
                reloaded.pages[0].decorations[0].assetId == placed.decorations[0].assetId, "Stable page-local data, independent of camera or resolution.");
            check("Rotation, scale, base dimensions, length and layer survive", () => JsonUtility.ToJson(reloaded.pages[0].decorations[0]) == JsonUtility.ToJson(placed.decorations[0]),
                "All additive Stage 3 fields and older payload fields roundtrip together.");
            check("Non-current pages retain their own decorations", () => {
                var clone = document.DeepClone(); clone.currentSpread = 8;
                clone.pages[17].decorations.Add(Item(clone.pages[17].pageId, "right-hidden-page"));
                string p = Path.Combine(directory, "other-spread.json"); var s = new JournalSaveService(catalog, p);
                if (!s.Save(clone)) return false;
                var copy = new JournalSaveService(catalog, p).LoadOrCreate();
                return copy.currentSpread == 8 && copy.pages[0].decorations.Count == 1 && copy.pages[17].decorations.Count == 1 &&
                    copy.pages[0].decorations[0].pageId != copy.pages[17].decorations[0].pageId;
            }, "Save includes all pages, not just the currently displayed spread.");

            Action<string, Action<JournalDocument>> reject = (name, mutate) => check(name, () => {
                string before = File.ReadAllText(roundtripPath); var bad = document.DeepClone(); mutate(bad);
                return !writer.Save(bad) && File.ReadAllText(roundtripPath) == before;
            }, "Malformed data is rejected before the valid primary is changed.");
            reject("Duplicate decoration IDs across different pages rejected", bad => {
                var duplicate = JsonUtility.FromJson<JournalDecorationData>(JsonUtility.ToJson(bad.pages[0].decorations[0]));
                duplicate.pageId = bad.pages[1].pageId; bad.pages[1].decorations.Add(duplicate);
            });
            reject("Missing decoration instance ID rejected", bad => bad.pages[0].decorations[0].instanceId = string.Empty);
            reject("Non-finite base size rejected", bad => bad.pages[0].decorations[0].size.x = float.NaN);
            reject("Partially empty base size rejected", bad => bad.pages[0].decorations[0].size = new Vector2(.1f, 0));
            reject("Negative base size rejected", bad => bad.pages[0].decorations[0].size.y = -.1f);
            reject("Negative tape length rejected", bad => bad.pages[0].decorations[0].length = -1);
            reject("Non-finite tape length rejected", bad => bad.pages[0].decorations[0].length = float.PositiveInfinity);
            reject("Out-of-page position rejected", bad => bad.pages[0].decorations[0].position.y = 1.01f);
            reject("Non-positive scale rejected", bad => bad.pages[0].decorations[0].scale.x = 0);
            check("Containing page repairs stale owner field", () => {
                var clone = document.DeepClone(); clone.pages[0].decorations[0].pageId = clone.pages[8].pageId;
                return writer.Save(clone) && clone.pages[0].decorations[0].pageId == clone.pages[0].pageId;
            }, "The owning page collection is authoritative when an older record lacks or misstates pageId.");

            check("Queued saves coalesce before filesystem work starts", () => {
                string p = Path.Combine(directory, "queued-coalesced.json"); var s = new JournalSaveService(catalog, p);
                var queue = new DecorationSaveQueue(s); var copy = document.DeepClone();
                for (int i = 0; i < 12; i++) { copy.currentSpread = i; if (!queue.Enqueue(copy)) return false; }
                bool preparationOnly = !File.Exists(p) && queue.IsPending && queue.CompletedCount == 0;
                // Further document edits must not alter the accepted immutable snapshot.
                copy.currentSpread = 0; copy.pages[0].decorations.Clear();
                if (!queue.Flush(5000)) return false;
                var restored = new JournalSaveService(catalog, p).LoadOrCreate();
                return preparationOnly && !queue.IsPending && queue.CompletedCount == 1 &&
                    restored.currentSpread == 11 && restored.pages[0].decorations.Count == 1;
            }, "An action only validates/serializes on the main thread; twelve pending states become one immutable latest disk commit.");
            check("Running writer commits newest pending state in order", () => {
                string p = Path.Combine(directory, "queued-running-latest.json"); var s = new JournalSaveService(catalog, p);
                var queue = new DecorationSaveQueue(s); var copy = document.DeepClone(); copy.currentSpread = 0;
                if (!queue.Enqueue(copy)) return false; queue.Tick();
                for (int i = 1; i <= 8; i++) { copy.currentSpread = i; if (!queue.Enqueue(copy)) return false; }
                if (!queue.Flush(5000)) return false;
                var restored = new JournalSaveService(catalog, p).LoadOrCreate();
                var previous = JsonUtility.FromJson<JournalDocument>(File.ReadAllText(p + ".backup"));
                return queue.CompletedCount == 2 && restored.currentSpread == 8 && previous.currentSpread == 0;
            }, "One active generation and one latest pending generation are written serially; the previous exact state remains recoverable.");
            check("Worker commit consumes immutable JSON without Unity access", () => {
                string p = Path.Combine(directory, "prepared-worker.json"); var s = new JournalSaveService(catalog, p);
                var copy = document.DeepClone(); copy.currentSpread = 4;
                var snapshot = s.PrepareSnapshot(copy); if (snapshot == null) return false;
                int mainThread = System.Threading.Thread.CurrentThread.ManagedThreadId, workerThread = mainThread;
                copy.currentSpread = 7; copy.pages[0].decorations.Clear();
                var task = System.Threading.Tasks.Task.Run(() => { workerThread = System.Threading.Thread.CurrentThread.ManagedThreadId; return s.CommitPrepared(snapshot); });
                if (!task.Wait(5000) || !task.Result) return false; s.PublishCommitResult();
                var restored = new JournalSaveService(catalog, p).LoadOrCreate();
                return workerThread != mainThread && restored.currentSpread == 4 && restored.pages[0].decorations.Count == 1;
            }, "Actual background thread writes only the prepared payload, not a live document or Unity resource.");
            check("Older worker payload cannot overwrite a newer generation", () => {
                string p = Path.Combine(directory, "prepared-order.json"); var s = new JournalSaveService(catalog, p);
                var copy = document.DeepClone(); copy.currentSpread = 1; var older = s.PrepareSnapshot(copy);
                copy.currentSpread = 9; var newer = s.PrepareSnapshot(copy);
                var latestTask = System.Threading.Tasks.Task.Run(() => s.CommitPrepared(newer));
                if (!latestTask.Wait(5000) || !latestTask.Result) return false;
                var staleTask = System.Threading.Tasks.Task.Run(() => s.CommitPrepared(older));
                if (!staleTask.Wait(5000) || !staleTask.Result) return false;
                return new JournalSaveService(catalog, p).LoadOrCreate().currentSpread == 9;
            }, "Monotonic prepared sequences protect against stale commits, including callers mixing queued and synchronous saves.");
            check("Legacy synchronous save wins over an older queued state", () => {
                string p = Path.Combine(directory, "sync-after-queue.json"); var s = new JournalSaveService(catalog, p);
                var queue = new DecorationSaveQueue(s); var copy = document.DeepClone(); copy.currentSpread = 2;
                if (!queue.Enqueue(copy)) return false; copy.currentSpread = 6;
                if (!s.Save(copy) || !queue.Flush(5000)) return false;
                return !queue.IsPending && new JournalSaveService(catalog, p).LoadOrCreate().currentSpread == 6;
            }, "The existing public Save method remains synchronous and cannot be reverted by an older queued snapshot.");
            check("Invalid queued data never changes valid primary", () => {
                string p = Path.Combine(directory, "invalid-queue.json"); var s = new JournalSaveService(catalog, p);
                if (!s.Save(document.DeepClone())) return false; string exact = File.ReadAllText(p);
                var queue = new DecorationSaveQueue(s); var bad = document.DeepClone(); bad.pages[0].decorations[0].position.x = -2;
                return !queue.Enqueue(bad) && !queue.IsPending && !queue.Flush(100) && File.ReadAllText(p) == exact;
            }, "The worker only receives fully validated snapshots; an invalid request is reported before any I/O is scheduled.");
            check("A zero-time flush does not discard accepted state", () => {
                string p = Path.Combine(directory, "zero-timeout-queue.json"); var s = new JournalSaveService(catalog, p);
                var queue = new DecorationSaveQueue(s); var copy = document.DeepClone(); copy.currentSpread = 10;
                if (!queue.Enqueue(copy)) return false; bool finished = queue.Flush(0);
                bool retained = finished || queue.IsPending;
                return retained && queue.Flush(5000) && !queue.IsPending && new JournalSaveService(catalog, p).LoadOrCreate().currentSpread == 10;
            }, "If a flush deadline expires, the active/pending generation remains queued and a later flush durably completes it.");
            check("Background commits retain interruption recovery protocol", () => {
                string p = Path.Combine(directory, "queued-recovery.json"); var s = new JournalSaveService(catalog, p);
                var queue = new DecorationSaveQueue(s); var copy = document.DeepClone(); copy.currentSpread = 3;
                if (!queue.Enqueue(copy) || !queue.Flush(5000)) return false;
                string exactPrevious = File.ReadAllText(p); copy.currentSpread = 8;
                if (!queue.Enqueue(copy) || !queue.Flush(5000) || File.ReadAllText(p + ".backup") != exactPrevious) return false;
                File.WriteAllText(p, "{ interrupted background primary");
                var reader = new JournalSaveService(catalog, p); var restored = reader.LoadOrCreate();
                return restored.currentSpread == 3 && restored.pages[0].decorations.Count == 1 &&
                    File.Exists(reader.PreservedFilePath) && File.ReadAllText(p + ".backup") == exactPrevious;
            }, "Moving I/O off the main thread does not weaken exact previous-generation backup or damaged-primary recovery.");
            check("Snapshot preparation rejects use on the worker thread", () => {
                var s = new JournalSaveService(catalog, Path.Combine(directory, "wrong-thread.json"));
                var copy = document.DeepClone();
                var task = System.Threading.Tasks.Task.Run(() => {
                    try { s.PrepareSnapshot(copy); return false; }
                    catch (InvalidOperationException) { return true; }
                });
                return task.Wait(5000) && task.Result && !File.Exists(s.SavePath);
            }, "Validation and Unity JsonUtility preparation stay on the owning Unity thread; only CommitPrepared runs in the worker.");
            check("Unchanged queued state performs no repeated disk commit", () => {
                string p = Path.Combine(directory, "queue-deduplicate-committed.json"); var s = new JournalSaveService(catalog, p);
                var queue = new DecorationSaveQueue(s); var copy = document.DeepClone();
                if (!queue.Enqueue(copy) || !queue.Flush(5000)) return false;
                for (int i = 0; i < 10; i++) if (!queue.Enqueue(copy.DeepClone())) return false;
                queue.Tick();
                return queue.Flush(5000) && !queue.IsPending && queue.CompletedCount == 1 && !File.Exists(p + ".backup");
            }, "Exit, quit and duplicate autosave requests reuse a successfully committed identical payload instead of repeating EFS flush work.");
            check("Returning to the active snapshot discards a stale pending change", () => {
                string p = Path.Combine(directory, "queue-deduplicate-active.json"); var s = new JournalSaveService(catalog, p);
                var queue = new DecorationSaveQueue(s); var copy = document.DeepClone(); copy.currentSpread = 1;
                if (!queue.Enqueue(copy)) return false; queue.Tick();
                copy.currentSpread = 7; if (!queue.Enqueue(copy)) return false;
                copy.currentSpread = 1; if (!queue.Enqueue(copy)) return false;
                return queue.Flush(5000) && queue.CompletedCount == 1 && new JournalSaveService(catalog, p).LoadOrCreate().currentSpread == 1 && !File.Exists(p + ".backup");
            }, "An undo back to the running generation cancels an obsolete pending state rather than writing it after the correct generation.");
            check("A primary loaded at startup can deduplicate its unchanged state", () => {
                string p = Path.Combine(directory, "queue-deduplicate-loaded.json"); var initial = new JournalSaveService(catalog, p);
                if (!initial.Save(document.DeepClone())) return false;
                var reader = new JournalSaveService(catalog, p); var copy = reader.LoadOrCreate();
                var queue = new DecorationSaveQueue(reader);
                return queue.Enqueue(copy) && queue.Flush(5000) && queue.CompletedCount == 0 && !File.Exists(p + ".backup");
            }, "Opening a journal and immediately returning does not rewrite the same valid primary.");
            check("Failed queued commit retains data and accepts an explicit retry", () => {
                string blockedParent = Path.Combine(directory, "temporarily-blocked-parent"); File.WriteAllText(blockedParent, "a file temporarily blocks the directory");
                var s = new JournalSaveService(catalog, Path.Combine(blockedParent, "journal.json"));
                var queue = new DecorationSaveQueue(s); var copy = document.DeepClone(); copy.currentSpread = 5;
                if (!queue.Enqueue(copy) || queue.Flush(5000) || !queue.IsPending || queue.LastSucceeded) return false;
                long failedCommits = queue.CompletedCount;
                for (int i = 0; i < 10; i++) queue.Tick();
                bool noAutomaticHammering = queue.CompletedCount == failedCommits;
                File.Delete(blockedParent); Directory.CreateDirectory(blockedParent);
                return noAutomaticHammering && queue.Enqueue(copy) && queue.Flush(5000) && !queue.IsPending && queue.LastSucceeded &&
                    queue.CompletedCount == failedCommits + 1 && new JournalSaveService(catalog, s.SavePath).LoadOrCreate().currentSpread == 5;
            }, "A failed payload is not considered deduplicated success; it remains retryable without retrying every frame.");

            var left = new JournalPageData { pageId = "history-left", paperId = "page_01" };
            var right = new JournalPageData { pageId = "history-right", paperId = "page_02" };
            var history = new DecorationHistory();
            check("Empty history is safe", () => !history.Undo(left) && !history.Redo(left) && history.UndoCount(left.pageId) == 0,
                "Undo/redo cannot invent an action.");
            var empty = DecorationHistory.Clone(left.decorations);
            left.decorations.Add(Item(left.pageId, "history-left-element")); history.Record(left, empty, "Add");
            check("Add undo removes only that page item", () => history.Undo(left) && left.decorations.Count == 0 && right.decorations.Count == 0,
                "Snapshot command is bound to the stable owner page.");
            check("Add redo restores full item data", () => history.Redo(left) && left.decorations.Count == 1 && left.decorations[0].payloadJson.Contains("纸"),
                "Resource identity and extension payload are restored.");
            check("Complete drag records one history command", () => {
                int count = history.UndoCount(left.pageId); var before = DecorationHistory.Clone(left.decorations);
                for (int i = 0; i < 20; i++) left.decorations[0].position = new Vector2(.2f + i * .01f, .4f);
                history.Record(left, before, "Move");
                return history.UndoCount(left.pageId) == count + 1 && history.Undo(left) && left.decorations[0].position == before[0].position &&
                    history.Redo(left) && left.decorations[0].position == new Vector2(.39f, .4f);
            }, "Only the completed drag is recorded; intermediate frames produce no history entries.");
            check("Rotate undo and redo preserve the visual center", () => EditAndReverse(history, left, "Rotate", d => d.rotationDegrees = 45),
                "Rotation-only command leaves the page position and all other item fields unchanged after undo.");
            check("Proportional scale undo and redo", () => EditAndReverse(history, left, "Scale", d => d.scale = Vector2.one * 1.4f),
                "Both scale components roundtrip through the same command.");
            check("Tape length undo and redo", () => EditAndReverse(history, left, "Length", d => d.length = 2.8f),
                "Tape-specific parameters participate in history.");
            check("Layer order undo and redo", () => EditAndReverse(history, left, "Layer", d => d.sortOrder = 12),
                "Layer is page-owned serialized data, not merely renderer state.");
            check("Delete undo restores exact decoration", () => {
                string exact = JsonUtility.ToJson(left.decorations[0]); var before = DecorationHistory.Clone(left.decorations);
                left.decorations.Clear(); history.Record(left, before, "Delete");
                return history.Undo(left) && JsonUtility.ToJson(left.decorations[0]) == exact && history.Redo(left) && left.decorations.Count == 0 && history.Undo(left);
            }, "Deletion does not discard the resource, size, transform, layer or payload.");
            check("Right-page undo never modifies left page", () => {
                string beforeLeft = PageText(left); var beforeRight = DecorationHistory.Clone(right.decorations);
                right.decorations.Add(Item(right.pageId, "history-right-element")); history.Record(right, beforeRight, "Right add");
                return history.Undo(right) && right.decorations.Count == 0 && PageText(left) == beforeLeft && history.Redo(right);
            }, "Separate page histories remain independent while navigating spreads.");
            check("Unrelated page cannot consume another page history", () => {
                var unrelated = new JournalPageData { pageId = "different-spread-page" };
                string l = PageText(left), r = PageText(right);
                return !history.Undo(unrelated) && !history.Redo(unrelated) && PageText(left) == l && PageText(right) == r;
            }, "Commands never target a current-screen index in place of their real page ID.");
            check("History survives replacement of the in-memory page object", () => {
                var copiedPage = JsonUtility.FromJson<JournalPageData>(PageText(right));
                return history.Undo(copiedPage) && copiedPage.decorations.Count == 0 && right.decorations.Count == 1 && history.Redo(copiedPage);
            }, "Paper preview cloning can replace page objects while stable IDs continue to identify history.");
            check("Record snapshots do not alias mutable caller lists", () => {
                var p = new JournalPageData { pageId = "snapshot-alias" }; var h = new DecorationHistory();
                p.decorations.Add(Item(p.pageId, "alias")); var before = DecorationHistory.Clone(p.decorations);
                p.decorations[0].rotationDegrees = 75; h.Record(p, before, "Rotate");
                before[0].rotationDegrees = -800; p.decorations[0].rotationDegrees = 800;
                return h.Undo(p) && p.decorations[0].rotationDegrees == 0 && h.Redo(p) && p.decorations[0].rotationDegrees == 75;
            }, "Later mutations cannot alter either stored end of a command.");
            check("No-op record does not create history or discard redo", () => {
                var p = new JournalPageData { pageId = "noop" }; var h = new DecorationHistory();
                var before = DecorationHistory.Clone(p.decorations); p.decorations.Add(Item(p.pageId, "noop-item")); h.Record(p, before, "Add");
                h.Undo(p); int redo = h.RedoCount(p.pageId); h.Record(p, DecorationHistory.Clone(p.decorations), "No change");
                return h.UndoCount(p.pageId) == 0 && h.RedoCount(p.pageId) == redo && h.Redo(p);
            }, "Clicking without changing an item preserves useful redo history.");
            check("A new action discards redo only on its own page", () => {
                var a = new JournalPageData { pageId = "branch-a" }; var b = new JournalPageData { pageId = "branch-b" }; var h = new DecorationHistory();
                a.decorations.Add(Item(a.pageId, "a")); h.Record(a, new List<JournalDecorationData>(), "Add");
                b.decorations.Add(Item(b.pageId, "b")); h.Record(b, new List<JournalDecorationData>(), "Add"); h.Undo(a); h.Undo(b);
                a.decorations.Add(Item(a.pageId, "new-a")); h.Record(a, new List<JournalDecorationData>(), "New branch");
                return h.RedoCount(a.pageId) == 0 && h.RedoCount(b.pageId) == 1 && h.Redo(b);
            }, "A new left-page edit cannot invalidate a right-page redo stack.");
            check("Default capacity retains sixty complete commands", () => {
                var p = new JournalPageData { pageId = "sixty" }; var h = new DecorationHistory(); p.decorations.Add(Item(p.pageId, "sixty-item"));
                for (int i = 1; i <= 70; i++) { var before = DecorationHistory.Clone(p.decorations); p.decorations[0].rotationDegrees = i; h.Record(p, before, "Rotate"); }
                bool ok = h.UndoCount(p.pageId) == 60;
                for (int i = 0; i < 60; i++) ok &= h.Undo(p);
                ok &= p.decorations[0].rotationDegrees == 10 && !h.Undo(p);
                for (int i = 0; i < 60; i++) ok &= h.Redo(p);
                return ok && p.decorations[0].rotationDegrees == 70 && !h.Redo(p);
            }, "At least thirty actions are supported; default bounded history is sixty per page.");
            check("Requested undersized capacity still supports thirty actions", () => {
                var p = new JournalPageData { pageId = "minimum-thirty" }; var h = new DecorationHistory(1); p.decorations.Add(Item(p.pageId, "thirty-item"));
                for (int i = 1; i <= 40; i++) { var before = DecorationHistory.Clone(p.decorations); p.decorations[0].rotationDegrees = i; h.Record(p, before, "Rotate"); }
                bool ok = h.UndoCount(p.pageId) == 30;
                for (int i = 0; i < 30; i++) ok &= h.Undo(p);
                for (int i = 0; i < 30; i++) ok &= h.Redo(p);
                return ok && p.decorations[0].rotationDegrees == 40;
            }, "A caller cannot accidentally configure fewer than the required thirty undo entries.");

            var report = new Report { utc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                directory = directory, passed = results.All(result => result.passed), checks = results.ToArray() };
            string reportPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "docs/stage3-logs/data-tests.json");
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)); File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
            Debug.Log("[DecorationDataChecks] " + (report.passed ? "PASS" : "FAIL") + "; checks=" + results.Count + "; report=" + reportPath);
            return report.passed;
        }

        static JournalDecorationData Item(string owner, string id)
        { return new JournalDecorationData { instanceId = id, assetId = "tape_01", type = "tape", pageId = owner,
            position = new Vector2(.5f, .5f), size = new Vector2(.2f, .05f), scale = Vector2.one, length = 1, payloadJson = "{\"memo\":\"纸间时光\"}" }; }
        static string PageText(JournalPageData page) { return JsonUtility.ToJson(page); }
        static bool EditAndReverse(DecorationHistory history, JournalPageData page, string label, Action<JournalDecorationData> mutation)
        {
            var before = DecorationHistory.Clone(page.decorations); string exactBefore = JsonUtility.ToJson(before[0]);
            mutation(page.decorations[0]); string exactAfter = JsonUtility.ToJson(page.decorations[0]); history.Record(page, before, label);
            return history.Undo(page) && JsonUtility.ToJson(page.decorations[0]) == exactBefore && history.Redo(page) && JsonUtility.ToJson(page.decorations[0]) == exactAfter;
        }
    }
}

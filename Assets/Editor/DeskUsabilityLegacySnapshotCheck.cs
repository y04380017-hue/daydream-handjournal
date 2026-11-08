using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace VintageJournal.Editor
{
    /// <summary>Reads only an explicitly supplied backup. No player store, scene save, or document commit is used.</summary>
    public static class DeskUsabilityLegacySnapshotCheck
    {
        const string ScenePath = "Assets/Scenes/VintageJournalDesk.unity";
        const string ReportPath = "docs/desk-usability/legacy-user-snapshot-check.json";
        [Serializable] sealed class Check { public string name; public bool passed; }
        [Serializable] sealed class Report
        {
            public bool passed, sourceUnchanged, sceneFileUnchanged, originalStoreAccessed, diskDocumentCommitAttempted, exceptionObserved;
            public string sourceSha256Before, sourceSha256After, migratedDocumentSha256, sceneSha256Before, sceneSha256After;
            public int sourceBytes, originalItems, originalTombstones, originalPages, originalDecorations, originalNotebooks;
            public int sceneDefinitions, addedFiniteItems, finalItems, finalNotebooks, memoryPersistCalls, originalPendingTransfers;
            public Check[] checks;
        }
        static readonly List<Check> checks = new List<Check>();
        static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        static string HashText(string text) => Hash(Encoding.UTF8.GetBytes(text ?? string.Empty));
        static string Json(object value) => value == null ? "null" : JsonUtility.ToJson(value);
        static void Require(string name, bool passed)
        { checks.Add(new Check { name = name, passed = passed }); if (!passed) throw new InvalidOperationException("Redacted legacy snapshot assertion failed."); }
        static bool Same(object left, object right) => string.Equals(Json(left), Json(right), StringComparison.Ordinal);
        static T[] Components<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).ToArray();
        static CraftItemKind Kind(PhysicalItemType type)
        {
            switch (type) {
                case PhysicalItemType.Tape: return CraftItemKind.TapeRoll;
                case PhysicalItemType.Sticker: return CraftItemKind.Sticker;
                case PhysicalItemType.Note: return CraftItemKind.Note;
                case PhysicalItemType.LoosePage: return CraftItemKind.LoosePage;
                case PhysicalItemType.ReleaseBook: return CraftItemKind.ReleaseBook;
                case PhysicalItemType.TapeStrip: return CraftItemKind.TapeStrip;
                case PhysicalItemType.Tool: return CraftItemKind.Tool;
                default: return CraftItemKind.TN;
            }
        }
        static bool FullSchemaValid(JournalDocument document, JournalPaperCatalog catalog)
        {
            // PrepareSnapshot performs validation in memory. Never call LoadOrCreate, Save, or CommitPrepared.
            var neverWritten = Path.GetFullPath(".verification-data/legacy-check-never-written.json");
            var validator = new JournalSaveService(catalog, neverWritten);
            return validator.PrepareSnapshot(document.DeepClone()) != null;
        }
        public static void Run()
        {
            checks.Clear(); var report = new Report(); Scene opened = default; bool closeScene = false;
            string source = null; bool previousLogging = Debug.unityLogger.logEnabled;
            try {
                string[] args = Environment.GetCommandLineArgs(); int flag = Array.IndexOf(args, "--usability-legacy-snapshot");
                Require("Explicit backup argument supplied", flag >= 0 && flag + 1 < args.Length && Path.IsPathRooted(args[flag + 1]));
                source = Path.GetFullPath(args[flag + 1]);
                string persistent = Path.GetFullPath(Application.persistentDataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                Require("Snapshot is outside all live player save folders", !source.StartsWith(persistent, StringComparison.OrdinalIgnoreCase));
                Require("Backup exists within supported snapshot size", File.Exists(source) && new FileInfo(source).Length > 0 && new FileInfo(source).Length <= 8L * 1024 * 1024);
                byte[] bytes = File.ReadAllBytes(source); report.sourceBytes = bytes.Length; report.sourceSha256Before = Hash(bytes);
                var original = JsonUtility.FromJson<JournalDocument>(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'));
                Require("Backup has original journal schema one", original != null && original.schemaVersion == 1);
                original.NormalizeOptionalData();
                var catalog = JournalPaperCatalog.Load();
                Require("Complete original snapshot schema validates in memory", FullSchemaValid(original, catalog));
                var oldItems = (original.craftWorkspace?.items ?? new List<CraftItemRecord>()).ToDictionary(i => i.instanceId, i => i.Clone(), StringComparer.Ordinal);
                var oldPages = original.AllPages.ToDictionary(p => p.pageId, p => Json(p), StringComparer.Ordinal);
                var oldNotebooks = (original.notebookLibrary?.notebooks ?? new List<NotebookData>()).ToDictionary(b => b.bookId, b => Json(b), StringComparer.Ordinal);
                var oldReleaseBooks = (original.craftWorkspace?.books ?? new List<CraftReleaseBookData>()).ToDictionary(b => b.bookId, b => Json(b), StringComparer.Ordinal);
                var oldDrawers = (original.craftWorkspace?.drawers ?? new List<CraftDrawerState>()).ToDictionary(d => d.id, d => Json(d), StringComparer.Ordinal);
                report.originalItems = oldItems.Count; report.originalTombstones = oldItems.Values.Count(i => i.owner == CraftOwner.Deleted);
                report.originalPages = oldPages.Count; report.originalDecorations = original.AllPages.Sum(p => p.decorations?.Count ?? 0);
                report.originalNotebooks = oldNotebooks.Count; report.originalPendingTransfers = original.craftWorkspace?.pending == null ? 0 : 1;
                report.sceneSha256Before = Hash(File.ReadAllBytes(ScenePath));
                opened = SceneManager.GetSceneByPath(ScenePath);
                if (!opened.IsValid() || !opened.isLoaded) { opened = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive); closeScene = true; }
                Require("Inspected scene starts clean and will not be saved", !opened.isDirty);
                var layout = Components<DeskLayoutBindings>(opened).Single();
                var physical = Components<PhysicalInteractable>(opened);
                Require("New scene contains supported migration bindings", layout.version >= 1 && layout.version <= CraftWorkspaceData.CurrentDeskLayoutVersion && layout.placements != null && layout.placements.Length > 0);
                Require("All physical definitions have existing explicit stable IDs", physical.All(i => !string.IsNullOrEmpty(i.itemInstanceId) && !string.IsNullOrEmpty(i.assetId) && i.dimensions) && physical.Select(i => i.itemInstanceId).Distinct(StringComparer.Ordinal).Count() == physical.Length);
                var definitions = physical.Select(i => new CraftItemRecord {
                    instanceId = i.itemInstanceId, assetId = i.assetId, kind = Kind(i.itemType),
                    owner = i.CurrentSlot ? CraftOwner.Stock : CraftOwner.Desktop, containerId = i.CurrentSlot ? i.CurrentSlot.slotId : "desktop",
                    worldPosition = i.transform.position, worldEulerAngles = i.transform.eulerAngles,
                    sizeMeters = i.itemType == PhysicalItemType.TravelersNotebook ? new Vector2(i.dimensions.size.x, i.dimensions.size.y) : new Vector2(i.dimensions.size.x, i.dimensions.size.z)
                }).ToArray();
                var targets = layout.placements.Where(p => p != null && p.item && p.homeSlot).Select(p => new CraftDeskLayoutTarget {
                    instanceId = p.item.itemInstanceId, homeSlotId = p.homeSlot.slotId, displaySlotId = p.desiredDisplaySlot ? p.desiredDisplaySlot.slotId : null,
                    homePosition = p.homeSlot.WorldPose.position, homeEulerAngles = p.homeSlot.WorldPose.rotation.eulerAngles,
                    displayPosition = p.desiredDisplaySlot ? p.desiredDisplaySlot.WorldPose.position : p.homeSlot.WorldPose.position,
                    displayEulerAngles = p.desiredDisplaySlot ? p.desiredDisplaySlot.WorldPose.rotation.eulerAngles : p.homeSlot.WorldPose.rotation.eulerAngles,
                    migrateLegacyHome = p.migrateLegacyHome,relocateStockOnMigration=p.relocateStockOnMigration,
                    relocateDesktopOnMigration=p.relocateDesktopOnMigration,legacySlotIds=p.legacySlotIds,releaseBookId=p.releaseBookId
                }).ToArray();
                var workbench = Components<NotebookWorkbench>(opened).Single();
                var notebooks = workbench.notebooks ?? physical.Where(i => i.itemType == PhysicalItemType.TravelersNotebook).ToArray();
                var notebookDefinitions = notebooks.Select(i => new NotebookData { bookId = i.assetId, physicalInstanceId = i.itemInstanceId, displayName = i.displayName, pageSizeMeters = i.dimensions.pageSize }).ToArray();
                report.sceneDefinitions = definitions.Length;
                // Silence business diagnostics while handling private data. Only redacted fixed-name checks are published below.
                Debug.unityLogger.logEnabled = false;
                using (var service = new CraftWorkspaceService(original.DeepClone(), _ => { report.memoryPersistCalls++; return true; }, new Vector2(.148f, .210f), DecorationCatalog.Load())) {
                    int initializedCount = service.Items.Count, expectedAdded = definitions.Count(d => service.GetItem(d.instanceId) == null), writesBefore = report.memoryPersistCalls;
                    Require("Actual scene bindings migrate the backup successfully", service.RegisterDeskLayout(definitions, targets, layout.version));
                    report.addedFiniteItems = service.Items.Count - initializedCount;
                    Require("New finite inventory is added exactly once", report.addedFiniteItems == expectedAdded && definitions.All(d => service.Items.Count(i => i.instanceId == d.instanceId) == 1));
                    Require("Layout migration uses at most one atomic memory persistence callback", report.memoryPersistCalls - writesBefore <= 1);
                    Require("Notebook definitions initialize without replacing old notebooks", service.InitializeNotebooks(notebookDefinitions, catalog.defaultPaperId));
                    var migrated = service.Document;
                    Require("Journal and ownership schema remain one and layout reaches scene version", migrated.schemaVersion == 1 && migrated.craftWorkspace.schemaVersion == 1 && migrated.craftWorkspace.deskLayoutVersion == layout.version);
                    Require("Every original instance ID asset and kind is retained", oldItems.All(pair => service.GetItem(pair.Key) != null && service.GetItem(pair.Key).assetId == pair.Value.assetId && service.GetItem(pair.Key).kind == pair.Value.kind));
                    Require("Every consumed tombstone is preserved byte for byte", oldItems.Values.Where(i => i.owner == CraftOwner.Deleted).All(i => Same(i, service.GetItem(i.instanceId))));
                    Require("Existing attached instances retain original page ownership", oldItems.Values.Where(i => i.owner == CraftOwner.JournalPage || i.owner == CraftOwner.ReleaseBookPage).All(i => { var after = service.GetItem(i.instanceId); return after != null && after.owner == i.owner && after.pageId == i.pageId && after.containerId == i.containerId; }));
                    Require("Every original page including all decorations is unchanged", oldPages.All(pair => migrated.AllPages.Any(p => p.pageId == pair.Key && Json(p) == pair.Value)));
                    Require("Every original notebook and current spread is unchanged", oldNotebooks.All(pair => migrated.notebookLibrary.notebooks.Any(b => b.bookId == pair.Key && Json(b) == pair.Value)));
                    Require("Active notebook and binder cursor are unchanged", original.ActiveBookId == migrated.ActiveBookId && original.currentSpread == migrated.currentSpread);
                    Require("Release book pages and drawer state are preserved while repacking may close covers", oldReleaseBooks.All(pair => migrated.craftWorkspace.books.Any(b => {
                        var prior=JsonUtility.FromJson<CraftReleaseBookData>(pair.Value);var current=JsonUtility.FromJson<CraftReleaseBookData>(Json(b));
                        if(layout.version>=2)prior.isOpen=current.isOpen;
                        return b.bookId==pair.Key&&Json(current)==Json(prior);
                    })) && oldDrawers.All(pair => migrated.craftWorkspace.drawers.Any(d => d.id == pair.Key && Json(d) == pair.Value)));
                    Require("Private studio branch including diary remains identical", Same(original.personalStudio, migrated.personalStudio) && original.personalStudioEnabled == migrated.personalStudioEnabled);
                    Require("Social identity receipts and data remain identical", Same(original.atelierSocial, migrated.atelierSocial) && original.atelierSocialEnabled == migrated.atelierSocialEnabled);
                    Require("Commission collection and progress branches remain identical", Same(original.commissionDesk, migrated.commissionDesk) && Same(original.dreamGameplay, migrated.dreamGameplay) && Same(original.atelierProgress, migrated.atelierProgress));
                    Require("Complete migrated snapshot schema validates in memory", FullSchemaValid(migrated, catalog));
                    Require("All final ownership IDs and physical stock slots are unique", service.Items.Select(i => i.instanceId).Distinct(StringComparer.Ordinal).Count() == service.Items.Count && CraftWorkspaceData.Validate(migrated, out _));
                    string stable = Json(migrated); int stableWrites = report.memoryPersistCalls;
                    Require("Second layout and notebook initialization succeed", service.RegisterDeskLayout(definitions, targets, layout.version) && service.InitializeNotebooks(notebookDefinitions, catalog.defaultPaperId));
                    Require("Repeat execution is byte-identical and makes no persistence callback", stable == Json(service.Document) && report.memoryPersistCalls == stableWrites);
                    report.finalItems = service.Items.Count; report.finalNotebooks = service.Document.notebookLibrary.notebooks.Count; report.migratedDocumentSha256 = HashText(Json(service.Document));
                }
                Require("Read-only scene inspection did not dirty the scene", !opened.isDirty);
            }
            catch (Exception) { report.exceptionObserved = true; }
            finally {
                Debug.unityLogger.logEnabled = previousLogging;
                if (closeScene && opened.IsValid() && opened.isLoaded) EditorSceneManager.CloseScene(opened, true);
                if (!string.IsNullOrEmpty(source) && !string.IsNullOrEmpty(report.sourceSha256Before)) {
                    try { report.sourceSha256After = Hash(File.ReadAllBytes(source)); report.sourceUnchanged = report.sourceSha256Before == report.sourceSha256After; } catch { report.sourceUnchanged = false; }
                }
                if (!string.IsNullOrEmpty(report.sceneSha256Before)) {
                    try { report.sceneSha256After = Hash(File.ReadAllBytes(ScenePath)); report.sceneFileUnchanged = report.sceneSha256Before == report.sceneSha256After; } catch { report.sceneFileUnchanged = false; }
                }
                checks.Add(new Check { name = "Explicit backup source bytes are unchanged", passed = report.sourceUnchanged });
                checks.Add(new Check { name = "Scene source bytes are unchanged", passed = report.sceneFileUnchanged });
                report.checks = checks.ToArray(); report.passed = !report.exceptionObserved && checks.Count > 0 && checks.All(c => c.passed);
                Directory.CreateDirectory(Path.GetDirectoryName(ReportPath)); File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
                Debug.Log("[DeskUsabilityLegacy] " + (report.passed ? "PASS" : "FAIL") + "; checks=" + checks.Count + "; sourceSHA256=" + (report.sourceSha256Before ?? "none"));
            }
            if (!report.passed) throw new InvalidOperationException("Legacy snapshot check failed; inspect the redacted check report.");
        }
    }
}

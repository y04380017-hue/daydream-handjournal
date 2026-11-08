using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VintageJournal.Editor
{
    /// <summary>Real Unity serialization and filesystem checks in an isolated persistent-data subdirectory.</summary>
    public static class JournalDataTests
    {
        [Serializable] public sealed class Result { public string name; public bool passed; public string detail; }
        [Serializable] public sealed class Report
        {
            public string utc, unityVersion, directory;
            public bool passed;
            public Result[] checks;
        }

        [MenuItem("Vintage Journal/Run journal data checks")]
        public static void RunFromMenu()
        {
            if (!RunDataChecks()) throw new InvalidOperationException("Journal data checks failed; see docs/stage2-logs/data-tests.json.");
        }

        public static bool RunDataChecks()
        {
            var checks = new List<Result>();
            string directory = Path.Combine(Application.persistentDataPath, "Stage2Validation", "DataChecks", DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var catalog = JournalPaperCatalog.Load();
            Action<string, Func<bool>, string> check = (name, action, detail) => {
                try { checks.Add(new Result { name = name, passed = action(), detail = detail }); }
                catch (Exception error) { checks.Add(new Result { name = name, passed = false, detail = error.ToString() }); }
            };

            check("24 original paper definitions resolve", () => catalog && catalog.papers.Length >= 24 &&
                catalog.papers.All(paper => paper != null && !string.IsNullOrEmpty(paper.id) && paper.texture) &&
                catalog.papers.Select(paper => paper.id).Distinct().Count() == catalog.papers.Length,
                "No replacement PNGs; catalog textures are loaded from the existing project resources.");
            var document = JournalDocument.CreateDefault(catalog);
            check("24 independent page instances and 12 spreads", () => document.PageCount == 24 && document.SpreadCount == 12 &&
                document.pages.Select(page => page.pageId).Distinct().Count() == 24 &&
                (string.IsNullOrEmpty(catalog.defaultPaperId)
                    ? document.pages.Select(page => page.paperId).SequenceEqual(catalog.papers.Take(24).Select(paper => paper.id))
                    : catalog.Find(catalog.defaultPaperId) != null && document.pages.All(page => page.paperId == catalog.defaultPaperId)),
                "Page instance IDs differ from reusable paper asset IDs.");
            check("Left and right page mapping", () => document.GetPage(JournalSide.Left) == document.pages[0] &&
                document.GetPage(JournalSide.Right) == document.pages[1], "Zero-based spread maps to two stable page instances.");
            check("Page change is independent of opposite page", () => {
                string right = document.pages[1].paperId;
                document.pages[0].paperId = catalog.papers[6].id;
                return document.pages[1].paperId == right && document.pages[0].paperId != right;
            }, "Changing one instance never mutates another instance or the catalog.");
            document.currentSpread = 4;
            document.pages[8].paperId = catalog.papers[12].id;
            document.pages[9].paperId = catalog.papers[17].id;
            document.pages[8].decorations.Add(new JournalDecorationData {
                instanceId = "test-element-1", assetId = "sticker_01", type = "sticker",
                position = new Vector2(.17f, .82f), scale = new Vector2(.24f, .16f), rotationDegrees = 14.5f,
                sortOrder = 3, payloadJson = "{\"text\":\"纸间心事\"}"
            });
            check("Deep clone does not alias page decorations", () => {
                var clone = document.DeepClone(); clone.pages[8].decorations[0].position = Vector2.zero;
                clone.pages[0].paperId = catalog.papers[3].id;
                return document.pages[8].decorations[0].position == new Vector2(.17f, .82f) &&
                    document.pages[0].paperId != clone.pages[0].paperId;
            }, "Independent future decoration records survive JsonUtility cloning.");

            string savePath = Path.Combine(directory, "roundtrip.json");
            var service = new JournalSaveService(catalog, savePath);
            check("Explicit validation path remains isolated", () => service.SavePath == Path.GetFullPath(savePath) &&
                service.SavePath != Path.Combine(Application.persistentDataPath, "journal-v1.json"), "No test writes the normal player's journal.");
            check("First atomic save succeeds", () => service.Save(document) && File.Exists(savePath), "A new JSON file is written through a temporary file and rename.");
            check("Versioned UTF8 JSON written", () => File.ReadAllText(savePath).Contains("\"schemaVersion\": 1") &&
                File.ReadAllText(savePath).Contains("纸间心事"), "Schema marker and non-ASCII decoration payload are retained.");
            check("New service instance restores spread and both paper selections", () => {
                var loaded = new JournalSaveService(catalog, savePath).LoadOrCreate();
                return loaded.currentSpread == 4 && loaded.pages[8].paperId == catalog.papers[12].id &&
                    loaded.pages[9].paperId == catalog.papers[17].id && loaded.pages[0].paperId == catalog.papers[6].id;
            }, "Real disk read with a fresh save service, not an in-memory reference.");
            check("Page-local future decoration data roundtrips", () => {
                var loaded = new JournalSaveService(catalog, savePath).LoadOrCreate();
                var placed = loaded.pages[8].decorations[0];
                return loaded.pages[9].decorations.Count == 0 && placed.instanceId == "test-element-1" &&
                    placed.position == new Vector2(.17f, .82f) && placed.scale == new Vector2(.24f, .16f) &&
                    Mathf.Approximately(placed.rotationDegrees, 14.5f) && placed.sortOrder == 3 && placed.payloadJson.Contains("纸间心事");
            }, "One page owns its own normalized UV coordinates and extension payload.");
            check("Second atomic save retains previous backup", () => {
                string old = File.ReadAllText(savePath);
                document.currentSpread = 7;
                return service.Save(document) && File.Exists(savePath + ".backup") && File.ReadAllText(savePath + ".backup") == old;
            }, "A successful overwrite creates an exact previous-version backup.");
            check("Invalid save never overwrites valid disk content", () => {
                string old = File.ReadAllText(savePath); var bad = document.DeepClone(); bad.pages[1].pageId = bad.pages[0].pageId;
                return !service.Save(bad) && File.ReadAllText(savePath) == old;
            }, "Duplicate instance IDs are rejected before file replacement.");
            check("Interrupted primary recovers exact previous valid generation", () => {
                string path = Path.Combine(directory, "interrupted-primary.json");
                var writer = new JournalSaveService(catalog, path);
                var old = document.DeepClone(); old.currentSpread = 3;
                if (!writer.Save(old)) return false;
                string exactPrevious = File.ReadAllText(path);
                var newer = old.DeepClone(); newer.currentSpread = 8;
                if (!writer.Save(newer) || File.ReadAllText(path + ".backup") != exactPrevious) return false;
                File.WriteAllText(path, "{ interrupted write");
                var reader = new JournalSaveService(catalog, path); var recovered = reader.LoadOrCreate();
                return recovered.currentSpread == 3 && JsonUtility.ToJson(recovered) == JsonUtility.ToJson(old) &&
                    File.Exists(reader.PreservedFilePath) && reader.LastMessage.Contains("备份") &&
                    File.ReadAllText(path + ".backup") == exactPrevious;
            }, "Even on a non-atomic filesystem fallback, a damaged primary preserves the previous complete valid journal.");
            check("Missing primary recovers retained backup", () => {
                string path = Path.Combine(directory, "missing-primary.json");
                var old = document.DeepClone(); old.currentSpread = 6;
                File.WriteAllText(path + ".backup", JsonUtility.ToJson(old));
                var reader = new JournalSaveService(catalog, path); var recovered = reader.LoadOrCreate();
                return recovered.currentSpread == 6 && recovered.pages[8].paperId == old.pages[8].paperId && reader.LastMessage.Contains("备份");
            }, "A crash leaving only a previous generation does not silently create an empty journal.");

            Action<string, string> recovery = (name, raw) => check(name, () => {
                string path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json"); File.WriteAllText(path, raw);
                var reader = new JournalSaveService(catalog, path); var loaded = reader.LoadOrCreate();
                return loaded.PageCount == 24 && loaded.currentSpread == 0 && !string.IsNullOrEmpty(reader.LastMessage) &&
                    !string.IsNullOrEmpty(reader.PreservedFilePath) && File.Exists(reader.PreservedFilePath) &&
                    File.ReadAllText(reader.PreservedFilePath) == raw && !File.Exists(path);
            }, "Invalid original is retained byte-for-byte in a unique .broken file; default journal is safe in memory.");
            recovery("Malformed JSON safely recovers and preserves original", "{ broken journal ");
            var future = document.DeepClone(); future.schemaVersion = 99;
            recovery("Future schema is rejected without deleting future data", JsonUtility.ToJson(future));
            recovery("Missing schema is not silently treated as current", JsonUtility.ToJson(document).Replace("\"schemaVersion\":1,", ""));
            var missingId = document.DeepClone(); missingId.pages[1].pageId = string.Empty;
            recovery("Missing page instance ID preserves invalid save", JsonUtility.ToJson(missingId));
            var duplicateId = document.DeepClone(); duplicateId.pages[9].pageId = duplicateId.pages[0].pageId;
            recovery("Duplicate page instance ID preserves invalid save", JsonUtility.ToJson(duplicateId));
            var odd = document.DeepClone(); odd.pages = odd.pages.Take(23).ToArray();
            recovery("Incomplete spread count preserves invalid save", JsonUtility.ToJson(odd));
            var invalidCoordinates = document.DeepClone(); invalidCoordinates.pages[8].decorations[0].position.x = 1.5f;
            recovery("Out-of-page decoration coordinates preserve invalid save", JsonUtility.ToJson(invalidCoordinates));

            check("Unknown paper falls back without losing other pages", () => {
                var unknown = document.DeepClone(); unknown.pages[8].paperId = "missing-paper";
                string path = Path.Combine(directory, "missing-asset.json"); File.WriteAllText(path, JsonUtility.ToJson(unknown));
                var reader = new JournalSaveService(catalog, path); var loaded = reader.LoadOrCreate();
                return loaded.pages[8].paperId == catalog.papers[0].id && loaded.pages[9].paperId == document.pages[9].paperId &&
                    loaded.pages[8].decorations.Count == 1 && loaded.currentSpread == document.currentSpread &&
                    string.IsNullOrEmpty(reader.PreservedFilePath) && reader.LastMessage.Contains("素材");
            }, "A missing catalog entry only repairs affected paper references; page instance IDs and decorations remain.");
            check("Out-of-range current spread clamps safely", () => {
                var outOfRange = document.DeepClone(); outOfRange.currentSpread = 900;
                string path = Path.Combine(directory, "out-of-range.json"); File.WriteAllText(path, JsonUtility.ToJson(outOfRange));
                var loaded = new JournalSaveService(catalog, path).LoadOrCreate(); return loaded.currentSpread == 11;
            }, "Existing page data is preserved while navigation returns to a valid spread.");
            check("Missing save creates defaults at explicit path", () => {
                string path = Path.Combine(directory, "fresh.json"); var freshService = new JournalSaveService(catalog, path);
                return freshService.LoadOrCreate().PageCount == 24 && File.Exists(path);
            }, "First launch creates a full data-driven journal, not an empty placeholder.");

            var report = new Report { utc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                directory = directory, passed = checks.All(result => result.passed), checks = checks.ToArray() };
            string reportPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "docs/stage2-logs/data-tests.json");
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
            Debug.Log("[JournalDataChecks] " + (report.passed ? "PASS" : "FAIL") + "; checks=" + checks.Count + "; report=" + reportPath);
            return report.passed;
        }
    }
}

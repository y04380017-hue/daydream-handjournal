using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VintageJournal.Editor
{
    /// <summary>Runs real catalog/save regression checks and inspects the serialized Stage 4 scene.</summary>
    public static class PhysicalWorkspaceDataTests
    {
        [Serializable] public sealed class Result { public string name, detail; public bool passed; }
        [Serializable] public sealed class Report
        {
            public string timestampUtc, unityVersion, scope;
            public bool passed;
            public Result[] checks;
        }

        [MenuItem("Vintage Journal/Stage 4/Run physical workspace data checks")]
        public static void RunFromMenu()
        {
            if (!RunDataChecks()) throw new InvalidOperationException("Stage 4 data checks failed; see docs/stage4-logs/data-tests.json.");
        }

        public static bool RunDataChecks()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run data checks in Edit Mode.");
            string directory = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "docs/stage4-logs");
            Directory.CreateDirectory(directory);
            var results = new List<Result>();
            Action<string, Func<bool>, string> check = (name, action, detail) => {
                bool passed;
                try { passed = action(); }
                catch (Exception error) { passed = false; detail += "\n" + error; }
                results.Add(new Result { name = name, passed = passed, detail = detail });
                Debug.Log("[PhysicalData] " + (passed ? "PASS " : "FAIL ") + name + ": " + detail);
            };
            check("Stage 2 journal serialization and old-save suite", JournalDataTests.RunDataChecks,
                "Actual Unity JSON/filesystem regression checks, isolated Stage2Validation path.");
            check("Stage 3 decoration/history and old-save suite", DecorationDataTests.RunDataChecks,
                "Actual Unity JSON/filesystem/history regression checks, isolated Stage3Validation path.");

            EditorSceneManager.OpenScene("Assets/Scenes/VintageJournalDesk.unity", OpenSceneMode.Single);
            Type itemType = RuntimeType("VintageJournal.PhysicalInteractable");
            Type controllerType = RuntimeType("VintageJournal.PhysicalInteractionController");
            Type slotType = RuntimeType("VintageJournal.PhysicalStorageSlot");
            Type dimensionsType = RuntimeType("VintageJournal.PhysicalDimensionProfile");
            var items = FindComponents(itemType);
            check("Physical item contract exists", () => itemType != null && controllerType != null && slotType != null && dimensionsType != null,
                "Types are resolved through the loaded runtime assembly; missing Stage 4 implementation is a test failure.");
            check("Independent serialized physical instances", () => items.Length > 0 && items.Select(i => Text(i, "itemInstanceId")).All(i => !string.IsNullOrEmpty(i)) &&
                items.Select(i => Text(i, "itemInstanceId")).Distinct().Count() == items.Length,
                items.Length + " saved scene instances; persistent unique instance IDs must not alias reusable AssetId.");
            check("Required physical stock counts", () => Count(items, "Tape") >= 8 && Count(items, "Sticker") >= 12 && Count(items, "Note") >= 6 &&
                Count(items, "LoosePage") >= 1 && Count(items, "TravelersNotebook") >= 4,
                "Tape=" + Count(items,"Tape") + ", Sticker=" + Count(items,"Sticker") + ", Note=" + Count(items,"Note") +
                ", LoosePage=" + Count(items,"LoosePage") + ", TravelersNotebook=" + Count(items,"TravelersNotebook"));
            var catalog = DecorationCatalog.Load();
            var paperCatalog = JournalPaperCatalog.Load();
            check("Physical materials resolve existing stable asset IDs", () => items.Length > 0 && items.All(item => {
                string kind = Text(item,"itemType"), id = Text(item,"assetId");
                return kind == "TravelersNotebook" ? !string.IsNullOrEmpty(id) : kind == "LoosePage" ? paperCatalog.Find(id) != null : catalog.Find(id) != null;
            }), "Each reusable decoration ID resolves through the unchanged Stage 3 resource catalog; loose sheets use the existing paper catalog.");
            check("Model-independent item anchors and collision binding", () => items.Length > 0 && items.All(item =>
                ObjectValue(item,"grabAnchor") && ObjectValue(item,"homeAnchor") && ObjectValue(item,"inspectAnchor") &&
                ObjectValue(item,"visualRoot") && ObjectValue(item,"interactionCollider") && ObjectValue(item,"homeSlot")),
                "Every item has separate Transform anchors, visual replacement root, collider and home slot.");
            check("Meter dimensions and root transforms", () => items.Length > 0 && items.All(item => {
                var profile = ObjectValue(item,"dimensions");
                if (!profile) return false;
                var size = Value(profile,"size");
                if (!(size is Vector3 v) || v.x <= 0 || v.y <= 0 || v.z <= 0 || v.x > .3f || v.y > .3f || v.z > .3f) return false;
                var scale = item.transform.lossyScale;
                return Vector3.Distance(scale,Vector3.one) < .001f;
            }), "Logical item roots remain 1:1 in meters; visual adaptation is isolated below visualRoot.");
            var slots = FindComponents(slotType);
            check("Exclusive home slots", () => items.Length > 0 && items.Select(i => ObjectValue(i,"homeSlot")).Distinct().Count() == items.Length && slots.Length >= items.Length,
                slots.Length + " slots for " + items.Length + " independent items; no two exclusive homes share a slot.");
            var corePath = Path.Combine(Application.dataPath,"Scripts/Physical");
            string[] coreSources = Directory.Exists(corePath) ? Directory.GetFiles(corePath,"*.cs",SearchOption.AllDirectories)
                .Where(path => !Path.GetFileName(path).Contains("Mouse") && !Path.GetFileName(path).Contains("HUD")).ToArray() : Array.Empty<string>();
            check("Core is independent of mouse hardware", () => coreSources.Length > 0 && coreSources.All(path => !File.ReadAllText(path).Contains("Input.GetMouse") && !File.ReadAllText(path).Contains("Input.mousePosition")),
                "Supplementary source check only; Play tests separately drive the same intent API without native input.");
            check("Save schema compatibility unchanged", () => JournalDocument.CurrentSchemaVersion == 1,
                "Physical inventory is separate from persisted page decorations; no migration is required for schema v1.");
            var report = new Report { timestampUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                scope = "Edit Mode runtime serialization/catalog regression checks and serialized scene inspection. Not a Play Mode or native-input result.",
                passed = results.All(r => r.passed), checks = results.ToArray() };
            File.WriteAllText(Path.Combine(directory,"data-tests.json"),JsonUtility.ToJson(report,true));
            return report.passed;
        }

        static Type RuntimeType(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name,false)).FirstOrDefault(t => t != null);
        static Component[] FindComponents(Type type) => type == null ? Array.Empty<Component>() : UnityEngine.Object.FindObjectsByType(type,FindObjectsInactive.Include,FindObjectsSortMode.None).OfType<Component>().ToArray();
        static object Value(object owner,string name) => owner.GetType().GetField(name,BindingFlags.Public|BindingFlags.Instance)?.GetValue(owner) ?? owner.GetType().GetProperty(name,BindingFlags.Public|BindingFlags.Instance)?.GetValue(owner);
        static UnityEngine.Object ObjectValue(object owner,string name) => Value(owner,name) as UnityEngine.Object;
        static string Text(object owner,string name) => Value(owner,name)?.ToString() ?? string.Empty;
        static int Count(Component[] items,string kind) => items.Count(i => Text(i,"itemType") == kind);
    }
}

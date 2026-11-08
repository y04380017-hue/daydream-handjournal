using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace VintageJournal.Editor
{
    public static class DeskUsabilityDataChecks
    {
        [Serializable] public sealed class Row { public string name; public bool passed; }
        [Serializable] public sealed class Report { public string timestampUtc, scope; public bool passed; public Row[] checks; }
        static readonly List<Row> checks = new List<Row>();
        static void Check(string name, bool pass)
        { checks.Add(new Row { name = name, passed = pass }); Debug.Log("[DeskUsabilityData] " + (pass ? "PASS " : "FAIL ") + name); }
        static CraftItemRecord Item(string id, CraftItemKind kind, string slot, CraftOwner owner = CraftOwner.Stock)
        { return new CraftItemRecord { instanceId = id, assetId = kind == CraftItemKind.TapeRoll ? "tape_01" : "usability-" + id, kind = kind, owner = owner, containerId = slot, sizeMeters = new Vector2(.03f, .03f), worldPosition = new Vector3(.2f, .76f, 0) }; }
        static CraftDeskLayoutTarget Target(string id, string home, string display = null)
        { return new CraftDeskLayoutTarget { instanceId = id, homeSlotId = home, displaySlotId = display, homePosition = new Vector3(.2f, .59f, 0), displayPosition = new Vector3(-.4f, .96f, .1f) }; }
        public static bool RunDataChecks()
        {
            checks.Clear();
            try
            {
                var doc = JournalDocument.CreateDefault(JournalPaperCatalog.Load());
                string saved = null; bool accept = true; int writes = 0;
                using (var service = new CraftWorkspaceService(doc, d => { if (!accept) return false; writes++; saved = JsonUtility.ToJson(d); return true; }, new Vector2(.148f, .210f)))
                {
                    var old = new[] { Item("old-roll", CraftItemKind.TapeRoll, "home-old-roll"), Item("user-roll", CraftItemKind.TapeRoll, "shaft-1"),
                        Item("old-page", CraftItemKind.LoosePage, "desktop", CraftOwner.Desktop), Item("user-book", CraftItemKind.TN, "desktop", CraftOwner.Desktop),
                        Item("spent-note", CraftItemKind.Note, null, CraftOwner.Deleted), Item("pen", CraftItemKind.Tool, "home-pen") };
                    Check("Legacy schema-1 inventory valid before layout", service.RegisterPhysical(old) && service.Data.deskLayoutVersion == 0 && CraftWorkspaceData.Validate(service.Document, out _));
                    string binder = string.Join("\n", service.Document.pages.Select(p => JsonUtility.ToJson(p)));
                    var definitions = old.Concat(new[] { Item("new-roll", CraftItemKind.TapeRoll, "home-new-roll"), Item("new-note", CraftItemKind.Note, "home-new-note") }).ToArray();
                    var targets = new[] { Target("old-roll", "home-old-roll", "shaft-1"), Target("user-roll", "home-user-roll", "shaft-2"),
                        Target("old-page", "home-page"), Target("user-book", "home-book"), Target("spent-note", "home-spent"),
                        Target("pen", "home-pen"), Target("new-roll", "home-new-roll", "shaft-2"), Target("new-note", "home-new-note") };
                    int before = writes;
                    Check("Legacy migration and new stock use one atomic write", service.RegisterDeskLayout(definitions, targets, 1) && writes == before + 1);
                    Check("Journal and craft ownership schema remain version 1", service.Document.schemaVersion == 1 && service.Data.schemaVersion == 1 && service.Data.deskLayoutVersion == 1);
                    Check("Occupied shaft preserves user roll and uses old roll home fallback", service.GetItem("user-roll").containerId == "shaft-1" && service.GetItem("old-roll").containerId == "home-old-roll");
                    Check("New roll fills a free display shaft without cloning", service.GetItem("new-roll").containerId == "shaft-2" && service.Items.Count(i => i.instanceId == "new-roll") == 1);
                    Check("All desktop loose paper moves into drawer home once", service.GetItem("old-page").owner == CraftOwner.Stock && service.GetItem("old-page").containerId == "home-page");
                    Check("Custom desktop notebook pose preserved", service.GetItem("user-book").owner == CraftOwner.Desktop && service.GetItem("user-book").worldPosition == old[3].worldPosition);
                    Check("Consumed note tombstone does not respawn", service.GetItem("spent-note").owner == CraftOwner.Deleted && service.Items.Count(i => i.instanceId == "spent-note") == 1);
                    Check("Tool kind appended without changing old kind numbers", (int)CraftItemKind.Tool == 7 && (int)CraftItemKind.TN == 6 && (int)CraftItemKind.TapeRoll == 0);
                    Check("Original binder pages unchanged", string.Join("\n", service.Document.pages.Select(p => JsonUtility.ToJson(p))) == binder);
                    int count = service.Items.Count;
                    Check("Second registration is idempotent", service.RegisterDeskLayout(definitions, targets, 1) && service.Items.Count == count && writes == before + 1);
                    Check("Paper can be taken out after one-time migration", service.UpdatePhysicalLocation("old-page", "desktop", new Pose(new Vector3(.1f, .76f, -.1f), Quaternion.identity), false) &&
                        service.RegisterDeskLayout(definitions, targets, 1) && service.GetItem("old-page").owner == CraftOwner.Desktop);
                    var conflict = Item("conflict", CraftItemKind.TapeRoll, "home-old-roll");
                    string stable = JsonUtility.ToJson(service.Document);
                    Check("No free preferred or home slot rejects whole registration", !service.RegisterDeskLayout(new[] { conflict }, new[] { Target("conflict", "home-old-roll", "shaft-1") }, 1) && stable == JsonUtility.ToJson(service.Document));
                    var invalid = Target("new-invalid", "home-invalid"); invalid.homePosition = new Vector3(float.NaN, 0, 0);
                    Check("Nonfinite layout pose rejected before persistence", !service.RegisterDeskLayout(new[] { Item("new-invalid", CraftItemKind.Note, "home-invalid") }, new[] { invalid }, 1) && stable == JsonUtility.ToJson(service.Document));
                    accept = false;
                    Check("Failed save cannot add new finite inventory", !service.RegisterDeskLayout(new[] { Item("save-failure", CraftItemKind.Note, "home-fail") }, new[] { Target("save-failure", "home-fail") }, 1) && stable == JsonUtility.ToJson(service.Document));
                    accept = true;
                    var restarted = new CraftWorkspaceService(JsonUtility.FromJson<JournalDocument>(saved), _ => true, new Vector2(.148f, .210f));
                    Check("Restart preserves layout version and tombstone", restarted.Data.deskLayoutVersion == 1 && restarted.GetItem("spent-note").owner == CraftOwner.Deleted && CraftWorkspaceData.Validate(restarted.Document, out _));
                    restarted.Dispose();
                    Check("Unrecognized future layout version safely rejected", !service.RegisterDeskLayout(definitions, targets, CraftWorkspaceData.CurrentDeskLayoutVersion + 1) && stable == JsonUtility.ToJson(service.Document));
                }
            }
            catch (Exception error) { Debug.LogException(error); Check(error.GetType().Name + ": " + error.Message, false); }
            bool pass = checks.Count > 0 && checks.All(c => c.passed);
            Directory.CreateDirectory("docs/desk-usability");
            File.WriteAllText("docs/desk-usability/data-result.json", JsonUtility.ToJson(new Report { timestampUtc = DateTime.UtcNow.ToString("O"), scope = "Actual Unity data transactions; not Play Mode or native input.", passed = pass, checks = checks.ToArray() }, true));
            return pass;
        }
        public static void Run() { if (!RunDataChecks()) throw new InvalidOperationException("Desk usability migration data checks failed."); }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VintageJournal.Editor
{
    /// <summary>Commission-letter data/transaction checks through the real craft save seam. No Play Mode claim.</summary>
    public static class CommissionDataTests
    {
        static int passed, failed;
        [Serializable] public sealed class Result { public string name; public bool passed; }
        [Serializable] public sealed class Report { public string timestampUtc, scope, unityVersion; public int passed, failed; public Result[] checks; }
        static readonly List<Result> checks = new List<Result>();

        [MenuItem("Vintage Journal/Commission/Run commission data checks")]
        public static void Run() { if (!RunDataChecks()) throw new InvalidOperationException("Commission data checks failed."); }

        public static bool RunDataChecks()
        {
            passed = failed = 0; checks.Clear();
            try
            {
                var catalog = CommissionCatalog.Load();
                var doc = JournalDocument.CreateDefault(JournalPaperCatalog.Load());
                var legacyJson = JsonUtility.ToJson(doc);
                var legacy = JsonUtility.FromJson<JournalDocument>(legacyJson); legacy.NormalizeOptionalData();
                Check("Legacy v1 save without commission branch stays valid and absent",
                    legacy.commissionDesk == null && !legacy.commissionDeskEnabled && CommissionDeskData.Validate(legacy, out _) && legacy.schemaVersion == 1);

                int saves = 0; bool accept = true;
                var craft = new CraftWorkspaceService(doc, candidate => { if (!accept) return false; saves++; return true; }, new Vector2(.148f, .210f));
                var stock = new[] {
                    new CraftItemRecord { instanceId = "c-roll", assetId = "tape_01", kind = CraftItemKind.TapeRoll, owner = CraftOwner.Stock, containerId = "drawer-c", sizeMeters = new Vector2(.045f, .015f) },
                    new CraftItemRecord { instanceId = "c-rose", assetId = "sticker_02", kind = CraftItemKind.Sticker, owner = CraftOwner.Stock, containerId = "hang-c1", sizeMeters = new Vector2(.03f, .03f) },
                    new CraftItemRecord { instanceId = "c-moon", assetId = "sticker_12", kind = CraftItemKind.Sticker, owner = CraftOwner.Stock, containerId = "hang-c2", sizeMeters = new Vector2(.03f, .03f) }
                };
                Check("Register commission test stock", craft.RegisterPhysical(stock));

                CommissionDeskService desk = null;
                desk = new CommissionDeskService(catalog, () => craft.Document.commissionDesk, (m, l) => craft.CommitCommission(m, l),
                    (o, c, p) => craft.GetPageItems(o, o == CraftOwner.JournalPage ? null : c, p));
                Check("Service creates independently versioned branch", craft.Document.commissionDeskEnabled && craft.Document.commissionDesk.schemaVersion == 1 && craft.Document.commissionDesk.letters.Count == 0);
                desk.Tick(1); Check("No letter before first delay", desk.Data.letters.Count == 0);
                desk.Tick(1.5); var first = desk.OpenLetters.FirstOrDefault();
                Check("First authored letter arrives after delay", first != null && first.letterId == "letter-001" && first.wanted.SequenceEqual(new[] { "花草", "暖色" }));

                string pageA = craft.Books[0].pages[0].pageId, pageA2 = craft.Books[0].pages[1].pageId;
                var empty = desk.Evaluate(first, CraftOwner.ReleaseBookPage, "release-a", pageA);
                Check("Empty page has no content and cannot be sent", !empty.HasContent && !desk.Send(first.letterId, CraftOwner.ReleaseBookPage, "release-a", pageA, null) && desk.OpenLetters.Count() == 1);

                Check("Place rose sticker and cut tape on release page A1",
                    craft.BeginStock("c-rose") && craft.CommitHeld(new CraftPlacement { owner = CraftOwner.ReleaseBookPage, containerId = "release-a", pageId = pageA, position = new Vector2(.4f, .4f) }) &&
                    craft.CreateTapeStrip("tape_01", "c-roll", .05f) && craft.CommitHeld(new CraftPlacement { owner = CraftOwner.ReleaseBookPage, containerId = "release-a", pageId = pageA, position = new Vector2(.5f, .7f) }));
                var eval = desk.Evaluate(first, CraftOwner.ReleaseBookPage, "release-a", pageA);
                Check("Tags come from data table: rose+warm tape match 花草/暖色 with tier 3", eval.itemCount == 2 && eval.matched.Length == 2 && eval.missing.Length == 0 && eval.tier == 3);

                string itemsBefore = JsonUtility.ToJson(new Wrapper { items = craft.Data.items.Select(i => i.Clone()).ToList() });
                Check("Send records photo-only delivery", desk.Send(first.letterId, CraftOwner.ReleaseBookPage, "release-a", pageA, "letter-001.png"));
                string itemsAfter = JsonUtility.ToJson(new Wrapper { items = craft.Data.items.Select(i => i.Clone()).ToList() });
                var sent = desk.Data.letters.First(l => l.letterId == first.letterId);
                Check("Sending moves, copies and deletes no material instance", itemsBefore == itemsAfter && sent.status == CommissionLetterStatus.Sent && sent.tier == 3 && sent.itemCount == 2);
                Check("Sent snapshot passes full save validation", CraftWorkspaceData.Validate(craft.Document, out _) && CommissionDeskData.Validate(craft.Document, out _));

                desk.Tick(3.5); desk.Tick(3.5); var second = desk.OpenLetters.FirstOrDefault();
                Check("Next letter follows the send within a few seconds", second != null && second.letterId == "letter-002");
                Check("Identical page content cannot be sent twice", !desk.Send(second.letterId, CraftOwner.ReleaseBookPage, "release-a", pageA, null) && desk.LastMessage.Contains("原样"));

                Check("Undo of the material step keeps the sent letter", craft.Undo() && craft.Document.commissionDesk.letters.First(l => l.letterId == first.letterId).status == CommissionLetterStatus.Sent);
                Check("Undo reverted only the tape strip", craft.GetPageItems(CraftOwner.ReleaseBookPage, "release-a", pageA).Count == 1 && craft.Redo());

                for (int i = 0; i < 4; i++) desk.Tick(4.9);
                Check("Reply arrives after configured delay with stamps", desk.Data.letters.First(l => l.letterId == first.letterId).status == CommissionLetterStatus.Replied &&
                    desk.Data.stamps == 3 && desk.Data.letters.First(l => l.letterId == first.letterId).replyText.Contains("小满"));

                // Partial match: a moon sticker on another page for "旅行/书信" yields the plain tier.
                Check("Move moon sticker to page A2", craft.BeginStock("c-moon") && craft.CommitHeld(new CraftPlacement { owner = CraftOwner.ReleaseBookPage, containerId = "release-a", pageId = pageA2, position = new Vector2(.5f, .5f) }));
                var mismatch = desk.Evaluate(second, CraftOwner.ReleaseBookPage, "release-a", pageA2);
                Check("Mismatch still sendable with plain tier (no failure)", mismatch.tier == 1 && desk.Send(second.letterId, CraftOwner.ReleaseBookPage, "release-a", pageA2, null));
                desk.FastForwardReplies();
                Check("Plain reply grants one stamp", desk.Data.stamps == 4 && desk.Data.letters.First(l => l.letterId == second.letterId).stamps == 1);

                var roundTrip = JsonUtility.FromJson<JournalDocument>(JsonUtility.ToJson(craft.Document)); roundTrip.NormalizeOptionalData();
                Check("Save round trip keeps letters, stamps and clock", roundTrip.commissionDeskEnabled && roundTrip.commissionDesk.letters.Count == desk.Data.letters.Count &&
                    roundTrip.commissionDesk.stamps == 4 && Math.Abs(roundTrip.commissionDesk.clockSeconds - desk.Data.clockSeconds) < 1e-6 && CommissionDeskData.Validate(roundTrip, out _));

                var forged = roundTrip.DeepClone(); forged.commissionDesk.stamps += 5;
                Check("Validation rejects stamp totals that disagree with replies", !CommissionDeskData.Validate(forged, out _));
                var duplicate = roundTrip.DeepClone(); duplicate.commissionDesk.letters[1].sentSignature = duplicate.commissionDesk.letters[0].sentSignature;
                Check("Validation rejects a page signature sent twice", !CommissionDeskData.Validate(duplicate, out _));
                var future = roundTrip.DeepClone(); future.commissionDesk.schemaVersion = 99;
                Check("Unknown commission schema is rejected, original save kept", !CommissionDeskData.Validate(future, out _));

                accept = false; int before = desk.Data.letters.Count;
                desk.RequestLetterNow();
                Check("Rejected save leaves commission state unchanged", desk.Data.letters.Count == before);
                accept = true;

                var ids = new HashSet<string>(); var known = new HashSet<string>(catalog.AllTags); bool generated = true;
                for (int i = 0; i < 60; i++)
                {
                    var l = catalog.CreateLetter(i, 0);
                    generated &= ids.Add(l.letterId) && l.wanted.Length > 0 && l.wanted.All(known.Contains) && !string.IsNullOrEmpty(l.body) && !l.body.Contains("{");
                }
                Check("60 letters (7 authored + generated) have unique ids and satisfiable tags", generated);

                int maxOpen = 0;
                var flow = new CraftWorkspaceService(JournalDocument.CreateDefault(JournalPaperCatalog.Load()), _ => true, new Vector2(.148f, .210f));
                var flowDesk = new CommissionDeskService(catalog, () => flow.Document.commissionDesk, (m, l) => flow.CommitCommission(m, l), (o, c, p) => flow.GetPageItems(o, c, p));
                for (int i = 0; i < 400; i++) { flowDesk.Tick(4.5); maxOpen = Math.Max(maxOpen, flowDesk.OpenLetters.Count()); }
                Check("Idle desk keeps receiving letters but caps open letters at 3", maxOpen == catalog.maxOpenLetters && flowDesk.Data.letters.Count == catalog.maxOpenLetters);
                Check("Commission commits reached the save seam", saves > 0);
            }
            catch (Exception error) { failed++; checks.Add(new Result { name = "Exception: " + error.Message, passed = false }); Debug.LogException(error); }
            string directory = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "docs", "commission");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "commission-data-tests.json"), JsonUtility.ToJson(new Report
            {
                timestampUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion, passed = passed, failed = failed, checks = checks.ToArray(),
                scope = "Actual Unity JsonUtility data checks of commission letters through CraftWorkspaceService.CommitCommission; no Play Mode or native input claim."
            }, true));
            Debug.Log("[CommissionData] " + passed + " passed, " + failed + " failed");
            return failed == 0;
        }
        [Serializable] sealed class Wrapper { public List<CraftItemRecord> items; }
        static void Check(string name, bool condition)
        { if (condition) passed++; else failed++; checks.Add(new Result { name = name, passed = condition }); Debug.Log("[CommissionData] " + (condition ? "PASS " : "FAIL ") + name); }
    }
}

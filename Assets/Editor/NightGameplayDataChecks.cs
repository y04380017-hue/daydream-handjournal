using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VintageJournal.Editor
{
    public static class NightGameplayDataChecks
    {
        public static bool RunDataChecks()
        {
            try
            {
                var legacy = JournalDocument.CreateDefault(JournalPaperCatalog.Load());
                var catalog = DecorationCatalog.Load();
                Require(legacy.DeepClone().dreamGameplay == null, "Legacy optional branch stays absent");
                bool accept = true;
                var service = new CraftWorkspaceService(legacy, _ => accept, new Vector2(.148f, .210f), catalog);
                long now = new DateTime(2026, 11, 7, 8, 0, 0, DateTimeKind.Utc).Ticks;
                Require(service.InitializeDreamGameplay(now), "Initialize night slice");
                Require(service.Document.dreamGameplay.pending.Count == 3, "Three starter catches");
                string first = service.Document.dreamGameplay.pending[0].tokenId;
                string asset = service.Document.dreamGameplay.pending[0].assetId;
                accept = false;
                Require(!service.CatchNightMaterial(first) && service.Document.dreamGameplay.pending.Count == 3, "Rejected save leaves catch pending");
                accept = true;
                Require(service.CatchNightMaterial(first), "Atomic catch");
                Require(!service.CatchNightMaterial(first), "Same catch cannot be redeemed twice");
                var item = service.Items.Single(i => i.assetId == asset && i.instanceId.StartsWith("dream-"));
                string id = item.instanceId;
                Require(service.BeginStock(id), "Take finite material");
                var restart = new CraftWorkspaceService(service.Document.DeepClone(), _ => true, new Vector2(.148f, .210f), catalog);
                Require(restart.GetItem(id).owner == CraftOwner.Stock && restart.HeldItem == null, "Restart restores held material to bag");
                Require(service.CancelHeld() && service.GetItem(id).owner == CraftOwner.Stock, "Cancel restores material");
                Require(service.BeginStock(id) && service.CommitHeld(new CraftPlacement { owner = CraftOwner.JournalPage,
                    containerId = "journal", pageId = service.Document.pages[0].pageId, position = new Vector2(.5f,.5f), scale = 1 }), "Place collected material");
                // A catch after this command must survive undoing it; inventory events are not edit commands.
                string laterToken = service.Document.dreamGameplay.pending[0].tokenId;
                Require(service.CatchNightMaterial(laterToken), "Second catch after page edit");
                string laterId = "dream-catch-" + laterToken;
                Require(service.Undo() && service.GetItem(id).owner == CraftOwner.Stock && service.GetItem(laterId) != null, "Undo keeps later catch");
                Require(service.Document.dreamGameplay.pending.Count == 1, "Undo never regenerates captured objects");
                Require(service.Redo() && service.GetItem(id).owner == CraftOwner.JournalPage, "Redo material placement");
                Require(service.DeleteAttached(id), "Delete finite material");
                var deletedRestart = new CraftWorkspaceService(service.Document.DeepClone(), _ => true, new Vector2(.148f,.210f), catalog);
                Require(deletedRestart.GetItem(id).owner == CraftOwner.Deleted, "Deleted material stays a tombstone");
                int count = service.Document.dreamGameplay.pending.Count;
                service.RefreshNightCollection(now - TimeSpan.FromHours(1).Ticks);
                Require(service.Document.dreamGameplay.pending.Count == count, "Backward wall clock never gives extra stock");
                service.RefreshNightCollection(now + TimeSpan.FromDays(30).Ticks);
                Require(service.Document.dreamGameplay.pending.Count == 6, "Offline replenishment caps at six");
                var copy = service.Document.DeepClone();
                Require(DreamGameplayData.Validate(copy, out _), "Night branch validates after JSON roundtrip");
                copy.dreamGameplay.pending.Add(copy.dreamGameplay.pending[0]);
                Require(!DreamGameplayData.Validate(copy, out _), "Reject duplicate catch ids");
                Require(catalog.Find(asset)?.IsAvailable == true && !catalog.assets.Any(d => d.assetId == asset), "Earned art resolves without unlimited browser stock");
                foreach (string art in new[] { "night-glow-ink", "night-star-sticker", "night-lantern-paper", "night-moonlit-star", "night-forgotten-dream" })
                {
                    var definition = NightMaterialCatalog.Find(art);
                    Require(definition.alphaMask != null && definition.alphaMask.Any(a => a == 0) && definition.alphaMask.Any(a => a > 240), "Collected art has transparent margins and visible pattern: " + art);
                }
                service.Dispose(); restart.Dispose(); deletedRestart.Dispose();
                Debug.Log("[NightData] PASS all collection, finite ownership, rollback and compatibility checks");
                return true;
            }
            catch (Exception error) { Debug.LogError("[NightData] FAIL " + error); return false; }
        }
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); Debug.Log("[NightData] PASS " + message); }
        [MenuItem("Vintage Journal/Night gameplay/Run data checks")]
        public static void Run()
        {
            bool pass = RunDataChecks() && NightCommissionChecks.RunDataChecks() && CraftDataTests.RunDataChecks() &&
                JournalDataTests.RunDataChecks() && DecorationDataTests.RunDataChecks() && CommissionDataTests.RunDataChecks();
            Directory.CreateDirectory("docs/night-gameplay");
            File.WriteAllText("docs/night-gameplay/data-result.txt", DateTime.UtcNow.ToString("O") + "\nUnity data checks: " + (pass ? "PASS" : "FAIL"));
            if (!pass) throw new InvalidOperationException("Night or legacy data checks failed.");
        }
    }
}

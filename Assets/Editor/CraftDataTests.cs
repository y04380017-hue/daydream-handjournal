using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VintageJournal.Editor
{
    /// <summary>Data/transaction regression checks, separate from rendered Play Mode and native mouse tests.</summary>
    public static class CraftDataTests
    {
        static int passed, failed;
        [Serializable] public sealed class Result { public string name,detail;public bool passed; }
        [Serializable] public sealed class Report { public string timestampUtc,scope,unityVersion;public int passed,failed;public Result[] checks; }
        static readonly List<Result> checks=new List<Result>();
        [MenuItem("Vintage Journal/Stage 4B/Run ownership data checks")]
        public static void Run() { if (!RunDataChecks()) throw new InvalidOperationException("Craft ownership data checks failed."); }
        public static bool RunDataChecks()
        {
            passed = failed = 0;checks.Clear();
            try
            {
                var papers = JournalPaperCatalog.Load();
                var doc = JournalDocument.CreateDefault(papers);
                var legacyClone=doc.DeepClone();
                Check("Legacy deep clone retains absent optional workspace",legacyClone.craftWorkspace==null&&!legacyClone.craftWorkspaceEnabled);
                var rawLegacy=JsonUtility.FromJson<JournalDocument>(JsonUtility.ToJson(doc));rawLegacy.NormalizeOptionalData();
                Check("Unity inline-null ghost normalizes without changing pages",rawLegacy.craftWorkspace==null&&rawLegacy.pages.Length==24&&rawLegacy.pages[0].pageId==doc.pages[0].pageId);
                string latest = null; bool accept = true;
                var service = new CraftWorkspaceService(doc, candidate => { if (!accept) return false; latest = JsonUtility.ToJson(candidate); return true; }, new Vector2(.148f, .210f));
                Check("Legacy v1 receives additive six-page release books", service.Document.schemaVersion == 1 && service.Books.Count == 2 && service.Books.All(b => b.pages.Count == 6));
                var workspaceClone=service.Document.DeepClone();
                Check("No-held workspace clone removes only empty pending ghost",workspaceClone.craftWorkspaceEnabled&&workspaceClone.craftWorkspace.pending==null&&CraftWorkspaceData.Validate(workspaceClone,out _));
                var stock = new[] {
                    new CraftItemRecord { instanceId="test-roll",assetId="tape_01",kind=CraftItemKind.TapeRoll,owner=CraftOwner.Stock,containerId="drawer-1",sizeMeters=new Vector2(.045f,.015f) },
                    new CraftItemRecord { instanceId="test-sticker",assetId="sticker_01",kind=CraftItemKind.Sticker,owner=CraftOwner.Stock,containerId="hanging-1",sizeMeters=new Vector2(.03f,.03f) }
                };
                Check("Register finite independently identified stock", service.RegisterPhysical(stock));
                Check("Taking stock creates one durable held owner", service.BeginStock("test-sticker") && service.HeldItem.instanceId=="test-sticker" && service.Data.pending.origin.owner==CraftOwner.Stock);
                Check("Duplicate grab rejected", !service.BeginStock("test-sticker") && !service.CreateTapeStrip("tape_01","test-roll",.05f));
                Check("Cancel restores original finite stock", service.CancelHeld() && service.GetItem("test-sticker").owner==CraftOwner.Stock && service.HeldItem==null);
                service.BeginStock("test-sticker");
                Check("Attach to A preserves ID", service.CommitHeld(Page(service,"release-a")) && service.GetItem("test-sticker").containerId=="release-a");
                service.BeginPeel("test-sticker");bool navigateHeld=service.SetBookPage("release-a",2);
                var nextPage=Page(service,"release-a");nextPage.pageId=service.FindBook("release-a").pages[2].pageId;
                Check("Held transaction can target another page without copying",navigateHeld&&service.CommitHeld(nextPage)&&
                    service.GetItem("test-sticker").pageId==nextPage.pageId&&service.Items.Count(i=>i.instanceId=="test-sticker")==1);
                var safeNavigated=JsonUtility.ToJson(service.GetItem("test-sticker"));service.BeginPeel("test-sticker");service.SetBookPage("release-a",4);
                Check("Cancel after navigation restores exact source-page record",service.CancelHeld()&&JsonUtility.ToJson(service.GetItem("test-sticker"))==safeNavigated&&service.FindBook("release-a").currentPage==4);
                service.SetBookPage("release-a",0);service.BeginPeel("test-sticker");service.CommitHeld(Page(service,"release-a"));
                service.BeginPeel("test-sticker");
                Check("Transfer A to B never copies", service.CommitHeld(Page(service,"release-b")) && service.Items.Count(i=>i.instanceId=="test-sticker")==1 && service.GetItem("test-sticker").containerId=="release-b");
                Check("Cross-container undo restores A", service.Undo() && service.GetItem("test-sticker").containerId=="release-a");
                Check("Cross-container redo restores B", service.Redo() && service.GetItem("test-sticker").containerId=="release-b");
                service.BeginPeel("test-sticker");
                string pageId=service.Document.pages[0].pageId;
                Check("Transfer to journal creates exactly one compatible projection", service.CommitHeld(new CraftPlacement { owner=CraftOwner.JournalPage,containerId="journal",pageId=pageId }) &&
                    service.Document.pages.Sum(p=>p.decorations.Count(d=>d.instanceId=="test-sticker"))==1 && service.GetItem("test-sticker").owner==CraftOwner.JournalPage);
                var metricProjection=service.Document.pages[0].decorations.First(d=>d.instanceId=="test-sticker");
                Check("Physical projection carries actual page metric dimensions",metricProjection.physicalMaterial&&metricProjection.physicalPageSize==new Vector2(.148f,.210f));
                var outside=service.Document.DeepClone();outside.pages[0].decorations.First(d=>d.instanceId=="test-sticker").position=Vector2.zero;
                Check("Physical projection cannot save outside metric page bounds",!CraftWorkspaceData.Validate(outside,out _));
                Check("Undo journal transfer removes projection and restores B", service.Undo() && service.GetItem("test-sticker").containerId=="release-b" &&
                    !service.Document.pages.Any(p=>p.decorations.Any(d=>d.instanceId=="test-sticker")));
                Check("Cut long tape retains the original roll", service.CreateTapeStrip("tape_01","test-roll",.30f) && service.GetItem("test-roll").owner==CraftOwner.Stock);
                string stripId=service.HeldItem.instanceId;
                Check("Oversize strip is rejected without shortening or ownership loss", !service.CommitHeld(Page(service,"release-a")) &&
                    service.HeldItem.instanceId==stripId && Mathf.Approximately(service.HeldItem.lengthMeters,.30f));
                service.SetHeldTransform(90,.3f); var rotated=Page(service,"release-a");rotated.rotationDegrees=90;rotated.scale=.3f;
                Check("Explicit rotation/scale allows a legal long strip", service.CommitHeld(rotated) && Mathf.Approximately(service.GetItem(stripId).lengthMeters,.30f));
                Check("Delete creates finite-source tombstone", service.DeleteAttached("test-sticker") && service.GetItem("test-sticker").owner==CraftOwner.Deleted);
                service.RegisterPhysical(stock);
                Check("Reload registration cannot respawn consumed stock", service.GetItem("test-sticker").owner==CraftOwner.Deleted);
                string stable=JsonUtility.ToJson(service.Document); accept=false;
                Check("Rejected persistence leaves live document unchanged", !service.BeginPeel(stripId) && stable==JsonUtility.ToJson(service.Document));accept=true;
                Check("Pending peel serialized with exact rollback", service.BeginPeel(stripId) && latest.Contains("pending"));
                var restarted=new CraftWorkspaceService(JsonUtility.FromJson<JournalDocument>(latest), _=>true,new Vector2(.148f,.210f));
                Check("Restart safely cancels pending transfer", restarted.HeldItem==null && restarted.GetItem(stripId).owner==CraftOwner.ReleaseBookPage && restarted.GetItem(stripId).containerId=="release-a");
                var duplicate=restarted.Document.DeepClone();duplicate.craftWorkspace.items.Add(duplicate.craftWorkspace.items.First().Clone());
                Check("Duplicate ownership snapshot rejected", !CraftWorkspaceData.Validate(duplicate,out _));
                var occupied=restarted.Document.DeepClone();
                occupied.craftWorkspace.items.Add(new CraftItemRecord{instanceId="conflicting-roll",assetId="tape_02",kind=CraftItemKind.TapeRoll,
                    owner=CraftOwner.Stock,containerId="drawer-1",sizeMeters=new Vector2(.045f,.015f)});
                Check("Two stock records cannot occupy the same physical slot",!CraftWorkspaceData.Validate(occupied,out _));
                var legacy=JournalDocument.CreateDefault(papers);
                var legacyDecoration=new JournalDecorationData{instanceId="legacy-object",assetId="tape_01",type="Tape",pageId=legacy.pages[0].pageId,
                    size=new Vector2(.3f,.08f),position=new Vector2(.44f,.63f),scale=Vector2.one*.7f,length=1.4f,rotationDegrees=17,sortOrder=3,payloadJson="legacy payload"};
                legacy.pages[0].decorations.Add(legacyDecoration);string exact=JsonUtility.ToJson(legacyDecoration);
                string pendingJson=null;var legacyService=new CraftWorkspaceService(legacy,c=>{pendingJson=JsonUtility.ToJson(c);return true;},new Vector2(.148f,.210f));
                legacyService.BeginPeel("legacy-object");var recovered=new CraftWorkspaceService(JsonUtility.FromJson<JournalDocument>(pendingJson),_=>true,new Vector2(.148f,.210f));
                Check("Restart restores legacy projection byte-for-byte including payload", JsonUtility.ToJson(recovered.Document.pages[0].decorations.Single())==exact);
                Check("All resulting snapshots have one ID per owner", CraftWorkspaceData.Validate(recovered.Document,out _) && CraftWorkspaceData.Validate(restarted.Document,out _));
                var independent=new CraftWorkspaceService(JournalDocument.CreateDefault(papers),_=>true,new Vector2(.148f,.210f));
                independent.RegisterPhysical(new[]{
                    new CraftItemRecord{instanceId="independent-book",assetId="ReleaseBook_A",kind=CraftItemKind.ReleaseBook,owner=CraftOwner.Stock,containerId="independent-book-slot",sizeMeters=new Vector2(.112f,.156f)},
                    new CraftItemRecord{instanceId="independent-roll",assetId="tape_01",kind=CraftItemKind.TapeRoll,owner=CraftOwner.Stock,containerId="independent-roll-slot",sizeMeters=new Vector2(.045f,.015f)},
                    new CraftItemRecord{instanceId="independent-sticker",assetId="sticker_01",kind=CraftItemKind.Sticker,owner=CraftOwner.Stock,containerId="independent-sticker-slot",sizeMeters=new Vector2(.03f,.03f)}
                });
                independent.BeginStock("independent-sticker");independent.CommitHeld(Page(independent,"release-a"));
                independent.UpdatePhysicalLocation("independent-book","desktop",new Pose(new Vector3(.27f,.77f,-.22f),Quaternion.identity),false);
                independent.UpdatePhysicalLocation("independent-roll","new-shaft-slot",new Pose(new Vector3(-.4f,.92f,-.31f),Quaternion.Euler(0,0,90)),true);
                independent.SetDrawerState("independent-drawer",true,1);independent.SetDrawerState("shaft:new-shaft-slot",true,1);
                independent.SetBookOpen("release-a",true);independent.SetBookPage("release-a",2);
                bool undone=independent.Undo();
                Check("Material undo preserves unrelated physical locations and mechanism/view state",undone&&independent.GetItem("independent-sticker").owner==CraftOwner.Stock&&
                    independent.GetItem("independent-book").owner==CraftOwner.Desktop&&independent.GetItem("independent-book").worldPosition==new Vector3(.27f,.77f,-.22f)&&
                    independent.GetItem("independent-roll").containerId=="new-shaft-slot"&&independent.GetDrawerState("independent-drawer").isOpen&&
                    independent.GetDrawerState("shaft:new-shaft-slot").openFraction==1&&independent.FindBook("release-a").isOpen&&independent.FindBook("release-a").currentPage==2);
                Check("Material redo preserves those independent physical changes too",independent.Redo()&&independent.GetItem("independent-sticker").owner==CraftOwner.ReleaseBookPage&&
                    independent.GetItem("independent-book").owner==CraftOwner.Desktop&&independent.GetItem("independent-roll").containerId=="new-shaft-slot"&&
                    independent.GetDrawerState("independent-drawer").openFraction==1&&independent.FindBook("release-a").isOpen);
            }
            catch(Exception error){Check("Unhandled data-test exception",false);Debug.LogError(error);}
            Debug.Log("[CraftData] Completed passed="+passed+" failed="+failed+" (data checks only; no Play Mode/native input claim).");
            string directory=Path.Combine(Directory.GetParent(Application.dataPath).FullName,"docs/stage4b-logs");Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory,"craft-data-tests.json"),JsonUtility.ToJson(new Report{timestampUtc=DateTime.UtcNow.ToString("O"),
                scope="Actual Unity JsonUtility data/ownership transaction checks; no Play Mode or native input claim.",unityVersion=Application.unityVersion,passed=passed,failed=failed,checks=checks.ToArray()},true));
            return failed==0;
        }
        static CraftPlacement Page(CraftWorkspaceService service,string id)
        {return new CraftPlacement{owner=CraftOwner.ReleaseBookPage,containerId=id,pageId=service.FindBook(id).pages[0].pageId};}
        static void Check(string name,bool condition)
        {if(condition)passed++;else failed++;checks.Add(new Result{name=name,passed=condition,detail=condition?"Runtime data assertion passed.":"Runtime data assertion failed; inspect accompanying [CraftData] log."});Debug.Log("[CraftData] "+(condition?"PASS ":"FAIL ")+name);}
    }
}

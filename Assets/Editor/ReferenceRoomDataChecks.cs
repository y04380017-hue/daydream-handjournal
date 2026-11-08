using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace VintageJournal.Editor
{
    public static class ReferenceRoomDataChecks
    {
        [Serializable] public sealed class Row { public string name; public bool passed; }
        [Serializable] public sealed class Report { public bool passed; public string utc; public Row[] checks; }
        static readonly List<Row> rows=new List<Row>();
        static void Check(string name,bool pass){rows.Add(new Row{name=name,passed=pass});Debug.Log("[ReferenceData] "+(pass?"PASS ":"FAIL ")+name);}
        static CraftItemRecord Item(string id,CraftItemKind kind,string slot,CraftOwner owner=CraftOwner.Stock)=>new CraftItemRecord{
            instanceId=id,assetId="reference-"+id,kind=kind,containerId=slot,owner=owner,sizeMeters=new Vector2(.03f,.03f),worldPosition=new Vector3(.3f,.77f,-.1f)};
        static CraftDeskLayoutTarget Target(string id,string home,string display=null)=>new CraftDeskLayoutTarget{instanceId=id,homeSlotId=home,displaySlotId=display,homePosition=new Vector3(-.45f,.8f,.1f),displayPosition=new Vector3(-.45f,.8f,.1f)};
        public static void Run()
        {
            rows.Clear();
            try
            {
                var initial=new[]{Item("book-a",CraftItemKind.ReleaseBook,"desktop",CraftOwner.Desktop),Item("book-b",CraftItemKind.ReleaseBook,"old-work-b"),
                    Item("sheet",CraftItemKind.LoosePage,"home-sheet"),Item("peel",CraftItemKind.Note,"home-peel"),Item("spent",CraftItemKind.Sticker,null,CraftOwner.Deleted)};
                var oldTargets=new[]{Target("book-a","home-a","old-work-a"),Target("book-b","home-b","old-work-b"),Target("sheet","home-sheet"),Target("peel","home-peel"),Target("spent","home-spent")};
                bool accept=true;int writes=0;
                using(var service=new CraftWorkspaceService(JournalDocument.CreateDefault(JournalPaperCatalog.Load()),_=>{if(!accept)return false;writes++;return true;},new Vector2(.148f,.21f)))
                {
                    Check("Existing v1 layout remains supported",service.RegisterDeskLayout(initial,oldTargets,1)&&service.Data.deskLayoutVersion==1);
                    service.UpdatePhysicalLocation("sheet","desktop",new Pose(new Vector3(.1f,.77f,-.2f),Quaternion.identity),false);
                    service.SetBookOpen("release-a",true);service.SetBookOpen("release-b",true);service.SetBookPage("release-a",2);
                    service.BeginStock("peel");service.CommitHeld(new CraftPlacement{owner=CraftOwner.ReleaseBookPage,containerId="release-a",pageId=service.FindBook("release-a").pages[2].pageId});
                    string artwork=JsonUtility.ToJson(service.GetItem("peel"));string pageIds=string.Join("|",service.FindBook("release-a").pages.Select(p=>p.pageId));
                    string v1=JsonUtility.ToJson(service.Document);
                    var a=Target("book-a","home-a","rack-a");a.relocateStockOnMigration=a.relocateDesktopOnMigration=true;a.releaseBookId="release-a";
                    var b=Target("book-b","home-b","rack-b");b.relocateStockOnMigration=b.relocateDesktopOnMigration=true;b.releaseBookId="release-b";
                    var targets=new[]{a,b,oldTargets[2],oldTargets[3],oldTargets[4],Target("extra-roll","home-extra","rack-extra")};
                    var definitions=initial.Concat(new[]{Item("extra-roll",CraftItemKind.TapeRoll,"home-extra")}).ToArray();
                    int priorWrites=writes;
                    Check("Version two migration and extra stock commit atomically",service.RegisterDeskLayout(definitions,targets,2)&&writes==priorWrites+1&&service.Data.deskLayoutVersion==2);
                    Check("Desktop release book moves once into lowest rack slot",service.GetItem("book-a").owner==CraftOwner.Stock&&service.GetItem("book-a").containerId=="rack-a");
                    Check("Old work slot release book also moves once",service.GetItem("book-b").containerId=="rack-b");
                    Check("Repacking closes covers without resetting page cursor",!service.FindBook("release-a").isOpen&&!service.FindBook("release-b").isOpen&&service.FindBook("release-a").currentPage==2);
                    Check("Real six page identities and attached material unchanged",pageIds==string.Join("|",service.FindBook("release-a").pages.Select(p=>p.pageId))&&artwork==JsonUtility.ToJson(service.GetItem("peel")));
                    Check("V1 custom desktop sheet is not re-homed by unrelated v2 changes",service.GetItem("sheet").owner==CraftOwner.Desktop);
                    Check("Spent finite stock remains a tombstone",service.GetItem("spent").owner==CraftOwner.Deleted&&service.Items.Count(i=>i.instanceId=="spent")==1);
                    Check("New display roll has one unique instance",service.Items.Count(i=>i.instanceId=="extra-roll")==1&&service.GetItem("extra-roll").containerId=="rack-extra");
                    string stable=JsonUtility.ToJson(service.Document);int stableWrites=writes;
                    Check("Repeated migration is byte identical without additional save",service.RegisterDeskLayout(definitions,targets,2)&&stable==JsonUtility.ToJson(service.Document)&&writes==stableWrites);
                    Check("Old v1 callers cannot downgrade or undo v2 placement",service.RegisterDeskLayout(initial,oldTargets,1)&&stable==JsonUtility.ToJson(service.Document));
                    service.UpdatePhysicalLocation("book-a","desktop",new Pose(new Vector3(-.3f,.77f,-.1f),Quaternion.identity),false);
                    Check("Subsequent user book movement survives restart registration",service.RegisterDeskLayout(definitions,targets,2)&&service.GetItem("book-a").owner==CraftOwner.Desktop);
                    Check("Material undo preserves new layout version and independent book pose",service.Undo()&&service.Data.deskLayoutVersion==2&&service.GetItem("book-a").owner==CraftOwner.Desktop&&service.GetItem("book-b").containerId=="rack-b");
                    Check("All original schema-one invariants still validate",service.Document.schemaVersion==1&&service.Data.schemaVersion==1&&CraftWorkspaceData.Validate(service.Document,out _));
                    using(var retry=new CraftWorkspaceService(JsonUtility.FromJson<JournalDocument>(v1),_=>accept,new Vector2(.148f,.21f)))
                    {
                        string before=JsonUtility.ToJson(retry.Document);accept=false;
                        Check("Rejected v2 persistence preserves old covers layout and inventory",!retry.RegisterDeskLayout(definitions,targets,2)&&before==JsonUtility.ToJson(retry.Document));accept=true;
                    }
                    using(var reload=new CraftWorkspaceService(service.Document.DeepClone(),_=>true,new Vector2(.148f,.21f)))
                        Check("Reload retains v2 registry without duplicating new stock",reload.Data.deskLayoutVersion==2&&reload.Items.Count(i=>i.instanceId=="extra-roll")==1);
                }
            }
            catch(Exception error){Debug.LogException(error);Check("Unexpected data check exception",false);}
            bool pass=rows.Count>0&&rows.All(r=>r.passed);Directory.CreateDirectory("docs/reference-room");
            File.WriteAllText("docs/reference-room/data-result.json",JsonUtility.ToJson(new Report{passed=pass,utc=DateTime.UtcNow.ToString("O"),checks=rows.ToArray()},true));
            if(!pass)throw new InvalidOperationException("Reference room ownership data checks failed.");
        }
    }
}

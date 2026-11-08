using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VintageJournal.Editor
{
    public static class PersonalStudioChecks
    {
        static int passed;
        public static bool RunDataChecks()
        {
            passed=0;
            try
            {
                var legacy=JournalDocument.CreateDefault(JournalPaperCatalog.Load());
                Require(legacy.DeepClone().personalStudio==null,"Old journal absence is preserved");
                bool accept=true;var service=new CraftWorkspaceService(legacy,_=>accept,new Vector2(.148f,.210f),DecorationCatalog.Load());
                Require(service.InitializePersonalStudio(),"Personal metadata initializes additively");
                var ids=service.Document.pages.Select(p=>p.pageId).ToArray();
                Require(service.SetPersonalMode(true)&&service.Document.personalStudio.privateJournalMode,"Private mode persists without replacing pages");
                Require(service.Document.pages.Select(p=>p.pageId).SequenceEqual(ids)&&service.Document.pages.Length==24,"Original 24 page IDs remain authoritative");
                Require(service.CustomizeCover("风的记录","和纸",2,3),"Cover title, material, color and charm commit");
                Require(!service.CustomizeCover(new string('x',41),"和纸",0,0),"Oversized cover title is rejected");
                string date="2026-11-07";
                Require(service.SaveDiary(date,"今天是什么颜色？","薄荷色，像一阵轻轻的风。"),"Real private diary text saves");
                Require(service.SaveDiary(date,"今天是什么颜色？","今天也喜欢这阵风。")&&service.Document.personalStudio.diary.Count==1,"Editing a day's diary does not duplicate the date");
                Require(!service.SaveDiary("not-a-date","提示","文字"),"Invalid diary date is rejected");
                string page=service.Document.pages[0].pageId;
                Require(service.AddStudioSection("风和云",page)&&!service.AddStudioSection("不存在的页","missing"),"Sections reference only real pages");
                Require(service.MarkStudioPage(page,"第一阵风",true)&&service.Document.personalStudio.pageNotes.Count==1,"A real page can be dated and favorited");
                var texture=StudioGraphics.Stamp(1,new Color(.3f,.7f,.6f));
                string encoded=Convert.ToBase64String(texture.EncodeToPNG());
                Require(texture.GetPixels32().Any(p=>p.a==0)&&texture.GetPixels32().Any(p=>p.a>200),"Stamp art has visible shapes and transparent margins");
                string asset=service.SaveStudioArt("测试文字模板","text",encoded,256,256,"风",1);UnityEngine.Object.DestroyImmediate(texture);
                Require(!string.IsNullOrEmpty(asset)&&service.Document.personalStudio.artworks.Count==1,"Custom PNG and editable source metadata persist");
                Require(service.SaveStudioArt("坏图","text","AAAA",256,256)==null,"Malformed image cannot enter the journal");
                PersonalStudioAssets.BindDocument(service.Document);
                Require(PersonalStudioAssets.Find(asset)?.IsAvailable==true,"Custom template resolves through independent catalog adapter");
                Require(service.BeginStudioArt(asset),"Printing creates a real held ownership transaction");string instance=service.HeldItem.instanceId;
                Require(service.CancelHeld()&&service.GetItem(instance).owner==CraftOwner.Stock,"Cancel returns the same print to collection");
                Require(service.BeginStudioArt(asset)&&service.HeldItem.instanceId==instance,"Taking a canceled print reuses its identity");
                Require(service.CommitHeld(new CraftPlacement{owner=CraftOwner.JournalPage,containerId="journal",pageId=page,position=new Vector2(.5f,.5f),scale=1}),"Custom print really attaches to the existing page");
                Require(service.Document.pages[0].decorations.Count(d=>d.instanceId==instance)==1,"One print has exactly one page projection");
                Require(service.Undo()&&service.GetItem(instance).owner==CraftOwner.Stock,"Material undo returns print to stock");
                Require(service.Document.personalStudio.privateJournalMode&&service.Document.personalStudio.diary.Count==1&&service.Document.personalStudio.title=="风的记录","Creative undo does not undo private profile or diary");
                Require(service.Redo()&&service.GetItem(instance).owner==CraftOwner.JournalPage,"Material redo restores custom print");
                Require(service.DeleteAttached(instance),"Deleting a custom print keeps a consumed tombstone");
                var loaded=service.Document.DeepClone();
                Require(PersonalStudioData.Validate(loaded,out _)&&loaded.craftWorkspace.items.Single(i=>i.instanceId==instance).owner==CraftOwner.Deleted,"JSON roundtrip retains profile, templates and consumed instance");
                accept=false;string before=JsonUtility.ToJson(service.Document.personalStudio);
                Require(!service.SaveDiary("2026-11-08","明天","文字")&&JsonUtility.ToJson(service.Document.personalStudio)==before,"Rejected save leaves private diary untouched");
                service.Dispose();PersonalStudioAssets.Release();Debug.Log("[PersonalStudioData] "+passed+" passed; 0 failed");return true;
            }
            catch(Exception error){Debug.LogError("[PersonalStudioData] FAIL "+error);return false;}
        }
        static void Require(bool condition,string name){if(!condition)throw new InvalidOperationException(name);passed++;Debug.Log("[PersonalStudioData] PASS "+name);}
        [MenuItem("Vintage Journal/Personal studio/Run data checks")]
        public static void Run(){if(!RunDataChecks())throw new InvalidOperationException("Personal studio data checks failed.");}
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace VintageJournal
{
    /// <summary>Opt-in rendered runtime checks. Explicitly does not simulate native mouse input.</summary>
    public sealed partial class DeskRevisionProbe : MonoBehaviour
    {
        public string output;
        public bool restart;
        public Action<bool> completed;
        [Serializable] public sealed class CheckRow { public string name,detail;public bool passed; }
        [Serializable] public sealed class Report { public bool passed,actualPlayMode,nativeInputValidated,freshProcessRestart;public string unity,gpu,scope,savePath;public int frames;public CheckRow[] checks;public string[] errors; }
        [Serializable] public sealed class Expected { public string book,item,page;public int spread; }
        readonly List<CheckRow> checks=new List<CheckRow>();readonly List<string> errors=new List<string>();
        WorkspaceGameplayController w;NotebookWorkbench work;JournalController j;CommissionDeskController mail;
        float start;bool finished;readonly Stack<IEnumerator> stack=new Stack<IEnumerator>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var args=Environment.GetCommandLineArgs();int p=Array.IndexOf(args,"--desk-revision-probe");
            if(p<0||p+1>=args.Length||!args.Contains("--desk-revision-validation"))return;
            var probe=new GameObject("Desk revision evidence").AddComponent<DeskRevisionProbe>();probe.output=args[p+1];probe.restart=args.Contains("--desk-revision-restart");Application.runInBackground=true;
        }
        void Start(){Directory.CreateDirectory(output);start=Time.realtimeSinceStartup;Application.logMessageReceived+=Log;stack.Push(Execute());}
        void Log(string s,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(s+"\n"+trace);}
        void Update()
        {
            if(finished)return;
            if(Time.realtimeSinceStartup-start>200){Check("Harness timeout",false,"200 seconds");Finish();return;}
            try {while(stack.Count>0){var e=stack.Peek();if(!e.MoveNext()){stack.Pop();continue;}if(e.Current is IEnumerator child){stack.Push(child);continue;}return;}Finish();}
            catch(Exception e){errors.Add(e.ToString());Check("Harness exception",false,e.Message);Finish();}
        }
        void Check(string name,bool ok,string detail="") { checks.Add(new CheckRow{name=name,passed=ok,detail=detail});Debug.Log("[DeskRevision] "+(ok?"PASS ":"FAIL ")+name+" "+detail); }
        IEnumerator Wait(float seconds){float until=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<until)yield return null;}
        IEnumerator Idle(){float until=Time.realtimeSinceStartup+9;while((j.IsBusy||w.physical.IsSettling)&&Time.realtimeSinceStartup<until)yield return null;yield return null;}
        IEnumerator Shot(string name)
        {
            yield return Wait(.25f);
            if(!Application.isEditor){ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));yield return Wait(.4f);}
            else
            {
                var target=new RenderTexture(1920,1080,24);var tex=new Texture2D(1920,1080,TextureFormat.RGB24,false);var old=RenderTexture.active;
                try{var request=new RenderPipeline.StandardRequest{destination=target};RenderPipeline.SubmitRenderRequest(w.lens,request);RenderTexture.active=target;tex.ReadPixels(new Rect(0,0,1920,1080),0,0);tex.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),tex.EncodeToPNG());}
                finally{RenderTexture.active=old;Destroy(tex);target.Release();Destroy(target);}
            }
        }
        bool Reachable(PhysicalInteractable item)
        {
            if(!item||!item.interactionCollider)return false;Physics.SyncTransforms();var bounds=item.interactionCollider.bounds;
            foreach(float y in new[]{0f,.35f,.7f})foreach(float x in new[]{0f,-.35f,.35f})
            {var point=w.lens.WorldToScreenPoint(bounds.center+Vector3.up*bounds.extents.y*y+Vector3.right*bounds.extents.x*x);
                if(point.z>0&&point.x>0&&point.y>0&&point.x<Screen.width&&point.y<Screen.height&&Physics.Raycast(w.lens.ScreenPointToRay(point),out var hit,10)&&hit.collider.GetComponentInParent<PhysicalInteractable>()==item)return true;}
            return false;
        }
        IEnumerator Execute()
        {
            float until=Time.realtimeSinceStartup+25;
            while(!(w=FindFirstObjectByType<WorkspaceGameplayController>())||!w.Ready||!(work=FindFirstObjectByType<NotebookWorkbench>())||!work.Ready||!(mail=FindFirstObjectByType<CommissionDeskController>())||!mail.Ready)
            {if(Time.realtimeSinceStartup>until)throw new TimeoutException("Runtime bindings not ready");yield return null;}
            j=w.journal;
            foreach(var adapter in FindObjectsByType<WorkspaceInputRouter>(FindObjectsSortMode.None))adapter.enabled=false;
            foreach(var adapter in FindObjectsByType<PhysicalMouseInputAdapter>(FindObjectsSortMode.None))adapter.enabled=false;
            foreach(var adapter in FindObjectsByType<ScrapbookInput>(FindObjectsSortMode.None))adapter.enabled=false;
            RuntimeInputGate.Reset();w.desk.Begin();yield return Wait(.3f);
            if(restart)
            {
                var expected=JsonUtility.FromJson<Expected>(File.ReadAllText(Path.Combine(output,"expected.json")));
                Check("Fresh process restores active TN and spread",j.Document.ActiveBookId==expected.book&&j.Document.ActiveSpread==expected.spread);
                Check("Fresh process preserves unique decoration and owner",j.Document.AllPages.Sum(p=>p.decorations.Count(d=>d.instanceId==expected.item))==1&&w.Service.GetItem(expected.item)?.pageId==expected.page);
                if(expected.book==NotebookLibraryData.BinderId)
                    Check("Legacy save creates four separately bound travel journals",work.notebooks.All(i=>i.gameObject.activeInHierarchy)&&j.Document.notebookLibrary.notebooks.Count==4);
                else Check("Fresh process hides central TN from shelf",!work.notebooks.First(i=>i.assetId==expected.book).gameObject.activeInHierarchy);
                Check("Fresh process validates full atomic document",CraftWorkspaceData.Validate(j.Document,out var err),err);
                yield return Shot("restart-tn");yield break;
            }
            var time=FindFirstObjectByType<DreamTimeController>();time.SetNight(false,false,true);
            if(j.Document.ActiveNotebook!=null)work.UseBinder();
            Check("Runtime initialized all four independent TN books",j.Document.notebookLibrary.notebooks.Count==4&&j.Document.notebookLibrary.notebooks.All(b=>b.pages.Length==24));
            Check("Original binder IDs preserved",j.Document.pages.Length==24&&j.Document.pages[0].pageId=="journal-page-001");
            Check("Shared save schema remains v1",j.Document.schemaVersion==1&&j.Document.notebookLibrary.schemaVersion==1);
            yield return Shot("desk-day");
            var rack=GameObject.Find("Wooden washi atelier");
            var activeRenderers=rack.GetComponentsInChildren<Renderer>().Where(r=>r.enabled&&!r.GetComponentInParent<PhysicalInteractable>()).ToArray();
            float rackTop=activeRenderers.Max(r=>r.bounds.max.y);
            Check("Rack lowered from 375 mm to approximately 311 mm",rackTop-rack.transform.position.y<.322f,(rackTop-rack.transform.position.y).ToString("F4"));
            Check("All 15 shaft IDs and three drawers retained",rack.GetComponentsInChildren<WorkspaceShaft>().Length==15&&rack.GetComponentsInChildren<WorkspaceDrawer>().Length==3);
            var all=FindObjectsByType<PhysicalInteractable>(FindObjectsSortMode.None);
            Check("All note homes in cloth organizer",all.Where(i=>i.itemType==PhysicalItemType.Note).Count()==8&&all.Where(i=>i.itemType==PhysicalItemType.Note).All(i=>i.homeSlot.container.displayName=="布艺便签挂袋"));
            Check("All sticker homes in right-hand boxes",all.Count(i=>i.itemType==PhysicalItemType.Sticker)==39&&all.Where(i=>i.itemType==PhysicalItemType.Sticker).All(i=>i.homeSlot.container.displayName.Contains("贴纸盒")));
            foreach(var note in all.Where(i=>i.itemType==PhysicalItemType.Note))Check("Note exposed and ray reachable: "+note.assetId,Reachable(note));
            var designs=w.scrapbook.Catalog.assets.Where(d=>d.assetId.StartsWith("dream_sticker_")).ToArray();
            Check("18 new transparent 1024px sticker designs",designs.Length==18&&designs.All(d=>d.texture.width==1024&&d.alphaMask.Any(a=>a==0)&&d.alphaMask.Any(a=>a>240)));
            var tutorial=AtelierTutorial.Instance;tutorial.Open();
            Check("Tutorial opens with all eight chapters",tutorial.IsOpen&&AtelierTutorial.Chapters.Length==8);yield return Shot("tutorial");tutorial.Close();
            var point=w.lens.WorldToScreenPoint(mail.mailboxCollider.bounds.center);Physics.SyncTransforms();
            Check("Ground mailbox visible and directly clickable",point.x>0&&point.x<Screen.width&&point.y>0&&point.y<Screen.height&&Physics.Raycast(w.lens.ScreenPointToRay(point),out var boxHit,10)&&boxHit.collider==mail.mailboxCollider);
            Check("Mailbox opens commissioner request",mail.TryClick(w.lens.ScreenPointToRay(point))&&mail.PanelOpen&&mail.ActiveLetterId!=null);
            Check("Letter panel leaves wood rack and central pages free",mail.PanelRect.x>=1370&&!mail.ContainsPointer(w.lens.WorldToScreenPoint(rack.transform.position+Vector3.up*.2f)));
            yield return Shot("mail-request");mail.ClosePanel();
            Check("Closed mail UI restores scene access",!mail.PanelOpen);
            // Mouse-independent grab/move/cancel exercises actual swept physical motion and reservations.
            var first=work.notebooks[0];Check("TN front is ray reachable",Reachable(first));var origin=first.transform.position;
            bool grabbed=w.GrabPhysical(first);w.physical.Submit(new PhysicalInteractionIntent(PhysicalIntentKind.Move,first,new Pose(origin+Vector3.back*.14f,first.transform.rotation)));
            yield return Wait(.7f);Check("TN can be grabbed and moved",grabbed&&Vector3.Distance(origin,first.transform.position)>.01f,w.physical.LastResult);
            w.physical.RestoreGrabToSafePose(first);Check("TN cancel restores reserved slot",first.CurrentSlot==first.homeSlot&&Vector3.Distance(origin,first.transform.position)<.001f);
            foreach(var item in work.notebooks)
            {
                Check("Shelf TN reachable: "+item.assetId,Reachable(item));bool used=work.Use(item);yield return Wait(.1f);
                var metric=w.JournalMetres(JournalSide.Left);
                Check("Replace binder with "+item.assetId,used&&j.Document.ActiveBookId==item.assetId&&!item.gameObject.activeInHierarchy,work.Status);
                Check("TN pages retain real metric size: "+item.assetId,Vector2.Distance(metric,item.dimensions.pageSize)<.001f,metric.ToString("F4"));
                Check("Bound-book visuals replace rings",work.boundCover.activeSelf&&work.binderOnly.Count(r=>r.name=="Wire binding")==6&&work.binderOnly.All(r=>!r.enabled));
                work.UseBinder();yield return Wait(.1f);
            }
            // Persist a distinct paper in binder, then verify it survives repeated notebook replacement.
            j.EnterEditing();yield return Idle();j.BeginPaperSelection(JournalSide.Left);j.SelectPaper("page_03");j.ConfirmPaper();j.ExitEditing();yield return Idle();
            Check("Enter TN editing",work.Use(first)&&j.EnterEditing());yield return Idle();
            j.BeginPaperSelection(JournalSide.Left);Check("TN fixed paper rejects binder-style replacement",!j.HasPaperPreview);
            bool turned=j.Turn(1),duplicate=j.Turn(1);yield return Idle();
            Check("TN turns a spread and rejects repeated input",turned&&!duplicate&&j.Document.ActiveSpread==1);
            j.ZoomBy(.52f);yield return Wait(.4f);Check("TN supports controlled zoom",j.cameraRig.Zoom>1.4f);
            j.ResetView();
            var sticker=all.First(i=>i.assetId=="dream_sticker_13");bool taken=w.GrabPhysical(sticker);
            var page=j.Document.GetPage(JournalSide.Left).pageId;
            bool placed=taken&&w.Service.CommitHeld(new CraftPlacement{owner=CraftOwner.JournalPage,containerId="journal",pageId=page,position=new Vector2(.5f,.5f)});
            Check("New travel sticker attaches to TN",placed&&j.Document.GetPage(JournalSide.Left).decorations.Any(d=>d.instanceId==sticker.itemInstanceId));
            w.ClearSelection();j.ZoomBy(.52f);yield return Shot("tn-travel-page");j.ExitEditing();yield return Idle();
            work.UseBinder();Check("Binder paper and page content retained",j.Document.GetPage(JournalSide.Left).paperId=="page_03");
            Check("Inactive TN still owns its decoration exactly once",j.Document.AllPages.Sum(p=>p.decorations.Count(d=>d.instanceId==sticker.itemInstanceId))==1&&w.Service.GetItem(sticker.itemInstanceId).pageId==page);
            Check("Global undo from binder removes TN decoration",w.Undo()&&j.Document.AllPages.All(p=>p.decorations.All(d=>d.instanceId!=sticker.itemInstanceId))&&j.Document.ActiveBookId==NotebookLibraryData.BinderId);
            Check("Global redo restores TN decoration once",w.Redo()&&j.Document.AllPages.Sum(p=>p.decorations.Count(d=>d.instanceId==sticker.itemInstanceId))==1);
            work.Use(first);Check("Returning to TN restores previous spread",j.Document.ActiveSpread==1);
            bool peel=w.Service.BeginPeel(sticker.itemInstanceId);Check("Cannot change workbooks while holding material",peel&&!work.UseBinder());
            Check("Cancel peel restores same TN instance",w.Service.CancelHeld()&&j.Document.GetPage(JournalSide.Left).decorations.Count(d=>d.instanceId==sticker.itemInstanceId)==1);
            Check("Save all notebook pages and ownership",j.Save()&&w.Service.Flush());
            var loaded=new JournalSaveService(j.Catalog,j.SavePath).LoadOrCreate();
            Check("Readback restores TN and page decoration",loaded.ActiveBookId==first.assetId&&loaded.ActiveSpread==1&&loaded.GetPage(JournalSide.Left).decorations.Any(d=>d.instanceId==sticker.itemInstanceId));
            Check("Atomic registry validates after all operations",CraftWorkspaceData.Validate(loaded,out var failure),failure);
            File.WriteAllText(Path.Combine(output,"expected.json"),JsonUtility.ToJson(new Expected{book=first.assetId,spread=1,item=sticker.itemInstanceId,page=page},true));
            var movers=FindObjectsByType<DreamSkyPasser>(FindObjectsSortMode.None);
            Check("Nine or more distant decorative types",movers.Length>=9&&movers.All(m=>m.from.z>=3&&!m.GetComponentInChildren<Collider>(true)));
            time.SetNight(true,false);yield return Wait(time.NightTransitionDuration+.2f);yield return Shot("desk-night");
            Check("Night atmosphere remains operational",time.IsNight&&!time.IsTransitioning);
            if(time.atmosphere){yield return CheckPostalAtelier(time,movers);yield break;}
            var curiosities=GameObject.Find("Distant floating curiosities").GetComponentsInChildren<DreamSkyPasser>();
            foreach(var passer in curiosities)
            {
                passer.enabled=false;
                passer.Sample(passer.duration*.42f-passer.phaseSeconds);
                var view=w.lens.WorldToViewportPoint(passer.visual.position);
                Check("Timed sky path enters seated view: "+passer.name,passer.IsVisible&&view.z>0&&view.x>.04f&&view.x<.96f&&view.y>.2f&&view.y<.96f,view.ToString("F2"));
                passer.Sample(passer.duration+1-passer.phaseSeconds);
            }
            var swing=curiosities.First(m=>m.nightOnly);swing.Sample(swing.duration*.72f-swing.phaseSeconds);yield return Shot("moon-swing");
            foreach(var passer in curiosities)passer.enabled=true;
            time.SetNight(false,false,true);
        }
        void Finish()
        {
            if(finished)return;finished=true;bool pass=checks.Count>0&&checks.All(c=>c.passed)&&errors.Count==0;
            var report=new Report{passed=pass,actualPlayMode=Application.isPlaying,nativeInputValidated=false,freshProcessRestart=restart&&!Application.isEditor,
                unity=Application.unityVersion,gpu=SystemInfo.graphicsDeviceName,frames=Time.frameCount,savePath=j?j.SavePath:null,
                scope=Application.isEditor?"Actual Unity Play runtime API actions and camera raycasts; GPU captures exclude IMGUI. Native mouse input not simulated.":"Standalone GPU frames with UI; shared runtime API actions and raycasts, not native input.",checks=checks.ToArray(),errors=errors.ToArray()};
            File.WriteAllText(Path.Combine(output,restart?"restart.json":"result.json"),JsonUtility.ToJson(report,true));
            Debug.Log("[DeskRevision] Completed "+(pass?"PASS":"FAIL"));Application.logMessageReceived-=Log;
            if(completed!=null)completed(pass);else Application.Quit(pass?0:1);
        }
    }
}

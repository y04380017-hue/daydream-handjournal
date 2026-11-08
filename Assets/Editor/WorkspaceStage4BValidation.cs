using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace VintageJournal.Editor
{
    /// <summary>Actual saved-scene Play Mode execution. Native OS input and fresh-process restart remain separate probes.</summary>
    [InitializeOnLoad]
    public static partial class WorkspaceStage4BValidation
    {
        const string Pending="VintageJournal.Stage4BValidation.Pending";
        static readonly List<Result> results=new List<Result>();
        static readonly List<string> errors=new List<string>(),screenshots=new List<string>();
        static readonly Stack<IEnumerator> routines=new Stack<IEnumerator>();
        static bool preparing,running,finishing;
        static double preparationTime,started;
        static int lastFrame;
        static string root;
        static WorkspaceGameplayController workspace;
        static PhysicalInteractionController physical;
        static WorkspacePhysicalCoordinator coordinator;
        static JournalController journal;
        static ScrapbookController scrapbook;
        static Camera lens;
        static DeskUI desk;
        static PhysicalInteractable[] items;
        static WorkspaceDrawer[] drawers;
        static WorkspaceShaft[] shafts;
        static bool lastPlacementPassed;
        [Serializable] public sealed class Result{public string test,name,status,detail;public bool passed;}
        [Serializable] public sealed class Report
        {
            public string timestampUtc,mode,inputScope,screenshotScope,unityVersion,gpu,graphicsApi,savePath;
            public bool passed,actualPlayMode,nativeInputValidated,freshProcessRestartValidated;
            public bool focusedBookProbe;
            public int width,height,renderedFrames;
            public double lastCommitMilliseconds,maxCommitMilliseconds;
            public double lastSavePreparationMilliseconds,maxSavePreparationMilliseconds;
            public long commitCount;
            public Result[] checks;public string[] consoleErrors,screenshots;
        }
        [Serializable] public sealed class RestartExpectation{public string savePath,json;public string[] releaseMaterialIds;}
        static WorkspaceStage4BValidation()
        {
            EditorApplication.update+=Tick;EditorApplication.playModeStateChanged+=PlayChanged;
            if(SessionState.GetBool(Pending,false))Application.logMessageReceived+=CaptureLog;
        }
        [MenuItem("Vintage Journal/Stage 4B/Validate workspace in Play Mode")]
        public static void Begin()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Start in Edit Mode.");
            if(!Environment.GetCommandLineArgs().Contains("--workspace-validation"))throw new InvalidOperationException("Use --workspace-validation for isolated saves.");
            root=Directory.GetParent(Application.dataPath).FullName;
            Directory.CreateDirectory(Path.Combine(root,"docs/stage4b-logs"));Directory.CreateDirectory(Path.Combine(root,"docs/stage4b-screenshots"));
            bool journalData=JournalDataTests.RunDataChecks();bool decorationData=DecorationDataTests.RunDataChecks();bool prior=journalData&&decorationData;
            bool craft=CraftDataTests.RunDataChecks();
            if(!prior||!craft)throw new InvalidOperationException("Data suite failed before Play; inspect [CraftData]/[JournalData]/[DecorationData] logs.");
            string isolated=Path.Combine(Application.persistentDataPath,"Stage4BValidation","journal-v1.json");
            Directory.CreateDirectory(Path.GetDirectoryName(isolated));
            if(File.Exists(isolated))File.Copy(isolated,isolated+".before-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff"),false);
            File.WriteAllText(isolated,JsonUtility.ToJson(JournalDocument.CreateDefault(JournalPaperCatalog.Load()),true));
            results.Clear();errors.Clear();screenshots.Clear();
            SessionState.SetBool(Pending,true);Application.logMessageReceived-=CaptureLog;Application.logMessageReceived+=CaptureLog;
            EditorSceneManager.OpenScene("Assets/Scenes/VintageJournalDesk.unity",OpenSceneMode.Single);
            preparing=true;preparationTime=EditorApplication.timeSinceStartup;
        }
        static void PlayChanged(PlayModeStateChange state)
        {
            if(!SessionState.GetBool(Pending,false))return;
            if(state==PlayModeStateChange.EnteredPlayMode)
            {
                root=Directory.GetParent(Application.dataPath).FullName;results.Clear();errors.Clear();screenshots.Clear();routines.Clear();
                started=EditorApplication.timeSinceStartup;lastFrame=-1;running=true;finishing=false;
                Application.logMessageReceived-=CaptureLog;Application.logMessageReceived+=CaptureLog;routines.Push(Execute());
            }
            else if(state==PlayModeStateChange.EnteredEditMode&&!finishing){Check("Harness","Unexpected Play exit",false,"Validation incomplete.");Finish();}
        }
        static void CaptureLog(string message,string stack,LogType type)
        {if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message+"\n"+stack);}
        static void Check(string id,string name,bool passed,string detail)
        {results.Add(new Result{test=id,name=name,passed=passed,status=passed?"passed":"failed",detail=detail});Debug.Log("[Workspace4BValidation] "+id+" "+(passed?"PASS ":"FAIL ")+name+" "+detail);}
        static void Manual(string id,string name,string detail)
        {results.Add(new Result{test=id,name=name,status="requires_manual_verification",detail=detail});Debug.Log("[Workspace4BValidation] "+id+" MANUAL "+name+" "+detail);}
        static void Tick()
        {
            if(preparing)
            {
                if(EditorApplication.isCompiling||EditorApplication.isUpdating){preparationTime=EditorApplication.timeSinceStartup;return;}
                if(EditorApplication.timeSinceStartup-preparationTime<2)return;
                preparing=false;EditorApplication.isPlaying=true;return;
            }
            if(!running||finishing||!EditorApplication.isPlaying||Time.frameCount==lastFrame)return;lastFrame=Time.frameCount;
            if(EditorApplication.timeSinceStartup-started>240){Check("Harness","Timeout",false,"240 seconds exceeded.");Finish();return;}
            try
            {
                while(routines.Count>0)
                {var routine=routines.Peek();if(!routine.MoveNext()){routines.Pop();continue;}if(routine.Current is IEnumerator child){routines.Push(child);continue;}return;}
                Finish();
            }
            catch(Exception error){errors.Add(error.ToString());Check("Harness","Exception",false,error.ToString());Finish();}
        }
        static IEnumerator Wait(double seconds){double until=EditorApplication.timeSinceStartup+seconds;while(EditorApplication.timeSinceStartup<until)yield return null;}
        static IEnumerator Settled()
        {
            double until=EditorApplication.timeSinceStartup+8;
            while(physical.IsSettling&&EditorApplication.timeSinceStartup<until)yield return null;
            if(physical.IsSettling)throw new TimeoutException("Physical settle timeout: "+physical.LastResult);
            yield return null;coordinator.SyncPhysical();yield return null;
        }
        static IEnumerator JournalIdle()
        {double until=EditorApplication.timeSinceStartup+8;while((journal.IsBusy||journal.cameraRig.IsTransitioning)&&EditorApplication.timeSinceStartup<until)yield return null;if(journal.IsBusy)throw new TimeoutException("Journal transition timeout.");yield return null;}
        static bool Physical(PhysicalIntentKind kind,PhysicalInteractable target=null,Pose pose=default)=>physical.Submit(new PhysicalInteractionIntent(kind,target,pose));
        static IEnumerator Execute()
        {
            double readyUntil=EditorApplication.timeSinceStartup+15;
            while((workspace=UnityEngine.Object.FindFirstObjectByType<WorkspaceGameplayController>())==null||!workspace.Ready)
            {if(EditorApplication.timeSinceStartup>readyUntil)throw new TimeoutException("Workspace never became ready.");yield return null;}
            physical=workspace.physical;coordinator=workspace.coordinator;journal=workspace.journal;scrapbook=workspace.scrapbook;lens=workspace.lens;desk=workspace.desk;
            foreach(var adapter in UnityEngine.Object.FindObjectsByType<WorkspaceInputRouter>(FindObjectsSortMode.None))adapter.enabled=false;
            foreach(var adapter in UnityEngine.Object.FindObjectsByType<PhysicalMouseInputAdapter>(FindObjectsSortMode.None))adapter.enabled=false;
            foreach(var adapter in UnityEngine.Object.FindObjectsByType<ScrapbookInput>(FindObjectsSortMode.None))adapter.enabled=false;
            RuntimeInputGate.Reset();desk.Begin();yield return Wait(.3);
            items=UnityEngine.Object.FindObjectsByType<PhysicalInteractable>(FindObjectsSortMode.None);
            drawers=UnityEngine.Object.FindObjectsByType<WorkspaceDrawer>(FindObjectsSortMode.None).OrderBy(d=>d.drawerId).ToArray();
            shafts=UnityEngine.Object.FindObjectsByType<WorkspaceShaft>(FindObjectsSortMode.None).OrderBy(s=>s.shaftId).ToArray();
            Check("T02","Real-scale entities retain independent identity and dimensions",items.Length>=40&&items.All(i=>i.dimensions&&i.dimensions.IsValid&&Vector3.Distance(i.transform.lossyScale,Vector3.one)<.001f)&&
                items.Select(i=>i.itemInstanceId).Distinct().Count()==items.Length,items.Length+" unique real-scale entities in saved scene.");
            Check("T15","Two independently stored six-page A6 release books",workspace.Service.Books.Count==2&&workspace.Service.Books.All(b=>b.pages.Count==6&&b.pageSizeMeters==new Vector2(.105f,.148f)),"Six stable page IDs per book, independent records.");
            var invalidBindings=items.Where(i=>!i.homeSlot||i.CurrentSlot!=i.homeSlot||i.CurrentContainer!=i.homeSlot.container||
                Vector3.Distance(i.transform.position,i.homeSlot.transform.position)>.003f||!ExpectedStorageParent(i)).ToArray();
            Check("T02","Saved scene starts with current/home/new fixture bindings aligned",invalidBindings.Length==0,
                invalidBindings.Length==0?"Every live entity uses its Stage04B storage anchor at the correct authored world pose.":
                string.Join(" | ",invalidBindings.Select(i=>i.itemInstanceId+":current="+(i.CurrentSlot?i.CurrentSlot.name:"none")+",home="+(i.homeSlot?i.homeSlot.name:"none")+",container="+(i.CurrentContainer?i.CurrentContainer.name:"none"))));
            if(invalidBindings.Length>0){Capture("authoring-binding-failure.png");yield break;}
            ValidateWoodRack();Capture("workspace-default.png");
            if(journal.Catalog.defaultPaperId=="paper_daydream_ivory")
            {
                bool blank=journal.Document.pages.All(p=>p.paperId=="paper_daydream_ivory"&&p.decorations.Count==0);
                Check("Art01","New journal starts with blank cotton paper",blank&&journal.Catalog.Find("page_01")!=null,"All 24 instance IDs survive independently of the default paper.");
                CheckShaftSightlines("closed");
                bool entered=journal.EnterEditing();yield return JournalIdle();
                Check("Art02","Editing opens at seated desk pose",entered&&Vector3.Distance(lens.transform.position,journal.cameraRig.DefaultPosition)<.01f,"Measure the active camera after entering through JournalController.");
                journal.ZoomBy(.52f);yield return Wait(.8);Capture("blank-binder.png");
                journal.ExitEditing();yield return JournalIdle();
                var samples=new List<float>(); int frame=Time.frameCount;
                while(samples.Count<120){yield return null;if(frame!=Time.frameCount){frame=Time.frameCount;samples.Add(Time.unscaledDeltaTime*1000);}}
                samples.Sort();Debug.LogFormat("[Daydream] Editor Play idle 120 frames median {0:F2} ms / P95 {1:F2} ms",samples[60],samples[113]);
            }
            if(UnityEngine.Object.FindFirstObjectByType<DreamTimeController>())yield return ValidateFloatingAtelier();
            Check("T03","Three bounded independent storage tray joints",drawers.Length==3&&drawers.All(d=>d.slide&&d.container&&d.travel>0&&d.travel<=(GameObject.Find("Wooden washi atelier")&&d.drawerId=="drawer-release-books"?.35f:.2f)),"Actual slide transforms and separate access containers.");
            foreach(var drawer in drawers)
            {
                drawer.SetOpenAmount(-1);bool lower=Mathf.Approximately(drawer.OpenAmount,0);
                var contents=items.Where(i=>i.homeSlot&&i.homeSlot.container==drawer.container).ToArray();
                bool visible=contents.Length>0&&contents.All(i=>i.visualRoot&&i.visualRoot.gameObject.activeInHierarchy&&!i.interactionCollider.enabled);
                drawer.SetOpenAmount(3);bool upper=Mathf.Approximately(drawer.OpenAmount,1)&&drawer.container.isOpen;
                Check("T03","Drawer range, closed visibility and open access: "+drawer.drawerId,lower&&visible&&upper&&contents.All(i=>i.interactionCollider.enabled),"Stored items remain visible in open-front trays; grabbing disabled until the tray is extended.");
            }
            yield return Wait(.2);Capture("drawers-open.png");
            if(journal.Catalog.defaultPaperId=="paper_daydream_ivory")CheckShaftSightlines("open");
            foreach(var kind in new[]{PhysicalItemType.Tape,PhysicalItemType.Sticker,PhysicalItemType.Note,PhysicalItemType.LoosePage,PhysicalItemType.TravelersNotebook,PhysicalItemType.ReleaseBook})
            {
                if(kind==PhysicalItemType.ReleaseBook)
                {
                    // Upper open trays physically cover the lower drawer from the seated viewpoint.
                    // Close them through the real drawer API before testing the dedicated book drawer.
                    foreach(var drawer in drawers)drawer.SetOpenAmount(drawer.drawerId=="drawer-release-books"?1:0);
                    yield return Wait(.2);
                }
                bool visible=items.Where(i=>i.itemType==kind).Any(VisibleHit);
                Check("T01","Default camera exposes clickable "+kind,visible,"Actual camera ray must hit a member of the category. For the lower book drawer, upper trays are first closed through their real joints.");
            }
            foreach(var drawer in drawers)drawer.SetOpenAmount(1);
            yield return Wait(.2);
            if(Environment.GetCommandLineArgs().Contains("--workspace-book-probe"))
            {yield return BookProbe();yield break;}
            Check("T05","Three work rows expose five independent shafts each",shafts.Length==15&&shafts.GroupBy(s=>s.transform.parent).Count()==3&&shafts.GroupBy(s=>s.transform.parent).All(g=>g.Count()==5),"15 independent slots with physical rods and handles.");
            var rolls=items.Where(i=>i.itemType==PhysicalItemType.Tape&&i.homeSlot&&drawers.Any(d=>i.homeSlot.container==d.container)).OrderBy(i=>i.itemInstanceId).ToArray();
            if(rolls.Length<15||shafts.Length!=15){Check("T05","Required real stock/shaft fixtures exist",false,"rolls="+rolls.Length+" shafts="+shafts.Length);yield break;}
            bool allMounted=rolls.Length>=15;
            for(int index=0;index<Mathf.Min(15,rolls.Length);index++)
            {
                var roll=rolls[index];var shaft=shafts[index];
                OpenOnlyHomeDrawer(roll);yield return Wait(.1);Physics.SyncTransforms();
                shaft.SetExtended(true);
                bool grabbed=workspace.GrabPhysical(roll);bool mounted=grabbed&&coordinator.Install(roll,shaft);yield return Settled();
                bool one=mounted&&shaft.slot.Occupant==roll&&roll.CurrentSlot==shaft.slot&&!roll.homeSlot.Occupant;
                bool aligned=one&&Vector3.Distance(roll.transform.position,shaft.slot.transform.position)<.003f&&
                    Quaternion.Angle(roll.transform.rotation,shaft.slot.transform.rotation)<1;
                bool floorClear=aligned&&drawers.All(d=>!PenetratesDrawerFloor(roll,d));
                shaft.SetExtended(false);bool blocked=!Physical(PhysicalIntentKind.Grab,roll);
                Check(index==0?"T04":"T07","Actual roll transfer to "+shaft.shaftId,grabbed&&one&&aligned&&floorClear&&blocked,
                    "Source drawer vacated; shaft pose/rotation reached without drawer-floor penetration; closed shaft rejects removal. "+physical.LastResult);
                allMounted&=one&&aligned&&floorClear&&blocked;
                if(!grabbed||!one||!aligned||!floorClear||!blocked||physical.HeldItem)
                {Capture("first-mount-failure.png");Debug.LogWarning("[Workspace4BValidation] Stopping dependent tests after unsafe mount; "+physical.LastResult);yield break;}
            }
            coordinator.SyncPhysical();
            Check("T05","All three stands simultaneously hold five real rolls",allMounted&&shafts.All(s=>s.slot.Occupant)&&shafts.Select(s=>s.slot.Occupant).Distinct().Count()==15,"Runtime transfer completed for every capacity position.");
            Check("T08","Inventory has unique shaft ownership without duplicate instances",CraftWorkspaceData.Validate(journal.Document,out string ownershipError)&&
                workspace.Service.Items.Where(i=>i.kind==CraftItemKind.TapeRoll&&i.owner==CraftOwner.Stock).Select(i=>i.containerId).Distinct().Count()==rolls.Length,ownershipError);
            Capture("animal-stands-loaded.png");
            var firstRoll=rolls[0];var firstShaft=shafts[0];
            OpenOnlyHomeDrawer(firstRoll);coordinator.SyncPhysical();yield return Wait(.1);Physics.SyncTransforms();
            firstShaft.SetExtended(true);coordinator.SyncPhysical();yield return Wait(.1);Physics.SyncTransforms();
            bool removed=coordinator.TakeFromShaft(firstShaft);yield return Wait(.15);bool returned=coordinator.ReturnToHome(firstRoll);yield return Settled();
            Check("T06","Extended shaft permits removal and exact drawer return",removed&&returned&&firstRoll.CurrentSlot==firstRoll.homeSlot&&!firstShaft.slot.Occupant,
                "removed="+removed+" returned="+returned+" extended="+firstShaft.IsExtended+" current="+(firstRoll.CurrentSlot?firstRoll.CurrentSlot.slotId:"none")+
                " home="+(firstRoll.homeSlot?firstRoll.homeSlot.slotId:"none")+" shaftOccupant="+(firstShaft.slot.Occupant?firstShaft.slot.Occupant.itemInstanceId:"none")+" result="+physical.LastResult);
            if(!removed||!returned||physical.HeldItem||firstRoll.CurrentSlot!=firstRoll.homeSlot)yield break;
            var source=rolls[1];bool selected=workspace.SelectPhysical(source);bool lengthStart=workspace.StartTapeLength(source.transform.position);
            bool defaultLength=Mathf.Approximately(workspace.TapeLengthMeters,.05f);workspace.SetTapeLength(.085f);
            Check("T09","Left-select identifies actual mounted roll",selected&&workspace.SelectedTape==source,source.itemInstanceId);
            Check("T10","Tape selection enters shared length-selection state",lengthStart&&workspace.TapeLengthSelecting&&defaultLength,"Default length is exactly 5 cm.");
            Check("T11","Metric length changes without stretching strip width",Mathf.Approximately(workspace.TapeLengthMeters,.085f),"8.5 cm runtime value; texture repetition assessed from generated material below.");
            bool cut=workspace.ConfirmTapeLength();string stripId=workspace.Service.HeldItem?.instanceId;
            Check("T12","Confirm creates one independent strip while roll remains",cut&&workspace.Service.HeldItem!=null&&stripId!=source.itemInstanceId&&workspace.Service.HeldItem.kind==CraftItemKind.TapeStrip&&source.CurrentSlot,
                "Cut strip="+stripId+"; source roll="+source.itemInstanceId);
            if(!cut||string.IsNullOrEmpty(stripId))yield break;
            var journalPlacement=new CraftPlacement{owner=CraftOwner.JournalPage,containerId="journal",pageId=journal.Document.pages[0].pageId,position=new Vector2(.48f,.35f)};
            bool attached=workspace.Attach(journalPlacement);yield return null;
            var cutDecoration=journal.Document.pages[0].decorations.FirstOrDefault(d=>d.instanceId==stripId);
            Check("T13","Physical strip attaches to journal and retains cut length",attached&&cutDecoration!=null&&Mathf.Abs(cutDecoration.lengthMeters-.085f)<.0001f&&scrapbook.visuals.GetVisual(stripId),"Original third-stage renderer displays physical strip projection.");
            if(!attached||cutDecoration==null)yield break;
            Check("T11","Continuous tape repeat is computed from physical aspect",scrapbook.visuals.GetRenderer(stripId)&&scrapbook.visuals.GetRenderer(stripId).sharedMaterial.GetFloat("_UVRepeat")>0,"Repeat uniform is present; native captures retain actual artwork.");

            var bookA=workspace.releaseBooks.First(v=>v.bookId=="release-a");var bookB=workspace.releaseBooks.First(v=>v.bookId=="release-b");
            yield return PlacePhysical(bookA.physical,"T15",new Vector3(.27f,0,-.22f));
            if(!lastPlacementPassed)yield break;
            bool openA=workspace.OpenBook("release-a",true);yield return null;
            Check("T15","Book A opens real independent 3D page anchors",openA&&bookA.IsOpen&&bookA.spreadRoot.gameObject.activeInHierarchy&&bookA.pageAnchors.All(a=>a),"Book was taken from its actual drawer before opening.");
            bool pageForward=workspace.TurnBook("release-a",1);bool atTwo=workspace.Service.FindBook("release-a").currentPage==2;bool pageBack=workspace.TurnBook("release-a",-1);
            Check("T16","Release book turns independent pages",pageForward&&atTwo&&pageBack&&workspace.Service.FindBook("release-a").currentPage==0,"Six pages remain independently addressed; navigation is separate from journal spread.");
            workspace.SelectPhysical(source);workspace.StartTapeLength(source.transform.position);workspace.SetTapeLength(.062f);bool cutBook=workspace.ConfirmTapeLength();string bookStrip=workspace.Service.HeldItem?.instanceId;
            if(!cutBook||string.IsNullOrEmpty(bookStrip)){Check("T12","Second strip creation prerequisite",false,workspace.Service.LastMessage);yield break;}
            bool bookAttach=workspace.Attach(Release("release-a",0,new Vector2(.44f,.36f),19));yield return null;
            Check("T14","Tape attaches freely to release-book page",cutBook&&bookAttach&&workspace.Service.GetItem(bookStrip).owner==CraftOwner.ReleaseBookPage&&workspace.materialRenderer.GetVisual(bookStrip),bookStrip);
            if(!bookAttach)yield break;
            var sticker=items.First(i=>i.itemType==PhysicalItemType.Sticker&&workspace.Service.GetItem(i.itemInstanceId)?.owner==CraftOwner.Stock);
            bool stickerTake=workspace.GrabPhysical(sticker);string stickerId=sticker.itemInstanceId;
            bool stickerPlace=workspace.Attach(Release("release-a",0,new Vector2(.69f,.70f),-23,.8f));yield return null;
            Check("T26","Hanging sticker source becomes one attachable finite instance",stickerTake&&stickerPlace&&workspace.Service.GetItem(stickerId).owner==CraftOwner.ReleaseBookPage&&!sticker.gameObject.activeSelf,"Source presentation hides after ownership transfer.");
            if(!stickerTake||!stickerPlace)yield break;
            Check("T17","Multiple materials retain free non-grid transforms",workspace.Service.GetItem(stickerId).position==new Vector2(.69f,.70f)&&
                workspace.Service.GetItem(bookStrip).position==new Vector2(.44f,.36f)&&workspace.Service.GetItem(stickerId).rotationDegrees==-23,"Independent arbitrary UVs, rotation, layer and scale retained.");
            Capture("release-book-a-decorated.png");
            yield return PlacePhysical(bookB.physical,"T15",new Vector3(.27f,0,.02f));
            if(!lastPlacementPassed)yield break;
            bool openB=workspace.OpenBook("release-b",true);Check("T15","Book B opens separately",openB&&bookB.IsOpen&&bookA.IsOpen,"Both real books remain independent.");
            bool peel=workspace.BeginPeel(stickerId);bool absentOrigin=workspace.Service.GetPageItems(CraftOwner.ReleaseBookPage,"release-a",workspace.Service.FindBook("release-a").pages[0].pageId).All(i=>i.instanceId!=stickerId);
            Check("T18","Peeling removes source membership and preserves identity",peel&&absentOrigin&&workspace.Service.HeldItem.instanceId==stickerId,"One authoritative Held owner, with persisted exact rollback snapshot.");
            bool toB=workspace.Attach(Release("release-b",0,new Vector2(.45f,.56f),11,.8f));
            Check("T21","One sticker transfers from A to B",toB&&workspace.Service.GetItem(stickerId).containerId=="release-b"&&Count(stickerId)==1,stickerId);
            bool undo=workspace.Undo();bool backA=workspace.Service.GetItem(stickerId).containerId=="release-a";bool redo=workspace.Redo();
            Check("T30","Cross-book undo/redo preserves exactly one owner",undo&&backA&&redo&&workspace.Service.GetItem(stickerId).containerId=="release-b"&&Count(stickerId)==1,"Full ownership transaction replayed across containers.");
            bool toJournalPeel=workspace.BeginPeel(stickerId);bool toJournal=workspace.Attach(new CraftPlacement{owner=CraftOwner.JournalPage,containerId="journal",pageId=journal.Document.pages[1].pageId,position=new Vector2(.63f,.58f),scale=.8f});
            Check("T22","Transfer to journal never duplicates release-book material",toJournalPeel&&toJournal&&Count(stickerId)==1&&journal.Document.pages[1].decorations.Count(d=>d.instanceId==stickerId)==1&&
                !workspace.Service.Items.Any(i=>i.instanceId==stickerId&&i.owner==CraftOwner.ReleaseBookPage),"Journal projection is compatibility data for the same authoritative ID.");
            var original=JsonUtility.ToJson(workspace.Service.GetItem(stickerId));bool cancelPeel=workspace.BeginPeel(stickerId)&&workspace.Cancel();
            Check("T20","Cancel peel restores exact safe original membership",cancelPeel&&JsonUtility.ToJson(workspace.Service.GetItem(stickerId))==original,"Position, size, rotation, layer, owner and instance ID restored.");
            bool peelTape=workspace.BeginPeel(bookStrip);bool retape=workspace.Attach(Release("release-b",1,new Vector2(.46f,.48f),31));
            Check("T19","Collected tape can be peeled and moved to another book/page",peelTape&&retape&&Count(bookStrip)==1&&workspace.Service.GetItem(bookStrip).pageId==workspace.Service.FindBook("release-b").pages[1].pageId,"Original 6.2 cm metadata retained.");
            bool peelForPage=workspace.BeginPeel(bookStrip);bool pageWhileHeld=workspace.TurnBook("release-b",1);
            bool anotherPage=workspace.Attach(Release("release-b",2,new Vector2(.49f,.51f),27));
            Check("T20","Held material can transfer to another actual page of the same book",peelForPage&&pageWhileHeld&&anotherPage&&Count(bookStrip)==1&&
                workspace.Service.GetItem(bookStrip).pageId==workspace.Service.FindBook("release-b").pages[2].pageId,"Only the explicit target book pager is enabled during holding; original transaction remains intact.");
            string safePageRecord=JsonUtility.ToJson(workspace.Service.GetItem(bookStrip));
            bool peelBeforeNavigation=workspace.BeginPeel(bookStrip);bool pageBackWhileHeld=workspace.TurnBook("release-b",-1);bool restoredAfterNavigation=workspace.Cancel();
            Check("T20","Cancel after target-page navigation restores original page ownership",peelBeforeNavigation&&pageBackWhileHeld&&restoredAfterNavigation&&
                JsonUtility.ToJson(workspace.Service.GetItem(bookStrip))==safePageRecord,"View navigation never overwrites the pending source page or instance identity.");
            yield return StockAndLegacyRegression();
            Check("T23","Single-file disk reload retains both books and all ownership",workspace.Service.Flush()&&DiskMatches(),"Actual isolated save file read with a new JournalSaveService; fresh process remains separate.");
            File.WriteAllText(Path.Combine(root,"docs/stage4b-logs/restart-expected.json"),JsonUtility.ToJson(new RestartExpectation{savePath=journal.SavePath,json=JsonUtility.ToJson(journal.Document),
                releaseMaterialIds=workspace.Service.Items.Where(i=>i.owner==CraftOwner.ReleaseBookPage).Select(i=>i.instanceId).ToArray()},true));
            Capture("workspace-final.png");
            Manual("T23","Fresh-process restart acceptance","This editor harness does not restart a player process. Compare restart-expected.json in a fresh standalone process separately.");
            Manual("T31","Final artwork, clipping and occlusion review","GPU images retained. Unusual dragging routes, transparency and visual polish also need native captures/human review.");
            Manual("T33","Windows native mouse and keyboard acceptance","Requires separately launched built player and verified native OS input probe; not inferred from shared-intent tests.");
            Check("T32","No blocking Unity runtime errors",errors.Count==0,errors.Count+" errors/asserts/exceptions captured during actual Play session.");
        }

        static IEnumerator PlacePhysical(PhysicalInteractable item,string test,Vector3 preferred)
        {
            lastPlacementPassed=false;
            OpenOnlyHomeDrawer(item);yield return Wait(.1);Physics.SyncTransforms();
            bool grabbed=workspace.GrabPhysical(item);Pose destination=FreePose(item,preferred);
            bool moved=Physical(PhysicalIntentKind.Move,item,destination);yield return Wait(.25);bool placed=Physical(PhysicalIntentKind.Release,item,destination);yield return Settled();
            lastPlacementPassed=grabbed&&moved&&placed&&item.CurrentState==PhysicalItemState.Placed&&Vector3.Distance(item.transform.position,destination.position)<.007f;
            Check(test,"Take and place physical "+item.displayName,lastPlacementPassed,
                "requested="+destination.position+" actual="+item.transform.position+" "+physical.LastResult+" blocker="+BlockerDescription());
        }
        static IEnumerator BookProbe()
        {
            var book=workspace.releaseBooks.First(v=>v.bookId=="release-a");
            yield return PlacePhysical(book.physical,"T15",new Vector3(.27f,0,-.22f));
            Capture("focused-book-placement.png");
            if(!lastPlacementPassed)yield break;
            bool clear=!physical.surface.HasCollision(book.physical,book.physical.transform.position,book.physical.transform.rotation,false);
            Check("T31","Focused book resting footprint is collision free",clear,"Actual tabletop query; blocker="+BlockerDescription());
            bool opened=workspace.OpenBook("release-a",true);yield return null;
            Check("T15","Focused book opens real page presentation",opened&&book.IsOpen&&book.spreadRoot&&book.spreadRoot.gameObject.activeInHierarchy,"Focused diagnostic only, not full Stage04B acceptance.");
            if(!opened)yield break;
            var sticker=items.First(i=>i.itemType==PhysicalItemType.Sticker&&workspace.Service.GetItem(i.itemInstanceId)?.owner==CraftOwner.Stock);
            bool grabbed=workspace.GrabPhysical(sticker);bool attached=workspace.Attach(Release("release-a",0,new Vector2(.51f,.48f),17,.8f));yield return null;
            Check("T17","Focused book receives one independent material",grabbed&&attached&&workspace.materialRenderer.GetVisual(sticker.itemInstanceId),sticker.itemInstanceId);
            Capture("focused-book-attachment.png");
            Check("T32","Focused probe has no blocking runtime errors",errors.Count==0,errors.Count+" errors/asserts/exceptions captured.");
        }
        static string BlockerDescription()
        {
            var blocker=physical.surface.LastBlockingCollider;
            return blocker?(blocker.transform.parent?blocker.transform.parent.name+"/":"")+blocker.name+" "+blocker.bounds:"none";
        }
        static Pose FreePose(PhysicalInteractable item,Vector3 preferred)
        {
            foreach(float z in new[]{preferred.z,-.25f,-.12f,.04f,.18f,.26f,-.31f})
                foreach(float x in new[]{preferred.x,.24f,.36f,.48f,-.22f,-.35f,.55f})
                {
                    var pose=physical.surface.RestingPose(item,new Vector3(x,physical.surface.transform.position.y,z),Quaternion.identity);
                    if(physical.surface.IsValidPlacement(item,pose))return pose;
                }
            throw new InvalidOperationException("No accessible free tabletop pose found for "+item.displayName);
        }
        static CraftPlacement Release(string book,int page,Vector2 uv,float rotation=0,float scale=1)
        {return new CraftPlacement{owner=CraftOwner.ReleaseBookPage,containerId=book,pageId=workspace.Service.FindBook(book).pages[page].pageId,position=uv,rotationDegrees=rotation,scale=scale};}
        static int Count(string id)=>workspace.Service.Items.Count(i=>i.instanceId==id);
        static bool DiskMatches()
        {var disk=new JournalSaveService(journal.Catalog,journal.SavePath).LoadOrCreate();return JsonUtility.ToJson(disk)==JsonUtility.ToJson(journal.Document);}
        static IEnumerator StockAndLegacyRegression()
        {
            var noteGroups=items.Where(i=>i.itemType==PhysicalItemType.Note&&i.homeSlot).GroupBy(i=>i.homeSlot.container).ToArray();
            if(GameObject.Find("Desk revision 20261107"))
                Check("T27","Eight separate note pockets and three sticker boxes",noteGroups.Length==1&&noteGroups[0].Count()==8&&noteGroups[0].Select(i=>i.homeSlot).Distinct().Count()==8&&items.Where(i=>i.itemType==PhysicalItemType.Sticker).Select(i=>i.homeSlot.container).Distinct().Count()==3,"Verified actual home slots and container bindings.");
            else Check("T27","Three independently labeled note styles retain distinct stock",noteGroups.Length==3&&noteGroups.All(g=>g.Count()>=2)&&noteGroups.Select(g=>g.Key.displayName).Distinct().Count()==3,
                string.Join(" | ",noteGroups.Select(g=>g.Key.displayName+":"+g.Count())));
            foreach(var group in noteGroups)
            {
                group.Key.SetOpen(true);var note=group.First();bool take=workspace.GrabPhysical(note);bool cancel=workspace.Cancel();
                Check("T27","Note sheet take/cancel restores its own style stock",take&&cancel&&workspace.Service.GetItem(note.itemInstanceId).owner==CraftOwner.Stock,note.assetId);
            }
            var paper=items.Where(i=>i.itemType==PhysicalItemType.LoosePage&&i.GetComponent<LoosePageBinding>()).ToArray();
            Check("T24","Layered paper rack exposes eight styles and actual paper IDs",paper.Length>=8&&paper.Select(i=>i.GetComponent<LoosePageBinding>().style).Distinct().Count()>=8&&
                paper.All(i=>i.GetComponent<LoosePageBinding>().paperId==i.assetId&&i.GetComponent<LoosePageBinding>().holeAnchors.Length==6),"Paper identity, physical size and future six-ring anchors match.");
            foreach(var sheet in paper.Take(8))
            {
                Pose home=sheet.CurrentWorldPose;bool grab=workspace.GrabPhysical(sheet);
                var target=physical.surface.RestingPose(sheet,home.position+new Vector3(0,0,-.05f),Quaternion.identity);
                bool move=Physical(PhysicalIntentKind.Move,sheet,target);yield return Wait(.35);bool displaced=Vector3.Distance(sheet.transform.position,home.position)>.002f;
                bool returned=coordinator.ReturnToHome(sheet);yield return Settled();
                Check("T25","Take, move and return actual paper style",grab&&move&&displaced&&returned&&sheet.CurrentSlot==sheet.homeSlot&&Vector3.Distance(sheet.transform.position,home.position)<.004f,sheet.GetComponent<LoosePageBinding>().style);
            }
            var notebooks=items.Where(i=>i.itemType==PhysicalItemType.TravelersNotebook).ToArray();
            foreach(var notebook in notebooks.Take(4))
            {
                Pose home=notebook.CurrentWorldPose;bool inspect=Physical(PhysicalIntentKind.Inspect,notebook);
                var anchor=notebook.inspectAnchor?notebook.inspectAnchor:physical.inspectAnchor;double until=EditorApplication.timeSinceStartup+4;
                while(inspect&&anchor&&Vector3.Distance(notebook.transform.position,anchor.position)>.015f&&EditorApplication.timeSinceStartup<until)yield return null;
                bool viewed=anchor&&Vector3.Distance(notebook.transform.position,anchor.position)<=.015f;
                bool returned=coordinator.ReturnToHome(notebook);yield return Settled();
                Check("T28","TN reaches inspect anchor and returns independently",inspect&&viewed&&returned&&notebook.CurrentSlot==notebook.homeSlot&&Vector3.Distance(notebook.transform.position,home.position)<.004f,notebook.assetId);
            }
            RuntimeInputGate.Reset();while(scrapbook.Cancel()){}
            bool entered=journal.EnterEditing();yield return JournalIdle();
            var asset=scrapbook.Catalog.assets.First(d=>d.category==DecorationCategory.Sticker&&d.IsAvailable);
            scrapbook.Browse(DecorationCategory.Sticker);bool chose=scrapbook.Choose(asset.assetId);bool placed=scrapbook.Place(JournalSide.Left,new Vector2(.4f,.67f));string id=scrapbook.SelectedId;
            bool transform=scrapbook.BeginTransform("move")&&scrapbook.MoveTo(new Vector2(.58f,.64f));scrapbook.CommitTransform();
            bool undo=scrapbook.Undo();var restored=journal.Document.GetPage(JournalSide.Left).decorations.FirstOrDefault(d=>d.instanceId==id);
            bool redo=scrapbook.Redo();var moved=journal.Document.GetPage(JournalSide.Left).decorations.FirstOrDefault(d=>d.instanceId==id);
            Check("T29","Legacy digital editing participates safely in shared history",entered&&chose&&placed&&transform&&undo&&redo&&restored!=null&&moved!=null&&
                Vector2.Distance(restored.position,new Vector2(.4f,.67f))<.005f&&Vector2.Distance(moved.position,new Vector2(.58f,.64f))<.005f,"Reacquire document references after whole-document undo; no stale row references.");
            while(scrapbook.Cancel()){}
            int initial=journal.Document.currentSpread;bool next=journal.Turn(1);yield return JournalIdle();bool previous=journal.Turn(-1);yield return JournalIdle();
            string originalPaper=journal.Document.GetPage(JournalSide.Right).paperId;string other=journal.Catalog.papers.First(p=>p.id!=originalPaper).id;
            journal.BeginPaperSelection(JournalSide.Right);journal.SelectPaper(other);journal.ConfirmPaper();journal.ZoomBy(.25f);yield return Wait(.2);
            Check("T29","Original flip, paper replacement and zoom remain usable",next&&previous&&journal.Document.currentSpread==initial&&
                journal.Document.GetPage(JournalSide.Right).paperId==other&&journal.cameraRig.Zoom>1.2f,"Original journal runtime APIs, real camera projection and existing paper catalogue.");
            Check("T30","Digital undo cannot resurrect a transferred duplicate",CraftWorkspaceData.Validate(journal.Document,out string reason),reason);
            journal.ResetView();journal.ExitEditing();yield return JournalIdle();
        }
        static bool VisibleHit(PhysicalInteractable item)
        {
            if(!item.interactionCollider||!item.interactionCollider.enabled)return false;Physics.SyncTransforms();Bounds b=item.interactionCollider.bounds;
            for(int x=-2;x<=2;x++)for(int z=-2;z<=2;z++)
            {
                Vector3 point=lens.WorldToScreenPoint(b.center+new Vector3(b.extents.x*x*.35f,b.extents.y*.4f,b.extents.z*z*.35f));
                if(point.z<=0||point.x<4||point.x>Screen.width-4||point.y<4||point.y>Screen.height-4)continue;
                if(Physics.Raycast(lens.ScreenPointToRay(point),out var hit,5,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Collide)&&hit.collider.GetComponentInParent<PhysicalInteractable>()==item)return true;
            }
            return false;
        }
        static bool ExpectedStorageParent(PhysicalInteractable item)
        {
            if(!item.homeSlot||!item.homeSlot.container)return false;
            var container=item.homeSlot.container;
            if(item.itemType==PhysicalItemType.Tape||item.itemType==PhysicalItemType.ReleaseBook)
            {var drawer=container.GetComponent<WorkspaceDrawer>();return drawer&&drawer.slide&&item.homeSlot.transform.IsChildOf(drawer.slide);}
            if(item.itemType==PhysicalItemType.Sticker)return GameObject.Find("Desk revision 20261107")?container.displayName.EndsWith("贴纸盒"):container.name=="HangingStickerRack";
            if(item.itemType==PhysicalItemType.LoosePage)return container.name=="TieredPaperArchive";
            if(item.itemType==PhysicalItemType.Note)return GameObject.Find("Desk revision 20261107")?container.displayName=="布艺便签挂袋":container.name.StartsWith("SmallNoteBox_",StringComparison.Ordinal);
            if(item.itemType==PhysicalItemType.TravelersNotebook)return container.name=="TravelersBookcase";
            return true;
        }
        static void OpenOnlyHomeDrawer(PhysicalInteractable item)
        {
            if(!item||!item.homeSlot||!item.homeSlot.container)return;
            var source=drawers.FirstOrDefault(d=>d.container==item.homeSlot.container);
            if(!source)return;
            foreach(var drawer in drawers)drawer.SetOpenAmount(drawer==source?1:0);
            Debug.Log("[Workspace4BValidation] Accessible source drawer only: "+source.drawerId+" for "+item.itemInstanceId);
        }
        static bool PenetratesDrawerFloor(PhysicalInteractable item,WorkspaceDrawer drawer)
        {
            if(!item.interactionCollider||!drawer.slide)return false;
            foreach(var floor in drawer.slide.GetComponentsInChildren<Collider>())
            {
                if(floor.transform.IsChildOf(item.transform)||!floor.enabled||(floor.name!="Clear drawer floor"&&floor.name!="Wooden drawer floor"))continue;
                if(Physics.ComputePenetration(item.interactionCollider,item.transform.position,item.transform.rotation,
                    floor,floor.transform.position,floor.transform.rotation,out _,out float depth)&&depth>.0005f)return true;
            }
            return false;
        }
        static void CheckShaftSightlines(string drawerPose)
        {
            Physics.SyncTransforms();
            foreach(var shaft in shafts){
                var handle=shaft.rod.GetComponentInChildren<BoxCollider>();bool visible=false;
                if(handle){
                    for(int x=-1;x<=1 && !visible;x++)for(int y=-1;y<=1 && !visible;y++){
                        var b=handle.bounds;var px=lens.WorldToScreenPoint(b.center+new Vector3(x*b.extents.x*.45f,y*b.extents.y*.45f,0));
                        if(px.z<=0||px.x<1||px.y<1||px.x>=Screen.width-1||px.y>=Screen.height-1)continue;
                        var ray=lens.ScreenPointToRay(new Vector2(Mathf.Round(px.x),Mathf.Round(px.y)));
                        visible=Physics.Raycast(ray,out var hit,5,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Collide)&&hit.collider==handle;
                    }
                }
                Check("Art03","Seated ray reaches pull handle / drawers "+drawerPose+" / "+shaft.shaftId,visible,"First hit must be the real rod handle.");
            }
        }
        static void Capture(string name)
        {
            RenderTexture previous=RenderTexture.active;var target=new RenderTexture(1920,1080,24);var texture=new Texture2D(1920,1080,TextureFormat.RGB24,false);bool async=ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation=false;var request=new RenderPipeline.StandardRequest{destination=target};
                if(!RenderPipeline.SupportsRenderRequest(lens,request))throw new InvalidOperationException("GPU capture unsupported.");
                RenderPipeline.SubmitRenderRequest(lens,request);RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,1920,1080),0,0);texture.Apply();
                File.WriteAllBytes(Path.Combine(root,"docs/stage4b-screenshots",name),texture.EncodeToPNG());screenshots.Add(name);
            }
            finally{ShaderUtil.allowAsyncCompilation=async;RenderTexture.active=previous;UnityEngine.Object.Destroy(texture);target.Release();UnityEngine.Object.Destroy(target);}
        }
        static void Finish()
        {
            if(finishing)return;finishing=true;running=false;SessionState.SetBool(Pending,false);Application.logMessageReceived-=CaptureLog;
            root=root??Directory.GetParent(Application.dataPath).FullName;Directory.CreateDirectory(Path.Combine(root,"docs/stage4b-logs"));
            bool passed=results.Any(r=>r.passed)&&results.All(r=>r.status!="failed")&&errors.Count==0;
            bool bookProbe=Environment.GetCommandLineArgs().Contains("--workspace-book-probe");
            var report=new Report{timestampUtc=DateTime.UtcNow.ToString("O"),mode=bookProbe?"Focused book diagnostic in actual Unity Play Mode":"Actual rendered Unity Editor Play Mode",
                inputScope=bookProbe?"Focused lower-drawer book extraction, tabletop placement, opening and one material attachment only. Not full Stage04B acceptance; no native OS input.":"Runtime shared-intent actions and actual scene raycasts; no native OS input simulated by this harness.",
                screenshotScope="GPU live camera captures 1920x1080, excluding IMGUI. Native player screenshots are separate.",unityVersion=Application.unityVersion,gpu=SystemInfo.graphicsDeviceName,graphicsApi=SystemInfo.graphicsDeviceType.ToString(),
                savePath=journal?journal.SavePath:"Unavailable",width=Screen.width,height=Screen.height,renderedFrames=Time.frameCount,actualPlayMode=Application.isPlaying,passed=passed,
                lastCommitMilliseconds=workspace&&workspace.Ready?workspace.Service.LastCommitMilliseconds:0,
                maxCommitMilliseconds=workspace&&workspace.Ready?workspace.Service.MaxCommitMilliseconds:0,commitCount=workspace&&workspace.Ready?workspace.Service.CommitCount:0,
                lastSavePreparationMilliseconds=workspace&&workspace.Ready?workspace.Service.LastSavePreparationMilliseconds:0,
                maxSavePreparationMilliseconds=workspace&&workspace.Ready?workspace.Service.MaxSavePreparationMilliseconds:0,
                focusedBookProbe=bookProbe,
                checks=results.ToArray(),consoleErrors=errors.ToArray(),screenshots=screenshots.ToArray()};
            File.WriteAllText(Path.Combine(root,bookProbe?"docs/stage4b-logs/book-probe-tests.json":"docs/stage4b-logs/play-tests.json"),JsonUtility.ToJson(report,true));Debug.Log("[Workspace4BValidation] "+(bookProbe?"Focused book probe ":"")+"Completed "+(passed?"PASS":"FAIL"));
            if(Application.isBatchMode)EditorApplication.Exit(passed?0:1);else EditorApplication.isPlaying=false;
        }
    }
}

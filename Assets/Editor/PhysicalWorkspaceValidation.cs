using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace VintageJournal.Editor
{
    /// <summary>Actual rendered Play Mode acceptance. Intent submission is explicitly separate from native input.</summary>
    [InitializeOnLoad]
    public static class PhysicalWorkspaceValidation
    {
        const string Pending = "VintageJournal.Stage4Validation.Pending";
        static readonly List<Result> results = new List<Result>();
        static readonly List<string> errors = new List<string>();
        static readonly List<string> screenshots = new List<string>();
        static readonly Stack<IEnumerator> routines = new Stack<IEnumerator>();
        static bool preparing, running, finishing;
        static double preparationTime, started;
        static int lastFrame;
        static string root;
        static Component controller, surface;
        static Component[] items;
        static Camera lens;
        static DeskUI desk;
        static JournalController journal;
        static ScrapbookController scrapbook;
        static DecorationRenderer decorations;

        [Serializable] public sealed class Result { public string test, name, status, detail; public bool passed; }
        [Serializable] public sealed class Report
        {
            public string timestampUtc, mode, inputScope, screenshotScope, unityVersion, gpu, graphicsApi, savePath;
            public bool passed, actualPlayMode, nativeInputValidated;
            public int width, height, renderedFrames;
            public Result[] checks;
            public string[] consoleErrors, screenshots;
        }

        static PhysicalWorkspaceValidation()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += PlayChanged;
            if (SessionState.GetBool(Pending,false)) Application.logMessageReceived += CaptureLog;
        }

        [MenuItem("Vintage Journal/Stage 4/Validate physical workspace in Play Mode")]
        public static void Begin()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Start validation in Edit Mode.");
            if (!Environment.GetCommandLineArgs().Contains("--physical-validation")) throw new InvalidOperationException("Use --physical-validation to isolate player saves.");
            root = Directory.GetParent(Application.dataPath).FullName;
            Directory.CreateDirectory(Path.Combine(root,"docs/stage4-logs"));
            Directory.CreateDirectory(Path.Combine(root,"docs/stage4-screenshots"));
            if (!PhysicalWorkspaceDataTests.RunDataChecks()) throw new InvalidOperationException("Stage 4 data checks failed before Play; inspect data-tests.json.");
            results.Clear(); errors.Clear(); screenshots.Clear();
            SessionState.SetBool(Pending,true);
            Application.logMessageReceived -= CaptureLog; Application.logMessageReceived += CaptureLog;
            EditorSceneManager.OpenScene("Assets/Scenes/VintageJournalDesk.unity",OpenSceneMode.Single);
            preparing = true; preparationTime = EditorApplication.timeSinceStartup;
        }

        static void PlayChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Pending,false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                root = Directory.GetParent(Application.dataPath).FullName;
                results.Clear(); errors.Clear(); screenshots.Clear(); routines.Clear();
                started = EditorApplication.timeSinceStartup; lastFrame = -1; running = true; finishing = false;
                Application.logMessageReceived -= CaptureLog; Application.logMessageReceived += CaptureLog;
                routines.Push(Execute());
            }
            else if (state == PlayModeStateChange.EnteredEditMode && !finishing)
            { Check("Harness","Unexpected Play exit",false,"Validation did not complete."); Finish(); }
        }

        static void CaptureLog(string message,string stack,LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message + "\n" + stack); }

        static void Check(string id,string name,bool pass,string detail)
        {
            results.Add(new Result { test=id,name=name,passed=pass,status=pass?"passed":"failed",detail=detail });
            Debug.Log("[PhysicalValidation] " + id + " " + (pass?"PASS ":"FAIL ") + name + " " + detail);
        }

        static void Manual(string id,string name,string detail)
        {
            results.Add(new Result { test=id,name=name,passed=false,status="requires_manual_verification",detail=detail });
            Debug.Log("[PhysicalValidation] " + id + " MANUAL " + name + " " + detail);
        }

        static void Tick()
        {
            if (preparing)
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) { preparationTime=EditorApplication.timeSinceStartup; return; }
                if (EditorApplication.timeSinceStartup-preparationTime < 2) return;
                preparing=false; EditorApplication.isPlaying=true; return;
            }
            if (!running || finishing || !EditorApplication.isPlaying || Time.frameCount==lastFrame) return;
            lastFrame=Time.frameCount;
            if (EditorApplication.timeSinceStartup-started>240) { Check("Harness","Timeout",false,"240 seconds exceeded."); Finish(); return; }
            try
            {
                while (routines.Count>0)
                {
                    var routine=routines.Peek();
                    if (!routine.MoveNext()) { routines.Pop(); continue; }
                    if (routine.Current is IEnumerator child) { routines.Push(child); continue; }
                    return;
                }
                Finish();
            }
            catch (Exception error) { errors.Add(error.ToString()); Check("Harness","Exception",false,error.ToString()); Finish(); }
        }

        static IEnumerator Wait(double seconds)
        { double until=EditorApplication.timeSinceStartup+seconds; while(EditorApplication.timeSinceStartup<until)yield return null; }

        static IEnumerator Settled()
        {
            if(!controller){yield return Wait(.8);yield break;}
            double until=EditorApplication.timeSinceStartup+8;
            while((bool)Value(controller,"IsSettling") && EditorApplication.timeSinceStartup<until)yield return null;
            if((bool)Value(controller,"IsSettling"))throw new TimeoutException("Physical item did not settle in 8 seconds. LastResult="+Text(controller,"LastResult"));
            yield return null;
        }

        static IEnumerator JournalIdle()
        {
            double until=EditorApplication.timeSinceStartup+8;
            while ((journal.IsBusy || journal.cameraRig.IsTransitioning) && EditorApplication.timeSinceStartup<until) yield return null;
            if (journal.IsBusy || journal.cameraRig.IsTransitioning) throw new TimeoutException("Journal/camera did not settle.");
            yield return null;
        }

        static IEnumerator Execute()
        {
            yield return Settled();
            controller=FindOne("PhysicalInteractionController"); surface=FindOne("DeskPlacementSurface");
            items=FindAll("PhysicalInteractable"); lens=Camera.main;
            desk=UnityEngine.Object.FindFirstObjectByType<DeskUI>(); journal=UnityEngine.Object.FindFirstObjectByType<JournalController>();
            scrapbook=UnityEngine.Object.FindFirstObjectByType<ScrapbookController>(); decorations=UnityEngine.Object.FindFirstObjectByType<DecorationRenderer>();
            Check("Harness","Actual rendered Unity Play Mode",Application.isPlaying && SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null,
                Application.unityVersion+"; "+SystemInfo.graphicsDeviceName+"; "+Screen.width+"x"+Screen.height);
            if (!controller || !surface || items.Length==0 || !lens || !desk || !journal || !scrapbook || !decorations)
                throw new InvalidOperationException("Missing saved Stage 4 or Stage 3 runtime components.");
            if (journal.SavePath==null || !journal.SavePath.Contains("Stage4Validation"))
                throw new InvalidOperationException("Refusing to mutate a normal player save: "+journal.SavePath);
            // Runtime-intent tests deliberately bypass hardware/focus. Native OS events are a separate result.
            var mouse=FindOne("PhysicalMouseInputAdapter") as Behaviour;
            Check("T27","Mouse adapter uses shared runtime controller",mouse && Value(mouse,"controller")==controller,
                "Adapter exists and is bound to the same controller. Hardware event behavior is tested separately.");
            if (mouse) mouse.enabled=false;
            var oldInput=UnityEngine.Object.FindFirstObjectByType<ScrapbookInput>(); if(oldInput)oldInput.enabled=false;
            desk.Begin(); yield return Wait(.2); Physics.SyncTransforms();
            var rig=lens.GetComponent<DeskCamera>(); Vector3 seated=lens.transform.position; Quaternion seatedRotation=lens.transform.rotation;
            yield return Wait(.15);
            Check("T01","Fixed seated first-person perspective",rig && !rig.IsInspecting && Vector3.Distance(seated,lens.transform.position)<.0001f &&
                Quaternion.Angle(seatedRotation,lens.transform.rotation)<.001f,"Camera remains at the configured desk pose while idle.");
            var size=(Vector2)Value(surface,"size");
            Check("T02","Desk operation area uses meters",size.x>=1.10f && size.x<=1.4f && size.y>=.55f && size.y<=.75f &&
                surface.transform.position.y>=.72f && surface.transform.position.y<=.761f,
                "Configured surface="+size+", world tabletop y="+surface.transform.position.y.ToString("F3"));
            Check("T24","Every new physical item has meter dimensions",items.All(ValidDimensions),
                items.Length+" items checked against type-specific real-world ranges; no enlarged logical root scale.");
            Check("T04","Four distinct TN notebooks and independent anchors",items.Count(i=>Text(i,"itemType")=="TravelersNotebook")>=4 &&
                items.Where(i=>Text(i,"itemType")=="TravelersNotebook").All(i=>ObjectValue(i,"grabAnchor")&&ObjectValue(i,"homeAnchor")&&ObjectValue(i,"inspectAnchor")) &&
                items.Where(i=>Text(i,"itemType")=="TravelersNotebook").Select(i=>i.transform.position).Distinct().Count()>=4,
                "Notebook styles occupy separate shelf poses and expose artwork-independent anchors.");

            var boxes=FindAll("PhysicalContainer");
            foreach(var box in boxes) Invoke(box,"SetOpen",true);
            yield return Wait(.5); Physics.SyncTransforms();
            foreach(string category in new[]{"Tape","Sticker","Note","LoosePage","TravelersNotebook"})
            {
                var visible=items.Where(i=>Text(i,"itemType")==category).FirstOrDefault(i=>FindHit(i,out _));
                Check("T05","Ray identifies visible "+category,visible,"Actual camera Physics.Raycast must hit this exact PhysicalInteractable.");
            }
            Capture("workspace-open.png");
            Component tape=First("Tape"),sticker=First("Sticker"),note=First("Note"),sheet=First("LoosePage"),tn=First("TravelersNotebook");
            Pose home=PoseOf(tape); string tapeId=Text(tape,"itemInstanceId");
            bool hovered=Submit("Hover",tape);
            Check("T05","Hover intent selects and highlights item",hovered && ObjectValue(controller,"HoveredItem")==tape && Text(tape,"CurrentState")=="Hovered",
                Text(tape,"displayName")+"; hover transitions through the shared input-independent controller.");
            bool grabbed=Submit("Grab",tape);
            Check("T06","Grab physical tape roll",grabbed && ObjectValue(controller,"HeldItem")==tape && IsHeld(tape),Text(tape,"CurrentState"));
            Check("T17","Reject repeated and concurrent grabs",!Submit("Grab",tape) && !Submit("Grab",sticker) && ObjectValue(controller,"HeldItem")==tape,
                "A second grab of the held item and a competing item are both rejected.");
            Vector3 before=tape.transform.position;
            Pose free=FindFreePose(tape);
            bool moved=Submit("Move",tape,free);
            Check("T14","Move target is smoothed rather than teleported",moved && Vector3.Distance(before,tape.transform.position)<.004f,
                "Immediately after intent: displacement="+Vector3.Distance(before,tape.transform.position).ToString("F5")+"m.");
            yield return Wait(1.1);
            Check("T07","Tape follows workspace target",Vector3.Distance(new Vector3(tape.transform.position.x,0,tape.transform.position.z),
                new Vector3(free.position.x,0,free.position.z))<.035f && IsHeld(tape),"Target="+free.position+" actual="+tape.transform.position);
            bool released=Submit("Release",tape,free);yield return Settled();
            Check("T07","Tape placed on valid desk region",released && !ObjectValue(controller,"HeldItem") && Text(tape,"CurrentState")=="Placed",
                "Placement state="+Text(tape,"CurrentState")+"; root="+tape.transform.position);
            Capture("tape-on-desk.png");
            Check("T03","Placed tape rests on the tabletop",Mathf.Abs(tape.transform.position.y-(surface.transform.position.y+Dimension(tape).y*.5f))<.006f,
                "No artificial display-scale lift after release; expected support height from profile thickness.");
            bool returned=Submit("ReturnHome",tape);yield return Settled();
            Check("T08","Tape returns to its reserved rack slot",returned && PoseNear(PoseOf(tape),home) && IsStored(tape) &&
                ObjectValue(ObjectValue(tape,"homeSlot"),"Occupant")==tape,"Exact origin pose and exclusive slot occupant restored.");
            bool snapGrab=Submit("Grab",tape);
            Submit("Move",tape,new Pose(home.position+Vector3.right*.005f,home.rotation));
            yield return Wait(.2);
            bool snapRelease=Submit("Release",tape);yield return Settled();
            Check("T08","Release near original slot automatically snaps home",snapGrab && snapRelease &&
                PoseNear(PoseOf(tape),home) && IsStored(tape) && ObjectValue(ObjectValue(tape,"homeSlot"),"Occupant")==tape,
                "Move intent requests a point 5mm from home; Release restores the exact reserved storage pose without ReturnHome.");

            var obstacle=GameObject.CreatePrimitive(PrimitiveType.Cube);obstacle.name="Temporary collision acceptance fixture";
            obstacle.transform.position=free.position+Vector3.up*.025f;obstacle.transform.localScale=new Vector3(.02f,.08f,.08f);
            Physics.SyncTransforms();
            var collider=ObjectValue(tape,"interactionCollider") as BoxCollider;
            Vector3 sweepFrom=free.position+Vector3.right*.09f,sweepTo=free.position-Vector3.right*.09f;
            var swept=(Vector3)Invoke(surface,"Sweep",tape,sweepFrom,sweepTo,Quaternion.identity,false);
            bool penetrates=Physics.ComputePenetration(collider,swept,Quaternion.identity,obstacle.GetComponent<Collider>(),obstacle.transform.position,
                obstacle.transform.rotation,out _,out _);
            Check("T03","Swept movement cannot tunnel through a static obstacle",!penetrates && Vector3.Distance(swept,sweepTo)>.04f &&
                swept.x>obstacle.transform.position.x,"Actual box-cast sweep across a temporary solid fixture stops before it; endpoint="+swept);
            bool blockedPlacement=!(bool)Invoke(surface,"IsValidPlacement",tape,free);
            bool blockerGrab=Submit("Grab",tape);Submit("Move",tape,free);yield return Wait(.2);
            bool blockerRelease=Submit("Release",tape,free);yield return Settled();
            Check("T15","Drop on solid obstacle is rejected and restored",blockedPlacement && blockerGrab && !blockerRelease &&
                !ObjectValue(controller,"HeldItem") && PoseNear(PoseOf(tape),home) && IsStored(tape),
                "Actual overlap check rejects the occupied tabletop target; controller restores its safe rack pose.");
            UnityEngine.Object.Destroy(obstacle);yield return null;Physics.SyncTransforms();

            Pose thinPose=FindFreePose(note);
            bool baselineClear=(bool)Invoke(surface,"IsValidPlacement",note,thinPose);
            var raisedSlab=GameObject.CreatePrimitive(PrimitiveType.Cube);raisedSlab.name="Temporary overhead slab regression fixture";
            raisedSlab.transform.position=thinPose.position+Vector3.up*.025f;
            raisedSlab.transform.localScale=new Vector3(.12f,.008f,.12f);Physics.SyncTransforms();
            bool endpointClear=!(bool)Invoke(surface,"HasCollision",note,thinPose.position,thinPose.rotation,false);
            bool approachBlocked=!(bool)Invoke(surface,"IsValidPlacement",note,thinPose);
            UnityEngine.Object.Destroy(raisedSlab);yield return null;Physics.SyncTransforms();
            bool unobstructedAgain=(bool)Invoke(surface,"IsValidPlacement",note,thinPose);
            Check("T15","Thin paper cannot be lowered through a raised slab",baselineClear && endpointClear && approachBlocked && unobstructedAgain,
                "The note endpoint has no overlap under a slab 21mm above it, but its vertical hand approach is blocked; removing the slab restores validity.");

            var stickerBox=ObjectValue(sticker,"CurrentContainer") as Component;
            if (!stickerBox) stickerBox=ObjectValue(ObjectValue(sticker,"homeSlot"),"container") as Component;
            bool shut=stickerBox && (bool)Invoke(stickerBox,"SetOpen",false);
            bool blocked=shut && !Submit("Grab",sticker);
            bool opened=stickerBox && (bool)Invoke(stickerBox,"SetOpen",true);
            yield return Wait(.4);
            Check("T09","Sticker box opens and gates access",shut && blocked && opened && (bool)Value(stickerBox,"isOpen"),
                "Closed box rejects grab; opening restores access through container state.");
            Pose stickerHome=PoseOf(sticker);
            bool stickerGrab=Submit("Grab",sticker);yield return Wait(.15);
            Check("T10","Take independent sticker from box",stickerGrab && IsHeld(sticker) && !ObjectValue(sticker,"CurrentContainer"),Text(sticker,"assetId"));
            bool closedHeld=stickerBox && (bool)Invoke(stickerBox,"SetOpen",false);
            Check("T16","Container cannot close across held item",!closedHeld && (bool)Value(stickerBox,"isOpen"),"No trapped item or simultaneous stored/grabbed ownership.");
            Submit("Cancel",sticker);yield return Settled();
            Check("T15","Cancel returns sticker safely",!ObjectValue(controller,"HeldItem") && PoseNear(PoseOf(sticker),stickerHome) && IsStored(sticker),
                "Exact grab-origin pose and ownership restored.");

            yield return TakeReturn(note,"T11","Note sheet taken and returned");
            yield return TakeReturn(sheet,"T12","Loose inner page taken and returned");
            Pose tnHome=PoseOf(tn);bool inspected=Submit("Inspect",tn);yield return Wait(1.0);
            Check("T13","TN notebook can be inspected",inspected && ObjectValue(controller,"HeldItem")==tn && IsHeld(tn) &&
                Vector3.Distance(tn.transform.position,tnHome.position)>.03f,"Inspect moves the independent product toward its viewing anchor without opening/editing pages.");
            bool tnReturn=Submit("ReturnHome",tn);yield return Settled();
            Check("T13","TN notebook returns to shelf",tnReturn && PoseNear(PoseOf(tn),tnHome) && IsStored(tn),"Product identity and shelf anchor retained.");
            Check("T01","Object inspection preserves seated camera",Vector3.Distance(seated,lens.transform.position)<.0001f &&
                Quaternion.Angle(seatedRotation,lens.transform.rotation)<.001f,"Only held object moves; DeskCamera remains the camera owner.");
            Capture("tn-returned.png");

            Pose invalidHome=PoseOf(tape);Submit("Grab",tape);
            Pose invalid=new Pose(surface.transform.position+new Vector3(size.x+1,Dimension(tape).y*.5f,0),Quaternion.identity);
            Submit("Move",tape,invalid);yield return Wait(.15);
            bool invalidRelease=Submit("Release",tape,invalid);yield return Settled();
            Check("T15","Off-table release restores safe origin",!invalidRelease && !ObjectValue(controller,"HeldItem") &&
                PoseNear(PoseOf(tape),invalidHome) && IsStored(tape),"Raw invalid request is rejected; visual clamping never makes an invalid drop valid.");
            Check("T16","Illegal stored-to-moving transition rejected",!(bool)Invoke(tape,"TryTransition",EnumValue("PhysicalItemState","Moving")) && IsStored(tape),
                "Movement cannot start without a grab transaction.");

            var duplicatePair=items.GroupBy(i=>Text(i,"assetId")).FirstOrDefault(group=>group.Count()>=2);
            Component subject=duplicatePair!=null?duplicatePair.First():sticker;
            var other=duplicatePair!=null?duplicatePair.Skip(1).First():note;
            string otherState=Text(other,"CurrentState");Pose otherPose=PoseOf(other);
            bool independentGrab=Submit("Grab",subject);Submit("Move",subject,FindFreePose(subject));yield return Wait(.15);
            Check("T18","Same-asset instances keep independent mutable state",duplicatePair!=null && independentGrab &&
                Text(subject,"assetId")==Text(other,"assetId") && Text(other,"CurrentState")==otherState && PoseNear(PoseOf(other),otherPose) &&
                Text(subject,"itemInstanceId")!=Text(other,"itemInstanceId"),
                "Two physical instances of "+Text(subject,"assetId")+" share immutable asset data but keep separate identities, pose and state.");
            Submit("Cancel",subject);yield return Settled();
            var slot=ObjectValue(tape,"homeSlot");
            var anotherTape=items.First(i=>i!=tape && Text(i,"itemType")=="Tape");
            bool occupied=(bool)Invoke(slot,"TryStore",anotherTape,false);
            Check("T16","Exclusive storage refuses second occupant",!occupied && ObjectValue(slot,"Occupant")==tape,
                "A reserved/occupied slot never admits a competing object.");

            // Replace artwork without changing the logical root, collider, anchors, dimensions or AssetId.
            Transform original=ObjectValue(tape,"visualRoot") as Transform;
            var replacement=GameObject.CreatePrimitive(PrimitiveType.Cube);replacement.name="Temporary visual replacement test";
            replacement.transform.SetParent(tape.transform,false);replacement.transform.localScale=Dimension(tape);
            UnityEngine.Object.Destroy(replacement.GetComponent<Collider>());yield return null;
            original.gameObject.SetActive(false);Invoke(tape,"RebindVisual",replacement.transform);
            Pose replacementHome=PoseOf(tape);bool replacementGrab=Submit("Grab",tape);yield return Wait(.15);
            bool replacementReturn=Submit("ReturnHome",tape);yield return Settled();
            Check("T26","Replacing visual model preserves interaction",replacementGrab && replacementReturn && PoseNear(PoseOf(tape),replacementHome) &&
                Text(tape,"itemInstanceId")==tapeId && ObjectValue(tape,"interactionCollider") && Text(tape,"assetId")!=string.Empty,
                "Live tape artwork replaced by a cube, grabbed and returned through unchanged logical binding.");
            Invoke(tape,"RebindVisual",original);original.gameObject.SetActive(true);UnityEngine.Object.Destroy(replacement);
            Check("T25","Gameplay remains independent of artwork",items.All(i=>ObjectValue(i,"visualRoot") && ObjectValue(i,"grabAnchor") &&
                ObjectValue(i,"homeAnchor") && ObjectValue(i,"interactionCollider")),"Independent binding contract plus the live replacement behavior in T26.");
            bool syntheticHover=Submit("Hover",note),syntheticGrab=Submit("Grab",note);Submit("Cancel",note);yield return Settled();
            Check("T28","Alternate input source drives unchanged item logic",syntheticHover && syntheticGrab && !ObjectValue(controller,"HeldItem") && IsStored(note),
                "This test is an alternate producer of Hover/Grab/Cancel intents; no mouse events or per-item code changes were used.");

            yield return JournalRegression();
            Manual("T23","Windows standalone native input acceptance","Requires a separately launched Windows build and verified native mouse/keyboard probe. This Play report does not claim it.");
            Manual("T03","Visual inspection of all furniture and paper","GPU screenshots are retained; final aesthetic/occlusion review and unusual dragging routes require human inspection.");
            Check("T22","No blocking Unity runtime errors",errors.Count==0,errors.Count+" errors/asserts/exceptions captured during this Play session.");
        }

        static IEnumerator TakeReturn(Component item,string id,string name)
        {
            var box=ObjectValue(ObjectValue(item,"homeSlot"),"container");if(box)Invoke(box,"SetOpen",true);
            Pose home=PoseOf(item);bool grab=Submit("Grab",item);yield return Wait(.2);
            bool held=ObjectValue(controller,"HeldItem")==item && IsHeld(item);
            Pose target=FindFreePose(item);bool moved=Submit("Move",item,target);yield return Wait(1);
            bool release=Submit("Release",item,target);yield return Settled();
            bool placed=!ObjectValue(controller,"HeldItem") && Text(item,"CurrentState")=="Placed" &&
                Vector3.Distance(item.transform.position,target.position)<.007f;
            if(Text(item,"itemType")=="LoosePage")placed=placed && Quaternion.Angle(item.transform.rotation,Quaternion.identity)<1;
            Check(id,name+"; moved and placed",grab && held && moved && release && placed,
                Text(item,"assetId")+"; target="+target.position+"; actual="+item.transform.position+"; "+Text(controller,"LastResult"));
            bool ret=Submit("ReturnHome",item);yield return Settled();
            Check(id,name+"; exact home restored",ret && PoseNear(PoseOf(item),home) && IsStored(item),Text(item,"assetId"));
        }

        static IEnumerator JournalRegression()
        {
            scrapbook.Cancel();journal.Document.currentSpread=0;
            foreach(var page in journal.Document.pages)page.decorations.Clear();journal.RefreshPages();decorations.Refresh();
            Check("T19","Original journal editor opens",journal.EnterEditing(),"Existing JournalController.EnterEditing, unchanged digital material data.");
            yield return JournalIdle();
            var sticker=scrapbook.Catalog.assets.First(a=>a.category==DecorationCategory.Sticker&&a.IsAvailable);
            scrapbook.Browse(DecorationCategory.Sticker);bool chose=scrapbook.Choose(sticker.assetId);
            bool placed=scrapbook.Place(JournalSide.Left,new Vector2(.4f,.45f));string id=scrapbook.SelectedId;
            var pageLeft=journal.Document.GetPage(JournalSide.Left);var record=pageLeft.decorations.FirstOrDefault(d=>d.instanceId==id);
            bool digital=chose && placed && record!=null && decorations.GetVisual(id) && !items.Any(i=>Text(i,"itemInstanceId")==id);
            bool began=scrapbook.BeginTransform("move");scrapbook.MoveTo(new Vector2(.62f,.55f));scrapbook.CommitTransform();
            bool undo=scrapbook.Undo();var restored=pageLeft.decorations.FirstOrDefault(d=>d.instanceId==id);
            Check("T19","Digital sticker edit and undo remain usable",digital && began && undo && restored!=null &&
                Vector2.Distance(restored.position,new Vector2(.4f,.45f))<.005f,"Page-decoration instance remains separate from physical stock.");
            while(scrapbook.Cancel()){};
            var tape=scrapbook.Catalog.assets.First(a=>a.category==DecorationCategory.Tape&&a.IsAvailable);
            scrapbook.Browse(DecorationCategory.Tape);bool tapeChose=scrapbook.Choose(tape.assetId);
            bool tapePlaced=scrapbook.Place(JournalSide.Left,new Vector2(.5f,.72f));string tapeId=scrapbook.SelectedId;
            Check("T19","Digital continuous-texture tape remains placeable",tapeChose && tapePlaced && FindDecoration(tapeId)!=null &&
                decorations.GetVisual(tapeId) && tape.texture!=tape.thumbnail,"Continuous texture reference stays separate from the product-card thumbnail.");
            TransformRoundtrip(tapeId,"length",()=>scrapbook.SetLength(1.7f),"Tape length undo/redo");
            TransformRoundtrip(tapeId,"rotate",()=>scrapbook.RotateTo(35f),"Tape rotation undo/redo");
            TransformRoundtrip(tapeId,"scale",()=>scrapbook.ScaleTo(1.2f),"Tape scale undo/redo");
            scrapbook.Select(tapeId,JournalSide.Left);bool bottom=scrapbook.Layer("bottom");int bottomOrder=FindDecoration(tapeId).sortOrder;
            bool top=scrapbook.Layer("top");int topOrder=FindDecoration(tapeId).sortOrder;
            bool layerUndo=scrapbook.Undo();bool restoredBottom=FindDecoration(tapeId).sortOrder==bottomOrder;
            bool layerRedo=scrapbook.Redo();bool restoredTop=FindDecoration(tapeId).sortOrder==topOrder;
            Check("T19","Overlapping tape/sticker layer edit undo/redo",bottom && top && topOrder>bottomOrder && layerUndo && restoredBottom &&
                layerRedo && restoredTop && pageLeft.decorations.Select(d=>d.sortOrder).Distinct().Count()==pageLeft.decorations.Count,
                "Layer order remains per-page and distinct after replaying history at the adapted physical page scale.");
            while(scrapbook.Cancel()){};
            Vector3 tapeWorld=decorations.GetVisual(tapeId).position;
            Vector2 uv=new Vector2(.3f,.65f);Vector3 world=journal.pageView.WorldFromNormalized(JournalSide.Left,uv);
            bool conversion=journal.pageView.TryWorldToNormalized(JournalSide.Left,world,out var roundtrip);
            Check("T02","Physical page and normalized coordinate roundtrip",conversion && Vector2.Distance(uv,roundtrip)<.0001f,
                "World/page transform preserves existing normalized decoration coordinates at real display scale.");
            string snapshot=JsonUtility.ToJson(journal.Document);
            bool forward=journal.Turn(1);yield return JournalIdle();bool back=journal.Turn(-1);yield return JournalIdle();
            Check("T20","Page turn preserves original decorated spread",forward && back && JsonUtility.ToJson(journal.Document)==snapshot,
                "Page identities, selected papers, decorations and current spread return exactly.");
            string prior=journal.Document.GetPage(JournalSide.Right).paperId;
            string next=journal.Catalog.papers.First(p=>p.id!=prior).id;
            journal.BeginPaperSelection(JournalSide.Right);journal.SelectPaper(next);journal.ConfirmPaper();
            Check("T20","Original inner-paper replacement works",journal.Document.GetPage(JournalSide.Right).paperId==next && !journal.HasPaperPreview,next);
            journal.ZoomBy(.3f);yield return Wait(.3);
            Check("T20","Original page zoom works",journal.cameraRig.Zoom>1.2f && Vector3.Distance(world,journal.pageView.WorldFromNormalized(JournalSide.Left,uv))<.0001f,
                "Zoom changes camera projection while decoration attachment remains page-local.");
            Check("T19","Tape attachment survives page turn and zoom",decorations.GetVisual(tapeId) &&
                Vector3.Distance(tapeWorld,decorations.GetVisual(tapeId).position)<.001f,
                "The scaled, rotated, length-adjusted tape keeps its physical page attachment after flip, paper replacement and camera zoom.");
            journal.ResetView();yield return Wait(.3);Capture("digital-editor-regression.png");
            string saved=JsonUtility.ToJson(journal.Document);bool wrote=journal.Save();
            var loaded=new JournalSaveService(journal.Catalog,journal.SavePath).LoadOrCreate();
            Check("T21","Existing v1 save reads and roundtrips",wrote && JsonUtility.ToJson(loaded)==saved && loaded.schemaVersion==1,
                "Actual isolated filesystem write and new JournalSaveService reload; prior-schema fixtures also run in data-tests.json.");
            journal.ExitEditing();yield return JournalIdle();
            Capture("workspace-final.png");
        }

        static JournalDecorationData FindDecoration(string id)=>journal.Document.GetPage(JournalSide.Left).decorations.FirstOrDefault(d=>d.instanceId==id);

        static void TransformRoundtrip(string id,string kind,Func<bool> setter,string label)
        {
            bool selected=scrapbook.Select(id,JournalSide.Left);string before=JsonUtility.ToJson(FindDecoration(id));
            bool began=scrapbook.BeginTransform(kind);bool changed=began&&setter();scrapbook.CommitTransform();
            string after=JsonUtility.ToJson(FindDecoration(id));bool undo=scrapbook.Undo();bool beforeRestored=JsonUtility.ToJson(FindDecoration(id))==before;
            bool redo=scrapbook.Redo();bool afterRestored=JsonUtility.ToJson(FindDecoration(id))==after;
            Check("T19",label,selected && began && changed && before!=after && undo && beforeRestored && redo && afterRestored,
                "Full decoration identity, transform, length and layer record roundtrips through existing history commands.");
        }

        static Pose FindFreePose(Component item)
        {
            Vector2 size=(Vector2)Value(surface,"size");
            MethodInfo method=surface.GetType().GetMethod("IsValidPlacement",BindingFlags.Public|BindingFlags.Instance);
            // Prefer the front work area, leaving the central journal and rear furniture clear.
            foreach(float z in new[]{-.25f,-.35f,-.1f,.05f,.2f,.35f})
                foreach(float x in new[]{-.35f,.35f,-.2f,.2f,0f,-.42f,.42f})
                {
                    var point=surface.transform.TransformPoint(new Vector3(size.x*x,Dimension(item).y*.5f,size.y*z));
                    Pose pose=new Pose(point,Quaternion.identity);
                    if (method!=null)
                    {
                        var ps=method.GetParameters();
                        if(ps.Length==2 && ps[0].ParameterType==item.GetType() && ps[1].ParameterType==typeof(Pose) && (bool)method.Invoke(surface,new object[]{item,pose}))return pose;
                    }
                }
            // API-independent fallback candidate; successful release and exact support are asserted afterwards.
            return new Pose(surface.transform.TransformPoint(new Vector3(size.x*.3f,Dimension(item).y*.5f,-size.y*.28f)),Quaternion.identity);
        }

        static bool FindHit(Component item,out Vector2 point)
        {
            point=default;var collider=ObjectValue(item,"interactionCollider") as Collider;
            if(!collider || !collider.enabled)return false;
            var b=collider.bounds;
            for(int x=-2;x<=2;x++)for(int z=-2;z<=2;z++)
            {
                Vector3 projected=lens.WorldToScreenPoint(b.center+new Vector3(b.extents.x*x*.35f,b.extents.y*.5f,b.extents.z*z*.35f));
                if(projected.z<=0||projected.x<4||projected.y<4||projected.x>Screen.width-4||projected.y>Screen.height-4||desk.IsPointerOverInterface(projected))continue;
                if(Physics.Raycast(lens.ScreenPointToRay(projected),out var hit,5f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Collide) &&
                    hit.collider.GetComponentInParent(item.GetType())==item){point=projected;return true;}
            }
            return false;
        }

        static bool ValidDimensions(Component item)
        {
            Vector3 v=Dimension(item);if(v.x<=0||v.y<=0||v.z<=0||Vector3.Distance(item.transform.lossyScale,Vector3.one)>.001f)return false;
            switch(Text(item,"itemType"))
            {
                case "Tape": return v.x>=.03f&&v.x<=.06f&&v.y>=.009f&&v.y<=.025f&&v.z>=.03f&&v.z<=.06f;
                case "Sticker": return v.x>=.015f&&v.x<=.07f&&v.y<=.004f&&v.z>=.015f&&v.z<=.07f;
                case "Note": return v.x>=.065f&&v.x<=.09f&&v.y<=.005f&&v.z>=.065f&&v.z<=.09f;
                case "LoosePage": return v.x>=.1f&&v.x<=.16f&&v.y<=.005f&&v.z>=.14f&&v.z<=.22f;
                case "TravelersNotebook": return v.x>=.085f&&v.x<=.14f&&Mathf.Min(v.y,v.z)>=.008f&&Mathf.Min(v.y,v.z)<=.035f&&Mathf.Max(v.y,v.z)>=.12f&&Mathf.Max(v.y,v.z)<=.23f;
                default:return false;
            }
        }

        static void Capture(string name)
        {
            var previous=RenderTexture.active;var target=new RenderTexture(1920,1080,24);var texture=new Texture2D(1920,1080,TextureFormat.RGB24,false);
            bool asynchronous=ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation=false;
                var request=new RenderPipeline.StandardRequest{destination=target};
                if(!RenderPipeline.SupportsRenderRequest(lens,request))throw new InvalidOperationException("SRP capture unsupported.");
                RenderPipeline.SubmitRenderRequest(lens,request);RenderTexture.active=target;
                texture.ReadPixels(new Rect(0,0,1920,1080),0,0);texture.Apply();
                File.WriteAllBytes(Path.Combine(root,"docs/stage4-screenshots",name),texture.EncodeToPNG());screenshots.Add(name);
            }
            finally
            {ShaderUtil.allowAsyncCompilation=asynchronous;RenderTexture.active=previous;UnityEngine.Object.Destroy(texture);target.Release();UnityEngine.Object.Destroy(target);}
        }

        static void Finish()
        {
            if(finishing)return;finishing=true;running=false;SessionState.SetBool(Pending,false);Application.logMessageReceived-=CaptureLog;
            root=root??Directory.GetParent(Application.dataPath).FullName;Directory.CreateDirectory(Path.Combine(root,"docs/stage4-logs"));
            var report=new Report{timestampUtc=DateTime.UtcNow.ToString("O"),mode="Actual rendered Unity Editor Play Mode",
                inputScope="Actual scene raycasts and alternate producer of shared physical interaction intents; no native OS events in this harness. Digital editor runtime regression plus real isolated save-file I/O.",
                screenshotScope="GPU-rendered live Play scene at 1920x1080, excluding IMGUI. Separate native player captures include UI.",
                unityVersion=Application.unityVersion,gpu=SystemInfo.graphicsDeviceName,graphicsApi=SystemInfo.graphicsDeviceType.ToString(),
                savePath=journal?journal.SavePath:"Unavailable",width=Screen.width,height=Screen.height,renderedFrames=Time.frameCount,
                actualPlayMode=Application.isPlaying,nativeInputValidated=false,
                passed=results.Any(r=>r.status=="passed")&&results.All(r=>r.status!="failed")&&errors.Count==0,
                checks=results.ToArray(),consoleErrors=errors.ToArray(),screenshots=screenshots.ToArray()};
            File.WriteAllText(Path.Combine(root,"docs/stage4-logs/play-tests.json"),JsonUtility.ToJson(report,true));
            Debug.Log("[PhysicalValidation] Completed "+(report.passed?"PASS":"FAIL")+"; manual checks remain explicitly listed.");
            if(Application.isBatchMode)EditorApplication.Exit(report.passed?0:1);else EditorApplication.isPlaying=false;
        }

        static Type RuntimeType(string name)=>AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("VintageJournal."+name,false)).FirstOrDefault(t=>t!=null);
        static Component[] FindAll(string name){Type type=RuntimeType(name);return type==null?Array.Empty<Component>():UnityEngine.Object.FindObjectsByType(type,FindObjectsInactive.Include,FindObjectsSortMode.None).OfType<Component>().ToArray();}
        static Component FindOne(string name)=>FindAll(name).FirstOrDefault();
        static Component First(string kind)=>items.First(i=>Text(i,"itemType")==kind);
        static object Value(object owner,string name)=>owner==null?null:owner.GetType().GetField(name,BindingFlags.Public|BindingFlags.Instance)?.GetValue(owner)??owner.GetType().GetProperty(name,BindingFlags.Public|BindingFlags.Instance)?.GetValue(owner);
        static UnityEngine.Object ObjectValue(object owner,string name)=>Value(owner,name) as UnityEngine.Object;
        static string Text(object owner,string name)=>Value(owner,name)?.ToString()??string.Empty;
        static object EnumValue(string type,string name)=>Enum.Parse(RuntimeType(type),name);
        static object Invoke(object owner,string method,params object[] args)
        {var methods=owner.GetType().GetMethods(BindingFlags.Public|BindingFlags.Instance).Where(m=>m.Name==method&&m.GetParameters().Length==args.Length).ToArray();if(methods.Length!=1)throw new MissingMethodException(owner.GetType().Name+"."+method+"/"+args.Length);return methods[0].Invoke(owner,args);}
        static bool Submit(string kind,Component target=null,Pose? pose=null)
        {
            Type type=RuntimeType("PhysicalInteractionIntent");object intent=Activator.CreateInstance(type,
                new object[]{EnumValue("PhysicalIntentKind",kind),target,pose??default(Pose)});
            return (bool)Invoke(controller,"Submit",intent);
        }
        static Vector3 Dimension(Component item)=>(Vector3)Value(ObjectValue(item,"dimensions"),"size");
        static Pose PoseOf(Component item)=>new Pose(item.transform.position,item.transform.rotation);
        static bool PoseNear(Pose a,Pose b)=>Vector3.Distance(a.position,b.position)<.004f&&Quaternion.Angle(a.rotation,b.rotation)<1;
        static bool IsHeld(Component item)=>new[]{"Grabbed","Moving"}.Contains(Text(item,"CurrentState"));
        static bool IsStored(Component item)=>new[]{"Stored","Available"}.Contains(Text(item,"CurrentState"));
    }
}

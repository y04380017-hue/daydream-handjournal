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
    /// <summary>Runs the saved scene in actual Play Mode. Native events/restart are separate probes.</summary>
    [InitializeOnLoad]
    public static class ScrapbookValidation
    {
        const string Pending="VintageJournal.Stage3Validation.Pending";
        static readonly List<Result> results=new List<Result>();
        static readonly List<string> errors=new List<string>();
        static readonly Stack<IEnumerator> routines=new Stack<IEnumerator>();
        static ScrapbookController scrapbook;
        static JournalController journal;
        static DecorationRenderer decorations;
        static DeskUI desk;
        static DeskInteraction interaction;
        static Camera lens;
        static bool preparing,running,finishing;
        static double preparationTime,started;
        static int lastFrame;
        static string root;
        static DateTime startedUtc;
        [Serializable] public sealed class Result {public string test,name,detail;public bool passed;}
        [Serializable] public sealed class Report
        {
            public string timestampUtc,mode,inputScope,screenshotScope,unityVersion,gpu,graphicsApi,savePath;
            public int width,height,renderedFrames; public bool passed,actualPlayMode;
            public Result[] checks; public string[] consoleErrors,screenshots;
        }
        static ScrapbookValidation()
        {
            EditorApplication.update+=Tick;EditorApplication.playModeStateChanged+=PlayChanged;
            if(SessionState.GetBool(Pending,false))Application.logMessageReceived+=CaptureLog;
        }
        [MenuItem("Vintage Journal/Stage 3/Validate scrapbook in Play Mode")]
        public static void Begin()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Start validation in Edit Mode.");
            if(!Environment.GetCommandLineArgs().Contains("--scrapbook-validation"))throw new InvalidOperationException("Use --scrapbook-validation to isolate player saves.");
            root=Directory.GetParent(Application.dataPath).FullName;
            Directory.CreateDirectory(Path.Combine(root,"docs/stage3-logs"));Directory.CreateDirectory(Path.Combine(root,"docs/stage3-screenshots"));
            var data=typeof(ScrapbookValidation).Assembly.GetType("VintageJournal.Editor.DecorationDataTests");
            var method=data?.GetMethod("RunDataChecks",BindingFlags.Public|BindingFlags.Static);
            if(method==null||!(bool)method.Invoke(null,null))throw new InvalidOperationException("Decoration data/history checks did not pass.");
            results.Clear();errors.Clear();SessionState.SetBool(Pending,true);
            Application.logMessageReceived-=CaptureLog;Application.logMessageReceived+=CaptureLog;
            EditorSceneManager.OpenScene("Assets/Scenes/VintageJournalDesk.unity",OpenSceneMode.Single);
            ConfigureFullHd();preparing=true;preparationTime=EditorApplication.timeSinceStartup;
        }
        static void PlayChanged(PlayModeStateChange state)
        {
            if(!SessionState.GetBool(Pending,false))return;
            if(state==PlayModeStateChange.EnteredPlayMode)
            {
                root=Directory.GetParent(Application.dataPath).FullName;results.Clear();errors.Clear();routines.Clear();
                started=EditorApplication.timeSinceStartup;startedUtc=DateTime.UtcNow;lastFrame=-1;running=true;finishing=false;
                Application.logMessageReceived-=CaptureLog;Application.logMessageReceived+=CaptureLog;routines.Push(Execute());
            }
            else if(state==PlayModeStateChange.EnteredEditMode&&!finishing){Check("Harness","Unexpected Play exit",false,"Validation did not complete.");Finish();}
        }
        static void CaptureLog(string message,string stack,LogType type)
        {if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message+"\n"+stack);}
        static void Check(string id,string name,bool pass,string detail)
        {results.Add(new Result{test=id,name=name,passed=pass,detail=detail});Debug.Log("[ScrapbookValidation] "+id+" "+(pass?"PASS ":"FAIL ")+name+" "+detail);}
        static void Tick()
        {
            if(preparing)
            {
                if(EditorApplication.isCompiling||EditorApplication.isUpdating){preparationTime=EditorApplication.timeSinceStartup;return;}
                if(EditorApplication.timeSinceStartup-preparationTime<3)return;
                preparing=false;EditorApplication.isPlaying=true;return;
            }
            if(!running||finishing||!EditorApplication.isPlaying||Time.frameCount==lastFrame)return;lastFrame=Time.frameCount;
            if(EditorApplication.timeSinceStartup-started>200){Check("Harness","Timeout",false,"200 seconds exceeded");Finish();return;}
            try
            {
                while(routines.Count>0)
                {
                    var routine=routines.Peek();if(!routine.MoveNext()){routines.Pop();continue;}
                    if(routine.Current is IEnumerator child){routines.Push(child);continue;}return;
                }
                Finish();
            }
            catch(Exception error){errors.Add(error.ToString());Check("Harness","Exception",false,error.Message);Finish();}
        }
        static IEnumerator Wait(double seconds){double until=EditorApplication.timeSinceStartup+seconds;while(EditorApplication.timeSinceStartup<until)yield return null;}
        static IEnumerator Idle(double seconds=6)
        {
            double until=EditorApplication.timeSinceStartup+seconds;
            while((journal.IsBusy||journal.cameraRig.IsTransitioning)&&EditorApplication.timeSinceStartup<until)yield return null;
            if(journal.IsBusy||journal.cameraRig.IsTransitioning)throw new TimeoutException("Journal did not settle.");yield return null;
        }
        static object Call(string name,params object[] args)
        {
            var candidates=scrapbook.GetType().GetMethods(BindingFlags.Public|BindingFlags.Instance).Where(m=>m.Name==name&&m.GetParameters().Length==args.Length).ToArray();
            if(candidates.Length!=1)throw new MissingMethodException("Unambiguous ScrapbookController."+name+" is required.");
            return candidates[0].Invoke(scrapbook,args);
        }
        static bool Bool(string name,params object[] args)=>Call(name,args) is bool value&&value;
        static JournalDecorationData Find(JournalSide side,string id)=>journal.Document.GetPage(side).decorations.FirstOrDefault(d=>d.instanceId==id);
        static string Snapshot(JournalPageData page)=>JsonUtility.ToJson(new DecorationList{items=page.decorations});
        [Serializable] sealed class DecorationList{public List<JournalDecorationData> items;}
        static void Transform(string kind,string setter,object value)
        {
            if(!Bool("BeginTransform",kind))throw new InvalidOperationException("Cannot begin "+kind+" transform.");
            Call(setter,value);Call("CommitTransform");
        }
        static bool BoundsValid(JournalDecorationData d)
        {
            var definition=scrapbook.Catalog.Find(d.assetId);if(definition==null)return false;
            Vector2 size=d.size.x>0&&d.size.y>0?d.size:definition.defaultSize;
            float width=size.x*d.scale.x*(definition.supportsLengthAdjustment?d.length:1)*.393f,height=size.y*d.scale.y*.52f;
            float a=d.rotationDegrees*Mathf.Deg2Rad,c=Mathf.Abs(Mathf.Cos(a)),s=Mathf.Abs(Mathf.Sin(a));
            float x=(c*width+s*height)/(.393f*2),y=(s*width+c*height)/(.52f*2);
            return !float.IsNaN(x)&&!float.IsInfinity(x)&&x<=.5005f&&y<=.5005f&&d.position.x>=x-.002f&&d.position.x<=1-x+.002f&&d.position.y>=y-.002f&&d.position.y<=1-y+.002f;
        }
        static IEnumerator Execute()
        {
            yield return Wait(.8);
            scrapbook=UnityEngine.Object.FindFirstObjectByType<ScrapbookController>();journal=UnityEngine.Object.FindFirstObjectByType<JournalController>();
            decorations=UnityEngine.Object.FindFirstObjectByType<DecorationRenderer>();desk=UnityEngine.Object.FindFirstObjectByType<DeskUI>();
            interaction=UnityEngine.Object.FindFirstObjectByType<DeskInteraction>();lens=Camera.main;
            Check("Harness","Actual Play",Application.isPlaying&&SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null,SystemInfo.graphicsDeviceName);
            Check("Harness","Actual resolution",Screen.width==1920&&Screen.height==1080,Screen.width+"x"+Screen.height);
            if(!scrapbook||!journal||!decorations||!desk||!interaction||!lens)throw new InvalidOperationException("Missing scrapbook runtime components.");
            if(!journal.SavePath.Contains("Stage3Validation"))throw new InvalidOperationException("Refusing to mutate normal player save: "+journal.SavePath);
            Check("Harness","Isolated persistent save",Path.GetFullPath(journal.SavePath).StartsWith(Path.GetFullPath(Application.persistentDataPath),StringComparison.OrdinalIgnoreCase),journal.SavePath);
            // An explicitly isolated fixture only. Never touches the ordinary player's document.
            Call("Cancel");journal.Document.currentSpread=0;foreach(var page in journal.Document.pages)page.decorations.Clear();journal.RefreshPages();decorations.Refresh();journal.Save();
            desk.Begin();yield return Wait(.3);Physics.SyncTransforms();
            foreach(var entry in new[]{new{test="T01",name="TapeRack",category=DecorationCategory.Tape},new{test="T02",name="StickerBox",category=DecorationCategory.Sticker},new{test="T03",name="NoteBox",category=DecorationCategory.Note}})
            {
                var item=UnityEngine.Object.FindObjectsByType<DeskItem>(FindObjectsSortMode.None).FirstOrDefault(i=>i.name==entry.name);
                Vector2 point;bool hit=item&&FindHit(item,out point);bool clicked=false;if(hit){FindHit(item,out point);clicked=interaction.TryActivateAt(point);}
                Check(entry.test,"Storage ray opens "+entry.category,clicked&&scrapbook.State.ToString()=="BrowsingAssets",item?item.name:"Missing desk storage");
                Call("Cancel");if(desk.IsBlocking)desk.Close();yield return Wait(.15);
            }
            var catalog=scrapbook.Catalog.assets;
            Check("T04","Usable material counts",catalog.Count(a=>a.category==DecorationCategory.Tape&&a.IsAvailable)>=8&&catalog.Count(a=>a.category==DecorationCategory.Sticker&&a.IsAvailable)>=12&&catalog.Count(a=>a.category==DecorationCategory.Note&&a.IsAvailable)>=6,"Tape/sticker/note definitions available");
            Check("T04","All thumbnails resolve",catalog.All(a=>a.thumbnail&&a.texture),catalog.Length+" resource definitions");
            var sticker=catalog.First(a=>a.category==DecorationCategory.Sticker&&a.IsAvailable);
            var sticker2=catalog.First(a=>a.category==DecorationCategory.Sticker&&a.IsAvailable&&a.assetId!=sticker.assetId);
            var tape=catalog.First(a=>a.category==DecorationCategory.Tape&&a.IsAvailable);
            var note=catalog.First(a=>a.category==DecorationCategory.Note&&a.IsAvailable);
            var edgeFixture=new JournalDecorationData{instanceId="edge-fixture",assetId=sticker.assetId,position=Vector2.zero,size=Vector2.zero,scale=Vector2.one};
            var hugeFixture=new JournalDecorationData{instanceId="huge-fixture",assetId=sticker.assetId,position=Vector2.one,size=new Vector2(3,3),scale=Vector2.one*.35f};
            journal.Document.GetPage(JournalSide.Left).decorations.Add(edgeFixture);journal.Document.GetPage(JournalSide.Left).decorations.Add(hugeFixture);
            scrapbook.NormalizeLoadedDecorations();
            Check("T23","Legacy edge and oversized records fit page",BoundsValid(edgeFixture)&&BoundsValid(hugeFixture),"Known legacy records normalized without deleting them; renderer minimum scale remains consistent.");
            journal.Document.GetPage(JournalSide.Left).decorations.Clear();decorations.Refresh();
            Call("Browse",DecorationCategory.Sticker);bool chose=Bool("Choose",sticker.assetId);yield return Idle();
            Check("T05","Desktop material choice enters editing",chose&&journal.IsEditing,"Selected from the actual desk-mode browser");
            Capture(null);yield return Wait(3);
            Check("T05","Choose enters placement",chose&&scrapbook.State.ToString()=="Placing",scrapbook.State.ToString());
            Vector2 uv;JournalSide side;
            var screen=lens.WorldToScreenPoint(journal.pageView.WorldFromNormalized(JournalSide.Left,new Vector2(.35f,.44f)));
            Check("T05","Preview uses page-plane coordinates",scrapbook.TryPageHit(screen,out side,out uv)&&side==JournalSide.Left&&Vector2.Distance(uv,new Vector2(.35f,.44f))<.01f,"Actual camera ray against independent page planes");
            // Batch Editor has no native focus: capture this actual renderer update before
            // the native-input watchdog correctly clears an unfocused placement ghost.
            decorations.SetPreview(sticker,JournalSide.Left,new Vector2(.35f,.44f),1,true);Capture("placement-preview.png");decorations.ClearPreview();
            bool leftPlaced=Bool("Place",JournalSide.Left,new Vector2(.35f,.44f));yield return Wait(.1);
            string leftId=scrapbook.SelectedId;
            Check("T06","Sticker on left",leftPlaced&&Find(JournalSide.Left,leftId)!=null&&Find(JournalSide.Left,leftId).pageId==journal.Document.GetPage(JournalSide.Left).pageId,leftId);
            int afterPlace=journal.Document.GetPage(JournalSide.Left).decorations.Count;
            for(int n=0;n<8;n++)Call("Place",JournalSide.Left,new Vector2(.36f,.45f));
            Check("T21","Repeated confirm creates no duplicates",journal.Document.GetPage(JournalSide.Left).decorations.Count==afterPlace,"One successful pending placement consumed once");
            Call("Browse",DecorationCategory.Sticker);Call("Choose",sticker2.assetId);Call("Place",JournalSide.Right,new Vector2(.5f,.55f));yield return Wait(.1);
            string rightId=scrapbook.SelectedId;
            Check("T07","Sticker on right independently",Find(JournalSide.Right,rightId)!=null&&Find(JournalSide.Left,leftId)!=null&&rightId!=leftId,rightId);
            Check("T09","Placed object can be reselected",Bool("Select",leftId,JournalSide.Left)&&scrapbook.SelectedData!=null&&scrapbook.State.ToString()=="Selected",scrapbook.SelectedId);
            var pageLeft=journal.Document.GetPage(JournalSide.Left);var beforeMove=Find(JournalSide.Left,leftId).position;
            int historyBefore=scrapbook.History.UndoCount(pageLeft.pageId);
            Call("BeginTransform","move");for(int n=0;n<30;n++)Call("MoveTo",Vector2.Lerp(beforeMove,new Vector2(.62f,.34f),(n+1)/30f));Call("CommitTransform");
            var moved=Find(JournalSide.Left,leftId).position;
            Check("T10","Move changes only selected element",Vector2.Distance(moved,beforeMove)>.05f&&Find(JournalSide.Right,rightId).position==new Vector2(.5f,.55f),moved.ToString());
            Check("T15","Whole drag records one command",scrapbook.History.UndoCount(pageLeft.pageId)==historyBefore+1,"Thirty drag updates, one committed history action");
            Call("Undo");Check("T15","Move undo",Vector2.Distance(Find(JournalSide.Left,leftId).position,beforeMove)<.0001f,"Prior position restored");
            Call("Redo");Check("T15","Move redo",Vector2.Distance(Find(JournalSide.Left,leftId).position,moved)<.0001f,"Committed position restored");
            Call("Select",leftId,JournalSide.Left);Transform("rotate","RotateTo",35f);
            Check("T11","Rotation",Mathf.Abs(Mathf.DeltaAngle(Find(JournalSide.Left,leftId).rotationDegrees,35))<.01f,"Around visual center");
            Call("Undo");Check("T15","Rotation undo",Mathf.Abs(Find(JournalSide.Left,leftId).rotationDegrees)<.01f,"Original angle");Call("Redo");
            Call("Select",leftId,JournalSide.Left);Transform("scale","ScaleTo",1.3f);
            Vector2 scaled=Find(JournalSide.Left,leftId).scale;
            Check("T12","Uniform scaling",Mathf.Abs(scaled.x-scaled.y)<.0001f&&scaled.x>1.01f,scaled.ToString());
            Call("Undo");Check("T15","Scale undo",Mathf.Abs(Find(JournalSide.Left,leftId).scale.x-1)<.001f,"Original scale");Call("Redo");
            Call("Select",leftId,JournalSide.Left);string beforeDelete=JsonUtility.ToJson(Find(JournalSide.Left,leftId));
            Check("T13","Delete selected",Bool("DeleteSelected")&&Find(JournalSide.Left,leftId)==null,"Only selected instance removed");
            Check("T14","Deletion undo",Bool("Undo")&&JsonUtility.ToJson(Find(JournalSide.Left,leftId))==beforeDelete,"Full instance identity and transform restored");
            Call("Browse",DecorationCategory.Tape);Call("Choose",tape.assetId);Call("Place",JournalSide.Left,new Vector2(.50f,.72f));string tapeId=scrapbook.SelectedId;
            Transform("length","SetLength",1.7f);yield return Wait(.15);
            Check("T08","Tape length independently adjustable",Find(JournalSide.Left,tapeId)!=null&&Mathf.Abs(Find(JournalSide.Left,tapeId).length-1.7f)<.02f&&tape.texture!=tape.thumbnail,"Uses continuous pattern, not product card");
            Capture("tape-length.png");Call("Undo");Check("T15","Tape length undo",Mathf.Abs(Find(JournalSide.Left,tapeId).length-1)<.001f,"Original cut length");Call("Redo");
            Call("Browse",DecorationCategory.Note);Call("Choose",note.assetId);Call("Place",JournalSide.Left,moved);string noteId=scrapbook.SelectedId;
            bool bottom=Bool("Layer","bottom");int bottomOrder=Find(JournalSide.Left,noteId).sortOrder;
            bool top=Bool("Layer","top");int topOrder=Find(JournalSide.Left,noteId).sortOrder;
            Check("T16","Overlapping layers reorder",bottom&&top&&topOrder>bottomOrder&&journal.Document.GetPage(JournalSide.Left).decorations.Select(d=>d.sortOrder).Distinct().Count()==journal.Document.GetPage(JournalSide.Left).decorations.Count,"Distinct per-page layer order");
            Call("Undo");Check("T16","Layer undo",Find(JournalSide.Left,noteId).sortOrder==bottomOrder,"Previous order restored");Call("Redo");
            Call("Select",leftId,JournalSide.Left);Call("BeginTransform","scale");Call("ScaleTo",1000f);Call("CommitTransform");
            Call("BeginTransform","rotate");Call("RotateTo",225f);Call("CommitTransform");Call("BeginTransform","move");Call("MoveTo",new Vector2(-10,10));Call("CommitTransform");
            Check("T23","Extreme rotated/scaled object stays inside page",BoundsValid(Find(JournalSide.Left,leftId)),JsonUtility.ToJson(Find(JournalSide.Left,leftId)));
            Call("Cancel");Capture("decorated-pages.png");
            string pageSnapshot=Snapshot(journal.Document.GetPage(JournalSide.Left));
            journal.Turn(1);Check("T22","No placement during page turn",!Bool("Place",JournalSide.Left,new Vector2(.5f,.5f)),"PageTurning/Transitioning rejects placement");yield return Idle();
            journal.Turn(-1);yield return Idle();
            Check("T17","Page turn preserves exact decoration transforms",Snapshot(journal.Document.GetPage(JournalSide.Left))==pageSnapshot,"Identity, UV, angle, size, length and order retained");
            var visual=decorations.GetVisual(noteId);if(!visual)throw new InvalidOperationException("Saved decoration has no live visual after returning to page.");
            Vector3 world=visual.position;journal.ZoomBy(.4f);yield return Wait(.4);journal.Pan(new Vector2(.1f,.05f));yield return Wait(.2);
            Check("T18","Zoom/pan keeps attachment to paper",Vector3.Distance(visual.position,world)<.002f&&journal.cameraRig.Zoom>1,"Camera changes, page-local decoration remains fixed");
            journal.ResetView();yield return Wait(.3);Call("Select",noteId,JournalSide.Left);Call("BeginTransform","move");
            Check("T22","Dragging locks page turn",!journal.Turn(1),"No competing flip during a live element transaction");Call("CancelTransform");Call("Cancel");
            string all=JsonUtility.ToJson(journal.Document);Check("T20a","Decorations save to actual JSON",journal.Save()&&File.Exists(journal.SavePath),journal.SavePath);
            var reloaded=new JournalSaveService(journal.Catalog,journal.SavePath).LoadOrCreate();
            Check("T20a","Actual disk reload keeps all decoration fields",JsonUtility.ToJson(reloaded)==all,"Fresh save-service read; process restart separately tested by native probe");
            journal.ExitEditing();yield return Idle();journal.EnterEditing();yield return Idle();
            Check("T19","Desk exit and reentry preserve creation",Snapshot(journal.Document.GetPage(JournalSide.Left))==pageSnapshot&&decorations.GetVisual(noteId),"Original page data and independent visuals retained");
            Capture("selected-material.png");journal.ExitEditing();yield return Idle();
            Check("T24","No blocking runtime errors",errors.Count==0,errors.Count+" errors/asserts/exceptions");
        }
        static bool FindHit(DeskItem item,out Vector2 point)
        {
            point=default;
            foreach(var collider in item.GetComponentsInChildren<Collider>())
            {
                var b=collider.bounds;
                for(int x=-2;x<=2;x++)for(int z=-2;z<=2;z++)
                {
                    var sample=lens.WorldToScreenPoint(b.center+new Vector3(b.extents.x*x/3,b.extents.y,b.extents.z*z/3));
                    if(sample.z<=0||sample.x<20||sample.y<20||sample.x>Screen.width-20||sample.y>Screen.height-20||desk.IsPointerOverInterface(sample))continue;
                    if(Physics.Raycast(lens.ScreenPointToRay(sample),out var hit,interaction.maxDistance)&&hit.collider.GetComponentInParent<DeskItem>()==item){point=sample;return true;}
                }
            }
            return false;
        }
        static void Capture(string name)
        {
            var previous=RenderTexture.active;var target=new RenderTexture(1920,1080,24);var texture=new Texture2D(1920,1080,TextureFormat.RGB24,false);bool asynchronous=ShaderUtil.allowAsyncCompilation;
            try{ShaderUtil.allowAsyncCompilation=false;var request=new RenderPipeline.StandardRequest{destination=target};if(!RenderPipeline.SupportsRenderRequest(lens,request))throw new InvalidOperationException("SRP capture unsupported.");RenderPipeline.SubmitRenderRequest(lens,request);RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,1920,1080),0,0);texture.Apply();if(name!=null)File.WriteAllBytes(Path.Combine(root,"docs/stage3-screenshots",name),texture.EncodeToPNG());}
            finally{ShaderUtil.allowAsyncCompilation=asynchronous;RenderTexture.active=previous;UnityEngine.Object.Destroy(texture);target.Release();UnityEngine.Object.Destroy(target);}
        }
        static void Finish()
        {
            if(finishing)return;finishing=true;running=false;SessionState.SetBool(Pending,false);Application.logMessageReceived-=CaptureLog;
            root=root??Directory.GetParent(Application.dataPath).FullName;
            var report=new Report{timestampUtc=DateTime.UtcNow.ToString("O"),mode="Actual Unity Editor Play Mode",inputScope="Actual Physics rays plus runtime operations. T20a is disk roundtrip; native input and fresh-process restart are separate tests.",screenshotScope="GPU rendered current game scene during Play, without IMGUI; native screenshots include controls.",unityVersion=Application.unityVersion,gpu=SystemInfo.graphicsDeviceName,graphicsApi=SystemInfo.graphicsDeviceType.ToString(),savePath=journal?journal.SavePath:"Unavailable",width=Screen.width,height=Screen.height,renderedFrames=Time.frameCount,actualPlayMode=Application.isPlaying,passed=results.Count>0&&results.All(r=>r.passed)&&errors.Count==0,checks=results.ToArray(),consoleErrors=errors.ToArray(),screenshots=new[]{"placement-preview.png","tape-length.png","decorated-pages.png","selected-material.png"}.Where(n=>File.Exists(Path.Combine(root,"docs/stage3-screenshots",n))&&File.GetLastWriteTimeUtc(Path.Combine(root,"docs/stage3-screenshots",n))>=startedUtc).ToArray()};
            File.WriteAllText(Path.Combine(root,"docs/stage3-logs/play-tests.json"),JsonUtility.ToJson(report,true));Debug.Log("[ScrapbookValidation] Completed "+(report.passed?"PASS":"FAIL"));
            if(Application.isBatchMode)EditorApplication.Exit(report.passed?0:1);else EditorApplication.isPlaying=false;
        }
        static void ConfigureFullHd()
        {
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;var assemblies=AppDomain.CurrentDomain.GetAssemblies();
            var gameType=assemblies.Select(a=>a.GetType("UnityEditor.GameView",false)).First(t=>t!=null);var sizesType=assemblies.Select(a=>a.GetType("UnityEditor.GameViewSizes",false)).First(t=>t!=null);
            var sizes=sizesType.GetProperty("instance",BindingFlags.Public|BindingFlags.Static|BindingFlags.FlattenHierarchy).GetValue(null);var group=sizesType.GetProperty("currentGroup",flags).GetValue(sizes);int count=(int)group.GetType().GetMethod("GetTotalCount",flags).Invoke(group,null),selected=-1;
            for(int index=0;index<count;index++){var size=group.GetType().GetMethod("GetGameViewSize",flags).Invoke(group,new object[]{index});if((int)size.GetType().GetProperty("width",flags).GetValue(size)==1920&&(int)size.GetType().GetProperty("height",flags).GetValue(size)==1080){selected=index;break;}}
            if(selected<0)throw new InvalidOperationException("Existing FullHD GameView preset unavailable.");var view=EditorWindow.GetWindow(gameType,false,"Game",false);view.Show();gameType.GetMethod("SizeSelectionCallback",flags).Invoke(view,new object[]{selected,null});gameType.BaseType.GetProperty("targetSize",flags).SetValue(view,new Vector2(1920,1080));view.Repaint();
        }
    }
}

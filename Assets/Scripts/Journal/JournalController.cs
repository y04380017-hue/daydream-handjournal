using System;
using System.IO;
using UnityEngine;
namespace VintageJournal
{
    public enum JournalMode { DeskMode, EditingMode, ZoomMode, Transitioning }
    [DefaultExecutionOrder(-20)]
    public sealed class JournalController : MonoBehaviour
    {
        public JournalPaperCatalog Catalog;
        public DeskCamera cameraRig;
        public JournalPageView pageView;
        public JournalFlipController flip;
        public JournalMode Mode {get;private set;} = JournalMode.DeskMode;
        public JournalDocument Document {get;private set;}
        public bool IsEditing => Mode!=JournalMode.DeskMode;
        public bool IsBusy => Mode==JournalMode.Transitioning;
        public bool IsExiting => exiting;
        public bool HasPaperPreview {get;private set;}
        public JournalSide PreviewSide {get;private set;}
        public string StatusMessage {get;private set;}
        public string SelectedPaperId {get;private set;}
        public JournalSaveService Store {get;private set;}
        public string SavePath => Store?.SavePath;
        public int SpreadCount => Document.SpreadCount;
        JournalDocument previewOriginal;
        bool exiting,ready;
        DeskUI deskUI;
        ScrapbookController scrapbook;
        void Start()
        {
            if(!Catalog)Catalog=JournalPaperCatalog.Load();
            if(!cameraRig)cameraRig=FindFirstObjectByType<DeskCamera>();
            deskUI=FindFirstObjectByType<DeskUI>();
            scrapbook=GetComponent<ScrapbookController>();
            if(!pageView)pageView=GetComponent<JournalPageView>();
            if(!flip)flip=GetComponent<JournalFlipController>();
            pageView.Initialize();string testPath=null;
            foreach(string arg in Environment.GetCommandLineArgs())
                if(arg=="--scrapbook-validation")testPath=Path.Combine(Application.persistentDataPath,"Stage3Validation","journal-v1.json");
            foreach(string arg in Environment.GetCommandLineArgs())
                if(arg=="--journal-validation")testPath=Path.Combine(Application.persistentDataPath,"Stage2Validation","journal-v1.json");
            foreach(string arg in Environment.GetCommandLineArgs())
                if(arg=="--physical-validation")testPath=Path.Combine(Application.persistentDataPath,"Stage4Validation","journal-v1.json");
            foreach(string arg in Environment.GetCommandLineArgs())
                if(arg=="--workspace-validation")testPath=Path.Combine(Application.persistentDataPath,"Stage4BValidation","journal-v1.json");
            foreach(string arg in Environment.GetCommandLineArgs())
                if(arg=="--workspace-native-validation")testPath=Path.Combine(Application.persistentDataPath,"Stage4BNativeValidation","journal-v1.json");
            foreach(string arg in Environment.GetCommandLineArgs())
                if(arg=="--commission-validation")testPath=Path.Combine(Application.persistentDataPath,"CommissionValidation","journal-v1.json");
            foreach(string arg in Environment.GetCommandLineArgs())
                if(arg=="--commission-native-validation")testPath=Path.Combine(Application.persistentDataPath,"CommissionNativeValidation","journal-v1.json");
            foreach(string arg in Environment.GetCommandLineArgs())
                if(arg=="--desk-revision-validation")testPath=Path.Combine(Application.persistentDataPath,"DeskRevisionValidation","journal-v1.json");
            if(testPath!=null && System.Array.IndexOf(Environment.GetCommandLineArgs(),"--desk-revision-validation")>=0)
                foreach(var arg in Environment.GetCommandLineArgs())
                    if(arg.StartsWith("--revision-run=") && Guid.TryParseExact(arg.Substring(15),"N",out Guid runId))
                        testPath=Path.Combine(Application.persistentDataPath,"DeskRevisionValidation",runId.ToString("N"),"journal-v1.json");
            if(testPath==null)
                foreach(var arg in Environment.GetCommandLineArgs())
                    if(arg.StartsWith("--atelier-profile=",StringComparison.Ordinal))
                    {
                        string profile=arg.Substring("--atelier-profile=".Length);
                        if(!System.Text.RegularExpressions.Regex.IsMatch(profile,@"\A[A-Za-z0-9_-]{1,32}\z"))
                            throw new ArgumentException("Profile names use 1–32 letters, digits, underscores or hyphens.");
                        testPath=Path.Combine(Application.persistentDataPath,"AtelierProfiles",profile,"journal-v1.json");
                    }
            Store=new JournalSaveService(Catalog,testPath);Document=Store.LoadOrCreate();StatusMessage=Store.LastMessage;
            ready=true;RefreshPages();
            // Existing isolated legacy probes compare their synthetic snapshot. Real diaries,
            // custom PNGs and local social credentials never enter the normal player log.
            bool isolatedDiagnostic=Array.Exists(Environment.GetCommandLineArgs(),a=>a.Contains("validation"));
            if(isolatedDiagnostic&&Document.personalStudio==null&&Document.atelierSocial==null)
                Debug.Log("[Journal] Loaded:"+JsonUtility.ToJson(Document));
            else Debug.Log("[Journal] LoadedSummary: schema="+Document.schemaVersion+"; pages="+Document.PageCount);
            Debug.Log("[Journal] SavePath:"+SavePath);
        }
        public bool EnterEditing()
        {
            if(RuntimeInputGate.BlocksJournalOperations)return false;
            if(!ready||IsEditing||!cameraRig||cameraRig.IsTransitioning)return false;
            if(deskUI)deskUI.ConsumePointer();
            exiting=false;pageView.SetEditing(true);SetMode(JournalMode.Transitioning);cameraRig.Inspect();
            Debug.Log("[Journal] Enter");return true;
        }
        public void ExitEditing()
        {
            if(!IsEditing||exiting)return;
            if(scrapbook)scrapbook.OnExitEditing();
            CancelPaper();if(flip.IsFlipping)flip.CancelFlip(true);Save();exiting=true;SetMode(JournalMode.Transitioning);
            if(deskUI)deskUI.ConsumePointer();cameraRig.Return();Debug.Log("[Journal] Exit");
        }
        void Update()
        {
            if(!ready)return;
            if(Mode==JournalMode.Transitioning&&!cameraRig.IsTransitioning&&!flip.IsFlipping)
            {
                if(exiting){exiting=false;pageView.SetEditing(false);SetMode(JournalMode.DeskMode);}
                else SetMode(cameraRig.Zoom>1.01f?JournalMode.ZoomMode:JournalMode.EditingMode);
            }
        }
        public bool Turn(int direction)
        {
            if(RuntimeInputGate.BlocksJournalOperations)return false;
            if(scrapbook&&scrapbook.BlocksPageOperations)return false;
            if(!IsEditing||IsBusy||HasPaperPreview||(direction!=1&&direction!=-1))return false;
            int next=Document.ActiveSpread+direction;
            if(next<0||next>=SpreadCount){Debug.Log("[Journal] Flip:Boundary");return false;}
            if(scrapbook){if(!scrapbook.FlushSaves())return false;}else Save();
            cameraRig.ResetView();int old=Document.ActiveSpread;
            var front=Catalog.Find(Document.ActivePages[old*2+(direction>0?1:0)].paperId).texture;
            var back=Catalog.Find(Document.ActivePages[next*2+(direction>0?0:1)].paperId).texture;
            SetMode(JournalMode.Transitioning);
            bool started=flip.StartFlip(direction,front,back,()=>{Document.ActiveSpread=next;RefreshPages();},()=>
            {SetMode(exiting?JournalMode.Transitioning:JournalMode.EditingMode);AutoSave();Debug.Log("[Journal] Flip:Complete:"+Document.ActiveSpread);});
            if(!started){SetMode(JournalMode.EditingMode);return false;}
            return true;
        }
        public void BeginPaperSelection(JournalSide side)
        {
            if(RuntimeInputGate.BlocksJournalOperations)return;
            if(scrapbook&&scrapbook.BlocksPageOperations)return;
            if(!IsEditing||IsBusy||HasPaperPreview||Document.ActiveNotebook!=null)return;
            cameraRig.ResetView();SetMode(JournalMode.EditingMode);
            cameraRig.LayoutOffsetX=-.10f;
            previewOriginal=Document.DeepClone();HasPaperPreview=true;PreviewSide=side;
            SelectedPaperId=Document.GetPage(side).paperId;Debug.Log("[Journal] Picker:Open");
        }
        public void SelectPaper(string id)
        {
            if(!HasPaperPreview||IsBusy||Catalog.Find(id)==null)return;
            SelectedPaperId=id;Document.GetPage(PreviewSide).paperId=id;RefreshPages();Debug.Log("[Journal] Preview:"+PreviewSide+":"+id);
        }
        public void SetPreviewSide(JournalSide side)
        {
            if(!HasPaperPreview||PreviewSide==side)return;
            RestorePreviewPaper(Document);PreviewSide=side;SelectPaper(SelectedPaperId);
        }
        public void ConfirmPaper()
        {if(!HasPaperPreview)return;HasPaperPreview=false;previewOriginal=null;cameraRig.LayoutOffsetX=0;AutoSave();Debug.Log("[Journal] Paper:Confirm:"+PreviewSide+":"+SelectedPaperId);}
        public void CancelPaper()
        {if(!HasPaperPreview)return;RestorePreviewPaper(Document);previewOriginal=null;HasPaperPreview=false;cameraRig.LayoutOffsetX=0;RefreshPages();Debug.Log("[Journal] Paper:Cancel");}
        void RestorePreviewPaper(JournalDocument target)
        {
            if(previewOriginal==null||target==null)return;
            foreach(var page in target.AllPages)
                foreach(var original in previewOriginal.AllPages)
                    if(original.pageId==page.pageId){page.paperId=original.paperId;break;}
        }
        // Async social receipts may arrive while a paper preview is open. Only the previewed
        // paper is provisional; independently committed ownership, messages and rewards survive.
        public JournalDocument DocumentForWorkspaceSave(JournalDocument document)
        {if(!HasPaperPreview)return document;var copy=document.DeepClone();RestorePreviewPaper(copy);return copy;}
        public void ZoomBy(float delta)
        {
            if(scrapbook&&scrapbook.BlocksPageOperations)return;
            if(!IsEditing||IsBusy||HasPaperPreview||float.IsNaN(delta)||float.IsInfinity(delta))return;
            cameraRig.ZoomBy(delta);SetMode(cameraRig.Zoom>1.01f?JournalMode.ZoomMode:JournalMode.EditingMode);
            Debug.Log("[Journal] Zoom:"+cameraRig.Zoom.ToString("F2",System.Globalization.CultureInfo.InvariantCulture));
        }
        public void ResetView()
        {if(!IsEditing||IsBusy)return;cameraRig.ResetView();SetMode(JournalMode.EditingMode);Debug.Log("[Journal] Reset");}
        public void Pan(Vector2 delta)
        {
            if(scrapbook&&scrapbook.BlocksPageOperations)return;
            if(Mode!=JournalMode.ZoomMode||IsBusy||HasPaperPreview||float.IsNaN(delta.x)||float.IsInfinity(delta.x)||float.IsNaN(delta.y)||float.IsInfinity(delta.y))return;
            cameraRig.Pan(delta);Debug.Log("[Journal] Pan:"+cameraRig.PanOffset);
        }
        public void RefreshPages()
        {GetComponent<NotebookWorkbench>()?.RefreshPresentation();pageView.Apply(Catalog.Find(Document.GetPage(JournalSide.Left).paperId).texture,Catalog.Find(Document.GetPage(JournalSide.Right).paperId).texture);if(scrapbook&&scrapbook.visuals)scrapbook.visuals.Refresh();}
        public void ApplyWorkspaceDocument(JournalDocument document)
        {
            if(document==null)throw new ArgumentNullException(nameof(document));
            Document=document;
            if(ready)RefreshPages();
        }
        public bool Save()
        {if(!ready)return false;var timer=System.Diagnostics.Stopwatch.StartNew();var doc=DocumentForWorkspaceSave(Document);bool ok=scrapbook?scrapbook.SaveNow(scrapbook.DocumentForSave(doc)):Store.Save(doc);StatusMessage=Store.LastMessage;Debug.Log("[Journal] Save:"+ok);if(scrapbook)Debug.Log("[Scrapbook] SaveTimeMs:"+timer.Elapsed.TotalMilliseconds.ToString("F2",System.Globalization.CultureInfo.InvariantCulture));return ok;}
        public bool AutoSave(){if(!ready)return false;if(!scrapbook)return Save();return scrapbook.QueueSave(scrapbook.DocumentForSave(DocumentForWorkspaceSave(Document)));}
        public void RefreshSaveStatus(){if(Store!=null)StatusMessage=(scrapbook&&scrapbook.SaveFailed?"保存失败：":"")+Store.LastMessage;}
        void SetMode(JournalMode mode){if(Mode==mode)return;Mode=mode;Debug.Log("[Journal] Mode:"+mode);}
        void OnApplicationPause(bool pause){if(pause)Save();}
        void OnApplicationFocus(bool focus){if(!focus)Save();}
        void OnApplicationQuit(){Save();}
    }
}

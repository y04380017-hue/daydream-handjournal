using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace VintageJournal
{
    public enum ScrapbookState { Idle,BrowsingAssets,Placing,Selected,Dragging,Rotating,Scaling,PageTurning,Transitioning }
    [DefaultExecutionOrder(-10)]
    public sealed class ScrapbookController:MonoBehaviour
    {
        public JournalController journal;
        public DecorationCatalog Catalog;
        public DecorationRenderer visuals;
        public DecorationHistory History {get;private set;}=new DecorationHistory();
        public CraftWorkspaceService CraftService {get;set;}
        public ScrapbookState State {get;private set;}=ScrapbookState.Idle;
        public JournalSide ActiveSide {get;private set;}=JournalSide.Left;
        public string SelectedId {get;private set;}
        public DecorationCategory Category {get;private set;}
        public DecorationDefinition Chosen {get;private set;}
        public JournalDecorationData Ghost {get;private set;}
        public bool BrowserOpen=>State==ScrapbookState.BrowsingAssets;
        public bool IsTransforming=>State==ScrapbookState.Dragging||State==ScrapbookState.Rotating||State==ScrapbookState.Scaling;
        public bool BlocksPageOperations=>BrowserOpen||Chosen!=null||SelectedId!=null||IsTransforming;
        public bool InterceptsInput=>BlocksPageOperations;
        public JournalDecorationData SelectedData=>CurrentPage?.decorations.Find(d=>d.instanceId==SelectedId);
        public JournalPageData CurrentPage=>journal&&journal.Document!=null?journal.Document.GetPage(ActiveSide):null;
        public int InputBlockedUntilFrame {get;private set;}
        int consumedFrame=-1;
        public bool InputConsumedThisFrame=>consumedFrame==Time.frameCount;
        public void ConsumeInput(){consumedFrame=Time.frameCount;}
        List<JournalDecorationData> before;
        string transformKind;
        bool pendingPlacement;
        DeskUI desk;
        DecorationSaveQueue saveQueue;
        public bool SavePending=>saveQueue!=null&&saveQueue.IsPending;
        public bool SaveFailed=>saveQueue!=null&&!saveQueue.LastSucceeded;
        void Start()
        {
            if(!journal)journal=GetComponent<JournalController>();if(!Catalog)Catalog=DecorationCatalog.Load();if(!visuals)visuals=GetComponent<DecorationRenderer>();
            desk=FindFirstObjectByType<DeskUI>();saveQueue=new DecorationSaveQueue(journal.Store);visuals.journal=journal;visuals.catalog=Catalog;NormalizeLoadedDecorations();visuals.Refresh();
            Debug.Log("[Scrapbook] Ready:"+(Catalog?Catalog.assets.Length:0));
        }
        void Update()
        {
            if(!journal||journal.Document==null)return;
            if(saveQueue!=null){saveQueue.Tick();journal.RefreshSaveStatus();}
            if(journal.IsBusy)
            {
                if(!IsTransforming)SetState(journal.flip.IsFlipping?ScrapbookState.PageTurning:ScrapbookState.Transitioning);
            }
            else if(State==ScrapbookState.PageTurning||State==ScrapbookState.Transitioning)
            {if(pendingPlacement&&Chosen!=null){pendingPlacement=false;SetState(ScrapbookState.Placing);}else SetState(SelectedId!=null?ScrapbookState.Selected:ScrapbookState.Idle);visuals.Refresh();}
        }
        public void NormalizeLoadedDecorations()
        {
            if(!journal||journal.Document==null||!Catalog)return;
            for(int i=0;i<journal.Document.pages.Length;i++)
                foreach(var d in journal.Document.pages[i].decorations)
                {
                    var def=Catalog.Find(d.assetId);if(def==null||!def.IsAvailable||d.physicalMaterial)continue;
                    DecorationGeometry.Constrain(def,d,journal.pageView.SurfaceSize(i%2==0?JournalSide.Left:JournalSide.Right));
                }
        }
        public void NotifyDeskCatalog(string category)
        {
            if(journal&&journal.IsEditing)return;
            if(category!="tape"&&category!="sticker"&&category!="note")return;
            Category=category=="tape"?DecorationCategory.Tape:category=="sticker"?DecorationCategory.Sticker:DecorationCategory.Note;
            SetState(ScrapbookState.BrowsingAssets);Debug.Log("[Scrapbook] Browse:"+Category);
        }
        public void Browse(DecorationCategory category)
        {
            if(!journal||journal.IsBusy||journal.HasPaperPreview)return;
            Cancel();Category=category;
            if(!journal.IsEditing){desk.OpenCategory(category==DecorationCategory.Tape?"tape":category==DecorationCategory.Sticker?"sticker":"note");NotifyDeskCatalog(category.ToString().ToLowerInvariant());}
            else {journal.ResetView();journal.cameraRig.LayoutOffsetX=-.10f;SetState(ScrapbookState.BrowsingAssets);Debug.Log("[Scrapbook] Browse:"+Category);}
            BlockClick();
        }
        public void CloseBrowser()
        {
            if(BrowserOpen){SetState(ScrapbookState.Idle);if(journal)journal.cameraRig.LayoutOffsetX=0;}
            visuals?.ClearPreview();BlockClick();
        }
        public bool Choose(string id)
        {
            var def=Catalog?Catalog.Find(id):null;if(def==null||!def.IsAvailable||journal.IsBusy||journal.HasPaperPreview)return false;
            if(IsTransforming)CancelTransform();if(!journal.IsEditing){desk.Close();if(!journal.EnterEditing())return false;pendingPlacement=true;}
            journal.cameraRig.LayoutOffsetX=0;SelectedId=null;Chosen=def;
            Ghost=new JournalDecorationData{assetId=id,type=def.category.ToString(),position=new Vector2(.5f,.5f),size=def.defaultSize,scale=Vector2.one,length=1};
            SetState(pendingPlacement?ScrapbookState.Transitioning:ScrapbookState.Placing);BlockClick();visuals.SetSelected(null);Debug.Log("[Scrapbook] Choose:"+id);return true;
        }
        public void PreviewAt(JournalSide side,Vector2 uv,bool valid)
        {
            if(Chosen==null||Ghost==null||journal.IsBusy)return;ActiveSide=side;Ghost.position=uv;
            DecorationGeometry.Constrain(Chosen,Ghost,journal.pageView.SurfaceSize(side));visuals.SetPreviewData(Chosen,side,Ghost,valid);
        }
        public bool Place(JournalSide side,Vector2 uv)
        {
            if(State!=ScrapbookState.Placing||Chosen==null||journal.IsBusy||uv.x<0||uv.x>1||uv.y<0||uv.y>1)return false;
            ActiveSide=side;var page=CurrentPage;var old=DecorationHistory.Clone(page.decorations);
            var d=JsonUtility.FromJson<JournalDecorationData>(JsonUtility.ToJson(Ghost));d.instanceId=Guid.NewGuid().ToString("N");d.pageId=page.pageId;d.position=uv;
            d.sortOrder=page.decorations.Count==0?0:page.decorations.Max(x=>x.sortOrder)+1;DecorationGeometry.Constrain(Chosen,d,journal.pageView.SurfaceSize(side));
            page.decorations.Add(d);History.Record(page,old,"添加素材");Chosen=null;Ghost=null;visuals.ClearPreview();SelectedId=d.instanceId;SetState(ScrapbookState.Selected);Changed();
            Debug.Log("[Scrapbook] Place:"+page.pageId+":"+d.instanceId);return true;
        }
        public bool Select(string id,JournalSide side)
        {
            if(journal.IsBusy||Chosen!=null||BrowserOpen)return false;
            if(!journal.Document.GetPage(side).decorations.Any(x=>x.instanceId==id))return false;
            ActiveSide=side;SelectedId=id;SetState(ScrapbookState.Selected);visuals.SetSelected(id);Debug.Log("[Scrapbook] Select:"+id);return true;
        }
        public void Deselect(){SelectedId=null;visuals.SetSelected(null);SetState(ScrapbookState.Idle);}
        public void SetActiveSide(JournalSide side){if(!BlocksPageOperations)ActiveSide=side;}
        public bool BeginTransform(string kind)
        {
            if(State!=ScrapbookState.Selected||SelectedData==null||journal.IsBusy)return false;
            transformKind=kind;before=DecorationHistory.Clone(CurrentPage.decorations);
            SetState(kind=="move"?ScrapbookState.Dragging:kind=="rotate"?ScrapbookState.Rotating:ScrapbookState.Scaling);return true;
        }
        public bool MoveTo(Vector2 uv){if(!IsTransforming||SelectedData==null||!Finite(uv.x)||!Finite(uv.y))return false;return MutateSelection(d=>d.position=uv);}
        public bool RotateTo(float angle){if(!IsTransforming||SelectedData==null||!Finite(angle))return false;return MutateSelection(d=>d.rotationDegrees=angle);}
        public bool ScaleTo(float factor){if(!IsTransforming||SelectedData==null||!Finite(factor))return false;return MutateSelection(d=>d.scale=Vector2.one*factor);}
        public bool SetLength(float factor)
        {
            if(!Finite(factor))return false;
            if(Chosen!=null&&Chosen.supportsLengthAdjustment){Ghost.length=Mathf.Clamp(factor,.3f,2.5f);return true;}
            if(!IsTransforming||SelectedData==null||!Catalog.Find(SelectedData.assetId).supportsLengthAdjustment)return false;
            bool changed=MutateSelection(d=>d.length=factor);if(changed)Debug.Log("[Scrapbook] Length:"+SelectedData.length);return changed;
        }
        bool MutateSelection(Action<JournalDecorationData> mutate)
        {
            var data=SelectedData;if(data==null)return false;
            var previous=data.physicalMaterial?JsonUtility.FromJson<JournalDecorationData>(JsonUtility.ToJson(data)):null;
            mutate(data);
            if(data.physicalMaterial&&!CraftWorkspaceData.PhysicalDecorationFits(data))
            {
                int index=CurrentPage.decorations.IndexOf(data);CurrentPage.decorations[index]=previous;
                visuals.Refresh();visuals.SetSelected(SelectedId);return false;
            }
            ConstrainSelection();return true;
        }
        void ConstrainSelection(){var d=SelectedData;DecorationGeometry.Constrain(Catalog.Find(d.assetId),d,journal.pageView.SurfaceSize(ActiveSide));visuals.Refresh();visuals.SetSelected(SelectedId);}
        public void CommitTransform()
        {
            if(!IsTransforming)return;History.Record(CurrentPage,before,transformKind);before=null;SetState(ScrapbookState.Selected);Changed();Debug.Log("[Scrapbook] Commit:"+transformKind);
        }
        public void CancelTransform(){if(!IsTransforming)return;CurrentPage.decorations=DecorationHistory.Clone(before);before=null;SetState(ScrapbookState.Selected);visuals.Refresh();}
        public bool DeleteSelected()
        {
            if(State!=ScrapbookState.Selected||SelectedData==null)return false;var old=DecorationHistory.Clone(CurrentPage.decorations);string id=SelectedId;
            CurrentPage.decorations.RemoveAll(x=>x.instanceId==id);History.Record(CurrentPage,old,"删除素材");Deselect();Changed();Debug.Log("[Scrapbook] Delete:"+id);return true;
        }
        public bool Layer(string op)
        {
            if(State!=ScrapbookState.Selected||SelectedData==null)return false;var page=CurrentPage;var old=DecorationHistory.Clone(page.decorations);
            var sorted=page.decorations.OrderBy(d=>d.sortOrder).ThenBy(d=>d.instanceId).ToList();var d=SelectedData;int from=sorted.IndexOf(d),to=from;
            if(op=="top")to=sorted.Count-1;else if(op=="bottom")to=0;else if(op=="up")to=Mathf.Min(sorted.Count-1,from+1);else if(op=="down")to=Mathf.Max(0,from-1);else return false;
            sorted.RemoveAt(from);sorted.Insert(to,d);for(int i=0;i<sorted.Count;i++)sorted[i].sortOrder=i;History.Record(page,old,"调整图层");Changed();Debug.Log("[Scrapbook] Layer:"+op);return true;
        }
        public bool Undo(){if(IsTransforming){CancelTransform();return true;}if(Chosen!=null||BrowserOpen||journal.IsBusy)return false;bool ok=CraftService!=null?CraftService.Undo():History.Undo(CurrentPage);if(ok){Deselect();Changed();Debug.Log("[Scrapbook] Undo");}return ok;}
        public bool Redo(){if(BlocksPageOperations&&State!=ScrapbookState.Selected||journal.IsBusy)return false;bool ok=CraftService!=null?CraftService.Redo():History.Redo(CurrentPage);if(ok){Deselect();Changed();Debug.Log("[Scrapbook] Redo");}return ok;}
        public bool Cancel()
        {
            if(IsTransforming){CancelTransform();return true;}
            if(Chosen!=null){Chosen=null;Ghost=null;pendingPlacement=false;visuals.ClearPreview();SetState(ScrapbookState.Idle);BlockClick();return true;}
            if(BrowserOpen){CloseBrowser();return true;}if(SelectedId!=null){Deselect();return true;}return false;
        }
        public void OnExitEditing(){while(Cancel()){}visuals.ClearPreview();journal.cameraRig.LayoutOffsetX=0;}
        public JournalDocument DocumentForSave(JournalDocument doc)
        {if(!IsTransforming||before==null)return doc;var copy=doc.DeepClone();var page=copy.AllPages.FirstOrDefault(x=>x.pageId==CurrentPage.pageId);if(page!=null)page.decorations=DecorationHistory.Clone(before);return copy;}
        public bool TryPageHit(Vector2 screen,out JournalSide side,out Vector2 uv)
        {foreach(JournalSide s in new[]{JournalSide.Left,JournalSide.Right})if(UvOnSide(screen,s,out uv)&&uv.x>=0&&uv.y>=0&&uv.x<=1&&uv.y<=1){side=s;return true;}side=JournalSide.Left;uv=Vector2.zero;return false;}
        public bool UvOnSide(Vector2 screen,JournalSide side,out Vector2 uv)
        {
            var anchor=journal.pageView.GetSurface(side);var ray=journal.cameraRig.Lens.ScreenPointToRay(screen);var plane=new Plane(anchor.forward,anchor.position);
            if(plane.Raycast(ray,out float t)){Vector3 p=anchor.InverseTransformPoint(ray.GetPoint(t));Vector2 size=journal.pageView.SurfaceSize(side);uv=new Vector2(p.x/size.x,p.y/size.y);return true;}uv=Vector2.zero;return false;
        }
        public string HitDecoration(JournalSide side,Vector2 uv)
        {return journal.Document.GetPage(side).decorations.OrderByDescending(d=>d.sortOrder).ThenByDescending(d=>d.instanceId).FirstOrDefault(d=>Catalog.Find(d.assetId)!=null&&DecorationGeometry.Hit(Catalog.Find(d.assetId),d,uv,journal.pageView.SurfaceSize(side)))?.instanceId;}
        void Changed()
        {
            if(CraftService!=null&&!CraftService.SynchronizeJournalProjection())return;
            visuals.Refresh();visuals.SetSelected(SelectedId);
            var timer=System.Diagnostics.Stopwatch.StartNew();
            bool accepted=QueueSave(DocumentForSave(journal.Document));
            Debug.Log("[Scrapbook] AutoSavePrepareMs:"+timer.Elapsed.TotalMilliseconds.ToString("F2",System.Globalization.CultureInfo.InvariantCulture));
            if(!accepted)journal.RefreshSaveStatus();
        }
        public bool FlushSaves(){return saveQueue==null||saveQueue.Flush();}
        public bool QueueSave(JournalDocument doc){return saveQueue!=null?saveQueue.Enqueue(doc):journal.Store.Save(doc);}
        public bool SaveNow(JournalDocument doc){return saveQueue!=null?saveQueue.Enqueue(doc)&&saveQueue.Flush():journal.Store.Save(doc);}
        public void BlockClick(){InputBlockedUntilFrame=Time.frameCount+2;}
        void SetState(ScrapbookState value){if(State==value)return;State=value;Debug.Log("[Scrapbook] State:"+value);}
        static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
    }
}

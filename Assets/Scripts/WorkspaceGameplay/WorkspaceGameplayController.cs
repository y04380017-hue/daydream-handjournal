using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VintageJournal
{
    /// <summary>Input-independent workspace actions. All material ownership changes go through one save transaction.</summary>
    public sealed class WorkspaceGameplayController : MonoBehaviour
    {
        public JournalController journal;
        public ScrapbookController scrapbook;
        public PhysicalInteractionController physical;
        public WorkspacePhysicalCoordinator coordinator;
        public DeskUI desk;
        public Camera lens;
        public ReleaseBookView[] releaseBooks=Array.Empty<ReleaseBookView>();
        public CraftMaterialRenderer materialRenderer;
        public WorkspaceGameplayHUD hud;
        public BookInspectionPresentation bookInspection;
        public CraftWorkspaceService Service {get;private set;}
        public PhysicalInteractable SelectedTape {get;private set;}
        public PhysicalInteractable SelectedPhysical {get;private set;}
        public int SelectionRevision {get;private set;}
        public string SelectedMaterialId {get;private set;}
        public string ActiveBookId {get;private set;}
        public bool TapeLengthSelecting {get;private set;}
        public float TapeLengthMeters {get;private set;}=.05f;
        public float minimumTapeLength=.02f,maximumTapeLength=.30f,defaultTapeLength=.05f;
        public Vector3 TapePreviewOrigin {get;private set;}
        public float TearAnimationUntil {get;private set;}
        public string Status {get;private set;}="正在准备工作台…";
        public CraftPlacement PreviewPlacement {get;private set;}
        public Vector3 PointerWorld {get;private set;}
        public bool PreviewFits {get;private set;}
        public bool Ready=>Service!=null;
        public bool InteractionBusy=>TapeLengthSelecting||(Ready&&Service.HeldItem!=null)||(physical&&(physical.HeldItem||physical.IsSettling));
        public bool ConsumesLegacyInput=>InteractionBusy||SelectedMaterialIsReleaseBook;
        public bool SelectedMaterialIsReleaseBook=>Ready&&Service.GetItem(SelectedMaterialId)?.owner==CraftOwner.ReleaseBookPage;
        public bool CanNavigateTargetBook
        {
            get
            {
                var input=GetComponent<WorkspaceInputRouter>();
                return Ready&&!TapeLengthSelecting&&!(physical&&(physical.HeldItem||physical.IsSettling))&&!(input&&input.IsDraggingMechanism);
            }
        }
        Vector3 lengthPointerStart;
        bool ready;

        IEnumerator Start()
        {
            if(!journal)journal=FindFirstObjectByType<JournalController>();
            if(!scrapbook)scrapbook=FindFirstObjectByType<ScrapbookController>();
            if(!physical)physical=FindFirstObjectByType<PhysicalInteractionController>();
            if(!coordinator)coordinator=FindFirstObjectByType<WorkspacePhysicalCoordinator>();
            if(!desk)desk=FindFirstObjectByType<DeskUI>();
            if(!lens)lens=Camera.main;
            while(journal&&journal.Document==null)yield return null;
            if(!journal||!scrapbook||!physical||!coordinator){Status="工作台绑定缺失，请检查场景。";Debug.LogError("[Workspace] Missing required scene bindings.");yield break;}
            Service=new CraftWorkspaceService(journal,scrapbook);
            PersonalStudioAssets.Bind(this);SocialMaterialAssets.Bind(this);
            releaseBooks=FindObjectsByType<ReleaseBookView>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            coordinator.Service=Service;coordinator.RegisterScene();coordinator.ApplySavedState();
            Service.DataChanged+=RefreshViews;
            if(!materialRenderer)materialRenderer=GetComponent<CraftMaterialRenderer>();
            if(!materialRenderer)materialRenderer=gameObject.AddComponent<CraftMaterialRenderer>();
            materialRenderer.workspace=this;
            if(!hud)hud=GetComponent<WorkspaceGameplayHUD>();
            if(!hud)hud=gameObject.AddComponent<WorkspaceGameplayHUD>();hud.workspace=this;
            if(!GetComponent<DreamGameplayController>())gameObject.AddComponent<DreamGameplayController>().workspace=this;
            if(!GetComponent<AtelierHub>())gameObject.AddComponent<AtelierHub>().workspace=this;
            if(!GetComponent<AtelierUsageTracker>())gameObject.AddComponent<AtelierUsageTracker>().workspace=this;
            bookInspection=GetComponent<BookInspectionPresentation>();
            if(!bookInspection)bookInspection=gameObject.AddComponent<BookInspectionPresentation>();bookInspection.workspace=this;
            ready=true;RefreshViews();Status="左键选中保持原位，右拖移动；离型本左拖桌面、右键打开；胶带向前拉选长度。";
            Debug.Log("[Workspace] Ready:"+Service.Items.Count+" items; "+Service.Books.Count+" release books.");
        }

        public bool Submit(WorkspaceIntent intent)
        {
            if(!Ready)return false;
            switch(intent.kind)
            {
                case WorkspaceIntentKind.Select:return intent.target?SelectPhysical(intent.target):SelectMaterial(intent.instanceId);
                case WorkspaceIntentKind.Grab:return intent.target?GrabPhysical(intent.target):BeginPeel(intent.instanceId);
                case WorkspaceIntentKind.Move:
                    if(Service.HeldItem!=null){PointerWorld=intent.pose.position;return true;}
                    return physical.Submit(new PhysicalInteractionIntent(PhysicalIntentKind.Move,physical.HeldItem,intent.pose));
                case WorkspaceIntentKind.Place:
                case WorkspaceIntentKind.Attach:
                case WorkspaceIntentKind.Transfer:return intent.placement!=null?Attach(intent.placement):false;
                case WorkspaceIntentKind.Return:return ReturnSelected();
                case WorkspaceIntentKind.Open:return OpenBook(intent.instanceId,true);
                case WorkspaceIntentKind.Close:return OpenBook(intent.instanceId,false);
                case WorkspaceIntentKind.Peel:return BeginPeel(intent.instanceId);
                case WorkspaceIntentKind.Cancel:return Cancel();
                case WorkspaceIntentKind.TapeLengthStart:return StartTapeLength(PointerWorld);
                case WorkspaceIntentKind.TapeLengthConfirm:return ConfirmTapeLength();
                default:return false;
            }
        }

        public bool SelectPhysical(PhysicalInteractable item)
        {
            if(!item||item.CurrentState==PhysicalItemState.Disabled||(item.CurrentContainer&&!item.CurrentContainer.IsAccessibleHierarchy)||InteractionBusy)return false;
            if(SelectedPhysical&&SelectedPhysical!=item)SelectedPhysical.SetHighlight(false);
            if(SelectedTape&&SelectedTape!=item){SelectedTape.SetHighlight(false);SelectedTape=null;}
            SelectedPhysical=item;SelectedMaterialId=null;
            item.SetHighlight(true);
            if(item.itemType==PhysicalItemType.Tape)
            {SelectedTape=item;item.SetHighlight(true);Status=item.displayName+" · 向桌前拉拽选择长度；横向右拖取整卷需先抽出轴杆。";}
            else Status=item.displayName;
            Debug.Log("[Workspace] Select:"+item.itemInstanceId);
            SelectionRevision++;
            return true;
        }

        public bool SelectMaterial(string id)
        {
            var item=Ready?Service.GetItem(id):null;
            if(item==null||InteractionBusy)return false;
            if(SelectedTape){SelectedTape.SetHighlight(false);SelectedTape=null;}
            SelectedPhysical=null;
            SelectedMaterialId=id;
            if(item.owner==CraftOwner.ReleaseBookPage){ActiveBookId=item.containerId;scrapbook.Deselect();}
            Status="已选中素材，保持原位 · 右拖或按 P 揭取 · Q/E旋转 · [/]缩放。";
            SelectionRevision++;Debug.Log("[Workspace] SelectMaterial:"+id);return true;
        }

        public bool GrabPhysical(PhysicalInteractable item)
        {
            if(!item||!Ready||TapeLengthSelecting||Service.HeldItem!=null||!item.IsAccessible)return false;
            SelectedPhysical=item;
            if(item.itemType==PhysicalItemType.Sticker||item.itemType==PhysicalItemType.Note)
            {
                coordinator.SyncPhysical();
                bool grabbed=Service.BeginStock(item.itemInstanceId);
                if(grabbed){SelectedMaterialId=item.itemInstanceId;Status="移动到手账或打开的离型本，左键贴上；ESC取消。";}
                return Report(grabbed,"MaterialGrab:"+item.itemInstanceId);
            }
            if(IsReleaseBook(item)&&Service.FindBook(BookIdFor(item))?.isOpen==true)
            {if(!Service.SetBookOpen(BookIdFor(item),false))return false;}
            var shaft=FindObjectsByType<WorkspaceShaft>(FindObjectsSortMode.None).FirstOrDefault(s=>s.slot&&s.slot.Occupant==item);
            if(shaft)
            {
                if(!shaft.IsExtended){Status="先拖动独立轴杆把手，向侧面抽出横轴。";return false;}
                bool detached=coordinator.TakeFromShaft(shaft);return Report(detached,"Detach:"+item.itemInstanceId);
            }
            return Report(physical.Submit(new PhysicalInteractionIntent(PhysicalIntentKind.Grab,item)),"PhysicalGrab:"+item.itemInstanceId);
        }

        public bool DetachSelectedTape()
        {return SelectedTape&&GrabPhysical(SelectedTape);}

        public bool BeginPeel(string id)
        {
            if(!Ready||TapeLengthSelecting||(physical&&physical.HeldItem))return false;
            if(scrapbook.Chosen!=null){Status="请先放置或取消当前数字素材。";return false;}
            if(scrapbook.IsTransforming)scrapbook.CancelTransform();
            if(string.IsNullOrEmpty(id))id=scrapbook.SelectedId??SelectedMaterialId;
            coordinator.SyncPhysical();
            bool peeled=Service.BeginPeel(id);
            if(peeled){if(scrapbook.SelectedId==id)scrapbook.Deselect();SelectedMaterialId=id;Status="已揭起，同一素材正在移动 · 左键贴到目标页面 · ESC恢复。";}
            return Report(peeled,"Peel:"+id);
        }

        public bool StartTapeLength(Vector3 pointer)
        {
            if(!Ready||!SelectedTape||InteractionBusy)return false;
            var record=Service.GetItem(SelectedTape.itemInstanceId);
            if(record==null){Status="胶带未登记。";return false;}
            coordinator.SyncPhysical();
            TapeLengthSelecting=true;TapeLengthMeters=Mathf.Clamp(defaultTapeLength,minimumTapeLength,maximumTapeLength);
            lengthPointerStart=pointer;TapePreviewOrigin=SelectedTape.transform.position+Vector3.up*.027f;
            PreviewPlacement=null;Status="移动鼠标调整厘米长度；再次右键撕取，ESC取消。";
            Debug.Log("[Workspace] TapeLengthStart:"+SelectedTape.itemInstanceId+":"+TapeLengthMeters.ToString("F3"));return true;
        }

        public void SetTapeLength(float metres)
        {if(TapeLengthSelecting)TapeLengthMeters=Mathf.Clamp(metres,minimumTapeLength,maximumTapeLength);}

        public bool ConfirmTapeLength()
        {
            if(!Ready||!TapeLengthSelecting||!SelectedTape)return false;
            float width=SelectedTape.dimensions?SelectedTape.dimensions.size.y:.015f;
            bool created=Service.CreateTapeStrip(SelectedTape.assetId,SelectedTape.itemInstanceId,TapeLengthMeters,width);
            if(created)
            {
                TapeLengthSelecting=false;SelectedMaterialId=Service.HeldItem.instanceId;TearAnimationUntil=Time.unscaledTime+.22f;
                Status="已撕取 "+(TapeLengthMeters*100).ToString("F1")+" cm · 左键贴到有效页面；Q/E旋转，[ / ]缩放。";
                Debug.Log("[Workspace] TapeCut:"+Service.HeldItem.instanceId+":"+TapeLengthMeters.ToString("F3"));
            }
            return created;
        }

        public void MovePointer(Ray ray)
        {
            if(!Ready)return;
            Vector3 point=PointerWorld;
            if(physical.surface.TryRaycast(ray,out var desktop))point=desktop+Vector3.up*.003f;
            PointerWorld=point;
            if(TapeLengthSelecting)
            {SetTapeLength(defaultTapeLength+Vector3.Dot(point-lengthPointerStart,-physical.surface.transform.forward));return;}
            if(Service.HeldItem==null){PreviewPlacement=null;return;}
            PreviewPlacement=null;PreviewFits=false;
            if(TryHitPage(ray,out var placement,out var anchor,out var metres))
            {
                placement.rotationDegrees=Service.HeldItem.rotationDegrees;placement.scale=Service.HeldItem.scale;
                PreviewPlacement=placement;PreviewFits=Fits(Service.HeldItem,placement.position,metres);
                var local=ReleaseBookView.LocalPageSize(anchor,metres);
                PointerWorld=anchor.TransformPoint(new Vector3(local.x*placement.position.x,local.y*placement.position.y,-.0015f));
            }
        }

        public bool TryPlacePointer(Ray ray)
        {
            MovePointer(ray);
            if(PreviewPlacement==null){Status="请放到手账或打开的离型本页面。ESC可恢复原处。";return false;}
            if(!PreviewFits){Status="素材超过纸页边界，请移动、旋转或按 [ 缩小；ESC恢复原处。";return false;}
            return Attach(PreviewPlacement);
        }

        public bool Attach(CraftPlacement placement)
        {
            if(!Ready||Service.HeldItem==null)return false;
            string id=Service.HeldItem.instanceId;
            bool attached=Service.CommitHeld(placement);
            if(attached)
            {
                SelectedMaterialId=id;Status="已贴入页面 · 仍保留同一实例身份；P可再次揭取。";PreviewPlacement=null;
                if(placement.owner==CraftOwner.JournalPage)
                    foreach(JournalSide side in new[]{JournalSide.Left,JournalSide.Right})
                        if(journal.Document.GetPage(side).pageId==placement.pageId)scrapbook.Select(id,side);
                Debug.Log("[Workspace] Attach:"+id+":"+placement.owner+":"+placement.containerId+":"+placement.pageId);
            }
            else Status=Service.LastMessage;
            return attached;
        }

        public bool TryHitPage(Ray ray,out CraftPlacement placement,out Transform anchor,out Vector2 metres)
        {
            placement=null;anchor=null;metres=default;float nearest=float.MaxValue;
            if(!Ready||journal.IsBusy)return false;
            foreach(var view in releaseBooks)
            {
                if(!view)continue;var book=Service.FindBook(view.bookId);
                if(!view.TryHit(book,ray,out var page,out var uv,out float distance)||distance>=nearest)continue;
                nearest=distance;placement=new CraftPlacement{owner=CraftOwner.ReleaseBookPage,containerId=view.bookId,pageId=page,position=uv};
                anchor=view.AnchorFor(book,page);metres=view.pageSize;
            }
            foreach(JournalSide side in new[]{JournalSide.Left,JournalSide.Right})
            {
                var candidate=journal.pageView.GetSurface(side);var plane=new Plane(candidate.forward,candidate.position);
                if(!plane.Raycast(ray,out float distance)||distance<0||distance>=nearest)continue;
                var p=candidate.InverseTransformPoint(ray.GetPoint(distance));var size=journal.pageView.SurfaceSize(side);
                var uv=new Vector2(p.x/size.x,p.y/size.y);
                if(uv.x<0||uv.x>1||uv.y<0||uv.y>1)continue;
                nearest=distance;anchor=candidate;metres=JournalMetres(side);
                placement=new CraftPlacement{owner=CraftOwner.JournalPage,containerId="journal",pageId=journal.Document.GetPage(side).pageId,position=uv};
            }
            return placement!=null;
        }

        public string HitMaterial(CraftPlacement page,Vector2 pageMetres)
        {
            if(!Ready||page==null)return null;
            foreach(var item in Service.GetPageItems(page.owner,page.containerId,page.pageId).OrderByDescending(i=>i.sortOrder))
            {
                Vector2 p=Vector2.Scale(page.position-item.position,pageMetres);float a=-item.rotationDegrees*Mathf.Deg2Rad;
                var local=new Vector2(p.x*Mathf.Cos(a)-p.y*Mathf.Sin(a),p.x*Mathf.Sin(a)+p.y*Mathf.Cos(a));
                var size=MetricSize(item)*item.scale;
                if(Mathf.Abs(local.x)>size.x*.5f||Mathf.Abs(local.y)>size.y*.5f)continue;
                var definition=scrapbook.Catalog.Find(item.assetId);
                if(definition!=null&&item.kind!=CraftItemKind.TapeStrip&&!definition.HitAlpha(new Vector2(local.x/size.x+.5f,local.y/size.y+.5f)))continue;
                return item.instanceId;
            }
            return null;
        }

        public bool OpenBook(string id,bool open)
        {
            if(!Ready||InteractionBusy)return false;
            if(string.IsNullOrEmpty(id))id=ActiveBookId??BookIdFor(SelectedPhysical);
            var view=releaseBooks.FirstOrDefault(v=>v&&v.bookId==id);
            if(!view){Status="请先选中一本离型本。";return false;}
            if(open&&view.physical&&(view.physical.CurrentSlot||view.physical.CurrentState==PhysicalItemState.Stored))
            {Status="请先从抽屉取出离型本，放到桌面再打开。";return false;}
            coordinator.SyncPhysical();
            bool changed=Service.SetBookOpen(id,open);if(changed){ActiveBookId=id;SelectedMaterialId=null;Status=open?"离型本已打开 · 可自由贴入、拖动揭取与翻页。":"离型本已合上，可以拿起并归位。";}
            if(changed)LogInputTargets();
            return Report(changed,(open?"OpenBook:":"CloseBook:")+id);
        }

        public bool TurnBook(string id,int direction)
        {
            bool inspected=physical&&physical.IsInspecting&&BookIdFor(physical.HeldItem)==id;
            if(!CanNavigateTargetBook&&!inspected)return false;var book=Service.FindBook(id);
            if(book==null||!book.isOpen)return false;
            int next=Mathf.Clamp(book.currentPage+direction*2,0,Mathf.Max(0,book.pages.Count-2));
            if(next==book.currentPage)return false;
            SelectedMaterialId=null;bool changed=Service.SetBookPage(id,next);if(changed)LogInputTargets();return Report(changed,"BookPage:"+id+":"+next);
        }

        public bool InspectBook(PhysicalInteractable item)
        {
            if(!Ready||!item||TapeLengthSelecting||Service.HeldItem!=null||physical.IsSettling)return false;
            if(item.itemType!=PhysicalItemType.TravelersNotebook&&!IsReleaseBook(item))return false;
            if(physical.HeldItem&&physical.HeldItem!=item)return false;
            bool wasOpen=IsReleaseBook(item)&&Service.FindBook(BookIdFor(item))?.isOpen==true;
            var originalInspectAnchor=item.inspectAnchor;
            item.inspectAnchor=bookInspection.InspectionAnchorFor(item);
            bool inspecting=physical.Submit(new PhysicalInteractionIntent(PhysicalIntentKind.Inspect,item));
            item.inspectAnchor=originalInspectAnchor;
            if(!inspecting)return false;
            SelectedPhysical=item;
            if(IsReleaseBook(item))Service.SetBookOpen(BookIdFor(item),true);
            bookInspection.Begin(item,wasOpen);
            Status="正在查看真实内页 · ←/→ 或上一页/下一页翻阅 · ESC放回原处。";
            return true;
        }
        public bool TurnInspectedBook(int direction)=>bookInspection&&bookInspection.Turn(direction);

        public bool ReturnSelected()
        {
            if(!Ready)return false;
            if(bookInspection&&bookInspection.IsActive)bookInspection.End();
            if(TapeLengthSelecting||Service.HeldItem!=null)return Cancel();
            var item=physical.HeldItem?physical.HeldItem:SelectedPhysical;
            if(!item)return false;
            string bookId=BookIdFor(item);
            if(bookId!=null&&Service.FindBook(bookId)?.isOpen==true){Status="先合上离型本。";return false;}
            if(item.homeSlot&&item.homeSlot.container&&!item.homeSlot.container.isOpen)
            {Status="先打开原收纳抽屉，再按 R 归位。";return false;}
            bool returned=coordinator.ReturnToHome(item);
            Status=returned?"物品正在返回原收纳位。":!string.IsNullOrEmpty(coordinator.LastMessage)?coordinator.LastMessage:physical.LastResult;
            if(returned)Debug.Log("[Workspace] Return:"+item.itemInstanceId);
            return returned;
        }

        public bool Cancel()
        {
            if(bookInspection&&bookInspection.IsActive)bookInspection.End();
            if(TapeLengthSelecting){TapeLengthSelecting=false;Status="已取消拉取，胶带卷保持原位。";return true;}
            if(Ready&&Service.HeldItem!=null){bool cancelled=Service.CancelHeld();PreviewPlacement=null;Status=cancelled?"已恢复素材原来的位置。":Service.LastMessage;return cancelled;}
            if(physical&&(physical.HeldItem||physical.IsSettling)){physical.Cancel();Status="已取消拿取。";return true;}
            if(!string.IsNullOrEmpty(SelectedMaterialId)){if(scrapbook.SelectedId==SelectedMaterialId)scrapbook.Deselect();SelectedMaterialId=null;Status="已取消素材选择。";return true;}
            if(SelectedTape){SelectedTape.SetHighlight(false);SelectedTape=null;SelectedPhysical=null;Status="已取消胶带选择。";return true;}
            if(SelectedPhysical){SelectedPhysical.SetHighlight(false);SelectedPhysical=null;Status="已取消物品选择。";return true;}
            return false;
        }
        public void ClearSelection()
        { if(SelectedTape)SelectedTape.SetHighlight(false);if(SelectedPhysical)SelectedPhysical.SetHighlight(false);SelectedTape=null;SelectedPhysical=null;SelectedMaterialId=null;scrapbook.Deselect(); }

        public bool EditSelected(string operation,float amount=0)
        {
            if(!Ready)return false;
            var held=Service.HeldItem;var item=held??Service.GetItem(SelectedMaterialId);
            if(item==null)return false;bool result=false;
            if(held!=null)
            {
                if(operation=="rotate")result=Service.SetHeldTransform(item.rotationDegrees+amount,item.scale);
                if(operation=="scale")result=Service.SetHeldTransform(item.rotationDegrees,Mathf.Clamp(item.scale+amount,.2f,2));
            }
            else if(operation=="rotate")result=Service.RotateAttached(item.instanceId,item.rotationDegrees+amount);
            else if(operation=="scale")result=Service.ScaleAttached(item.instanceId,Mathf.Clamp(item.scale+amount,.2f,2));
            else if(operation=="delete"){result=Service.DeleteAttached(item.instanceId);if(result)SelectedMaterialId=null;}
            else if(operation=="top"||operation=="bottom"||operation=="up"||operation=="down")result=Service.LayerAttached(item.instanceId,operation);
            Status=result?"已更新素材 · Ctrl+Z撤销，Ctrl+Y重做。":Service.LastMessage;return result;
        }

        public bool Undo(){if(!Ready||physical&&(physical.HeldItem||physical.IsSettling))return false;if(TapeLengthSelecting)return Cancel();bool result=Service.Undo();RefreshViews();return result;}
        public bool Redo(){if(!Ready||TapeLengthSelecting||physical&&(physical.HeldItem||physical.IsSettling))return false;bool result=Service.Redo();RefreshViews();return result;}
        public void SyncLegacySelection()
        {if(Ready&&journal.IsEditing&&!InteractionBusy&&!SelectedMaterialIsReleaseBook)SelectedMaterialId=scrapbook.SelectedId;}
        public string BookIdFor(PhysicalInteractable item)=>item?releaseBooks.FirstOrDefault(v=>v&&v.physical==item)?.bookId:null;
        public bool IsReleaseBook(PhysicalInteractable item)=>BookIdFor(item)!=null;
        public Vector2 JournalMetres(JournalSide side)
        {var origin=journal.pageView.WorldFromNormalized(side,Vector2.zero);return new Vector2(Vector3.Distance(origin,journal.pageView.WorldFromNormalized(side,Vector2.right)),Vector3.Distance(origin,journal.pageView.WorldFromNormalized(side,Vector2.up)));}
        public static Vector2 MetricSize(CraftItemRecord item)=>item.sizeMeters.x>0&&item.sizeMeters.y>0?item.sizeMeters:new Vector2(item.lengthMeters,item.widthMeters);
        public static bool Fits(CraftItemRecord item,Vector2 uv,Vector2 page)
        {
            var size=MetricSize(item)*item.scale;float angle=item.rotationDegrees*Mathf.Deg2Rad,c=Mathf.Abs(Mathf.Cos(angle)),s=Mathf.Abs(Mathf.Sin(angle));
            var ext=new Vector2((size.x*c+size.y*s)/page.x,(size.x*s+size.y*c)/page.y)*.5f;
            return uv.x-ext.x>=.002f&&uv.x+ext.x<=.998f&&uv.y-ext.y>=.002f&&uv.y+ext.y<=.998f;
        }
        bool Report(bool result,string message){if(result)Debug.Log("[Workspace] "+message);else if(Ready)Status=Service.LastMessage;return result;}
        public void RefreshViews()
        {if(!Ready)return;foreach(var view in releaseBooks)if(view)view.Apply(Service.FindBook(view.bookId));if(materialRenderer)materialRenderer.Refresh();}
        void LogInputTargets(){var input=GetComponent<WorkspaceInputRouter>();if(input&&desk&&desk.HasBegun)input.LogVisibleTargets();}
        void OnApplicationFocus(bool focus){if(!focus&&ready){Cancel();Service.Flush();}}
        void OnApplicationQuit(){if(Ready){Cancel();Service.Flush();}}
        void OnDestroy(){if(Service!=null){Service.DataChanged-=RefreshViews;Service.Dispose();}}
    }
}

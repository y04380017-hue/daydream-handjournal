using System;
using System.Linq;
using UnityEngine;

namespace VintageJournal
{
    /// <summary>One native input producer. Consumed frames never reach legacy page, physical or camera input.</summary>
    [DefaultExecutionOrder(-100)]
    public sealed class WorkspaceInputRouter : MonoBehaviour
    {
        public WorkspaceGameplayController workspace;
        public WorkspaceDrawer DraggedDrawer {get;private set;}
        public WorkspaceShaft DraggedShaft {get;private set;}
        public PhysicalInteractable HoveredPhysical {get;private set;}
        public PhysicalContainer HoveredContainer {get;private set;}
        public WorkspaceShaft HoveredShaft {get;private set;}
        public WorkspaceDrawer HoveredDrawer {get;private set;}
        public bool IsDraggingMechanism=>DraggedDrawer||DraggedShaft;
        PhysicalInteractable pendingTape, pendingNotebook, pendingPhysical;
        string pendingPeel;
        Vector2 pointerDown;
        float mechanismOrigin;
        bool materialDrag,logged,previousCameraTransition,nativeValidation;
        int pendingButton, physicalDragButton, materialDragButton;
        Vector3 pointerWorldDown;
        Vector3 physicalDragOffset;
        readonly SeatedLookMouseAdapter seatedLook = new SeatedLookMouseAdapter();
        float DragThreshold => Mathf.Max(6, 9 * Screen.width / 1920f);
        static readonly int[] lateralSamples={0,-1,1,-2,2},verticalSamples={0,-1,1};

        void Awake(){if(!workspace)workspace=GetComponent<WorkspaceGameplayController>();nativeValidation=Environment.GetCommandLineArgs().Contains("--workspace-native-validation");}
        void Update()
        {
            RuntimeInputGate.WorkspaceOwnsInput=workspace&&workspace.enabled;
            RuntimeInputGate.BlocksLegacyInput=workspace&&(workspace.ConsumesLegacyInput||IsDraggingMechanism);
            RuntimeInputGate.BlocksJournalOperations=workspace&&(workspace.InteractionBusy||IsDraggingMechanism);
            var atelier=AtelierHub.Instance;
            if(atelier&&atelier.IsOpen)
            {
                RuntimeInputGate.BlocksLegacyInput=true;RuntimeInputGate.BlocksJournalOperations=true;
                if(Input.GetKeyDown(KeyCode.Escape))atelier.Close();
                RuntimeInputGate.Consume();return;
            }
            if(AtelierTutorial.Blocking)
            {
                RuntimeInputGate.BlocksLegacyInput=true;RuntimeInputGate.BlocksJournalOperations=true;
                if(Input.GetKeyDown(KeyCode.Escape))AtelierTutorial.Instance.Close();
                RuntimeInputGate.Consume();return;
            }
            var dream=DreamGameplayController.Instance;
            if(dream&&dream.BagOpen)
            {
                RuntimeInputGate.BlocksLegacyInput=true;RuntimeInputGate.BlocksJournalOperations=true;
                if(Input.GetKeyDown(KeyCode.Escape))dream.CloseBag();
                RuntimeInputGate.Consume();return;
            }
            if(DeskViewControls.Instance&&DeskViewControls.Instance.Expanded)
            {
                RuntimeInputGate.BlocksLegacyInput=true;RuntimeInputGate.BlocksJournalOperations=true;
                if(Input.GetKeyDown(KeyCode.Escape))DeskViewControls.Instance.CloseMenu();
                RuntimeInputGate.Consume();return;
            }
            if(DrawerStockIndex.Instance&&DrawerStockIndex.Instance.Expanded)
            {
                RuntimeInputGate.BlocksLegacyInput=true;RuntimeInputGate.BlocksJournalOperations=true;
                if(Input.GetKeyDown(KeyCode.Escape))DrawerStockIndex.Instance.Close();
                RuntimeInputGate.Consume();return;
            }
            if(nativeValidation&&Input.GetKeyDown(KeyCode.F8))LogMode();
            if(!workspace||!workspace.Ready||!workspace.desk||!workspace.lens)return;
            if(!Application.isFocused||!workspace.desk.HasBegun||workspace.desk.IsBlocking)
            {
                if(workspace.InteractionBusy||IsDraggingMechanism){workspace.Cancel();EndMechanism(true);RuntimeInputGate.Consume();}
                ClearPending();HoveredPhysical=null;HoveredContainer=null;return;
            }
            if(!workspace.desk.SceneInputAllowed&&!workspace.InteractionBusy&&!IsDraggingMechanism){ClearHover();return;}
            if(!logged){logged=true;LogVisibleTargets();}
            bool transitioning=workspace.journal.cameraRig&&workspace.journal.cameraRig.IsTransitioning;
            if(previousCameraTransition&&!transitioning)LogVisibleTargets();
            previousCameraTransition=transitioning;
            if(Input.GetKeyDown(KeyCode.F8))LogVisibleTargets();
            Vector2 pointer=Input.mousePosition;Ray ray=workspace.lens.ScreenPointToRay(pointer);
            if(workspace.journal.cameraRig&&workspace.journal.cameraRig.MouseLookAwaitingNeutral&&
                seatedLook.IsNeutral(pointer,new Vector2(Screen.width,Screen.height),workspace.journal.cameraRig.profile))workspace.journal.cameraRig.ArmMouseLook();
            workspace.SyncLegacySelection();
            workspace.MovePointer(ray);
            if(Input.GetKeyDown(KeyCode.Escape))
            {
                ClearPending();materialDrag=false;
                if(CommissionDeskController.Instance&&CommissionDeskController.Instance.PanelOpen)
                {CommissionDeskController.Instance.ClosePanel();RuntimeInputGate.Consume();return;}
                if(IsDraggingMechanism){EndMechanism(true);RuntimeInputGate.Consume();return;}
                if(!workspace.journal.IsEditing&&!workspace.InteractionBusy&&workspace.journal.cameraRig&&
                    (workspace.journal.cameraRig.ActiveDeskView!=DeskViewKind.Desk||workspace.journal.cameraRig.HasSeatedLookOffset))
                {workspace.ClearSelection();workspace.journal.cameraRig.Return();RuntimeInputGate.Consume();return;}
                if((workspace.InteractionBusy||workspace.SelectedMaterialIsReleaseBook||!workspace.journal.IsEditing)&&workspace.Cancel()){RuntimeInputGate.Consume();return;}
                if(!workspace.journal.IsEditing&&workspace.journal.cameraRig&&(workspace.journal.cameraRig.ActiveDeskView!=DeskViewKind.Desk||workspace.journal.cameraRig.HasSeatedLookOffset))
                {workspace.journal.cameraRig.Return();RuntimeInputGate.Consume();return;}
            }
            if(IsDraggingMechanism)
            {
                if(DraggedDrawer)DraggedDrawer.DragTo(pointer,workspace.lens);
                if(DraggedShaft)DraggedShaft.DragTo(pointer,workspace.lens);
                // A release edge can be missed while focus or a frame stalls; a
                // mechanism drag must also finish once the button is released.
                if(Input.GetMouseButtonUp(0)||!Input.GetMouseButton(0))EndMechanism(false);
                RuntimeInputGate.Consume();return;
            }
            bool ctrl=Input.GetKey(KeyCode.LeftControl)||Input.GetKey(KeyCode.RightControl);
            if(ctrl&&(Input.GetKeyDown(KeyCode.Z)||Input.GetKeyDown(KeyCode.Y)))
            {if(Input.GetKeyDown(KeyCode.Z))workspace.Undo();else workspace.Redo();RuntimeInputGate.Consume();return;}
            if(Input.GetKeyDown(KeyCode.R)&&workspace.ReturnSelected()){RuntimeInputGate.Consume();return;}
            if(workspace.physical.IsInspecting)
            {
                if(Input.GetKeyDown(KeyCode.LeftArrow)||Input.GetKeyDown(KeyCode.PageUp))workspace.TurnInspectedBook(-1);
                if(Input.GetKeyDown(KeyCode.RightArrow)||Input.GetKeyDown(KeyCode.PageDown))workspace.TurnInspectedBook(1);
                if(Input.GetKeyDown(KeyCode.E))workspace.Cancel();
                RuntimeInputGate.Consume();return;
            }
            if(Input.GetKeyDown(KeyCode.P))
            {if(workspace.BeginPeel(workspace.scrapbook.SelectedId??workspace.SelectedMaterialId))RuntimeInputGate.Consume();return;}
            bool craftSelected=workspace.Service.HeldItem!=null||workspace.SelectedMaterialIsReleaseBook;
            if(craftSelected)
            {
                if(Input.GetKeyDown(KeyCode.Delete)){workspace.EditSelected("delete");RuntimeInputGate.Consume();return;}
                float rotationStep=Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift)?15:5;
                if(Input.GetKeyDown(KeyCode.Q)||Input.GetKeyDown(KeyCode.E))
                {workspace.EditSelected("rotate",Input.GetKeyDown(KeyCode.Q)?-rotationStep:rotationStep);RuntimeInputGate.Consume();return;}
                if(Input.GetKeyDown(KeyCode.LeftBracket)||Input.GetKeyDown(KeyCode.RightBracket))
                {workspace.EditSelected("scale",Input.GetKeyDown(KeyCode.LeftBracket)?-.1f:.1f);RuntimeInputGate.Consume();return;}
            }
            if(Input.GetMouseButtonDown(1))
            {
                if(workspace.TapeLengthSelecting){workspace.ConfirmTapeLength();RuntimeInputGate.Consume();return;}
            }
            if(workspace.TapeLengthSelecting){RuntimeInputGate.Consume();return;}
            var commission=CommissionDeskController.Instance;
            bool overLetters=commission&&commission.ContainsPointer(pointer);
            bool overDream=dream&&dream.Contains(pointer);
            bool overAtelier=atelier&&atelier.Contains(pointer);
            bool overCamera=DeskViewControls.Instance&&DeskViewControls.Instance.Contains(pointer);
            bool overInspection=workspace.bookInspection&&workspace.bookInspection.Contains(pointer);
            bool overDrawerIndex=DrawerStockIndex.Instance&&DrawerStockIndex.Instance.Contains(pointer);
            bool overDiscovery=MaterialDiscoveryPresentation.Instance&&MaterialDiscoveryPresentation.Instance.Contains(pointer);
            bool overUI=overDrawerIndex||overDiscovery||overCamera||overInspection||overAtelier||overDream||overLetters||workspace.desk.IsPointerOverInterface(pointer)||(workspace.hud&&workspace.hud.Contains(pointer));
            if(workspace.Service.HeldItem!=null)
            {
                if(!overUI&&Input.GetMouseButtonDown(0)){workspace.TryPlacePointer(ray);materialDrag=false;RuntimeInputGate.Consume();return;}
                if(Input.GetMouseButtonUp(materialDragButton)&&materialDrag&&Vector2.Distance(pointer,pointerDown)>DragThreshold)
                {if(overUI||!workspace.TryPlacePointer(ray))workspace.Cancel();materialDrag=false;}
                RuntimeInputGate.Consume();return;
            }
            if(workspace.physical.HeldItem)
            {
                MovePhysical(ray,overUI);
                if(Input.GetKeyDown(KeyCode.E))workspace.InspectBook(workspace.physical.HeldItem);
                if(Input.GetMouseButtonUp(physicalDragButton)&&!workspace.physical.IsInspecting)ReleasePhysical(pointer,ray,overUI);
                RuntimeInputGate.Consume();return;
            }
            if(pendingTape&&Input.GetMouseButton(pendingButton)&&Vector2.Distance(pointer,pointerDown)>DragThreshold)
            {
                // Pull towards the front edge of the desk to unwind; never move the roll itself.
                float forward=Vector3.Dot(workspace.PointerWorld-pointerWorldDown,-workspace.physical.surface.transform.forward);
                if(forward>.004f || pointer.y < pointerDown.y-DragThreshold)
                {workspace.StartTapeLength(pointerWorldDown);ClearPending();RuntimeInputGate.Consume();return;}
                if(pendingButton==1)
                {physicalDragButton=1;physicalDragOffset=pendingTape.transform.position-pointerWorldDown;physicalDragOffset.y=0;workspace.GrabPhysical(pendingTape);ClearPending();RuntimeInputGate.Consume();return;}
            }
            if(pendingPhysical&&Input.GetMouseButton(pendingButton)&&Vector2.Distance(pointer,pointerDown)>DragThreshold)
            {
                physicalDragButton=pendingButton;materialDragButton=pendingButton;
                physicalDragOffset=pendingPhysical.transform.position-pointerWorldDown;physicalDragOffset.y=0;
                bool grabbed=workspace.GrabPhysical(pendingPhysical);
                materialDrag=grabbed&&workspace.Service.HeldItem!=null;
                ClearPending();RuntimeInputGate.Consume();return;
            }
            if(pendingPeel!=null&&Input.GetMouseButton(1)&&Vector2.Distance(pointer,pointerDown)>DragThreshold)
            {
                materialDrag=workspace.BeginPeel(pendingPeel);materialDragButton=1;pendingPeel=null;RuntimeInputGate.Consume();return;
            }
            if(pendingPhysical&&Input.GetMouseButtonUp(pendingButton))
            {
                var clicked=pendingPhysical;int button=pendingButton;ClearPending();
                if(button==1&&workspace.IsReleaseBook(clicked))workspace.OpenBook(workspace.BookIdFor(clicked),true);
                else if(button==1&&clicked.itemType==PhysicalItemType.TravelersNotebook)workspace.InspectBook(clicked);
                RuntimeInputGate.Consume();return;
            }
            if(Input.GetMouseButtonUp(0)||Input.GetMouseButtonUp(1)){pendingTape=null;pendingNotebook=null;pendingPeel=null;}
            if(overUI)
            {
                ClearHover();
                if(overDrawerIndex||overDiscovery||overCamera||overInspection||overDream||overAtelier)RuntimeInputGate.Consume();
                // The letter panel owns its clicks and wheel; legacy page and camera gestures must not see them.
                if(overLetters&&(Input.GetMouseButtonDown(0)||Input.GetMouseButton(0)||Input.GetMouseButtonUp(0)||Input.mouseScrollDelta.y!=0))RuntimeInputGate.Consume();
                return;
            }
            if(TryDispatchSeatedLook(pointer))
            {ClearHover();RuntimeInputGate.Consume();return;}
            if(Input.GetMouseButtonDown(0)&&dream&&dream.CanCollect&&dream.collection&&dream.collection.TryClick(ray))
            {ClearHover();RuntimeInputGate.Consume();return;}
            if(workspace.TryHitPage(ray,out var surface,out _,out var metres))
            {
                string hit=workspace.HitMaterial(surface,metres);
                if(surface.owner==CraftOwner.ReleaseBookPage&&(Input.GetMouseButtonDown(0)||Input.GetMouseButtonDown(1)))
                {
                    if(hit!=null){workspace.SelectMaterial(hit);if(Input.GetMouseButtonDown(1)){pendingPeel=hit;pointerDown=pointer;}}
                    else if(Input.GetMouseButtonDown(0))
                    {
                        var book=workspace.releaseBooks.FirstOrDefault(b=>b&&b.bookId==surface.containerId);
                        if(book&&book.physical){workspace.SelectPhysical(book.physical);pendingPhysical=book.physical;pendingButton=0;pointerDown=pointer;}
                    }
                    RuntimeInputGate.Consume();return;
                }
            }
            if(Input.GetMouseButtonDown(0)&&commission&&!workspace.journal.IsEditing&&commission.TryClick(ray)){ClearHover();RuntimeInputGate.Consume();return;}
            Hover(ray);
            if(workspace.journal.IsEditing)return; // Existing digital gestures retain their established operation path.
            if(Input.GetKeyDown(KeyCode.E)&&(HoveredPhysical||workspace.SelectedPhysical))
            {workspace.InspectBook(HoveredPhysical?HoveredPhysical:workspace.SelectedPhysical);RuntimeInputGate.Consume();return;}
            if(!Input.GetMouseButtonDown(0)&&!Input.GetMouseButtonDown(1))return;
            pointerDown=pointer;
            pointerWorldDown=workspace.PointerWorld;
            pendingButton=Input.GetMouseButtonDown(1)?1:0;
            if(HoveredDrawer&&pendingButton==0)
            {mechanismOrigin=HoveredDrawer.OpenAmount;if(HoveredDrawer.BeginDrag(pointer,workspace.lens))DraggedDrawer=HoveredDrawer;RuntimeInputGate.Consume();return;}
            if(HoveredShaft&&pendingButton==0)
            {mechanismOrigin=HoveredShaft.OpenAmount;if(HoveredShaft.BeginDrag(pointer,workspace.lens))DraggedShaft=HoveredShaft;RuntimeInputGate.Consume();return;}
            if(HoveredPhysical)
            {
                workspace.SelectPhysical(HoveredPhysical);
                if(HoveredPhysical.itemType==PhysicalItemType.Tape)pendingTape=HoveredPhysical;
                else if(pendingButton==1||workspace.IsReleaseBook(HoveredPhysical))pendingPhysical=HoveredPhysical;
                RuntimeInputGate.Consume();return;
            }
            if(HoveredContainer&&pendingButton==0){HoveredContainer.ToggleOpen();RuntimeInputGate.Consume();}
        }

        void ClearPending(){pendingTape=null;pendingNotebook=null;pendingPhysical=null;pendingPeel=null;}

        bool TryDispatchSeatedLook(Vector2 pointer)
        {
            var camera=workspace.journal.cameraRig;
            bool mouseActive=Input.GetMouseButton(0)||Input.GetMouseButton(1)||Input.GetMouseButton(2)||
                Input.GetMouseButtonDown(0)||Input.GetMouseButtonDown(1)||Input.GetMouseButtonDown(2)||
                Input.GetMouseButtonUp(0)||Input.GetMouseButtonUp(1)||Input.GetMouseButtonUp(2);
            if(!CanDispatchSeatedLook(pointer,mouseActive,Input.mouseScrollDelta.sqrMagnitude>.0001f))return false;
            return seatedLook.TrySample(pointer,new Vector2(Screen.width,Screen.height),Time.unscaledDeltaTime,camera.profile,out var intent)&&camera.SubmitLook(intent);
        }
        /// <summary>Device-independent ownership gate. The native producer separately supplies focus/edge/button state.</summary>
        public bool CanDispatchSeatedLook(Vector2 pointer,bool mouseButtonActive=false,bool scrolling=false)
        {
            if(!workspace||!workspace.Ready||!workspace.desk||!workspace.desk.HasBegun||workspace.desk.IsBlocking)return false;
            var camera=workspace.journal.cameraRig;
            if(!camera||!camera.CanLook||camera.MouseLookAwaitingNeutral||workspace.InteractionBusy||IsDraggingMechanism||workspace.journal.IsEditing||workspace.journal.IsBusy||
                workspace.journal.HasPaperPreview||workspace.scrapbook.IsTransforming||workspace.scrapbook.BrowserOpen||
                pendingTape||pendingNotebook||pendingPhysical||pendingPeel!=null||
                (CommissionDeskController.Instance&&CommissionDeskController.Instance.PanelOpen)||
                (AtelierHub.Instance&&AtelierHub.Instance.IsOpen)||AtelierTutorial.Blocking||
                (DreamGameplayController.Instance&&DreamGameplayController.Instance.BagOpen)||
                (DeskViewControls.Instance&&DeskViewControls.Instance.Expanded)||mouseButtonActive||scrolling||OverUI(pointer))return false;
            return true;
        }

        void Hover(Ray ray)
        {
            HoveredPhysical=null;HoveredContainer=null;HoveredShaft=null;HoveredDrawer=null;
            if(Physics.Raycast(ray,out var hit,3,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
            {
                HoveredPhysical=ResolvePhysical(hit.collider);
                if(!HoveredPhysical)
                {
                    HoveredDrawer=hit.collider.GetComponentInParent<WorkspaceDrawer>();
                    HoveredShaft=hit.collider.GetComponentInParent<WorkspaceShaft>();
                    if(!HoveredDrawer&&!HoveredShaft)HoveredContainer=hit.collider.GetComponentInParent<PhysicalContainer>();
                }
            }
            workspace.physical.Submit(new PhysicalInteractionIntent(PhysicalIntentKind.Hover,HoveredPhysical));
            if(workspace.SelectedPhysical)workspace.SelectedPhysical.SetHighlight(true);
        }
        void ClearHover()
        {HoveredPhysical=null;HoveredContainer=null;HoveredShaft=null;HoveredDrawer=null;workspace.physical.Submit(new PhysicalInteractionIntent(PhysicalIntentKind.Hover));}
        public static PhysicalInteractable ResolvePhysical(Collider collider)
        {
            if(!collider)return null;
            var item=collider.GetComponentInParent<PhysicalInteractable>();if(item)return item;
            var sample=collider.GetComponentInParent<TapePatternPreview>();return sample?sample.Source:null;
        }

        void MovePhysical(Ray ray,bool overUI)
        {
            if(overUI||workspace.physical.IsInspecting)return;
            var item=workspace.physical.HeldItem;
            if(workspace.physical.surface.TryRaycast(ray,out var point))
            {
                Quaternion rotation=item.itemType==PhysicalItemType.TravelersNotebook?Quaternion.Euler(90,0,0):item.transform.rotation;
                workspace.physical.Submit(new PhysicalInteractionIntent(PhysicalIntentKind.Move,item,new Pose(point+physicalDragOffset,rotation)));
            }
        }
        void ReleasePhysical(Vector2 pointer,Ray ray,bool overUI)
        {
            var item=workspace.physical.HeldItem;
            var workbench=workspace.journal.GetComponent<NotebookWorkbench>();
            if(item.itemType==PhysicalItemType.TravelersNotebook&&!overUI&&workbench&&workbench.IsCentralDrop(ray))
            { if(!workbench.Use(item))workspace.physical.Cancel();return; }
            if(item.itemType==PhysicalItemType.Tape)
            {
                var shaft=FindObjectsByType<WorkspaceShaft>(FindObjectsSortMode.None).Where(s=>s.IsExtended&&s.slot)
                    .Select(s=>new{shaft=s,screen=workspace.lens.WorldToScreenPoint(s.slot.transform.position)})
                    .Where(candidate=>candidate.screen.z>0)
                    .OrderBy(candidate=>Vector2.Distance(pointer,candidate.screen)).FirstOrDefault();
                if(shaft!=null&&Vector2.Distance(pointer,shaft.screen)<45*Screen.width/1920f&&workspace.coordinator.Install(item,shaft.shaft))return;
            }
            if(overUI||!workspace.physical.surface.TryRaycast(ray,out _))workspace.physical.Cancel();
            else workspace.physical.Submit(new PhysicalInteractionIntent(PhysicalIntentKind.Release));
        }
        void EndMechanism(bool cancel,bool refreshTargets=true)
        {
            if(DraggedDrawer){var drawer=DraggedDrawer;drawer.EndDrag();if(cancel)drawer.SetOpenAmount(mechanismOrigin);DraggedDrawer=null;}
            if(DraggedShaft){var shaft=DraggedShaft;shaft.EndDrag();if(cancel)shaft.SetOpenAmount(mechanismOrigin);DraggedShaft=null;}
            if(refreshTargets)LogVisibleTargets();
        }

        [Serializable] public sealed class Target{public string id,type,pageId,containerId;public float x,y,openX,openY,closedX,closedY;}
        [Serializable] sealed class InputMode
        {
            public string draggedDrawer,draggedShaft,physicalHeldItem,materialHeldItem,pendingTape;
            public string selectedPhysical,selectedTape,inspectedInstance;
            public Vector3 selectedPhysicalPosition,physicalHeldPosition;
            public bool tapeLengthSelecting,isInspecting;
            public float tapeLengthMeters;
            public int inspectedSpread;
            public bool sceneInputAllowed,isEditing,leftHeld,leftDown,leftUp,hasBegun,isBlocking,blocksLegacyInput;
            public bool mouseLookEnabled,forwardFacing;
            public float seatedYaw,seatedPitch;
            public Vector3 cameraPosition;
            public int frame;
        }
        void LogMode()
        {
            Debug.Log("[WorkspaceInput] Mode "+JsonUtility.ToJson(new InputMode{
                draggedDrawer=DraggedDrawer?DraggedDrawer.drawerId:null,draggedShaft=DraggedShaft?DraggedShaft.shaftId:null,
                physicalHeldItem=workspace&&workspace.physical&&workspace.physical.HeldItem?workspace.physical.HeldItem.itemInstanceId:null,
                selectedPhysical=workspace&&workspace.SelectedPhysical?workspace.SelectedPhysical.itemInstanceId:null,
                selectedPhysicalPosition=workspace&&workspace.SelectedPhysical?workspace.SelectedPhysical.transform.position:Vector3.zero,
                selectedTape=workspace&&workspace.SelectedTape?workspace.SelectedTape.itemInstanceId:null,
                physicalHeldPosition=workspace&&workspace.physical&&workspace.physical.HeldItem?workspace.physical.HeldItem.transform.position:Vector3.zero,
                tapeLengthSelecting=workspace&&workspace.TapeLengthSelecting,tapeLengthMeters=workspace?workspace.TapeLengthMeters:0,
                isInspecting=workspace&&workspace.physical&&workspace.physical.IsInspecting,
                inspectedInstance=workspace&&workspace.bookInspection&&workspace.bookInspection.IsActive?workspace.bookInspection.Item.itemInstanceId:null,
                inspectedSpread=workspace&&workspace.bookInspection?workspace.bookInspection.CurrentSpread:0,
                materialHeldItem=workspace&&workspace.Ready?workspace.Service.HeldItem?.instanceId:null,pendingTape=pendingTape?pendingTape.itemInstanceId:null,
                sceneInputAllowed=workspace&&workspace.desk&&workspace.desk.SceneInputAllowed,isEditing=workspace&&workspace.journal&&workspace.journal.IsEditing,
                hasBegun=workspace&&workspace.desk&&workspace.desk.HasBegun,isBlocking=workspace&&workspace.desk&&workspace.desk.IsBlocking,
                mouseLookEnabled=workspace&&workspace.journal&&workspace.journal.cameraRig&&workspace.journal.cameraRig.MouseLookEnabled,
                forwardFacing=workspace&&workspace.journal&&workspace.journal.cameraRig&&workspace.journal.cameraRig.ForwardFacing,
                seatedYaw=workspace&&workspace.journal&&workspace.journal.cameraRig?workspace.journal.cameraRig.SeatedYawDegrees:0,
                seatedPitch=workspace&&workspace.journal&&workspace.journal.cameraRig?workspace.journal.cameraRig.SeatedPitchDegrees:0,
                cameraPosition=workspace&&workspace.lens?workspace.lens.transform.position:Vector3.zero,
                leftHeld=Input.GetMouseButton(0),leftDown=Input.GetMouseButtonDown(0),leftUp=Input.GetMouseButtonUp(0),blocksLegacyInput=RuntimeInputGate.BlocksLegacyInput,frame=Time.frameCount}));
        }
        public void LogVisibleTargets()
        {
            // Disable/scene teardown can destroy the camera before this router.
            // Target diagnostics must never traverse a partially destroyed scene.
            if(!isActiveAndEnabled||!workspace||!workspace.Ready||!workspace.lens||!workspace.journal||!workspace.desk||
                !workspace.journal.pageView||workspace.journal.Document==null||!workspace.physical||!workspace.physical.surface)return;
            Physics.SyncTransforms();
            LogBookDrops();
            var legacy=FindFirstObjectByType<PhysicalMouseInputAdapter>();if(legacy)legacy.LogVisibleTargets();
            foreach(var item in FindObjectsByType<PhysicalInteractable>(FindObjectsSortMode.None))
                LogRayTarget(item.itemInstanceId,item.itemType.ToString(),item);
            foreach(var book in workspace.releaseBooks)
            {
                if(!book||book.pageAnchors==null)continue;
                var data=workspace.Service.FindBook(book.bookId);if(data==null||!data.isOpen)continue;
                for(int side=0;side<book.pageAnchors.Length;side++)
                {
                    int index=data.currentPage+side;if(index>=data.pages.Count)continue;
                    var anchor=book.pageAnchors[side];if(!anchor)continue;var size=ReleaseBookView.LocalPageSize(anchor,book.pageSize);
                    var p=workspace.lens.WorldToScreenPoint(anchor.TransformPoint(new Vector3(size.x*.5f,size.y*.5f,0)));
                    if(p.z>0&&!OverUI(p))Debug.Log("[WorkspaceInput] Page "+JsonUtility.ToJson(new Target{id=book.bookId,type="ReleaseBookPage",pageId=data.pages[index].pageId,x=p.x/Screen.width,y=1-p.y/Screen.height}));
                }
            }
            foreach(var shaft in FindObjectsByType<WorkspaceShaft>(FindObjectsSortMode.None))LogRayTarget(shaft.shaftId,"Shaft",shaft);
            foreach(var drawer in FindObjectsByType<WorkspaceDrawer>(FindObjectsSortMode.None))LogRayTarget(drawer.drawerId,"Drawer",drawer);
            foreach(var shaft in FindObjectsByType<WorkspaceShaft>(FindObjectsSortMode.None))
                if(shaft.slot)LogProjected(shaft.shaftId,"ShaftSlot",shaft.slot.transform.position,shaft.slot.slotId);
            LogProjected("work-area-a","WorkArea",workspace.physical.surface.ToWorld(new Vector3(.27f,0,-.22f)));
            LogProjected("work-area-b","WorkArea",workspace.physical.surface.ToWorld(new Vector3(.27f,0,.02f)));
            foreach(JournalSide side in new[]{JournalSide.Left,JournalSide.Right})
            {
                var p=workspace.lens.WorldToScreenPoint(workspace.journal.pageView.WorldFromNormalized(side,new Vector2(.5f,.5f)));
                if(p.z>0&&!OverUI(p))Debug.Log("[WorkspaceInput] Page "+JsonUtility.ToJson(new Target{id="journal",type="JournalPage",pageId=workspace.journal.Document.GetPage(side).pageId,x=p.x/Screen.width,y=1-p.y/Screen.height}));
            }
        }
        [Serializable] sealed class BookDropTarget { public string instanceId;public Vector3 worldPosition;public float x,y; }
        void LogBookDrops()
        {
            if(!nativeValidation||!workspace.SelectedPhysical||!workspace.IsReleaseBook(workspace.SelectedPhysical)||workspace.InteractionBusy)return;
            var item=workspace.SelectedPhysical;var surface=workspace.physical.surface;
            foreach(float x in new[]{-.30f,-.38f,.38f,.43f})foreach(float z in new[]{-.06f,-.12f,-.19f})
            {
                var pose=surface.RestingPose(item,surface.ToWorld(new Vector3(x,0,z)),Quaternion.identity);
                if(Vector3.Distance(pose.position,item.transform.position)<.04f||!surface.IsValidPlacement(item,pose))continue;
                var screen=workspace.lens.WorldToScreenPoint(pose.position);
                if(screen.z<=0||screen.x<0||screen.x>=Screen.width||screen.y<0||screen.y>=Screen.height||OverUI(screen))continue;
                Debug.Log("[WorkspaceInput] BookDrop "+JsonUtility.ToJson(new BookDropTarget{instanceId=item.itemInstanceId,worldPosition=pose.position,x=screen.x/Screen.width,y=1-screen.y/Screen.height}));
            }
        }
        bool OverUI(Vector2 point)=>workspace.desk.IsPointerOverInterface(point)||(workspace.hud&&workspace.hud.Contains(point))||
            (DeskViewControls.Instance&&DeskViewControls.Instance.Contains(point))||(workspace.bookInspection&&workspace.bookInspection.Contains(point))||
            (DrawerStockIndex.Instance&&DrawerStockIndex.Instance.Contains(point))||(MaterialDiscoveryPresentation.Instance&&MaterialDiscoveryPresentation.Instance.Contains(point))||
            (DreamGameplayController.Instance&&DreamGameplayController.Instance.Contains(point))||(AtelierHub.Instance&&AtelierHub.Instance.Contains(point))||
            (CommissionDeskController.Instance&&CommissionDeskController.Instance.ContainsPointer(point));
        void LogRayTarget(string id,string type,Component owner)
        {
            var colliders=owner.GetComponentsInChildren<Collider>(true).AsEnumerable();
            if(owner is PhysicalInteractable physicalOwner)
                colliders=colliders.Concat(FindObjectsByType<TapePatternPreview>(FindObjectsSortMode.None).Where(p=>p.Source==physicalOwner).SelectMany(p=>p.GetComponentsInChildren<Collider>(true)));
            foreach(var collider in colliders.Distinct())
            {
                if(!collider.enabled||!collider.gameObject.activeInHierarchy||collider.isTrigger)continue;
                Bounds bounds=collider.bounds;
                foreach(int y in verticalSamples)foreach(int x in lateralSamples)foreach(int z in lateralSamples)
                {
                    var point=workspace.lens.WorldToScreenPoint(bounds.center+new Vector3(bounds.extents.x*x*.35f,bounds.extents.y*y*.6f,bounds.extents.z*z*.35f));
                    // Native pointer placement uses integer client pixels. A
                    // floating ray grazing an edge can round onto a drawer or
                    // shaft beside the intended object, so log an interior pixel.
                    point.x=Mathf.Round(point.x);point.y=Screen.height-Mathf.Round(Screen.height-point.y);
                    if(point.z<=0||point.x<4||point.y<4||point.x>=Screen.width-4||point.y>=Screen.height-4||OverUI(point))continue;
                    if(!RayHitsOwner(owner,point,out var hit))continue;
                    bool interior=true;
                    for(int dy=-1;dy<=1&&interior;dy++)for(int dx=-1;dx<=1;dx++)
                        if((dx!=0||dy!=0)&&!RayHitsOwner(owner,(Vector2)point+new Vector2(dx,dy),out _)){interior=false;break;}
                    if(!interior)continue;
                    var target=new Target{id=id,type=type,x=point.x/Screen.width,y=1-point.y/Screen.height};
                    if(owner is PhysicalInteractable physicalTarget&&physicalTarget.CurrentContainer)target.containerId=physicalTarget.CurrentContainer.name;
                    Vector3 world=workspace.lens.ScreenPointToRay(point).GetPoint(hit.distance),axis=Vector3.zero;float amount=0,travel=0;
                    if(owner is WorkspaceDrawer drawer){axis=drawer.slide.parent?drawer.slide.parent.TransformDirection(drawer.slideAxis.normalized):drawer.slideAxis.normalized;amount=drawer.OpenAmount;travel=drawer.travel;}
                    if(owner is WorkspaceShaft shaft){axis=shaft.rod.parent?shaft.rod.parent.TransformDirection(shaft.slideAxis.normalized):shaft.slideAxis.normalized;amount=shaft.OpenAmount;travel=shaft.travel;}
                    if(travel>0)
                    {
                        var open=workspace.lens.WorldToScreenPoint(world+axis*travel*(1-amount));var closed=workspace.lens.WorldToScreenPoint(world-axis*travel*amount);
                        target.openX=open.x/Screen.width;target.openY=1-open.y/Screen.height;target.closedX=closed.x/Screen.width;target.closedY=1-closed.y/Screen.height;
                    }
                    Debug.Log("[WorkspaceInput] Target "+JsonUtility.ToJson(target));return;
                }
            }
        }
        bool RayHitsOwner(Component owner,Vector2 pixel,out RaycastHit hit)
        {
            hit=default;
            if(OverUI(pixel)||!Physics.Raycast(workspace.lens.ScreenPointToRay(pixel),out hit,3,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))return false;
            var physicalItem=ResolvePhysical(hit.collider);
            return owner is PhysicalInteractable expectedItem?physicalItem==expectedItem:
                !physicalItem&&hit.collider.GetComponentInParent(owner.GetType())==owner;
        }
        void LogProjected(string id,string type,Vector3 world,string containerId=null)
        {var p=workspace.lens.WorldToScreenPoint(world);if(p.z>0)Debug.Log("[WorkspaceInput] Target "+JsonUtility.ToJson(new Target{id=id,type=type,containerId=containerId,x=p.x/Screen.width,y=1-p.y/Screen.height}));}
        void OnApplicationFocus(bool focus){if(!focus){ClearPending();materialDrag=false;if(workspace)workspace.Cancel();EndMechanism(true);}}
        void OnDisable(){if(workspace)workspace.Cancel();EndMechanism(true,false);RuntimeInputGate.WorkspaceOwnsInput=false;RuntimeInputGate.BlocksLegacyInput=false;RuntimeInputGate.BlocksJournalOperations=false;}
    }
}

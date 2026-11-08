using System;
using UnityEngine;
namespace VintageJournal
{
    /// <summary>The only physical-system component reading native mouse/keyboard input.</summary>
    [DefaultExecutionOrder(-3)]
    public sealed class PhysicalMouseInputAdapter : MonoBehaviour
    {
        public PhysicalInteractionController controller;
        public Camera lens;
        public DeskUI desk;
        public JournalController journal;
        public PhysicalWorkspaceHUD hud;
        public PhysicalInteractable SelectedItem {get; private set;}
        public PhysicalContainer HoveredContainer {get;private set;}
        public bool OwnsPointer => controller && (controller.HeldItem || controller.HoveredItem || HoveredContainer || (hud&&hud.Contains(Input.mousePosition)));
        public bool CanInteract => desk && desk.SceneInputAllowed && (!journal || !journal.IsEditing) &&
            (!desk.cameraRig || !desk.cameraRig.IsTransitioning);
        bool logged;
        PhysicalInteractable pendingDrag;
        Vector2 dragOrigin;
        void Awake()
        {
            if(!controller)controller=GetComponent<PhysicalInteractionController>();
            if(!lens)lens=Camera.main;
            if(!desk)desk=FindFirstObjectByType<DeskUI>();
            if(!journal)journal=FindFirstObjectByType<JournalController>();
            if(!hud)hud=GetComponent<PhysicalWorkspaceHUD>();
        }
        void Update()
        {
            if(RuntimeInputGate.WorkspaceOwnsInput)return;
            if(!controller||!lens)return;
            if(!CanInteract||!Application.isFocused)
            {
                if(controller.HeldItem)controller.Cancel();
                controller.Submit(new PhysicalInteractionIntent(PhysicalIntentKind.Hover));
                HoveredContainer=null; return;
            }
            if(!logged){logged=true;LogVisibleTargets();}
            if(Input.GetKeyDown(KeyCode.F8))LogVisibleTargets();
            if(Input.GetKeyDown(KeyCode.Escape))
            {pendingDrag=null;controller.Cancel();if(desk)desk.ConsumePointer();return;}
            if(Input.GetKeyDown(KeyCode.R))
            {
                var item=controller.HeldItem?controller.HeldItem:(controller.HoveredItem?controller.HoveredItem:SelectedItem);
                if(item)controller.Submit(new PhysicalInteractionIntent(PhysicalIntentKind.ReturnHome,item));
                return;
            }
            if(Input.GetKeyDown(KeyCode.E))
            {
                var item=controller.HeldItem?controller.HeldItem:controller.HoveredItem;
                if(item){SelectedItem=item;controller.Submit(new PhysicalInteractionIntent(PhysicalIntentKind.Inspect,item));}
                return;
            }
            Vector2 pointer=Input.mousePosition;
            bool overUI=desk.IsPointerOverInterface(pointer)||(hud&&hud.Contains(pointer));
            if(controller.HeldItem)
            {
                if(controller.IsInspecting)return;
                bool hasTarget=!overUI&&controller.surface.TryRaycast(lens.ScreenPointToRay(pointer),out _);
                if(hasTarget&&controller.surface.TryRaycast(lens.ScreenPointToRay(pointer),out var point))
                {
                    var item=controller.HeldItem;
                    point+=controller.surface.transform.up*item.dimensions.size.y*.5f;
                    controller.Submit(new PhysicalInteractionIntent(PhysicalIntentKind.Move,item,new Pose(point,item.transform.rotation)));
                }
                if(Input.GetMouseButtonUp(1))
                {
                    if(!hasTarget)controller.Cancel();
                    else controller.Submit(new PhysicalInteractionIntent(PhysicalIntentKind.Release));
                }
                return;
            }
            if(overUI){controller.Submit(new PhysicalInteractionIntent(PhysicalIntentKind.Hover));HoveredContainer=null;return;}
            if(pendingDrag&&Input.GetMouseButton(1)&&Vector2.Distance(pointer,dragOrigin)>Mathf.Max(6,9*Screen.width/1920f))
            {controller.Submit(new PhysicalInteractionIntent(PhysicalIntentKind.Grab,pendingDrag));pendingDrag=null;desk.ConsumePointer();return;}
            if(Input.GetMouseButtonUp(1))pendingDrag=null;
            HoverAt(pointer);
            if(Input.GetMouseButtonDown(0))
            {
                if(controller.HoveredItem)
                {if(SelectedItem)SelectedItem.SetHighlight(false);SelectedItem=controller.HoveredItem;SelectedItem.SetHighlight(true);desk.ConsumePointer();}
                else if(HoveredContainer)Toggle(HoveredContainer);
            }
            if(Input.GetMouseButtonDown(1)&&controller.HoveredItem){pendingDrag=controller.HoveredItem;dragOrigin=pointer;desk.ConsumePointer();}
        }
        public PhysicalInteractable HoverAt(Vector2 screenPosition)
        {
            PhysicalInteractable item=null;HoveredContainer=null;
            if(lens&&Physics.Raycast(lens.ScreenPointToRay(screenPosition),out var hit,3,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
            {
                item=hit.collider.GetComponentInParent<PhysicalInteractable>();
                if(!item)HoveredContainer=hit.collider.GetComponentInParent<PhysicalContainer>();
            }
            controller.Submit(new PhysicalInteractionIntent(PhysicalIntentKind.Hover,item));
            return item;
        }
        public void Toggle(PhysicalContainer container)
        {
            if(container&&container.ToggleOpen())
            {Debug.Log("[PhysicalInput] Container:"+container.name+":"+container.isOpen);LogVisibleTargets();}
        }
        [Serializable] public sealed class Target {public string id,type,name;public float x,y;}
        public void LogVisibleTargets()
        {
            if(!lens)return;
            Physics.SyncTransforms();
            if(controller&&controller.surface)
            {
                var free=lens.WorldToScreenPoint(controller.surface.ToWorld(new Vector3(.28f,0,-.23f)));
                Debug.Log("[PhysicalInput] PlacementPoint "+JsonUtility.ToJson(new Target {x=free.x/Screen.width,y=1-free.y/Screen.height}));
            }
            foreach(var item in FindObjectsByType<PhysicalInteractable>(FindObjectsSortMode.None))
            {
                if(!item.interactionCollider||!item.interactionCollider.enabled)continue;
                var b=item.interactionCollider.bounds;bool found=false;
                for(int z=-2;z<=2&&!found;z++)for(int x=-2;x<=2&&!found;x++)
                {
                    var p=lens.WorldToScreenPoint(b.center+new Vector3(b.extents.x*x*.3f,b.extents.y,b.extents.z*z*.3f));
                    if(p.z<=0||p.x<0||p.y<0||p.x>=Screen.width||p.y>=Screen.height)continue;
                    if(Physics.Raycast(lens.ScreenPointToRay(p),out var hit,3)&&hit.collider.GetComponentInParent<PhysicalInteractable>()==item)
                    {
                        Debug.Log("[PhysicalInput] Target "+JsonUtility.ToJson(new Target{id=item.itemInstanceId,type=item.itemType.ToString(),name=item.displayName,x=p.x/Screen.width,y=1-p.y/Screen.height}));found=true;
                    }
                }
            }
        }
        void OnApplicationFocus(bool focus){if(!focus&&controller)controller.Cancel();}
        void OnDisable(){if(controller)controller.Cancel();}
    }
}

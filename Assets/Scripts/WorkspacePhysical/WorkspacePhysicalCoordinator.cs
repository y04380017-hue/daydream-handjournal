using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace VintageJournal
{
    /// <summary>Reconciles scene entities with the single persisted ownership registry.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class WorkspacePhysicalCoordinator : MonoBehaviour
    {
        public CraftWorkspaceService Service;
        public PhysicalInteractionController physical;
        public string LastMessage{get;private set;}
        PhysicalInteractable[] items=Array.Empty<PhysicalInteractable>();
        PhysicalStorageSlot[] slots=Array.Empty<PhysicalStorageSlot>();
        WorkspaceDrawer[] drawers=Array.Empty<WorkspaceDrawer>();
        WorkspaceShaft[] shafts=Array.Empty<WorkspaceShaft>();
        CraftWorkspaceService bound;
        PhysicalInteractionController observedPhysical;
        bool reconcilePending,applying,syncing;
        readonly Dictionary<WorkspaceDrawer,float> syncedDrawers=new Dictionary<WorkspaceDrawer,float>();
        readonly Dictionary<WorkspaceShaft,float> syncedShafts=new Dictionary<WorkspaceShaft,float>();
        float nextSync;
        public void RegisterScene()
        {
            items=GetComponentsInChildren<PhysicalInteractable>(true);
            if(items.Length==0)items=FindObjectsByType<PhysicalInteractable>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            slots=FindObjectsByType<PhysicalStorageSlot>(FindObjectsSortMode.None);
            drawers=FindObjectsByType<WorkspaceDrawer>(FindObjectsSortMode.None);
            shafts=FindObjectsByType<WorkspaceShaft>(FindObjectsSortMode.None);
            if(!physical)physical=FindFirstObjectByType<PhysicalInteractionController>();
            BindPhysical();
            foreach(var item in items)
            {
                item.Initialize();
                if(item.homeSlot&&string.IsNullOrEmpty(item.homeSlot.slotId))item.homeSlot.slotId="home:"+item.itemInstanceId;
            }
            foreach(var shaft in shafts)if(shaft.slot&&string.IsNullOrEmpty(shaft.slot.slotId))shaft.slot.slotId=shaft.shaftId;
            EnsureBound();
            if(Service==null)return;
            var records=items.Select(item=>new CraftItemRecord{instanceId=item.itemInstanceId,assetId=item.assetId,
                kind=Kind(item.itemType),owner=item.CurrentSlot?CraftOwner.Stock:CraftOwner.Desktop,
                containerId=item.CurrentSlot?item.CurrentSlot.slotId:"desktop",worldPosition=item.transform.position,
                worldEulerAngles=item.transform.eulerAngles,sizeMeters=MetricSize(item)});
            var layout=FindFirstObjectByType<DeskLayoutBindings>();
            bool registered;
            if(layout&&layout.placements!=null)
            {
                var targets=layout.placements.Where(p=>p!=null&&p.item&&p.homeSlot).Select(p=>new CraftDeskLayoutTarget{
                    instanceId=p.item.itemInstanceId,homeSlotId=p.homeSlot.slotId,
                    displaySlotId=p.desiredDisplaySlot?p.desiredDisplaySlot.slotId:null,
                    homePosition=p.homeSlot.WorldPose.position,homeEulerAngles=p.homeSlot.WorldPose.rotation.eulerAngles,
                    displayPosition=p.desiredDisplaySlot?p.desiredDisplaySlot.WorldPose.position:p.homeSlot.WorldPose.position,
                    displayEulerAngles=p.desiredDisplaySlot?p.desiredDisplaySlot.WorldPose.rotation.eulerAngles:p.homeSlot.WorldPose.rotation.eulerAngles,
                    migrateLegacyHome=p.migrateLegacyHome,relocateStockOnMigration=p.relocateStockOnMigration,
                    relocateDesktopOnMigration=p.relocateDesktopOnMigration,legacySlotIds=p.legacySlotIds,releaseBookId=p.releaseBookId});
                registered=Service.RegisterDeskLayout(records,targets,layout.version);
            }
            else registered=Service.RegisterPhysical(records);
            if(!registered){LastMessage=Service.LastMessage;Debug.LogError("[WorkspacePhysical] "+LastMessage);}
        }
        static CraftItemKind Kind(PhysicalItemType type)
        {
            switch(type){case PhysicalItemType.Tape:return CraftItemKind.TapeRoll;case PhysicalItemType.Sticker:return CraftItemKind.Sticker;
                case PhysicalItemType.Note:return CraftItemKind.Note;case PhysicalItemType.LoosePage:return CraftItemKind.LoosePage;
                case PhysicalItemType.ReleaseBook:return CraftItemKind.ReleaseBook;case PhysicalItemType.TapeStrip:return CraftItemKind.TapeStrip;
                case PhysicalItemType.Tool:return CraftItemKind.Tool;
                default:return CraftItemKind.TN;}
        }
        static Vector2 MetricSize(PhysicalInteractable item)
        {
            var v=item.dimensions.size;
            return item.itemType==PhysicalItemType.TravelersNotebook?new Vector2(v.x,v.y):new Vector2(v.x,v.z);
        }
        void EnsureBound()
        {
            if(bound==Service)return;
            if(bound!=null)bound.DataChanged-=Changed;
            bound=Service;if(bound!=null)bound.DataChanged+=Changed;
        }
        void Changed(){if(!applying&&!syncing)reconcilePending=true;}
        void BindPhysical()
        {
            if(observedPhysical==physical)return;
            if(observedPhysical)observedPhysical.Settled-=PhysicalSettled;
            observedPhysical=physical;
            if(observedPhysical)observedPhysical.Settled+=PhysicalSettled;
        }
        void PhysicalSettled(PhysicalInteractable item){if(!applying)SyncPhysical();}
        bool LocalDevicesDirty()=>drawers.Any(d=>syncedDrawers.TryGetValue(d,out float amount)&&Mathf.Abs(d.OpenAmount-amount)>.0001f)||
            shafts.Any(s=>syncedShafts.TryGetValue(s,out float amount)&&Mathf.Abs(s.OpenAmount-amount)>.0001f);
        void RememberDevices(){foreach(var d in drawers)syncedDrawers[d]=d.OpenAmount;foreach(var s in shafts)syncedShafts[s]=s.OpenAmount;}
        public void ApplySavedState()
        {
            if(Service==null||applying||physical&&(physical.HeldItem||physical.IsSettling)||drawers.Any(d=>d.IsDragging)||shafts.Any(s=>s.IsDragging))return;
            // A newly dragged joint is a local edit, not a stale copy to overwrite with an unrelated material event.
            if(LocalDevicesDirty())SyncPhysical();
            applying=true;
            try
            {
                foreach(var drawer in drawers){var state=Service.GetDrawerState(drawer.drawerId);if(state!=null)drawer.SetOpenAmount(state.openFraction);}
                foreach(var shaft in shafts){var state=Service.GetDrawerState("shaft:"+shaft.shaftId);if(state!=null)shaft.SetOpenAmount(state.openFraction);}
                // Scene display occupancy can differ from permanent homes after Initialize(). Clear
                // every registered slot, including a serialized display shaft no item still points at.
                foreach(var slot in slots)if(slot.Occupant)slot.Vacate(slot.Occupant);
                foreach(var item in items)if(item.CurrentSlot)item.SetSlot(null);
                foreach(var item in items)
                {
                    var record=Service.GetItem(item.itemInstanceId);if(record==null)continue;
                    bool visible=record.owner==CraftOwner.Stock||record.owner==CraftOwner.Desktop;
                    item.gameObject.SetActive(visible);
                    if(!visible){item.TryTransition(PhysicalItemState.Disabled);continue;}
                    if(record.owner==CraftOwner.Stock)
                    {
                        var slot=slots.FirstOrDefault(s=>s.slotId==record.containerId)??item.homeSlot;
                        if(slot&&slot.TryStore(item,true))
                        {
                            item.SetSlot(slot);item.TryTransition(PhysicalItemState.Disabled);item.TryTransition(PhysicalItemState.Stored);
                            item.transform.SetPositionAndRotation(slot.WorldPose.position,slot.WorldPose.rotation);
                        }
                        else{LastMessage="存档收纳位冲突："+record.instanceId;Debug.LogError("[WorkspacePhysical] "+LastMessage);}
                    }
                    else
                    {
                        item.TryTransition(PhysicalItemState.Disabled);item.TryTransition(PhysicalItemState.Available);
                        item.TryTransition(PhysicalItemState.Grabbed);item.TryTransition(PhysicalItemState.Placed);
                        item.transform.SetPositionAndRotation(record.worldPosition,Quaternion.Euler(record.worldEulerAngles));
                    }
                    item.RefreshVisibility();
                }
                reconcilePending=false;
                RememberDevices();
            }
            finally{applying=false;}
        }
        public bool Install(PhysicalInteractable item,WorkspaceShaft shaft)
        {
            if(!physical||!item||item.itemType!=PhysicalItemType.Tape||!shaft||!shaft.IsExtended||!shaft.slot)return false;
            if(physical.HeldItem!=item&&!physical.Submit(new PhysicalInteractionIntent(PhysicalIntentKind.Grab,item)))return false;
            bool ok=physical.CommitToSlot(shaft.slot);
            LastMessage=ok?"胶带已套入横轴，请推回轴杆。":physical.LastResult;
            return ok;
        }
        public bool TakeFromShaft(WorkspaceShaft shaft)
        {
            if(!physical||!shaft||!shaft.IsExtended||!shaft.slot||!shaft.slot.Occupant)return false;
            return physical.Submit(new PhysicalInteractionIntent(PhysicalIntentKind.Grab,shaft.slot.Occupant));
        }
        public bool MoveToSlot(PhysicalInteractable item,PhysicalStorageSlot slot)
        {
            if(!physical||!item||!slot)return false;
            if(physical.HeldItem!=item&&!physical.Submit(new PhysicalInteractionIntent(PhysicalIntentKind.Grab,item)))return false;
            return physical.CommitToSlot(slot);
        }
        public bool ReturnToHome(PhysicalInteractable item)
        {
            if(!physical||!item)return false;
            return physical.Submit(new PhysicalInteractionIntent(PhysicalIntentKind.ReturnHome,item));
        }
        public void SyncPhysical()
        {
            if(Service==null||applying||physical&&(physical.HeldItem||physical.IsSettling)||drawers.Any(d=>d.IsDragging)||shafts.Any(s=>s.IsDragging))return;
            syncing=true;
            try
            {
            foreach(var item in items)
            {
                var record=Service.GetItem(item.itemInstanceId);
                if(record==null||record.owner!=CraftOwner.Stock&&record.owner!=CraftOwner.Desktop)continue;
                bool stored=item.CurrentSlot;string location=stored?item.CurrentSlot.slotId:"desktop";
                bool changed=record.containerId!=location||record.owner!=(stored?CraftOwner.Stock:CraftOwner.Desktop)||
                    (!stored&&(Vector3.Distance(record.worldPosition,item.transform.position)>.001f||Quaternion.Angle(Quaternion.Euler(record.worldEulerAngles),item.transform.rotation)>1));
                if(changed&&!Service.UpdatePhysicalLocation(item.itemInstanceId,location,item.CurrentWorldPose,stored))LastMessage=Service.LastMessage;
            }
            foreach(var drawer in drawers)Service.SetDrawerState(drawer.drawerId,drawer.container&&drawer.container.isOpen,drawer.OpenAmount);
            foreach(var shaft in shafts)Service.SetDrawerState("shaft:"+shaft.shaftId,shaft.IsExtended,shaft.OpenAmount);
            RememberDevices();
            }
            finally{syncing=false;}
        }
        void LateUpdate()
        {
            BindPhysical();EnsureBound();if(Service==null)return;
            if(reconcilePending)ApplySavedState();
            foreach(var item in items)if(item&&item.gameObject.activeInHierarchy&&item.CurrentSlot&&
                (item.CurrentState==PhysicalItemState.Stored||item.CurrentState==PhysicalItemState.Hovered))
                item.transform.SetPositionAndRotation(item.CurrentSlot.WorldPose.position,item.CurrentSlot.WorldPose.rotation);
            if(Time.unscaledTime>=nextSync){nextSync=Time.unscaledTime+.3f;SyncPhysical();}
        }
        void OnApplicationPause(bool paused){if(paused){SyncPhysical();Service?.Flush();}}
        void OnApplicationQuit(){SyncPhysical();Service?.Flush();}
        void OnDestroy(){if(bound!=null)bound.DataChanged-=Changed;if(observedPhysical)observedPhysical.Settled-=PhysicalSettled;}
    }
}

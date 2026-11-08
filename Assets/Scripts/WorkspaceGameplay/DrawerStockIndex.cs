using System;
using System.Collections;
using System.Linq;
using UnityEngine;
namespace VintageJournal
{
    /// <summary>An input adapter for dense, real-size stock. Extraction moves the same instance to one exclusive work position.</summary>
    public sealed class DrawerStockIndex : MonoBehaviour
    {
        public static DrawerStockIndex Instance {get;private set;}
        public WorkspaceGameplayController workspace;
        public DeskLayoutBindings layout;
        public PhysicalStorageSlot[] extractionSlots;
        public bool Expanded {get;private set;}
        public bool Busy {get;private set;}
        public string Status {get;private set;}="从目录抽取叠层里的同一份素材，不会复制。";
        int page,category;
        GUIStyle label,button;
        Font font;
        public static readonly Rect EntryRect = new Rect(24,392,326,52),PanelRect=new Rect(1476,284,420,512);
        void Awake(){Instance=this;}
        public WorkspaceDrawer ActiveDrawer {
            get {
                if(!workspace||!workspace.Ready||!layout||!workspace.journal.cameraRig)return null;
                var view=workspace.journal.cameraRig.ActiveDeskView;
                return view==DeskViewKind.LeftDrawer?layout.leftDrawer:view==DeskViewKind.RightDrawer?layout.rightDrawer:null;
            }
        }
        public bool IsVisible=>workspace&&workspace.Ready&&workspace.desk.HasBegun&&!workspace.desk.IsBlocking&&!AtelierTutorial.Blocking&&
            !(AtelierHub.Instance&&AtelierHub.Instance.IsOpen)&&!(CommissionDeskController.Instance&&CommissionDeskController.Instance.PanelOpen)&&
            !(DreamGameplayController.Instance&&DreamGameplayController.Instance.BagOpen)&&!(workspace.bookInspection&&workspace.bookInspection.IsActive)&&
            !workspace.physical.IsInspecting&&!(DeskViewControls.Instance&&DeskViewControls.Instance.Expanded)&&ActiveDrawer&&ActiveDrawer.container.IsAccessibleHierarchy;
        public bool Contains(Vector2 pixel){if(!IsVisible)return false;var p=new Vector2(pixel.x*1920/Screen.width,(Screen.height-pixel.y)*1080/Screen.height);return Expanded||EntryRect.Contains(p);}
        public void Close(){Expanded=false;}
        public PhysicalInteractable[] ItemsFor(WorkspaceDrawer drawer)
        {
            if(!layout||!drawer||!workspace||!workspace.Ready)return Array.Empty<PhysicalInteractable>();
            return layout.placements.Select(p=>p.item).Where(i=>i&&i.gameObject.activeInHierarchy&&i.CurrentSlot&&
                i.CurrentSlot.transform.IsChildOf(drawer.slide)&&workspace.Service.GetItem(i.itemInstanceId)?.owner==CraftOwner.Stock)
                .OrderBy(i=>i.itemType).ThenBy(i=>i.assetId).ThenBy(i=>i.itemInstanceId).ToArray();
        }
        public bool RequestExtract(PhysicalInteractable item)
        {
            if(Busy||!workspace||!workspace.Ready||workspace.InteractionBusy||!item||!item.CurrentSlot||!item.CurrentSlot.IsAccessible)return false;
            var drawer=item.CurrentSlot.GetComponentInParent<WorkspaceDrawer>();
            if(drawer!=layout.leftDrawer&&drawer!=layout.rightDrawer)return false;
            var slot=extractionSlots.FirstOrDefault(s=>s&&s.acceptedType==item.itemType);
            if(!slot)return false;
            Expanded=false;StartCoroutine(Extract(item,slot));return true;
        }
        IEnumerator Extract(PhysicalInteractable item,PhysicalStorageSlot target)
        {
            Busy=true;
            try {
                foreach(var slot in extractionSlots){
                    var old=slot?slot.Occupant:null;
                    if(!old||old==item)continue;
                    var oldDrawer=old.homeSlot?old.homeSlot.GetComponentInParent<WorkspaceDrawer>():null;
                    if(oldDrawer&&!oldDrawer.container.IsAccessibleHierarchy)oldDrawer.SetOpenAmount(1);
                    if(!workspace.coordinator.ReturnToHome(old)){Status="先将取用台上的素材归位。";yield break;}
                    while(workspace.physical.IsSettling)yield return null;
                    if(old.CurrentSlot!=old.homeSlot){Status=workspace.physical.HeldItem==old?"归位路线被挡住。物品仍在手中，可按ESC恢复或移开障碍。":"归位未完成，素材保留原归属。";yield break;}
                }
                var pose=workspace.physical.surface.RestingPose(item,target.transform.position,Quaternion.identity);
                target.transform.SetPositionAndRotation(pose.position,pose.rotation);
                if(!workspace.physical.surface.IsValidPlacement(item,pose)){Status="桌侧取用台被占用，请先移开物品。";yield break;}
                if(!workspace.coordinator.MoveToSlot(item,target)){Status=workspace.physical.LastResult;yield break;}
                while(workspace.physical.IsSettling)yield return null;
                if(item.CurrentSlot!=target){Status=workspace.physical.HeldItem==item?"取用路线被挡住。物品仍在手中，可按ESC恢复或移开障碍。":"抽取未完成，素材保留原归属。";yield break;}
                workspace.coordinator.SyncPhysical();workspace.SelectPhysical(item);
                Status="已抽到桌侧取用台："+item.displayName+"。右拖使用，R归位。";
            }finally{Busy=false;}
        }
        void Styles(){if(font)return;font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","Arial"},20);label=new GUIStyle(GUI.skin.label){font=font,fontSize=20,wordWrap=true};label.normal.textColor=DaydreamPalette.Ink;button=new GUIStyle(GUI.skin.button){font=font,fontSize=20,wordWrap=true};button.normal.background=button.hover.background=button.active.background=Texture2D.whiteTexture;button.normal.textColor=button.hover.textColor=button.active.textColor=DaydreamPalette.Ink;}
        bool Click(Rect r,string text){if(!GUI.Button(r,text,button))return false;workspace.desk.ConsumePointer();RuntimeInputGate.Consume();return true;}
        void OnGUI()
        {
            if(!IsVisible){Expanded=false;return;}Styles();var matrix=GUI.matrix;var bg=GUI.backgroundColor;int depth=GUI.depth;
            GUI.matrix=Matrix4x4.Scale(new Vector3(Screen.width/1920f,Screen.height/1080f,1));GUI.backgroundColor=DaydreamPalette.Paper;GUI.depth=-145;
            try{
                if(Expanded&&Event.current.type==EventType.MouseDown&&!PanelRect.Contains(Event.current.mousePosition)&&!EntryRect.Contains(Event.current.mousePosition)){
                    Expanded=false;workspace.desk.ConsumePointer();RuntimeInputGate.Consume();Event.current.Use();return;
                }
                if(Click(EntryRect,Expanded?"合上抽屉素材目录":"抽屉素材目录 · 取出叠层素材")){Expanded=!Expanded;page=0;}
                if(!Expanded)return;
                GUI.BeginGroup(PanelRect);
                try{DrawPanel();}finally{GUI.EndGroup();}
            }finally{GUI.enabled=true;GUI.matrix=matrix;GUI.backgroundColor=bg;GUI.depth=depth;}
        }
        void DrawPanel()
        {
                var c=GUI.color;GUI.color=DaydreamPalette.Cream;GUI.DrawTexture(new Rect(0,0,PanelRect.width,PanelRect.height),Texture2D.whiteTexture);GUI.color=c;
                var all=ItemsFor(ActiveDrawer);var types=all.Select(i=>i.itemType).Distinct().ToArray();
                if(types.Length==0){GUI.Label(new Rect(18,30,380,120),"这里的素材已取出或用进页面。归位后会重新出现在目录。",label);return;}
                category=Mathf.Clamp(category,0,types.Length-1);
                if(Click(new Rect(18,16,382,48),"分类："+TypeName(types[category]))){category=(category+1)%types.Length;page=0;}
                var items=all.Where(i=>i.itemType==types[category]).ToArray();int pages=Mathf.Max(1,(items.Length+5)/6);page=Mathf.Clamp(page,0,pages-1);
                GUI.enabled=!Busy&&!workspace.InteractionBusy;
                var shown=items.Skip(page*6).Take(6).ToArray();
                for(int n=0;n<shown.Length;n++){
                    var item=shown[n];GUI.Label(new Rect(18,76+n*52,244,46),item.displayName,label);
                    if(Click(new Rect(274,76+n*52,126,44),"抽出取用"))RequestExtract(item);
                }
                GUI.enabled=true;
                if(Click(new Rect(18,396,118,44),"上一页"))page=Mathf.Max(0,page-1);
                GUI.Label(new Rect(148,400,112,44),(page+1)+" / "+pages,label);
                if(Click(new Rect(274,396,126,44),"下一页"))page=Mathf.Min(pages-1,page+1);
                GUI.Label(new Rect(18,450,382,54),Status,label);
        }
        static string TypeName(PhysicalItemType t){switch(t){case PhysicalItemType.Tape:return "胶带";case PhysicalItemType.LoosePage:return "内页";case PhysicalItemType.Sticker:return "贴纸";case PhysicalItemType.Note:return "便签";case PhysicalItemType.Tool:return "笔";default:return t.ToString();}}
        void OnDestroy(){if(Instance==this)Instance=null;if(font)Destroy(font);}
    }
}

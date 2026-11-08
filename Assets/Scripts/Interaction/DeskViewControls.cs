using System;
using System.Collections;
using UnityEngine;

namespace VintageJournal
{
    /// <summary>Named, reversible seated viewpoints. No camera transforms or raw hardware input here.</summary>
    [DefaultExecutionOrder(-200)]
    public sealed class DeskViewControls : MonoBehaviour
    {
        public static DeskViewControls Instance {get;private set;}
        public DeskCamera cameraRig;
        public bool Expanded {get;private set;}
        public bool RequestPending {get;private set;}
        WorkspaceGameplayController workspace;
        GUIStyle button,small;
        Font font;
        public static readonly Rect ToggleRect=new Rect(24,274,252,52),MenuRect=new Rect(24,332,286,386);
        public static readonly Rect MouseLookRect=new Rect(24,334,122,44),ForwardRect=new Rect(154,334,122,44);
        public static readonly Rect LookHintRect=new Rect(24,450,292,72);
        static readonly DeskViewKind[] choices=(DeskViewKind[])Enum.GetValues(typeof(DeskViewKind));
        bool Visible=>workspace&&workspace.Ready&&workspace.desk.HasBegun&&!workspace.desk.IsBlocking&&
            !AtelierTutorial.Blocking&&!(AtelierHub.Instance&&AtelierHub.Instance.IsOpen)&&
            !(DreamGameplayController.Instance&&DreamGameplayController.Instance.BagOpen)&&
            !(CommissionDeskController.Instance&&CommissionDeskController.Instance.PanelOpen)&&
            !(workspace.physical&&workspace.physical.IsInspecting);
        void Awake(){Instance=this;if(!cameraRig)cameraRig=GetComponent<DeskCamera>();}
        void Start(){workspace=FindFirstObjectByType<WorkspaceGameplayController>();}
        void Update(){if(!Visible)Expanded=false;}
        public void CloseMenu(){Expanded=false;if(workspace&&workspace.desk)workspace.desk.ConsumePointer();RuntimeInputGate.Consume();}
        public bool Contains(Vector2 pixel)
        {
            if(!Visible)return false;
            var p=new Vector2(pixel.x*1920/Screen.width,(Screen.height-pixel.y)*1080/Screen.height);
            // An expanded picker owns outside clicks too: the first click dismisses it instead of grabbing a material below.
            return Expanded||ToggleRect.Contains(p)||MouseLookRect.Contains(p)||ForwardRect.Contains(p);
        }
        public bool RequestView(DeskViewKind kind)
        {
            if(!Visible||RequestPending||workspace.InteractionBusy||workspace.journal.IsBusy||workspace.journal.HasPaperPreview||workspace.scrapbook.IsTransforming||
                workspace.GetComponent<WorkspaceInputRouter>().IsDraggingMechanism||cameraRig.IsTransitioning)return false;
            Expanded=false;workspace.desk.ConsumePointer();RuntimeInputGate.Consume();StartCoroutine(Apply(kind));return true;
        }
        IEnumerator Apply(DeskViewKind kind)
        {
            RequestPending=true;
            if(workspace.journal.IsEditing)
            {
                workspace.journal.ExitEditing();
                while(workspace.journal.IsEditing||cameraRig.IsTransitioning)yield return null;
            }
            cameraRig.SelectDeskView(kind);
            RequestPending=false;
        }
        void OnGUI()
        {
            if(!Visible)return;
            if(!font){font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","Arial"},20);button=new GUIStyle(GUI.skin.button){font=font,fontSize=19,wordWrap=true};small=new GUIStyle(GUI.skin.label){font=font,fontSize=17,wordWrap=true};}
            var matrix=GUI.matrix;var background=GUI.backgroundColor;int depth=GUI.depth;
            GUI.matrix=Matrix4x4.Scale(new Vector3(Screen.width/1920f,Screen.height/1080f,1));GUI.depth=-151;
            GUI.backgroundColor=DaydreamPalette.Mint;small.normal.textColor=DaydreamPalette.Text(DaydreamPalette.Ink);
            try{
                var e=Event.current;
                if(Expanded&&e.type==EventType.MouseDown&&!MenuRect.Contains(e.mousePosition)&&!ToggleRect.Contains(e.mousePosition)){
                    CloseMenu();e.Use();return;
                }
                string label=cameraRig.IsInspecting?"手账编辑":cameraRig.profile?cameraRig.profile.FindDeskView(cameraRig.ActiveDeskView)?.label:"桌面近景";
                if(!cameraRig.IsInspecting&&cameraRig.ActiveDeskView==DeskViewKind.Desk&&cameraRig.HasSeatedLookOffset)
                    label="环视 · "+(Mathf.Abs(cameraRig.SeatedYawDegrees)>120?"身后":cameraRig.SeatedYawDegrees<-8?"左侧":cameraRig.SeatedYawDegrees>8?"右侧":"桌前");
                GUI.enabled=!workspace.InteractionBusy&&!workspace.journal.IsBusy&&!cameraRig.IsTransitioning&&!workspace.GetComponent<WorkspaceInputRouter>().IsDraggingMechanism;
                if(GUI.Button(ToggleRect,"视角："+label+" ▾",button)){Expanded=!Expanded;workspace.desk.ConsumePointer();RuntimeInputGate.Consume();}
                if(!Expanded){
                    string lookCaption=cameraRig.IsInspecting||cameraRig.ActiveDeskView!=DeskViewKind.Desk?"回桌环视":"环视："+(cameraRig.MouseLookEnabled?"开":"关");
                    if(GUI.Button(MouseLookRect,lookCaption,button)){
                        if(!cameraRig.MouseLookEnabled||cameraRig.ActiveDeskView!=DeskViewKind.Desk||workspace.journal.IsEditing){cameraRig.SetMouseLookEnabled(true);RequestView(DeskViewKind.Desk);}
                        else cameraRig.SetMouseLookEnabled(false);
                        workspace.desk.ConsumePointer();RuntimeInputGate.Consume();
                    }
                    if(GUI.Button(ForwardRect,"回到桌面",button))RequestView(DeskViewKind.Desk);
                    GUI.enabled=true;
                    GUI.Label(LookHintRect,"不按键：移到左右边缘持续转头\nESC / 回到桌面：返回正前方",small);
                    return;
                }
                GUI.enabled=true;
                var old=GUI.color;GUI.color=DaydreamPalette.Surface(DaydreamPalette.Cream);GUI.DrawTexture(MenuRect,Texture2D.whiteTexture);GUI.color=old;
                GUI.enabled=!RequestPending&&!workspace.InteractionBusy&&!workspace.journal.HasPaperPreview&&!workspace.scrapbook.IsTransforming&&!cameraRig.IsTransitioning;
                for(int i=0;i<choices.Length;i++){
                    var kind=choices[i];var view=cameraRig.profile?cameraRig.profile.FindDeskView(kind):null;
                    if(GUI.Button(new Rect(MenuRect.x+8,MenuRect.y+8+i*48,MenuRect.width-16,43),view?.label??kind.ToString(),button))RequestView(kind);
                }
                GUI.enabled=true;GUI.Label(new Rect(MenuRect.x+10,MenuRect.yMax-35,MenuRect.width-20,31),"固定坐姿 · 可随时返回近景",small);
            }finally{GUI.enabled=true;GUI.matrix=matrix;GUI.backgroundColor=background;GUI.depth=depth;}
        }
        void OnDestroy(){if(Instance==this)Instance=null;if(font)Destroy(font);}
    }
}

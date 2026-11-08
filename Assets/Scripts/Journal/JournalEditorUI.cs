using UnityEngine;
namespace VintageJournal
{
    public sealed class JournalEditorUI : MonoBehaviour
    {
        public JournalController journal;
        ScrapbookController scrapbook;ScrapbookUI scrapbookUI;
        void Start(){scrapbook=GetComponent<ScrapbookController>();scrapbookUI=GetComponent<ScrapbookUI>();}
        Font font; GUIStyle text,small,title,button; Vector2 scroll,lastPointer; bool stylesReady;
        Color paper=DaydreamPalette.Paper,ink=DaydreamPalette.Ink,gold=DaydreamPalette.SkyDeep,sage=DaydreamPalette.MintDeep;
        void Update()
        {
            if(RuntimeInputGate.BlocksLegacyInput||RuntimeInputGate.ConsumedThisFrame)return;
            if(!journal||!journal.IsEditing)return;
            if(scrapbook&&(scrapbook.InterceptsInput||scrapbook.InputConsumedThisFrame))return;
            if(Input.GetKeyDown(KeyCode.Escape)){if(journal.HasPaperPreview)journal.CancelPaper();else journal.ExitEditing();return;}
            if(journal.IsBusy||journal.HasPaperPreview)return;
            if(Input.GetKeyDown(KeyCode.RightArrow))journal.Turn(1);
            if(Input.GetKeyDown(KeyCode.LeftArrow))journal.Turn(-1);
            Vector2 p=Input.mousePosition;
            if(!OverControls(p))
            {
                float wheel=Input.mouseScrollDelta.y;if(wheel!=0)journal.ZoomBy(wheel*.12f);
                if(Input.GetMouseButton(1)||Input.GetMouseButton(2))
                {
                    if(!Input.GetMouseButtonDown(1)&&!Input.GetMouseButtonDown(2))
                        journal.Pan(new Vector2(-(p.x-lastPointer.x)/Screen.width,(p.y-lastPointer.y)/Screen.height)*.65f);
                }
            }
            lastPointer=p;
        }
        public bool OverControls(Vector2 screen)
        {
            Vector2 p=new Vector2(screen.x*1920/Screen.width,(Screen.height-screen.y)*1080/Screen.height);
            return (AtelierTutorial.Instance&&AtelierTutorial.Instance.Contains(screen))||(CommissionDeskController.Instance&&CommissionDeskController.Instance.ContainsPointer(screen))||(scrapbookUI&&scrapbookUI.OverControls(screen))||p.y<156||p.y>975||p.x>1700||(journal.HasPaperPreview&&new Rect(24,130,430,800).Contains(p));
        }
        void Styles()
        {
            if(stylesReady)return;font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","Microsoft YaHei UI","Arial"},22);
            text=Style(22);small=Style(17);title=Style(28);button=Style(22);button.alignment=TextAnchor.MiddleCenter;stylesReady=true;
        }
        GUIStyle Style(int size){var s=new GUIStyle(GUI.skin.label){font=font,fontSize=size,wordWrap=true};s.normal.textColor=ink;return s;}
        void OnGUI()
        {
            if(RuntimeInputGate.BlocksLegacyInput)return;
            if(CommissionDeskController.Instance&&CommissionDeskController.Instance.PanelOpen)return;
            if(!journal||journal.Document==null||journal.IsExiting)return;Styles();
            var old=GUI.matrix;var color=GUI.color;
            GUI.matrix=Matrix4x4.Scale(new Vector3(Screen.width/1920f,Screen.height/1080f,1));GUI.color=Color.white;
            if(!journal.IsEditing)
            {
                if(!string.IsNullOrEmpty(journal.StatusMessage)&&(journal.StatusMessage.Contains("损坏")||journal.StatusMessage.Contains("恢复")||journal.StatusMessage.Contains("失败")))
                {Panel(new Rect(24,120,800,75));GUI.Label(new Rect(42,131,764,54),journal.StatusMessage,small);}
            }
            else
            {
                Panel(new Rect(245,24,1445,66));
                if(Btn(new Rect(24,24,200,66),"← 返回桌面"))journal.ExitEditing();
                GUI.Label(new Rect(270,38,440,42),journal.Document.ActiveNotebook?.displayName ?? "纸间时光  /  活页本",title);
                int first=journal.Document.ActiveSpread*2+1;
                GUI.Label(new Rect(760,42,600,40),"第 "+first+"–"+(first+1)+" 页   /   共 "+journal.Document.ActivePages.Length+" 页",text);
                GUI.Label(new Rect(1390,43,275,34),journal.IsBusy?"纸页轻轻翻动中…":journal.HasPaperPreview?"实时预览 · 尚未确认":scrapbook&&scrapbook.SavePending?"正在保存…":"已为你保留这一页",small);
                if(!string.IsNullOrEmpty(journal.StatusMessage)&&(journal.StatusMessage.Contains("损坏")||journal.StatusMessage.Contains("恢复")||journal.StatusMessage.Contains("失败")))
                {Panel(new Rect(500,112,1150,48));GUI.Label(new Rect(516,121,1110,32),journal.StatusMessage,small);}
                bool unlocked=!RuntimeInputGate.BlocksJournalOperations&&!journal.IsBusy&&!journal.HasPaperPreview&&(!scrapbook||!scrapbook.BlocksPageOperations);
                GUI.enabled=unlocked;
                if(Btn(new Rect(1760,160,132,52),"＋ 放大"))journal.ZoomBy(.2f);
                if(Btn(new Rect(1760,225,132,52),"－ 缩小"))journal.ZoomBy(-.2f);
                if(Btn(new Rect(1720,290,172,52),"恢复视角"))journal.ResetView();
                GUI.enabled=unlocked&&journal.Document.ActiveSpread>0;
                if(Btn(new Rect(480,984,160,62),"← 上一页"))journal.Turn(-1);
                GUI.enabled=unlocked;
                GUI.enabled=unlocked&&journal.Document.ActiveNotebook==null;
                if(Btn(new Rect(800,984,320,62),journal.Document.ActiveNotebook==null?"更换内页":"TN · 固定装订米色内页"))journal.BeginPaperSelection(JournalSide.Left);
                GUI.enabled=unlocked&&journal.Document.ActiveSpread<journal.SpreadCount-1;
                if(Btn(new Rect(1400,984,160,62),"下一页 →"))journal.Turn(1);
                GUI.enabled=true;
                GUI.Label(new Rect(28,955,410,70),journal.Mode==JournalMode.ZoomMode?"按住右键 / 中键拖动，查看纸张局部":"方向键翻页 · 滚轮缩放 · ESC 返回",small);
                if(journal.HasPaperPreview)Picker();
            }
            GUI.matrix=old;GUI.color=color;GUI.enabled=true;
        }
        void Picker()
        {
            Panel(new Rect(24,130,430,800));GUI.Label(new Rect(44,150,390,44),"挑选一张内页",title);
            GUI.Label(new Rect(44,194,390,26),"先选纸张，再确认；取消会恢复原样。",small);
            if(Btn(new Rect(44,225,180,45),"应用到左页",journal.PreviewSide==JournalSide.Left))journal.SetPreviewSide(JournalSide.Left);
            if(Btn(new Rect(244,225,180,45),"应用到右页",journal.PreviewSide==JournalSide.Right))journal.SetPreviewSide(JournalSide.Right);
            var entries=journal.Catalog.papers;
            scroll=GUI.BeginScrollView(new Rect(44,300,390,530),scroll,new Rect(0,0,365,Mathf.CeilToInt(entries.Length/3f)*157));
            for(int i=0;i<entries.Length;i++)
            {
                var e=entries[i];var r=new Rect(i%3*126,i/3*157,116,147);
                Fill(r,e.id==journal.SelectedPaperId?DaydreamPalette.Mint:DaydreamPalette.Card);
                if(e.texture)GUI.DrawTexture(new Rect(r.x+8,r.y+6,100,113),e.texture,ScaleMode.ScaleToFit,true);
                GUI.Label(new Rect(r.x+6,r.y+119,108,25),e.displayName,small);
                if(GUI.Button(r,GUIContent.none,GUIStyle.none))journal.SelectPaper(e.id);
            }
            GUI.EndScrollView();
            if(Btn(new Rect(44,855,180,52),"确认更换",true))journal.ConfirmPaper();
            if(Btn(new Rect(244,855,180,52),"取消"))journal.CancelPaper();
        }
        void Fill(Rect r,Color c){var old=GUI.color;GUI.color=c;GUI.DrawTexture(r,Texture2D.whiteTexture);GUI.color=old;}
        void Panel(Rect r)
        {
            Fill(new Rect(r.x+4,r.y+5,r.width,r.height),DaydreamPalette.Shadow);Fill(r,paper);
            Fill(new Rect(r.x,r.y,r.width,1),gold);Fill(new Rect(r.x,r.yMax-1,r.width,1),gold);Fill(new Rect(r.x,r.y,1,r.height),gold);Fill(new Rect(r.xMax-1,r.y,1,r.height),gold);
            for(float y=r.y+12;y<r.yMax-10;y+=13)Fill(new Rect(r.x+9,y,r.width-18,1),new Color(.36f,.58f,.74f,.02f));
        }
        bool Btn(Rect r,string label,bool active=false)
        {
            Fill(r,active?sage:paper);Fill(new Rect(r.x,r.yMax-2,r.width,2),gold);
            button.normal.textColor=active?Color.white:ink;button.hover.textColor=button.normal.textColor;button.active.textColor=button.normal.textColor;
            return GUI.Button(r,label,button);
        }
    }
}

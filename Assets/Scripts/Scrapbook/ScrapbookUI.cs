using UnityEngine;
namespace VintageJournal
{
    public sealed class ScrapbookUI:MonoBehaviour
    {
        public ScrapbookController book;ScrapbookInput input;Font font;GUIStyle label,title,button,small;Vector2 scroll;
        Color paper=DaydreamPalette.Paper,brown=DaydreamPalette.Ink,sage=DaydreamPalette.MintDeep,brass=DaydreamPalette.SkyDeep;
        void Start(){input=GetComponent<ScrapbookInput>();}
        public bool OverControls(Vector2 screen)
        {
            Vector2 p=new Vector2(screen.x*1920/Screen.width,(Screen.height-screen.y)*1080/Screen.height);
            return p.y<156||p.y>975||p.x>1700||new Rect(460,100,880,56).Contains(p)||book.BrowserOpen&&new Rect(24,162,430,790).Contains(p)||book.SelectedData!=null&&new Rect(24,180,300,650).Contains(p);
        }
        void Styles(){if(font)return;font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","Arial"},21);label=Style(21);title=Style(26);small=Style(17);button=Style(20);button.alignment=TextAnchor.MiddleCenter;}
        GUIStyle Style(int n){var s=new GUIStyle(GUI.skin.label){font=font,fontSize=n,wordWrap=true};s.normal.textColor=brown;return s;}
        void OnGUI()
        {
            if(RuntimeInputGate.BlocksLegacyInput)return;
            if(!book||!book.journal||!book.journal.IsEditing||book.journal.IsExiting||book.Catalog==null)return;Styles();
            var matrix=GUI.matrix;var color=GUI.color;GUI.matrix=Matrix4x4.Scale(new Vector3(Screen.width/1920f,Screen.height/1080f,1));GUI.color=Color.white;
            bool available=!book.journal.IsBusy&&!book.journal.HasPaperPreview&&!book.IsTransforming;
            GUI.enabled=available;
            if(Btn(new Rect(470,104,165,48),"和纸胶带"))book.Browse(DecorationCategory.Tape);
            if(Btn(new Rect(650,104,165,48),"复古贴纸"))book.Browse(DecorationCategory.Sticker);
            if(Btn(new Rect(830,104,165,48),"便签装饰"))book.Browse(DecorationCategory.Note);
            GUI.enabled=available&&book.Chosen==null;
            string side=book.ActiveSide==JournalSide.Left?"左":"右";
            if(Btn(new Rect(1030,104,130,48),"撤销 "+side+" ↶"))book.Undo();if(Btn(new Rect(1170,104,130,48),"重做 "+side+" ↷"))book.Redo();
            GUI.enabled=true;
            if(book.BrowserOpen)Browser();
            else if(book.SelectedData!=null){SelectionPanel();DrawSelection();}
            else if(book.Chosen!=null)
            {
                Panel(new Rect(24,180,300,180));GUI.Label(new Rect(42,195,260,145),book.Chosen.supportsLengthAdjustment?"胶带待放置\n按住左键横向拉出长度，松开贴上。\n右键 / ESC 取消":"素材待放置\n移动到左右书页，单击贴上。\n右键 / ESC 取消",label);
            }
            GUI.matrix=matrix;GUI.color=color;GUI.enabled=true;
        }
        void Browser()
        {
            Panel(new Rect(24,162,430,790));GUI.Label(new Rect(44,180,340,42),"纸间素材收藏",title);
            if(Btn(new Rect(374,176,60,42),"×"))book.CloseBrowser();
            if(Btn(new Rect(44,232,120,44),"胶带",book.Category==DecorationCategory.Tape))book.Browse(DecorationCategory.Tape);
            if(Btn(new Rect(174,232,120,44),"贴纸",book.Category==DecorationCategory.Sticker))book.Browse(DecorationCategory.Sticker);
            if(Btn(new Rect(304,232,120,44),"便签",book.Category==DecorationCategory.Note))book.Browse(DecorationCategory.Note);
            int count=0;foreach(var def in book.Catalog.assets)if(def.category==book.Category)count++;
            scroll=GUI.BeginScrollView(new Rect(44,300,390,580),scroll,new Rect(0,0,365,Mathf.CeilToInt(count/3f)*157));int i=0;
            foreach(var def in book.Catalog.assets)
            {
                if(def.category!=book.Category)continue;var r=new Rect(i%3*126,i/3*157,116,147);i++;
                Fill(r,DaydreamPalette.Card);if(def.thumbnail)GUI.DrawTexture(new Rect(r.x+8,r.y+5,100,100),def.thumbnail,ScaleMode.ScaleToFit,true);
                GUI.Label(new Rect(r.x+5,r.y+106,110,40),def.displayName+"\n"+(def.IsAvailable?"可用":"暂缺"),small);
                // Mouse-down begins a real drag; a click without movement leaves click-to-place armed.
                if(def.IsAvailable&&Event.current.type==EventType.MouseDown&&Event.current.button==0&&r.Contains(Event.current.mousePosition))
                {input.StartAssetDrag(def.assetId);Event.current.Use();break;}
            }
            GUI.EndScrollView();GUI.Label(new Rect(44,896,390,46),"点选后到页面单击，或直接拖入纸页。\n可用 "+count+" 款 · ESC 关闭",small);
        }
        void SelectionPanel()
        {
            var d=book.SelectedData;var def=book.Catalog.Find(d.assetId);Panel(new Rect(24,180,300,650));
            GUI.Label(new Rect(42,198,264,70),def.displayName+"\n"+(book.ActiveSide==JournalSide.Left?"左页":"右页")+" · 已选中",title);
            GUI.enabled=!book.IsTransforming;
            if(Btn(new Rect(42,294,118,48),"↶ 旋转"))Edit("rotate",()=>book.RotateTo(d.rotationDegrees-15));
            if(Btn(new Rect(174,294,118,48),"旋转 ↷"))Edit("rotate",()=>book.RotateTo(d.rotationDegrees+15));
            if(Btn(new Rect(42,354,118,48),"－ 缩小"))Edit("scale",()=>book.ScaleTo(d.scale.x-.1f));
            if(Btn(new Rect(174,354,118,48),"＋ 放大"))Edit("scale",()=>book.ScaleTo(d.scale.x+.1f));
            if(def.supportsLengthAdjustment)
            {
                if(Btn(new Rect(42,414,118,48),"胶带短些"))Edit("length",()=>book.SetLength(d.length-.2f));
                if(Btn(new Rect(174,414,118,48),"胶带长些"))Edit("length",()=>book.SetLength(d.length+.2f));
            }
            if(Btn(new Rect(42,494,118,48),"置于底层"))book.Layer("bottom");if(Btn(new Rect(174,494,118,48),"置于顶层"))book.Layer("top");
            if(Btn(new Rect(42,554,118,48),"下移一层"))book.Layer("down");if(Btn(new Rect(174,554,118,48),"上移一层"))book.Layer("up");
            if(Btn(new Rect(42,634,250,50),"删除当前素材"))book.DeleteSelected();
            if(Btn(new Rect(42,696,250,46),"取消选择"))book.Deselect();
            GUI.enabled=true;GUI.Label(new Rect(42,752,264,65),"Q / E 旋转 · Shift吸附15°\n[ / ] 缩放 · Delete删除",small);
        }
        void Edit(string kind,System.Action apply){if(book.BeginTransform(kind)){apply();book.CommitTransform();input.LogHandles();}}
        void DrawSelection()
        {
            var d=book.SelectedData;var def=book.Catalog.Find(d.assetId);var page=book.journal.pageView.SurfaceSize(book.ActiveSide);
            Vector2[] corners={new Vector2(-.5f,-.5f),new Vector2(-.5f,.5f),new Vector2(.5f,.5f),new Vector2(.5f,-.5f)};
            for(int i=0;i<4;i++){var a=input.ControlCorner(corners[i]*2);var b=input.ControlCorner(corners[(i+1)%4]*2);Line(Canvas(a),Canvas(b));}
            foreach(string kind in new[]{"rotate","scale","delete","length"})
            {if(kind=="length"&&!def.supportsLengthAdjustment)continue;var p=Canvas(input.HandlePoint(kind));Fill(new Rect(p.x-12,p.y-12,24,24),paper);GUI.Label(new Rect(p.x-12,p.y-14,24,28),kind=="rotate"?"↻":kind=="scale"?"↗":kind=="delete"?"×":"↔",button);}
        }
        Vector2 Canvas(Vector2 p)=>new Vector2(p.x*1920/Screen.width,(Screen.height-p.y)*1080/Screen.height);
        void Line(Vector2 a,Vector2 b){var m=GUI.matrix;GUIUtility.RotateAroundPivot(Mathf.Atan2(b.y-a.y,b.x-a.x)*Mathf.Rad2Deg,a);Fill(new Rect(a.x,a.y,Vector2.Distance(a,b),2),brass);GUI.matrix=m;}
        void Fill(Rect r,Color c){var old=GUI.color;GUI.color=c;GUI.DrawTexture(r,Texture2D.whiteTexture);GUI.color=old;}
        void Panel(Rect r){Fill(new Rect(r.x+4,r.y+4,r.width,r.height),DaydreamPalette.Shadow);Fill(r,paper);Fill(new Rect(r.x,r.y,r.width,1),brass);Fill(new Rect(r.x,r.yMax-1,r.width,1),brass);}
        bool Btn(Rect r,string text,bool active=false){Fill(r,active?sage:paper);Fill(new Rect(r.x,r.yMax-2,r.width,2),brass);button.normal.textColor=active?Color.white:brown;return GUI.Button(r,text,button);}
    }
}

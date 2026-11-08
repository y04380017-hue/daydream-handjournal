using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace VintageJournal
{
    /// <summary>Explicit personal-creation modal. The shared input router owns keyboard/pointer dispatch beneath it.</summary>
    public sealed class AtelierHub : MonoBehaviour
    {
        public static AtelierHub Instance { get; private set; }
        public static bool PrivateMode => Instance && Instance.workspace && Instance.workspace.Ready && Instance.workspace.Service.Document.personalStudio?.privateJournalMode == true;
        public WorkspaceGameplayController workspace;
        public bool IsOpen { get; private set; }
        public bool Ready { get; private set; }
        public int Tab { get; private set; }
        public string Status { get; private set; } = "这里的记录默认只属于你；分享与联网都需要你主动选择。";
        readonly string[] tabs={"我的手账","日记与分区","创作工坊","珍藏与回顾","拍照与分享","日常与工房","云上朋友"};
        static readonly Rect EntryRect=new Rect(24,850,280,54);
        GUIStyle label,title,small,button,field;Font font;
        Vector2 scroll;string coverTitle="",sectionTitle="梦里的颜色",pageTitle="",diaryBody="",diaryDate="",artName="我的小贴纸",typedText="今天像一阵轻轻的风。";
        int colorIndex,charm,coverMaterial,brush,inkIndex,textStyle,stampShape,photoFilter,targetIndex,artPage,keepMode,paperIndex,paperTint;
        long lastPageIndex;
        Texture2D canvas,photo;Color[] drawing=new Color[256*256];Vector2 previousDraw;bool painting;
        readonly Stack<Color[]> drawingUndo=new Stack<Color[]>();
        readonly Dictionary<string,Texture2D> photoCache=new Dictionary<string,Texture2D>();
        PersonalStudioData Data=>workspace.Service.Document.personalStudio;
        public void SelectTab(int index){Tab=Mathf.Clamp(index,0,tabs.Length-1);scroll=Vector2.zero;}
        void Awake(){Instance=this;}
        IEnumerator Start()
        {
            if(!workspace)workspace=GetComponent<WorkspaceGameplayController>();
            while(!workspace||!workspace.Ready)yield return null;
            if(!workspace.Service.InitializePersonalStudio()){Status=workspace.Service.LastMessage;yield break;}
            PersonalStudioAssets.Bind(workspace);
            workspace.journal.RefreshPages();workspace.RefreshViews();
            var presentation=GetComponent<StudioPresentation>();if(!presentation)presentation=gameObject.AddComponent<StudioPresentation>();presentation.workspace=workspace;
            canvas=new Texture2D(256,256,TextureFormat.RGBA32,false){filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};UpdateCanvas();
            LoadDrafts();Ready=true;Debug.Log("[PersonalStudio] Ready; templates="+Data.artworks.Count);
        }
        void LoadDrafts()
        {
            coverTitle=Data.title;colorIndex=Data.coverColor;charm=Data.charm;coverMaterial=Array.IndexOf(new[]{"棉布","和纸","软木"},Data.coverMaterial);
            diaryDate=DateTime.Now.ToString("yyyy-MM-dd");diaryBody=Data.diary.Find(d=>d.dateKey==diaryDate)?.body??string.Empty;
            paperIndex=Mathf.Max(0,Array.FindIndex(workspace.journal.Catalog.papers,p=>p.id==workspace.journal.Document.GetPage(JournalSide.Left).paperId));
            paperTint=Data.pageNotes.Find(p=>p.pageId==workspace.journal.Document.GetPage(JournalSide.Left).pageId)?.paperColor??0;
        }
        void Update()
        {
            if(!Ready||IsOpen||workspace.InteractionBusy||workspace.journal.HasPaperPreview||workspace.scrapbook.IsTransforming||DateTime.UtcNow.Ticks<lastPageIndex)return;
            lastPageIndex=DateTime.UtcNow.AddSeconds(5).Ticks;
            var page=workspace.Service.Document.AllPages.FirstOrDefault(p=>p.decorations.Count>0&&!Data.pageNotes.Any(n=>n.pageId==p.pageId));
            if(page!=null)workspace.Service.MarkStudioPage(page.pageId,string.Empty,false);
        }
        bool Visible=>Ready&&workspace.desk.HasBegun&&!workspace.desk.IsBlocking&&!AtelierTutorial.Blocking;
        bool EntryVisible=>Visible&&!workspace.InteractionBusy&&!workspace.journal.HasPaperPreview&&!workspace.scrapbook.BrowserOpen&&workspace.scrapbook.SelectedData==null;
        public bool Contains(Vector2 pixel)
        {return Visible&&(IsOpen||(EntryVisible&&EntryRect.Contains(new Vector2(pixel.x*1920/Screen.width,(Screen.height-pixel.y)*1080/Screen.height))));}
        public void Open()
        {
            if(!Visible||workspace.InteractionBusy||workspace.journal.IsBusy||workspace.journal.HasPaperPreview||workspace.scrapbook.IsTransforming||workspace.GetComponent<WorkspaceInputRouter>().IsDraggingMechanism)return;
            if(CommissionDeskController.Instance&&CommissionDeskController.Instance.PanelOpen)CommissionDeskController.Instance.ClosePanel();
            if(DreamGameplayController.Instance&&DreamGameplayController.Instance.BagOpen)DreamGameplayController.Instance.CloseBag();
            workspace.scrapbook.Cancel();workspace.ClearSelection();IsOpen=true;LoadDrafts();workspace.desk.ConsumePointer();RuntimeInputGate.Consume();Debug.Log("[PersonalStudio] Open");
        }
        public void Close(){IsOpen=false;painting=false;workspace?.desk.ConsumePointer();RuntimeInputGate.Consume();Debug.Log("[PersonalStudio] Close");}
        public bool EnterPersonalJournal()
        {
            if(!Ready||workspace.InteractionBusy||workspace.journal.HasPaperPreview)return false;
            if(!workspace.Service.SetPersonalMode(true))return false;
            var work=workspace.journal.GetComponent<NotebookWorkbench>();
            if(work&&work.Ready&&!work.UseBinder()){Status=work.Status;return false;}
            Close();StartCoroutine(EnterAfterModal());return true;
        }
        IEnumerator EnterAfterModal(){yield return null;if(!workspace.journal.IsEditing)workspace.journal.EnterEditing();}
        public bool ReturnToCommissions(){bool ok=workspace.Service.SetPersonalMode(false);if(ok)Close();return ok;}
        void Styles()
        {
            if(font)return;font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","Arial"},22);
            label=new GUIStyle(GUI.skin.label){font=font,fontSize=23,wordWrap=true};title=new GUIStyle(label){fontSize=31};small=new GUIStyle(label){fontSize=19};
            button=new GUIStyle(GUI.skin.button){font=font,fontSize=22,wordWrap=true};field=new GUIStyle(GUI.skin.textArea){font=font,fontSize=22,wordWrap=true};
            button.normal.background=button.hover.background=button.active.background=Texture2D.whiteTexture;
        }
        void OnGUI()
        {
            if(!Visible)return;Styles();var matrix=GUI.matrix;int depth=GUI.depth;var bg=GUI.backgroundColor;
            GUI.matrix=Matrix4x4.Scale(new Vector3(Screen.width/1920f,Screen.height/1080f,1));GUI.depth=-165;
            Color ink=DaydreamPalette.Text(DaydreamPalette.Ink);label.normal.textColor=title.normal.textColor=small.normal.textColor=button.normal.textColor=button.hover.textColor=button.active.textColor=ink;
            GUI.backgroundColor=DaydreamPalette.Mint;
            try
            {
                if(!IsOpen){if(EntryVisible&&GUI.Button(EntryRect,PrivateMode?"我的手账 · 创作工房":"我的手账 / 手作房",button))Open();return;}
                Fill(new Rect(0,0,1920,1080),new Color(.22f,.28f,.45f,.45f));Fill(new Rect(160,120,1600,840),DaydreamPalette.Surface(DaydreamPalette.Cream));
                GUI.Label(new Rect(200,146,1060,52),"白日梦手作房  /  留给自己的创作",title);
                if(Btn(new Rect(1570,144,150,52),"关闭 ×"))Close();
                for(int i=0;i<tabs.Length;i++)if(Btn(new Rect(196+i*216,216,204,50),tabs[i],Tab==i)){SelectTab(i);}
                Rect body=new Rect(204,288,1512,580);
                switch(Tab)
                {
                    case 0:DrawJournal(body);break;case 1:DrawDiary(body);break;case 2:DrawWorkshop(body);break;
                    case 3:DrawKeepsakes(body);break;case 4:DrawPhoto(body);break;
                    case 5:AtelierProgressPanel.Draw(body,workspace);break;
                    case 6:AtelierSocialPanel.Draw(body,workspace);break;
                }
                GUI.Label(new Rect(204,898,1508,46),Status,small);
            }
            finally{GUI.enabled=true;GUI.matrix=matrix;GUI.depth=depth;GUI.backgroundColor=bg;}
        }
        void DrawJournal(Rect r)
        {
            GUI.Label(new Rect(r.x,r.y,780,45),"My Journal · 私人活页本",title);
            GUI.Label(new Rect(r.x,r.y+58,760,85),"这24页一直属于你，与TN、离型本各自独立。私人模式隐藏委托和分数，所有旧作品都会继续留在原处。",label);
            if(Btn(new Rect(r.x,r.y+152,330,58),"进入我的手账"))EnterPersonalJournal();
            if(Btn(new Rect(r.x+350,r.y+152,330,58),"返回委托创作"))ReturnToCommissions();
            GUI.Label(new Rect(r.x,r.y+238,160,40),"封面标题",label);coverTitle=GUI.TextField(new Rect(r.x+164,r.y+230,520,52),coverTitle,40,field);
            string[] materials={"棉布","和纸","软木"};for(int i=0;i<3;i++)if(Btn(new Rect(r.x+i*224,r.y+306,210,48),materials[i],coverMaterial==i))coverMaterial=i;
            for(int i=0;i<6;i++){Fill(new Rect(r.x+i*110,r.y+378,96,54),StudioGraphics.Colors[i]);if(GUI.Button(new Rect(r.x+i*110,r.y+378,96,54),colorIndex==i?"✓":"",GUIStyle.none))colorIndex=i;}
            if(Btn(new Rect(r.x,r.y+462,280,50),"挂饰："+new[]{"无","薄荷珠","星光珠","梦色珠"}[charm]))charm=(charm+1)%4;
            if(Btn(new Rect(r.x+310,r.y+462,370,50),"保存封面与挂饰"))Report(workspace.Service.CustomizeCover(coverTitle,materials[Mathf.Clamp(coverMaterial,0,2)],colorIndex,charm));
            Fill(new Rect(r.x+875,r.y+42,450,430),StudioGraphics.Colors[colorIndex]);
            GUI.Label(new Rect(r.x+910,r.y+138,380,160),coverTitle,title);GUI.Label(new Rect(r.x+910,r.y+332,380,80),materials[Mathf.Clamp(coverMaterial,0,2)]+" · "+new[]{"无挂饰","薄荷珠","星光珠","梦色珠"}[charm],label);
            GUI.Label(new Rect(r.x+850,r.y+488,590,34),"当前活页左页：点击切换纸纹 / 底色",small);
            var papers=workspace.journal.Catalog.papers;GUI.enabled=workspace.journal.Document.ActiveNotebook==null&&papers.Length>0;
            if(papers.Length>0&&Btn(new Rect(r.x+850,r.y+530,338,50),"纸纹 · "+papers[Mathf.Clamp(paperIndex,0,papers.Length-1)].displayName))
            {paperIndex=(paperIndex+1)%papers.Length;Report(workspace.Service.SetStudioPaper(workspace.journal.Document.GetPage(JournalSide.Left).pageId,papers[paperIndex].id,paperTint));}
            if(papers.Length>0&&Btn(new Rect(r.x+1200,r.y+530,240,50),"底色 · "+new[]{"原纸","天蓝","蜜桃","淡紫","浅金","奶油"}[paperTint]))
            {paperTint=(paperTint+1)%6;Report(workspace.Service.SetStudioPaper(workspace.journal.Document.GetPage(JournalSide.Left).pageId,papers[Mathf.Clamp(paperIndex,0,papers.Length-1)].id,paperTint));}
            GUI.enabled=true;
        }
        void DrawDiary(Rect r)
        {
            string prompt=AtelierProgressCatalog.TodayPrompt(DateTime.Now);
            GUI.Label(new Rect(r.x,r.y,900,42),"可选每日提示："+prompt,label);
            GUI.Label(new Rect(r.x,r.y+50,180,40),diaryDate,small);
            diaryBody=GUI.TextArea(new Rect(r.x,r.y+94,810,250),diaryBody,2000,field);
            if(Btn(new Rect(r.x,r.y+364,240,52),"保存私人日记"))Report(workspace.Service.SaveDiary(diaryDate,prompt,diaryBody));
            GUI.Label(new Rect(r.x,r.y+436,170,42),"页面分区",label);sectionTitle=GUI.TextField(new Rect(r.x+170,r.y+428,390,52),sectionTitle,40,field);
            if(Btn(new Rect(r.x+580,r.y+428,230,52),"从当前左页开始"))Report(workspace.Service.AddStudioSection(sectionTitle,workspace.journal.Document.GetPage(JournalSide.Left).pageId));
            GUI.Label(new Rect(r.x+870,r.y,610,42),"日记和分区目录",title);
            scroll=GUI.BeginScrollView(new Rect(r.x+870,r.y+64,610,482),scroll,new Rect(0,0,574,(Data.diary.Count+Data.sections.Count)*62+20));
            int row=0;foreach(var entry in Data.diary.OrderByDescending(d=>d.dateKey).Take(40))
                if(Btn(new Rect(0,row++*62,560,52),entry.dateKey+"  "+(entry.body.Length>14?entry.body.Substring(0,14)+"…":entry.body))){diaryDate=entry.dateKey;diaryBody=entry.body;}
            foreach(var section in Data.sections)if(Btn(new Rect(0,row++*62,560,52),"分区 · "+section.title)){Navigate(section.pageId);}
            GUI.EndScrollView();GUI.Label(new Rect(r.x,r.y+516,810,60),"文字日记默认只保存在本机，不会自动发到画廊或好友。",small);
        }
        void DrawWorkshop(Rect r)
        {
            bool unlocked=AtelierProgressCatalog.ToolUnlocked(workspace.Service.Document,"sticker-maker");
            GUI.Label(new Rect(r.x,r.y,560,42),"256 × 256 贴纸工坊",title);
            GUI.Label(new Rect(r.x+600,r.y,850,52),unlocked?"画笔、方笔、点彩；白色刀模边缘随保存生成。":"贴纸工坊需完成2封回信，或在日常与工房兑换工具。",small);
            Rect drawingRect=new Rect(r.x,r.y+64,384,384);Fill(drawingRect,Color.white);GUI.DrawTexture(drawingRect,canvas,ScaleMode.StretchToFill,true);
            if(unlocked)DrawCanvasInput(drawingRect);
            string[] brushes={"圆画笔","方笔","点彩"};for(int i=0;i<3;i++)if(Btn(new Rect(r.x+418+i*174,r.y+64,160,48),brushes[i],brush==i))brush=i;
            Color[] inks=InkPalette();for(int i=0;i<inks.Length;i++){Fill(new Rect(r.x+420+i*66,r.y+130,56,48),inks[i]);if(GUI.Button(new Rect(r.x+420+i*66,r.y+130,56,48),inkIndex==i?"✓":"",GUIStyle.none))inkIndex=i;}
            GUI.Label(new Rect(r.x+418,r.y+194,900,46),"调色板来自已收集的墨水，起始有铅笔灰。",small);
            artName=GUI.TextField(new Rect(r.x+418,r.y+252,520,50),artName,40,field);
            if(Btn(new Rect(r.x+418,r.y+320,240,50),"撤销一笔")&&drawingUndo.Count>0){drawing=drawingUndo.Pop();UpdateCanvas();}
            if(Btn(new Rect(r.x+678,r.y+320,260,50),"清空画布")){drawingUndo.Push((Color[])drawing.Clone());drawing=new Color[65536];UpdateCanvas();}
            GUI.enabled=unlocked;
            if(Btn(new Rect(r.x+418,r.y+388,520,54),"刀模保存到我的素材")){var cut=StudioGraphics.DieCut(canvas);try{SaveArt(cut,"sticker",artName);}finally{Destroy(cut);}}
            GUI.enabled=true;
            typedText=GUI.TextArea(new Rect(r.x,r.y+474,670,92),typedText,120,field);
            if(Btn(new Rect(r.x+700,r.y+474,220,48),"字体："+new[]{"整洁","手写","书信"}[textStyle]))textStyle=(textStyle+1)%3;
            if(Btn(new Rect(r.x+700,r.y+532,220,48),"生成文字贴纸")){var text=StudioGraphics.Text(typedText,textStyle,inks[Mathf.Clamp(inkIndex,0,inks.Length-1)]);try{SaveArt(text,"text","我的文字",typedText);}finally{Destroy(text);}}
            GUI.Label(new Rect(r.x+1000,r.y+64,450,44),"收集墨水印章",title);
            if(Btn(new Rect(r.x+1000,r.y+126,400,50),"形状："+new[]{"圆印","星花","爱心","环印"}[stampShape]))stampShape=(stampShape+1)%4;
            GUI.enabled=AtelierProgressCatalog.ToolUnlocked(workspace.Service.Document,"stamp");
            if(Btn(new Rect(r.x+1000,r.y+192,400,50),"保存墨水印章")){var stamp=StudioGraphics.Stamp(stampShape,inks[Mathf.Clamp(inkIndex,0,inks.Length-1)]);try{SaveArt(stamp,"stamp","收集墨水印章");}finally{Destroy(stamp);}}
            GUI.enabled=true;GUI.Label(new Rect(r.x+1000,r.y+270,400,170),"印章在首封回信后开放。保存好的文字、印章与自制贴纸可在珍藏页反复印制，每次都成为独立的可移动实例。",label);
        }
        void DrawCanvasInput(Rect rect)
        {
            var e=Event.current;if(e.type==EventType.MouseUp){painting=false;return;}
            if((e.type!=EventType.MouseDown&&e.type!=EventType.MouseDrag)||e.button!=0||!rect.Contains(e.mousePosition))return;
            Vector2 p=new Vector2((e.mousePosition.x-rect.x)/rect.width*256,(rect.yMax-e.mousePosition.y)/rect.height*256);
            if(e.type==EventType.MouseDown){if(drawingUndo.Count>=12)drawingUndo.Clear();drawingUndo.Push((Color[])drawing.Clone());previousDraw=p;painting=true;}
            if(!painting)return;var palette=InkPalette();Color color=palette[Mathf.Clamp(inkIndex,0,palette.Length-1)];
            int samples=Mathf.Max(1,Mathf.CeilToInt(Vector2.Distance(previousDraw,p)/2));
            for(int step=0;step<=samples;step++){var centre=Vector2.Lerp(previousDraw,p,(float)step/samples);for(int dy=-7;dy<=7;dy++)for(int dx=-7;dx<=7;dx++)
            {if(brush==0&&dx*dx+dy*dy>40||brush==2&&((dx*7+dy*11+step)%13!=0))continue;int x=Mathf.RoundToInt(centre.x)+dx,y=Mathf.RoundToInt(centre.y)+dy;if(x<0||y<0||x>=256||y>=256)continue;drawing[y*256+x]=color;}}
            previousDraw=p;UpdateCanvas();e.Use();
        }
        Color[] InkPalette()
        {
            var ids=workspace.Service.Items.Select(i=>i.assetId).Distinct().Where(id=>id.Contains("ink")||id.Contains("glow")).Take(5).ToArray();
            return new[]{DaydreamPalette.Ink}.Concat(ids.Select(id=>StudioGraphics.Colors[Math.Abs(id.GetHashCode()%6)])).ToArray();
        }
        void UpdateCanvas(){if(canvas){canvas.SetPixels(drawing);canvas.Apply();}}
        void SaveArt(Texture2D texture,string kind,string name,string text="")
        {
            if(kind=="sticker"&&!texture.GetPixels32().Any(p=>p.a>128)){Status="先画一点属于你的图案。";return;}
            string id=workspace.Service.SaveStudioArt(name,kind,Convert.ToBase64String(texture.EncodeToPNG()),texture.width,texture.height,text,textStyle);
            Status=id!=null?"已保存到珍藏页；选择印制后，可贴进任意打开的页面。":workspace.Service.LastMessage;
        }
        void DrawKeepsakes(Rect r)
        {
            GUI.Label(new Rect(r.x,r.y,920,44),"我的素材模板 / 作品收藏",title);
            if(Btn(new Rect(r.x+680,r.y,300,45),keepMode==0?"切到逐日页面":"切到自制素材")){keepMode=1-keepMode;scroll=Vector2.zero;}
            if(keepMode==1){DrawDatedPages(r);return;}
            int total=Mathf.Max(1,Mathf.CeilToInt(Data.artworks.Count/6f));artPage=Mathf.Clamp(artPage,0,total-1);
            if(Btn(new Rect(r.x+1010,r.y,170,45),"上一组"))artPage=Mathf.Max(0,artPage-1);
            if(Btn(new Rect(r.x+1200,r.y,170,45),"下一组"))artPage=Mathf.Min(total-1,artPage+1);
            var arts=Data.artworks.Skip(artPage*6).Take(6).ToArray();
            for(int i=0;i<arts.Length;i++)
            {
                float x=r.x+i*240;var art=arts[i];var definition=PersonalStudioAssets.Find(art.assetId);Fill(new Rect(x,r.y+72,224,260),DaydreamPalette.Surface(DaydreamPalette.Card));
                if(definition?.thumbnail)GUI.DrawTexture(new Rect(x+16,r.y+86,192,150),definition.thumbnail,ScaleMode.ScaleToFit,true);
                GUI.Label(new Rect(x+12,r.y+242,200,40),art.name,small);
                if(Btn(new Rect(x+12,r.y+286,200,42),"印制并拿取")){if(workspace.Service.BeginStudioArt(art.assetId)){Close();workspace.RefreshViews();}else Status=workspace.Service.LastMessage;}
            }
            if(arts.Length==0)GUI.Label(new Rect(r.x,r.y+100,1300,110),"这里还没有自制素材。文字工具从开始就能使用；印章、贴纸工坊与照片工具随着回信逐渐开放。",label);
            var current=workspace.journal.Document.GetPage(JournalSide.Left);pageTitle=GUI.TextField(new Rect(r.x,r.y+372,470,50),pageTitle,80,field);
            if(Btn(new Rect(r.x+490,r.y+372,260,50),"收藏当前左页"))Report(workspace.Service.MarkStudioPage(current.pageId,pageTitle,true));
            if(Btn(new Rect(r.x+780,r.y+372,360,50),"导出我的年度回顾")){try{Status="已导出："+StudioExports.ExportYear(workspace);}catch(Exception e){Status=e.Message;}}
            string favorites=string.Join("  ·  ",Data.pageNotes.Where(n=>n.favorite).OrderByDescending(n=>n.createdDate).Take(6).Select(n=>(string.IsNullOrEmpty(n.title)?n.pageId:n.title)+" "+n.createdDate));
            GUI.Label(new Rect(r.x,r.y+450,1400,90),"按日期珍藏："+favorites,small);
            if(Data.pageNotes.Count>0&&Btn(new Rect(r.x,r.y+534,400,44),"翻阅最新创作页面"))Navigate(Data.pageNotes.OrderByDescending(n=>n.createdDate).First().pageId);
        }
        void DrawDatedPages(Rect r)
        {
            var pages=workspace.Service.Document.AllPages.Where(p=>p.decorations.Count>0||Data.pageNotes.Any(n=>n.pageId==p.pageId&&n.favorite))
                .Select(p=>new{page=p,note=Data.pageNotes.Find(n=>n.pageId==p.pageId)}).OrderByDescending(p=>p.note?.createdDate??"").ThenBy(p=>p.page.pageId).ToArray();
            GUI.Label(new Rect(r.x,r.y+66,1400,48),"按创作日期翻阅；委托照片是独立归档，原页仍保留在自己的本子里。",small);
            scroll=GUI.BeginScrollView(new Rect(r.x,r.y+132,1450,370),scroll,new Rect(0,0,1400,Mathf.Max(360,pages.Length*72)));
            for(int i=0;i<pages.Length;i++)
            {
                var entry=pages[i];string caption=(entry.note?.createdDate??"旧页 · 日期未知")+"  "+(entry.note?.favorite==true?"★ ":"")+(!string.IsNullOrEmpty(entry.note?.title)?entry.note.title:entry.page.pageId)+"  · "+entry.page.decorations.Count+"件素材";
                if(Btn(new Rect(0,i*72,1360,60),caption))Navigate(entry.page.pageId);
            }
            GUI.EndScrollView();
            if(Btn(new Rect(r.x,r.y+520,450,50),"导出年度双页回顾")){try{Status="已导出："+StudioExports.ExportYear(workspace);}catch(Exception e){Status=e.Message;}}
        }
        void DrawPhoto(Rect r)
        {
            var mail=CommissionDeskController.Instance;var targets=mail&&mail.Ready?mail.Targets():new List<CommissionPageTarget>();targetIndex=Mathf.Clamp(targetIndex,0,Mathf.Max(0,targets.Count-1));
            GUI.Label(new Rect(r.x,r.y,850,44),"页面分享 / 工作室照片 / 委托归档",title);
            if(targets.Count>0)
            {
                if(Btn(new Rect(r.x,r.y+64,660,50),"导出页面："+targets[targetIndex].label))targetIndex=(targetIndex+1)%targets.Count;
                if(Btn(new Rect(r.x+680,r.y+64,500,50),"保存带小标识的分享图片")){try{Status="已保存PNG："+StudioExports.ExportPage(workspace,targets[targetIndex]);}catch(Exception e){Status=e.Message;}}
            }
            GUI.enabled=AtelierProgressCatalog.ToolUnlocked(workspace.Service.Document,"photo");
            if(Btn(new Rect(r.x,r.y+142,300,48),"滤镜："+new[]{"原色","暖纸","梦紫","回忆"}[photoFilter]))photoFilter=(photoFilter+1)%4;
            if(Btn(new Rect(r.x+320,r.y+142,360,48),"拍摄当前桌面的照片")){if(photo)Destroy(photo);photo=StudioGraphics.Scene(workspace.lens,photoFilter);Status="拍摄完成；镜头位置保持当前受控坐姿。";}
            if(photo&&Btn(new Rect(r.x+700,r.y+142,480,48),"保存拍立得到素材收藏")){var framed=StudioGraphics.Framed(photo);try{SaveArt(framed,"photo","云上拍立得");}finally{Destroy(framed);}}
            GUI.enabled=true;
            if(photo)GUI.DrawTexture(new Rect(r.x,r.y+218,640,350),photo,ScaleMode.ScaleToFit,true);
            else GUI.Label(new Rect(r.x,r.y+230,640,110),"照片工具在第3封回信后开放；页面分享从开始即可使用。个人作品不会自动上传。",label);
            var letters=workspace.Service.Document.commissionDesk?.letters.Where(l=>!l.IsOpen&&!string.IsNullOrEmpty(l.photoFile)).OrderByDescending(l=>l.sequence).ToArray()??Array.Empty<CommissionLetterState>();
            GUI.Label(new Rect(r.x+700,r.y+218,700,44),"已完成委托照片归档  ·  "+letters.Length+" 页",label);
            scroll=GUI.BeginScrollView(new Rect(r.x+700,r.y+278,740,280),scroll,new Rect(0,0,700,Mathf.Max(270,letters.Length*90)));
            for(int i=0;i<letters.Length;i++)
            {
                var letter=letters[i];string path=Path.Combine(mail.PhotoFolder,Path.GetFileName(letter.photoFile));
                if(!photoCache.TryGetValue(path,out var texture)&&File.Exists(path)){texture=new Texture2D(2,2);texture.LoadImage(File.ReadAllBytes(path));photoCache[path]=texture;}
                if(texture)GUI.DrawTexture(new Rect(0,i*90,60,82),texture,ScaleMode.ScaleToFit);
                GUI.Label(new Rect(80,i*90+4,590,78),letter.sender+" · "+letter.title+"\n"+letter.sentPageId,small);
            }
            GUI.EndScrollView();
        }
        void Navigate(string pageId)
        {
            var doc=workspace.Service.Document;var book=doc.notebookLibrary?.notebooks.Find(b=>b.pages.Any(p=>p.pageId==pageId));
            var work=workspace.journal.GetComponent<NotebookWorkbench>();
            if(work&&work.Ready&&!work.Use(book?.bookId??NotebookLibraryData.BinderId)){Status=work.Status;return;}
            if(!workspace.Service.NavigateStudioSpread(pageId)){Status=workspace.Service.LastMessage;return;}
            workspace.journal.RefreshPages();Close();StartCoroutine(EnterAfterModal());
        }
        void Report(bool ok){Status=ok?workspace.Service.LastMessage:workspace.Service.LastMessage;}
        bool Btn(Rect rect,string caption,bool selected=false)
        {var old=GUI.backgroundColor;GUI.backgroundColor=selected?DaydreamPalette.Mint:DaydreamPalette.Card;bool result=GUI.Button(rect,caption,button);GUI.backgroundColor=old;return result;}
        static void Fill(Rect rect,Color color){var old=GUI.color;GUI.color=color;GUI.DrawTexture(rect,Texture2D.whiteTexture);GUI.color=old;}
        void OnDestroy(){if(Instance==this)Instance=null;PersonalStudioAssets.Release();if(font)Destroy(font);if(canvas)Destroy(canvas);if(photo)Destroy(photo);foreach(var texture in photoCache.Values)if(texture)Destroy(texture);}
    }
}

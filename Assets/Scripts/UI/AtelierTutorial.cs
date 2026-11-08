using UnityEngine;

namespace VintageJournal
{
    /// <summary>Help is an explicit overlay; the input router owns dismissal and stops page/camera gestures beneath it.</summary>
    public sealed class AtelierTutorial : MonoBehaviour
    {
        public static AtelierTutorial Instance { get; private set; }
        public WorkspaceGameplayController workspace;
        public bool IsOpen { get; private set; }
        public int Chapter { get; private set; }
        public static bool Blocking => Instance && Instance.IsOpen;
        public static Rect ButtonRect => new Rect(1740, 106, 156, 48);
        GUIStyle label, title, button;
        Font font;
        Vector2 scroll;
        public static readonly string[] Titles = { "从桌前开始", "活页本与 TN", "翻页与纸张", "抽屉和胶带", "贴纸与便签", "离型本与转移", "委托信", "保存与快捷键", "三时段与手作房" };
        public static readonly string[] Chapters = {
            "你一直坐在云上的桌前。点击中央本子进入编辑，返回桌面或 ESC 退出。\n\n滚轮、＋／－调整查看大小；放大后按住右键或中键拖动查看局部。恢复视角回到完整桌面。\n\n点击实体可以选中，按住左键拖动拿取。ESC 取消未完成取放，R 回原来的收纳位。左上方两个离型本入口有各自的翻页按钮。",
            "架子上有四本旅行手账：两本 Standard（110 × 210 mm）和两本 Passport（90 × 134 mm）。\n\n点击 TN，再点右侧“放到中央”；也可以按住拖动 TN，松手放在中央本子的页面上。中央的活页本会收好，原来的 TN 会自动回到它的架位。\n\n点击“换回活页本”可返回。每本都有独立的 24 页、当前页码和装饰，换本不会复制素材。\n\nTN 是固定装订的米色内页，不能撕页或更换纸张。仍可翻页、贴素材、揭起贴纸和胶带；旅行票据也适合留在这里。",
            "点击中央本子后，底部左右按钮或键盘方向键以完整跨页翻页。第一页和最后一页会停止，翻页期间等待纸页落下。\n\n活页本可点击“更换内页”：选择图案，选择左页或右页，确认后保留。取消恢复原样。另一页不变。\n\n素材放大时可看清纸纹和图案。编辑面板关闭后才接收下一次桌面点击，避免误抓物件。",
            "木架下方两层抽屉存放胶带，最下面存放两本离型本。拖动把手向前拉开，再拿取里面的物品。\n\n上方 15 个独立横轴是临时工作位。先向前拉出轴杆，把一卷胶带放到轴端，再推回。每槽只放一卷。\n\n选中胶带后右键开始拉取，移动鼠标调整 2–30 cm，第二次右键确认。默认 5 cm，图案按真实长度连续重复。\n\n按左键贴到页面，Q/E 旋转、[ / ] 缩放，ESC 取消。过长胶带不会暗中缩短，可以旋转、缩放或换更大的页面。",
            "布艺挂袋收纳便签：取露在口袋外的纸张，移动到打开的页面，点击贴下。\n\n右侧三只盒子收纳白日梦、可爱生活和旅行主题贴纸。每张是独立有限实例；拿取后原收纳位会空出来。\n\n持有素材时 Q/E 旋转，Shift 配合可用更大角度；[ / ] 缩放。半透明预览落在纸内才可贴下；ESC 恢复原处。\n\n不会因为换本或重新启动而再生已经使用、删除的单张素材。",
            "先拉出木架最下层抽屉，拿出合上的离型本并放到桌面。点击本子或顶部 A/B 按钮打开。两本的页码、内容各自独立。\n\n胶带裁条、贴纸和便签都能贴在离型本上。选中已贴素材，拖动或按 P 揭起，再贴到另一页、另一本或中央手账。\n\n这会移动同一个素材，不生成副本。Ctrl+Z 撤销，Ctrl+Y 重做；拿着素材时 ESC 恢复。\n\n合上离型本后才能拿起或归位。拿着素材时不要更换中央本子，先完成或取消转移。",
            "点击桌边带立柱和底座的粉彩邮筒阅读委托。面板在右侧，点击“开始创作”关闭，收纳设施重新露出来。\n\n夜里有灯塔守望者和飞蛾图书管理员来信。读一读他们的短诗，试着表达信里的情绪。没有唯一正确答案，每一张完成的非空页都会被接受。\n\n再次打开信件，选择要寄出的手账 / TN / 离型本页面，点击“拍照寄出”。寄出的只是照片，素材仍留在原本子里。\n\n稍后收到回信、邮票、梦币和素材。新客户的故事随回信继续；相同页面内容不能反复寄出。旧委托仍可阅读和完成。",
            "操作完成后会自动保存。页码、纸张、装饰、物品归属、抽屉状态和委托信保存在同一份 journal-v1.json；保存位置是 Windows 用户数据目录。\n\nESC：先关闭教程／信件，再取消当前操作或返回桌面\nR：实体归位　P：揭起已选素材\nCtrl+Z / Ctrl+Y：撤销 / 重做素材操作\nQ / E：旋转　[ / ]：缩放素材\n方向键：中央本子翻页　滚轮：编辑视角缩放\n\n左上方返回桌面后，设置里可选择白天、黄昏、黑夜。重启会恢复选择。未完成的素材转移会回到安全起点。",
            "打开右上角“素材袋”，可以切换白天、午后与黑夜。六位朋友分别在白天、午后和夜晚送来故事信；已经收到的信和素材不受时段限制。\n\n返回桌面，轻点桌后漂浮的萤火、近星或小纸灯笼。它们会变成萤光墨印、近星贴纸和灯笼和纸，偶尔会遇到特别纹理或被遗忘的梦。\n\n打开素材袋取用，左键贴进打开的手账或离型本。Q/E旋转、[ / ]缩放、ESC放回；每份只用一次，揭取转移沿用同一个实例。\n\n初始有3份微光，每约两小时补充3份，最多等候6份，不会过期。没有连续登录要求。梦币和回礼自动存好；梦币可在左下方手作房兑换工具与装饰。"
        };
        void Awake() { Instance = this; }
        public void Open()
        {
            if (!workspace || !workspace.Ready || !workspace.desk.HasBegun) return;
            if (workspace.GetComponent<WorkspaceInputRouter>().IsDraggingMechanism || workspace.physical.IsSettling) return;
            IsOpen = true; scroll = Vector2.zero; workspace.desk.ConsumePointer(); RuntimeInputGate.Consume();
            Debug.Log("[Tutorial] Open");
        }
        public void Close() { IsOpen = false; workspace?.desk.ConsumePointer(); RuntimeInputGate.Consume(); Debug.Log("[Tutorial] Close"); }
        public bool Contains(Vector2 pixel)
        { return IsOpen || ButtonRect.Contains(new Vector2(pixel.x*1920/Screen.width,(Screen.height-pixel.y)*1080/Screen.height)); }
        void OnGUI()
        {
            if(AtelierHub.Instance&&AtelierHub.Instance.IsOpen)return;
            if (!workspace || !workspace.Ready || !workspace.desk.HasBegun || workspace.desk.IsBlocking) return;
            if (!font)
            {
                font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","Arial"},22);
                label=new GUIStyle(GUI.skin.label){font=font,fontSize=24,wordWrap=true};label.normal.textColor=DaydreamPalette.Ink;
                title=new GUIStyle(label){fontSize=32}; button=new GUIStyle(label){font=font,fontSize=22,alignment=TextAnchor.MiddleCenter}; button.hover.textColor=DaydreamPalette.Ink; button.active.textColor=DaydreamPalette.Ink;
            }
            var matrix=GUI.matrix;int depth=GUI.depth;GUI.depth=-150;
            GUI.matrix=Matrix4x4.Scale(new Vector3(Screen.width/1920f,Screen.height/1080f,1));
            if(!IsOpen){if(Button(ButtonRect,"教程",false))Open();}
            else
            {
                Fill(new Rect(0,0,1920,1080),new Color(.36f,.48f,.65f,.38f));
                Fill(new Rect(260,168,1400,760),DaydreamPalette.Cream);
                Fill(new Rect(260,168,1400,8),DaydreamPalette.Mint);
                GUI.Label(new Rect(306,204,800,60),"云上手作 · 教程",title);
                if(Button(new Rect(1442,202,170,52),"关闭  ×",false))Close();
                for(int i=0;i<Titles.Length;i++) if(Button(new Rect(306,300+i*66,240,54),Titles[i],Chapter==i)) { Chapter=i;scroll=Vector2.zero; }
                GUI.Label(new Rect(592,300,970,54),Titles[Chapter],title);
                scroll=GUI.BeginScrollView(new Rect(590,370,1018,494),scroll,new Rect(0,0,970,Mathf.Max(490,label.CalcHeight(new GUIContent(Chapters[Chapter]),940)+28)));
                GUI.Label(new Rect(12,10,940,900),Chapters[Chapter],label);GUI.EndScrollView();
            }
            GUI.matrix=matrix;GUI.depth=depth;
        }
        bool Button(Rect r,string caption,bool selected)
        { Fill(r,selected?DaydreamPalette.Mint:DaydreamPalette.Card);Fill(new Rect(r.x,r.yMax-2,r.width,2),DaydreamPalette.SkyDeep);return GUI.Button(r,caption,button); }
        static void Fill(Rect r,Color c){var old=GUI.color;GUI.color=c;GUI.DrawTexture(r,Texture2D.whiteTexture);GUI.color=old;}
        void OnDestroy(){if(Instance==this)Instance=null;if(font)Destroy(font);}
    }
}

using System.Linq;
using UnityEngine;

namespace VintageJournal
{
    /// <summary>Small action hints accompany the actual world objects; storage and page content remain 3D.</summary>
    public sealed class WorkspaceGameplayHUD : MonoBehaviour
    {
        public WorkspaceGameplayController workspace;
        GUIStyle label,title,small,button;
        Font font;
        readonly Color paper=DaydreamPalette.Paper,ink=DaydreamPalette.Ink,brass=DaydreamPalette.SkyDeep;
        bool Visible=>!(AtelierHub.Instance&&AtelierHub.Instance.IsOpen)&&
            !(DrawerStockIndex.Instance&&DrawerStockIndex.Instance.Expanded&&DrawerStockIndex.Instance.IsVisible)&&
            !AtelierTutorial.Blocking&&workspace&&workspace.Ready&&workspace.desk&&workspace.desk.HasBegun&&!workspace.desk.IsBlocking;
        bool PaperSelected=>workspace.SelectedPhysical&&workspace.SelectedPhysical.itemType==PhysicalItemType.LoosePage;
        bool NotebookSelected=>workspace.SelectedPhysical&&workspace.SelectedPhysical.itemType==PhysicalItemType.TravelersNotebook;
        bool ActionsVisible=>workspace.SelectedTape||workspace.Service.HeldItem!=null||workspace.SelectedMaterialIsReleaseBook||PaperSelected||(workspace.SelectedPhysical&&workspace.IsReleaseBook(workspace.SelectedPhysical));
        bool Detailed=>NotebookSelected||workspace.TapeLengthSelecting||workspace.Service.HeldItem!=null||workspace.SelectedMaterialIsReleaseBook||PaperSelected;
        public bool Contains(Vector2 screen)
        {
            if(!Visible)return false;
            var p=new Vector2(screen.x*1920/Screen.width,(Screen.height-screen.y)*1080/Screen.height);
            if((!workspace.journal.IsEditing||workspace.Service.HeldItem!=null)&&new Rect(580,24,1136,70).Contains(p))return true;
            if(ActionsVisible&&new Rect(1120,840,776,122).Contains(p))return true;
            if(workspace.journal.Document.ActiveNotebook!=null&&new Rect(24,110,245,54).Contains(p))return true;
            if(Detailed&&new Rect(1510,175,386,490).Contains(p))return true;
            return false;
        }
        void Styles()
        {
            if(font)return;font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","Microsoft YaHei UI","Arial"},20);
            label=Style(20);title=Style(25);small=Style(17);
            button=new GUIStyle(GUI.skin.button){font=font,fontSize=19,wordWrap=true};
            button.normal.background=button.hover.background=button.active.background=Texture2D.whiteTexture;
            button.normal.textColor=button.hover.textColor=button.active.textColor=ink;
        }
        GUIStyle Style(int size){var style=new GUIStyle(GUI.skin.label){font=font,fontSize=size,wordWrap=true};style.normal.textColor=ink;return style;}
        void OnGUI()
        {
            if(!Visible)return;Styles();var old=GUI.matrix;var color=GUI.color;var background=GUI.backgroundColor;
            GUI.matrix=Matrix4x4.Scale(new Vector3(Screen.width/1920f,Screen.height/1080f,1));GUI.color=Color.white;GUI.backgroundColor=paper;
            if(!workspace.journal.IsEditing||workspace.Service.HeldItem!=null)BooksToolbar();
            if(workspace.journal.Document.ActiveNotebook!=null&&Btn(new Rect(24,110,245,54),"换回活页本"))workspace.journal.GetComponent<NotebookWorkbench>().UseBinder();
            if(ActionsVisible) {
            Panel(new Rect(1120,840,776,122));
            GUI.Label(new Rect(1138,851,738,50),workspace.Status,label);
            bool held=workspace.Service.HeldItem!=null;
            if(workspace.SelectedTape&&!workspace.InteractionBusy&&!workspace.journal.IsEditing)
            {
                if(Btn(new Rect(1138,908,220,45),"选长度 · 默认5 cm"))workspace.StartTapeLength(workspace.PointerWorld);
                if(Btn(new Rect(1370,908,220,45),"取下选中胶带"))workspace.DetachSelectedTape();
                if(Btn(new Rect(1602,908,270,45),"R 放回原收纳位"))workspace.ReturnSelected();
            }
            else if(workspace.SelectedPhysical&&workspace.IsReleaseBook(workspace.SelectedPhysical)&&!workspace.InteractionBusy)
            {
                string id=workspace.BookIdFor(workspace.SelectedPhysical);var book=workspace.Service.FindBook(id);
                if(Btn(new Rect(1138,908,220,45),book!=null&&book.isOpen?"合上离型本":"打开离型本"))workspace.OpenBook(id,book==null||!book.isOpen);
                if(Btn(new Rect(1370,908,220,45),"拿起查看内页"))workspace.InspectBook(workspace.SelectedPhysical);
                if(Btn(new Rect(1602,908,270,45),"R 放回抽屉"))workspace.ReturnSelected();
            }
            else if(!held&&(!string.IsNullOrEmpty(workspace.scrapbook.SelectedId)||!string.IsNullOrEmpty(workspace.SelectedMaterialId)))
            {
                if(Btn(new Rect(1138,908,352,45),"P 揭取选中素材"))workspace.BeginPeel(workspace.scrapbook.SelectedId??workspace.SelectedMaterialId);
                if(Btn(new Rect(1510,908,362,45),"取消素材选择"))workspace.Cancel();
            }
            else if(held&&workspace.journal.IsEditing)
            {
                if(Btn(new Rect(1138,908,352,45),"带着素材查看桌面目标"))workspace.journal.ExitEditing();
                GUI.Label(new Rect(1510,908,362,45),"保留手持；ESC仍可恢复",small);
            }
            else GUI.Label(new Rect(1138,908,735,44),held?"左键贴到页面 · Q/E旋转 · [/]缩放 · ESC恢复":"左键选中原位 · 右拖移动 · 离型本左拖/右点打开 · 胶带向前拉",small);
            }
            if(Detailed)SelectionPanel();
            GUI.matrix=old;GUI.color=color;GUI.backgroundColor=background;GUI.enabled=true;
        }
        void BooksToolbar()
        {
            var books=workspace.Service.Books;
            for(int i=0;i<Mathf.Min(2,books.Count);i++)
            {
                var book=books[i];float x=580+i*568;
                string name=i==0?"离型本 A":"离型本 B";
                if(Btn(new Rect(x,24,254,60),name+(book.isOpen?" · 合上":" · 打开")))
                {
                    var view=workspace.releaseBooks.FirstOrDefault(v=>v&&v.bookId==book.bookId);
                    if(view&&view.physical)workspace.SelectPhysical(view.physical);
                    workspace.OpenBook(book.bookId,!book.isOpen);
                }
                GUI.enabled=book.isOpen&&workspace.CanNavigateTargetBook;
                if(Btn(new Rect(x+264,24,136,60),"← 上页"))workspace.TurnBook(book.bookId,-1);
                if(Btn(new Rect(x+410,24,136,60),"下页 →"))workspace.TurnBook(book.bookId,1);
                GUI.enabled=true;
            }
        }
        void SelectionPanel()
        {
            Panel(new Rect(1510,175,386,490));
            if(NotebookSelected) {
                var notebook=workspace.SelectedPhysical;var book=workspace.journal.GetComponent<NotebookWorkbench>();
                GUI.Label(new Rect(1530,192,346,68),notebook.displayName,title);
                GUI.Label(new Rect(1530,280,346,130),"固定装订 · 24 页\n独立保存页码和装饰\n右点查看并翻页，右拖移动\n拖到中央本子可切换工作本",label);
                GUI.enabled=!workspace.InteractionBusy&&!workspace.journal.IsBusy;
                if(Btn(new Rect(1530,436,346,58),"放到中央"))book.Use(notebook);
                GUI.enabled=true;
                if(Btn(new Rect(1530,510,346,48),"拿起查看 / 翻看内页"))workspace.InspectBook(notebook);
                if(Btn(new Rect(1530,572,346,48),"R 放回架子"))workspace.ReturnSelected();
                return;
            }
            if(PaperSelected&&!workspace.TapeLengthSelecting&&workspace.Service.HeldItem==null)
            {
                var physical=workspace.SelectedPhysical;
                var binding=physical.GetComponent<LoosePageBinding>();
                string paperId=binding?binding.paperId:physical.assetId;
                var paperDefinition=workspace.journal.Catalog.Find(paperId);
                var metres=binding?binding.pageSize:new Vector2(physical.dimensions.size.x,physical.dimensions.size.z);
                int holes=binding&&binding.holeAnchors!=null?binding.holeAnchors.Count(anchor=>anchor):0;
                string bindingName=binding?binding.bindingType:"";
                if(bindingName=="SixRing_A5")bindingName="六孔活页 · A5";
                else if(bindingName=="SixRing_A6")bindingName="六孔活页 · A6";
                GUI.Label(new Rect(1530,192,346,48),"内页图案预览",title);
                if(paperDefinition!=null&&paperDefinition.texture)
                    GUI.DrawTexture(new Rect(1530,252,136,194),paperDefinition.texture,ScaleMode.ScaleToFit,true);
                GUI.Label(new Rect(1684,252,192,60),binding?binding.style:physical.displayName,label);
                GUI.Label(new Rect(1684,320,192,66),(metres.x*1000).ToString("F0")+" × "+(metres.y*1000).ToString("F0")+" mm\n"+bindingName,small);
                GUI.Label(new Rect(1684,394,192,52),holes+" 个独立孔位锚点",small);
                GUI.Label(new Rect(1530,466,346,68),paperDefinition!=null?paperDefinition.displayName:paperId,label);
                GUI.Label(new Rect(1530,540,346,80),"右拖纸张到桌面 · R 放回抽屉\n点击中央手账 → 更换内页\n孔位留给后续装订。",small);
                return;
            }
            if(workspace.TapeLengthSelecting)
            {
                GUI.Label(new Rect(1530,192,346,50),"拉出一段和纸胶带",title);
                GUI.Label(new Rect(1530,258,346,85),(workspace.TapeLengthMeters*100).ToString("F1")+" cm\n宽 "+((workspace.SelectedTape.dimensions?workspace.SelectedTape.dimensions.size.y:.015f)*1000).ToString("F0")+" mm",title);
                GUI.Label(new Rect(1530,362,346,100),"向桌前拉拽调整长度\n范围 2–30 cm\n右键或按钮确认，原卷保留",label);
                if(Btn(new Rect(1530,490,346,54),"确认撕取"))workspace.ConfirmTapeLength();
                if(Btn(new Rect(1530,562,346,54),"ESC 取消"))workspace.Cancel();
                return;
            }
            var item=workspace.Service.HeldItem??workspace.Service.GetItem(workspace.SelectedMaterialId);
            if(item==null)return;
            var definition=workspace.scrapbook.Catalog.Find(item.assetId);
            GUI.Label(new Rect(1530,192,346,55),definition!=null?definition.displayName:item.assetId,title);
            GUI.Label(new Rect(1530,249,346,50),item.kind==CraftItemKind.TapeStrip?"裁切长度 "+(item.lengthMeters*100).ToString("F1")+" cm · 缩放 "+item.scale.ToString("F2"):"同一素材实例 · 缩放 "+item.scale.ToString("F2"),small);
            if(Btn(new Rect(1530,318,164,48),"Q 逆时针"))workspace.EditSelected("rotate",-15);
            if(Btn(new Rect(1712,318,164,48),"E 顺时针"))workspace.EditSelected("rotate",15);
            if(Btn(new Rect(1530,380,164,48),"[ 缩小"))workspace.EditSelected("scale",-.1f);
            if(Btn(new Rect(1712,380,164,48),"] 放大"))workspace.EditSelected("scale",.1f);
            GUI.enabled=workspace.Service.HeldItem==null;
            if(Btn(new Rect(1530,442,164,48),"置于底层"))workspace.EditSelected("bottom");
            if(Btn(new Rect(1712,442,164,48),"置于顶层"))workspace.EditSelected("top");
            if(Btn(new Rect(1530,504,346,48),"P 揭起并转移"))workspace.BeginPeel(item.instanceId);
            if(Btn(new Rect(1530,566,164,48),"Delete 删除"))workspace.EditSelected("delete");
            GUI.enabled=true;
            if(Btn(new Rect(1712,566,164,48),"取消"))workspace.Cancel();
        }
        bool Btn(Rect r,string text)
        {
            bool clicked=GUI.Button(r,text,button);
            if(clicked){RuntimeInputGate.Consume();workspace.desk.ConsumePointer();}
            return clicked;
        }
        void Panel(Rect r)
        {var color=GUI.color;GUI.color=paper;GUI.DrawTexture(r,Texture2D.whiteTexture);GUI.color=brass;GUI.DrawTexture(new Rect(r.x,r.y,r.width,2),Texture2D.whiteTexture);GUI.color=color;}
        void OnDestroy(){if(font)Destroy(font);}
    }
}

using UnityEngine;
namespace VintageJournal
{
    public sealed class PhysicalWorkspaceHUD : MonoBehaviour
    {
        public PhysicalInteractionController controller;
        public PhysicalMouseInputAdapter input;
        public PhysicalContainer[] containers;
        public DeskUI desk;
        public JournalController journal;
        GUIStyle label,button; Font font;
        bool Visible=>!(AtelierHub.Instance&&AtelierHub.Instance.IsOpen)&&desk&&desk.HasBegun&&!desk.IsBlocking&&(!journal||!journal.IsEditing);
        public bool Contains(Vector2 screen)
        {
            if(!Visible)return false;
            var p=new Vector2(screen.x*1920/Screen.width,(Screen.height-screen.y)*1080/Screen.height);
            return new Rect(580,24,1140,58).Contains(p)||new Rect(1110,905,786,150).Contains(p);
        }
        void OnGUI()
        {
            if(!Visible||!controller)return;
            if(label==null)
            {
                font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","SimHei","Arial"},20);
                label=new GUIStyle(GUI.skin.label){font=font,fontSize=20,wordWrap=true};
                label.normal.textColor=DaydreamPalette.Ink;
                button=new GUIStyle(GUI.skin.button){font=font,fontSize=20};
                button.normal.background=Texture2D.whiteTexture;
                button.hover.background=Texture2D.whiteTexture;
                button.active.background=Texture2D.whiteTexture;
                button.normal.textColor=label.normal.textColor;
                button.hover.textColor=label.normal.textColor;
                button.active.textColor=label.normal.textColor;
            }
            var old=GUI.matrix;var oldColor=GUI.color;var oldBackground=GUI.backgroundColor;
            GUI.matrix=Matrix4x4.Scale(new Vector3(Screen.width/1920f,Screen.height/1080f,1));
            GUI.color=Color.white;GUI.backgroundColor=DaydreamPalette.Card;
            if(containers!=null)for(int i=0;i<containers.Length;i++)
            {
                var c=containers[i];if(!c||!c.canClose)continue;
                string shortName=c.displayName.Split('·')[0].Trim();
                if(GUI.Button(new Rect(580+i*228,24,220,58),shortName+(c.isOpen?" · 合上":" · 打开"),button))
                {input.Toggle(c);desk.ConsumePointer();}
            }
            GUI.color=DaydreamPalette.Paper;
            GUI.DrawTexture(new Rect(1110,905,786,150),Texture2D.whiteTexture);
            GUI.color=Color.white;
            var item=controller.HeldItem?controller.HeldItem:controller.HoveredItem;
            string name=item?item.displayName:(input.HoveredContainer?input.HoveredContainer.displayName:"实体工作台");
            string hint=controller.IsInspecting?"正在查看 · R 放回书架 · ESC / 右键取消":
                controller.HeldItem?"按住左键移动，松开放置 · R 归位 · ESC / 右键取消":
                "左键拿取 / 打开盒子 · E 查看 · R 归位\n点击中央手账进入原有数字编辑";
            GUI.Label(new Rect(1130,918,740,44),name,label);
            GUI.Label(new Rect(1130,962,740,82),hint,label);
            GUI.matrix=old;GUI.color=oldColor;GUI.backgroundColor=oldBackground;
        }
        void OnDestroy(){if(font)Destroy(font);}
    }
}

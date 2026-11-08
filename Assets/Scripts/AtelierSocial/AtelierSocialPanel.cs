using System;
using System.Linq;
using UnityEngine;

namespace VintageJournal
{
    public static class AtelierSocialPanel
    {
        static Vector2 scroll;
        static GUIStyle label, small, button, field;
        static Font font;
        static readonly string[] views={"画廊","我的发布","好友寄信","收件箱","玩家委托","本机审核"};
        static void Styles()
        {
            if(font)return;
            font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","Arial"},20);
            label=new GUIStyle(GUI.skin.label){font=font,fontSize=20,wordWrap=true};small=new GUIStyle(label){fontSize=16};
            button=new GUIStyle(GUI.skin.button){font=font,fontSize=18,wordWrap=true};
            button.normal.background=button.hover.background=button.active.background=Texture2D.whiteTexture;
            button.normal.textColor=button.hover.textColor=button.active.textColor=DaydreamPalette.Ink;
            field=new GUIStyle(GUI.skin.textField){font=font,fontSize=18};
        }
        public static void Draw(Rect rect,WorkspaceGameplayController workspace)
        {
            var c=AtelierSocialController.Ensure(workspace);Styles();
            label.normal.textColor=small.normal.textColor=DaydreamPalette.Text(DaydreamPalette.Ink);
            GUI.BeginGroup(rect);float width=rect.width;
            GUI.Label(new Rect(0,0,width,34),c.Busy?"邮局正在处理…":c.Status,label);
            GUI.Label(new Rect(0,38,58,30),"地址",small);c.ServerUrl=GUI.TextField(new Rect(58,36,Mathf.Min(450,width*.4f),34),c.ServerUrl,180,field);
            float nameX=Mathf.Min(520,width*.46f);c.Nickname=GUI.TextField(new Rect(nameX,36,175,34),c.Nickname,24,field);
            GUI.enabled=!c.Busy;
            if(GUI.Button(new Rect(nameX+185,36,150,34),c.Registered?"新建本机身份":"注册并连接",button))c.Register();
            GUI.enabled=true;
            GUI.Label(new Rect(0,78,width,30),c.Registered?"当前："+c.Data.name+"  ·  好友码："+c.Data.friendCode+"  ·  默认只连接本机，不上传私人日记。":"先运行本机邮局，再注册独立的匿名测试身份。",small);
            float tabWidth=(width-145)/views.Length;
            for(int i=0;i<views.Length;i++)
            {
                var old=GUI.backgroundColor;GUI.backgroundColor=c.View==i?DaydreamPalette.Mint:DaydreamPalette.Card;
                if(GUI.Button(new Rect(i*tabWidth,112,tabWidth-6,38),views[i],button)){c.View=i;scroll=Vector2.zero;c.Refresh();}
                GUI.backgroundColor=old;
            }
            GUI.enabled=!c.Busy&&c.Registered;
            if(GUI.Button(new Rect(width-138,112,138,38),"刷新",button))c.Refresh();
            GUI.enabled=true;
            float top=160;
            if(c.View==0||c.View==1||c.View==2||c.View==4)
            {
                c.Title=GUI.TextField(new Rect(0,top,Mathf.Min(450,width*.44f),34),c.Title,80,field);
                c.Text=GUI.TextField(new Rect(Mathf.Min(460,width*.45f),top,width-Mathf.Min(460,width*.45f),34),c.Text,800,field);
                top+=42;
            }
            GUI.enabled=!c.Busy&&c.Registered;
            if(c.View==0||c.View==1)
            {
                if(GUI.Button(new Rect(0,top,220,36),"拍照发布当前"+(c.ShareSide==JournalSide.Left?"左页":"右页"),button))c.PublishPage();
                if(GUI.Button(new Rect(230,top,150,36),"切换左／右页",button))c.ShareSide=c.ShareSide==JournalSide.Left?JournalSide.Right:JournalSide.Left;
                GUI.Label(new Rect(395,top,width-400,36),"仅公开你主动提交并经审核的照片，无评论。",small);top+=44;
            }
            if(c.View==2)
            {
                c.FriendCode=GUI.TextField(new Rect(0,top,250,34),c.FriendCode,24,field);
                if(GUI.Button(new Rect(264,top,180,34),"用好友码邀请",button))c.Invite();
                var artworks=workspace.Service.Document.personalStudio?.artworks?.Where(a=>a.kind=="sticker").ToArray();
                if(artworks!=null&&artworks.Length>0)
                {
                    c.GiftTemplateIndex=Mathf.Clamp(c.GiftTemplateIndex,0,artworks.Length-1);
                    if(GUI.Button(new Rect(460,top,width-460,34),"随信附上："+artworks[c.GiftTemplateIndex].name+"  ›",button))c.GiftTemplateIndex=(c.GiftTemplateIndex+1)%artworks.Length;
                }
                else GUI.Label(new Rect(460,top,width-460,34),"贴纸工坊保存模板后，可随信赠送。",small);
                top+=44;
            }
            if(c.View==4)
            {if(GUI.Button(new Rect(0,top,250,36),"发布上方诗意委托",button))c.PostCommission();GUI.Label(new Rect(265,top,width-265,36),"接单和作品也经过本机审核。",small);top+=44;}
            GUI.enabled=true;
            if(c.View==5)
            {
                GUI.Label(new Rect(0,top,250,32),"独立审核密钥（不保存）",small);
                c.AdminKey=GUI.PasswordField(new Rect(260,top,width-260,32),c.AdminKey,'•',120,field);
                top+=38;GUI.Label(new Rect(0,top,width,46),"密钥在 SocialService/data/moderator-key.txt。先输入后刷新。普通身份不能审核；所有文字和图片默认待审。",small);top+=50;
            }
            float inner=width-36;
            float contentHeight=80+c.Entries.Sum(e=>Mathf.Max(215,Mathf.Max(90,small.CalcHeight(new GUIContent(EntryText(c,e)),inner-180))+125))+c.Friends.Length*65+c.Invitations.Length*65+(c.Data?.gifts.Count??0)*60+(c.View==5?c.ModeratorUsers.Length*48:0);
            scroll=GUI.BeginScrollView(new Rect(0,top,width,Mathf.Max(80,rect.height-top)),scroll,new Rect(0,0,width-24,Mathf.Max(rect.height-top,contentHeight)));
            float y=4;
            GUI.enabled=!c.Busy&&c.Registered;
            if(c.View==2)
            {
                foreach(var invitation in c.Invitations)
                {GUI.Label(new Rect(0,y,inner-200,44),(invitation.from?.name??"朋友")+"想与你交换来信",label);if(GUI.Button(new Rect(inner-190,y,180,38),"接受邀请",button))c.AcceptInvite(invitation.id);y+=60;}
                foreach(var friend in c.Friends)
                {GUI.Label(new Rect(0,y,inner-230,44),friend.name+" · "+friend.code,label);if(GUI.Button(new Rect(inner-220,y,210,38),"寄上方信／贴纸",button))c.SendLetter(friend);y+=60;}
            }
            foreach(var entry in c.Entries)
            {
                string entryText=EntryText(c,entry);float bodyHeight=Mathf.Max(90,small.CalcHeight(new GUIContent(entryText),inner-180));
                var image=c.Preview(entry);if(image)GUI.DrawTexture(new Rect(0,y,155,150),image,ScaleMode.ScaleToFit,true);
                GUI.Label(new Rect(172,y,inner-180,32),entry.title+" · "+entry.ownerName,label);
                GUI.Label(new Rect(172,y+38,inner-180,bodyHeight),entryText,small);
                GUI.Label(new Rect(172,y+bodyHeight+40,inner-180,27),State(entry.status)+" · "+entry.createdUtc,small);
                float x=0;float actionY=y+bodyHeight+72;
                if(c.View==0)
                {if(Btn(ref x,actionY,140,entry.liked?"已喜欢 ♥":"喜欢 ♡",!entry.liked))c.Like(entry);if(Btn(ref x,actionY,130,"举报",true))c.Report(entry);if(entry.ownerId!=c.Data.playerId&&Btn(ref x,actionY,150,"屏蔽作者",true))c.Block(entry);}
                if(c.View==3)
                {bool claimed=c.Data.claimedGiftIds.Contains(entry.id);if(Btn(ref x,actionY,210,claimed?"已入存档／重试确认":entry.acked?"其他存档已领取":"保存来信和礼物",!entry.acked||claimed))c.Receive(entry);}
                if(c.View==4)
                {
                    bool mine=entry.ownerId==c.Data.playerId,done=!string.IsNullOrEmpty(entry.fulfilledId);
                    if(Btn(ref x,actionY,200,done?"已完成":entry.acceptedBy==c.Data.playerId?"提交当前页面照片":"接下这封委托",!mine&&!done&&(string.IsNullOrEmpty(entry.acceptedBy)||entry.acceptedBy==c.Data.playerId)))
                    {if(entry.acceptedBy==c.Data.playerId)c.Fulfill(entry);else c.AcceptCommission(entry);}
                    if(Btn(ref x,actionY,120,"举报",true))c.Report(entry);
                }
                if(c.View==5)
                {if(Btn(ref x,actionY,160,"审核通过",true))c.Moderate(entry,true);if(Btn(ref x,actionY,160,"拒绝公开",true))c.Moderate(entry,false);}
                y+=Mathf.Max(215,bodyHeight+125);
            }
            if(c.View==3&&c.Data.gifts.Count>0)
            {
                GUI.Label(new Rect(0,y,inner,32),"已收好的有限贴纸（每份只用一次）",label);y+=40;
                foreach(var gift in c.Data.gifts)
                {GUI.Label(new Rect(0,y,inner-230,38),gift.name,label);if(GUI.Button(new Rect(inner-220,y,210,38),"拿起这份礼物",button))c.TakeGift(gift);y+=60;}
            }
            if(c.View==5)foreach(var user in c.ModeratorUsers)
            {GUI.Label(new Rect(0,y,inner-230,38),user.name+" · "+(user.banned?"已暂停":"正常"),label);if(GUI.Button(new Rect(inner-220,y,210,36),user.banned?"恢复账号":"暂停账号",button))c.Ban(user);y+=48;}
            if(y<20)GUI.Label(new Rect(0,y,inner,75),c.View==2?"输入朋友的好友码，接受邀请后互寄信件。":"这里暂时安静。点击刷新，或先发布一份等待审核的作品。",label);
            GUI.enabled=true;GUI.EndScrollView();GUI.EndGroup();
        }
        static bool Btn(ref float x,float y,float width,string caption,bool enabled)
        {bool old=GUI.enabled;GUI.enabled=old&&enabled;bool hit=GUI.Button(new Rect(x,y,width,36),caption,button);x+=width+10;GUI.enabled=old;return hit;}
        static string State(string value)=>value=="approved"?"已审核公开":value=="pending"?"待审核":value=="quarantined"?"举报后待复核":"未公开";
        static string EntryText(AtelierSocialController controller,SocialEntry entry)=>
            (entry.text??string.Empty)+(controller.View==5?string.Concat(controller.ModeratorReports.Where(r=>r.contentId==entry.id).Select(r=>"\n举报："+r.reason)):string.Empty);
    }
}

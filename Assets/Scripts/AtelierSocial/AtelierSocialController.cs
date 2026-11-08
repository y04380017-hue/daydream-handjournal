using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace VintageJournal
{
    /// <summary>Networking happens only after explicit panel actions; never publishes the private diary.</summary>
    public sealed class AtelierSocialController : MonoBehaviour
    {
        public WorkspaceGameplayController workspace;
        public bool Busy { get; private set; }
        public bool LastRequestSucceeded { get; private set; }
        public string Status { get; private set; } = "本机邮局需要先启动。连接只在你点击后进行。";
        public SocialEntry[] Entries { get; private set; } = Array.Empty<SocialEntry>();
        public SocialUser[] Friends { get; private set; } = Array.Empty<SocialUser>();
        public SocialFriendRequest[] Invitations { get; private set; } = Array.Empty<SocialFriendRequest>();
        public SocialUser[] ModeratorUsers { get; private set; } = Array.Empty<SocialUser>();
        public SocialReport[] ModeratorReports { get; private set; } = Array.Empty<SocialReport>();
        public string ServerUrl = "http://127.0.0.1:18747", Nickname = "云上的朋友", FriendCode = "", Title = "一页给你的心情", Text = "", AdminKey = "", ReportReason = "希望审核员看看这份内容";
        public int View, GiftTemplateIndex;
        public JournalSide ShareSide = JournalSide.Left;
        readonly Dictionary<string, Texture2D> previews = new Dictionary<string, Texture2D>();
        public AtelierSocialData Data => workspace.Service.Document.atelierSocial;
        public bool Registered => !string.IsNullOrEmpty(Data?.token);
        public static AtelierSocialController Ensure(WorkspaceGameplayController workspace)
        {
            var component = workspace.GetComponent<AtelierSocialController>();
            if (!component) { component = workspace.gameObject.AddComponent<AtelierSocialController>(); component.workspace = workspace; component.Initialize(); }
            return component;
        }
        void Initialize()
        {
            workspace.Service.InitializeAtelierSocial(); SocialMaterialAssets.Bind(workspace);
            if (Data != null) { ServerUrl = Data.serverUrl; if (!string.IsNullOrEmpty(Data.name)) Nickname = Data.name; }
        }
        public void Register()
        {
            if (Busy) return;
            if (!AtelierSocialData.ValidServerUrl(ServerUrl)) { Status = "请填写本机HTTP或HTTPS地址，不含路径。"; return; }
            StartCoroutine(Request("POST", "/v1/register", new SocialPayload { name = Nickname }, result => {
                if (workspace.Service.ConfigureSocial(ServerUrl, result.name, result.id, result.code, result.token)) Status = "已连接为" + result.name + "，好友码 " + result.code;
                else Status = workspace.Service.LastMessage;
            }, authenticate:false));
        }
        public void Refresh()
        {
            if (Busy || !Registered) return;
            string endpoint = View == 0 ? "/v1/gallery" : View == 1 ? "/v1/mine" : View == 2 ? "/v1/friends" : View == 3 ? "/v1/inbox" : View == 4 ? "/v1/commissions" : "/v1/moderation";
            StartCoroutine(Request("GET", endpoint, null, result => {
                Entries = result.entries ?? Array.Empty<SocialEntry>(); Friends = result.users ?? Array.Empty<SocialUser>(); Invitations = result.requests ?? Array.Empty<SocialFriendRequest>();
                if (View == 5) { ModeratorUsers = result.users ?? Array.Empty<SocialUser>(); ModeratorReports = result.reports ?? Array.Empty<SocialReport>(); } Status = "已刷新。只有审核通过的内容会公开或寄达。";
            }, admin:View == 5));
        }
        public IEnumerator Request(string method, string endpoint, SocialPayload body, Action<SocialEnvelope> success, bool authenticate = true, bool admin = false)
        {
            if (Busy) yield break;
            LastRequestSucceeded = false;
            if (!AtelierSocialData.ValidServerUrl(ServerUrl)) { Status = "连接地址无效。"; yield break; }
            if ((authenticate || admin) && (!Registered || !AtelierSocialData.SameOrigin(ServerUrl,Data.serverUrl)))
            { Status="连接地址已改变。请先在新地址注册身份，旧连接凭据不会发送到新服务器。";yield break; }
            Busy = true; Status = "正在与本机邮局通信…";
            using (var request = new UnityWebRequest(ServerUrl.TrimEnd('/') + endpoint, method))
            {
                request.downloadHandler = new DownloadHandlerBuffer(); request.timeout = 10; request.redirectLimit=0;
                if (body != null) { request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(body))); request.SetRequestHeader("Content-Type", "application/json"); }
                if (authenticate && Registered) request.SetRequestHeader("Authorization", "Bearer " + Data.token);
                if (admin) request.SetRequestHeader("X-Admin-Key", AdminKey.Trim());
                yield return request.SendWebRequest();
                Busy = false;
                SocialEnvelope response = null;
                try { response = JsonUtility.FromJson<SocialEnvelope>(request.downloadHandler.text); } catch (ArgumentException) { }
                if (request.result != UnityWebRequest.Result.Success || response == null || !string.IsNullOrEmpty(response.error))
                { Status = response?.error ?? "没有连上本机邮局。请先运行一键启动或检查端口。"; yield break; }
                LastRequestSucceeded = true; Status = "邮局已收到。"; success?.Invoke(response);
            }
        }
        bool Capture(out string png)
        {
            png = null;
            if (workspace.InteractionBusy || workspace.journal.HasPaperPreview || workspace.scrapbook.IsTransforming) { Status = "请先完成或取消页面编辑。"; return false; }
            var commission = CommissionDeskController.Instance;
            var page = workspace.journal.Document.GetPage(ShareSide);
            var target = commission ? commission.Targets().FirstOrDefault(t => t.owner == CraftOwner.JournalPage && t.pageId == page?.pageId) : null;
            if (target == null || page == null || page.decorations.Count == 0) { Status = "选中的页面还是空的，先留下些创作吧。"; return false; }
            string folder = Path.Combine(Path.GetDirectoryName(workspace.journal.SavePath), "social-exports"); Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, "page-" + Guid.NewGuid().ToString("N") + ".png");
            if (!commission.CapturePage(target, file)) { Status = "照片尚未完成，请稍后重试。"; return false; }
            png = Convert.ToBase64String(File.ReadAllBytes(file)); return true;
        }
        public void PublishPage()
        {
            if (Busy || !Registered || !Capture(out string png)) return;
            StartCoroutine(Request("POST", "/v1/gallery", new SocialPayload { title=Title, text=Text, pngBase64=png, requestKey=Guid.NewGuid().ToString("N") }, _ => Status="作品已进入审核队列，通过后才会出现在画廊。"));
        }
        public void PublishPng(string title,string text,string pngBase64,string key = null)
        { if(!Busy&&Registered)StartCoroutine(Request("POST","/v1/gallery",new SocialPayload{title=title,text=text,pngBase64=pngBase64,requestKey=key??Guid.NewGuid().ToString("N")},_=>Status="作品已进入审核队列，通过后才公开。")); }
        public void Invite() { if (!Busy && Registered) StartCoroutine(Request("POST", "/v1/friends", new SocialPayload { code=FriendCode.Trim() }, _=>Status="好友邀请已送出，对方接受后才可寄礼物。")); }
        public void AcceptInvite(string id) { if (!Busy) StartCoroutine(Request("POST", "/v1/friends/accept", new SocialPayload {requestId=id}, _=>Status="已接受好友邀请，请刷新列表。")); }
        public void Like(SocialEntry item) { if (!Busy) StartCoroutine(Request("POST", "/v1/gallery/"+item.id+"/like", new SocialPayload(), _=>{item.liked=true;item.likes=Math.Max(1,item.likes);Status="心意已经留下。";})); }
        public void Report(SocialEntry item) { if (!Busy) StartCoroutine(Request("POST", "/v1/reports", new SocialPayload {contentId=item.id,reason=ReportReason}, _=>{Entries=Entries.Where(e=>e.id!=item.id).ToArray();Status="已举报，该内容暂时下架等待复核。";})); }
        public void Block(SocialEntry item) { if (!Busy) StartCoroutine(Request("POST", "/v1/block", new SocialPayload {playerId=item.ownerId}, _=>{Entries=Entries.Where(e=>e.ownerId!=item.ownerId).ToArray();Status="已屏蔽这位玩家。";})); }
        public void SendLetter(SocialUser recipient)
        {
            if (Busy || !Registered) return;
            var artwork = workspace.Service.Document.personalStudio?.artworks?.Where(a=>a.kind=="sticker").ToArray();
            string png = artwork != null && artwork.Length > 0 ? artwork[Mathf.Clamp(GiftTemplateIndex,0,artwork.Length-1)].pngBase64 : null;
            StartCoroutine(Request("POST", "/v1/letters", new SocialPayload { title=Title,text=Text,pngBase64=png,recipientId=recipient.id,requestKey=Guid.NewGuid().ToString("N") }, _=>Status="信与自制贴纸已进入审核，审核通过后寄达。"));
        }
        public void Receive(SocialEntry item)
        {
            if (Busy) return;
            if (!workspace.Service.ClaimSocialGift(item)) { Status=workspace.Service.LastMessage;return; }
            StartCoroutine(Request("POST", "/v1/inbox/"+item.id+"/ack",new SocialPayload(),_=>{item.acked=true;Status="来信已存好；贴纸可在下方礼物库存取用。";}));
        }
        public void TakeGift(SocialGiftArtwork gift)
        {
            if (workspace.InteractionBusy) {Status="先放下手里的素材。";return;}
            var item=workspace.Service.Items.FirstOrDefault(i=>i.instanceId=="dream-social-"+gift.giftId&&i.owner==CraftOwner.Stock);
            if(item==null){Status="这份礼物已使用，可从页面揭取转移。";return;}
            if(workspace.Service.BeginStock(item.instanceId)){workspace.RefreshViews();Status="礼物已拿起，关闭手作中心后贴进页面。";}
        }
        public void PostCommission() { if(!Busy&&Registered) StartCoroutine(Request("POST","/v1/commissions",new SocialPayload{title=Title,text=Text,requestKey=Guid.NewGuid().ToString("N")},_=>Status="玩家委托已进入审核队列。")); }
        public void AcceptCommission(SocialEntry item) { if(!Busy)StartCoroutine(Request("POST","/v1/commissions/"+item.id+"/accept",new SocialPayload(),_=>{item.acceptedBy=Data.playerId;Status="已接下委托，完成后选择页面照片提交。";})); }
        public void Fulfill(SocialEntry item) { if(!Busy&&Capture(out string png))StartCoroutine(Request("POST","/v1/commissions/"+item.id+"/submit",new SocialPayload{title=Title,text=Text,pngBase64=png,requestKey=Guid.NewGuid().ToString("N")},_=>Status="作品已进入审核，审核后寄达委托人。")); }
        public void Moderate(SocialEntry item,bool approve) { if(!Busy)StartCoroutine(Request("POST","/v1/moderation/"+item.id,new SocialPayload{decision=approve?"approve":"reject"},_=>{Entries=Entries.Where(e=>e.id!=item.id).ToArray();Status="审核决定已保存。";},admin:true)); }
        public void Ban(SocialUser user) { if(!Busy)StartCoroutine(Request("POST","/v1/moderation/users/"+user.id,new SocialPayload{blocked=!user.banned},_=>{user.banned=!user.banned;Status="账号状态已更新。";},admin:true)); }
        public Texture2D Preview(SocialEntry entry)
        {
            if(string.IsNullOrEmpty(entry.pngBase64))return null;
            if(previews.TryGetValue(entry.id,out var image)&&image)return image;
            try{var bytes=Convert.FromBase64String(entry.pngBase64);if(bytes.Length<33||bytes.Length>600000||bytes[0]!=137||bytes[1]!=80)return null;
                uint width=(uint)(bytes[16]<<24|bytes[17]<<16|bytes[18]<<8|bytes[19]),height=(uint)(bytes[20]<<24|bytes[21]<<16|bytes[22]<<8|bytes[23]);
                if(width<1||height<1||width>1024||height>1024)return null;
                var tex=new Texture2D(2,2);if(!tex.LoadImage(bytes)){Destroy(tex);return null;}
                if(previews.Count>=48){var first=previews.First();if(first.Value)Destroy(first.Value);previews.Remove(first.Key);}
                previews[entry.id]=tex;return tex;}catch(Exception){return null;}
        }
        void OnDestroy(){foreach(var texture in previews.Values)if(texture)Destroy(texture);SocialMaterialAssets.Clear();}
    }
}

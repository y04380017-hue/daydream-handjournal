using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace VintageJournal
{
    /// <summary>Explicit isolated validation only. Uses real HTTP; never logs credentials or runs in normal play.</summary>
    public static class AtelierSocialProbeSteps
    {
        const string Server="http://127.0.0.1:18748";
        static Action<string,bool,string> report;
        public static IEnumerator Run(WorkspaceGameplayController workspace,string output,Action<string,bool,string> check)
        {
            report=check;
            var args=Environment.GetCommandLineArgs();
            Must(check,"HTTP probe explicitly isolated",args.Contains("--atelier-full-validation"),"--atelier-full-validation required");
            int arg=Array.IndexOf(args,"--atelier-http-fixture");
            Must(check,"HTTP fixture path supplied",arg>=0&&arg+1<args.Length,"Explicit test service fixture");
            string fixture=Path.GetFullPath(args[arg+1]);
            string keyFile=Path.Combine(fixture,"moderator-key.txt");
            Must(check,"Separate moderator key exists",File.Exists(keyFile),"Key file contents are never logged");
            string moderatorKey=File.ReadAllText(keyFile).Trim();
            Must(check,"Moderator key has content",moderatorKey.Length>=16,"Dedicated local fixture");

            var client=AtelierSocialController.Ensure(workspace);
            client.ServerUrl=Server;client.Nickname="联调云友A"+Guid.NewGuid().ToString("N").Substring(0,6);
            client.Register();yield return Wait(client,"register A");
            Must(check,"Unity HTTP registers local player A",client.Registered&&client.LastRequestSucceeded,"Identity saved through atomic journal queue");
            string firstIdentity=client.Data.playerId;
            var artwork=workspace.Service.Document.personalStudio?.artworks.FirstOrDefault(a=>a.kind=="sticker"&&a.width==256&&a.height==256);
            Must(check,"Publish uses a real saved 256px custom sticker",artwork!=null,"Private diary contents are not uploaded");
            string title="联调一页微光"+Guid.NewGuid().ToString("N").Substring(0,6);
            client.PublishPng(title,"给今天留一小片光",artwork.pngBase64,"http-probe-publish-"+Guid.NewGuid().ToString("N"));
            yield return Wait(client,"publish draft");
            Must(check,"Unity publication accepted for review",client.LastRequestSucceeded,"Explicit publish action");
            client.View=1;client.Refresh();yield return Wait(client,"read own drafts");
            var published=client.Entries.FirstOrDefault(e=>e.title==title);
            Must(check,"Own draft remains pending",published!=null&&published.status=="pending","Saved personal artwork enters moderation");
            client.View=0;client.Refresh();yield return Wait(client,"gallery before approval");
            Must(check,"Pending content is absent from public gallery",client.LastRequestSucceeded&&client.Entries.All(e=>e.id!=published.id),"No auto-publishing");

            client.AdminKey=moderatorKey;client.Moderate(published,true);yield return Wait(client,"approve page");
            Must(check,"Independent moderator approves through Unity HTTP",client.LastRequestSucceeded,"Moderator credentials remain in memory");
            client.View=0;client.Refresh();yield return Wait(client,"approved gallery");
            var shown=client.Entries.FirstOrDefault(e=>e.id==published.id);
            Must(check,"Approved gallery page appears",client.LastRequestSucceeded&&shown!=null&&shown.status=="approved","Real service response");
            client.Like(shown);yield return Wait(client,"first like");
            Must(check,"Unity like succeeds",client.LastRequestSucceeded,"One gentle reaction");
            client.Like(shown);yield return Wait(client,"repeat like");
            client.View=0;client.Refresh();yield return Wait(client,"read like count");
            shown=client.Entries.FirstOrDefault(e=>e.id==published.id);
            Must(check,"Repeated likes remain idempotent",shown!=null&&shown.likes==1&&shown.liked,"Server count remains one");

            SocialEnvelope second=null;
            yield return Raw("POST","/v1/register",new SocialPayload{name="联调云友B"+Guid.NewGuid().ToString("N").Substring(0,6)},null,null,r=>second=r);
            Must(check,"Second HTTP identity stays separate",second!=null&&!string.IsNullOrEmpty(second.token)&&second.id!=firstIdentity&&client.Data.playerId==firstIdentity,"Player A local identity was not replaced");
            SocialEnvelope invitation=null;
            yield return Raw("POST","/v1/friends",new SocialPayload{code=client.Data.friendCode},second.token,null,r=>invitation=r);
            client.View=2;client.Refresh();yield return Wait(client,"incoming friend request");
            var invite=client.Invitations.FirstOrDefault(i=>i.id==invitation.id);
            Must(check,"Unity receives B friend invitation",invite!=null&&invite.from!=null&&invite.from.id==second.id,"Acceptance still required");
            client.AcceptInvite(invite.id);yield return Wait(client,"accept friend");
            client.View=2;client.Refresh();yield return Wait(client,"confirmed friends");
            Must(check,"Friendship requires and records acceptance",client.LastRequestSucceeded&&client.Friends.Any(f=>f.id==second.id),"Two independent real HTTP clients");

            SocialEnvelope sentGift=null;
            yield return Raw("POST","/v1/letters",new SocialPayload{recipientId=firstIdentity,title="好友的小月亮",text="一枚我画的贴纸，送给你。",pngBase64=artwork.pngBase64,requestKey="http-probe-letter-"+Guid.NewGuid().ToString("N")},second.token,null,r=>sentGift=r);
            client.View=3;client.Refresh();yield return Wait(client,"inbox before moderation");
            Must(check,"Unreviewed friend gift is not delivered",client.LastRequestSucceeded&&client.Entries.All(e=>e.id!=sentGift.id),"Letter review is mandatory");
            client.Moderate(new SocialEntry{id=sentGift.id},true);yield return Wait(client,"approve gift");
            client.View=3;client.Refresh();yield return Wait(client,"approved gift inbox");
            var gift=client.Entries.FirstOrDefault(e=>e.id==sentGift.id);
            Must(check,"Approved friend letter reaches exact recipient",client.LastRequestSucceeded&&gift!=null&&gift.recipientId==firstIdentity&&gift.status=="approved","PNG attached to the reviewed letter");
            int before=workspace.Service.Items.Count;
            client.Receive(gift);yield return Wait(client,"receive gift");
            string instance="dream-social-"+gift.id;
            Must(check,"Receive creates one finite owned gift and receipt",client.LastRequestSucceeded&&workspace.Service.Document.atelierSocial.claimedGiftIds.Contains(gift.id)&&
                workspace.Service.Items.Count(i=>i.instanceId==instance)==1&&workspace.Service.Items.Count==before+1,"Atomic local receipt precedes HTTP acknowledgement");
            Must(check,"Gift ownership is stock and snapshot flushes",workspace.Service.GetItem(instance)?.owner==CraftOwner.Stock&&workspace.Service.Flush(),"Durable finite inventory");
            client.Receive(gift);yield return Wait(client,"repeat receive");
            Must(check,"Retry cannot create a duplicate gift",workspace.Service.Items.Count(i=>i.instanceId==instance)==1&&workspace.Service.Items.Count==before+1,"Idempotent local receipt and server acknowledgement");
            client.View=3;client.Refresh();yield return Wait(client,"acknowledged inbox");
            Must(check,"Server acknowledgement survives readback",client.Entries.Any(e=>e.id==gift.id&&e.acked),"Recipient-only ack is durable");
            var artGift=workspace.Service.Document.atelierSocial.gifts.First(g=>g.giftId==gift.id);
            client.TakeGift(artGift);
            Must(check,"Received sticker starts existing held transaction",workspace.Service.HeldItem?.instanceId==instance,"Same ownership registry as local craft");
            Must(check,"Cancel returns the same received sticker",workspace.Cancel()&&workspace.Service.GetItem(instance)?.owner==CraftOwner.Stock,"No duplicate or lost gift");

            // B posts a request. A responds with a real capture of an already crafted journal page.
            SocialEnvelope playerCommission=null;
            yield return Raw("POST","/v1/commissions",new SocialPayload{title="替星星留一页",text="想要一页会让人安静下来的光。",requestKey="http-probe-commission-"+Guid.NewGuid().ToString("N")},second.token,null,r=>playerCommission=r);
            client.Moderate(new SocialEntry{id=playerCommission.id},true);yield return Wait(client,"approve player commission");
            client.View=4;client.Refresh();yield return Wait(client,"player commission list");
            var request=client.Entries.FirstOrDefault(e=>e.id==playerCommission.id);
            Must(check,"Reviewed player-made request is visible",client.LastRequestSucceeded&&request!=null,"No unmoderated player request in gallery");
            client.AcceptCommission(request);yield return Wait(client,"accept player commission");
            Must(check,"Unity accepts another player's request",client.LastRequestSucceeded,"Fulfillment remains a photo submission");
            var left=workspace.journal.Document.GetPage(JournalSide.Left);
            var right=workspace.journal.Document.GetPage(JournalSide.Right);
            Must(check,"An actual crafted page is available for social response",left.decorations.Count>0||right.decorations.Count>0,"Existing authored page");
            client.ShareSide=left.decorations.Count>0?JournalSide.Left:JournalSide.Right;
            client.Title="给你的云上微光";client.Text="作品本身仍留在我的手账里。";
            int originalItems=workspace.Service.Items.Count;
            client.Fulfill(request);yield return Wait(client,"submit captured page");
            Must(check,"Unity submits actual page photo without changing ownership",client.LastRequestSucceeded&&workspace.Service.Items.Count==originalItems,"CapturePage and HTTP request both executed");
            client.View=1;client.Refresh();yield return Wait(client,"read fulfillment draft");
            var fulfillment=client.Entries.FirstOrDefault(e=>e.kind=="fulfillment"&&e.parentId==request.id);
            Must(check,"Photo fulfillment waits for review",fulfillment!=null&&fulfillment.status=="pending","Moderated UGC before recipient delivery");
            client.Moderate(fulfillment,true);yield return Wait(client,"approve fulfillment");
            SocialEnvelope secondInbox=null;
            yield return Raw("GET","/v1/inbox",null,second.token,null,r=>secondInbox=r);
            Must(check,"Reviewed page response reaches requester B",secondInbox.entries.Any(e=>e.id==fulfillment.id&&e.status=="approved"),"Two-client player commission round trip");

            client.AdminKey="";
            Must(check,"HTTP-created branches validate and flush",AtelierSocialData.Validate(workspace.Service.Document,out _)&&workspace.Service.Flush(),"No credentials are included in evidence files");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output,"http-client-result.txt"),DateTime.UtcNow.ToString("O")+"\nUnity real HTTP: PASS\nRegistration, moderated gallery, idempotent likes, accepted friendship, finite gift and reviewed player commission verified.\n");
        }
        static IEnumerator Wait(AtelierSocialController client,string action)
        {
            float deadline=Time.realtimeSinceStartup+15;
            while(client.Busy){if(Time.realtimeSinceStartup>deadline)Must(report,"HTTP client timeout: "+action,false,"15 seconds");yield return null;}
        }
        static IEnumerator Raw(string method,string endpoint,SocialPayload body,string token,string admin,Action<SocialEnvelope> complete)
        {
            using(var request=new UnityWebRequest(Server+endpoint,method))
            {
                request.downloadHandler=new DownloadHandlerBuffer();request.timeout=10;request.redirectLimit=0;
                if(body!=null){request.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(body)));request.SetRequestHeader("Content-Type","application/json");}
                if(!string.IsNullOrEmpty(token))request.SetRequestHeader("Authorization","Bearer "+token);
                if(!string.IsNullOrEmpty(admin))request.SetRequestHeader("X-Admin-Key",admin);
                var operation=request.SendWebRequest();float deadline=Time.realtimeSinceStartup+15;
                while(!operation.isDone){if(Time.realtimeSinceStartup>deadline)Must(report,"HTTP fixture timeout: "+endpoint,false,"15 seconds");yield return null;}
                if(request.result!=UnityWebRequest.Result.Success)Must(report,"HTTP fixture request: "+method+" "+endpoint,false,"status="+request.responseCode);
                var response=JsonUtility.FromJson<SocialEnvelope>(request.downloadHandler.text);
                if(response==null||!string.IsNullOrEmpty(response.error))Must(report,"HTTP fixture response: "+endpoint,false,"Invalid response envelope");
                complete(response);
            }
        }
        static void Must(Action<string,bool,string> check,string name,bool passed,string detail)
        {check(name,passed,detail);if(!passed)throw new InvalidOperationException(name+": "+detail);}
    }
}

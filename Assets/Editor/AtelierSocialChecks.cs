using System;
using System.Linq;
using UnityEngine;

namespace VintageJournal.Editor
{
    /// <summary>Ownership and failure seams; HTTP has a separate actual two-client Node test suite.</summary>
    public static class AtelierSocialChecks
    {
        static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);Debug.Log("[AtelierSocialData] PASS "+message);}
        public static bool RunDataChecks()
        {
            try
            {
                var old=JournalDocument.CreateDefault(JournalPaperCatalog.Load());
                Require(old.DeepClone().atelierSocial==null,"Legacy journal does not fabricate social identity");
                bool accept=true;
                using(var service=new CraftWorkspaceService(old,_=>accept,new Vector2(.148f,.210f),DecorationCatalog.Load()))
                {
                    Require(service.InitializeAtelierSocial(),"Optional local social branch initializes");
                    Require(!service.ConfigureSocial("file:///C:/","x","a","b","c"),"Reject local-file endpoint");
                    Require(!service.ConfigureSocial("http://example.invalid","x","a","b","c"),"Reject public clear-text endpoint");
                    Require(!AtelierSocialData.SameOrigin("http://127.0.0.1:18747","http://127.0.0.1:18748")&&AtelierSocialData.SameOrigin("http://localhost:18747/","http://LOCALHOST:18747"),"Credentials remain bound to one exact connected origin");
                    Require(service.ConfigureSocial("http://127.0.0.1:18747","测试朋友","receiver","CODE","test-token"),"Anonymous identity saves atomically");
                    var texture=new Texture2D(8,8,TextureFormat.RGBA32,false);var pixels=Enumerable.Repeat(new Color(.6f,.8f,1f,.8f),64).ToArray();texture.SetPixels(pixels);texture.Apply();
                    string png=Convert.ToBase64String(texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);
                    var gift=new SocialEntry{id="test-gift-1",recipientId="receiver",kind="letter",title="一枚自制贴纸",pngBase64=png,status="approved"};
                    accept=false;
                    Require(!service.ClaimSocialGift(gift)&&service.Document.atelierSocial.claimedGiftIds.Count==0,"Rejected save never acknowledges or creates gift");
                    accept=true;gift.status="pending";
                    Require(!service.ClaimSocialGift(gift),"Pending content cannot enter inventory");
                    gift.status="approved";gift.recipientId="other";
                    Require(!service.ClaimSocialGift(gift),"Other recipient cannot claim gift");
                    gift.recipientId="receiver";
                    Require(service.ClaimSocialGift(gift),"Approved gift commits receipt and finite stock together");
                    string id="dream-social-test-gift-1";
                    Require(service.GetItem(id)?.owner==CraftOwner.Stock&&service.Document.atelierSocial.gifts.Count==1,"One unique material has one stock owner");
                    int items=service.Items.Count;
                    Require(service.ClaimSocialGift(gift)&&service.Items.Count==items,"Retry before HTTP acknowledgment does not clone gift");
                    Require(service.BeginStock(id)&&service.CancelHeld()&&service.GetItem(id).owner==CraftOwner.Stock,"Cancel returns the same gift to inventory");
                    Require(service.BeginStock(id)&&service.CommitHeld(new CraftPlacement{owner=CraftOwner.JournalPage,containerId="journal",pageId=service.Document.pages[0].pageId,position=new Vector2(.5f,.5f),scale=1}),"Received art uses the existing page transfer transaction");
                    Require(service.DeleteAttached(id),"Delete gift retains consumed instance");
                    using(var restarted=new CraftWorkspaceService(service.Document.DeepClone(),_=>true,new Vector2(.148f,.210f),DecorationCatalog.Load()))
                    {
                        Require(restarted.GetItem(id).owner==CraftOwner.Deleted&&restarted.ClaimSocialGift(gift)&&restarted.Items.Count(i=>i.instanceId==id)==1,"Restart and retry cannot regenerate deleted gift");
                        Require(AtelierSocialData.Validate(restarted.Document,out _),"JSON roundtrip preserves receipt and tombstone consistency");
                    }
                    var claimedElsewhere=new SocialEntry{id="elsewhere",recipientId="receiver",kind="letter",status="approved",acked=true};
                    Require(!service.ClaimSocialGift(claimedElsewhere),"Server-acknowledged gift is not regenerated in another save");
                }
                Debug.Log("[AtelierSocialData] PASS all finite receipt and legacy checks; HTTP/native UI are separate evidence");return true;
            }
            catch(Exception error){Debug.LogError("[AtelierSocialData] FAIL "+error);return false;}
        }
    }
}

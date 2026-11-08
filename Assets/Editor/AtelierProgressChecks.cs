using System;
using System.Linq;
using UnityEngine;

namespace VintageJournal.Editor
{
    public static class AtelierProgressChecks
    {
        public static bool RunDataChecks()
        {
            try
            {
                var doc=JournalDocument.CreateDefault(JournalPaperCatalog.Load());bool accepts=true;
                var service=new CraftWorkspaceService(doc,_=>accepts,new Vector2(.148f,.210f),DecorationCatalog.Load());
                DateTime now=new DateTime(2026,11,7,8,0,0,DateTimeKind.Utc);
                Require(service.InitializeDreamGameplay(now.Ticks),"Legacy night initialization");
                string[] oldIds=service.Document.dreamGameplay.pending.Select(t=>t.tokenId).ToArray();
                Require(service.RefreshSettingCollection(1,now.Ticks)&&service.RefreshSettingCollection(2,now.Ticks),"Initialize independent daytime and afternoon queues");
                Require(service.Document.dreamGameplay.pending.Count==9&&Enumerable.Range(0,3).All(s=>service.Document.dreamGameplay.pending.Count(t=>t.setting==s)==3),"Three starters in each independent setting");
                service.RefreshSettingCollection(1,now.Ticks+TimeSpan.TicksPerHour);
                Require(service.Document.dreamGameplay.pending.Count==9,"Switching does not refresh before wall-clock interval");
                service.RefreshSettingCollection(1,now.Ticks+TimeSpan.TicksPerDay);
                Require(service.Document.dreamGameplay.pending.Count(t=>t.setting==1)==6&&service.Document.dreamGameplay.pending.Count(t=>t.setting==0)==3&&oldIds.All(id=>service.Document.dreamGameplay.pending.Any(t=>t.tokenId==id)),"Offline cap is independent and preserves other setting IDs");
                var content=CommissionCatalog.Load();
                Require(content.nightClients.Length==2&&content.dayClients.Length==2&&content.afternoonClients.Length==2,"Six recurring clients across three settings");
                foreach(int setting in new[]{0,1,2})
                {
                    var history=new CommissionDeskData();
                    foreach(int sequence in Enumerable.Range(0,16))
                    {var letter=content.CreateSettingLetter(history,sequence,setting);Require(letter!=null&&letter.setting==setting&&letter.wanted.Length>=2&&letter.wanted.Length<=4,"Setting chapter "+setting+"/"+sequence);letter.status=CommissionLetterStatus.Replied;history.letters.Add(letter);history.nextSequence++;}
                    Require(history.letters.Select(l=>l.clientId).Distinct().Count()==2&&history.letters.All(l=>!l.afterStory),"Each of two clients delivers all eight chapters in order");
                    var gated=new CommissionDeskData();
                    var first=content.CreateSettingLetter(gated,0,setting,1);
                    Require(first!=null&&first.clientId==content.ClientsFor(setting)[0].id,"Reputation one opens first client in setting "+setting);
                    gated.letters.Add(first);gated.nextSequence++;
                    Require(content.CreateSettingLetter(gated,1,setting,1)==null,"Reputation one does not open a new second client while first waits");
                    Require(content.CreateSettingLetter(gated,1,setting)?.clientId==content.ClientsFor(setting)[1].id,"Default factory preserves legacy ungated correspondence");
                    first.status=CommissionLetterStatus.Replied;
                    for(int done=1;done<3;done++)
                    {var continuing=content.CreateSettingLetter(gated,done,setting,1);Require(continuing!=null&&continuing.clientId==first.clientId,"First client continues before reputation two");continuing.status=CommissionLetterStatus.Replied;gated.letters.Add(continuing);gated.nextSequence++;}
                    int reputation=1+gated.letters.Count(l=>l.status==CommissionLetterStatus.Replied)/3;
                    Require(reputation==2&&content.CreateSettingLetter(gated,3,setting,reputation)?.clientId==content.ClientsFor(setting)[1].id,"Three replies unlock second client at reputation two");
                    var reached=gated.Clone();
                    var progressed=new CommissionDeskService(content,()=>reached,(mutate,label)=>{mutate(reached);return true;},(owner,container,page)=>Array.Empty<CraftItemRecord>(),true){ContentSetting=setting,ProgressionEnabled=true};
                    Require(progressed.RequestLetterNow()&&reached.letters.Last().clientId==content.ClientsFor(setting)[1].id,"Live service derives reputation two from all three replied letters");
                    var oldSave=new CommissionDeskData();
                    var met=content.CreateSettingLetter(gated,4,setting,2);met.clientId=content.ClientsFor(setting)[1].id;met.status=CommissionLetterStatus.Replied;
                    oldSave.letters.Add(met);oldSave.nextSequence=1;
                    Require(content.CreateSettingLetter(oldSave,5,setting,1)?.clientId==met.clientId,"Previously met client survives low-reputation old save");
                    var live=new CommissionDeskData();
                    var policy=new CommissionDeskService(content,()=>live,(mutate,label)=>{mutate(live);return true;},(owner,container,page)=>Array.Empty<CraftItemRecord>(),true){ContentSetting=setting,ProgressionEnabled=true};
                    Require(policy.RequestLetterNow()&&!policy.RequestLetterNow(),"Service enforces first-client reputation policy");
                    policy.ProgressionEnabled=false;
                    Require(policy.RequestLetterNow(),"Service explicit opt-out preserves legacy behavior");
                }
                foreach(string asset in NightMaterialCatalog.AllAssetIds)
                {
                    Require(NightMaterialCatalog.TagsFor(asset).Length>=1&&NightMaterialCatalog.TagsFor(asset).Length<=3,"Mood count: "+asset);
                    var art=NightMaterialCatalog.Find(asset);Require(art.IsAvailable&&art.alphaMask.Any(v=>v==0)&&art.alphaMask.Any(v=>v==255),"Transparent collected art: "+asset);
                }
                Require(service.InitializeAtelierProgress(now.Ticks),"Initialize daily rhythm");
                Require(service.ClaimDailyCommission(now)&&!service.ClaimDailyCommission(now),"One daily letter per date");
                Require(service.ClaimSeasonalEvent(now)&&!service.ClaimSeasonalEvent(now),"Seasonal material and guest are idempotent");
                int before=service.Items.Count;
                Require(service.TickAtelierProgress(now.AddDays(1)),"Welcome back after a day away");
                Require(service.Items.Count==before+1&&service.TickAtelierProgress(now.AddDays(1))&&service.Items.Count==before+1,"Welcome gift does not duplicate");
                Require(service.TickAtelierProgress(now.AddDays(-1))&&service.Document.atelierProgress.lastVisitDate=="2026-11-08","Clock reversal does not rewind the last visit");
                Require(service.RevealDreamEvent(now,0)&&!service.RevealDreamEvent(now,0),"Weekly door event creates one real pending token");
                for(int i=0;i<2;i++)
                {
                    int sequence=i;
                    Require(service.CommitCommission(d=>{
                        var l=new CommissionLetterState{letterId="progress-check-"+sequence,sequence=sequence,clientId="lighthouse",storyChapter=sequence+1,storyLength=8,
                            favoriteAssetId="night-lantern-paper",wanted=new[]{"温暖","思念"},status=CommissionLetterStatus.Replied,
                            sentPageId=service.Document.pages[0].pageId,sentSignature="progress-check-signature-"+sequence,itemCount=1,tier=3,stamps=3,
                            coins=24,rewardAssetId="night-lantern-paper",rewardCount=2,rareRewardAssetId="night-forgotten-dream",rareRewardCount=1};
                        d.letters.Add(l);d.stamps+=3;
                    },"测试完整回礼"),"Earn coins through an actual replied-letter transaction");
                }
                Require(service.Document.dreamGameplay.coins==48,"Economy receipt sum");
                accepts=false;Require(!service.PurchaseUpgrade("desk-lamp")&&service.Document.dreamGameplay.coins==48,"Rejected save never spends coins");accepts=true;
                Require(service.PurchaseUpgrade("desk-lamp")&&service.Document.dreamGameplay.coins==38&&!service.PurchaseUpgrade("desk-lamp"),"Purchase spends once and prevents duplicates");
                Require(DreamGameplayData.Validate(service.Document,out _)&&AtelierProgressData.Validate(service.Document,out _),"Economy balance equals earned minus purchase ledger");
                var copy=service.Document.DeepClone();copy.dreamGameplay.coins++;
                Require(!DreamGameplayData.Validate(copy,out _),"Reject corrupted coin balance");
                Require(AtelierProgressCatalog.ToolUnlocked(service.Document,"sticker-maker"),"Reply progression unlocks sticker maker");
                Require(AtelierProgressCatalog.SeasonalAsset(new DateTime(2026,4,5))=="season-sakura"&&AtelierProgressCatalog.SeasonalAsset(new DateTime(2026,12,1))=="season-snowflake","Seasonal calendar resolves spring and winter");
                service.Dispose();Debug.Log("[AtelierProgress] PASS all three-setting, daily, seasonal and economy checks");return true;
            }
            catch(Exception error){Debug.LogError("[AtelierProgress] FAIL "+error);return false;}
        }
        static void Require(bool condition,string name){if(!condition)throw new InvalidOperationException(name);Debug.Log("[AtelierProgress] PASS "+name);}
    }
}

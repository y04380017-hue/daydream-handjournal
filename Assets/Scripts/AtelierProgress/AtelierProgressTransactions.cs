using System;
using System.Linq;

namespace VintageJournal
{
    public sealed partial class CraftWorkspaceService
    {
        public bool InitializeAtelierProgress(long utcTicks)=>TickAtelierProgress(new DateTime(utcTicks,DateTimeKind.Utc).ToLocalTime());
        public bool TickAtelierProgress(DateTime now)
        {
            string date=AtelierProgressCatalog.DateId(now);var old=Document.atelierProgress;
            if(old!=null&&old.initialized&&old.lastVisitDate==date)return true;
            var candidate=Document.DeepClone();candidate.atelierProgress??=new AtelierProgressData();candidate.atelierProgressEnabled=true;
            var data=candidate.atelierProgress;
            bool returning=data.initialized&&!string.IsNullOrEmpty(data.lastVisitDate)&&string.CompareOrdinal(date,data.lastVisitDate)>0;
            if(!data.visitDates.Contains(date))data.visitDates.Add(date);
            if(returning&&!data.welcomeClaims.Contains(date))
            {
                AddDreamMaterial(candidate,"day-lined-paper","dream-welcome-"+date);data.welcomeClaims.Add(date);
                data.welcomeNote="你不在的时候，一张纸替你接住了风。没有错过的任务，只有一份小礼在等你。";
            }
            else if(!data.initialized)data.welcomeNote="欢迎来到云上。所有来信都可以慢慢读，不必连续登录。";
            if(string.IsNullOrEmpty(data.lastVisitDate)||string.CompareOrdinal(date,data.lastVisitDate)>=0)data.lastVisitDate=date;
            data.initialized=true;return Commit(candidate,"记录今日工房",false);
        }
        public bool PurchaseUpgrade(string id)
        {
            int price=AtelierProgressCatalog.Price(id);
            if(price<0||Document.dreamGameplay==null)return Fail("未找到这件工房小物。");
            if(Document.atelierProgress?.purchases.Any(p=>p.id==id)==true)return Fail("这件小物已经在工房里。");
            if(Document.dreamGameplay.coins<price)return Fail("梦币还不够，先回复几封信吧。");
            var candidate=Document.DeepClone();candidate.atelierProgress??=new AtelierProgressData();candidate.atelierProgressEnabled=true;
            candidate.atelierProgress.purchases.Add(new AtelierPurchase{id=id,price=price,purchasedUtcTicks=DateTime.UtcNow.Ticks});
            candidate.dreamGameplay.coins-=price;return Commit(candidate,"添置"+AtelierProgressCatalog.UpgradeName(id),false);
        }
        public bool ClaimDailyCommission(DateTime now)
        {
            string date=AtelierProgressCatalog.DateId(now),id="daily-"+date;
            if(Document.atelierProgress?.dailyLetters.Contains(date)==true)return Fail("今天的来信已经放进邮筒了。");
            var candidate=Document.DeepClone();candidate.atelierProgress??=new AtelierProgressData();candidate.atelierProgressEnabled=true;
            candidate.commissionDesk??=new CommissionDeskData();candidate.commissionDeskEnabled=true;
            if(!candidate.commissionDesk.letters.Any(l=>l.letterId==id))candidate.commissionDesk.letters.Add(SpecialLetter(id,"daily","今日的邮差",AtelierProgressCatalog.TodayPrompt(now),candidate.commissionDesk.nextSequence++));
            candidate.atelierProgress.dailyLetters.Add(date);
            if(!CommissionDeskData.Validate(candidate,out string error))return Fail(error);
            return Commit(candidate,"收到每日来信",false);
        }
        public bool ClaimSeasonalEvent(DateTime now)
        {
            string asset=AtelierProgressCatalog.SeasonalAsset(now),key=now.Year+"-"+asset;
            if(Document.atelierProgress?.seasonalClaims.Contains(key)==true)return Fail("这一季的小礼已经收好了，明年还有新的相遇。");
            var candidate=Document.DeepClone();candidate.atelierProgress??=new AtelierProgressData();candidate.atelierProgressEnabled=true;
            AddDreamMaterial(candidate,asset,"dream-season-"+key);candidate.atelierProgress.seasonalClaims.Add(key);
            candidate.commissionDesk??=new CommissionDeskData();candidate.commissionDeskEnabled=true;
            var letter=SpecialLetter("event-"+key,"season-guest","季节旅人","请替我收藏「"+NightMaterialCatalog.NameFor(asset)+"」的这一瞬。",candidate.commissionDesk.nextSequence++);
            letter.favoriteAssetId=asset;letter.favoriteMaterialName=NightMaterialCatalog.NameFor(asset);letter.wanted=NightMaterialCatalog.TagsFor(asset).Take(2).ToArray();
            candidate.commissionDesk.letters.Add(letter);
            if(!CommissionDeskData.Validate(candidate,out string error))return Fail(error);
            return Commit(candidate,"收到节庆客人与小礼",false);
        }
        public bool RevealDreamEvent(DateTime now,int setting)
        {
            if(setting<0||setting>2||Document.dreamGameplay==null)return false;
            var calendar=System.Globalization.CultureInfo.InvariantCulture.Calendar;
            string key=now.Year+"-week-"+calendar.GetWeekOfYear(now,System.Globalization.CalendarWeekRule.FirstFourDayWeek,DayOfWeek.Monday);
            if(Document.atelierProgress?.dreamEventClaims?.Contains(key)==true)return Fail("这周的梦已经替你留好了，下周还有新的门。");
            if(Document.dreamGameplay.pending.Count(t=>t.setting==setting)>=6)return Fail("先收好窗外的一份素材，给梦留一点位置。");
            var candidate=Document.DeepClone();candidate.atelierProgress??=new AtelierProgressData();candidate.atelierProgressEnabled=true;
            candidate.atelierProgress.dreamEventClaims??=new System.Collections.Generic.List<string>();
            candidate.atelierProgress.dreamEventClaims.Add(key);
            candidate.dreamGameplay.pending.Add(new DreamCatchToken{tokenId="dream-event-"+key,assetId="night-forgotten-dream",setting=setting,visualKind=now.Day%2==0?4:5,rarity=2});
            return Commit(candidate,"一扇门在窗外等你",false);
        }
        static CommissionLetterState SpecialLetter(string id,string client,string sender,string brief,int sequence)=>new CommissionLetterState{
            letterId=id,clientId=client,sender=sender,title="给今天留一页",body=brief+"\n不用担心有没有做对，你愿意留下的样子，就是今天的答案。",sequence=sequence,
            storyChapter=1,storyLength=8,wanted=new[]{"梦幻","温暖"},favoriteAssetId="night-glow-ink",favoriteMaterialName="萤光墨印",minItems=1,status=CommissionLetterStatus.Unread};
    }
}

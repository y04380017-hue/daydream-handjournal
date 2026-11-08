using System;
using System.Collections.Generic;
using UnityEngine;

namespace VintageJournal
{
    [Serializable] public sealed class AtelierSessionRecord { public string id, dateKey; public long utcTicks; }
    [Serializable] public sealed class AtelierShareRecord { public string id, pageId, dateKey; public long utcTicks; public bool isYearReview; }
    [Serializable] public sealed class AtelierPurchase { public string id; public int price; public long purchasedUtcTicks; }
    [Serializable] public sealed class AtelierProgressData
    {
        public int schemaVersion=1;
        public bool initialized;
        public string lastVisitDate, welcomeNote;
        public List<string> visitDates=new List<string>(), dailyLetters=new List<string>(), welcomeClaims=new List<string>(), seasonalClaims=new List<string>();
        public List<AtelierPurchase> purchases=new List<AtelierPurchase>();
        public List<string> dreamEventClaims=new List<string>();
        public List<AtelierSessionRecord> sessions=new List<AtelierSessionRecord>();
        public List<AtelierShareRecord> shares=new List<AtelierShareRecord>();
        public AtelierProgressData Clone()=>JsonUtility.FromJson<AtelierProgressData>(JsonUtility.ToJson(this));
        public static bool Validate(JournalDocument document,out string message)
        {
            message="";var data=document?.atelierProgress;if(data==null)return true;
            if(data.schemaVersion!=1||data.visitDates==null||data.dailyLetters==null||data.welcomeClaims==null||data.seasonalClaims==null||data.purchases==null)
            {message="工房成长分支无效。";return false;}
            if(data.visitDates.Count>5000||data.dailyLetters.Count>5000||data.welcomeClaims.Count>5000||data.seasonalClaims.Count>5000||data.purchases.Count>32)
            {message="工房记录超过容量。";return false;}
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var purchase in data.purchases)
                if(purchase==null||!AtelierProgressCatalog.IsUpgrade(purchase.id)||!ids.Add(purchase.id)||purchase.price!=AtelierProgressCatalog.Price(purchase.id)||purchase.purchasedUtcTicks<0||purchase.purchasedUtcTicks>DateTime.MaxValue.Ticks)
                {message="工房购买账本无效。";return false;}
            foreach(var list in new[]{data.visitDates,data.dailyLetters,data.welcomeClaims,data.seasonalClaims,data.dreamEventClaims??new List<string>()})
            {ids.Clear();foreach(var id in list)if(string.IsNullOrEmpty(id)||!ids.Add(id)){message="每日或节庆记录重复。";return false;}}
            if((data.sessions?.Count??0)>5000||(data.shares?.Count??0)>3000){message="本地使用记录已达容量上限。";return false;}
            ids.Clear();foreach(var session in data.sessions??new List<AtelierSessionRecord>())
                if(session==null||string.IsNullOrEmpty(session.id)||!ids.Add(session.id)||session.utcTicks<0||session.utcTicks>DateTime.MaxValue.Ticks)
                {message="本地会话记录无效。";return false;}
            ids.Clear();foreach(var share in data.shares??new List<AtelierShareRecord>())
                if(share==null||string.IsNullOrEmpty(share.id)||!ids.Add(share.id)||string.IsNullOrEmpty(share.pageId)||share.utcTicks<0||share.utcTicks>DateTime.MaxValue.Ticks)
                {message="本地导出记录无效。";return false;}
            return true;
        }
    }
}

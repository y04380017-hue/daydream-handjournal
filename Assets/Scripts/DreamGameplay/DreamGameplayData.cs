using System;
using System.Collections.Generic;
using UnityEngine;

namespace VintageJournal
{
    [Serializable] public sealed class DreamCatchToken
    {
        public string tokenId, assetId;
        public int visualKind, rarity, setting;
    }
    [Serializable] public sealed class DreamCollectionClock { public int setting; public long nextRefreshUtcTicks; }

    /// <summary>Small optional ledger. Actual finite materials live in the existing ownership registry.</summary>
    [Serializable] public sealed class DreamGameplayData
    {
        public const int CurrentSchemaVersion = 1;
        public int schemaVersion = CurrentSchemaVersion;
        public bool initialized;
        public long nextRefreshUtcTicks;
        public int randomState = 1374496523, coins;
        public List<DreamCatchToken> pending = new List<DreamCatchToken>();
        public List<string> claimedRewardLetters = new List<string>();
        public List<DreamCollectionClock> settingClocks = new List<DreamCollectionClock>();
        public DreamGameplayData Clone() => JsonUtility.FromJson<DreamGameplayData>(JsonUtility.ToJson(this));

        public static bool Validate(JournalDocument document, out string message)
        {
            message = string.Empty;
            var data = document?.dreamGameplay;
            if (data == null) return true;
            if (data.schemaVersion != CurrentSchemaVersion || data.coins < 0 || data.nextRefreshUtcTicks < 0 ||
                data.nextRefreshUtcTicks > DateTime.MaxValue.Ticks || data.pending == null || data.pending.Count > 64 ||
                data.claimedRewardLetters == null || data.claimedRewardLetters.Count > 5000)
            { message = "夜间玩法版本、库存或时间无效，保留原存档。"; return false; }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var token in data.pending)
                if (token == null || string.IsNullOrEmpty(token.tokenId) || !ids.Add(token.tokenId) ||
                    !NightMaterialCatalog.IsKnown(token.assetId) || token.setting < 0 || token.setting > 2 || token.visualKind < 0 || token.visualKind > 5 || token.rarity < 0 || token.rarity > 2)
                { message = "漂浮素材记录无效或重复。"; return false; }
            ids.Clear();
            long earnedCoins = 0;
            foreach (var id in data.claimedRewardLetters)
            {
                if (string.IsNullOrEmpty(id) || !ids.Add(id) || document.commissionDesk?.letters == null ||
                    !document.commissionDesk.letters.Exists(l => l.letterId == id && l.status == CommissionLetterStatus.Replied))
                { message = "夜间回礼账本与信件不一致。"; return false; }
                var letter = document.commissionDesk.letters.Find(l => l.letterId == id);
                earnedCoins += letter.coins;
                for (int i = 0; i < letter.rewardCount; i++)
                    if (!HasReward(document, "dream-reward-" + id + "-" + i, letter.rewardAssetId))
                    { message = "回信的素材回礼缺失。"; return false; }
                for (int i = 0; i < letter.rareRewardCount; i++)
                    if (!HasReward(document, "dream-gift-" + id + "-" + i, letter.rareRewardAssetId))
                    { message = "回信的稀有回礼缺失。"; return false; }
            }
            long spent = 0;
            if(document.atelierProgress?.purchases != null) foreach(var purchase in document.atelierProgress.purchases) spent += purchase.price;
            if (earnedCoins - spent != data.coins) { message = "梦币总数与回信/购买账本不一致。"; return false; }
            var settings = new HashSet<int>();
            if(data.settingClocks != null) foreach(var clock in data.settingClocks)
                if(clock == null || clock.setting < 0 || clock.setting > 2 || !settings.Add(clock.setting) || clock.nextRefreshUtcTicks < 0 || clock.nextRefreshUtcTicks > DateTime.MaxValue.Ticks)
                {message="时段补充时钟无效。";return false;}
            return true;
        }
        static bool HasReward(JournalDocument document, string id, string assetId) =>
            NightMaterialCatalog.IsKnown(assetId) && document.craftWorkspace?.items != null &&
            document.craftWorkspace.items.Exists(i => i.instanceId == id && i.assetId == assetId);
    }
}

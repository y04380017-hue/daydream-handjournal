using System;
using System.Linq;
using UnityEngine;

namespace VintageJournal
{
    public sealed partial class CraftWorkspaceService
    {
        public bool InitializeDreamGameplay(long utcTicks) => RefreshNightCollection(utcTicks);

        public bool RefreshNightCollection(long utcTicks, int cap = 6, int batch = 3, double refreshHours = 2)
        { return RefreshSettingCollection(0, utcTicks, cap, batch, refreshHours); }

        public bool RefreshSettingCollection(int setting, long utcTicks, int cap = 6, int batch = 3, double refreshHours = 2)
        {
            if (setting < 0 || setting > 2 || utcTicks < 0 || utcTicks > DateTime.MaxValue.Ticks || double.IsNaN(refreshHours) || double.IsInfinity(refreshHours)) return false;
            cap = Mathf.Clamp(cap, 1, 64); batch = Mathf.Clamp(batch, 1, cap);
            long interval = TimeSpan.FromHours(Math.Max(.01, Math.Min(720, refreshHours))).Ticks;
            var old = Document.dreamGameplay;
            var oldClock = old?.settingClocks?.FirstOrDefault(c=>c.setting==setting);
            long oldNext = oldClock != null ? oldClock.nextRefreshUtcTicks : setting == 0 && old != null && old.initialized ? old.nextRefreshUtcTicks : 0;
            if (oldNext > 0 && utcTicks < oldNext) return true;
            var candidate = Document.DeepClone();
            candidate.dreamGameplay ??= new DreamGameplayData(); candidate.dreamGameplayEnabled = true;
            var data = candidate.dreamGameplay;
            long periods = oldNext > 0 ? 1 + Math.Max(0, utcTicks - oldNext) / interval : 1;
            int settingCount = data.pending.Count(t=>t.setting==setting);
            int add = (int)Math.Min(Math.Max(0, cap - settingCount), Math.Min(cap, periods) * batch);
            for (int i = 0; i < add; i++)
            {
                int roll = NextNightRandom(data) % 100;
                int kind = (settingCount+i) % NightMaterialCatalog.ForSetting(setting).Length;
                int rarity = roll == 0 ? 2 : roll <= 10 ? 1 : 0;
                string asset = rarity == 2 ? "night-forgotten-dream" : rarity == 1 ? setting==0?"night-moonlit-star":setting==1?"day-pearl-paper":"afternoon-sunset-foil" : NightMaterialCatalog.ForSetting(setting)[kind];
                data.pending.Add(new DreamCatchToken { tokenId = "night-catch-" + Guid.NewGuid().ToString("N"), assetId = asset, setting=setting, visualKind = rarity==2?4:kind, rarity = rarity });
            }
            data.settingClocks ??= new System.Collections.Generic.List<DreamCollectionClock>();
            var clock = data.settingClocks.FirstOrDefault(c=>c.setting==setting);
            if(clock==null){clock=new DreamCollectionClock{setting=setting};data.settingClocks.Add(clock);}
            clock.nextRefreshUtcTicks = utcTicks > DateTime.MaxValue.Ticks - interval ? DateTime.MaxValue.Ticks : utcTicks + interval;
            if(setting==0)data.nextRefreshUtcTicks=clock.nextRefreshUtcTicks;
            data.initialized = true;
            return Commit(candidate, "补充" + AtelierProgressCatalog.SettingName(setting) + "漂浮素材", false);
        }

        public bool CatchNightMaterial(string tokenId)
        {
            if (Document.dreamGameplay?.pending.Find(t => t.tokenId == tokenId) == null) return Fail("这份素材已经收好了。");
            var candidate = Document.DeepClone(); var data = candidate.dreamGameplay;
            var token = data.pending.Find(t => t.tokenId == tokenId);
            data.pending.Remove(token);
            AddDreamMaterial(candidate, token.assetId, "dream-catch-" + token.tokenId);
            return Commit(candidate, "收集" + NightMaterialCatalog.NameFor(token.assetId), false);
        }

        static int NextNightRandom(DreamGameplayData data)
        {
            uint value = unchecked((uint)data.randomState); if (value == 0) value = 1374496523;
            value ^= value << 13; value ^= value >> 17; value ^= value << 5;
            data.randomState = unchecked((int)value); return (int)(value & 0x7fffffff);
        }
        static bool IsDreamInstance(CraftItemRecord item) => item.instanceId != null && item.instanceId.StartsWith("dream-", StringComparison.Ordinal);
        static void AddDreamMaterial(JournalDocument document, string assetId, string id)
        {
            if (document.craftWorkspace.items.Any(i => i.instanceId == id)) return;
            bool paper = NightMaterialCatalog.IsPaper(assetId);
            bool tape = assetId == "day-cloud-washi";
            document.craftWorkspace.items.Add(new CraftItemRecord { instanceId = id, assetId = assetId,
                kind = tape?CraftItemKind.TapeStrip:paper ? CraftItemKind.Note : CraftItemKind.Sticker, owner = CraftOwner.Stock,
                containerId = "dream-stock:" + id, sizeMeters = tape?new Vector2(.05f,.015f):paper ? new Vector2(.045f, .06f) : new Vector2(.035f, .035f),
                lengthMeters=tape?.05f:0,widthMeters=tape?.015f:0 });
        }
        static void ApplyNightRewards(JournalDocument candidate)
        {
            foreach (var letter in candidate.commissionDesk.letters.Where(l => l.status == CommissionLetterStatus.Replied && !string.IsNullOrEmpty(l.clientId)))
            {
                candidate.dreamGameplay ??= new DreamGameplayData(); candidate.dreamGameplayEnabled = true;
                var data = candidate.dreamGameplay;
                if (data.claimedRewardLetters.Contains(letter.letterId)) continue;
                data.coins = checked(data.coins + letter.coins);
                for (int i = 0; i < letter.rewardCount; i++)
                    AddDreamMaterial(candidate, letter.rewardAssetId, "dream-reward-" + letter.letterId + "-" + i);
                for (int i = 0; i < letter.rareRewardCount; i++)
                    AddDreamMaterial(candidate, letter.rareRewardAssetId, "dream-gift-" + letter.letterId + "-" + i);
                data.claimedRewardLetters.Add(letter.letterId);
            }
        }
    }
}

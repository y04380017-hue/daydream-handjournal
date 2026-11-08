using System;
using System.Collections.Generic;
using UnityEngine;

namespace VintageJournal
{
    public enum CommissionLetterStatus { Unread, Read, Sent, Replied }

    /// <summary>One commission letter. Text is snapshotted so later catalogue edits never rewrite old saves.</summary>
    [Serializable]
    public sealed class CommissionLetterState
    {
        public string letterId;
        public int sequence;
        public string sender, title, body;
        public string[] wanted = Array.Empty<string>();
        public int minItems;
        public CommissionLetterStatus status;
        public double arrivedAt, sentAt, replyDueAt;
        // Sent page reference. The page and its materials stay with the player; only a photo travels.
        public CraftOwner sentOwner;
        public string sentContainerId, sentPageId, sentSignature, photoFile;
        public string[] matched = Array.Empty<string>();
        public string[] sentAssets = Array.Empty<string>();
        public int itemCount, tier, stamps;
        public string replyFull, replyPartial, replyPlain, replyText;
        // Additive night-slice fields. Empty clientId identifies a legacy letter, whose old rules remain intact.
        public string clientId, favoriteAssetId, favoriteMaterialName;
        public int storyChapter, storyLength;
        public bool afterStory;
        public string twistKind, twistBrief, feedbackDetail;
        public int moodScore, balanceBonus, favoriteBonus;
        // Rewards are snapshotted on send and redeemed atomically when the reply changes to Replied.
        public int coins, rewardCount, rareRewardCount;
        public int setting;
        public long sentUtcTicks;
        public string rewardAssetId, rareRewardAssetId;
        public bool IsOpen => status == CommissionLetterStatus.Unread || status == CommissionLetterStatus.Read;
    }

    /// <summary>Additive, independently versioned branch inside the atomic journal-v1 snapshot.</summary>
    [Serializable]
    public sealed class CommissionDeskData
    {
        public const int CurrentSchemaVersion = 1;
        public int schemaVersion = CurrentSchemaVersion;
        public double clockSeconds, nextLetterAt = 2;
        public int nextSequence, stamps;
        public List<CommissionLetterState> letters = new List<CommissionLetterState>();

        public static CommissionDeskData CreateDefault() => new CommissionDeskData();

        public static bool Validate(JournalDocument document, out string message)
        {
            message = string.Empty;
            var data = document?.commissionDesk;
            if (data == null) return true; // Saves written before the commission prototype remain valid.
            if (data.schemaVersion != CurrentSchemaVersion || data.letters == null) return Fail(out message, "委托信分支版本或集合无效，保留原存档。");
            if (data.letters.Count > 5000) return Fail(out message, "委托信数量超过上限。");
            if (!Finite(data.clockSeconds) || data.clockSeconds < 0 || !Finite(data.nextLetterAt) || data.stamps < 0 || data.nextSequence < 0)
                return Fail(out message, "委托信时钟或邮票数量无效。");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var signatures = new HashSet<string>(StringComparer.Ordinal);
            int earned = 0;
            foreach (var letter in data.letters)
            {
                if (letter == null || string.IsNullOrWhiteSpace(letter.letterId) || !ids.Add(letter.letterId) ||
                    !Enum.IsDefined(typeof(CommissionLetterStatus), letter.status) || letter.wanted == null || letter.wanted.Length == 0 ||
                    letter.minItems < 0 || !Finite(letter.arrivedAt) || !Finite(letter.sentAt) || !Finite(letter.replyDueAt))
                    return Fail(out message, "委托信编号、状态或时间无效。");
                if (!string.IsNullOrEmpty(letter.clientId) && (letter.storyChapter < 1 || letter.storyChapter > letter.storyLength ||
                    letter.storyLength < 8 || letter.wanted.Length < 2 || letter.wanted.Length > 4 || string.IsNullOrEmpty(letter.favoriteAssetId) ||
                    letter.setting < 0 || letter.setting > 2 || letter.sentUtcTicks < 0 || letter.sentUtcTicks > DateTime.MaxValue.Ticks))
                    return Fail(out message, "夜间委托章节或情绪请求无效。");
                if (letter.IsOpen) continue;
                if (string.IsNullOrEmpty(letter.sentPageId) || string.IsNullOrEmpty(letter.sentSignature) || letter.itemCount <= 0 ||
                    letter.tier < 1 || letter.tier > 3 || letter.stamps != letter.tier || !signatures.Add(letter.sentSignature))
                    return Fail(out message, "已寄出的委托缺少页面、内容签名或评价，或同一页面内容被重复寄出。");
                if (!string.IsNullOrEmpty(letter.clientId) &&
                    (letter.storyChapter < 1 || letter.storyChapter > letter.storyLength || letter.storyLength < 8 ||
                     letter.wanted.Length < 2 || letter.wanted.Length > 4 || letter.coins < 0 || letter.rewardCount < 1 ||
                     string.IsNullOrEmpty(letter.rewardAssetId) || letter.rareRewardCount < 0 ||
                     (letter.rareRewardCount > 0 && string.IsNullOrEmpty(letter.rareRewardAssetId)) ||
                     letter.moodScore < 0 || letter.moodScore > 100))
                    return Fail(out message, "夜间委托章节、情绪评价或回礼快照无效。");
                if (letter.status == CommissionLetterStatus.Replied) earned += letter.stamps;
            }
            if (earned != data.stamps) return Fail(out message, "邮票总数与已回信委托不一致。");
            return true;
        }
        static bool Fail(out string message, string reason) { message = reason; return false; }
        static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
        public CommissionDeskData Clone() => JsonUtility.FromJson<CommissionDeskData>(JsonUtility.ToJson(this));
    }
}

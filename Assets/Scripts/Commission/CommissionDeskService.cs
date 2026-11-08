using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VintageJournal
{
    /// <summary>Result of reading one page against a letter. Pure data; no Unity objects.</summary>
    public sealed class CommissionEvaluation
    {
        public string[] wanted = Array.Empty<string>(), matched = Array.Empty<string>(), missing = Array.Empty<string>(), assets = Array.Empty<string>();
        public int itemCount, minItems, tier;
        public int moodScore, balanceBonus, favoriteBonus;
        public string feedbackDetail;
        public string signature;
        public bool HasContent => itemCount > 0;
    }

    /// <summary>
    /// Deterministic commission rules. The live clock advances every frame but is only persisted together with
    /// real events (arrival, send, reply), all through the shared atomic craft/journal save transaction.
    /// </summary>
    public sealed class CommissionDeskService
    {
        readonly Func<CommissionDeskData> read;
        readonly Func<Action<CommissionDeskData>, string, bool> commit;
        readonly Func<CraftOwner, string, string, IReadOnlyList<CraftItemRecord>> pageItems;
        public CommissionCatalog Catalog { get; }
        public double Clock { get; private set; }
        public string LastMessage { get; private set; } = string.Empty;
        public event Action<CommissionLetterState> LetterArrived, ReplyArrived;
        public bool ContentAvailable { get; set; } = true;
        public int ContentSetting {get;set;}
        // Opt-in preserves the old story factories and legacy validation behavior.
        public bool ProgressionEnabled {get;set;}
        public bool NightClientsEnabled { get; }

        public CommissionDeskService(CommissionCatalog catalog, Func<CommissionDeskData> read,
            Func<Action<CommissionDeskData>, string, bool> commit, Func<CraftOwner, string, string, IReadOnlyList<CraftItemRecord>> pageItems,
            bool useNightClients = false)
        {
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.read = read; this.commit = commit; this.pageItems = pageItems;
            NightClientsEnabled = useNightClients;
            var data = read();
            Clock = data != null ? data.clockSeconds : 0;
            if (data == null) commit(d => { d.nextLetterAt = catalog.firstLetterDelaySeconds; }, "初始化委托信");
        }

        public CommissionDeskData Data => read();
        public IEnumerable<CommissionLetterState> OpenLetters => Data.letters.Where(l => l.IsOpen).OrderBy(l => l.sequence);
        public IEnumerable<CommissionLetterState> WaitingReplies => Data.letters.Where(l => l.status == CommissionLetterStatus.Sent).OrderBy(l => l.sequence);
        public IEnumerable<CommissionLetterState> Replied => Data.letters.Where(l => l.status == CommissionLetterStatus.Replied).OrderByDescending(l => l.sequence);
        public int UnreadCount => Data.letters.Count(l => l.status == CommissionLetterStatus.Unread || (l.status == CommissionLetterStatus.Replied && !replySeen.Contains(l.letterId)));
        readonly HashSet<string> replySeen = new HashSet<string>(StringComparer.Ordinal);
        public void MarkReplySeen(string id) { if (id != null) replySeen.Add(id); }
        public bool ReplySeen(string id) => replySeen.Contains(id);

        /// <summary>Advance time; deliver letters and replies when due. Returns true when something was committed.</summary>
        public bool Tick(double deltaSeconds)
        {
            if (deltaSeconds > 0 && deltaSeconds < 5) Clock += deltaSeconds;
            var data = Data; bool changed = false;
            foreach (var due in data.letters.Where(l => l.status == CommissionLetterStatus.Sent && l.replyDueAt <= Clock).Select(l => l.letterId).ToArray())
                changed |= DeliverReply(due);
            data = Data;
            int open = data.letters.Count(l => l.IsOpen);
            if ((!NightClientsEnabled || ContentAvailable) && open < Catalog.maxOpenLetters && Clock >= data.nextLetterAt) changed |= DeliverNextLetter();
            return changed;
        }

        /// <summary>Validation and manual pacing: bring the next arrival forward to now.</summary>
        public bool RequestLetterNow()
        {
            if (NightClientsEnabled && !ContentAvailable) { LastMessage = "夜间的朋友在星光下到访。切到黑夜，就能收到他们的信。"; return false; }
            if (Data.letters.Count(l => l.IsOpen) >= Catalog.maxOpenLetters) { LastMessage = "邮筒里已经有 " + Catalog.maxOpenLetters + " 封待回应的信。"; return false; }
            return DeliverNextLetter();
        }
        public bool FastForwardReplies() { Clock = Math.Max(Clock, WaitingReplies.Select(l => l.replyDueAt).DefaultIfEmpty(Clock).Max()); return Tick(0); }

        bool DeliverNextLetter()
        {
            // Choose before committing: waiting for either client's reply must not emit empty save transactions every frame.
            int reputation=ProgressionEnabled?1+Data.letters.Count(l=>l.status==CommissionLetterStatus.Replied)/3:int.MaxValue;
            CommissionLetterState arrived = NightClientsEnabled ? Catalog.CreateSettingLetter(Data, Clock,ContentSetting,reputation) : Catalog.CreateLetter(Data.nextSequence, Clock);
            if (arrived == null) { LastMessage = "两位朋友正在读你的作品，回信后故事会继续。"; return false; }
            bool ok = commit(d =>
            {
                // Template ids are deterministic; never collide with an existing record.
                while (d.letters.Any(l => l.letterId == arrived.letterId)) arrived.letterId += "-b";
                d.letters.Add(arrived); d.nextSequence++; d.clockSeconds = Clock;
                d.nextLetterAt = Clock + Catalog.idleLetterIntervalSeconds;
            }, "收到委托信");
            if (ok) { LastMessage = "收到一封来自" + arrived.sender + "的信。"; LetterArrived?.Invoke(arrived); }
            return ok;
        }

        bool DeliverReply(string letterId)
        {
            CommissionLetterState replied = null;
            bool ok = commit(d =>
            {
                replied = d.letters.First(l => l.letterId == letterId);
                replied.status = CommissionLetterStatus.Replied; replied.replyText = Catalog.ReplyFor(replied);
                d.stamps += replied.stamps; d.clockSeconds = Clock;
            }, "收到回信");
            if (ok) { LastMessage = replied.sender + "回信了，附上 " + replied.stamps + " 枚邮票" +
                (!string.IsNullOrEmpty(replied.clientId) ? "、" + replied.coins + " 金币和素材回礼。" : "。"); ReplyArrived?.Invoke(replied); }
            return ok;
        }

        public bool MarkRead(string letterId)
        {
            var letter = Data.letters.FirstOrDefault(l => l.letterId == letterId);
            if (letter == null || letter.status != CommissionLetterStatus.Unread) return letter != null;
            return commit(d => { d.letters.First(l => l.letterId == letterId).status = CommissionLetterStatus.Read; d.clockSeconds = Clock; }, "阅读委托信");
        }

        /// <summary>Reads only the page's own instances. Tags come from data, never from meshes or names.</summary>
        public CommissionEvaluation Evaluate(CommissionLetterState letter, CraftOwner owner, string containerId, string pageId)
        {
            var result = new CommissionEvaluation { wanted = letter?.wanted ?? Array.Empty<string>(), minItems = letter?.minItems ?? 0 };
            var items = string.IsNullOrEmpty(pageId) ? Array.Empty<CraftItemRecord>() : pageItems(owner, containerId, pageId) ?? Array.Empty<CraftItemRecord>();
            result.itemCount = items.Count;
            result.assets = items.Select(i => i.assetId).ToArray();
            bool night = !string.IsNullOrEmpty(letter?.clientId);
            var present = new HashSet<string>(items.SelectMany(i => night ? Catalog.MoodsFor(i.assetId) : Catalog.TagsFor(i.assetId)));
            result.matched = result.wanted.Where(present.Contains).ToArray();
            result.missing = result.wanted.Where(t => !present.Contains(t)).ToArray();
            bool allTags = result.missing.Length == 0, enough = result.itemCount >= result.minItems;
            result.tier = allTags && enough ? 3 : (result.matched.Length * 2 >= result.wanted.Length && result.matched.Length > 0) ? 2 : 1;
            if (night)
            {
                // Small, deterministic composition bonuses. These are gentle tie breakers, never a requirement to submit.
                if (items.Count >= 2)
                {
                    var centre = new Vector2(items.Average(i => i.position.x), items.Average(i => i.position.y));
                    float span = Mathf.Max(items.Max(i => i.position.x) - items.Min(i => i.position.x), items.Max(i => i.position.y) - items.Min(i => i.position.y));
                    if (Mathf.Abs(centre.x - .5f) <= .2f && Mathf.Abs(centre.y - .5f) <= .2f && span >= .2f) result.balanceBonus = 5;
                }
                if (!string.IsNullOrEmpty(letter.favoriteAssetId) && result.assets.Contains(letter.favoriteAssetId)) result.favoriteBonus = 5;
                result.moodScore = Mathf.Clamp(Mathf.RoundToInt(90f * result.matched.Length / Mathf.Max(1, result.wanted.Length)) + result.balanceBonus + result.favoriteBonus, 0, 100);
                result.tier = result.moodScore >= 80 ? 3 : result.moodScore >= 40 ? 2 : 1;
                result.feedbackDetail = result.favoriteBonus > 0 ? "你留下的" + (letter.favoriteMaterialName ?? "那份素材") + "，是我最喜欢的细节。" :
                    result.matched.Length > 0 ? "这一页的「" + result.matched[0] + "」，让我停下来看了很久。" : "这一页是你认真留给我的心意，我会好好收藏。";
                if (result.balanceBonus > 0) result.feedbackDetail += " 素材之间留出的呼吸，也让我觉得安心。";
            }
            result.signature = owner + "/" + containerId + "/" + pageId + "#" + string.Join(";", items.OrderBy(i => i.instanceId, StringComparer.Ordinal)
                .Select(i => i.instanceId + "@" + i.position.x.ToString("F3") + "," + i.position.y.ToString("F3") + "," + i.rotationDegrees.ToString("F1") + "," + i.scale.ToString("F2")));
            return result;
        }

        public bool AlreadySent(string signature) => Data.letters.Any(l => !l.IsOpen && l.sentSignature == signature);

        /// <summary>Sends a photo of the page. Materials keep their owner; nothing is moved, copied or deleted.</summary>
        public bool Send(string letterId, CraftOwner owner, string containerId, string pageId, string photoFile)
        {
            var letter = Data.letters.FirstOrDefault(l => l.letterId == letterId);
            if (letter == null || !letter.IsOpen) { LastMessage = "这封信已经回应过了。"; return false; }
            if (owner != CraftOwner.JournalPage && owner != CraftOwner.ReleaseBookPage) { LastMessage = "请选择手账或离型本上的一页。"; return false; }
            var evaluation = Evaluate(letter, owner, containerId, pageId);
            if (!evaluation.HasContent) { LastMessage = "这一页还是空白的，先贴上一些素材吧。"; return false; }
            if (AlreadySent(evaluation.signature)) { LastMessage = "这一页已经原样寄出过了。换一页，或者再添点什么。"; return false; }
            bool ok = commit(d =>
            {
                var l = d.letters.First(x => x.letterId == letterId);
                l.status = CommissionLetterStatus.Sent; l.sentAt = Clock; l.replyDueAt = Clock + Catalog.replyDelaySeconds;
                l.sentUtcTicks=DateTime.UtcNow.Ticks;
                l.sentOwner = owner; l.sentContainerId = containerId; l.sentPageId = pageId; l.sentSignature = evaluation.signature;
                l.matched = evaluation.matched; l.sentAssets = evaluation.assets; l.itemCount = evaluation.itemCount;
                l.tier = evaluation.tier; l.stamps = evaluation.tier; l.photoFile = photoFile ?? string.Empty; d.clockSeconds = Clock;
                if (!string.IsNullOrEmpty(l.clientId))
                {
                    l.moodScore = evaluation.moodScore; l.balanceBonus = evaluation.balanceBonus; l.favoriteBonus = evaluation.favoriteBonus;
                    l.feedbackDetail = evaluation.feedbackDetail;
                    l.coins = l.tier == 3 ? 24 : l.tier == 2 ? 16 : 10;
                    l.rewardAssetId = l.setting==1?NightMaterialCatalog.DayAssetIds[l.sequence%NightMaterialCatalog.DayAssetIds.Length]:l.setting==2?NightMaterialCatalog.AfternoonAssetIds[l.sequence%NightMaterialCatalog.AfternoonAssetIds.Length]:l.clientId == "lighthouse" ? "night-lantern-paper" : "night-glow-ink";
                    l.rewardCount = l.tier >= 2 ? 2 : 1;
                    l.rareRewardAssetId = l.tier == 3 ? "night-forgotten-dream" : null;
                    l.rareRewardCount = l.tier == 3 ? 1 : 0;
                }
                // The next request follows shortly so the desk keeps a steady rhythm.
                d.nextLetterAt = Math.Min(d.nextLetterAt, Clock + Catalog.nextLetterDelaySeconds);
            }, "寄出委托作品");
            LastMessage = ok ? "已拍照寄出，作品留在你的本子里。回信大约 " + Mathf.RoundToInt(Catalog.replyDelaySeconds) + " 秒后到达。" : "寄出失败，作品和信件保持原样。";
            return ok;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VintageJournal
{
    [Serializable] public sealed class CommissionAssetTags { public string assetId; public string[] tags = Array.Empty<string>(); public string[] moods = Array.Empty<string>(); }

    [Serializable]
    public sealed class NightClientDefinition
    {
        public string id, name, favoriteAssetId, favoriteMaterialName;
        public CommissionLetterDefinition[] chapters = Array.Empty<CommissionLetterDefinition>();
    }

    [Serializable]
    public sealed class CommissionLetterDefinition
    {
        public string id, sender, title, body;
        public string[] wanted = Array.Empty<string>();
        public int minItems = 2;
        public string replyFull, replyPartial, replyPlain;
    }

    [Serializable]
    public sealed class CommissionOccasion
    {
        public string title, body; // {sender} {a} {b} placeholders
        public int minItems = 2;
    }

    /// <summary>Data-only commission content: material tags, authored story letters and templates for an endless stream.</summary>
    [Serializable]
    public sealed class CommissionCatalog
    {
        public float firstLetterDelaySeconds = 2, nextLetterDelaySeconds = 6, idleLetterIntervalSeconds = 150, replyDelaySeconds = 25;
        public int maxOpenLetters = 3;
        public CommissionAssetTags[] tags = Array.Empty<CommissionAssetTags>();
        public CommissionLetterDefinition[] letters = Array.Empty<CommissionLetterDefinition>();
        public string[] senders = Array.Empty<string>();
        public CommissionOccasion[] occasions = Array.Empty<CommissionOccasion>();
        public string[] tagPairs = Array.Empty<string>(); // "花草|暖色"
        public string replyFullTemplate, replyPartialTemplate, replyPlainTemplate;
        public NightClientDefinition[] nightClients = Array.Empty<NightClientDefinition>();
        public NightClientDefinition[] dayClients = Array.Empty<NightClientDefinition>(), afternoonClients = Array.Empty<NightClientDefinition>();
        public NightClientDefinition[] ClientsFor(int setting)=>setting==1?dayClients:setting==2?afternoonClients:nightClients;

        Dictionary<string, string[]> lookup;
        public const string ResourcePath = "Commissions/commission-catalog";

        public static CommissionCatalog Load()
        {
            var asset = Resources.Load<TextAsset>(ResourcePath);
            if (!asset) throw new InvalidOperationException("缺少委托信数据 Resources/" + ResourcePath + ".json");
            return Parse(asset.text);
        }
        public static CommissionCatalog Parse(string json)
        {
            var catalog = JsonUtility.FromJson<CommissionCatalog>(json);
            if (catalog == null || catalog.letters.Length == 0 || catalog.senders.Length == 0 || catalog.occasions.Length == 0 || catalog.tagPairs.Length == 0)
                throw new InvalidOperationException("委托信数据缺少信件、寄信人、场合或标签组合。");
            return catalog;
        }

        public string[] TagsFor(string assetId)
        {
            if (lookup == null) lookup = tags.Where(t => t != null && !string.IsNullOrEmpty(t.assetId)).ToDictionary(t => t.assetId, t => t.tags ?? Array.Empty<string>());
            return assetId != null && lookup.TryGetValue(assetId, out var found) ? found : Array.Empty<string>();
        }
        public IEnumerable<string> AllTags => tags.SelectMany(t => t.tags).Distinct();

        public string[] MoodsFor(string assetId)
        {
            var entry = tags.FirstOrDefault(t => t != null && t.assetId == assetId);
            if(entry != null && entry.moods != null && entry.moods.Length > 0)return entry.moods;
            var builtin=NightMaterialCatalog.TagsFor(assetId);
            if(builtin.Length>0)return builtin;
            var personal=PersonalStudioAssets.TagsFor(assetId);if(personal.Length>0)return personal;
            return assetId!=null&&assetId.StartsWith("social-gift-",StringComparison.Ordinal)?new[]{"温暖","梦幻"}:Array.Empty<string>();
        }

        /// <summary>At most one unfinished chapter per client. A reply advances that client's story, regardless of score.</summary>
        public CommissionLetterState CreateNightLetter(CommissionDeskData data, double now)
        { return CreateSettingLetter(data,now,0); }
        public CommissionLetterState CreateSettingLetter(CommissionDeskData data,double now,int setting,int reputation=int.MaxValue)
        {
            var clients=ClientsFor(setting);
            if (clients == null || clients.Length == 0) return null;
            int delivered = data.letters.Count(l => !string.IsNullOrEmpty(l.clientId)&&l.setting==setting);
            for (int offset = 0; offset < clients.Length; offset++)
            {
                int clientIndex=(delivered+offset)%clients.Length;
                var client = clients[clientIndex];
                int requiredReputation=clientIndex==0?1:2;
                // Existing correspondence is never locked again when opening an older save.
                bool alreadyMet=data.letters.Any(l=>l.clientId==client.id);
                if(reputation<requiredReputation&&!alreadyMet)continue;
                if (data.letters.Any(l => l.clientId == client.id && l.status != CommissionLetterStatus.Replied)) continue;
                int finished = data.letters.Count(l => l.clientId == client.id && l.status == CommissionLetterStatus.Replied && !l.afterStory);
                int chapter = Math.Min(finished, client.chapters.Length - 1);
                var definition = client.chapters[chapter]; bool after = finished >= client.chapters.Length;
                return new CommissionLetterState {
                    letterId = "night-" + client.id + "-" + data.nextSequence.ToString("D4"), sequence = data.nextSequence,setting=setting,
                    sender = client.name, clientId = client.id, storyChapter = chapter + 1, storyLength = client.chapters.Length,
                    favoriteAssetId = client.favoriteAssetId, favoriteMaterialName = client.favoriteMaterialName, afterStory = after,
                    title = after ? "故事之后，仍有一盏灯" : definition.title,
                    body = after ? "今天没有新的难题。只想再收到一页你的近况，把今天的一点光留在这里。" : definition.body,
                    wanted = definition.wanted.ToArray(), minItems = 1, status = CommissionLetterStatus.Unread, arrivedAt = now,
                    replyFull = definition.replyFull, replyPartial = definition.replyPartial, replyPlain = definition.replyPlain,
                    twistKind = delivered % 4 == 3 ? new[] { "three", "space", "found", "line" }[(delivered / 4) % 4] : null,
                    twistBrief = delivered % 4 == 3 ? new[] { "小小灵感：试着只用三样素材。", "小小灵感：给这页多留一点空白。", "小小灵感：试试今天拾到的素材。", "小小灵感：把素材排成一条安静的路。" }[(delivered / 4) % 4] : null
                };
            }
            return null;
        }

        /// <summary>Authored letters first, then a deterministic, endless template stream.</summary>
        public CommissionLetterState CreateLetter(int sequence, double now)
        {
            var state = new CommissionLetterState { sequence = sequence, arrivedAt = now, status = CommissionLetterStatus.Unread };
            if (sequence < letters.Length)
            {
                var d = letters[sequence];
                state.letterId = d.id; state.sender = d.sender; state.title = d.title; state.body = d.body; state.wanted = d.wanted.ToArray();
                state.minItems = d.minItems; state.replyFull = d.replyFull; state.replyPartial = d.replyPartial; state.replyPlain = d.replyPlain;
                return state;
            }
            int n = sequence - letters.Length;
            string sender = senders[(n * 7 + 3) % senders.Length];
            var occasion = occasions[(n * 5 + 1) % occasions.Length];
            var pair = tagPairs[(n * 11 + 2) % tagPairs.Length].Split('|');
            string a = pair[0], b = pair.Length > 1 ? pair[1] : pair[0];
            string Fill(string s) => (s ?? string.Empty).Replace("{sender}", sender).Replace("{a}", a).Replace("{b}", b);
            state.letterId = "letter-gen-" + sequence.ToString("D4"); state.sender = sender;
            state.title = Fill(occasion.title); state.body = Fill(occasion.body);
            state.wanted = a == b ? new[] { a } : new[] { a, b }; state.minItems = occasion.minItems;
            return state;
        }

        public string ReplyFor(CommissionLetterState letter)
        {
            string custom = letter.tier >= 3 ? letter.replyFull : letter.tier == 2 ? letter.replyPartial : letter.replyPlain;
            string template = !string.IsNullOrEmpty(custom) ? custom :
                letter.tier >= 3 ? replyFullTemplate : letter.tier == 2 ? replyPartialTemplate : replyPlainTemplate;
            string matched = letter.matched != null && letter.matched.Length > 0 ? string.Join("、", letter.matched) : "你的心意";
            string text = (template ?? "谢谢你的作品。—— {sender}").Replace("{sender}", letter.sender).Replace("{matched}", matched);
            if (!string.IsNullOrEmpty(letter.clientId) && !string.IsNullOrEmpty(letter.feedbackDetail)) text += "\n" + letter.feedbackDetail;
            return text;
        }
    }
}

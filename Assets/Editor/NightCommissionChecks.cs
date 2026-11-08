using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VintageJournal.Editor
{
    /// <summary>Deterministic checks run in the real Unity runtime; they do not claim Play Mode or native mouse coverage.</summary>
    public static class NightCommissionChecks
    {
        [Serializable] public sealed class CheckResult { public string name; public bool passed; }
        [Serializable] sealed class Report { public string scope, timestampUtc; public int passed, failed; public CheckResult[] checks; }
        static readonly List<CheckResult> results = new List<CheckResult>();

        [MenuItem("Vintage Journal/Night slice/Run commission data checks")]
        public static void Run() { if (!RunDataChecks()) throw new InvalidOperationException("Night commission checks failed."); }

        public static bool RunDataChecks()
        {
            results.Clear();
            try
            {
                var catalog = CommissionCatalog.Load();
                Check("Two recurring night clients each have eight complete authored chapters", catalog.nightClients.Length == 2 &&
                    catalog.nightClients.Select(c => c.id).Distinct().Count() == 2 && catalog.nightClients.All(c => c.chapters.Length == 8 &&
                    c.chapters.All(l => !string.IsNullOrWhiteSpace(l.body) && !string.IsNullOrWhiteSpace(l.replyFull) && l.wanted.Length >= 2 && l.wanted.Length <= 4)));
                var moods = new HashSet<string>(catalog.tags.SelectMany(t => t.moods).Concat(new[] { "静谧", "魔法", "微光", "梦幻", "遥远", "希望", "思念", "温暖", "怀旧" }));
                Check("Every existing material has one to three mood tags", catalog.tags.All(t => t.moods != null && t.moods.Length >= 1 && t.moods.Length <= 3));
                Check("Every requested mood can be provided by existing or collectible material", catalog.nightClients.SelectMany(c => c.chapters).SelectMany(l => l.wanted).All(moods.Contains));
                Check("New night collectible mood tags are available to evaluation", catalog.MoodsFor("night-glow-ink").Contains("微光") && catalog.MoodsFor("night-lantern-paper").Contains("温暖"));

                var legacy = catalog.CreateLetter(0, 0);
                Check("Old letter IDs and old theme tags remain unchanged", legacy.letterId == "letter-001" && legacy.wanted.SequenceEqual(new[] { "花草", "暖色" }) && catalog.TagsFor("tape_01").Contains("花草"));

                var sim = new Simulation(catalog);
                sim.service.ContentAvailable = false;
                Check("Daytime cannot request a new night commission", !sim.service.RequestLetterNow() && sim.data.letters.Count == 0);
                sim.service.ContentAvailable = true;
                Check("Both clients can visit and no client has two unfinished chapters", sim.service.RequestLetterNow() && sim.service.RequestLetterNow() && !sim.service.RequestLetterNow() &&
                    sim.data.letters.All(l => l.storyChapter == 1) && sim.data.letters.Select(l => l.clientId).Distinct().Count() == 2);
                var first = sim.service.OpenLetters.First();
                Check("Empty pages cannot be submitted", !sim.service.Send(first.letterId, CraftOwner.JournalPage, "journal", "empty", ""));
                sim.items.Add(new CraftItemRecord { instanceId = "unknown", assetId = "unknown-material", position = new Vector2(.5f, .5f) });
                var mismatch = sim.service.Evaluate(first, CraftOwner.JournalPage, "journal", "page-1");
                Check("No-match completed page is accepted with coins and a common material", mismatch.tier == 1 && sim.service.Send(first.letterId, CraftOwner.JournalPage, "journal", "page-1", "photo.png") &&
                    sim.data.letters.First().coins == 10 && sim.data.letters.First().rewardCount == 1);
                Check("Sending the same letter twice cannot duplicate its reward", !sim.service.Send(first.letterId, CraftOwner.JournalPage, "journal", "page-1", "photo.png"));
                sim.service.ContentAvailable = false;
                sim.service.FastForwardReplies();
                Check("Replies still arrive in daytime and story advances only after reply", sim.data.letters.First().status == CommissionLetterStatus.Replied && sim.data.letters.Count == 2);
                var restored = new Simulation(catalog, sim.data.Clone());
                restored.service.ContentAvailable = true;
                restored.service.RequestLetterNow();
                var next = restored.data.letters.Last();
                Check("Restart resumes chapter two without skipping the outstanding other client", next.clientId == first.clientId && next.storyChapter == 2 && restored.data.letters.Count(l => l.clientId != first.clientId) == 1);

                var evaluationSim = new Simulation(catalog);
                var example = new CommissionLetterState { clientId = "moth", wanted = new[] { "静谧", "魔法" }, favoriteAssetId = "night-glow-ink", favoriteMaterialName = "微光墨迹" };
                evaluationSim.items.Add(new CraftItemRecord { instanceId = "glow", assetId = "night-glow-ink", position = new Vector2(.35f, .5f) });
                evaluationSim.items.Add(new CraftItemRecord { instanceId = "star", assetId = "night-star-sticker", position = new Vector2(.65f, .5f) });
                var full = evaluationSim.service.Evaluate(example, CraftOwner.JournalPage, "journal", "example");
                Check("Hidden mood match has small layout and favorite-material bonuses", full.tier == 3 && full.balanceBonus == 5 && full.favoriteBonus == 5 && full.moodScore <= 100 && full.feedbackDetail.Contains("微光墨迹"));
                Check("Evaluation does not change the letter or material ownership", example.tier == 0 && evaluationSim.items.All(i => i.owner == CraftOwner.Stock));

                var story = new Simulation(catalog);
                bool ordered = true, twist = false, valid = true;
                var perClient = new Dictionary<string, int>();
                for (int i = 0; i < 16; i++)
                {
                    if (!story.service.OpenLetters.Any()) story.service.RequestLetterNow();
                    var letter = story.service.OpenLetters.First();
                    int expected = perClient.TryGetValue(letter.clientId, out int previous) ? previous + 1 : 1;
                    ordered &= letter.storyChapter == expected && !letter.afterStory;
                    twist |= !string.IsNullOrEmpty(letter.twistBrief);
                    story.items.Clear(); story.items.Add(new CraftItemRecord { instanceId = "story-item-" + i, assetId = letter.favoriteAssetId });
                    ordered &= story.service.Send(letter.letterId, CraftOwner.JournalPage, "journal", "story-page-" + i, "photo.png");
                    story.service.FastForwardReplies(); perClient[letter.clientId] = expected;
                    valid &= CommissionDeskData.Validate(new JournalDocument { commissionDesk = story.data }, out _);
                }
                Check("Both eight-chapter arcs progress in order and remain valid after every reply", ordered && valid && perClient.Count == 2 && perClient.Values.All(n => n == 8));
                Check("Roughly every fourth commission rotates a gentle creative invitation", twist && story.data.letters.Take(16).Count(l => !string.IsNullOrEmpty(l.twistBrief)) == 4);
                if (!story.service.OpenLetters.Any()) story.service.RequestLetterNow();
                Check("Clients keep writing after their story without resetting progress", story.service.OpenLetters.All(l => l.afterStory && l.storyChapter == 8));
                int count = story.data.letters.Count;
                story.accept = false; story.service.RequestLetterNow();
                Check("A rejected atomic save does not add a letter", story.data.letters.Count == count);
            }
            catch (Exception error) { Check("Exception: " + error, false); Debug.LogException(error); }
            int failed = results.Count(r => !r.passed);
            var directory = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "docs", "night-gameplay"); Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "commission-data-tests.json"), JsonUtility.ToJson(new Report {
                timestampUtc = DateTime.UtcNow.ToString("O"), scope = "Actual Unity JsonUtility and deterministic commission-service checks; no Play Mode or native input claim.",
                passed = results.Count - failed, failed = failed, checks = results.ToArray()
            }, true));
            Debug.Log("[NightCommission] " + (results.Count - failed) + " passed; " + failed + " failed"); return failed == 0;
        }

        sealed class Simulation
        {
            public CommissionDeskData data;
            public CommissionDeskService service;
            public bool accept = true;
            public readonly List<CraftItemRecord> items = new List<CraftItemRecord>();
            public Simulation(CommissionCatalog catalog, CommissionDeskData initial = null)
            {
                data = initial;
                service = new CommissionDeskService(catalog, () => data, (mutate, label) => {
                    if (!accept) return false;
                    var next = data?.Clone() ?? CommissionDeskData.CreateDefault(); mutate(next);
                    if (!CommissionDeskData.Validate(new JournalDocument { commissionDesk = next }, out string error)) throw new InvalidOperationException(error);
                    data = next; return true;
                }, (owner, container, page) => page == "empty" ? Array.Empty<CraftItemRecord>() : items.ToArray(), true);
            }
        }
        static void Check(string name, bool passed) { results.Add(new CheckResult { name = name, passed = passed }); Debug.Log("[NightCommission] " + (passed ? "PASS " : "FAIL ") + name); }
    }
}

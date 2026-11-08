using System;
using System.Linq;
using UnityEngine;

namespace VintageJournal
{
    public sealed partial class CraftWorkspaceService
    {
        static PersonalStudioData EnsureStudio(JournalDocument document)
        {
            document.personalStudio ??= new PersonalStudioData(); document.personalStudioEnabled = true;
            document.personalStudio.initialized = true; return document.personalStudio;
        }
        public bool InitializePersonalStudio()
        {
            if (Document.personalStudio?.initialized == true) return true;
            var candidate = Document.DeepClone(); var studio = EnsureStudio(candidate);
            studio.sections.Add(new StudioSection { id = "studio-section-dreams", title = "梦的记录", pageId = candidate.pages[0].pageId });
            return Commit(candidate, "准备我的手账", false);
        }
        public bool SetPersonalMode(bool value)
        {
            var candidate = Document.DeepClone(); EnsureStudio(candidate).privateJournalMode = value;
            return Commit(candidate, value ? "进入私人创作" : "返回委托创作", false);
        }
        public bool CustomizeCover(string title, string material, int colorIndex, int charm)
        {
            if (string.IsNullOrWhiteSpace(title) || title.Length > 40 || colorIndex < 0 || colorIndex > 5 || charm < 0 || charm > 3 ||
                (material != "棉布" && material != "和纸" && material != "软木")) return Fail("封面标题或选项无效。");
            var candidate = Document.DeepClone(); var studio = EnsureStudio(candidate);
            studio.title = title; studio.coverMaterial = material; studio.coverColor = colorIndex; studio.charm = charm;
            return Commit(candidate, "保存私人封面", false);
        }
        public bool AddStudioSection(string title, string pageId)
        {
            if (string.IsNullOrWhiteSpace(title) || title.Length > 40 || !Document.AllPages.Any(p => p.pageId == pageId)) return Fail("选择有效页面并填写分区名。");
            var candidate = Document.DeepClone(); var studio = EnsureStudio(candidate);
            if (studio.sections.Count >= 32) return Fail("分区最多32个。");
            studio.sections.Add(new StudioSection { id = "section-" + Guid.NewGuid().ToString("N"), title = title, pageId = pageId });
            return Commit(candidate, "新增手账分区", false);
        }
        public bool SaveDiary(string dateKey, string prompt, string body)
        {
            if (!DateTime.TryParseExact(dateKey, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _) ||
                body == null || body.Length > 2000 || prompt == null || prompt.Length > 200) return Fail("日记日期或文字超出范围。");
            var candidate = Document.DeepClone(); var studio = EnsureStudio(candidate);
            var entry = studio.diary.Find(e => e.dateKey == dateKey);
            if (entry == null) { if (studio.diary.Count >= 3660) return Fail("日记已达容量上限。"); entry = new StudioDiaryEntry { dateKey = dateKey }; studio.diary.Add(entry); }
            entry.body = body; entry.prompt = prompt; entry.updatedUtcTicks = DateTime.UtcNow.Ticks;
            return Commit(candidate, "保存私人日记", false);
        }
        public bool MarkStudioPage(string pageId, string title, bool favorite)
        {
            if (!Document.AllPages.Any(p => p.pageId == pageId) || title == null || title.Length > 80) return Fail("页面标记无效。");
            var candidate = Document.DeepClone(); var studio = EnsureStudio(candidate);
            var note = studio.pageNotes.Find(n => n.pageId == pageId);
            if (note == null) { note = new StudioPageNote { pageId = pageId, createdDate = DateTime.Now.ToString("yyyy-MM-dd") }; studio.pageNotes.Add(note); }
            note.title = title; note.favorite = favorite;
            return Commit(candidate, "保存作品收藏", false);
        }
        public bool SetStudioPaper(string pageId, string paperId, int colorIndex)
        {
            if (!Document.pages.Any(p => p.pageId == pageId) || colorIndex < 0 || colorIndex > 5 || JournalPaperCatalog.Load().Find(paperId) == null)
                return Fail("仅活页本允许更换已拥有的内页纸张。");
            var candidate = Document.DeepClone(); var studio = EnsureStudio(candidate);
            candidate.pages.First(p => p.pageId == pageId).paperId = paperId;
            var note = studio.pageNotes.Find(n => n.pageId == pageId);
            if (note == null) { note = new StudioPageNote { pageId = pageId, title = string.Empty, createdDate = DateTime.Now.ToString("yyyy-MM-dd") }; studio.pageNotes.Add(note); }
            note.paperColor = colorIndex;
            return Commit(candidate, "保存内页纸纹与底色", false);
        }
        public bool NavigateStudioSpread(string pageId)
        {
            int index = Array.FindIndex(Document.ActivePages, p => p.pageId == pageId);
            if (index < 0) return Fail("请先把这页所属的本子放到中央。");
            var candidate = Document.DeepClone(); candidate.ActiveSpread = index / 2;
            return Commit(candidate, "翻阅珍藏页面", false);
        }
        public string SaveStudioArt(string name, string kind, string pngBase64, int width = 256, int height = 256, string text = "", int fontStyle = 0)
        {
            if (Document.personalStudio?.artworks.Count >= 24) { Fail("素材模板已满24张；现有素材仍可反复印制。"); return null; }
            if (kind != "text" && !AtelierProgressCatalog.ToolUnlocked(Document, kind == "sticker" ? "sticker-maker" : kind))
            { Fail("先完成更多委托，或在工房兑换对应工具。"); return null; }
            var candidate = Document.DeepClone(); var studio = EnsureStudio(candidate);
            var art = new StudioArtwork { assetId = "studio-art-" + Guid.NewGuid().ToString("N"), name = name, kind = kind,
                pngBase64 = pngBase64, width = width, height = height, text = text ?? string.Empty, fontStyle = fontStyle, createdUtcTicks = DateTime.UtcNow.Ticks };
            art.moodTags = kind == "photo" ? new[] { "怀旧", "日常" } : kind == "text" ? new[] { "日常", "思念" } : kind == "stamp" ? new[] { "微光", "温暖" } : new[] { "梦幻", "希望" };
            studio.artworks.Add(art);
            if (!PersonalStudioData.Validate(candidate, out string error)) { Fail(error); return null; }
            return Commit(candidate, "保存自制素材", false) ? art.assetId : null;
        }
        public bool BeginStudioArt(string assetId)
        {
            if (HeldItem != null || (scrapbook && scrapbook.IsTransforming)) return Fail("先完成当前素材操作。");
            var art = Document.personalStudio?.artworks.Find(a => a.assetId == assetId);
            if (art == null) return Fail("没有找到这份自制素材。");
            var reusable = Items.FirstOrDefault(i => i.assetId == assetId && i.owner == CraftOwner.Stock && i.instanceId.StartsWith("dream-custom-", StringComparison.Ordinal));
            if (reusable != null) return BeginStock(reusable.instanceId);
            var candidate = Document.DeepClone();
            string id = "dream-custom-" + Guid.NewGuid().ToString("N");
            bool text = art.kind == "text", photo = art.kind == "photo";
            candidate.craftWorkspace.items.Add(new CraftItemRecord { instanceId = id, assetId = assetId, owner = CraftOwner.Stock,
                containerId = "studio-print:" + id, kind = photo ? CraftItemKind.Note : CraftItemKind.Sticker,
                sizeMeters = text ? new Vector2(.07f, .035f) : photo ? new Vector2(.05f, .06f) : new Vector2(.04f, .04f) });
            // Printing is a durable acquisition. Cancel and restart restore this exact instance to the collection.
            if (!Commit(candidate, "印制自制素材", false)) return false;
            return BeginStock(id);
        }
    }
}

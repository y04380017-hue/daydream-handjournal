using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VintageJournal
{
    [Serializable] public sealed class StudioSection { public string id, title, pageId; }
    [Serializable] public sealed class StudioDiaryEntry { public string dateKey, prompt, body; public long updatedUtcTicks; }
    [Serializable] public sealed class StudioPageNote { public string pageId, title, createdDate; public bool favorite; public int paperColor; }
    [Serializable] public sealed class StudioArtwork
    {
        public string assetId, name, kind, pngBase64, text;
        public string[] moodTags = Array.Empty<string>();
        public int fontStyle, width = 256, height = 256;
        public long createdUtcTicks;
    }
    /// <summary>Private expression metadata. Existing binder page IDs and the craft ownership registry stay authoritative.</summary>
    [Serializable] public sealed class PersonalStudioData
    {
        public int schemaVersion = 1;
        public bool initialized, privateJournalMode;
        public string title = "我的白日梦手账", coverMaterial = "棉布";
        public int coverColor, charm;
        public List<StudioSection> sections = new List<StudioSection>();
        public List<StudioDiaryEntry> diary = new List<StudioDiaryEntry>();
        public List<StudioPageNote> pageNotes = new List<StudioPageNote>();
        public List<StudioArtwork> artworks = new List<StudioArtwork>();
        public PersonalStudioData Clone() => JsonUtility.FromJson<PersonalStudioData>(JsonUtility.ToJson(this));
        public static bool Validate(JournalDocument document, out string message)
        {
            message = string.Empty; var data = document?.personalStudio;
            if (data == null) return true;
            if (data.schemaVersion != 1 || data.title == null || data.title.Length > 40 || data.coverColor < 0 || data.coverColor > 5 ||
                data.charm < 0 || data.charm > 3 || data.sections == null || data.sections.Count > 32 || data.diary == null || data.diary.Count > 3660 ||
                data.pageNotes == null || data.pageNotes.Count > 4096 || data.artworks == null || data.artworks.Count > 24)
            { message = "私人手账的版本、封面或集合无效。"; return false; }
            var pages = new HashSet<string>(document.AllPages.Select(p => p.pageId));
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var section in data.sections)
                if (section == null || string.IsNullOrEmpty(section.id) || !ids.Add(section.id) || string.IsNullOrWhiteSpace(section.title) || section.title.Length > 40 || !pages.Contains(section.pageId))
                { message = "私人手账分区的页面或标题无效。"; return false; }
            ids.Clear();
            foreach (var entry in data.diary)
                if (entry == null || !DateTime.TryParseExact(entry.dateKey, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _) ||
                    !ids.Add(entry.dateKey) || entry.body == null || entry.body.Length > 2000 || entry.prompt == null || entry.prompt.Length > 200)
                { message = "私人日记日期或文字无效。"; return false; }
            ids.Clear();
            foreach (var note in data.pageNotes)
                if (note == null || !pages.Contains(note.pageId) || !ids.Add(note.pageId) || note.title == null || note.title.Length > 80)
                { message = "私人页面索引无效。"; return false; }
            ids.Clear();
            foreach (var art in data.artworks)
            {
                if (art == null || string.IsNullOrEmpty(art.assetId) || !art.assetId.StartsWith("studio-art-", StringComparison.Ordinal) || !ids.Add(art.assetId) ||
                    string.IsNullOrWhiteSpace(art.name) || art.name.Length > 40 || art.text == null || art.text.Length > 120 ||
                    art.width < 1 || art.width > 512 || art.height < 1 || art.height > 512 ||
                    art.pngBase64 == null || art.pngBase64.Length > 200000 || art.moodTags == null || art.moodTags.Length < 1 || art.moodTags.Length > 3 || (art.kind != "sticker" && art.kind != "text" && art.kind != "stamp" && art.kind != "photo"))
                { message = "自制素材定义或容量无效。"; return false; }
                try
                {
                    byte[] png = Convert.FromBase64String(art.pngBase64);
                    if (png.Length < 24 || png[0] != 137 || png[1] != 80 || png[2] != 78 || png[3] != 71 || ReadBigEndian(png, 16) != art.width || ReadBigEndian(png, 20) != art.height)
                    { message = "自制素材PNG尺寸或格式无效。"; return false; }
                }
                catch (FormatException) { message = "自制素材PNG编码无效。"; return false; }
            }
            return true;
        }
        static int ReadBigEndian(byte[] bytes, int offset) => (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
    }
}

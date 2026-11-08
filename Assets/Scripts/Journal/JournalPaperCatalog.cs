using System;
using System.Collections.Generic;
using UnityEngine;

namespace VintageJournal
{
    [Serializable]
    public sealed class JournalPaperDefinition
    {
        public string id;
        public string displayName;
        public Texture2D texture;
    }

    /// <summary>Read-only art definitions. Player selections live in JournalDocument instead.</summary>
    [CreateAssetMenu(fileName = "JournalPaperCatalog", menuName = "Vintage Journal/Paper catalog")]
    public sealed class JournalPaperCatalog : ScriptableObject
    {
        public JournalPaperDefinition[] papers = Array.Empty<JournalPaperDefinition>();
        public string defaultPaperId;

        public JournalPaperDefinition Find(string id)
        {
            if (string.IsNullOrEmpty(id) || papers == null) return null;
            foreach (var paper in papers)
                if (paper != null && string.Equals(paper.id, id, StringComparison.Ordinal)) return paper;
            return null;
        }

        public static JournalPaperCatalog Load()
        {
            var saved = Resources.Load<JournalPaperCatalog>("JournalPaperCatalog");
            if (saved) return saved;

            // This compatibility path reuses stage-one resources; it never creates or edits art files.
            var fallback = CreateInstance<JournalPaperCatalog>();
            fallback.name = "JournalPaperCatalog (existing stage-one art)";
            fallback.hideFlags = HideFlags.DontSave;
            var definitions = new List<JournalPaperDefinition>();
            var items = ItemCatalog.Load().items;
            if (items != null)
                foreach (var item in items)
                    if (item != null && item.category == "page" && !string.IsNullOrEmpty(item.id))
                        definitions.Add(new JournalPaperDefinition {
                            id = item.id, displayName = item.name, texture = item.Thumbnail
                        });
            definitions.Sort((a, b) => string.Compare(a.id, b.id, StringComparison.Ordinal));
            fallback.papers = definitions.ToArray();
            return fallback;
        }
    }
}

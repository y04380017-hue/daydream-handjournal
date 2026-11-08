using System;
using System.Collections.Generic;
using UnityEngine;

namespace VintageJournal
{
    /// <summary>
    /// A bounded, page-owned command history. A command captures one completed
    /// interaction; intermediate drag frames never need to be recorded.
    /// </summary>
    public sealed class DecorationHistory
    {
        [Serializable] sealed class Snapshot { public List<JournalDecorationData> items; }
        sealed class Command
        {
            public string pageId, label;
            public List<JournalDecorationData> before, after;
        }
        sealed class PageHistory
        {
            public readonly List<Command> undo = new List<Command>();
            public readonly List<Command> redo = new List<Command>();
        }

        readonly Dictionary<string, PageHistory> pages = new Dictionary<string, PageHistory>(StringComparer.Ordinal);
        readonly int capacity;
        public event Action<JournalPageData, List<JournalDecorationData>, string> CommandRecorded;

        public DecorationHistory(int capacity = 60) { this.capacity = Mathf.Max(30, capacity); }

        public static List<JournalDecorationData> Clone(List<JournalDecorationData> source)
        {
            if (source == null || source.Count == 0) return new List<JournalDecorationData>();
            var copy = JsonUtility.FromJson<Snapshot>(JsonUtility.ToJson(new Snapshot { items = source }));
            return copy?.items ?? new List<JournalDecorationData>();
        }

        /// <summary>Call once after a completed edit. The page already contains the result.</summary>
        public void Record(JournalPageData page, List<JournalDecorationData> before, string label)
        {
            if (page == null || string.IsNullOrEmpty(page.pageId))
                throw new ArgumentException("History commands require a stable page ID.", nameof(page));
            var start = Clone(before);
            var finish = Clone(page.decorations);
            if (Serialize(start) == Serialize(finish)) return;
            if (!pages.TryGetValue(page.pageId, out var history))
                pages.Add(page.pageId, history = new PageHistory());
            history.undo.Add(new Command { pageId = page.pageId, label = label ?? string.Empty, before = start, after = finish });
            if (history.undo.Count > capacity) history.undo.RemoveAt(0);
            history.redo.Clear();
            CommandRecorded?.Invoke(page, Clone(start), label ?? string.Empty);
        }

        public bool Undo(JournalPageData page) { return Apply(page, false); }
        public bool Redo(JournalPageData page) { return Apply(page, true); }
        public int UndoCount(string pageId) { return pageId != null && pages.TryGetValue(pageId, out var h) ? h.undo.Count : 0; }
        public int RedoCount(string pageId) { return pageId != null && pages.TryGetValue(pageId, out var h) ? h.redo.Count : 0; }

        bool Apply(JournalPageData page, bool redo)
        {
            if (page == null || string.IsNullOrEmpty(page.pageId) || !pages.TryGetValue(page.pageId, out var history)) return false;
            var source = redo ? history.redo : history.undo;
            var destination = redo ? history.undo : history.redo;
            if (source.Count == 0) return false;
            var command = source[source.Count - 1];
            if (!string.Equals(command.pageId, page.pageId, StringComparison.Ordinal)) return false;
            // Clone before removing the command so a serialization failure cannot
            // consume a valid history entry or leave a partially mutated page.
            var replacement = Clone(redo ? command.after : command.before);
            source.RemoveAt(source.Count - 1);
            page.decorations = replacement;
            destination.Add(command);
            return true;
        }

        static string Serialize(List<JournalDecorationData> items)
        { return JsonUtility.ToJson(new Snapshot { items = items }); }
    }
}

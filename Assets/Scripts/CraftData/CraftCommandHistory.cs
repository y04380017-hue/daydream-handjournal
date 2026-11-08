using System.Collections.Generic;
using UnityEngine;

namespace VintageJournal
{
    /// <summary>One bounded chronology spanning journal and both release books. Snapshots never share mutable records.</summary>
    public sealed class CraftCommandHistory
    {
        public sealed class Command { public string before, after, label; }
        readonly List<Command> undo = new List<Command>(), redo = new List<Command>();
        readonly int capacity;
        public int UndoCount => undo.Count;
        public int RedoCount => redo.Count;
        public CraftCommandHistory(int capacity = 60) { this.capacity = Mathf.Clamp(capacity, 30, 200); }
        public void Record(JournalDocument before, JournalDocument after, string label)
        { Record(JsonUtility.ToJson(before), JsonUtility.ToJson(after), label); }
        public void Record(string before, string after, string label)
        {
            if (before == after) return;
            undo.Add(new Command { before = before, after = after, label = label });
            if (undo.Count > capacity) undo.RemoveAt(0);
            redo.Clear();
        }
        public Command Peek(bool isRedo)
        { var list = isRedo ? redo : undo; return list.Count > 0 ? list[list.Count - 1] : null; }
        public void Complete(bool isRedo)
        {
            var source = isRedo ? redo : undo; var target = isRedo ? undo : redo;
            if (source.Count == 0) return;
            var command = source[source.Count - 1]; source.RemoveAt(source.Count - 1); target.Add(command);
        }
    }
}

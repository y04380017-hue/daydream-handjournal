using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VintageJournal
{
    [Serializable]
    public sealed class NotebookData
    {
        public string bookId, physicalInstanceId, displayName;
        public Vector2 pageSizeMeters;
        public int currentSpread;
        public JournalPageData[] pages;
    }

    /// <summary>Binder remains in the original v1 fields; every bound notebook owns a separate page collection.</summary>
    [Serializable]
    public sealed class NotebookLibraryData
    {
        public const string BinderId = "binder";
        public int schemaVersion = 1;
        public string activeBookId = BinderId;
        public List<NotebookData> notebooks = new List<NotebookData>();
        public NotebookData Active => notebooks?.FirstOrDefault(b => b != null && b.bookId == activeBookId);
        public static bool Validate(JournalDocument document, out string error)
        {
            error = null;
            var lib = document.notebookLibrary;
            if (lib == null) return true;
            if (lib.schemaVersion != 1 || lib.notebooks == null || lib.notebooks.Count > 32)
            { error = "工作本资料版本或数量无效。"; return false; }
            var ids = new HashSet<string>(); var instances = new HashSet<string>();
            foreach (var b in lib.notebooks)
            {
                if (b == null || string.IsNullOrEmpty(b.bookId) || b.bookId == BinderId || !ids.Add(b.bookId) ||
                    string.IsNullOrEmpty(b.physicalInstanceId) || !instances.Add(b.physicalInstanceId) ||
                    !CraftWorkspaceData.Positive(b.pageSizeMeters) || b.pageSizeMeters.x > .3f || b.pageSizeMeters.y > .4f ||
                    b.pages == null || b.pages.Length < 24 || b.pages.Length > 4096 || b.pages.Length % 2 != 0 ||
                    b.currentSpread < 0 || b.currentSpread >= b.pages.Length / 2)
                { error = "TN 本的编号、尺寸或页码无效，未覆盖原存档。"; return false; }
            }
            if (lib.activeBookId != BinderId && !ids.Contains(lib.activeBookId))
            { error = "中央工作本不存在。"; return false; }
            return true;
        }
    }
}

using System.Linq;

namespace VintageJournal
{
    public sealed partial class CraftWorkspaceService
    {
        /// <summary>Turn the inspected TN's own pages without replacing the central working book.</summary>
        public bool SetInspectedNotebookSpread(string bookId, int spread)
        {
            if (HeldItem != null) return Fail("先完成手中的素材。");
            var book = Document.notebookLibrary?.notebooks?.FirstOrDefault(b => b.bookId == bookId);
            if (book == null || book.pages == null || spread < 0 || spread >= book.pages.Length / 2)
                return Fail("查看页码无效。");
            if (book.currentSpread == spread) return true;
            var candidate = Document.DeepClone();
            candidate.notebookLibrary.notebooks.First(b => b.bookId == bookId).currentSpread = spread;
            return Commit(candidate, "查看本子内页", false);
        }
    }
}

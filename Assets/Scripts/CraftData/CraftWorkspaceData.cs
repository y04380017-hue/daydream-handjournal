using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VintageJournal
{
    public enum CraftOwner { Stock, Desktop, JournalPage, ReleaseBookPage, Held, Deleted, WorkBook }
    public enum CraftItemKind { TapeRoll, TapeStrip, Sticker, Note, LoosePage, ReleaseBook, TN, Tool }

    [Serializable]
    public sealed class CraftItemRecord
    {
        public string instanceId, assetId, containerId, pageId;
        public CraftItemKind kind;
        public CraftOwner owner;
        public Vector2 position = new Vector2(.5f, .5f);
        public Vector2 sizeMeters = new Vector2(.04f, .04f);
        public float rotationDegrees, scale = 1, lengthMeters, widthMeters;
        public int sortOrder;
        public Vector3 worldPosition, worldEulerAngles;
        public CraftItemRecord Clone() => JsonUtility.FromJson<CraftItemRecord>(JsonUtility.ToJson(this));
    }

    [Serializable] public sealed class CraftReleasePageData { public string pageId; }
    [Serializable]
    public sealed class CraftReleaseBookData
    {
        public string bookId, displayName;
        public Vector2 pageSizeMeters = new Vector2(.105f, .148f);
        public int currentPage;
        public bool isOpen;
        public List<CraftReleasePageData> pages = new List<CraftReleasePageData>();
    }
    [Serializable]
    public sealed class CraftDrawerState
    { public string id; public bool isOpen; public float openFraction; }
    [Serializable]
    public sealed class CraftPendingTransfer
    {
        public string instanceId;
        // This is a rollback snapshot, never a second owned entity.
        public CraftItemRecord origin;
        public JournalDecorationData originDecoration;
        public bool created;
    }
    [Serializable]
    public sealed class CraftPlacement
    {
        public CraftOwner owner;
        public string containerId, pageId;
        public Vector2 position = new Vector2(.5f, .5f);
        public float rotationDegrees, scale = 1;
        public Vector3 worldPosition, worldEulerAngles;
    }

    /// <summary>Additive, independently versioned section inside the existing atomic journal-v1 snapshot.</summary>
    [Serializable]
    public sealed class CraftWorkspaceData
    {
        public const int CurrentSchemaVersion = 1;
        public const int CurrentDeskLayoutVersion = 2;
        public int schemaVersion = CurrentSchemaVersion;
        // Additive presentation migration; journal-v1 and ownership schema stay unchanged.
        public int deskLayoutVersion;
        public List<CraftItemRecord> items = new List<CraftItemRecord>();
        public List<CraftReleaseBookData> books = new List<CraftReleaseBookData>();
        public List<CraftDrawerState> drawers = new List<CraftDrawerState>();
        public CraftPendingTransfer pending;

        public static CraftWorkspaceData CreateDefault()
        {
            var data = new CraftWorkspaceData();
            foreach (string suffix in new[] { "a", "b" })
            {
                var book = new CraftReleaseBookData { bookId = "release-" + suffix, displayName = "ReleaseBook_" + suffix.ToUpperInvariant() };
                for (int i = 1; i <= 6; i++) book.pages.Add(new CraftReleasePageData { pageId = book.bookId + "-page-" + i.ToString("D3") });
                data.books.Add(book);
            }
            return data;
        }

        public static bool Validate(JournalDocument document, out string message)
        {
            message = string.Empty;
            document?.NormalizeOptionalData();
            var data = document?.craftWorkspace;
            if (data == null) return true; // All previous v1 saves remain valid without this optional section.
            if (data.schemaVersion != CurrentSchemaVersion || data.deskLayoutVersion < 0 || data.deskLayoutVersion > CurrentDeskLayoutVersion || data.items == null || data.books == null || data.drawers == null)
                return Fail(out message, "制作工作区版本或集合无效，保留原存档。");
            if (data.items.Count > 20000 || data.books.Count > 128) return Fail(out message, "制作工作区超过容量上限。");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var bookIds = new HashSet<string>(StringComparer.Ordinal);
            var pageOwners = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var book in data.books)
            {
                if (book == null || string.IsNullOrWhiteSpace(book.bookId) || !bookIds.Add(book.bookId) || !Positive(book.pageSizeMeters) ||
                    book.pages == null || book.pages.Count < 1 || book.currentPage < 0 || book.currentPage >= book.pages.Count)
                    return Fail(out message, "离型本编号、尺寸或页码无效。");
                foreach (var page in book.pages)
                {
                    if (page == null || string.IsNullOrWhiteSpace(page.pageId) || pageOwners.ContainsKey(page.pageId))
                        return Fail(out message, "离型本页面编号重复或缺失。");
                    pageOwners.Add(page.pageId, book.bookId);
                }
            }
            var journalPages = new Dictionary<string, JournalPageData>(StringComparer.Ordinal);
            if (document.pages != null)
                foreach (var page in document.AllPages)
                {
                    if (page == null || string.IsNullOrWhiteSpace(page.pageId) || journalPages.ContainsKey(page.pageId))
                        return Fail(out message, "手账页面实例编号缺失或重复。");
                    journalPages.Add(page.pageId, page);
                }
            var projected = new Dictionary<string, string>(StringComparer.Ordinal);
            if (document.pages != null)
                foreach (var page in document.AllPages)
                    if (page?.decorations != null)
                        foreach (var d in page.decorations)
                        {
                            if (d == null || string.IsNullOrWhiteSpace(d.instanceId) || projected.ContainsKey(d.instanceId))
                                return Fail(out message, "手账装饰实例重复或缺失。");
                            if (d.physicalMaterial && !PhysicalDecorationFits(d)) return Fail(out message, "实体页面素材的物理尺寸或边界无效。");
                            projected.Add(d.instanceId, page.pageId);
                        }
            if (!NotebookLibraryData.Validate(document, out message)) return false;
            var activeNotebook = document.ActiveNotebook;
            var workBooks = data.items.Where(i => i != null && i.owner == CraftOwner.WorkBook).ToArray();
            if (workBooks.Length != (activeNotebook == null ? 0 : 1) ||
                (activeNotebook != null && (workBooks[0].instanceId != activeNotebook.physicalInstanceId || workBooks[0].kind != CraftItemKind.TN)))
                return Fail(out message, "中央本子与物理库存归属不一致。");
            int heldCount = 0;
            var physicalSlots = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in data.items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.instanceId) || !ids.Add(item.instanceId) || string.IsNullOrWhiteSpace(item.assetId) ||
                    !Enum.IsDefined(typeof(CraftOwner), item.owner) || !Enum.IsDefined(typeof(CraftItemKind), item.kind) ||
                    !Positive(item.sizeMeters) || !Finite(item.position) || !Finite(item.rotationDegrees) || !Positive(item.scale) ||
                    !Finite(item.worldPosition) || !Finite(item.worldEulerAngles) || !Finite(item.lengthMeters) || item.lengthMeters < 0 ||
                    !Finite(item.widthMeters) || item.widthMeters < 0)
                    return Fail(out message, "素材实例编号、归属或物理尺寸无效。");
                if (item.owner == CraftOwner.Held) heldCount++;
                if (item.owner == CraftOwner.Stock && item.kind != CraftItemKind.TapeStrip && !string.IsNullOrEmpty(item.containerId) &&
                    !physicalSlots.Add(item.containerId)) return Fail(out message, "同一实体收纳位置被两个物品占用。");
                if (item.owner == CraftOwner.JournalPage)
                {
                    if (journalPages == null || string.IsNullOrEmpty(item.pageId) || !journalPages.ContainsKey(item.pageId) ||
                        !projected.TryGetValue(item.instanceId, out string owner) || owner != item.pageId)
                        return Fail(out message, "手账素材与制作工作区归属不一致。");
                }
                else if (projected.ContainsKey(item.instanceId)) return Fail(out message, "同一素材同时出现在手账和另一容器。");
                if (item.owner == CraftOwner.ReleaseBookPage && (string.IsNullOrEmpty(item.pageId) ||
                    !pageOwners.TryGetValue(item.pageId, out string bookOwner) || bookOwner != item.containerId))
                    return Fail(out message, "离型本素材归属的页面不存在。");
            }
            if (heldCount > 1 || (heldCount == 0 && data.pending != null) ||
                (heldCount == 1 && (data.pending == null || !data.items.Any(i => i.owner == CraftOwner.Held && i.instanceId == data.pending.instanceId))))
                return Fail(out message, "临时持有事务与素材归属不一致。");
            if (data.pending != null && !data.pending.created && (data.pending.origin == null ||
                data.pending.origin.instanceId != data.pending.instanceId || data.pending.origin.owner == CraftOwner.Held))
                return Fail(out message, "临时持有事务没有安全回退位置。");
            if (data.pending?.origin != null && data.pending.origin.owner == CraftOwner.JournalPage &&
                (data.pending.originDecoration == null || data.pending.originDecoration.instanceId != data.pending.instanceId))
                return Fail(out message, "手账揭取事务缺少原始页面装饰快照。");
            var drawerIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var drawer in data.drawers)
                if (drawer == null || string.IsNullOrWhiteSpace(drawer.id) || !drawerIds.Add(drawer.id) ||
                    !Finite(drawer.openFraction) || drawer.openFraction < 0 || drawer.openFraction > 1)
                    return Fail(out message, "抽屉状态无效。");
            return true;
        }
        static bool Fail(out string message, string reason) { message = reason; return false; }
        public static bool PhysicalDecorationFits(JournalDecorationData decoration)
        {
            if (decoration == null || !Positive(decoration.physicalPageSize) || !Positive(decoration.size) || !Finite(decoration.position) ||
                !Finite(decoration.rotationDegrees) || !Finite(decoration.scale) || decoration.scale.x < .1f || decoration.scale.x > 3f ||
                Mathf.Abs(decoration.scale.x - decoration.scale.y) > .0001f || !Positive(decoration.length)) return false;
            Vector2 meters = Vector2.Scale(decoration.size, decoration.physicalPageSize) * decoration.scale.x;
            if (decoration.type == "Tape") meters.x *= decoration.length;
            float radians = decoration.rotationDegrees * Mathf.Deg2Rad;
            float x = (Mathf.Abs(Mathf.Cos(radians)) * meters.x + Mathf.Abs(Mathf.Sin(radians)) * meters.y) * .5f / decoration.physicalPageSize.x;
            float y = (Mathf.Abs(Mathf.Sin(radians)) * meters.x + Mathf.Abs(Mathf.Cos(radians)) * meters.y) * .5f / decoration.physicalPageSize.y;
            return decoration.position.x - x >= .002f && decoration.position.x + x <= .998f &&
                decoration.position.y - y >= .002f && decoration.position.y + y <= .998f;
        }
        public static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        public static bool Finite(Vector2 v) => Finite(v.x) && Finite(v.y);
        public static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        public static bool Positive(float v) => Finite(v) && v > 0;
        public static bool Positive(Vector2 v) => Positive(v.x) && Positive(v.y);
    }
}

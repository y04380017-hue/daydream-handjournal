using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VintageJournal
{
    /// <summary>
    /// Ownership transactions contain the complete journal, registry, release books and pending rollback in one
    /// existing save snapshot. Accepted snapshots enter the existing single-writer queue before live state changes.
    /// </summary>
    public sealed partial class CraftWorkspaceService : IDisposable
    {
        readonly Func<JournalDocument> read;
        readonly Action<JournalDocument> apply;
        readonly Func<JournalDocument, bool> save;
        readonly Func<bool> flush;
        readonly Func<Vector2> journalMetrics;
        readonly Vector2 binderPageSize;
        readonly DecorationCatalog catalog;
        readonly ScrapbookController scrapbook;
        readonly CraftCommandHistory history = new CraftCommandHistory();
        string transferBefore;
        bool applying;
        public event Action DataChanged;
        public string LastMessage { get; private set; } = "制作工作区已准备。";
        public JournalDocument Document => read();
        public CraftWorkspaceData Data => Document.craftWorkspace;
        public IReadOnlyList<CraftItemRecord> Items => Data.items;
        public IReadOnlyList<CraftReleaseBookData> Books => Data.books;
        public CraftItemRecord HeldItem => Data.items.FirstOrDefault(i => i.owner == CraftOwner.Held);
        public int UndoCount => history.UndoCount;
        public int RedoCount => history.RedoCount;
        public double LastCommitMilliseconds { get; private set; }
        public double MaxCommitMilliseconds { get; private set; }
        public double LastSavePreparationMilliseconds { get; private set; }
        public double MaxSavePreparationMilliseconds { get; private set; }
        public long CommitCount { get; private set; }

        public CraftWorkspaceService(JournalController journal, ScrapbookController scrapbook)
        {
            if (!journal || journal.Document == null) throw new ArgumentException("Journal must finish loading before the craft workspace.");
            read = () => journal.Document;
            apply = journal.ApplyWorkspaceDocument;
            save = doc => scrapbook ? scrapbook.QueueSave(scrapbook.DocumentForSave(journal.DocumentForWorkspaceSave(doc))) : journal.Store.Save(journal.DocumentForWorkspaceSave(doc));
            flush = () => scrapbook ? scrapbook.FlushSaves() : true;
            journalMetrics = () => JournalMeters(journal);
            binderPageSize = new Vector2(.148f, .210f);
            this.scrapbook = scrapbook;
            catalog = scrapbook && scrapbook.Catalog ? scrapbook.Catalog : DecorationCatalog.Load();
            Initialize();
            if (scrapbook) { scrapbook.CraftService = this; scrapbook.History.CommandRecorded += ExternalEditRecorded; }
        }

        /// <summary>Deterministic storage seam for data tests; persist receives the complete candidate snapshot.</summary>
        public CraftWorkspaceService(JournalDocument document, Func<JournalDocument, bool> persist, Vector2 journalPageMeters, DecorationCatalog catalog = null)
        {
            JournalDocument current = document ?? throw new ArgumentNullException(nameof(document));
            read = () => current; apply = doc => current = doc;
            save = persist ?? (_ => true); flush = () => true;
            journalMetrics = () => journalPageMeters; this.catalog = catalog;
            binderPageSize = journalPageMeters;
            Initialize();
        }

        void Initialize()
        {
            var candidate = Document.DeepClone();
            if (candidate.craftWorkspace == null) candidate.craftWorkspace = CraftWorkspaceData.CreateDefault();
            candidate.craftWorkspaceEnabled = true;
            if (!CraftWorkspaceData.Validate(candidate, out string error)) throw new InvalidOperationException(error);
            if (candidate.craftWorkspace.pending != null)
            {
                RestorePending(candidate); // Restart cancels a half-finished gesture; exactly one source survives.
                LastMessage = "已恢复上次未完成的素材取放。";
            }
            if (!SynchronizeJournal(candidate, out error) || !Commit(candidate, "初始化制作工作区", false))
                throw new InvalidOperationException(string.IsNullOrEmpty(error) ? LastMessage : error);
        }

        public CraftItemRecord GetItem(string id) => Data.items.FirstOrDefault(i => i.instanceId == id);
        public CraftReleaseBookData FindBook(string id) => Data.books.FirstOrDefault(b => b.bookId == id);
        public IReadOnlyList<CraftItemRecord> GetPageItems(CraftOwner owner, string containerId, string pageId)
        { return Data.items.Where(i => i.owner == owner && i.pageId == pageId && (string.IsNullOrEmpty(containerId) || i.containerId == containerId)).OrderBy(i => i.sortOrder).ToArray(); }
        public CraftReleasePageData GetPage(string bookId, string pageId)
        { return FindBook(bookId)?.pages.FirstOrDefault(p => p.pageId == pageId); }

        public bool RegisterPhysical(string id, string assetId, CraftItemKind kind, string containerId, Pose worldPose, Vector2 sizeMeters)
        {
            return RegisterPhysical(new[] { new CraftItemRecord { instanceId = id, assetId = assetId, kind = kind, owner = CraftOwner.Stock,
                containerId = containerId, worldPosition = worldPose.position, worldEulerAngles = worldPose.rotation.eulerAngles, sizeMeters = sizeMeters } });
        }
        public bool RegisterPhysical(IEnumerable<CraftItemRecord> records)
        {
            if (records == null) return Fail("没有需要登记的实体物品。");
            var candidate = Document.DeepClone(); bool changed = false;
            foreach (var record in records)
            {
                if (record == null) return Fail("实体登记记录为空。");
                var existing = candidate.craftWorkspace.items.FirstOrDefault(i => i.instanceId == record.instanceId);
                if (existing != null)
                { if (existing.assetId != record.assetId || existing.kind != record.kind) return Fail("实体编号与已保存素材不一致。"); continue; }
                candidate.craftWorkspace.items.Add(record.Clone()); changed = true;
            }
            return !changed || Commit(candidate, "登记实体库存", false);
        }
        public bool UpdatePhysicalLocation(string id, string containerId, Pose pose, bool stored)
        {
            if (HeldItem != null) return Fail("请先放下正在制作的素材。");
            var candidate = Document.DeepClone(); var record = Find(candidate, id);
            if (record == null || (record.owner != CraftOwner.Stock && record.owner != CraftOwner.Desktop)) return Fail("物品不在实体工作区中。");
            record.owner = stored ? CraftOwner.Stock : CraftOwner.Desktop; record.containerId = containerId;
            record.worldPosition = pose.position; record.worldEulerAngles = pose.rotation.eulerAngles; record.pageId = null;
            return Commit(candidate, "移动实体物品", false);
        }
        public CraftDrawerState GetDrawerState(string id) => Data.drawers.FirstOrDefault(d => d.id == id);
        public bool SetDrawerState(string id, bool isOpen, float openFraction)
        {
            if (string.IsNullOrEmpty(id) || !CraftWorkspaceData.Finite(openFraction)) return Fail("抽屉状态无效。");
            var old = GetDrawerState(id); float fraction = Mathf.Clamp01(openFraction);
            if (old != null && old.isOpen == isOpen && Mathf.Abs(old.openFraction - fraction) < .0001f) return true;
            var candidate = Document.DeepClone(); var state = candidate.craftWorkspace.drawers.FirstOrDefault(d => d.id == id);
            if (state == null) { state = new CraftDrawerState { id = id }; candidate.craftWorkspace.drawers.Add(state); }
            state.isOpen = isOpen; state.openFraction = fraction; return Commit(candidate, "保存抽屉状态", false);
        }

        public bool BeginStock(string id)
        {
            var item = GetItem(id);
            if (item == null || (item.owner != CraftOwner.Stock && item.owner != CraftOwner.Desktop) ||
                (item.kind != CraftItemKind.Sticker && item.kind != CraftItemKind.Note && item.kind != CraftItemKind.TapeStrip))
                return Fail("请选择可粘贴的单张素材。");
            return BeginTransfer(id);
        }
        public bool BeginPeel(string id)
        {
            var item = GetItem(id);
            if (item == null || (item.owner != CraftOwner.JournalPage && item.owner != CraftOwner.ReleaseBookPage)) return Fail("素材没有贴在有效页面上。");
            return BeginTransfer(id);
        }
        bool BeginTransfer(string id)
        {
            if (HeldItem != null) return Fail("当前已经持有一件素材。");
            if (scrapbook && scrapbook.IsTransforming) return Fail("请先完成当前页面编辑。");
            var candidate = Document.DeepClone(); var item = Find(candidate, id);
            string before = JsonUtility.ToJson(candidate);
            candidate.craftWorkspace.pending = new CraftPendingTransfer { instanceId = id, origin = item.Clone() };
            if (item.owner == CraftOwner.JournalPage)
            {
                var original = candidate.AllPages.SelectMany(p => p.decorations).First(d => d.instanceId == id);
                candidate.craftWorkspace.pending.originDecoration = JsonUtility.FromJson<JournalDecorationData>(JsonUtility.ToJson(original));
            }
            RemoveProjection(candidate, id); item.owner = CraftOwner.Held; item.pageId = null; item.containerId = null;
            if (!Commit(candidate, "揭取素材", false)) return false;
            transferBefore = before; return true;
        }
        public bool CreateTapeStrip(string assetId, string sourceId, float lengthMeters, float widthMeters = .015f)
        {
            if (HeldItem != null || !CraftWorkspaceData.Finite(lengthMeters) || lengthMeters < .02f || lengthMeters > .30f ||
                !CraftWorkspaceData.Positive(widthMeters) || widthMeters > .05f) return Fail("胶带长度必须为 2–30 cm，宽度必须有效。");
            var source = GetItem(sourceId);
            if (source == null || source.kind != CraftItemKind.TapeRoll || source.assetId != assetId ||
                (source.owner != CraftOwner.Stock && source.owner != CraftOwner.Desktop)) return Fail("胶带卷来源无效。");
            if (catalog && (catalog.Find(assetId) == null || !catalog.Find(assetId).IsAvailable)) return Fail("胶带素材不可用。");
            var candidate = Document.DeepClone(); string before = JsonUtility.ToJson(candidate);
            var item = new CraftItemRecord { instanceId = "tape-strip-" + Guid.NewGuid().ToString("N"), assetId = assetId,
                kind = CraftItemKind.TapeStrip, owner = CraftOwner.Held, sizeMeters = new Vector2(lengthMeters, widthMeters),
                lengthMeters = lengthMeters, widthMeters = widthMeters, worldPosition = source.worldPosition };
            candidate.craftWorkspace.items.Add(item);
            candidate.craftWorkspace.pending = new CraftPendingTransfer { instanceId = item.instanceId, created = true };
            if (!Commit(candidate, "裁切胶带", false)) return false;
            transferBefore = before; return true;
        }
        public bool SetHeldTransform(float rotationDegrees, float scale)
        {
            if (HeldItem == null || !CraftWorkspaceData.Finite(rotationDegrees) || !ValidScale(scale)) return Fail("持有素材的角度或缩放无效。");
            var candidate = Document.DeepClone(); var item = Find(candidate, HeldItem.instanceId);
            item.rotationDegrees = NormalizeAngle(rotationDegrees); item.scale = scale;
            return Commit(candidate, "调整持有素材", false);
        }
        public bool CommitHeld(CraftPlacement placement)
        {
            if (HeldItem == null || placement == null) return Fail("当前没有待放置素材。");
            var candidate = Document.DeepClone(); var item = Find(candidate, HeldItem.instanceId);
            item.owner = placement.owner; item.containerId = placement.containerId; item.pageId = placement.pageId;
            item.position = placement.position; item.rotationDegrees = NormalizeAngle(placement.rotationDegrees); item.scale = placement.scale;
            item.worldPosition = placement.worldPosition; item.worldEulerAngles = placement.worldEulerAngles;
            if (item.owner != CraftOwner.JournalPage && item.owner != CraftOwner.ReleaseBookPage && item.owner != CraftOwner.Desktop)
                return Fail("该位置不能接收待贴素材。");
            if (item.owner == CraftOwner.ReleaseBookPage && item.kind != CraftItemKind.TapeStrip && item.kind != CraftItemKind.Sticker && item.kind != CraftItemKind.Note)
                return Fail("离型本只接收可粘贴素材。");
            if ((item.owner == CraftOwner.JournalPage || item.owner == CraftOwner.ReleaseBookPage) && !Fits(candidate, item))
                return Fail("素材超出页面，请调整位置、旋转或缩放后再放置。");
            item.sortOrder = candidate.craftWorkspace.items.Where(i => i.instanceId != item.instanceId && i.owner == item.owner &&
                i.containerId == item.containerId && i.pageId == item.pageId).Select(i => i.sortOrder).DefaultIfEmpty(-1).Max() + 1;
            candidate.craftWorkspace.pending = null;
            Project(candidate, item);
            string before = transferBefore ?? BeforePending(Document);
            if (!Commit(candidate, "转移素材", false)) return false;
            history.Record(before, JsonUtility.ToJson(candidate), "转移素材"); transferBefore = null; return true;
        }
        public bool CancelHeld()
        {
            if (HeldItem == null) return false;
            var candidate = Document.DeepClone(); RestorePending(candidate);
            if (!Commit(candidate, "取消素材取放", false)) return false;
            transferBefore = null; return true;
        }

        public bool MoveAttached(string id, Vector2 position, float rotationDegrees)
        { return EditAttached(id, i => { i.position = position; i.rotationDegrees = NormalizeAngle(rotationDegrees); }, "移动素材"); }
        public bool RotateAttached(string id, float rotationDegrees)
        { return EditAttached(id, i => i.rotationDegrees = NormalizeAngle(rotationDegrees), "旋转素材"); }
        public bool ScaleAttached(string id, float scale)
        { return EditAttached(id, i => i.scale = scale, "缩放素材"); }
        bool EditAttached(string id, Action<CraftItemRecord> edit, string label)
        {
            if (HeldItem != null) return Fail("请先放下持有的素材。");
            var candidate = Document.DeepClone(); var item = Find(candidate, id);
            if (!Attached(item)) return Fail("请选择页面上的素材。");
            edit(item); if (!Fits(candidate, item)) return Fail("调整结果超出页面，已保留原位置和尺寸。");
            Project(candidate, item); return Commit(candidate, label, true);
        }
        public bool DeleteAttached(string id)
        {
            if (HeldItem != null) return Fail("请先放下持有的素材。");
            var candidate = Document.DeepClone(); var item = Find(candidate, id);
            if (!Attached(item)) return Fail("只能删除已贴在页面上的素材。");
            item.owner = CraftOwner.Deleted; item.pageId = null; item.containerId = null;
            RemoveProjection(candidate, id); return Commit(candidate, "删除素材", true);
        }
        public bool LayerAttached(string id, string operation)
        {
            if (HeldItem != null) return false;
            var candidate = Document.DeepClone(); var item = Find(candidate, id);
            if (!Attached(item)) return Fail("请选择页面上的素材。");
            var ordered = candidate.craftWorkspace.items.Where(i => i.owner == item.owner && i.containerId == item.containerId && i.pageId == item.pageId)
                .OrderBy(i => i.sortOrder).ThenBy(i => i.instanceId).ToList();
            int from = ordered.IndexOf(item), to = from;
            if (operation == "top") to = ordered.Count - 1; else if (operation == "bottom") to = 0;
            else if (operation == "up") to = Mathf.Min(ordered.Count - 1, from + 1); else if (operation == "down") to = Mathf.Max(0, from - 1);
            else return Fail("未知图层操作。");
            ordered.RemoveAt(from); ordered.Insert(to, item);
            for (int i = 0; i < ordered.Count; i++) { ordered[i].sortOrder = i; Project(candidate, ordered[i]); }
            return Commit(candidate, "调整素材图层", true);
        }

        public bool SetBookOpen(string bookId, bool isOpen)
        {
            if (HeldItem != null) return Fail("持有素材时请先完成放置。");
            var candidate = Document.DeepClone(); var book = candidate.craftWorkspace.books.FirstOrDefault(b => b.bookId == bookId);
            if (book == null) return false; book.isOpen = isOpen; return Commit(candidate, isOpen ? "打开离型本" : "合上离型本", false);
        }
        public bool SetBookPage(string bookId, int pageIndex)
        {
            var candidate = Document.DeepClone(); var book = candidate.craftWorkspace.books.FirstOrDefault(b => b.bookId == bookId);
            if (book == null || pageIndex < 0 || pageIndex >= book.pages.Count) return false;
            book.currentPage = pageIndex; return Commit(candidate, "离型本翻页", false);
        }
        public bool Undo() { if (HeldItem != null) return CancelHeld(); return ApplyHistory(false); }
        public bool Redo() { return HeldItem == null && ApplyHistory(true); }
        bool ApplyHistory(bool isRedo)
        {
            var command = history.Peek(isRedo); if (command == null) return false;
            var candidate = JsonUtility.FromJson<JournalDocument>(isRedo ? command.after : command.before);
            // Physical movement and mechanism navigation intentionally do not enter material history.
            // Preserve their current records while replaying material ownership across the journal/books.
            candidate.craftWorkspace.items.RemoveAll(i => IndependentPhysical(i.kind));
            candidate.craftWorkspace.items.AddRange(Data.items.Where(i => IndependentPhysical(i.kind)).Select(i => i.Clone()));
            candidate.craftWorkspace.drawers = Data.drawers.Select(d => JsonUtility.FromJson<CraftDrawerState>(JsonUtility.ToJson(d))).ToList();
            candidate.craftWorkspace.deskLayoutVersion = Data.deskLayoutVersion;
            candidate.currentSpread = Document.currentSpread;
            if (candidate.notebookLibrary != null && Document.notebookLibrary != null)
            {
                candidate.notebookLibrary.activeBookId = Document.notebookLibrary.activeBookId;
                foreach (var book in candidate.notebookLibrary.notebooks)
                { var current = Document.notebookLibrary.notebooks.FirstOrDefault(b => b.bookId == book.bookId); if (current != null) book.currentSpread = current.currentSpread; }
            }
            // Sent letters are irreversible; material undo never rewinds the commission branch.
            candidate.commissionDesk = Document.commissionDesk?.Clone(); candidate.commissionDeskEnabled = Document.commissionDeskEnabled;
            // Collection and rewards are independent events. Preserve their ledger and any instances
            // earned after the historical snapshot; existing instances still replay normal ownership edits.
            candidate.dreamGameplay = Document.dreamGameplay?.Clone(); candidate.dreamGameplayEnabled = Document.dreamGameplayEnabled;
            candidate.atelierProgress = Document.atelierProgress?.Clone(); candidate.atelierProgressEnabled = Document.atelierProgressEnabled;
            candidate.personalStudio = Document.personalStudio?.Clone(); candidate.personalStudioEnabled = Document.personalStudioEnabled;
            candidate.atelierSocial = Document.atelierSocial?.Clone(); candidate.atelierSocialEnabled = Document.atelierSocialEnabled;
            var historicalIds = new HashSet<string>(candidate.craftWorkspace.items.Select(i => i.instanceId));
            candidate.craftWorkspace.items.AddRange(Data.items.Where(i => IsDreamInstance(i) && !historicalIds.Contains(i.instanceId)).Select(i => i.Clone()));
            foreach (var book in candidate.craftWorkspace.books)
            { var current = FindBook(book.bookId); if (current != null) { book.currentPage = current.currentPage; book.isOpen = current.isOpen; } }
            if (!Commit(candidate, isRedo ? "重做制作操作" : "撤销制作操作", false)) return false;
            history.Complete(isRedo); return true;
        }
        public bool Flush() => flush();

        public bool InitializeNotebooks(IEnumerable<NotebookData> definitions, string paperId)
        {
            var candidate = Document.DeepClone(); bool changed = candidate.notebookLibrary == null;
            if (changed) candidate.notebookLibrary = new NotebookLibraryData();
            candidate.notebookLibraryEnabled = true;
            foreach (var definition in definitions)
            {
                if (candidate.notebookLibrary.notebooks.Any(b => b.bookId == definition.bookId)) continue;
                var book = JsonUtility.FromJson<NotebookData>(JsonUtility.ToJson(definition));
                book.pages = Enumerable.Range(1, 24).Select(i => new JournalPageData {
                    pageId = book.bookId + "-page-" + i.ToString("D3"), paperId = paperId }).ToArray();
                candidate.notebookLibrary.notebooks.Add(book); changed = true;
            }
            return !changed || Commit(candidate, "准备独立旅行手账", false);
        }

        /// <summary>Changing the central book is navigation; decorations never change ownership or get cloned.</summary>
        public bool SwitchNotebook(string bookId, string previousHomeSlot, Pose previousHomePose)
        {
            if (HeldItem != null || Document.notebookLibrary == null) return Fail("请先结束素材取放，再更换本子。");
            if (bookId == Document.ActiveBookId) return true;
            var candidate = Document.DeepClone();
            var target = candidate.notebookLibrary.notebooks.FirstOrDefault(b => b.bookId == bookId);
            if (bookId != NotebookLibraryData.BinderId && target == null) return Fail("未找到这本旅行手账。");
            var next = target != null ? Find(candidate, target.physicalInstanceId) : null;
            if (target != null && (next == null || (next.owner != CraftOwner.Stock && next.owner != CraftOwner.Desktop)))
                return Fail("旅行手账暂时不能取用。");
            var old = candidate.ActiveNotebook;
            if (old != null)
            {
                if (string.IsNullOrEmpty(previousHomeSlot)) return Fail("原本子的归位位置无效。");
                var record = Find(candidate, old.physicalInstanceId);
                if (record == null) return Fail("原本子库存记录缺失。");
                if (candidate.craftWorkspace.items.Any(i => i.instanceId != record.instanceId && i.owner == CraftOwner.Stock && i.containerId == previousHomeSlot))
                    return Fail("原本子的架位已经被占用。");
                record.owner = CraftOwner.Stock; record.containerId = previousHomeSlot;
                record.worldPosition = previousHomePose.position; record.worldEulerAngles = previousHomePose.rotation.eulerAngles;
            }
            if (next != null) { next.owner = CraftOwner.WorkBook; next.containerId = "central-workbook"; next.pageId = null; }
            candidate.notebookLibrary.activeBookId = bookId;
            return Commit(candidate, "更换中央工作本", false);
        }

        /// <summary>Commits an optional document branch (e.g. commission letters) through the same validated atomic save queue.</summary>
        public bool CommitCommission(Action<CommissionDeskData> mutate, string label)
        {
            if (mutate == null) return Fail("委托信更新为空。");
            var candidate = Document.DeepClone();
            if (candidate.commissionDesk == null) candidate.commissionDesk = CommissionDeskData.CreateDefault();
            candidate.commissionDeskEnabled = true;
            mutate(candidate.commissionDesk);
            ApplyNightRewards(candidate);
            if (!CommissionDeskData.Validate(candidate, out string error)) return Fail(error);
            return Commit(candidate, label, false);
        }

        bool Commit(JournalDocument candidate, string label, bool recordHistory)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
            if (!CraftWorkspaceData.Validate(candidate, out string error) || !DreamGameplayData.Validate(candidate, out error)) return Fail(error);
            if (!AtelierProgressData.Validate(candidate, out error) || !PersonalStudioData.Validate(candidate, out error) ||
                !AtelierSocialData.Validate(candidate, out error)) return Fail(error);
            string before = recordHistory ? JsonUtility.ToJson(Document) : null;
            bool accepted = save(candidate);
            LastSavePreparationMilliseconds = timer.Elapsed.TotalMilliseconds;
            MaxSavePreparationMilliseconds = Math.Max(MaxSavePreparationMilliseconds, LastSavePreparationMilliseconds);
            if (!accepted) return Fail("保存请求未被接受，已保留操作前的数据。");
            applying = true;
            try { apply(candidate); }
            finally { applying = false; }
            if (recordHistory) history.Record(before, JsonUtility.ToJson(candidate), label);
            LastMessage = label + "。"; DataChanged?.Invoke();
            Debug.Log("[Craft] " + label + "; held=" + (HeldItem?.instanceId ?? "none"));
            return true;
            }
            finally
            {
                LastCommitMilliseconds = timer.Elapsed.TotalMilliseconds;
                MaxCommitMilliseconds = Math.Max(MaxCommitMilliseconds, LastCommitMilliseconds);
                CommitCount++;
            }
        }
        bool Fail(string reason) { LastMessage = reason; return false; }
        bool Fits(JournalDocument doc, CraftItemRecord item)
        {
            if (!CraftWorkspaceData.Finite(item.position) || !CraftWorkspaceData.Finite(item.rotationDegrees) || !ValidScale(item.scale)) return false;
            Vector2 size;
            if (item.owner == CraftOwner.JournalPage)
            { if (!doc.AllPages.Any(p => p.pageId == item.pageId)) return false; size = doc.PageMetres(item.pageId, binderPageSize); item.containerId = "journal"; }
            else
            {
                var book = doc.craftWorkspace.books.FirstOrDefault(b => b.bookId == item.containerId);
                if (book == null || !book.pages.Any(p => p.pageId == item.pageId)) return false; size = book.pageSizeMeters;
            }
            if (!CraftWorkspaceData.Positive(size)) return false;
            float radians = item.rotationDegrees * Mathf.Deg2Rad;
            Vector2 meters = item.sizeMeters * item.scale;
            float x = (Mathf.Abs(Mathf.Cos(radians)) * meters.x + Mathf.Abs(Mathf.Sin(radians)) * meters.y) * .5f / size.x;
            float y = (Mathf.Abs(Mathf.Sin(radians)) * meters.x + Mathf.Abs(Mathf.Cos(radians)) * meters.y) * .5f / size.y;
            return item.position.x - x >= .002f && item.position.x + x <= .998f && item.position.y - y >= .002f && item.position.y + y <= .998f;
        }
        void Project(JournalDocument doc, CraftItemRecord item)
        {
            RemoveProjection(doc, item.instanceId);
            if (item.owner != CraftOwner.JournalPage) return;
            var page = doc.AllPages.First(p => p.pageId == item.pageId); Vector2 metric = doc.PageMetres(item.pageId, binderPageSize);
            page.decorations.Add(new JournalDecorationData { instanceId = item.instanceId, assetId = item.assetId, pageId = item.pageId,
                type = item.kind == CraftItemKind.TapeStrip ? "Tape" : item.kind.ToString(), position = item.position,
                size = new Vector2(item.sizeMeters.x / metric.x, item.sizeMeters.y / metric.y), scale = Vector2.one * item.scale,
                rotationDegrees = item.rotationDegrees, sortOrder = item.sortOrder, length = 1, lengthMeters = item.lengthMeters,
                physicalMaterial = true, physicalPageSize = metric });
        }
        static void RemoveProjection(JournalDocument doc, string id)
        { foreach (var page in doc.AllPages) page.decorations.RemoveAll(d => d.instanceId == id); }
        static CraftItemRecord Find(JournalDocument doc, string id) => doc.craftWorkspace.items.FirstOrDefault(i => i.instanceId == id);
        static bool Attached(CraftItemRecord item) => item != null && (item.owner == CraftOwner.JournalPage || item.owner == CraftOwner.ReleaseBookPage);
        static bool IndependentPhysical(CraftItemKind kind) => kind == CraftItemKind.TapeRoll || kind == CraftItemKind.LoosePage ||
            kind == CraftItemKind.ReleaseBook || kind == CraftItemKind.TN || kind == CraftItemKind.Tool;
        static bool ValidScale(float value) => CraftWorkspaceData.Finite(value) && value >= .1f && value <= 3f;
        static float NormalizeAngle(float value) => CraftWorkspaceData.Finite(value) ? Mathf.Repeat(value + 180, 360) - 180 : value;
        static Vector2 JournalMeters(JournalController journal)
        {
            if (!journal.pageView) return new Vector2(.148f, .210f);
            Transform surface = journal.pageView.GetSurface(JournalSide.Left); Vector2 local = journal.pageView.SurfaceSize(JournalSide.Left);
            if (!surface) return new Vector2(.148f, .210f);
            return new Vector2(surface.TransformVector(Vector3.right * local.x).magnitude, surface.TransformVector(Vector3.up * local.y).magnitude);
        }
        static string BeforePending(JournalDocument document)
        { var before = document.DeepClone(); RestorePending(before); return JsonUtility.ToJson(before); }
        static void RestorePending(JournalDocument doc)
        {
            var pending = doc.craftWorkspace.pending; if (pending == null) return;
            doc.craftWorkspace.items.RemoveAll(i => i.instanceId == pending.instanceId);
            if (!pending.created && pending.origin != null)
            {
                var origin = pending.origin.Clone(); doc.craftWorkspace.items.Add(origin);
                if (origin.owner == CraftOwner.JournalPage)
                {
                    // The full legacy projection is reconstructed by the persisted dimensions and original transform.
                    var page = doc.AllPages.FirstOrDefault(p => p.pageId == origin.pageId);
                    if (page != null)
                    {
                        page.decorations.RemoveAll(d => d.instanceId == origin.instanceId);
                        page.decorations.Add(JsonUtility.FromJson<JournalDecorationData>(JsonUtility.ToJson(pending.originDecoration)));
                    }
                }
            }
            doc.craftWorkspace.pending = null;
        }

        public bool SynchronizeJournalProjection()
        {
            var candidate = Document.DeepClone();
            if (!SynchronizeJournal(candidate, out string error)) return Fail(error);
            Document.craftWorkspace = candidate.craftWorkspace; return true;
        }
        bool SynchronizeJournal(JournalDocument doc, out string error)
        {
            error = string.Empty;
            var rows = doc.AllPages.SelectMany(p => p.decorations.Select(d => new { page = p, decoration = d })).ToList();
            var present = new HashSet<string>(rows.Select(r => r.decoration.instanceId), StringComparer.Ordinal);
            foreach (var missing in doc.craftWorkspace.items.Where(i => i.owner == CraftOwner.JournalPage && !present.Contains(i.instanceId)))
            { missing.owner = CraftOwner.Deleted; missing.pageId = null; missing.containerId = null; }
            foreach (var row in rows)
            {
                Vector2 metric = doc.PageMetres(row.page.pageId, binderPageSize);
                var d = row.decoration; var item = Find(doc, d.instanceId);
                if (item != null && item.owner != CraftOwner.JournalPage && item.owner != CraftOwner.Deleted) { error = "旧编辑操作试图复制已转移素材，操作未被保存。"; return false; }
                if (item == null) { item = new CraftItemRecord { instanceId = d.instanceId }; doc.craftWorkspace.items.Add(item); }
                var definition = catalog ? catalog.Find(d.assetId) : null;
                Vector2 baseSize = d.size.x > 0 && d.size.y > 0 ? d.size : definition != null ? definition.defaultSize : new Vector2(.2f, .2f);
                float length = (definition != null && definition.supportsLengthAdjustment) || d.type == "Tape" ? Mathf.Max(.001f, d.length) : 1;
                item.assetId = d.assetId; item.kind = d.type == "Tape" ? CraftItemKind.TapeStrip : d.type == "Note" ? CraftItemKind.Note : CraftItemKind.Sticker;
                item.owner = CraftOwner.JournalPage; item.containerId = "journal"; item.pageId = row.page.pageId;
                item.position = d.position; item.rotationDegrees = d.rotationDegrees; item.scale = d.scale.x;
                item.sizeMeters = Vector2.Scale(baseSize, metric); item.sizeMeters.x *= length;
                item.sortOrder = d.sortOrder; item.lengthMeters = d.lengthMeters;
                item.widthMeters = item.kind == CraftItemKind.TapeStrip ? item.sizeMeters.y : 0;
            }
            return true;
        }
        void ExternalEditRecorded(JournalPageData page, List<JournalDecorationData> before, string label)
        {
            if (applying || Data.pending != null) return;
            var after = Document.DeepClone();
            if (!SynchronizeJournal(after, out string error)) { Fail(error); return; }
            var previous = after.DeepClone();
            previous.AllPages.First(p => p.pageId == page.pageId).decorations = DecorationHistory.Clone(before);
            if (!SynchronizeJournal(previous, out error)) { Fail(error); return; }
            // Copy only the registry; page list references currently used by the digital editor stay valid.
            Document.craftWorkspace = after.craftWorkspace;
            history.Record(previous, after, label);
            DataChanged?.Invoke();
        }
        public void Dispose()
        {
            if (scrapbook)
            { scrapbook.History.CommandRecorded -= ExternalEditRecorded; if (scrapbook.CraftService == this) scrapbook.CraftService = null; }
        }
    }
}

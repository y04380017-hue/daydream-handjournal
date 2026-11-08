using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using UnityEngine;

namespace VintageJournal
{
    /// <summary>Versioned atomic JSON persistence, independent from scene, camera and UI state.</summary>
    public sealed class JournalSaveService
    {
        const long MaximumFileBytes = 8L * 1024L * 1024L;
        readonly JournalPaperCatalog catalog;
        [Serializable] sealed class SaveHeader { public int schemaVersion; }
        volatile bool writeBlocked;
        readonly object commitLock = new object();
        readonly object stateLock = new object();
        readonly object snapshotOwner = new object();
        readonly int ownerThreadId;
        long preparedSequence, committedSequence;
        string lastMessage = string.Empty, preservedFilePath = string.Empty, compatibleWarning = string.Empty, lastCommittedJson;
        bool usedCompatibleFileCommit, lastCommitSucceeded, lastCommitSkipped;
        public string SavePath { get; }
        public string LastMessage { get { lock (stateLock) return lastMessage; } private set { lock (stateLock) lastMessage = value; } }
        public string PreservedFilePath { get { lock (stateLock) return preservedFilePath; } private set { lock (stateLock) preservedFilePath = value; } }
        public bool UsedCompatibleFileCommit { get { lock (stateLock) return usedCompatibleFileCommit; } private set { lock (stateLock) usedCompatibleFileCommit = value; } }
        public string LastCommittedJson { get { lock (stateLock) return lastCommittedJson; } }

        /// <summary>Immutable main-thread validation/serialization result. Contains no document or asset references.</summary>
        public sealed class PreparedSnapshot
        {
            internal readonly object OwnerToken;
            internal readonly string Warning;
            public string Json { get; }
            public long Sequence { get; }
            internal PreparedSnapshot(object ownerToken, string json, string warning, long sequence)
            { OwnerToken = ownerToken; Json = json; Warning = warning; Sequence = sequence; }
        }
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool MoveFileEx(string source, string destination, int flags);
#endif

        public JournalSaveService(JournalPaperCatalog catalog, string path = null)
        {
            ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            this.catalog = catalog ? catalog : throw new ArgumentNullException(nameof(catalog));
            if (catalog.papers == null || catalog.papers.Length == 0)
                throw new ArgumentException("手账内页目录为空。", nameof(catalog));
            var paperIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var definition in catalog.papers)
                if (definition == null || string.IsNullOrWhiteSpace(definition.id) || !paperIds.Add(definition.id))
                    throw new ArgumentException("手账内页目录有缺失或重复的素材编号。", nameof(catalog));
            string requested = string.IsNullOrEmpty(path)
                ? Path.Combine(Application.persistentDataPath, "journal-v1.json") : path;
            if (!Path.IsPathRooted(requested))
                throw new ArgumentException("存档位置必须为明确的绝对路径。", nameof(path));
            SavePath = Path.GetFullPath(requested);
        }

        public JournalDocument LoadOrCreate()
        {
            writeBlocked = false;
            lock (stateLock) lastCommittedJson = null;
            PreservedFilePath = string.Empty;
            if (!File.Exists(SavePath))
            {
                if (File.Exists(SavePath + ".backup"))
                {
                    try
                    {
                        var restored = ReadValidated(SavePath + ".backup", out string restoredMessage);
                        LastMessage = "主存档缺失，已恢复上一次成功保存的备份。" + restoredMessage;
                        Debug.LogWarning("[Journal] Save:BackupRecovered:" + LastMessage);
                        return restored;
                    }
                    catch (Exception backupError) when (backupError is IOException || backupError is UnauthorizedAccessException ||
                        backupError is ArgumentException || backupError is InvalidDataException) { }
                }
                var fresh = JournalDocument.CreateDefault(catalog);
                if (Save(fresh)) LastMessage = "已创建新的手账。";
                return fresh;
            }
            try
            {
                var loaded = ReadValidated(SavePath, out string message);
                LastMessage = message;
                return loaded;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
                error is ArgumentException || error is InvalidDataException)
            {
                string reason = "原存档无法读取。";
                try
                {
                    string suffix = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                    PreservedFilePath = SavePath + "." + suffix + ".broken";
                    File.Move(SavePath, PreservedFilePath);
                    reason += "原文件已保留，可从损坏存档副本中找回内容。";
                }
                catch (Exception preserveError) when (preserveError is IOException || preserveError is UnauthorizedAccessException)
                {
                    writeBlocked = true;
                    PreservedFilePath = string.Empty;
                    reason += "原文件暂时无法备份，本次不会覆盖它；请检查目录写入权限。";
                }
                if (File.Exists(SavePath + ".backup"))
                {
                    try
                    {
                        var recovered = ReadValidated(SavePath + ".backup", out string backupMessage);
                        LastMessage = "主要存档无法读取，已恢复上一次成功保存的备份。" + reason + backupMessage;
                        Debug.LogWarning("[Journal] Save:BackupRecovered:" + LastMessage);
                        return recovered;
                    }
                    catch (Exception backupError) when (backupError is IOException || backupError is UnauthorizedAccessException ||
                        backupError is ArgumentException || backupError is InvalidDataException)
                    { reason += "已有备份也无法读取。"; }
                }
                LastMessage = "已安全恢复默认手账。" + reason + "（" + error.Message + "）";
                Debug.LogWarning("[Journal] Save:Recovered:" + LastMessage);
                return JournalDocument.CreateDefault(catalog);
            }
        }

        public bool Save(JournalDocument document)
        {
            var prepared = PrepareSnapshot(document);
            if (prepared == null)
            {
                Debug.LogWarning("[Journal] Save:Failed:" + LastMessage);
                return false;
            }
            bool result = CommitPrepared(prepared);
            PublishCommitResult();
            return result;
        }

        /// <summary>Call on the service-owning Unity thread. Performs no filesystem I/O.</summary>
        public PreparedSnapshot PrepareSnapshot(JournalDocument document)
        {
            document?.NormalizeOptionalData();
            if (Thread.CurrentThread.ManagedThreadId != ownerThreadId)
                throw new InvalidOperationException("PrepareSnapshot must run on the Unity thread that owns the journal.");
            if (writeBlocked)
            {
                LastMessage = "存档保护已启用：原文件无法备份，当前更改尚未写入磁盘。";
                return null;
            }
            if (!ValidateAndRepair(document, out string warning))
            {
                LastMessage = "手账数据不完整，未覆盖现有存档。" + warning;
                return null;
            }
            string json = JsonUtility.ToJson(document, true);
            if (Encoding.UTF8.GetByteCount(json) > MaximumFileBytes)
            {
                LastMessage = "存档超过当前支持的大小，原存档未被替换。";
                return null;
            }
            return new PreparedSnapshot(snapshotOwner, json, warning, Interlocked.Increment(ref preparedSequence));
        }

        /// <summary>
        /// Pure managed/native filesystem work. Safe on one worker thread: no
        /// Unity API, catalogue, JsonUtility, document, transform or renderer calls.
        /// The commit gate also serializes legacy synchronous Save calls.
        /// </summary>
        public bool CommitPrepared(PreparedSnapshot prepared)
        {
            if (prepared == null || !ReferenceEquals(prepared.OwnerToken, snapshotOwner))
                throw new ArgumentException("The prepared snapshot belongs to a different journal save service.", nameof(prepared));
            lock (commitLock)
            {
                lock (stateLock) { compatibleWarning = string.Empty; lastCommitSkipped = false; lastCommitSucceeded = false; }
                if (writeBlocked)
                {
                    LastMessage = "存档保护已启用：原文件无法备份，当前更改尚未写入磁盘。";
                    return false;
                }
                if (prepared.Sequence <= committedSequence)
                {
                    // A legacy synchronous Save may have committed a newer snapshot
                    // before an older worker acquired the gate. Never write it back.
                    lock (stateLock) { lastCommitSkipped = true; lastCommitSucceeded = true; }
                    return true;
                }
                bool result = CommitFile(prepared);
                if (result) committedSequence = prepared.Sequence;
                lock (stateLock) { lastCommitSucceeded = result; if (result) lastCommittedJson = prepared.Json; }
                return result;
            }
        }

        /// <summary>Publish worker status only from the Unity thread after completion.</summary>
        public void PublishCommitResult()
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThreadId)
                throw new InvalidOperationException("PublishCommitResult must run on the Unity thread.");
            bool succeeded, skipped; string warning, message;
            lock (stateLock)
            { succeeded = lastCommitSucceeded; skipped = lastCommitSkipped; warning = compatibleWarning; message = lastMessage; }
            if (skipped) return;
            if (!string.IsNullOrEmpty(warning)) Debug.LogWarning(warning);
            if (succeeded) Debug.Log("[Journal] Save:Saved:" + SavePath);
            else Debug.LogWarning("[Journal] Save:Failed:" + message);
        }

        bool CommitFile(PreparedSnapshot prepared)
        {
            string temporary = SavePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            bool replacing = false;
            try
            {
                string directory = Path.GetDirectoryName(SavePath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true))
                    { writer.Write(prepared.Json); writer.Flush(); }
                    stream.Flush(true);
                }
                if (File.Exists(SavePath))
                {
                    // Preserve the complete previous generation before touching the primary.
                    // Some encrypted/virtualized Windows folders cannot implement ReplaceFile.
                    byte[] previous = File.ReadAllBytes(SavePath);
                    using (var backup = new FileStream(SavePath + ".backup", FileMode.Create, FileAccess.Write, FileShare.None))
                    { backup.Write(previous, 0, previous.Length); backup.Flush(true); }
                    replacing = true;
                    try { File.Replace(temporary, SavePath, null, true); }
                    catch (Exception atomicError) when (atomicError is IOException || atomicError is NotSupportedException)
                    {
                        UsedCompatibleFileCommit = true;
                        bool moved = false;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
                        // COPY_ALLOWED handles logical folders whose backing files are on
                        // different volumes. Such a fallback is recoverable, not fully atomic.
                        moved = MoveFileEx(temporary, SavePath, 1 | 2 | 8);
#endif
                        if (!moved) File.Copy(temporary, SavePath, true);
                        using (var committed = new FileStream(SavePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                            committed.Flush(true);
                        lock (stateLock) compatibleWarning = "[Journal] Save:CompatibleCommit:Atomic replacement unavailable; prior valid generation retained at " + SavePath + ".backup; " + atomicError.Message;
                    }
                }
                else File.Move(temporary, SavePath);
                LastMessage = string.IsNullOrEmpty(prepared.Warning) ? "手账已保存。" : prepared.Warning + "手账已保存。";
                return true;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
                error is ArgumentException || error is NotSupportedException)
            {
                LastMessage = replacing
                    ? "本次保存未完成，上一次内容已保留在备份中。请检查磁盘空间和目录权限。（" + error.Message + "）"
                    : "无法保存手账，原存档保持不变。请检查磁盘空间和目录权限。（" + error.Message + "）";
                return false;
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        JournalDocument ReadValidated(string path, out string message)
        {
            if (new FileInfo(path).Length > MaximumFileBytes)
                throw new InvalidDataException("存档超过当前支持的大小。");
            string json = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith("{", StringComparison.Ordinal))
                throw new InvalidDataException("存档不是有效的 JSON 对象。");
            var header = JsonUtility.FromJson<SaveHeader>(json);
            if (header == null || header.schemaVersion != JournalDocument.CurrentSchemaVersion)
                throw new InvalidDataException(header != null && header.schemaVersion > JournalDocument.CurrentSchemaVersion
                    ? "这是较新版本的存档，当前版本不能直接读取。" : "存档版本号缺失或不受支持。");
            var loaded = JsonUtility.FromJson<JournalDocument>(json);
            loaded?.NormalizeOptionalData();
            if (!ValidateAndRepair(loaded, out message)) throw new InvalidDataException(message);
            // A recovered backup is not a committed primary and must be written
            // before it can be deduplicated by a new queue. Likewise a repaired
            // legacy record differs from this raw JSON and gets one real save.
            if (string.Equals(Path.GetFullPath(path), SavePath, StringComparison.OrdinalIgnoreCase))
                lock (stateLock) lastCommittedJson = json;
            return loaded;
        }

        bool ValidateAndRepair(JournalDocument document, out string message)
        {
            message = string.Empty;
            if (document == null) { message = "未找到手账数据。"; return false; }
            if (document.schemaVersion != JournalDocument.CurrentSchemaVersion)
            {
                message = document.schemaVersion > JournalDocument.CurrentSchemaVersion
                    ? "这是较新版本的存档，当前版本不能直接读取。" : "存档版本号缺失或不受支持。";
                return false;
            }
            if (document.pages == null || document.pages.Length < 24 || document.pages.Length % 2 != 0 || document.pages.Length > 4096)
            { message = "页面数量无效，需要至少24张并以完整跨页保存。"; return false; }
            if (!NotebookLibraryData.Validate(document, out string libraryError)) { message = libraryError; return false; }
            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            var decorationIds = new HashSet<string>(StringComparer.Ordinal);
            var reservedDecorationIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var existingPage in document.AllPages)
                if (existingPage != null && existingPage.decorations != null)
                    foreach (var existing in existingPage.decorations)
                        if (existing != null && !string.IsNullOrWhiteSpace(existing.instanceId)) reservedDecorationIds.Add(existing.instanceId);
            bool repairedPaper = false;
            foreach (var page in document.AllPages)
            {
                if (page == null || string.IsNullOrWhiteSpace(page.pageId) || !identifiers.Add(page.pageId))
                { message = "页面实例编号缺失或重复。"; return false; }
                if (page.decorations == null) page.decorations = new List<JournalDecorationData>();
                int decorationIndex = 0;
                foreach (var decoration in page.decorations)
                {
                    if (decoration == null || !Finite(decoration.position.x) || !Finite(decoration.position.y) ||
                        decoration.position.x < 0 || decoration.position.x > 1 || decoration.position.y < 0 || decoration.position.y > 1 ||
                        !Finite(decoration.scale.x) || !Finite(decoration.scale.y) || decoration.scale.x <= 0 || decoration.scale.y <= 0 ||
                        !Finite(decoration.rotationDegrees))
                    { message = "页面装饰坐标无效，未覆盖原有内容。"; return false; }
                    bool legacySize = decoration.size == Vector2.zero;
                    if (string.IsNullOrWhiteSpace(decoration.instanceId) && string.IsNullOrWhiteSpace(decoration.pageId) && legacySize)
                    {
                        // Stage 2 reserved decoration data but did not require an ID.
                        // Migrate only that old shape; malformed new records still fail.
                        string stem = "legacy-" + page.pageId + "-" + decorationIndex.ToString("D3");
                        string generated = stem;
                        int suffix = 0;
                        while (reservedDecorationIds.Contains(generated)) generated = stem + "-" + (++suffix);
                        decoration.instanceId = generated;
                        reservedDecorationIds.Add(generated);
                    }
                    if (string.IsNullOrWhiteSpace(decoration.instanceId) || !decorationIds.Add(decoration.instanceId))
                    { message = "装饰实例编号缺失或重复，未覆盖原有内容。"; return false; }
                    // Older v1 saves had neither pageId nor size/length. Membership in
                    // this page's list gives their stable owner without regenerating art.
                    decoration.pageId = page.pageId;
                    if (!Finite(decoration.size.x) || !Finite(decoration.size.y) ||
                        (!legacySize && (decoration.size.x <= 0 || decoration.size.y <= 0)) ||
                        !Finite(decoration.length) || decoration.length < 0 || !Finite(decoration.lengthMeters) || decoration.lengthMeters < 0)
                    { message = "装饰尺寸或胶带长度无效，未覆盖原有内容。"; return false; }
                    // JsonUtility implementations can deserialize absent scalar fields
                    // as zero; the default tape multiplier for legacy records is one.
                    if (decoration.length == 0) decoration.length = 1;
                    if (decoration.physicalMaterial && !CraftWorkspaceData.PhysicalDecorationFits(decoration))
                    { message = "实体素材超出页面物理范围，未覆盖原有存档。"; return false; }
                    decorationIndex++;
                }
            }
            foreach (var page in document.AllPages)
                if (catalog.Find(page.paperId) == null)
                { page.paperId = catalog.papers[0].id; repairedPaper = true; }
            if (document.currentSpread < 0 || document.currentSpread >= document.pages.Length / 2)
            {
                document.currentSpread = Mathf.Clamp(document.currentSpread, 0, document.pages.Length / 2 - 1);
                message = "上次页码超出范围，已回到有效页码。";
            }
            if (repairedPaper) message += "部分内页素材已不可用，相关页面已恢复默认纸张，其余内容保留。";
            if (!CraftWorkspaceData.Validate(document, out string craftMessage)) { message += craftMessage; return false; }
            if (!CommissionDeskData.Validate(document, out string commissionMessage)) { message += commissionMessage; return false; }
            if (!DreamGameplayData.Validate(document, out string dreamMessage)) { message += dreamMessage; return false; }
            if (!AtelierProgressData.Validate(document, out string progressMessage)) { message += progressMessage; return false; }
            if (!PersonalStudioData.Validate(document, out string studioMessage)) { message += studioMessage; return false; }
            if (!AtelierSocialData.Validate(document, out string socialMessage)) { message += socialMessage; return false; }
            return true;
        }

        static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}

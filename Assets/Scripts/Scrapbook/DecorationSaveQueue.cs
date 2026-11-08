using System;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;

namespace VintageJournal
{
    /// <summary>
    /// Main-thread snapshot preparation with a single background filesystem writer.
    /// At most one running and one latest pending snapshot are retained. Tick and
    /// Flush must be called on the same Unity thread as the save service.
    /// </summary>
    public sealed class DecorationSaveQueue
    {
        readonly JournalSaveService store;
        JournalSaveService.PreparedSnapshot pending;
        JournalSaveService.PreparedSnapshot active;
        JournalSaveService.PreparedSnapshot failed;
        Task<bool> running;
        public bool IsPending => pending != null || running != null || failed != null;
        public bool LastSucceeded { get; private set; } = true;
        public long CompletedCount { get; private set; }
        public string LastError { get; private set; } = string.Empty;

        public DecorationSaveQueue(JournalSaveService store)
        { this.store = store ?? throw new ArgumentNullException(nameof(store)); }

        public bool Enqueue(JournalDocument document)
        {
            CompleteRunning();
            var snapshot = store.PrepareSnapshot(document);
            if (snapshot == null)
            {
                LastSucceeded = false; LastError = store.LastMessage;
                return false;
            }
            if (pending != null && string.Equals(pending.Json, snapshot.Json, StringComparison.Ordinal)) return true;
            if (active != null && string.Equals(active.Json, snapshot.Json, StringComparison.Ordinal))
            {
                // A reversal can return to the running generation. Any different
                // pending state is now obsolete and must not be written afterward.
                pending = null; failed = null;
                return true;
            }
            if (running == null && string.Equals(store.LastCommittedJson, snapshot.Json, StringComparison.Ordinal))
            {
                pending = null; failed = null; LastSucceeded = true; LastError = string.Empty;
                return true;
            }
            // Documents may keep changing during a drag or a later command, but
            // only this immutable JSON string is visible to the worker.
            pending = snapshot; failed = null;
            return true;
        }

        public void Tick()
        {
            CompleteRunning();
            if (running == null && pending != null)
            {
                active = pending; pending = null;
                var prepared = active;
                running = Task.Run(() => store.CommitPrepared(prepared));
            }
        }

        void CompleteRunning()
        {
            if (running != null && running.IsCompleted)
            {
                try
                {
                    LastSucceeded = running.GetAwaiter().GetResult();
                    LastError = LastSucceeded ? string.Empty : store.LastMessage;
                    store.PublishCommitResult();
                }
                catch (Exception error)
                {
                    LastSucceeded = false; LastError = error.Message;
                    UnityEngine.Debug.LogWarning("[Journal] Save:Failed:后台保存未完成，已有备份保持保留。" + error.Message);
                }
                finally
                {
                    // A failed latest request remains available for an explicit
                    // retry. Tick alone does not hammer a failing disk every frame.
                    if (!LastSucceeded && pending == null) failed = active;
                    else failed = null;
                    active = null; running = null; CompletedCount++;
                }
            }
        }

        /// <summary>
        /// Drain the latest accepted state before switching pages, a legacy sync
        /// save, or quit. Timeout never discards an active write or pending state.
        /// </summary>
        public bool Flush(int timeoutMs = 3000)
        {
            var clock = Stopwatch.StartNew();
            int limit = Math.Max(0, timeoutMs);
            // A previous failure gets one explicit retry per Flush. Further
            // failures stay retained until another Enqueue/Flush request.
            if (failed != null && running == null && pending == null) { pending = failed; failed = null; }
            while (pending != null || running != null)
            {
                Tick();
                if (failed != null && running == null && pending == null) return false;
                if (running == null) continue;
                int remaining = Math.Max(0, limit - (int)Math.Min(int.MaxValue, clock.ElapsedMilliseconds));
                try
                {
                    if (!running.Wait(remaining))
                    {
                        LastError = "保存仍在进行中，当前写入与待保存内容已保留。";
                        return false;
                    }
                }
                catch (AggregateException)
                {
                    // Tick observes and reports the original task exception on
                    // the Unity thread, then can continue with the latest state.
                }
            }
            return LastSucceeded && failed == null;
        }
    }
}

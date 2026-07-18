using System;
using System.Threading;

namespace SimpleLLMChatCLI.RAG
{
    /// <summary>
    /// CLI host for indexing lifecycle, status-pipe progress, and deferred error reporting.
    /// Serializes indexing with a single-run guard (concurrent requests are dropped; a
    /// reconcile request that arrives while busy is remembered and runs once afterward).
    /// </summary>
    public static class RagHost
    {
        private enum IndexWork
        {
            Reconcile,
            Clear
        }

        private static readonly object Sync = new object();
        private static ConfigHandler _config;
        private static string _pendingError;
        private static int _running; // 0 = idle, 1 = running
        private static int _pendingReconcile; // 1 = run reconcile again when idle
        private static readonly ManualResetEvent Idle = new ManualResetEvent(true);

        public static void Initialize(ConfigHandler config)
        {
            lock (Sync) { _config = config; }
            RequestIndex(IndexWork.Reconcile, true);
        }

        public static void OnConfigReloaded(ConfigHandler config)
        {
            bool enabled = config.GetConfigBool("ragEnabled", false);
            lock (Sync)
            {
                _config = config;
                if (!enabled)
                    _pendingError = null;
            }

            if (!enabled)
            {
                Interlocked.Exchange(ref _pendingReconcile, 0);
                DocumentIndex.Invalidate();
            }
            else
                RequestIndex(IndexWork.Reconcile, true);
        }

        public static void BuildIndex()
        {
            ConfigHandler config;
            lock (Sync) { config = _config; }
            if (config == null || !config.GetConfigBool("ragEnabled", false))
            {
                PublishError("RAG is disabled.");
                return;
            }
            RequestIndex(IndexWork.Reconcile, false);
        }

        public static void ClearIndex()
        {
            RequestIndex(IndexWork.Clear, false);
        }

        public static void WaitIfIndexing()
        {
            try { Idle.WaitOne(120000); }
            catch { }
        }

        /// <summary>
        /// If an index error was recorded, print it once on the next chat request.
        /// </summary>
        public static void FlushPendingErrorOnce()
        {
            string error;
            lock (Sync)
            {
                error = _pendingError;
                _pendingError = null;
            }
            if (!string.IsNullOrEmpty(error))
                ChatOutput.WriteLine("[Auto-RAG index error: " + error + "]");
        }

        public static string GetKnowledgePathHint(ConfigHandler config)
        {
            if (config == null || !config.GetConfigBool("ragEnabled", false))
                return null;
            return "Knowledge for RAG is stored in " + DocumentIndex.ResolveKnowledgePath(config);
        }

        /// <summary>
        /// Starts indexing if idle. If busy, drops the request unless it's a reconcile with
        /// <paramref name="rememberReconcileIfBusy"/> set, in which case it's queued once.
        /// </summary>
        private static void RequestIndex(IndexWork work, bool rememberReconcileIfBusy)
        {
            ConfigHandler config;
            lock (Sync) { config = _config; }
            if (config == null)
                return;
            if (work != IndexWork.Clear && !config.GetConfigBool("ragEnabled", false))
                return;

            if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            {
                if (rememberReconcileIfBusy && work == IndexWork.Reconcile)
                    Interlocked.Exchange(ref _pendingReconcile, 1);
                return; // already running
            }

            Idle.Reset();
            StartBackground(work);
        }

        private static void StartBackground(IndexWork work)
        {
            Thread thread = new Thread(() =>
            {
                try
                {
                    ConfigHandler config;
                    lock (Sync) { config = _config; }
                    if (config == null)
                        return;

                    DocumentIndexer indexer = new DocumentIndexer(config, OnProgress);
                    if (work == IndexWork.Clear)
                        indexer.ClearIndex();
                    else
                        indexer.Reconcile();
                }
                catch (Exception ex)
                {
                    PublishError(ex.Message);
                }
                finally
                {
                    CompleteWork();
                }
            });
            thread.IsBackground = true;
            thread.Name = "RagHost-Indexer";
            thread.Start();
        }

        private static void CompleteWork()
        {
            bool pending = Interlocked.Exchange(ref _pendingReconcile, 0) == 1;

            ConfigHandler config;
            lock (Sync) { config = _config; }
            bool enabled = config != null && config.GetConfigBool("ragEnabled", false);

            if (pending && enabled)
            {
                StartBackground(IndexWork.Reconcile);
                return;
            }

            Interlocked.Exchange(ref _running, 0);
            Idle.Set();
        }

        private static void OnProgress(IndexProgress progress)
        {
            if (progress == null || Program.StatusPipe == null)
            {
                if (progress != null && string.Equals(progress.Phase, StatusPipe.IndexingPhaseError, StringComparison.OrdinalIgnoreCase))
                    RememberError(progress.Message);
                return;
            }

            if (string.Equals(progress.Phase, StatusPipe.IndexingPhaseStart, StringComparison.OrdinalIgnoreCase))
            {
                Program.StatusPipe.PublishLine(StatusPipe.FormatIndexingStart(progress.Total));
            }
            else if (string.Equals(progress.Phase, StatusPipe.IndexingPhaseProgress, StringComparison.OrdinalIgnoreCase))
            {
                Program.StatusPipe.PublishLine(StatusPipe.FormatIndexingProgress(
                    progress.Current, progress.Total, progress.File));
            }
            else if (string.Equals(progress.Phase, StatusPipe.IndexingPhaseDone, StringComparison.OrdinalIgnoreCase))
            {
                Program.StatusPipe.PublishLine(StatusPipe.FormatIndexingDone(progress.FileCount));
                lock (Sync) { _pendingError = null; }
            }
            else if (string.Equals(progress.Phase, StatusPipe.IndexingPhaseCleared, StringComparison.OrdinalIgnoreCase))
            {
                Program.StatusPipe.PublishLine(StatusPipe.FormatIndexingCleared());
                lock (Sync) { _pendingError = null; }
            }
            else if (string.Equals(progress.Phase, StatusPipe.IndexingPhaseError, StringComparison.OrdinalIgnoreCase))
            {
                Program.StatusPipe.PublishLine(StatusPipe.FormatIndexingError(progress.Message));
                RememberError(progress.Message);
            }
        }

        private static void PublishError(string message)
        {
            if (Program.StatusPipe != null)
                Program.StatusPipe.PublishLine(StatusPipe.FormatIndexingError(message));
            RememberError(message);
        }

        private static void RememberError(string message)
        {
            lock (Sync)
            {
                _pendingError = message;
            }
        }
    }
}

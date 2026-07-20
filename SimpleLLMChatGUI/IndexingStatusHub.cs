using System;
using System.Globalization;
using System.Windows;

namespace SimpleLLMChatGUI
{
    /// <summary>
    /// Holds latest RAG indexing status from the CLI status pipe for the Options RAG pane
    /// (NyoCoder-style brief + detail lines).
    /// </summary>
    public sealed class IndexingStatusSnapshot
    {
        public string BriefText;
        public string DetailText;
        public bool IsBusy;
    }

    public static class IndexingStatusHub
    {
        private static readonly object Sync = new object();
        private static string _briefText = "Index: (unknown)";
        private static string _detailText = string.Empty;
        private static bool _busy;

        public static event Action Updated;

        /// <summary>Returns brief/detail/busy together so callers never see a torn read across events.</summary>
        public static IndexingStatusSnapshot GetSnapshot()
        {
            lock (Sync)
            {
                return new IndexingStatusSnapshot
                {
                    BriefText = _briefText,
                    DetailText = _detailText,
                    IsBusy = _busy
                };
            }
        }

        public static void Publish(IndexingStatusEvent status)
        {
            if (status == null)
                return;

            string brief;
            string detail;
            bool busy = false;

            if (string.Equals(status.Phase, StatusPipe.IndexingPhaseStart, StringComparison.OrdinalIgnoreCase))
            {
                brief = status.Total > 0
                    ? string.Format(CultureInfo.CurrentCulture, "Indexing... 0/{0}", status.Total)
                    : "Indexing...";
                detail = "Scanning knowledge folder.";
                busy = true;
            }
            else if (string.Equals(status.Phase, StatusPipe.IndexingPhaseProgress, StringComparison.OrdinalIgnoreCase))
            {
                brief = string.Format(CultureInfo.CurrentCulture, "Indexing... {0}/{1}",
                    status.Current, status.Total);
                detail = string.IsNullOrEmpty(status.File)
                    ? string.Empty
                    : "Current file: " + status.File;
                busy = true;
            }
            else if (string.Equals(status.Phase, StatusPipe.IndexingPhaseDone, StringComparison.OrdinalIgnoreCase))
            {
                brief = status.FileCount > 0 ? "Index: Ready" : "Index: Empty";
                detail = string.Format(CultureInfo.CurrentCulture,
                    "{0} file(s) in index.", status.FileCount);
                busy = false;
            }
            else if (string.Equals(status.Phase, StatusPipe.IndexingPhaseCleared, StringComparison.OrdinalIgnoreCase))
            {
                brief = "Index: cleared";
                detail = "On-disk index removed. Run Index Now to rebuild.";
                busy = false;
            }
            else if (string.Equals(status.Phase, StatusPipe.IndexingPhaseError, StringComparison.OrdinalIgnoreCase))
            {
                string err = string.IsNullOrEmpty(status.Message) ? "unknown" : status.Message.Trim();
                brief = "Index: error";
                detail = err;
                busy = false;
            }
            else
            {
                brief = "Index: (unknown)";
                detail = status.RawLine ?? string.Empty;
            }

            lock (Sync)
            {
                _briefText = brief;
                _detailText = detail;
                _busy = busy;
            }

            RaiseUpdated();
        }

        private static void RaiseUpdated()
        {
            Action handler = Updated;
            if (handler == null)
                return;

            Application app = Application.Current;
            if (app != null && app.Dispatcher != null && !app.Dispatcher.CheckAccess())
            {
                app.Dispatcher.BeginInvoke(handler);
            }
            else
            {
                handler();
            }
        }
    }
}

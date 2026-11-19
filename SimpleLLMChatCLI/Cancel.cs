using System;
using System.Diagnostics;
using System.Threading;

namespace SimpleLLMChatCLI
{
    /// <summary>
    /// Stops the turn in progress. Triggered by Ctrl+C in a console, or by the GUI
    /// setting the named event (its child CLI has no console to send a key to).
    /// </summary>
    internal static class Cancel
    {
        /// <summary>True once the current turn was asked to stop.</summary>
        public static volatile bool Requested;

        // Unblocks whatever the turn is waiting on (HTTP stream, cURL, tool process).
        // Non-null only while a turn is running.
        private static volatile Action _abort;

        public static void Listen()
        {
            Console.CancelKeyPress += (s, e) =>
            {
                // Outside a turn, Ctrl+C exits as usual.
                if (_abort != null)
                {
                    e.Cancel = true;
                    Request();
                }
            };

            EventWaitHandle stopEvent = new EventWaitHandle(
                false, EventResetMode.AutoReset, ChatPipe.GetCancelEventName(Process.GetCurrentProcess().Id));
            Thread thread = new Thread(() =>
            {
                while (true)
                {
                    stopEvent.WaitOne();
                    Request();
                }
            });
            thread.IsBackground = true;
            thread.Start();
        }

        public static void Begin()
        {
            Requested = false;
            _abort = () => { };
        }

        public static void End()
        {
            _abort = null;
        }

        /// <summary>
        /// Registers how to unblock the operation about to start. Runs it at once
        /// if the stop already arrived.
        /// </summary>
        public static void OnAbort(Action abort)
        {
            if (_abort == null)
                return; // not in a turn (e.g. /reload, non-interactive mode)

            _abort = abort;
            if (Requested)
                Request();
        }

        private static void Request()
        {
            Requested = true;
            Action abort = _abort;
            if (abort == null)
                return;
            try { abort(); }
            catch { }
        }
    }
}

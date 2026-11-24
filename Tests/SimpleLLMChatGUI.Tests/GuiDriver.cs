using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Automation;

namespace SimpleLLMChatGUI.Tests
{
    /// <summary>
    /// Drives SimpleLLMChatGUI.exe via UI Automation.
    /// </summary>
    public sealed class GuiDriver : IDisposable
    {
        private Process _gui;
        private AutomationElement _window;
        private readonly string _workDir;

        public Process GuiProcess { get { return _gui; } }
        public string WorkDir { get { return _workDir; } }

        public GuiDriver(string workDir)
        {
            _workDir = workDir;
            string exe = Path.Combine(workDir, "SimpleLLMChatGUI.exe");
            if (!File.Exists(exe))
                throw new TestFailureException("GUI EXE missing: " + exe);

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = workDir,
                UseShellExecute = false
            };
            _gui = Process.Start(psi);
            if (_gui == null)
                throw new TestFailureException("Failed to start GUI");

            _window = WaitForWindow(_gui, 30000);
            if (_window == null)
                throw new TestFailureException("GUI window never appeared");
        }

        public void WaitUntilReady(int timeoutMs)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                AutomationElement send = FindById("sendButton");
                if (send != null && send.Current.IsEnabled)
                    return;
                Thread.Sleep(100);
            }
            throw new TestFailureException("sendButton never became enabled (CLI not ready?)");
        }

        public void SendTurn(string text, int timeoutMs)
        {
            Submit(text);
            WaitForTurnComplete(timeoutMs);
        }

        /// <summary>Types a message and presses Send without waiting for the reply.</summary>
        public void Submit(string text)
        {
            AutomationElement input = FindById("chatInput");
            AutomationElement send = FindById("sendButton");
            if (input == null || send == null)
                throw new TestFailureException("chatInput/sendButton not found");

            input.SetFocus();
            ValuePattern value = input.GetCurrentPattern(ValuePattern.Pattern) as ValuePattern;
            if (value != null)
                value.SetValue(text ?? "");
            else
            {
                // Fallback: select-all then type via SendKeys-like Insert
                throw new TestFailureException("chatInput does not support ValuePattern");
            }

            InvokePattern invoke = send.GetCurrentPattern(InvokePattern.Pattern) as InvokePattern;
            if (invoke == null)
                throw new TestFailureException("sendButton does not support InvokePattern");
            invoke.Invoke();
        }

        public void WaitForTurnComplete(int timeoutMs)
        {
            AutomationElement send;

            // Wait until the input is disabled then enabled again (generation complete;
            // the send button stays enabled as Stop meanwhile).
            // Fast replies can flip enabled→disabled→enabled between polls; also
            // accept "still enabled after a short settle" only if we never saw busy.
            Stopwatch sw = Stopwatch.StartNew();
            bool sawDisabled = false;
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                send = FindById("chatInput");
                if (send == null)
                {
                    Thread.Sleep(50);
                    continue;
                }
                if (!send.Current.IsEnabled)
                {
                    sawDisabled = true;
                }
                else if (sawDisabled)
                {
                    return;
                }
                else if (sw.ElapsedMilliseconds > 1500)
                {
                    // Never observed disabled; treat as complete after settle.
                    return;
                }
                Thread.Sleep(50);
            }
            throw new TestFailureException("Turn did not complete within " + timeoutMs + " ms");
        }

        /// <summary>
        /// Text of every loaded chat turn: document text plus the labels and
        /// bodies of collapsible blocks.
        /// </summary>
        public string ReadChatText()
        {
            StringBuilder sb = new StringBuilder();
            AutomationElement list = FindById("chatList");
            if (list == null)
                return "";

            foreach (AutomationElement el in list.FindAll(TreeScope.Descendants, Condition.TrueCondition))
            {
                try
                {
                    object pattern;
                    if (el.TryGetCurrentPattern(TextPattern.Pattern, out pattern))
                        sb.AppendLine(((TextPattern)pattern).DocumentRange.GetText(-1));
                    else if (el.Current.ControlType == ControlType.Text)
                        sb.AppendLine(el.Current.Name);
                }
                catch (ElementNotAvailableException)
                {
                    // turn re-rendered mid-walk
                }
            }
            return sb.ToString();
        }

        public bool WaitForChatText(string needle, int timeoutMs)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (ReadChatText().IndexOf(needle, StringComparison.Ordinal) >= 0)
                    return true;
                Thread.Sleep(100);
            }
            return false;
        }

        /// <summary>
        /// Waits for the GUI's message box, presses Yes or No, and returns its text.
        /// </summary>
        public string AnswerMessageBox(bool yes, int timeoutMs)
        {
            Condition isDialog = new AndCondition(
                new PropertyCondition(AutomationElement.ProcessIdProperty, _gui.Id),
                new PropertyCondition(AutomationElement.ClassNameProperty, "#32770"));

            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                AutomationElement dialog =
                    AutomationElement.RootElement.FindFirst(TreeScope.Children, isDialog)
                    ?? _window.FindFirst(TreeScope.Descendants, isDialog);
                // IDYES = 6, IDNO = 7
                AutomationElement button = dialog == null ? null : dialog.FindFirst(
                    TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.AutomationIdProperty, yes ? "6" : "7"));
                if (button != null)
                {
                    StringBuilder text = new StringBuilder();
                    foreach (AutomationElement label in dialog.FindAll(
                        TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text)))
                        text.AppendLine(label.Current.Name);

                    ((InvokePattern)button.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                    return text.ToString();
                }
                Thread.Sleep(100);
            }
            throw new TestFailureException("Message box did not appear within " + timeoutMs + " ms");
        }

        public Process FindCliProcess()
        {
            foreach (Process p in Process.GetProcessesByName("SimpleLLMChatCLI"))
            {
                try
                {
                    string path = p.MainModule.FileName;
                    if (path != null &&
                        path.StartsWith(_workDir, StringComparison.OrdinalIgnoreCase))
                        return p;
                }
                catch
                {
                    // access denied / exited
                }
            }
            return null;
        }

        /// <summary>Opens or closes the history sidebar (the History toggle button).</summary>
        public void ToggleHistory()
        {
            AutomationElement button = FindById("historyButton");
            if (button == null)
                throw new TestFailureException("historyButton not found");
            ((TogglePattern)button.GetCurrentPattern(TogglePattern.Pattern)).Toggle();
        }

        /// <summary>Clicks New Chat.</summary>
        public void NewChat()
        {
            Condition byName = new PropertyCondition(AutomationElement.NameProperty, "New Chat");
            AutomationElement button = _window.FindFirst(TreeScope.Descendants, byName);
            if (button == null)
                throw new TestFailureException("New Chat button not found");
            ((InvokePattern)button.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        }

        /// <summary>
        /// The sidebar's entries, top to bottom, as "title" or "title*" for the
        /// selected one (the open chat, unless the arrow keys moved it).
        /// </summary>
        public string ReadHistory()
        {
            AutomationElement list = FindById("historyList");
            if (list == null)
                return null;
            StringBuilder sb = new StringBuilder();
            Condition items = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem);
            foreach (AutomationElement item in list.FindAll(TreeScope.Children, items))
            {
                SelectionItemPattern selection = item.GetCurrentPattern(SelectionItemPattern.Pattern) as SelectionItemPattern;
                if (sb.Length > 0)
                    sb.Append('|');
                sb.Append(item.Current.Name);
                if (selection != null && selection.Current.IsSelected)
                    sb.Append('*');
            }
            return sb.ToString();
        }

        public bool WaitForHistory(string expected, int timeoutMs)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (ReadHistory() == expected)
                    return true;
                Thread.Sleep(100);
            }
            return false;
        }

        private AutomationElement FindById(string automationId)
        {
            if (_window == null) return null;
            Condition cond = new PropertyCondition(AutomationElement.AutomationIdProperty, automationId);
            return _window.FindFirst(TreeScope.Descendants, cond);
        }

        private static AutomationElement WaitForWindow(Process process, int timeoutMs)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                process.Refresh();
                if (process.HasExited)
                    throw new TestFailureException("GUI exited early with code " + process.ExitCode);
                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    AutomationElement el = AutomationElement.FromHandle(process.MainWindowHandle);
                    if (el != null)
                        return el;
                }
                Thread.Sleep(100);
            }
            return null;
        }

        public void Dispose()
        {
            try
            {
                foreach (Process p in Process.GetProcessesByName("SimpleLLMChatCLI"))
                {
                    try
                    {
                        string path = null;
                        try { path = p.MainModule.FileName; } catch { }
                        if (path != null && path.StartsWith(_workDir, StringComparison.OrdinalIgnoreCase))
                            p.Kill();
                    }
                    catch { }
                    finally
                    {
                        try { p.Dispose(); } catch { }
                    }
                }
            }
            catch { }

            if (_gui != null)
            {
                try
                {
                    if (!_gui.HasExited)
                        _gui.Kill();
                }
                catch { }
                try { _gui.Dispose(); } catch { }
                _gui = null;
            }
        }
    }
}

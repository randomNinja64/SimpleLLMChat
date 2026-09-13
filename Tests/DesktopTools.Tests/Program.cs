using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace DesktopTools.Tests
{
    internal static class Program
    {
        private const string Exe = "DesktopTools.exe";
        private const string FixtureExe = "DesktopTools.Fixture.exe";
        private const string FixtureTitle = "DesktopTools.XpFixture";

        static int Main(string[] args)
        {
            return SuiteMain.Run("DesktopTools", args, Run);
        }

        static void Run()
        {
            TestRunner.Run("unknown_tool", () => ToolClient.AssertUnknownTool(Exe));

            if (!ToolClient.ProductExists(FixtureExe))
                throw new TestFailureException("DesktopTools.Fixture.exe not packaged beside test EXE");

            Process fixture = null;
            try
            {
                fixture = StartFixture();
                Thread.Sleep(800);

                string hwnd = null;

                TestRunner.Run("list_windows", () =>
                {
                    ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "list_windows",
                        new JObject { ["window_title"] = FixtureTitle });
                    TestAssert.Equal(0, r.ExitCode, "exit");
                    TestAssert.Contains(r.Text, FixtureTitle, "title");
                    hwnd = ParseHwnd(r.Text);
                    TestAssert.True(!string.IsNullOrEmpty(hwnd), "parsed hwnd");
                });

                TestRunner.Run("get_desktop_context", () =>
                {
                    ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "get_desktop_context", new JObject());
                    TestAssert.Equal(0, r.ExitCode, "exit");
                    TestAssert.True(!string.IsNullOrWhiteSpace(r.Text), "non-empty");
                });

                TestRunner.Run("list_controls", () =>
                {
                    EnsureHwnd(ref hwnd, fixture);
                    ToolInvokeResult r = ListControls(hwnd);
                    TestAssert.Equal(0, r.ExitCode, "exit");
                    TestAssert.ContainsIgnoreCase(r.Text, "btnClick", "button");
                    TestAssert.ContainsIgnoreCase(r.Text, "txtEdit", "edit");
                    TestAssert.ContainsIgnoreCase(r.Text, "lblDouble", "double-click label");
                    TestAssert.ContainsIgnoreCase(r.Text, "pnlDrag", "drag panel");
                    TestAssert.ContainsIgnoreCase(r.Text, "lblStatus", "status label");
                });

                TestRunner.Run("set_control_text", () =>
                {
                    EnsureHwnd(ref hwnd, fixture);
                    ToolInvokeResult list = ListControls(hwnd);
                    TestAssert.Equal(0, list.ExitCode, "list exit");
                    int element = FindElementIndex(list.Text, "txtEdit")
                        ?? FindElementIndex(list.Text, "edit")
                        ?? FindElementIndex(list.Text, "initial")
                        ?? 2;
                    ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "set_control_text",
                        new JObject
                        {
                            ["hwnd"] = hwnd,
                            ["element"] = element,
                            ["text"] = "from-test"
                        });
                    TestAssert.True(!r.Text.StartsWith("error:", StringComparison.OrdinalIgnoreCase), r.Text);
                    TestAssert.Equal(0, r.ExitCode, "exit");
                });

                TestRunner.Run("navigate.focus", () =>
                {
                    EnsureHwnd(ref hwnd, fixture);
                    ToolInvokeResult list = ListControls(hwnd);
                    int? element = FindElementIndex(list.Text, "txtEdit")
                        ?? FindElementIndex(list.Text, "from-test")
                        ?? FindElementIndex(list.Text, "edit")
                        ?? FindElementIndex(list.Text, "initial");
                    TestAssert.True(element.HasValue, "edit element");
                    NavigateOk(new JObject
                    {
                        ["action"] = "focus",
                        ["hwnd"] = hwnd,
                        ["element"] = element.Value
                    });
                    Thread.Sleep(250);
                    AssertStatus(hwnd, "focused");
                });

                TestRunner.Run("navigate.click", () =>
                {
                    EnsureHwnd(ref hwnd, fixture);
                    fixture.Refresh();
                    TestAssert.True(!fixture.HasExited, "fixture still running");

                    ToolInvokeResult list = ListControls(hwnd);
                    int? element = FindElementIndex(list.Text, "btnClick");
                    TestAssert.True(element.HasValue, "btnClick element");
                    NavigateOk(new JObject
                    {
                        ["action"] = "click",
                        ["hwnd"] = hwnd,
                        ["element"] = element.Value
                    });
                    Thread.Sleep(250);
                    AssertStatus(hwnd, "clicked");
                });

                TestRunner.Run("navigate.double_click", () =>
                {
                    EnsureHwnd(ref hwnd, fixture);
                    ToolInvokeResult list = ListControls(hwnd);
                    int? element = FindElementIndex(list.Text, "lblDouble")
                        ?? FindElementIndex(list.Text, "DoubleClick Me");
                    TestAssert.True(element.HasValue, "double-click label element");
                    NavigateOk(new JObject
                    {
                        ["action"] = "double_click",
                        ["hwnd"] = hwnd,
                        ["element"] = element.Value
                    });
                    Thread.Sleep(350);
                    AssertStatus(hwnd, "double_clicked");
                });

                TestRunner.Run("navigate.drag.element", () =>
                {
                    EnsureHwnd(ref hwnd, fixture);
                    ToolInvokeResult list = ListControls(hwnd);
                    int? from = FindElementIndex(list.Text, "pnlDrag")
                        ?? FindElementIndex(list.Text, "Drag Me");
                    int? to = FindElementIndex(list.Text, "pnlDrop")
                        ?? FindElementIndex(list.Text, "Drop Here");
                    TestAssert.True(from.HasValue, "drag start element");
                    TestAssert.True(to.HasValue, "drag end element");
                    NavigateOk(new JObject
                    {
                        ["action"] = "drag",
                        ["hwnd"] = hwnd,
                        ["element"] = from.Value,
                        ["to_element"] = to.Value
                    });
                    Thread.Sleep(350);
                    AssertStatus(hwnd, "dragged");
                });

                TestRunner.Run("navigate.drag.coords", () =>
                {
                    EnsureHwnd(ref hwnd, fixture);
                    // Clear prior "dragged" so this case must re-trigger via x/y → to_x/to_y.
                    ToolInvokeResult before = ListControls(hwnd);
                    int? edit = FindElementIndex(before.Text, "txtEdit");
                    TestAssert.True(edit.HasValue, "txtEdit for status reset");
                    NavigateOk(new JObject
                    {
                        ["action"] = "focus",
                        ["hwnd"] = hwnd,
                        ["element"] = edit.Value
                    });
                    Thread.Sleep(200);
                    AssertStatus(hwnd, "focused");

                    ToolInvokeResult list = ListControls(hwnd);
                    int fromX, fromY, toX, toY;
                    TestAssert.True(
                        TryParseCenter(list.Text, "pnlDrag", out fromX, out fromY),
                        "pnlDrag bounds");
                    TestAssert.True(
                        TryParseCenter(list.Text, "pnlDrop", out toX, out toY),
                        "pnlDrop bounds");
                    TestAssert.True(fromX != toX || fromY != toY, "drag start/end differ");
                    NavigateOk(new JObject
                    {
                        ["action"] = "drag",
                        ["hwnd"] = hwnd,
                        ["x"] = fromX,
                        ["y"] = fromY,
                        ["to_x"] = toX,
                        ["to_y"] = toY
                    });
                    Thread.Sleep(350);
                    AssertStatus(hwnd, "dragged");
                });

                TestRunner.Run("navigate.scroll", () =>
                {
                    EnsureHwnd(ref hwnd, fixture);
                    ToolInvokeResult list = ListControls(hwnd);
                    int? element = FindElementIndex(list.Text, "pnlScroll")
                        ?? FindElementIndex(list.Text, "scroll-line-0");
                    TestAssert.True(element.HasValue, "scroll target element");
                    NavigateOk(new JObject
                    {
                        ["action"] = "scroll",
                        ["hwnd"] = hwnd,
                        ["element"] = element.Value,
                        ["direction"] = "down",
                        ["amount"] = 5
                    });
                    Thread.Sleep(350);
                    AssertStatus(hwnd, "scrolled");
                });

                TestRunner.Run("navigate.unknown_action", () =>
                {
                    EnsureHwnd(ref hwnd, fixture);
                    ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "navigate",
                        new JObject
                        {
                            ["action"] = "not_a_real_action",
                            ["hwnd"] = hwnd
                        });
                    TestAssert.Equal(1, r.ExitCode, "exit");
                    TestAssert.ContainsIgnoreCase(r.Text, "unknown action", "error text");
                });

                TestRunner.Run("send_keys", () =>
                {
                    ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "send_keys",
                        new JObject { ["keys"] = "{TAB}" });
                    TestAssert.Equal(0, r.ExitCode, "exit");
                });

                TestRunner.Run("screenshot", () =>
                {
                    EnsureHwnd(ref hwnd, fixture);
                    ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "screenshot",
                        new JObject { ["hwnd"] = hwnd });
                    TestAssert.Equal(0, r.ExitCode, "exit");
                    TestAssert.True(!string.IsNullOrEmpty(r.ImageBase64), "image data");
                });
            }
            finally
            {
                if (fixture != null)
                {
                    try
                    {
                        if (!fixture.HasExited)
                            fixture.Kill();
                    }
                    catch { }
                    try { fixture.Dispose(); } catch { }
                }
            }
        }

        static ToolInvokeResult ListControls(string hwnd)
        {
            return ToolClient.InvokeProduct(Exe, "list_controls",
                new JObject { ["hwnd"] = hwnd });
        }

        static void NavigateOk(JObject args)
        {
            ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "navigate", args);
            TestAssert.True(!r.Text.StartsWith("error:", StringComparison.OrdinalIgnoreCase), r.Text);
            TestAssert.Equal(0, r.ExitCode, "exit");
        }

        static void AssertStatus(string hwnd, string expected)
        {
            // Fixture puts the status in the window title ("DesktopTools.XpFixture - <status>").
            // list_controls prefers AccessibleName, so the status label Text may not appear.
            ToolInvokeResult windows = ToolClient.InvokeProduct(Exe, "list_windows",
                new JObject { ["window_title"] = FixtureTitle });
            TestAssert.Equal(0, windows.ExitCode, "list_windows after " + expected);
            TestAssert.True(
                windows.Text.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0,
                "expected title to include '" + expected + "': " + Truncate(windows.Text, 400));
        }

        static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s ?? "";
            return s.Substring(0, max) + "...";
        }

        static void EnsureHwnd(ref string hwnd, Process fixture)
        {
            if (!string.IsNullOrEmpty(hwnd))
                return;
            fixture.Refresh();
            if (fixture.MainWindowHandle != IntPtr.Zero)
                hwnd = "0x" + fixture.MainWindowHandle.ToString("X");
            else
                throw new TestFailureException("fixture hwnd unavailable");
        }

        static string ParseHwnd(string listWindowsText)
        {
            if (string.IsNullOrEmpty(listWindowsText))
                return null;
            string[] lines = listWindowsText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                string t = line.Trim();
                if (!t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    continue;
                int space = t.IndexOf(' ');
                if (space > 2)
                    return t.Substring(0, space);
                return t;
            }
            return null;
        }

        static Process StartFixture()
        {
            string path = ToolClient.ProductExe(FixtureExe);
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = path,
                WorkingDirectory = Path.GetDirectoryName(path),
                UseShellExecute = false
            };
            Process p = Process.Start(psi);
            if (p == null)
                throw new TestFailureException("Failed to start fixture: " + path);
            for (int i = 0; i < 50; i++)
            {
                p.Refresh();
                if (p.MainWindowHandle != IntPtr.Zero)
                    break;
                Thread.Sleep(100);
            }
            if (p.MainWindowHandle == IntPtr.Zero)
                throw new TestFailureException("Fixture window did not appear");
            return p;
        }

        /// <summary>1-based element index from list_controls output (digits only, no '#').</summary>
        static int? FindElementIndex(string tree, string marker)
        {
            if (string.IsNullOrEmpty(tree) || string.IsNullOrEmpty(marker))
                return null;
            string[] lines = tree.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                if (line.IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                int hash = line.IndexOf('#');
                if (hash < 0) continue;
                int end = hash + 1;
                while (end < line.Length && char.IsDigit(line[end])) end++;
                if (end > hash + 1)
                {
                    int n;
                    if (int.TryParse(line.Substring(hash + 1, end - (hash + 1)), out n) && n > 0)
                        return n;
                }
            }
            return null;
        }

        /// <summary>
        /// Parse center of "@x,y WxH" from the list_controls line matching <paramref name="marker"/>.
        /// Coordinates are capture-relative (same space as screenshot / navigate x,y).
        /// </summary>
        static bool TryParseCenter(string tree, string marker, out int cx, out int cy)
        {
            cx = 0;
            cy = 0;
            if (string.IsNullOrEmpty(tree) || string.IsNullOrEmpty(marker))
                return false;
            string[] lines = tree.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                if (line.IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                int at = line.LastIndexOf('@');
                if (at < 0) continue;
                string rest = line.Substring(at + 1).Trim();
                // "42,301 300x150" or trailing junk
                int space = rest.IndexOf(' ');
                if (space <= 0) continue;
                string xy = rest.Substring(0, space);
                string wh = rest.Substring(space + 1).Trim();
                int comma = xy.IndexOf(',');
                int xmul = wh.IndexOf('x');
                if (comma <= 0 || xmul <= 0) continue;
                int x, y, w, h;
                if (!int.TryParse(xy.Substring(0, comma), out x)) continue;
                if (!int.TryParse(xy.Substring(comma + 1), out y)) continue;
                if (!int.TryParse(wh.Substring(0, xmul), out w)) continue;
                string hPart = wh.Substring(xmul + 1);
                int hEnd = 0;
                while (hEnd < hPart.Length && char.IsDigit(hPart[hEnd])) hEnd++;
                if (hEnd == 0 || !int.TryParse(hPart.Substring(0, hEnd), out h)) continue;
                cx = x + w / 2;
                cy = y + h / 2;
                return true;
            }
            return false;
        }
    }
}

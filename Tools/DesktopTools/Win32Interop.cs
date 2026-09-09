using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopTools
{
    internal static class Win32Interop
    {
        public const int WM_GETTEXT = 0x000D;
        public const int WM_GETTEXTLENGTH = 0x000E;
        public const int WM_SETTEXT = 0x000C;
        public const int BM_CLICK = 0x00F5;
        public const int CB_GETCURSEL = 0x0147;
        public const int CB_GETLBTEXT = 0x0148;
        public const int CB_GETLBTEXTLEN = 0x0149;

        public const int SW_RESTORE = 9;
        public const int SW_SHOW = 5;

        public const uint INPUT_MOUSE = 0;
        public const uint INPUT_KEYBOARD = 1;

        public const uint MOUSEEVENTF_MOVE = 0x0001;
        public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        public const uint MOUSEEVENTF_LEFTUP = 0x0004;
        public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        public const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        public const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        public const uint MOUSEEVENTF_WHEEL = 0x0800;
        public const uint MOUSEEVENTF_HWHEEL = 0x01000;
        public const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
        public const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
        public const int WHEEL_DELTA = 120;

        public const uint KEYEVENTF_KEYUP = 0x0002;
        public const uint KEYEVENTF_UNICODE = 0x0004;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindowEnabled(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, StringBuilder lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessageTimeout(
          IntPtr hWnd, int msg, IntPtr wParam, string lParam,
          uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private const uint SMTO_ABORTIFHUNG = 0x0002;
        private const uint DefaultSendTimeoutMs = 2000;

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern IntPtr GetLastActivePopup(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        public static extern uint GetDoubleClickTime();

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        public static extern short VkKeyScan(char ch);

        [DllImport("user32.dll")]
        public static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetAwarenessFromDpiAwarenessContext(IntPtr context);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;
        private const int SM_XVIRTUALSCREEN = 76;
        private const int SM_YVIRTUALSCREEN = 77;
        private const int SM_CXVIRTUALSCREEN = 78;
        private const int SM_CYVIRTUALSCREEN = 79;
        private const int LOGPIXELSX = 88;
        private const int DPI_AWARENESS_UNAWARE = 0;

        private static bool _dpiAwarenessSet;

        /// <summary>
        /// Accessibility providers report rectangles in the target window's own coordinate
        /// space. Windows virtualizes that space for DPI-unaware windows, so their rectangles
        /// come back unscaled while SendInput expects physical pixels. Returns the factor to
        /// multiply by, or 1.0 when the two spaces already agree.
        /// </summary>
        public static double GetProviderScale(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return 1.0;

            try
            {
                if (GetAwarenessFromDpiAwarenessContext(GetWindowDpiAwarenessContext(hwnd)) != DPI_AWARENESS_UNAWARE)
                    return 1.0;

                int systemDpi = GetSystemDpi();
                return systemDpi > 0 ? systemDpi / 96.0 : 1.0;
            }
            catch (EntryPointNotFoundException)
            {
                // Before Windows 10 there is no way to ask; assume no correction is needed.
                return 1.0;
            }
        }

        private static int GetSystemDpi()
        {
            IntPtr hdc = GetDC(IntPtr.Zero);
            if (hdc == IntPtr.Zero)
                return 0;

            try
            {
                return GetDeviceCaps(hdc, LOGPIXELSX);
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, hdc);
            }
        }

        public static void EnsureDpiAwareness()
        {
            if (_dpiAwarenessSet)
                return;
            _dpiAwarenessSet = true;
            try
            {
                SetProcessDPIAware();
            }
            catch
            {
                // Vista+ only; ignore on XP.
            }
        }

        public static IntPtr ParseHwnd(string hwndText)
        {
            if (string.IsNullOrWhiteSpace(hwndText))
                return IntPtr.Zero;

            hwndText = hwndText.Trim();
            if (hwndText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                long value = Convert.ToInt64(hwndText.Substring(2), 16);
                return new IntPtr(value);
            }

            long dec;
            if (long.TryParse(hwndText, out dec))
                return new IntPtr(dec);

            throw new ArgumentException("invalid hwnd format: '" + hwndText + "'. Use hex (0x...) or decimal.");
        }

        public static string FormatHwnd(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return "0x0";
            return "0x" + hwnd.ToInt64().ToString("X");
        }

        public static string GetWindowTitle(IntPtr hwnd)
        {
            var sb = new StringBuilder(512);
            GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        public static string GetControlClass(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        public static string GetControlText(IntPtr hwnd)
        {
            if (!IsWindow(hwnd))
                return "";

            string className = GetControlClass(hwnd);
            if (className.Equals("ComboBox", StringComparison.OrdinalIgnoreCase))
                return GetComboBoxText(hwnd);

            int length = (int)SendMessage(hwnd, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero);
            if (length <= 0)
            {
                var title = new StringBuilder(512);
                GetWindowText(hwnd, title, title.Capacity);
                return title.ToString();
            }

            var sb = new StringBuilder(length + 1);
            SendMessage(hwnd, WM_GETTEXT, (IntPtr)(length + 1), sb);
            return sb.ToString();
        }

        private static string GetComboBoxText(IntPtr hwnd)
        {
            int sel = (int)SendMessage(hwnd, CB_GETCURSEL, IntPtr.Zero, IntPtr.Zero);
            if (sel < 0)
                return "";

            int len = (int)SendMessage(hwnd, CB_GETLBTEXTLEN, (IntPtr)sel, IntPtr.Zero);
            if (len <= 0)
                return "";

            var sb = new StringBuilder(len + 1);
            SendMessage(hwnd, CB_GETLBTEXT, (IntPtr)sel, sb);
            return sb.ToString();
        }

        /// <summary>
        /// Outer-window rect origin and size (same space as a window screenshot).
        /// </summary>
        public static bool TryGetWindowOrigin(IntPtr hwnd, out int left, out int top, out int width, out int height)
        {
            left = 0;
            top = 0;
            width = 0;
            height = 0;

            RECT rect;
            if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out rect))
                return false;

            left = rect.Left;
            top = rect.Top;
            width = rect.Right - rect.Left;
            height = rect.Bottom - rect.Top;
            return width > 0 && height > 0;
        }

        public static void GetVirtualDesktopOrigin(out int left, out int top, out int width, out int height)
        {
            left = GetSystemMetrics(SM_XVIRTUALSCREEN);
            top = GetSystemMetrics(SM_YVIRTUALSCREEN);
            width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
            if (width <= 0) width = GetSystemMetrics(SM_CXSCREEN);
            if (height <= 0) height = GetSystemMetrics(SM_CYSCREEN);
            if (width <= 0) width = 1;
            if (height <= 0) height = 1;
        }

        /// <summary>
        /// Convert capture-relative pixels to physical screen coordinates.
        /// When <paramref name="hwnd"/> is set, (x,y) is relative to the window outer rect;
        /// otherwise relative to the virtual desktop (same as a full-desktop screenshot).
        /// </summary>
        public static bool TryCaptureToScreen(IntPtr hwnd, int x, int y, out int screenX, out int screenY, out string error)
        {
            screenX = 0;
            screenY = 0;
            error = null;

            if (hwnd != IntPtr.Zero)
            {
                int left, top, width, height;
                if (!TryGetWindowOrigin(hwnd, out left, out top, out width, out height))
                {
                    error = "error: GetWindowRect failed for hwnd=" + FormatHwnd(hwnd) + ".";
                    return false;
                }

                screenX = left + x;
                screenY = top + y;
                return true;
            }

            int deskLeft, deskTop, deskW, deskH;
            GetVirtualDesktopOrigin(out deskLeft, out deskTop, out deskW, out deskH);
            screenX = deskLeft + x;
            screenY = deskTop + y;
            return true;
        }

        public static List<IntPtr> EnumerateTopLevelWindows()
        {
            var list = new List<IntPtr>();
            EnumWindows((hWnd, lParam) =>
            {
                if (IsWindowVisible(hWnd))
                    list.Add(hWnd);
                return true;
            }, IntPtr.Zero);
            return list;
        }

        public static List<IntPtr> EnumerateChildControls(IntPtr parent)
        {
            var list = new List<IntPtr>();
            EnumChildWindows(parent, (hWnd, lParam) =>
            {
                list.Add(hWnd);
                return true;
            }, IntPtr.Zero);
            return list;
        }

        public static bool SetControlText(IntPtr hwnd, string text)
        {
            IntPtr result;
            IntPtr sent = SendMessageTimeout(
              hwnd, WM_SETTEXT, IntPtr.Zero, text ?? "",
              SMTO_ABORTIFHUNG, DefaultSendTimeoutMs, out result);
            return sent != IntPtr.Zero && result != IntPtr.Zero;
        }

        public enum MouseButton
        {
            Left,
            Right,
            Middle
        }

        public static MouseButton ParseMouseButton(string button)
        {
            if (string.IsNullOrWhiteSpace(button))
                return MouseButton.Left;

            switch (button.Trim().ToLowerInvariant())
            {
                case "right":
                case "r":
                    return MouseButton.Right;
                case "middle":
                case "mid":
                case "m":
                    return MouseButton.Middle;
                default:
                    return MouseButton.Left;
            }
        }

        public static void ClickAtScreenPoint(int x, int y, MouseButton button, int clickCount, KeyModifiers modifiers)
        {
            if (clickCount < 1)
                clickCount = 1;

            uint downFlag, upFlag;
            GetMouseButtonFlags(button, out downFlag, out upFlag);

            MoveAbsolute(x, y);

            HoldModifiers(modifiers, keyUp: false);
            try
            {
                int gapMs = (int)(GetDoubleClickTime() / 2);
                if (gapMs < 1)
                    gapMs = 50;

                for (int i = 0; i < clickCount; i++)
                {
                    if (i > 0)
                        System.Threading.Thread.Sleep(gapMs);

                    SendInput(2, new[]
                    {
                        MouseFlagInput(downFlag),
                        MouseFlagInput(upFlag)
                    }, Marshal.SizeOf(typeof(INPUT)));
                }
            }
            finally
            {
                HoldModifiers(modifiers, keyUp: true);
            }
        }

        public static void ClickControlCenter(IntPtr hwnd, MouseButton button, int clickCount, KeyModifiers modifiers)
        {
            int x, y;
            if (!TryGetWindowCenter(hwnd, out x, out y))
                throw new InvalidOperationException("could not get control rect for hwnd " + FormatHwnd(hwnd));

            ClickAtScreenPoint(x, y, button, clickCount, modifiers);
        }

        /// <summary>
        /// Press at (fromX,fromY), move to (toX,toY) in steps, then release. Uses the real
        /// cursor via SendInput — same path as ClickAtScreenPoint.
        /// </summary>
        public static void DragAtScreenPoints(int fromX, int fromY, int toX, int toY, MouseButton button, KeyModifiers modifiers)
        {
            uint downFlag, upFlag;
            GetMouseButtonFlags(button, out downFlag, out upFlag);

            MoveAbsolute(fromX, fromY);
            System.Threading.Thread.Sleep(30);

            HoldModifiers(modifiers, keyUp: false);
            try
            {
                SendInput(1, new[] { MouseFlagInput(downFlag) }, Marshal.SizeOf(typeof(INPUT)));
                System.Threading.Thread.Sleep(40);

                // Intermediate moves so apps that only track WM_MOUSEMOVE see the path.
                const int steps = 8;
                for (int i = 1; i <= steps; i++)
                {
                    int x = fromX + (toX - fromX) * i / steps;
                    int y = fromY + (toY - fromY) * i / steps;
                    MoveAbsolute(x, y);
                    System.Threading.Thread.Sleep(15);
                }

                SendInput(1, new[] { MouseFlagInput(upFlag) }, Marshal.SizeOf(typeof(INPUT)));
            }
            finally
            {
                HoldModifiers(modifiers, keyUp: true);
            }
        }

        [Flags]
        public enum KeyModifiers
        {
            None = 0,
            Ctrl = 1,
            Shift = 2,
            Alt = 4,
            Win = 8
        }

        private const ushort VK_SHIFT = 0x10;
        private const ushort VK_CONTROL = 0x11;
        private const ushort VK_MENU = 0x12;
        private const ushort VK_LWIN = 0x5B;

        private static void HoldModifiers(KeyModifiers modifiers, bool keyUp)
        {
            if (modifiers == KeyModifiers.None)
                return;

            // Down: ctrl, shift, alt, win. Up: reverse order.
            ushort[] order = keyUp
                ? new ushort[] { VK_LWIN, VK_MENU, VK_SHIFT, VK_CONTROL }
                : new ushort[] { VK_CONTROL, VK_SHIFT, VK_MENU, VK_LWIN };

            KeyModifiers[] flags = keyUp
                ? new[] { KeyModifiers.Win, KeyModifiers.Alt, KeyModifiers.Shift, KeyModifiers.Ctrl }
                : new[] { KeyModifiers.Ctrl, KeyModifiers.Shift, KeyModifiers.Alt, KeyModifiers.Win };

            for (int i = 0; i < order.Length; i++)
            {
                if ((modifiers & flags[i]) != 0)
                    SendKeyInput(order[i], keyUp);
            }
        }

        private static void MoveAbsolute(int screenX, int screenY)
        {
            int absX, absY;
            uint moveFlags;
            ToAbsoluteMouse(screenX, screenY, out absX, out absY, out moveFlags);
            SendInput(1, new[] { MouseMoveInput(absX, absY, moveFlags) }, Marshal.SizeOf(typeof(INPUT)));
        }

        public static bool TryGetWindowCenter(IntPtr hwnd, out int x, out int y)
        {
            x = 0;
            y = 0;
            RECT rect;
            if (!GetWindowRect(hwnd, out rect))
                return false;

            x = (rect.Left + rect.Right) / 2;
            y = (rect.Top + rect.Bottom) / 2;
            return true;
        }

        private static void GetMouseButtonFlags(MouseButton button, out uint downFlag, out uint upFlag)
        {
            switch (button)
            {
                case MouseButton.Right:
                    downFlag = MOUSEEVENTF_RIGHTDOWN;
                    upFlag = MOUSEEVENTF_RIGHTUP;
                    break;
                case MouseButton.Middle:
                    downFlag = MOUSEEVENTF_MIDDLEDOWN;
                    upFlag = MOUSEEVENTF_MIDDLEUP;
                    break;
                default:
                    downFlag = MOUSEEVENTF_LEFTDOWN;
                    upFlag = MOUSEEVENTF_LEFTUP;
                    break;
            }
        }

        private static void GetWheelFlags(ScrollDirection direction, int amount, out uint wheelFlag, out int delta)
        {
            switch (direction)
            {
                case ScrollDirection.Up:
                    wheelFlag = MOUSEEVENTF_WHEEL;
                    delta = WHEEL_DELTA * amount;
                    break;
                case ScrollDirection.Left:
                    wheelFlag = MOUSEEVENTF_HWHEEL;
                    delta = -WHEEL_DELTA * amount;
                    break;
                case ScrollDirection.Right:
                    wheelFlag = MOUSEEVENTF_HWHEEL;
                    delta = WHEEL_DELTA * amount;
                    break;
                default:
                    wheelFlag = MOUSEEVENTF_WHEEL;
                    delta = -WHEEL_DELTA * amount;
                    break;
            }
        }

        private static INPUT MouseMoveInput(int absX, int absY, uint moveFlags)
        {
            return new INPUT
            {
                type = INPUT_MOUSE,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = absX,
                        dy = absY,
                        dwFlags = moveFlags
                    }
                }
            };
        }

        private static INPUT MouseFlagInput(uint flags, uint mouseData = 0)
        {
            return new INPUT
            {
                type = INPUT_MOUSE,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        mouseData = mouseData,
                        dwFlags = flags
                    }
                }
            };
        }

        /// <summary>
        /// Map physical screen pixels to SendInput absolute coords over the virtual desktop.
        /// </summary>
        private static void ToAbsoluteMouse(int screenX, int screenY, out int absX, out int absY, out uint flags)
        {
            int left, top, width, height;
            GetVirtualDesktopOrigin(out left, out top, out width, out height);

            absX = (int)(((screenX - left) * 65535.0) / width);
            absY = (int)(((screenY - top) * 65535.0) / height);
            flags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK;
        }

        public enum ScrollDirection
        {
            Up,
            Down,
            Left,
            Right
        }

        public static ScrollDirection ParseScrollDirection(string direction)
        {
            if (string.IsNullOrWhiteSpace(direction))
                return ScrollDirection.Down;

            switch (direction.Trim().ToLowerInvariant())
            {
                case "up":
                    return ScrollDirection.Up;
                case "left":
                    return ScrollDirection.Left;
                case "right":
                    return ScrollDirection.Right;
                default:
                    return ScrollDirection.Down;
            }
        }

        public static void ScrollAtScreenPoint(int x, int y, ScrollDirection direction, int amount)
        {
            if (amount < 1)
                amount = 1;

            uint wheelFlag;
            int delta;
            GetWheelFlags(direction, amount, out wheelFlag, out delta);

            int absX, absY;
            uint moveFlags;
            ToAbsoluteMouse(x, y, out absX, out absY, out moveFlags);

            SendInput(2, new[]
            {
                MouseMoveInput(absX, absY, moveFlags),
                MouseFlagInput(wheelFlag, unchecked((uint)delta))
            }, Marshal.SizeOf(typeof(INPUT)));
        }

        public static void ScrollControlCenter(IntPtr hwnd, ScrollDirection direction, int amount)
        {
            int x, y;
            if (!TryGetWindowCenter(hwnd, out x, out y))
                throw new InvalidOperationException("could not get control rect for hwnd " + FormatHwnd(hwnd));

            ScrollAtScreenPoint(x, y, direction, amount);
        }

        public static bool FocusWindow(IntPtr hwnd)
        {
            if (!IsWindow(hwnd))
                return false;

            ShowWindow(hwnd, SW_RESTORE);
            ShowWindow(hwnd, SW_SHOW);

            IntPtr foreground = GetForegroundWindow();
            if (foreground == hwnd)
                return true;

            uint dummyPid;
            uint foregroundThread = GetWindowThreadProcessId(foreground, out dummyPid);
            uint targetThread = GetWindowThreadProcessId(hwnd, out dummyPid);
            uint currentThread = GetCurrentThreadId();

            bool attached = false;
            try
            {
                if (foregroundThread != targetThread)
                    attached = AttachThreadInput(currentThread, targetThread, true);

                BringWindowToTop(hwnd);
                SetForegroundWindow(hwnd);
            }
            finally
            {
                if (attached)
                    AttachThreadInput(currentThread, targetThread, false);
            }

            return GetForegroundWindow() == hwnd;
        }

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        public static void SendKeyInput(ushort vk, bool keyUp)
        {
            var input = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = vk,
                        wScan = (ushort)MapVirtualKey(vk, 0),
                        dwFlags = keyUp ? KEYEVENTF_KEYUP : 0
                    }
                }
            };
            SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
        }

        public static void SendUnicodeChar(char ch)
        {
            var down = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wScan = ch,
                        dwFlags = KEYEVENTF_UNICODE
                    }
                }
            };
            var up = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wScan = ch,
                        dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP
                    }
                }
            };
            SendInput(2, new[] { down, up }, Marshal.SizeOf(typeof(INPUT)));
        }
    }
}

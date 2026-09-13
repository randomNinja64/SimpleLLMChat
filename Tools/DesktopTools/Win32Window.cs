using System;
using System.Collections.Generic;
using System.Text;

namespace DesktopTools
{
  /// <summary>
  /// HWND helpers: identity, text, geometry, enumeration, DPI, and focus.
  /// </summary>
  internal static partial class Win32Interop
  {
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
        // EnumChildWindows includes all descendants; the caller recurses by level.
        if (GetParent(hWnd) == parent)
          list.Add(hWnd);
        return true;
      }, IntPtr.Zero);
      return list;
    }

    /// <summary>
    /// Select a list or combo item by caption via LB_/CB_ messages. UIA SelectionItem on
    /// XP WinForms ListBox items often reports success without changing SelectedIndex.
    /// </summary>
    public static bool TrySelectNamedItem(IntPtr hwnd, string name)
    {
      if (hwnd == IntPtr.Zero || !IsWindow(hwnd) || string.IsNullOrEmpty(name))
        return false;

      string className = GetControlClass(hwnd);
      if (IsComboBoxClass(className))
        return TryComboSelect(hwnd, name);

      if (IsListBoxClass(className))
      {
        if (TryListSelect(hwnd, name))
          return true;

        IntPtr parent = GetParent(hwnd);
        if (parent != IntPtr.Zero && IsComboBoxClass(GetControlClass(parent)))
          return TryComboSelect(parent, name);
      }

      return false;
    }

    private static bool IsListBoxClass(string className)
    {
      if (string.IsNullOrEmpty(className))
        return false;
      return className.Equals("ListBox", StringComparison.OrdinalIgnoreCase) ||
             className.Equals("ComboLBox", StringComparison.OrdinalIgnoreCase) ||
             className.StartsWith("WindowsForms10.LISTBOX", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsComboBoxClass(string className)
    {
      if (string.IsNullOrEmpty(className))
        return false;
      return className.Equals("ComboBox", StringComparison.OrdinalIgnoreCase) ||
             className.StartsWith("WindowsForms10.COMBOBOX", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryListSelect(IntPtr hwnd, string name)
    {
      int index = (int)SendMessage(hwnd, LB_FINDSTRINGEXACT, (IntPtr)(-1), name);
      if (index < 0)
        return false;
      if ((int)SendMessage(hwnd, LB_SETCURSEL, (IntPtr)index, IntPtr.Zero) == LB_ERR)
        return false;
      NotifySelectionChanged(hwnd, LBN_SELCHANGE);
      return true;
    }

    private static bool TryComboSelect(IntPtr hwnd, string name)
    {
      int index = (int)SendMessage(hwnd, CB_FINDSTRINGEXACT, (IntPtr)(-1), name);
      if (index < 0)
        return false;
      if ((int)SendMessage(hwnd, CB_SETCURSEL, (IntPtr)index, IntPtr.Zero) == LB_ERR)
        return false;
      NotifySelectionChanged(hwnd, CBN_SELCHANGE);
      return true;
    }

    private static void NotifySelectionChanged(IntPtr hwnd, int notification)
    {
      IntPtr parent = GetParent(hwnd);
      if (parent == IntPtr.Zero)
        return;
      int id = GetDlgCtrlID(hwnd);
      IntPtr wParam = (IntPtr)((notification << 16) | (id & 0xFFFF));
      SendMessage(parent, WM_COMMAND, wParam, hwnd);
    }

    public static bool SetControlText(IntPtr hwnd, string text)
    {
      IntPtr result;
      IntPtr sent = SendMessageTimeout(
        hwnd, WM_SETTEXT, IntPtr.Zero, text ?? "",
        SMTO_ABORTIFHUNG, DefaultSendTimeoutMs, out result);
      return sent != IntPtr.Zero && result != IntPtr.Zero;
    }

    public static bool FocusWindow(IntPtr hwnd)
    {
      if (!IsWindow(hwnd))
        return false;

      IntPtr root = GetAncestor(hwnd, 2); // GA_ROOT excludes an owned dialog's owner.
      ShowWindow(root, SW_RESTORE);
      ShowWindow(root, SW_SHOW);

      IntPtr foreground = GetForegroundWindow();
      if (foreground == hwnd && root == hwnd)
        return true;

      uint dummyPid;
      uint targetThread = GetWindowThreadProcessId(hwnd, out dummyPid);
      uint currentThread = GetCurrentThreadId();

      bool attached = false;
      try
      {
        if (currentThread != targetThread)
          attached = AttachThreadInput(currentThread, targetThread, true);

        BringWindowToTop(root);
        SetForegroundWindow(root);
        SetFocus(hwnd);
        return GetFocus() == hwnd;
      }
      finally
      {
        if (attached)
          AttachThreadInput(currentThread, targetThread, false);
      }

    }
  }
}

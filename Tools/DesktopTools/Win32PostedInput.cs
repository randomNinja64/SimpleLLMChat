using System;

namespace DesktopTools
{
  /// <summary>
  /// Cursor-free input via PostMessage for classic Win32/WinForms controls.
  /// </summary>
  internal static partial class Win32Interop
  {
    /// <summary>
    /// Scroll without moving the cursor via WM_VSCROLL/WM_HSCROLL for classic controls
    /// that honor those messages. Returns false for other classes so callers can SendInput.
    /// </summary>
    public static bool TryPostScroll(IntPtr hwnd, ScrollDirection direction, int amount)
    {
      if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
        return false;
      if (amount < 1)
        amount = 1;

      return TryPostScrollBar(hwnd, direction, amount);
    }

    private static bool AcceptsPostedMouse(IntPtr hwnd)
    {
      if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
        return false;
      return AcceptsPostedMouseClass(GetControlClass(hwnd));
    }

    private static bool TryPostScrollBar(IntPtr hwnd, ScrollDirection direction, int amount)
    {
      if (!AcceptsScrollBarMessages(GetControlClass(hwnd)))
        return false;

      int msg;
      int code;
      switch (direction)
      {
        case ScrollDirection.Up:
          msg = WM_VSCROLL;
          code = SB_LINEUP;
          break;
        case ScrollDirection.Left:
          msg = WM_HSCROLL;
          code = SB_LINELEFT;
          break;
        case ScrollDirection.Right:
          msg = WM_HSCROLL;
          code = SB_LINERIGHT;
          break;
        default:
          msg = WM_VSCROLL;
          code = SB_LINEDOWN;
          break;
      }

      for (int i = 0; i < amount; i++)
      {
        if (!PostMessage(hwnd, msg, (IntPtr)code, IntPtr.Zero))
          return false;
      }

      return true;
    }

    private static bool AcceptsScrollBarMessages(string className)
    {
      return AcceptsPostedMouseClass(className) &&
             !className.Equals("Button", StringComparison.OrdinalIgnoreCase) &&
             !className.Equals("Static", StringComparison.OrdinalIgnoreCase) &&
             !className.StartsWith("WindowsForms10.BUTTON", StringComparison.OrdinalIgnoreCase) &&
             !className.StartsWith("WindowsForms10.STATIC", StringComparison.OrdinalIgnoreCase);
    }

    private static bool AcceptsPostedMouseClass(string className)
    {
      if (string.IsNullOrEmpty(className))
        return false;

      switch (className.ToLowerInvariant())
      {
        case "button":
        case "static":
        case "listbox":
        case "combolbox":
        case "combobox":
        case "edit":
        case "richedit":
        case "richedit20a":
        case "richedit20w":
        case "richedit50w":
        case "syslistview32":
        case "systreeview32":
        case "systabcontrol32":
        case "toolbarwindow32":
        case "scrollbar":
          return true;
        default:
          return className.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase) ||
                 className.StartsWith("WindowsForms10.", StringComparison.OrdinalIgnoreCase);
      }
    }

    private static bool TryResolveClientPoint(
      IntPtr hwnd, int screenX, int screenY, out IntPtr target, out POINT client)
    {
      target = hwnd;
      client = new POINT { X = screenX, Y = screenY };
      if (!ScreenToClient(hwnd, ref client))
        return false;

      try
      {
        IntPtr child = RealChildWindowFromPoint(hwnd, client);
        if (child != IntPtr.Zero && child != hwnd && IsWindow(child))
        {
          target = child;
          client = new POINT { X = screenX, Y = screenY };
          if (!ScreenToClient(target, ref client))
            return false;
        }
      }
      catch
      {
      }

      return true;
    }

    /// <summary>
    /// Click without moving the system cursor: PostMessage button down/up in client coords.
    /// Only for classic Win32/WinForms classes that typically honor posted clicks.
    /// </summary>
    public static bool TryPostMouseClick(IntPtr hwnd, int screenX, int screenY, MouseButton button, int clickCount)
    {
      if (!AcceptsPostedMouse(hwnd))
        return false;
      if (clickCount < 1)
        clickCount = 1;

      IntPtr target;
      POINT client;
      if (!TryResolveClientPoint(hwnd, screenX, screenY, out target, out client))
        return false;

      if (!AcceptsPostedMouse(target))
        target = hwnd;

      int downMsg, upMsg, dblMsg, downKeys;
      GetButtonMessages(button, out downMsg, out upMsg, out dblMsg, out downKeys);

      IntPtr lp = (IntPtr)((client.Y << 16) | (client.X & 0xFFFF));
      IntPtr wDown = (IntPtr)downKeys;

      if (clickCount == 1)
      {
        if (!PostMessage(target, downMsg, wDown, lp))
          return false;
        return PostMessage(target, upMsg, IntPtr.Zero, lp);
      }

      if (!PostMessage(target, downMsg, wDown, lp) ||
          !PostMessage(target, upMsg, IntPtr.Zero, lp) ||
          !PostMessage(target, dblMsg, wDown, lp) ||
          !PostMessage(target, upMsg, IntPtr.Zero, lp))
        return false;

      for (int i = 2; i < clickCount; i++)
      {
        if (!PostMessage(target, downMsg, wDown, lp) ||
            !PostMessage(target, upMsg, IntPtr.Zero, lp))
          return false;
      }

      return true;
    }

    private static void GetButtonMessages(
      MouseButton button, out int downMsg, out int upMsg, out int dblMsg, out int downKeys)
    {
      switch (button)
      {
        case MouseButton.Right:
          downMsg = WM_RBUTTONDOWN;
          upMsg = WM_RBUTTONUP;
          dblMsg = WM_RBUTTONDBLCLK;
          downKeys = MK_RBUTTON;
          break;
        case MouseButton.Middle:
          downMsg = WM_MBUTTONDOWN;
          upMsg = WM_MBUTTONUP;
          dblMsg = WM_MBUTTONDBLCLK;
          downKeys = MK_MBUTTON;
          break;
        default:
          downMsg = WM_LBUTTONDOWN;
          upMsg = WM_LBUTTONUP;
          dblMsg = WM_LBUTTONDBLCLK;
          downKeys = MK_LBUTTON;
          break;
      }
    }
  }
}

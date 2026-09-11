using System;
using System.Collections.Generic;

namespace DesktopTools
{
  internal static class OutputFormat
  {
    private const int MaxTextLength = 80;

    private static readonly HashSet<string> NoiseClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
      "CtrlNotifySink",
      "DirectUIHWND",
      "DUIViewWndClassName",
      "TITLE_BAR_SCAFFOLDING_WINDOW_CLASS",
      "InputNonClientPointerSource",
      "DummyDWMListenerWindow",
      "EdgeUiInputTopWndClass"
    };

    /// <summary>
    /// Visible top-level windows worth listing. Untitled standard dialogs (#32770) are kept
    /// so Save/Open/MessageBox surfaces are discoverable.
    /// </summary>
    public static bool ShouldIncludeWindow(string title, string className)
    {
      if (!string.IsNullOrWhiteSpace(title))
        return true;
      return UiChanges.IsDialogClass(className);
    }

    public static bool IsInteresting(ControlInfo info)
    {
      if (!string.IsNullOrEmpty(info.Label))
        return true;
      return !NoiseClasses.Contains(info.Role);
    }

    public static string FormatWindowLine(IntPtr hwnd, string title)
    {
      return FormatWindowLine(hwnd, title, -1, -1);
    }

    public static string FormatWindowLine(IntPtr hwnd, string title, int width, int height)
    {
      string line = Win32Interop.FormatHwnd(hwnd) + " \"" + Truncate(Escape(title)) + "\"";
      if (width > 0 && height > 0)
        line += " " + width + "x" + height;
      return line;
    }

    /// <summary>
    /// Top-level window line for list_windows / change notes. Untitled dialogs use (dialog).
    /// </summary>
    public static string FormatTopLevelWindow(IntPtr hwnd, string title, string className)
    {
      if (!string.IsNullOrWhiteSpace(title))
        return FormatWindowLine(hwnd, title);

      string label = UiChanges.IsDialogClass(className) ? "dialog" : (className ?? "window");
      return Win32Interop.FormatHwnd(hwnd) + " (" + label + ")";
    }

    /// <summary>
    /// Screenshot caption: hwnd, title, and capture size in image space.
    /// </summary>
    public static string FormatCaptureCaption(IntPtr hwnd, string title, int width, int height)
    {
      return Win32Interop.FormatHwnd(hwnd) + " \"" + Escape(title) + "\" " + width + "x" + height;
    }

    /// <summary>
    /// When originLeft/originTop are the window outer-rect origin, printed @x,y are
    /// capture-relative (same space as a window screenshot).
    /// </summary>
    public static string FormatControlLine(ControlInfo info, int originLeft, int originTop)
    {
      var parts = new List<string>();
      parts.Add(new string(' ', Math.Max(0, info.Depth) * 2) + "#" + info.Index);
      parts.Add(string.IsNullOrEmpty(info.Role) ? "element" : info.Role);

      if (!string.IsNullOrEmpty(info.Label))
        parts.Add("\"" + Truncate(Escape(info.Label)) + "\"");

      if (info.ReportsValue)
        parts.Add("= \"" + Truncate(Escape(info.Value)) + "\"");

      if (info.HasLocation)
        parts.Add("@" + (info.X - originLeft) + "," + (info.Y - originTop) + " " + info.Width + "x" + info.Height);

      if (!info.Enabled)
        parts.Add("[off]");

      return string.Join(" ", parts.ToArray());
    }

    /// <summary>Maps a Win32 class name onto a UIA-style role so both trees read alike.</summary>
    public static string RoleFromClassName(string className)
    {
      if (string.IsNullOrEmpty(className))
        return "element";

      if (className.StartsWith("HwndWrapper[", StringComparison.OrdinalIgnoreCase))
        return "window";

      if (className.StartsWith("Chrome_WidgetWin", StringComparison.OrdinalIgnoreCase))
        return "chrome";

      if (className.StartsWith("MozillaWindowClass", StringComparison.OrdinalIgnoreCase))
        return "mozilla";

      if (className.StartsWith("WindowsForms10.", StringComparison.OrdinalIgnoreCase))
      {
        int start = "WindowsForms10.".Length;
        int end = className.IndexOf('.', start);
        if (end > start)
          className = className.Substring(start, end - start);
      }

      switch (className.ToLowerInvariant())
      {
        case "edit":
        case "richedit20w":
          return "edit";
        case "button":
          return "button";
        case "combobox":
          return "combobox";
        case "listbox":
          return "list";
        case "static":
          return "text";
        case "syslistview32":
          return "listview";
        case "systreeview32":
          return "tree";
        case "toolbarwindow32":
          return "toolbar";
        case "msctls_trackbar32":
          return "slider";
        default:
          return className.ToLowerInvariant();
      }
    }

    private static string Truncate(string value)
    {
      if (string.IsNullOrEmpty(value) || value.Length <= MaxTextLength)
        return value;
      return value.Substring(0, MaxTextLength) + "...";
    }

    private static string Escape(string value)
    {
      if (string.IsNullOrEmpty(value))
        return "";
      return value.Replace("\\", "\\\\").Replace("\"", "'").Replace("\r", " ").Replace("\n", " ");
    }
  }
}

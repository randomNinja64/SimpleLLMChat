using System;
using System.Collections.Generic;
using System.Text;

namespace DesktopTools
{
  internal struct WindowTarget
  {
    public string HwndText;
    public string WindowTitle;

    public bool IsEmpty
    {
      get { return string.IsNullOrEmpty(HwndText) && string.IsNullOrEmpty(WindowTitle); }
    }
  }

  internal static class WindowEnumerator
  {
    private struct WindowEntry
    {
      public IntPtr Hwnd;
      public string Title;
      public string Line;
    }

    public static string ListWindows(string titleContains)
    {
      var entries = Collect(titleContains);
      if (entries.Count == 0)
        return "No matching windows found.";

      var sb = new StringBuilder();
      sb.AppendLine(entries.Count + " windows");
      foreach (WindowEntry entry in entries)
        sb.AppendLine(entry.Line);
      return sb.ToString().TrimEnd();
    }

    public static string GetDesktopContext()
    {
      var entries = Collect("", foregroundFirst: true);
      if (entries.Count == 0)
        return "";

      const int maxShown = 5;
      int shown = Math.Min(entries.Count, maxShown);

      var sb = new StringBuilder("Currently Open: ");
      for (int i = 0; i < shown; i++)
      {
        if (i > 0)
          sb.Append(", ");
        sb.Append(entries[i].Title);
      }

      int remaining = entries.Count - shown;
      if (remaining > 0)
        sb.Append(" and ").Append(remaining).Append(" more");

      return sb.ToString();
    }

    public static WindowTarget ParseWindowTarget(string argumentsJson)
    {
      string hwndText = ToolHelper.JsonExtractString(argumentsJson, "hwnd");
      string windowTitle = ToolHelper.JsonExtractString(argumentsJson, "window_title");
      string titleContains = ToolHelper.JsonExtractString(argumentsJson, "title_contains");

      return new WindowTarget
      {
        HwndText = string.IsNullOrWhiteSpace(hwndText) ? "" : hwndText.Trim(),
        WindowTitle = CoalesceWindowTitle(windowTitle, titleContains)
      };
    }

    public static string CoalesceWindowTitle(string windowTitle, string titleContains)
    {
      if (!string.IsNullOrWhiteSpace(windowTitle))
        return windowTitle.Trim();
      if (!string.IsNullOrWhiteSpace(titleContains))
        return titleContains.Trim();
      return "";
    }

    public static IntPtr ResolveWindow(string hwndText, string windowTitle)
    {
      if (!string.IsNullOrWhiteSpace(hwndText))
      {
        IntPtr hwnd = Win32Interop.ParseHwnd(hwndText);
        if (!Win32Interop.IsWindow(hwnd))
          throw new InvalidOperationException("hwnd " + Win32Interop.FormatHwnd(hwnd) + " is not a valid window.");
        return hwnd;
      }

      if (!string.IsNullOrWhiteSpace(windowTitle))
        return ResolveByTitle(windowTitle);

      throw new ArgumentException("provide hwnd or window_title.");
    }

    private static IntPtr ResolveByTitle(string windowTitle)
    {
      var matches = Collect(windowTitle);

      if (matches.Count == 0)
        throw new InvalidOperationException("no visible window found matching title: '" + windowTitle + "'.");

      if (matches.Count == 1)
        return matches[0].Hwnd;

      IntPtr exact = IntPtr.Zero;
      int exactCount = 0;
      foreach (WindowEntry entry in matches)
      {
        if (!entry.Title.Equals(windowTitle, StringComparison.OrdinalIgnoreCase))
          continue;
        exact = entry.Hwnd;
        exactCount++;
      }

      if (exactCount == 1)
        return exact;

      IntPtr foreground = Win32Interop.GetForegroundWindow();
      foreach (WindowEntry entry in matches)
      {
        if (entry.Hwnd == foreground)
          return entry.Hwnd;
      }

      var sb = new StringBuilder();
      sb.Append("multiple windows match '").Append(windowTitle).Append("': ");
      for (int i = 0; i < matches.Count; i++)
      {
        if (i > 0)
          sb.Append("; ");
        sb.Append(matches[i].Line);
      }
      sb.Append(". Use a more specific window_title or hwnd.");
      throw new InvalidOperationException(sb.ToString());
    }

    private static List<WindowEntry> Collect(string titleContains, bool foregroundFirst = false)
    {
      var entries = new List<WindowEntry>();

      foreach (IntPtr hwnd in Win32Interop.EnumerateTopLevelWindows())
      {
        string title = Win32Interop.GetWindowTitle(hwnd);
        string className = Win32Interop.GetControlClass(hwnd);

        if (!OutputFormat.ShouldIncludeWindow(title, className))
          continue;

        if (!string.IsNullOrEmpty(titleContains) &&
            title.IndexOf(titleContains, StringComparison.OrdinalIgnoreCase) < 0)
          continue;

        entries.Add(new WindowEntry
        {
          Hwnd = hwnd,
          Title = string.IsNullOrWhiteSpace(title) ? "(dialog)" : title,
          Line = OutputFormat.FormatTopLevelWindow(hwnd, title, className)
        });
      }

      if (foregroundFirst)
        MoveForegroundFirst(entries);

      return entries;
    }

    private static void MoveForegroundFirst(List<WindowEntry> entries)
    {
      IntPtr foreground = Win32Interop.GetForegroundWindow();
      for (int i = 0; i < entries.Count; i++)
      {
        if (entries[i].Hwnd != foreground)
          continue;

        WindowEntry entry = entries[i];
        entries.RemoveAt(i);
        entries.Insert(0, entry);
        return;
      }
    }
  }
}

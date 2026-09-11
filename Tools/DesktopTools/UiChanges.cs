using System;
using System.Collections.Generic;
using System.Threading;

namespace DesktopTools
{
  /// <summary>
  /// After pointer/key actions, note new top-level windows (dialogs) and foreground
  /// changes so the model can retarget without a blind list_windows call.
  /// </summary>
  internal static class UiChanges
  {
    private const int SettleMs = 250;
    private const int MaxNewWindows = 4;

    public struct Snapshot
    {
      public Dictionary<long, string> Windows;
      public IntPtr Foreground;
    }

    public static Snapshot Capture()
    {
      var windows = new Dictionary<long, string>();
      foreach (IntPtr hwnd in Win32Interop.EnumerateTopLevelWindows())
      {
        string title = Win32Interop.GetWindowTitle(hwnd);
        string className = Win32Interop.GetControlClass(hwnd);
        if (!OutputFormat.ShouldIncludeWindow(title, className))
          continue;

        windows[hwnd.ToInt64()] = OutputFormat.FormatTopLevelWindow(hwnd, title, className);
      }

      return new Snapshot
      {
        Windows = windows,
        Foreground = Win32Interop.GetForegroundWindow()
      };
    }

    /// <summary>
    /// Append change notes to a successful action result. Errors are returned unchanged.
    /// </summary>
    public static string Annotate(string result, Snapshot before, IntPtr relatedWindow)
    {
      if (string.IsNullOrEmpty(result) ||
          result.StartsWith("error:", StringComparison.OrdinalIgnoreCase))
        return result;

      Thread.Sleep(SettleMs);
      Snapshot after = Capture();

      var notes = new List<string>();
      var reported = new HashSet<long>();

      IntPtr popup = IntPtr.Zero;
      if (relatedWindow != IntPtr.Zero && Win32Interop.IsWindow(relatedWindow))
      {
        try
        {
          popup = Win32Interop.GetLastActivePopup(relatedWindow);
        }
        catch
        {
          popup = IntPtr.Zero;
        }
      }

      if (popup != IntPtr.Zero &&
          popup != relatedWindow &&
          Win32Interop.IsWindowVisible(popup))
      {
        long key = popup.ToInt64();
        string line;
        if (!after.Windows.TryGetValue(key, out line))
          line = OutputFormat.FormatTopLevelWindow(popup, Win32Interop.GetWindowTitle(popup), Win32Interop.GetControlClass(popup));
        notes.Add("dialog " + line);
        reported.Add(key);
      }

      int newCount = 0;
      foreach (KeyValuePair<long, string> pair in after.Windows)
      {
        if (before.Windows != null && before.Windows.ContainsKey(pair.Key))
          continue;
        if (reported.Contains(pair.Key))
          continue;
        if (newCount >= MaxNewWindows)
          break;

        notes.Add("new " + pair.Value);
        reported.Add(pair.Key);
        newCount++;
      }

      if (after.Foreground != IntPtr.Zero &&
          after.Foreground != before.Foreground &&
          !reported.Contains(after.Foreground.ToInt64()))
      {
        long key = after.Foreground.ToInt64();
        string line;
        if (!after.Windows.TryGetValue(key, out line))
          line = OutputFormat.FormatTopLevelWindow(
            after.Foreground,
            Win32Interop.GetWindowTitle(after.Foreground),
            Win32Interop.GetControlClass(after.Foreground));
        notes.Add("foreground " + line);
      }

      if (notes.Count == 0)
        return result;

      return result + " | " + string.Join("; ", notes.ToArray());
    }

    public static bool IsDialogClass(string className)
    {
      if (string.IsNullOrEmpty(className))
        return false;
      if (className == "#32770")
        return true;
      return className.IndexOf("Dialog", StringComparison.OrdinalIgnoreCase) >= 0;
    }
  }
}

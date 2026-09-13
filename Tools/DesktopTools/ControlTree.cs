using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Automation;

namespace DesktopTools
{
  internal class ControlInfo
  {
    public int Index;
    public int Depth;
    public string Role = "";
    public string Label = "";
    public string Value = "";
    public IntPtr Hwnd;
    public bool IsNativeWindow;
    public bool SupportsNativeDiscovery;
    public bool Enabled = true;
    public int X;
    public int Y;
    public int Width;
    public int Height;
    public bool HasLocation;
    public AutomationElement Element;

    /// <summary>True when the element carries content beyond its own caption.</summary>
    public bool ReportsValue
    {
      get { return !string.IsNullOrEmpty(Value) && !Value.Equals(Label, StringComparison.Ordinal); }
    }

    public bool TryGetCenter(out int x, out int y)
    {
      x = X + Width / 2;
      y = Y + Height / 2;
      return HasLocation;
    }
  }

  /// <summary>
  /// Depth and size caps used when indexing controls. list_controls, navigate and
  /// set_control_text must share the same scope or element #N drifts between calls.
  /// </summary>
  internal struct TreeScope
  {
    public int MaxDepth;
    public int MaxControls;
  }

  /// <summary>
  /// Uses native discovery for recognized simple controls and UI Automation for richer
  /// or unknown interfaces. Listing and element resolution share this selection policy.
  /// </summary>
  internal static class ControlTree
  {
    /// <summary>
    /// Resolves max_depth from the call args (override) or the maxtreedepth config default.
    /// Pass the same max_depth used for list_controls when targeting by element index.
    /// </summary>
    public static TreeScope ParseScope(string argumentsJson)
    {
      int maxDepth = ToolHelper.GetConfigInt("maxtreedepth", 8);
      int maxControls = ToolHelper.GetConfigInt("maxcontrols", 80);

      string depthStr = ToolHelper.JsonExtractString(argumentsJson, "max_depth")?.Trim() ?? "";
      int parsedDepth;
      if (int.TryParse(depthStr, out parsedDepth) && parsedDepth >= 0)
        maxDepth = parsedDepth;

      return new TreeScope { MaxDepth = maxDepth, MaxControls = maxControls };
    }

    public static List<ControlInfo> Collect(IntPtr windowHwnd, int maxDepth, int maxControls)
    {
      List<ControlInfo> controls = null;
      // Inspect only familiar native hosts. Unknown/custom and virtual controls retain UIA.
      string host = Win32Interop.GetControlClass(windowHwnd);
      if (host == "#32770" || host.StartsWith("WindowsForms10.", StringComparison.OrdinalIgnoreCase))
      {
        List<ControlInfo> native = CollectWin32(windowHwnd, maxDepth);
        if (native.Count > 0 && native.TrueForAll(c => c.SupportsNativeDiscovery))
          controls = native;
      }
      if (controls == null)
        controls = UiaInterop.WalkTree(windowHwnd, maxDepth);

      if (controls.Count == 0)
        controls = CollectWin32(windowHwnd, maxDepth);

      int before = controls.Count;
      TruncateDeepest(controls, maxControls);
      bool truncated = before > controls.Count;

      for (int i = 0; i < controls.Count; i++)
        controls[i].Index = i + 1;

      if (truncated)
        controls.Add(TruncationMarker.Instance);

      return controls;
    }

    public static ControlInfo Find(IntPtr windowHwnd, int index, int maxDepth, int maxControls)
    {
      if (index < 1)
        return null;

      List<ControlInfo> controls = Collect(windowHwnd, maxDepth, maxControls);
      if (index > controls.Count)
        return null;

      ControlInfo control = controls[index - 1];
      return control == TruncationMarker.Instance ? null : control;
    }

    public static string Render(IntPtr windowHwnd, int maxDepth, int maxControls)
    {
      int originLeft, originTop, originW, originH;
      if (!Win32Interop.TryGetWindowOrigin(windowHwnd, out originLeft, out originTop, out originW, out originH))
      {
        originLeft = 0;
        originTop = 0;
        originW = 0;
        originH = 0;
      }

      var sb = new StringBuilder();
      sb.AppendLine(OutputFormat.FormatWindowLine(
        windowHwnd, Win32Interop.GetWindowTitle(windowHwnd), originW, originH));

      if (maxDepth == 0)
        return sb.Append(OutputFormat.FormatControlLine(DescribeWindow(windowHwnd), originLeft, originTop)).ToString();

      List<ControlInfo> controls = Collect(windowHwnd, maxDepth, maxControls);
      if (controls.Count == 0)
        return sb.Append("(no controls)").ToString();

      foreach (ControlInfo control in controls)
      {
        if (control == TruncationMarker.Instance)
        {
          sb.AppendLine("(truncated)");
          continue;
        }

        sb.AppendLine(OutputFormat.FormatControlLine(control, originLeft, originTop));
      }

      return sb.ToString().TrimEnd();
    }

    /// <summary>Drops the deepest controls first so shallow, actionable targets survive the cap.</summary>
    private static void TruncateDeepest(List<ControlInfo> controls, int maxControls)
    {
      while (controls.Count > maxControls)
      {
        int deepest = 0;
        for (int i = 0; i < controls.Count; i++)
        {
          if (controls[i].Depth > deepest)
            deepest = controls[i].Depth;
        }

        bool removed = false;
        for (int i = controls.Count - 1; i >= 0 && controls.Count > maxControls; i--)
        {
          if (controls[i].Depth != deepest)
            continue;

          controls.RemoveAt(i);
          removed = true;
        }

        if (!removed)
          break;
      }
    }

    private static class TruncationMarker
    {
      public static readonly ControlInfo Instance = new ControlInfo();
    }

    private static ControlInfo DescribeWindow(IntPtr hwnd)
    {
      ControlInfo info = FromHwnd(hwnd, 0);
      info.Index = 1;
      return info;
    }

    private static List<ControlInfo> CollectWin32(IntPtr windowHwnd, int maxDepth)
    {
      var controls = new List<ControlInfo>();
      CollectWin32Children(windowHwnd, 1, maxDepth, controls);
      return controls;
    }

    private static void CollectWin32Children(IntPtr parent, int depth, int maxDepth, List<ControlInfo> controls)
    {
      if (depth > maxDepth)
        return;

      foreach (IntPtr child in Win32Interop.EnumerateChildControls(parent))
      {
        ControlInfo info = FromHwnd(child, depth);
        if (OutputFormat.IsInteresting(info))
          controls.Add(info);

        CollectWin32Children(child, depth + 1, maxDepth, controls);
      }
    }

    private static ControlInfo FromHwnd(IntPtr hwnd, int depth)
    {
      var info = new ControlInfo
      {
        Hwnd = hwnd,
        IsNativeWindow = true,
        Depth = depth,
        Role = OutputFormat.RoleFromClassName(Win32Interop.GetControlClass(hwnd)),
        Label = Win32Interop.GetControlText(hwnd),
        Enabled = Win32Interop.IsWindowEnabled(hwnd)
      };

      MsaaInterop.DescribeWindow(hwnd, info);

      Win32Interop.RECT rect;
      if (Win32Interop.GetWindowRect(hwnd, out rect))
      {
        info.X = rect.Left;
        info.Y = rect.Top;
        info.Width = rect.Right - rect.Left;
        info.Height = rect.Bottom - rect.Top;
        info.HasLocation = info.Width > 0 && info.Height > 0;
      }

      return info;
    }
  }
}

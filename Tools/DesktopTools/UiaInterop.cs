using System;
using System.Collections.Generic;
using System.Windows.Automation;

namespace DesktopTools
{
  /// <summary>
  /// UI Automation is the primary discovery and action layer. It covers WPF natively and
  /// classic Win32/WinForms through the client-side providers shipped with .NET.
  /// </summary>
  internal static class UiaInterop
  {
    private const int DefaultActionTimeoutMs = 2000;

    public static List<ControlInfo> WalkTree(IntPtr windowHwnd, int maxDepth)
    {
      var controls = new List<ControlInfo>();
      if (windowHwnd == IntPtr.Zero || maxDepth < 1)
        return controls;

      try
      {
        AutomationElement root = AutomationElement.FromHandle(windowHwnd);
        if (root != null)
          Walk(root, null, 1, maxDepth, Frame.For(windowHwnd, Win32Interop.GetProviderScale(windowHwnd)), controls);
      }
      catch
      {
        // Target closed, denied, or UI Automation unavailable: caller falls back to Win32.
      }

      return controls;
    }

    private static void Walk(AutomationElement parent, ControlInfo parentInfo, int depth, int maxDepth, Frame frame, List<ControlInfo> controls)
    {
      if (depth > maxDepth)
        return;

      AutomationElement child = Step(delegate { return TreeWalker.ControlViewWalker.GetFirstChild(parent); });

      while (child != null)
      {
        ControlInfo info = Describe(child, depth, frame);

        if (info != null && !IsChrome(info.Role))
        {
          if (!EchoesLabel(info, parentInfo))
            controls.Add(info);

          // An element that reports its own value has already told us everything its
          // children would. Descending anyway is what makes grids explode: a row hands
          // back all of its cells joined together, then each cell repeats one of them.
          if (!info.ReportsValue)
          {
            Frame inner = info.Hwnd == IntPtr.Zero ? frame : Frame.For(info.Hwnd, frame.Scale);
            int before = controls.Count;
            Walk(child, info, depth + 1, maxDepth, inner, controls);

            // DataGridView often only shows scrollbars in ControlView. Prefer GridPattern,
            // then MSAA (needed on XP where GridPattern is missing).
            if ((info.Role == "table" || info.Role == "datagrid") && depth < maxDepth)
              TryAddGridRows(child, depth + 1, inner, controls, before);
          }
        }

        AutomationElement current = child;
        child = Step(delegate { return TreeWalker.ControlViewWalker.GetNextSibling(current); });
      }
    }

    /// <summary>Rendering furniture that is never a useful automation target, subtree included.</summary>
    private static bool IsChrome(string role)
    {
      switch (role)
      {
        case "scrollbar":
        case "thumb":
        case "separator":
          return true;
        default:
          return false;
      }
    }

    private static void TryAddGridRows(
      AutomationElement table, int depth, Frame frame, List<ControlInfo> controls, int before)
    {
      for (int i = before; i < controls.Count; i++)
        if (controls[i].ReportsValue)
          return;

      if (TryAddGridRowsViaPattern(table, depth, frame, controls))
        return;

      IntPtr hwnd = IntPtr.Zero;
      try
      {
        hwnd = new IntPtr(table.Current.NativeWindowHandle);
      }
      catch
      {
      }

      if (hwnd == IntPtr.Zero)
        hwnd = FindContainerHwnd(table);

      MsaaInterop.TryAppendGridRows(hwnd, depth, controls);
    }

    private static bool TryAddGridRowsViaPattern(
      AutomationElement table, int depth, Frame frame, List<ControlInfo> controls)
    {
      object pattern;
      try
      {
        if (!table.TryGetCurrentPattern(GridPattern.Pattern, out pattern))
          return false;
      }
      catch
      {
        return false;
      }

      var grid = (GridPattern)pattern;
      int rows, cols;
      try
      {
        rows = grid.Current.RowCount;
        cols = grid.Current.ColumnCount;
      }
      catch
      {
        return false;
      }

      if (rows < 1 || cols < 1)
        return false;
      if (rows > 500)
        rows = 500;

      int added = 0;
      for (int r = 0; r < rows; r++)
      {
        try
        {
          var parts = new string[cols];
          AutomationElement first = null;
          for (int c = 0; c < cols; c++)
          {
            AutomationElement cell = grid.GetItem(r, c);
            if (c == 0)
              first = cell;
            string text = cell == null ? "" : ReadValue(cell);
            if (string.IsNullOrEmpty(text) && cell != null)
              text = cell.Current.Name ?? "";
            parts[c] = text ?? "";
          }

          ControlInfo row = first != null ? Describe(first, depth, frame) : null;
          if (row == null)
            row = new ControlInfo { Depth = depth, Role = "custom", Element = first };
          row.Role = "custom";
          row.Label = "Row " + r;
          row.Value = string.Join(";", parts);
          row.Element = first;
          controls.Add(row);
          added++;
        }
        catch
        {
        }
      }

      return added > 0;
    }

    /// <summary>The label element a framework nests inside a control to draw its own caption.</summary>
    private static bool EchoesLabel(ControlInfo info, ControlInfo parent)
    {
      if (parent == null)
        return false;

      switch (info.Role)
      {
        case "text":
        case "header":
        case "headeritem":
          break;
        default:
          return false;
      }

      return string.IsNullOrEmpty(info.Value) &&
             !string.IsNullOrEmpty(info.Label) &&
             info.Label.Equals(parent.Label, StringComparison.Ordinal);
    }

    private static AutomationElement Step(Func<AutomationElement> move)
    {
      try
      {
        return move();
      }
      catch
      {
        return null;
      }
    }

    /// <summary>The window an element's rectangles should make sense within, in physical pixels.</summary>
    private class Frame
    {
      public IntPtr Hwnd;
      public double Scale;
      private Win32Interop.RECT _rect;
      private bool _known;

      public static Frame For(IntPtr hwnd, double scale)
      {
        var frame = new Frame { Hwnd = hwnd, Scale = scale };
        frame._known = Win32Interop.GetWindowRect(hwnd, out frame._rect);
        return frame;
      }

      public bool Covers(double x, double y)
      {
        return _known && x >= _rect.Left && x <= _rect.Right && y >= _rect.Top && y <= _rect.Bottom;
      }
    }

    /// <summary>
    /// In a DPI-unaware target, rectangles from the app's own accessibility provider arrive in
    /// its virtualized space while the non-client ones Windows supplies are already physical.
    /// Rather than guess by role, keep whichever reading actually lands inside the frame.
    /// </summary>
    private static double ScaleFor(System.Windows.Rect rect, IntPtr hwnd, Frame frame)
    {
      if (hwnd != IntPtr.Zero || frame.Scale == 1.0)
        return 1.0;

      double centerX = rect.X + rect.Width / 2;
      double centerY = rect.Y + rect.Height / 2;

      if (frame.Covers(centerX, centerY))
        return 1.0;

      return frame.Covers(centerX * frame.Scale, centerY * frame.Scale) ? frame.Scale : 1.0;
    }

    private static ControlInfo Describe(AutomationElement element, int depth, Frame frame)
    {
      try
      {
        AutomationElement.AutomationElementInformation current = element.Current;

        var info = new ControlInfo
        {
          Depth = depth,
          Element = element,
          Label = current.Name ?? "",
          Enabled = current.IsEnabled,
          Hwnd = new IntPtr(current.NativeWindowHandle),
          Role = RoleOf(current.ControlType)
        };

        System.Windows.Rect rect = current.BoundingRectangle;
        if (!rect.IsEmpty && rect.Width > 0 && rect.Height > 0)
        {
          double factor = ScaleFor(rect, info.Hwnd, frame);

          info.X = (int)Math.Round(rect.X * factor);
          info.Y = (int)Math.Round(rect.Y * factor);
          info.Width = (int)Math.Round(rect.Width * factor);
          info.Height = (int)Math.Round(rect.Height * factor);
          info.HasLocation = true;
        }

        info.Value = ReadValue(element);
        return info;
      }
      catch
      {
        return null;
      }
    }

    private static string RoleOf(ControlType controlType)
    {
      if (controlType == null)
        return "element";
      return controlType.ProgrammaticName.Replace("ControlType.", "").ToLowerInvariant();
    }

    private static string ReadValue(AutomationElement element)
    {
      try
      {
        object pattern;
        if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out pattern))
          return "";

        return ((ValuePattern)pattern).Current.Value ?? "";
      }
      catch
      {
        return "";
      }
    }

    /// <summary>Nearest ancestor backed by a real window, which owns the accessibility tree.</summary>
    public static IntPtr FindContainerHwnd(AutomationElement element)
    {
      AutomationElement node = element;

      for (int depth = 0; node != null && depth < 16; depth++)
      {
        int handle;
        try
        {
          handle = node.Current.NativeWindowHandle;
        }
        catch
        {
          return IntPtr.Zero;
        }

        if (handle != 0)
          return new IntPtr(handle);

        AutomationElement current = node;
        node = Step(delegate { return TreeWalker.ControlViewWalker.GetParent(current); });
      }

      return IntPtr.Zero;
    }

    /// <summary>
    /// Invoke/Toggle/Select/Expand with a wall-clock limit. TimedOut usually means the
    /// action started a modal dialog and the provider will not return until it closes —
    /// callers should treat that as success and must not fall through to another click.
    /// </summary>
    public static StaTimeout.Result TryInvokeTimed(AutomationElement element, int timeoutMs)
    {
      if (element == null)
        return StaTimeout.Result.Failed;

      bool applied = false;
      StaTimeout.Result result = StaTimeout.Run(delegate
      {
        object pattern;

        if (element.TryGetCurrentPattern(InvokePattern.Pattern, out pattern))
        {
          ((InvokePattern)pattern).Invoke();
          applied = true;
          return;
        }

        if (element.TryGetCurrentPattern(TogglePattern.Pattern, out pattern))
        {
          ((TogglePattern)pattern).Toggle();
          applied = true;
          return;
        }

        if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out pattern))
        {
          ((SelectionItemPattern)pattern).Select();
          applied = true;
          return;
        }

        if (element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out pattern))
        {
          ((ExpandCollapsePattern)pattern).Expand();
          applied = true;
        }
      }, timeoutMs, preferSta: true);

      if (result == StaTimeout.Result.TimedOut)
        return StaTimeout.Result.TimedOut;
      if (result == StaTimeout.Result.Failed || !applied)
        return StaTimeout.Result.Failed;
      return StaTimeout.Result.Succeeded;
    }

    public static StaTimeout.Result TryInvokeHwnd(IntPtr hwnd, int timeoutMs)
    {
      if (hwnd == IntPtr.Zero || !Win32Interop.IsWindow(hwnd))
        return StaTimeout.Result.Failed;

      try
      {
        AutomationElement element = AutomationElement.FromHandle(hwnd);
        return TryInvokeTimed(element, timeoutMs);
      }
      catch
      {
        return StaTimeout.Result.Failed;
      }
    }

    public static bool TrySetValue(AutomationElement element, string text)
    {
      if (element == null)
        return false;

      object pattern;
      if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out pattern))
        return false;

      var value = (ValuePattern)pattern;
      if (value.Current.IsReadOnly)
        return false;

      return StaTimeout.Run(delegate { value.SetValue(text ?? ""); }, DefaultActionTimeoutMs, preferSta: true)
        == StaTimeout.Result.Succeeded;
    }

    public static bool TryScroll(AutomationElement element, Win32Interop.ScrollDirection direction, int amount)
    {
      if (element == null)
        return false;

      object pattern;
      if (!element.TryGetCurrentPattern(ScrollPattern.Pattern, out pattern))
        return false;

      var scroll = (ScrollPattern)pattern;
      bool horizontal = direction == Win32Interop.ScrollDirection.Left ||
                        direction == Win32Interop.ScrollDirection.Right;

      if (horizontal ? !scroll.Current.HorizontallyScrollable : !scroll.Current.VerticallyScrollable)
        return false;

      ScrollAmount step = (direction == Win32Interop.ScrollDirection.Up ||
                           direction == Win32Interop.ScrollDirection.Left)
        ? ScrollAmount.SmallDecrement
        : ScrollAmount.SmallIncrement;

      return StaTimeout.Run(delegate
      {
        for (int i = 0; i < amount; i++)
        {
          if (horizontal)
            scroll.ScrollHorizontal(step);
          else
            scroll.ScrollVertical(step);
        }
      }, DefaultActionTimeoutMs, preferSta: true) == StaTimeout.Result.Succeeded;
    }

    public static bool TryFocus(AutomationElement element)
    {
      if (element == null)
        return false;

      return StaTimeout.Run(delegate { element.SetFocus(); }, DefaultActionTimeoutMs, preferSta: true)
        == StaTimeout.Result.Succeeded;
    }

    /// <summary>
    /// Best-effort bring into view for list/tree/grid items (ScrollItemPattern).
    /// Walks ancestors when the node itself is not a scroll item. Silent no-op if unsupported.
    /// </summary>
    public static bool TryEnsureInView(AutomationElement element)
    {
      if (element == null)
        return false;

      AutomationElement node = element;
      for (int depth = 0; depth < 12 && node != null; depth++)
      {
        object pattern;
        bool hasScrollItem;
        try
        {
          hasScrollItem = node.TryGetCurrentPattern(ScrollItemPattern.Pattern, out pattern);
        }
        catch
        {
          pattern = null;
          hasScrollItem = false;
        }

        if (hasScrollItem)
        {
          StaTimeout.Result result = StaTimeout.Run(
            delegate { ((ScrollItemPattern)pattern).ScrollIntoView(); },
            DefaultActionTimeoutMs,
            preferSta: true);
          if (result == StaTimeout.Result.Succeeded)
            return true;
        }

        AutomationElement current = node;
        node = Step(delegate { return TreeWalker.ControlViewWalker.GetParent(current); });
      }

      return false;
    }

    /// <summary>
    /// Re-read screen bounds after ScrollIntoView (or other layout changes).
    /// </summary>
    public static bool TryRefreshLocation(ControlInfo info, IntPtr windowHwnd)
    {
      if (info == null || info.Element == null)
        return false;

      try
      {
        System.Windows.Rect rect = info.Element.Current.BoundingRectangle;
        if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0)
          return false;

        Frame frame = Frame.For(windowHwnd, Win32Interop.GetProviderScale(windowHwnd));
        double factor = ScaleFor(rect, info.Hwnd, frame);

        info.X = (int)Math.Round(rect.X * factor);
        info.Y = (int)Math.Round(rect.Y * factor);
        info.Width = (int)Math.Round(rect.Width * factor);
        info.Height = (int)Math.Round(rect.Height * factor);
        info.HasLocation = info.Width > 0 && info.Height > 0;
        return info.HasLocation;
      }
      catch
      {
        return false;
      }
    }
  }
}

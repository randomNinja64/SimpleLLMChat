using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace DesktopTools
{
  /// <summary>
  /// Every action walks the same ladder: UI Automation pattern first (works without stealing
  /// focus), then a window message to the control's HWND, then synthetic input at coordinates.
  /// </summary>
  internal static class ControlActions
  {
    private class Target
    {
      public IntPtr WindowHwnd;
      public ControlInfo Control;
      public int X;
      public int Y;
      public bool HasPoint;
      public string Error;

      public bool Failed { get { return Error != null; } }
      public IntPtr ControlHwnd { get { return Control == null ? IntPtr.Zero : Control.Hwnd; } }
    }

    public static string Navigate(string action, string argumentsJson)
    {
      switch ((action ?? "").Trim().ToLowerInvariant())
      {
        case "click":
          return Click(argumentsJson, 1);
        case "double_click":
        case "doubleclick":
          return Click(argumentsJson, 2);
        case "drag":
          return Drag(argumentsJson);
        case "scroll":
          return Scroll(argumentsJson);
        case "focus":
          return Focus(argumentsJson);
        default:
          return "error: unknown action '" + action + "'. Use click, double_click, drag, scroll, or focus.";
      }
    }

    public static string SetText(string argumentsJson)
    {
      string text = ToolHelper.JsonExtractString(argumentsJson, "text") ?? "";

      Target target = Resolve(argumentsJson);
      if (target.Failed)
        return target.Error;

      UiChanges.Snapshot before = UiChanges.Capture();

      if (target.Control != null && UiaInterop.TrySetValue(target.Control.Element, text))
        return Done("ok", before, target.WindowHwnd);

      IntPtr hwnd = target.ControlHwnd != IntPtr.Zero ? target.ControlHwnd : target.WindowHwnd;
      if (hwnd == IntPtr.Zero || !Win32Interop.IsWindow(hwnd))
        return "error: target has no window handle and no editable value; click it and use send_keys instead.";

      if (!Win32Interop.SetControlText(hwnd, text))
        return "error: WM_SETTEXT failed for hwnd=" + Win32Interop.FormatHwnd(hwnd) + ".";

      return Done("ok", before, target.WindowHwnd);
    }

    private static string Click(string argumentsJson, int clickCount)
    {
      var button = Win32Interop.ParseMouseButton(ToolHelper.JsonExtractString(argumentsJson, "button"));

      Win32Interop.KeyModifiers modifiers;
      string modError;
      if (!TryParseModifiers(argumentsJson, out modifiers, out modError))
        return modError;

      Target target = Resolve(argumentsJson);
      if (target.Failed)
        return target.Error;

      UiChanges.Snapshot before = UiChanges.Capture();

      // UIA/MSAA/BM_CLICK ignore held keys — only use them for unmodified left clicks.
      bool plainClick = clickCount == 1 && button == Win32Interop.MouseButton.Left && modifiers == Win32Interop.KeyModifiers.None;

      if (plainClick && target.Control != null && target.Control.Element != null)
      {
        // Invoke can block forever if the handler opens a modal dialog — time out and
        // treat that as success so we do not double-activate with a mouse click.
        StaTimeout.Result invoked = UiaInterop.TryInvokeTimed(target.Control.Element, 2000);
        if (invoked == StaTimeout.Result.Succeeded || invoked == StaTimeout.Result.TimedOut)
          return Done("ok", before, target.WindowHwnd);
      }

      if (plainClick && TryActivateThroughMsaa(target.Control))
        return Done("ok", before, target.WindowHwnd);

      if (plainClick && IsPushButton(target.ControlHwnd))
      {
        // PostMessage BM_CLICK — SendMessage would hang if a modal opens.
        Win32Interop.PostMessage(target.ControlHwnd, Win32Interop.BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        return Done("ok", before, target.WindowHwnd);
      }

      if (target.HasPoint)
      {
        Win32Interop.ClickAtScreenPoint(target.X, target.Y, button, clickCount, modifiers);
        return Done("ok", before, target.WindowHwnd);
      }

      if (target.ControlHwnd != IntPtr.Zero)
      {
        Win32Interop.ClickControlCenter(target.ControlHwnd, button, clickCount, modifiers);
        return Done("ok", before, target.WindowHwnd);
      }

      return NoScreenLocationError();
    }

    private static string Drag(string argumentsJson)
    {
      var button = Win32Interop.ParseMouseButton(ToolHelper.JsonExtractString(argumentsJson, "button"));

      Win32Interop.KeyModifiers modifiers;
      string modError;
      if (!TryParseModifiers(argumentsJson, out modifiers, out modError))
        return modError;

      Target from = Resolve(argumentsJson);
      if (from.Failed)
        return from.Error;

      int fromX, fromY;
      if (!TryGetScreenPoint(from, out fromX, out fromY))
        return NoScreenLocationError();

      int toX, toY;
      string endError = ResolveDragEnd(argumentsJson, from.WindowHwnd, out toX, out toY);
      if (endError != null)
        return endError;

      if (fromX == toX && fromY == toY)
        return "error: drag start and end are the same point.";

      UiChanges.Snapshot before = UiChanges.Capture();
      Win32Interop.DragAtScreenPoints(fromX, fromY, toX, toY, button, modifiers);
      return Done("ok", before, from.WindowHwnd);
    }

    private static string Scroll(string argumentsJson)
    {
      var direction = Win32Interop.ParseScrollDirection(ToolHelper.JsonExtractString(argumentsJson, "direction"));

      int amount;
      if (!int.TryParse(ToolHelper.JsonExtractString(argumentsJson, "amount"), out amount) || amount < 1)
        amount = 3;

      Target target = Resolve(argumentsJson);
      if (target.Failed)
        return target.Error;

      UiChanges.Snapshot before = UiChanges.Capture();

      if (target.Control != null && UiaInterop.TryScroll(target.Control.Element, direction, amount))
        return Done("ok", before, target.WindowHwnd);

      if (target.HasPoint)
      {
        Win32Interop.ScrollAtScreenPoint(target.X, target.Y, direction, amount);
        return Done("ok", before, target.WindowHwnd);
      }

      IntPtr hwnd = target.ControlHwnd != IntPtr.Zero ? target.ControlHwnd : target.WindowHwnd;
      if (hwnd == IntPtr.Zero)
        return NoScreenLocationError();

      Win32Interop.ScrollControlCenter(hwnd, direction, amount);
      return Done("ok", before, target.WindowHwnd);
    }

    private static string Focus(string argumentsJson)
    {
      Target target = Resolve(argumentsJson);
      if (target.Failed)
        return target.Error;

      UiChanges.Snapshot before = UiChanges.Capture();

      if (target.Control != null && UiaInterop.TryFocus(target.Control.Element))
        return Done("ok", before, target.WindowHwnd);

      IntPtr hwnd = target.ControlHwnd != IntPtr.Zero ? target.ControlHwnd : target.WindowHwnd;
      if (hwnd == IntPtr.Zero)
        return "error: provide hwnd, window_title, or element.";

      if (Win32Interop.FocusWindow(hwnd))
        return Done("ok", before, target.WindowHwnd);
      return "warn: focus may be blocked";
    }

    private static string Done(string result, UiChanges.Snapshot before, IntPtr relatedWindow)
    {
      return UiChanges.Annotate(result, before, relatedWindow);
    }

    /// <summary>
    /// Grid rows, list items and similar virtual children expose no actionable UIA pattern,
    /// so reach the MSAA object beneath them. Real controls skip this and use the rungs below.
    /// </summary>
    private static bool TryActivateThroughMsaa(ControlInfo control)
    {
      if (control == null || control.Element == null || control.Hwnd != IntPtr.Zero)
        return false;

      IntPtr container = UiaInterop.FindContainerHwnd(control.Element);
      return MsaaInterop.TryActivate(container, control);
    }

    private static bool IsPushButton(IntPtr hwnd)
    {
      return hwnd != IntPtr.Zero &&
             Win32Interop.IsWindow(hwnd) &&
             Win32Interop.GetControlClass(hwnd).Equals("Button", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Targeting precedence: element index within a window, then explicit x/y in capture
    /// space (outer-window pixels when hwnd/window_title is set; virtual-desktop pixels
    /// otherwise — same as screenshot), then the window itself.
    /// </summary>
    private static Target Resolve(string argumentsJson)
    {
      var target = new Target();
      WindowTarget window = WindowEnumerator.ParseWindowTarget(argumentsJson);

      int elementIndex;
      bool hasElement = int.TryParse(ToolHelper.JsonExtractString(argumentsJson, "element"), out elementIndex) && elementIndex > 0;

      int x, y = 0;
      bool hasCoords = int.TryParse(ToolHelper.JsonExtractString(argumentsJson, "x"), out x) &&
                       int.TryParse(ToolHelper.JsonExtractString(argumentsJson, "y"), out y);

      if (window.IsEmpty && !hasCoords)
        return Fail("error: provide hwnd, window_title, or x/y.");

      if (!window.IsEmpty)
      {
        try
        {
          target.WindowHwnd = WindowEnumerator.ResolveWindow(window.HwndText, window.WindowTitle);
        }
        catch (Exception ex)
        {
          return Fail("error: " + ex.Message);
        }
      }

      if (hasElement)
      {
        ControlInfo control;
        string elementError = TryResolveElement(target.WindowHwnd, elementIndex, argumentsJson, "element", out control);
        if (elementError != null)
          return Fail(elementError);

        target.Control = control;
        target.HasPoint = control.TryGetCenter(out target.X, out target.Y);
        return target;
      }

      if (hasCoords)
      {
        int screenX, screenY;
        string error;
        if (!Win32Interop.TryCaptureToScreen(target.WindowHwnd, x, y, out screenX, out screenY, out error))
          return Fail(error);

        target.X = screenX;
        target.Y = screenY;
        target.HasPoint = true;
      }

      return target;
    }

    /// <summary>
    /// End point for drag: to_element (same window scope as start) or to_x/to_y in the same
    /// capture space as start (outer-window or virtual-desktop pixels).
    /// </summary>
    private static string ResolveDragEnd(string argumentsJson, IntPtr windowHwnd, out int toX, out int toY)
    {
      toX = 0;
      toY = 0;

      int toElement;
      bool hasToElement = int.TryParse(ToolHelper.JsonExtractString(argumentsJson, "to_element"), out toElement) && toElement > 0;

      int x, y = 0;
      bool hasCoords = int.TryParse(ToolHelper.JsonExtractString(argumentsJson, "to_x"), out x) &&
                       int.TryParse(ToolHelper.JsonExtractString(argumentsJson, "to_y"), out y);

      if (!hasToElement && !hasCoords)
        return "error: drag requires to_element or to_x/to_y.";

      if (hasToElement)
      {
        ControlInfo control;
        string elementError = TryResolveElement(windowHwnd, toElement, argumentsJson, "to_element", out control);
        if (elementError != null)
          return elementError;

        if (!control.TryGetCenter(out toX, out toY))
          return NoScreenLocationError();

        return null;
      }

      string error;
      if (!Win32Interop.TryCaptureToScreen(windowHwnd, x, y, out toX, out toY, out error))
        return error;

      return null;
    }

    /// <summary>
    /// Resolve element / to_element by index. max_depth must match list_controls.
    /// </summary>
    private static string TryResolveElement(
      IntPtr windowHwnd,
      int elementIndex,
      string argumentsJson,
      string paramName,
      out ControlInfo control)
    {
      control = null;

      if (windowHwnd == IntPtr.Zero)
        return "error: " + paramName + " requires hwnd or window_title to scope the control tree.";

      TreeScope scope = ControlTree.ParseScope(argumentsJson);
      control = ControlTree.Find(windowHwnd, elementIndex, scope.MaxDepth, scope.MaxControls);
      if (control == null)
        return "error: " + paramName + " #" + elementIndex + " not found. Re-run list_controls first (pass the same max_depth).";

      EnsureControlInView(control, windowHwnd);
      return null;
    }

    /// <summary>
    /// Silent best-effort: scroll list/tree items into view, then refresh cached bounds.
    /// </summary>
    private static void EnsureControlInView(ControlInfo control, IntPtr windowHwnd)
    {
      if (control == null || control.Element == null)
        return;

      UiaInterop.TryEnsureInView(control.Element);
      UiaInterop.TryRefreshLocation(control, windowHwnd);
    }

    private static bool TryGetScreenPoint(Target target, out int x, out int y)
    {
      if (target.HasPoint)
      {
        x = target.X;
        y = target.Y;
        return true;
      }

      if (target.ControlHwnd != IntPtr.Zero &&
          Win32Interop.IsWindow(target.ControlHwnd) &&
          Win32Interop.TryGetWindowCenter(target.ControlHwnd, out x, out y))
        return true;

      x = 0;
      y = 0;
      return false;
    }

    private static bool TryParseModifiers(string argumentsJson, out Win32Interop.KeyModifiers modifiers, out string error)
    {
      modifiers = Win32Interop.KeyModifiers.None;
      error = null;

      var tokens = new List<string>();
      JArray array = ToolHelper.JsonExtractArray(argumentsJson, "modifiers");
      if (array != null)
      {
        foreach (JToken item in array)
        {
          if (item == null || item.Type == JTokenType.Null)
            continue;
          string s = item.Type == JTokenType.String ? item.Value<string>() : item.ToString();
          if (!string.IsNullOrWhiteSpace(s))
            tokens.Add(s.Trim());
        }
      }
      else
      {
        string text = ToolHelper.JsonExtractString(argumentsJson, "modifiers");
        if (!string.IsNullOrWhiteSpace(text))
        {
          foreach (string part in text.Split(new[] { '+', ',', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            tokens.Add(part.Trim());
        }
      }

      foreach (string token in tokens)
      {
        switch (token.ToLowerInvariant())
        {
          case "ctrl":
          case "control":
            modifiers |= Win32Interop.KeyModifiers.Ctrl;
            break;
          case "shift":
            modifiers |= Win32Interop.KeyModifiers.Shift;
            break;
          case "alt":
          case "menu":
          case "option":
            modifiers |= Win32Interop.KeyModifiers.Alt;
            break;
          case "win":
          case "windows":
          case "super":
          case "meta":
          case "cmd":
            modifiers |= Win32Interop.KeyModifiers.Win;
            break;
          default:
            error = "error: unknown modifier '" + token + "'. Use ctrl, shift, alt, and/or win.";
            modifiers = Win32Interop.KeyModifiers.None;
            return false;
        }
      }

      return true;
    }

    private static string NoScreenLocationError()
    {
      return "error: target has no screen location; it may be off-screen — scroll the container, re-run list_controls, or pass x/y.";
    }

    private static Target Fail(string message)
    {
      return new Target { Error = message };
    }
  }
}

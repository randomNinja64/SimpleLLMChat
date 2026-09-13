using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace DesktopTools
{
  /// <summary>
    /// UIA elements use patterns; direct HWND/native targets use MSAA and window messages.
    /// Synthetic input is the final fallback and supplies dragging and modifier clicks.
  /// </summary>
  internal static class ControlActions
  {
    private class Target
    {
      public IntPtr WindowHwnd;
      public ControlInfo Control;
      public List<ControlInfo> Controls;
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

      // AX / window-message paths cannot hold modifier keys — those need SendInput.
      bool noModifiers = modifiers == Win32Interop.KeyModifiers.None;
      bool plainLeftClick = clickCount == 1 && button == Win32Interop.MouseButton.Left && noModifiers;
      IntPtr actionHwnd = ActionHwnd(target);

      // Direct HWND and native discovery use the XP-era MSAA action without UIA startup.
      bool nativeTarget = (target.Control == null && !target.HasPoint) ||
                          (target.Control != null && target.Control.IsNativeWindow);
      if (plainLeftClick && nativeTarget && actionHwnd != IntPtr.Zero &&
          MsaaInterop.TryDefaultActionHwnd(actionHwnd))
        return Done("ok", before, target.WindowHwnd);

      if (plainLeftClick && target.Control != null && target.Control.Element != null)
      {
        // Invoke can block forever if the handler opens a modal dialog — time out and
        // treat that as success so we do not double-activate with a mouse click.
        StaTimeout.Result invoked = UiaInterop.TryInvokeTimed(target.Control.Element, 2000);
        if (invoked == StaTimeout.Result.TimedOut)
          return Done("ok", before, target.WindowHwnd);

        // XP WinForms list items expose SelectionItem but Select does not change
        // LB_GETCURSEL. Drive the real list/combo HWND by caption instead.
        if (TrySelectNativeNamedItem(target.Control))
          return Done("ok", before, target.WindowHwnd);

        if (invoked == StaTimeout.Result.Succeeded && !IsHwndlessSelectableItem(target.Control))
          return Done("ok", before, target.WindowHwnd);
      }

      if (plainLeftClick && !nativeTarget && TryActivateThroughMsaa(target.Control))
        return Done("ok", before, target.WindowHwnd);

      if (plainLeftClick && IsPushButton(actionHwnd))
      {
        // PostMessage BM_CLICK — SendMessage would hang if a modal opens.
        Win32Interop.PostMessage(actionHwnd, Win32Interop.BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        return Done("ok", before, target.WindowHwnd);
      }

      // Posted client-message click: no cursor move. Skip when modifiers are held.
      if (noModifiers && TryClickViaPostedMessage(target, actionHwnd, button, clickCount))
        return Done("ok", before, target.WindowHwnd);

      if (target.HasPoint)
      {
        Win32Interop.ClickAtScreenPoint(target.X, target.Y, button, clickCount, modifiers);
        return Done("ok", before, target.WindowHwnd);
      }

      if (actionHwnd != IntPtr.Zero)
      {
        Win32Interop.ClickControlCenter(actionHwnd, button, clickCount, modifiers);
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
      string endError = ResolveDragEnd(argumentsJson, from.WindowHwnd, from.Controls, out toX, out toY);
      if (endError != null)
        return endError;

      if (fromX == toX && fromY == toY)
        return "error: drag start and end are the same point.";

      UiChanges.Snapshot before = UiChanges.Capture();
      // Drag has no reliable non-cursor path (TransformPattern is rare); SendInput required.
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

      IntPtr actionHwnd = ActionHwnd(target);

      if (actionHwnd != IntPtr.Zero && Win32Interop.TryPostScroll(actionHwnd, direction, amount))
        return Done("ok", before, target.WindowHwnd);

      if (target.HasPoint)
      {
        Win32Interop.ScrollAtScreenPoint(target.X, target.Y, direction, amount);
        return Done("ok", before, target.WindowHwnd);
      }

      if (actionHwnd == IntPtr.Zero)
        return NoScreenLocationError();

      Win32Interop.ScrollControlCenter(actionHwnd, direction, amount);
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

    private static bool IsHwndlessSelectableItem(ControlInfo control)
    {
      if (control == null || control.Hwnd != IntPtr.Zero)
        return false;
      string role = control.Role ?? "";
      return role == "listitem" || role == "treeitem" || role == "dataitem";
    }

    private static bool TrySelectNativeNamedItem(ControlInfo control)
    {
      if (control == null || string.IsNullOrEmpty(control.Label) || !IsHwndlessSelectableItem(control))
        return false;

      IntPtr hwnd = control.Hwnd;
      if (hwnd == IntPtr.Zero && control.Element != null)
        hwnd = UiaInterop.FindContainerHwnd(control.Element);
      return Win32Interop.TrySelectNamedItem(hwnd, control.Label);
    }

    /// <summary>
    /// Prefer MSAA before synthetic mouse input. Works for UIA elements and for
    /// MSAA-synthesized grid rows (Element null, Hwnd = grid).
    /// </summary>
    private static bool TryActivateThroughMsaa(ControlInfo control)
    {
      if (control == null)
        return false;

      IntPtr container = control.Hwnd;
      if (container == IntPtr.Zero && control.Element != null)
        container = UiaInterop.FindContainerHwnd(control.Element);
      if (container == IntPtr.Zero)
        return false;

      return MsaaInterop.TryActivate(container, control);
    }

    private static IntPtr ActionHwnd(Target target)
    {
      if (target.ControlHwnd != IntPtr.Zero && Win32Interop.IsWindow(target.ControlHwnd))
        return target.ControlHwnd;
      if (target.WindowHwnd != IntPtr.Zero && Win32Interop.IsWindow(target.WindowHwnd))
        return target.WindowHwnd;
      return IntPtr.Zero;
    }

    private static bool TryClickViaPostedMessage(
      Target target, IntPtr actionHwnd, Win32Interop.MouseButton button, int clickCount)
    {
      int x, y;
      if (target.HasPoint)
      {
        x = target.X;
        y = target.Y;
      }
      else if (actionHwnd != IntPtr.Zero && Win32Interop.TryGetWindowCenter(actionHwnd, out x, out y))
      {
        // center of action hwnd
      }
      else
        return false;

      IntPtr hwnd = actionHwnd;
      if (hwnd == IntPtr.Zero)
      {
        hwnd = Win32Interop.WindowFromPoint(new Win32Interop.POINT { X = x, Y = y });
        if (hwnd == IntPtr.Zero)
          return false;
      }

      return Win32Interop.TryPostMouseClick(hwnd, x, y, button, clickCount);
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
        TreeScope scope = ControlTree.ParseScope(argumentsJson);
        target.Controls = ControlTree.Collect(target.WindowHwnd, scope.MaxDepth, scope.MaxControls);
        ControlInfo control;
        string elementError = TryResolveElement(target.WindowHwnd, elementIndex, argumentsJson, "element", out control, target.Controls);
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
    /// End point uses element, coordinates, then window center, just like the start.
    /// An explicit destination window changes scope; otherwise inherit the start scope.
    /// </summary>
    private static string ResolveDragEnd(string argumentsJson, IntPtr windowHwnd, List<ControlInfo> controls, out int toX, out int toY)
    {
      toX = 0;
      toY = 0;

      string toHwnd = ToolHelper.JsonExtractString(argumentsJson, "to_hwnd");
      string toTitle = ToolHelper.JsonExtractString(argumentsJson, "to_window_title");
      bool hasWindow = !string.IsNullOrWhiteSpace(toHwnd) || !string.IsNullOrWhiteSpace(toTitle);
      if (hasWindow)
      {
        try
        {
          IntPtr end = WindowEnumerator.ResolveWindow(toHwnd, toTitle);
          if (end != windowHwnd) controls = null;
          windowHwnd = end;
        }
        catch (Exception ex) { return "error: " + ex.Message; }
      }

      int toElement;
      bool hasToElement = int.TryParse(ToolHelper.JsonExtractString(argumentsJson, "to_element"), out toElement) && toElement > 0;

      int x, y = 0;
      bool hasCoords = int.TryParse(ToolHelper.JsonExtractString(argumentsJson, "to_x"), out x) &&
                       int.TryParse(ToolHelper.JsonExtractString(argumentsJson, "to_y"), out y);

      if (!hasToElement && !hasCoords)
        return hasWindow
          ? (Win32Interop.TryGetWindowCenter(windowHwnd, out toX, out toY) ? null : NoScreenLocationError())
          : "error: drag requires to_hwnd, to_window_title, to_element or to_x/to_y.";

      if (hasToElement)
      {
        ControlInfo control;
        string elementError = TryResolveElement(windowHwnd, toElement, argumentsJson, "to_element", out control, controls);
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
      out ControlInfo control, List<ControlInfo> controls = null)
    {
      control = null;

      if (windowHwnd == IntPtr.Zero)
        return "error: " + paramName + " requires hwnd or window_title to scope the control tree.";

      TreeScope scope = ControlTree.ParseScope(argumentsJson);
      if (controls == null)
        controls = ControlTree.Collect(windowHwnd, scope.MaxDepth, scope.MaxControls);
      if (elementIndex > 0 && elementIndex <= controls.Count && controls[elementIndex - 1].Index == elementIndex)
        control = controls[elementIndex - 1];
      if (control == null)
        return "error: " + paramName + " #" + elementIndex + " not found. Re-run list_controls first (pass the same max_depth).";

      EnsureControlInView(control, windowHwnd);
      return null;
    }

    /// <summary>
    /// Silent best-effort: scroll list/tree/grid items into view, then refresh cached bounds.
    /// </summary>
    private static void EnsureControlInView(ControlInfo control, IntPtr windowHwnd)
    {
      if (control == null)
        return;

      if (control.IsNativeWindow)
        return;

      if (control.Element == null)
      {
        MsaaInterop.TryEnsureInView(control);
        return;
      }

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

      if (target.Control == null && target.WindowHwnd != IntPtr.Zero && Win32Interop.IsWindow(target.WindowHwnd) &&
          Win32Interop.TryGetWindowCenter(target.WindowHwnd, out x, out y))
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

using System;
using System.Runtime.InteropServices;

namespace DesktopTools
{
  /// <summary>
  /// Synthetic cursor/keyboard input via SendInput (moves the real cursor).
  /// </summary>
  internal static partial class Win32Interop
  {
    public enum MouseButton
    {
      Left,
      Right,
      Middle
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

    public enum ScrollDirection
    {
      Up,
      Down,
      Left,
      Right
    }

    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_MENU = 0x12;
    private const ushort VK_LWIN = 0x5B;

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
  }
}

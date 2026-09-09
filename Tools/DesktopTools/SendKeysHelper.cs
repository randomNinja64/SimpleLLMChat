using System;
using System.Collections.Generic;
using System.Text;

namespace DesktopTools
{
  internal static class SendKeysHelper
  {
    private static readonly Dictionary<string, ushort> NamedKeys = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
    {
      { "Enter", 0x0D },
      { "Return", 0x0D },
      { "Tab", 0x09 },
      { "Esc", 0x1B },
      { "Escape", 0x1B },
      { "Backspace", 0x08 },
      { "Bksp", 0x08 },
      { "Delete", 0x2E },
      { "Del", 0x2E },
      { "Insert", 0x2D },
      { "Ins", 0x2D },
      { "Home", 0x24 },
      { "End", 0x23 },
      { "PgUp", 0x21 },
      { "PageUp", 0x21 },
      { "PgDn", 0x22 },
      { "PageDown", 0x22 },
      { "Up", 0x26 },
      { "Down", 0x28 },
      { "Left", 0x25 },
      { "Right", 0x27 },
      { "Space", 0x20 },
      { "F1", 0x70 }, { "F2", 0x71 }, { "F3", 0x72 }, { "F4", 0x73 },
      { "F5", 0x74 }, { "F6", 0x75 }, { "F7", 0x76 }, { "F8", 0x77 },
      { "F9", 0x78 }, { "F10", 0x79 }, { "F11", 0x7A }, { "F12", 0x7B }
    };

    private static readonly Dictionary<string, ushort> ModifierKeys = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
    {
      { "Ctrl", 0x11 },
      { "Control", 0x11 },
      { "Shift", 0x10 },
      { "Alt", 0x12 },
      { "Win", 0x5B },
      { "LWin", 0x5B },
      { "RWin", 0x5C }
    };

    public static string Send(string keys)
    {
      if (string.IsNullOrEmpty(keys))
        return "error: keys argument is empty.";

      UiChanges.Snapshot before = UiChanges.Capture();
      IntPtr related = Win32Interop.GetForegroundWindow();

      var heldModifiers = new List<ushort>();
      int i = 0;
      while (i < keys.Length)
      {
        char ch = keys[i];

        if (ch == '{')
        {
          int end = keys.IndexOf('}', i + 1);
          if (end < 0)
            throw new ArgumentException("unclosed '{' in keys string.");

          string token = keys.Substring(i + 1, end - i - 1).Trim();
          i = end + 1;

          if (token.EndsWith(" down", StringComparison.OrdinalIgnoreCase))
          {
            ushort vk = ResolveKey(token.Substring(0, token.Length - 5).Trim());
            Win32Interop.SendKeyInput(vk, false);
            if (!heldModifiers.Contains(vk))
              heldModifiers.Add(vk);
            continue;
          }

          if (token.EndsWith(" up", StringComparison.OrdinalIgnoreCase))
          {
            ushort vk = ResolveKey(token.Substring(0, token.Length - 3).Trim());
            Win32Interop.SendKeyInput(vk, true);
            heldModifiers.Remove(vk);
            continue;
          }

          ushort named = ResolveKey(token);
          Win32Interop.SendKeyInput(named, false);
          Win32Interop.SendKeyInput(named, true);
          continue;
        }

        if (ch == '^')
        {
          SendChord(keys, ref i, 0x11);
          continue;
        }

        if (ch == '+')
        {
          SendChord(keys, ref i, 0x10);
          continue;
        }

        if (ch == '!')
        {
          SendChord(keys, ref i, 0x12);
          continue;
        }

        Win32Interop.SendUnicodeChar(ch);
        i++;
      }

      foreach (ushort vk in heldModifiers)
        Win32Interop.SendKeyInput(vk, true);

      return UiChanges.Annotate("ok", before, related);
    }

    private static void SendChord(string keys, ref int i, ushort modifierVk)
    {
      i++;
      if (i >= keys.Length)
        throw new ArgumentException("modifier at end of keys string.");

      char ch = keys[i];
      if (ch == '{')
      {
        int end = keys.IndexOf('}', i + 1);
        if (end < 0)
          throw new ArgumentException("unclosed '{' in keys string.");

        string token = keys.Substring(i + 1, end - i - 1).Trim();
        i = end + 1;
        ushort vk = ResolveKey(token);
        SendModifiedKey(modifierVk, vk);
        return;
      }

      ushort keyVk = CharToVk(ch);
      SendModifiedKey(modifierVk, keyVk);
      i++;
    }

    private static void SendModifiedKey(ushort modifierVk, ushort keyVk)
    {
      Win32Interop.SendKeyInput(modifierVk, false);
      Win32Interop.SendKeyInput(keyVk, false);
      Win32Interop.SendKeyInput(keyVk, true);
      Win32Interop.SendKeyInput(modifierVk, true);
    }

    private static ushort ResolveKey(string token)
    {
      ushort modifier;
      if (ModifierKeys.TryGetValue(token, out modifier))
        return modifier;

      ushort named;
      if (NamedKeys.TryGetValue(token, out named))
        return named;

      if (token.Length == 1)
        return CharToVk(token[0]);

      throw new ArgumentException("unknown key token: '" + token + "'.");
    }

    private static ushort CharToVk(char ch)
    {
      short scan = Win32Interop.VkKeyScan(ch);
      if (scan == -1)
        throw new ArgumentException("cannot map character to virtual key: '" + ch + "'.");

      return (ushort)(scan & 0xFF);
    }
  }
}

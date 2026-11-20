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
      { "ArrowUp", 0x26 },
      { "ArrowDown", 0x28 },
      { "ArrowLeft", 0x25 },
      { "ArrowRight", 0x27 },
      { "Page_Up", 0x21 },
      { "Page_Down", 0x22 },
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
      { "RWin", 0x5C },
      { "Windows", 0x5B },
      { "Super", 0x5B },
      { "Meta", 0x5B }
    };

    /// <summary>
    /// Types <paramref name="text"/> exactly as written, then sends <paramref name="keys"/>.
    /// Either may be empty, not both.
    ///
    /// keys is one of:
    ///   a key name            Enter, Tab, Esc, F5, Up
    ///   a chord               ctrl+s, alt+f4, ctrl+shift+esc
    ///   a sequence            {Ctrl+A}{Delete}done{Enter}
    /// In a sequence, text outside braces is typed. The older prefixes ^ + ! (Ctrl,
    /// Shift, Alt) still apply to the key that follows; {^} {+} {!} {{} {}} type the
    /// character itself.
    /// </summary>
    public static string Send(string text, string keys)
    {
      bool hasText = !string.IsNullOrEmpty(text);
      bool hasKeys = !string.IsNullOrEmpty(keys);
      if (!hasText && !hasKeys)
        return "error: provide 'text' (typed as written), 'keys' (key presses), or both.";

      // Everything is parsed before anything is sent, so a bad token types nothing.
      var held = new List<ushort>();
      var steps = new List<Action>();
      if (hasText)
        AddText(steps, text);
      if (hasKeys)
        AddKeys(steps, keys, held);

      UiChanges.Snapshot before = UiChanges.Capture();
      IntPtr related = Win32Interop.GetForegroundWindow();

      try
      {
        foreach (Action step in steps)
          step();
      }
      finally
      {
        // A {Key down} with no matching up must not leave the key held.
        for (int i = held.Count - 1; i >= 0; i--)
          Win32Interop.SendKeyInput(held[i], true);
      }

      return UiChanges.Annotate("ok", before, related);
    }

    private static void AddText(List<Action> steps, string text)
    {
      for (int i = 0; i < text.Length; i++)
      {
        char ch = text[i];
        if (ch == '\r' || ch == '\n')
        {
          // A line break is the Enter key; CRLF is one break.
          if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            i++;
          steps.Add(Press(0x0D));
        }
        else if (ch == '\t')
          steps.Add(Press(0x09));
        else
          steps.Add(Type(ch));
      }
    }

    private static void AddKeys(List<Action> steps, string keys, List<ushort> held)
    {
      // The whole string as one key or chord: "Enter", "ctrl+s".
      string whole = keys.Trim();
      List<ushort> chord = TryParseChord(whole, true);
      if (chord != null)
      {
        steps.Add(Chord(chord));
        return;
      }
      if (StartsWithModifierName(whole))
        throw new ArgumentException("unknown key in chord: '" + whole + "'.");

      int i = 0;
      while (i < keys.Length)
      {
        char ch = keys[i];

        if (ch == '{')
        {
          AddToken(steps, ReadBraceToken(keys, ref i), held);
          continue;
        }

        if (IsModifierPrefix(ch))
        {
          int start = i;
          var combo = new List<ushort>();
          while (i < keys.Length && IsModifierPrefix(keys[i]))
          {
            ushort modifier = PrefixVk(keys[i]);
            if (!combo.Contains(modifier))
              combo.Add(modifier);
            i++;
          }

          // Followed by a space or nothing, it is punctuation: "Wow! Nice", "C++", "1 + 2".
          if (i >= keys.Length || char.IsWhiteSpace(keys[i]))
          {
            for (int j = start; j < i; j++)
              steps.Add(Type(keys[j]));
            continue;
          }

          if (keys[i] == '{')
            combo.Add(ResolveKey(ReadBraceToken(keys, ref i)));
          else
            combo.Add(CharToVk(keys[i++]));
          steps.Add(Chord(combo));
          continue;
        }

        steps.Add(Type(ch));
        i++;
      }
    }

    /// <summary>Reads "{...}" starting at keys[i] and moves i past it.</summary>
    private static string ReadBraceToken(string keys, ref int i)
    {
      // "{}}" is a closing brace.
      if (i + 2 < keys.Length && keys[i + 1] == '}' && keys[i + 2] == '}')
      {
        i += 3;
        return "}";
      }

      int end = keys.IndexOf('}', i + 1);
      if (end < 0)
        throw new ArgumentException("unclosed '{' in keys string.");

      string token = keys.Substring(i + 1, end - i - 1).Trim();
      if (token.Length == 0)
        throw new ArgumentException("empty '{}' in keys string.");

      i = end + 1;
      return token;
    }

    private static void AddToken(List<Action> steps, string token, List<ushort> held)
    {
      if (token.EndsWith(" down", StringComparison.OrdinalIgnoreCase))
      {
        ushort vk = ResolveKey(token.Substring(0, token.Length - 5).Trim());
        steps.Add(() =>
        {
          Win32Interop.SendKeyInput(vk, false);
          if (!held.Contains(vk))
            held.Add(vk);
        });
        return;
      }

      if (token.EndsWith(" up", StringComparison.OrdinalIgnoreCase))
      {
        ushort vk = ResolveKey(token.Substring(0, token.Length - 3).Trim());
        steps.Add(() =>
        {
          Win32Interop.SendKeyInput(vk, true);
          held.Remove(vk);
        });
        return;
      }

      // {!} {+} {^} {{} {}}: a symbol in braces is typed as itself.
      if (token.Length == 1 && !char.IsLetterOrDigit(token[0]))
      {
        steps.Add(Type(token[0]));
        return;
      }

      List<ushort> chord = TryParseChord(token, false);
      if (chord != null)
        steps.Add(Chord(chord));
      else
        steps.Add(Press(ResolveKey(token)));
    }

    /// <summary>
    /// Parses "Ctrl+Shift+S" into its keys, the modifiers first. Returns null when the
    /// text is not a chord. A lone key name counts only when allowSingleKey is set.
    /// </summary>
    private static List<ushort> TryParseChord(string text, bool allowSingleKey)
    {
      string modifierPart = null;
      string keyPart = text;

      // "ctrl++" is Ctrl with the plus key.
      if (text.Length > 2 && text.EndsWith("++", StringComparison.Ordinal))
      {
        modifierPart = text.Substring(0, text.Length - 2);
        keyPart = "+";
      }
      else
      {
        int plus = text.LastIndexOf('+');
        if (plus >= 0)
        {
          modifierPart = text.Substring(0, plus);
          keyPart = text.Substring(plus + 1);
        }
      }

      var chord = new List<ushort>();
      if (modifierPart != null)
      {
        foreach (string name in modifierPart.Split('+'))
        {
          ushort modifier;
          if (!ModifierKeys.TryGetValue(name.Trim(), out modifier))
            return null;
          if (!chord.Contains(modifier))
            chord.Add(modifier);
        }
      }
      else if (!allowSingleKey)
        return null;

      // A lone character is text to type, not a key name.
      ushort key;
      if (!TryResolveKey(keyPart.Trim(), modifierPart != null, out key))
        return null;

      chord.Add(key);
      return chord;
    }

    private static bool StartsWithModifierName(string text)
    {
      int plus = text.IndexOf('+');
      return plus > 0 && ModifierKeys.ContainsKey(text.Substring(0, plus).Trim());
    }

    private static bool IsModifierPrefix(char ch)
    {
      return ch == '^' || ch == '+' || ch == '!';
    }

    private static ushort PrefixVk(char ch)
    {
      return (ushort)(ch == '^' ? 0x11 : ch == '+' ? 0x10 : 0x12);
    }

    private static Action Type(char ch)
    {
      return () => Win32Interop.SendUnicodeChar(ch);
    }

    private static Action Press(ushort vk)
    {
      return () =>
      {
        Win32Interop.SendKeyInput(vk, false);
        Win32Interop.SendKeyInput(vk, true);
      };
    }

    /// <summary>Presses the keys in order and releases them in reverse.</summary>
    private static Action Chord(List<ushort> keys)
    {
      return () =>
      {
        int pressed = 0;
        try
        {
          for (; pressed < keys.Count; pressed++)
            Win32Interop.SendKeyInput(keys[pressed], false);
        }
        finally
        {
          for (int i = pressed - 1; i >= 0; i--)
            Win32Interop.SendKeyInput(keys[i], true);
        }
      };
    }

    private static ushort ResolveKey(string token)
    {
      ushort vk;
      if (TryResolveKey(token, true, out vk))
        return vk;

      throw new ArgumentException("unknown key token: '" + token + "'.");
    }

    private static bool TryResolveKey(string token, bool allowCharacter, out ushort vk)
    {
      if (ModifierKeys.TryGetValue(token, out vk) || NamedKeys.TryGetValue(token, out vk))
        return true;

      if (allowCharacter && token.Length == 1)
      {
        short scan = Win32Interop.VkKeyScan(token[0]);
        if (scan != -1)
        {
          vk = (ushort)(scan & 0xFF);
          return true;
        }
      }

      vk = 0;
      return false;
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

using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SimpleLLMChatGUI
{
    public static class FontHandler
    {
        public const string CodeBlockFontFamilyKey = "CodeBlockFontFamily";

        public static void ApplyFontToWindow(Window window)
        {
            if (window == null) return;
            window.FontFamily = TryGetFontFamily(App.Config.Get(AppSettings.CustomFontFamily))
                                ?? SystemFonts.MessageFontFamily;
        }

        /// <summary>
        /// Pushes <c>codeblockfontfamily</c> into the app resource so code blocks
        /// bound with <see cref="FrameworkElement.SetResourceReference"/> update live.
        /// </summary>
        public static void ApplyCodeBlockFontFamily()
        {
            if (Application.Current == null)
                return;

            Application.Current.Resources[CodeBlockFontFamilyKey] =
                TryGetFontFamily(App.Config.Get(AppSettings.CodeBlockFontFamily))
                ?? new FontFamily("Courier New");
        }

        public static int GetFontSize()
        {
            return ClampFontSize(App.Config.Get(AppSettings.FontSize));
        }

        /// <summary>WPF throws on a font size outside 1..35791.</summary>
        public static int ClampFontSize(int size)
        {
            return Math.Max(1, Math.Min(35791, size));
        }

        internal static FontFamily TryGetFontFamily(string fontFamilyName)
        {
            if (string.IsNullOrWhiteSpace(fontFamilyName) ||
                fontFamilyName.Equals("Default", StringComparison.OrdinalIgnoreCase))
                return null;

            try
            {
                var fontFamily = new FontFamily(fontFamilyName);
                if (fontFamily.GetTypefaces().FirstOrDefault() != null)
                    return fontFamily;
            }
            catch { }

            return null;
        }

        public static void ApplyFontSizeToControl(Control control, int fontSize)
        {
            if (control != null)
            {
                control.FontSize = fontSize;
            }
        }
    }
}

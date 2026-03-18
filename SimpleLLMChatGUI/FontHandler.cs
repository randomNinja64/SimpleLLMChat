using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SimpleLLMChatGUI
{
    public static class FontHandler
    {
        private const int DefaultFontSize = AppConstants.DefaultChatFontSize;

        public static void ApplyFontToWindow(Window window)
        {
            if (window == null) return;

            var defaultFont = SystemFonts.MessageFontFamily;

            // Use default font if no custom font specified
            if (!App.Settings.TryGetValue("customfontfamily", out string fontFamilyName) ||
                string.IsNullOrWhiteSpace(fontFamilyName) ||
                fontFamilyName.Equals("Default", StringComparison.OrdinalIgnoreCase))
            {
                window.FontFamily = defaultFont;
                return;
            }

            // Try to apply the custom font
            try
            {
                var fontFamily = new FontFamily(fontFamilyName);
                // Verify the font exists by checking if it has typefaces
                if (fontFamily.GetTypefaces().FirstOrDefault() != null)
                {
                    window.FontFamily = fontFamily;
                }
                else
                {
                    // Font exists but has no typefaces, use default
                    window.FontFamily = defaultFont;
                }
            }
            catch
            {
                // Invalid font family name, use default
                window.FontFamily = defaultFont;
            }
        }

        public static int GetFontSize()
        {
            if (App.Settings.TryGetValue("fontsize", out string fontSizeValue))
            {
                if (int.TryParse(fontSizeValue, out int parsedSize) && parsedSize > 0)
                {
                    return parsedSize;
                }
            }
            return DefaultFontSize;
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

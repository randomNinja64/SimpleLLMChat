using System;
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
            string fontFamilyName = App.Config.GetConfigValue("customfontfamily");
            if (string.IsNullOrWhiteSpace(fontFamilyName) ||
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
            return App.Config.GetConfigInt("fontsize", AppConstants.DefaultChatFontSize);
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

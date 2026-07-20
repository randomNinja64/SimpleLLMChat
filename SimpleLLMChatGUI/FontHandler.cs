using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SimpleLLMChatGUI
{
    public static class FontHandler
    {
        public static void ApplyFontToWindow(Window window)
        {
            if (window == null) return;
            window.FontFamily = TryGetFontFamily(App.Config.GetConfigValue("customfontfamily"))
                                ?? SystemFonts.MessageFontFamily;
        }

        public static int GetFontSize()
        {
            return App.Config.GetConfigInt("fontsize", AppConstants.DefaultChatFontSize);
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

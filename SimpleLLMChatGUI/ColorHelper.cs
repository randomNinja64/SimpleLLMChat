using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace SimpleLLMChatGUI
{
    /// <summary>
    /// Color configuration for a single color setting.
    /// </summary>
    public class ColorConfig
    {
        public string Key { get; set; }
        public string ResourceKey { get; set; }
        public Color DefaultSystemColor { get; set; }
        public Brush DefaultSystemBrush { get; set; }
    }

    /// <summary>
    /// Color parsing utility for the GUI project.
    /// Handles parsing of color strings from configuration files.
    /// </summary>
    public static class ColorHelper
    {
        /// <summary>
        /// Centralized color configuration - defines all color settings in one place.
        /// </summary>
        public static readonly Dictionary<string, ColorConfig> ColorConfigs = new Dictionary<string, ColorConfig>(StringComparer.OrdinalIgnoreCase)
        {
            {
                "buttontextcolor",
                new ColorConfig
                {
                    Key = "buttontextcolor",
                    ResourceKey = "ButtonTextColorBrush",
                    DefaultSystemColor = SystemColors.ControlTextColor,
                    DefaultSystemBrush = SystemColors.ControlTextBrush
                }
            },
            {
                "chatbackgroundcolor",
                new ColorConfig
                {
                    Key = "chatbackgroundcolor",
                    ResourceKey = "ChatBackgroundColorBrush",
                    DefaultSystemColor = SystemColors.WindowColor,
                    DefaultSystemBrush = SystemColors.WindowBrush
                }
            },
            {
                "chattextcolor",
                new ColorConfig
                {
                    Key = "chattextcolor",
                    ResourceKey = "ChatTextColorBrush",
                    DefaultSystemColor = SystemColors.WindowTextColor,
                    DefaultSystemBrush = SystemColors.WindowTextBrush
                }
            },
            {
                "labeltextcolor",
                new ColorConfig
                {
                    Key = "labeltextcolor",
                    ResourceKey = "LabelTextColorBrush",
                    DefaultSystemColor = SystemColors.ControlTextColor,
                    DefaultSystemBrush = SystemColors.ControlTextBrush
                }
            },
            {
                "windowbackgroundcolor",
                new ColorConfig
                {
                    Key = "windowbackgroundcolor",
                    ResourceKey = "WindowBackgroundColorBrush",
                    DefaultSystemColor = SystemColors.ControlColor,
                    DefaultSystemBrush = SystemColors.ControlBrush
                }
            }
        };

        /// <summary>
        /// Attempts to parse a color string (hex format) into a WPF Color.
        /// Supports both RGB (#RRGGBB) and ARGB (#AARRGGBB) formats.
        /// </summary>
        /// <param name="colorString">Color string in hex format (with or without # prefix)</param>
        /// <param name="color">The parsed color, or WindowTextColor if parsing fails</param>
        /// <returns>True if parsing succeeded, false otherwise</returns>
        public static bool TryParseColor(string colorString, out Color color)
        {
            color = SystemColors.WindowTextColor;

            if (string.IsNullOrWhiteSpace(colorString))
                return false;

            try
            {
                // Remove # if present
                string hex = colorString.TrimStart('#');

                // Handle different formats
                if (hex.Length == 6)
                {
                    // RGB format, add FF for alpha
                    hex = "FF" + hex;
                }
                else if (hex.Length == 8)
                {
                    // ARGB format
                }
                else
                {
                    return false;
                }

                // Parse hex string
                byte a = Convert.ToByte(hex.Substring(0, 2), 16);
                byte r = Convert.ToByte(hex.Substring(2, 2), 16);
                byte g = Convert.ToByte(hex.Substring(4, 2), 16);
                byte b = Convert.ToByte(hex.Substring(6, 2), 16);

                color = Color.FromArgb(a, r, g, b);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Updates a global color brush resource by key.
        /// </summary>
        /// <param name="resourceKey">The resource key (e.g., "ButtonTextColorBrush")</param>
        /// <param name="brush">The brush to apply</param>
        public static void UpdateColorBrush(string resourceKey, Brush brush)
        {
            if (Application.Current != null && Application.Current.Resources != null)
            {
                Application.Current.Resources[resourceKey] = brush;
            }
        }

        /// <summary>
        /// Loads colors from an INI file and applies them to global resources.
        /// </summary>
        /// <param name="colorSettings">Dictionary of color settings from INI file</param>
        public static void LoadAndApplyColors(Dictionary<string, string> colorSettings)
        {
            if (colorSettings == null)
                return;

            foreach (var config in ColorConfigs.Values)
            {
                if (colorSettings.TryGetValue(config.Key, out string colorValue))
                {
                    Brush brush;
                    if (string.IsNullOrWhiteSpace(colorValue))
                    {
                        // Use system default if blank
                        brush = config.DefaultSystemBrush;
                    }
                    else if (TryParseColor(colorValue, out Color color))
                    {
                        // Use parsed color
                        brush = new SolidColorBrush(color);
                    }
                    else
                    {
                        // Invalid color, use system default
                        brush = config.DefaultSystemBrush;
                    }
                    UpdateColorBrush(config.ResourceKey, brush);
                }
            }
        }

        /// <summary>
        /// Converts a WPF Color to a hex string format (e.g., #FF000000 for black).
        /// </summary>
        /// <param name="color">The color to convert</param>
        /// <returns>Hex string representation of the color in ARGB format</returns>
        public static string ColorToString(Color color)
        {
            return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        }
    }
}

using System;
using System.IO;

namespace SimpleLLMChatCLI
{
    internal static class ImageEncoder
    {
        public const string DefaultMime = "image/png";

        public static string ImageFileToBase64(string path, out string mime)
        {
            mime = GuessMime(path);
            return Convert.ToBase64String(File.ReadAllBytes(path));
        }

        public static string GuessMime(string path)
        {
            if (string.IsNullOrEmpty(path))
                return DefaultMime;

            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".jpg":
                case ".jpeg":
                    return "image/jpeg";
                case ".gif":
                    return "image/gif";
                case ".webp":
                    return "image/webp";
                case ".bmp":
                    return "image/bmp";
                case ".tif":
                case ".tiff":
                    return "image/tiff";
                case ".png":
                    return "image/png";
                default:
                    return DefaultMime;
            }
        }
    }
}

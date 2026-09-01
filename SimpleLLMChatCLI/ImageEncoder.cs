using System;
using System.IO;

namespace SimpleLLMChatCLI
{
    internal static class ImageEncoder
    {
        public static string ImageFileToBase64(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            return Convert.ToBase64String(bytes);
        }
    }
}

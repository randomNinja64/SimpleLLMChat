using System;
using System.IO;

namespace SimpleLLMChatCLI
{
    internal class ImageHandler
    {
        public static string ImageFileToBase64(string path)
        {
            byte[] bytes = File.ReadAllBytes(path); // Read file bytes
            return Convert.ToBase64String(bytes);   // Convert bytes to base64
        }
    }
}

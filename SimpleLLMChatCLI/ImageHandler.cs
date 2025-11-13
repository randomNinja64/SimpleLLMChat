using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SimpleLLMChatCLISharp
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace WebTools
{
    public static class DownloadHandler
    {
        /// <summary>
        /// Downloads with yt-dlp.exe to <paramref name="directory"/> (the desktop when empty);
        /// a failure keeps yt-dlp's exit code.
        /// </summary>
        public static ToolResult DownloadVideo(string URL, string directory = null)
        {
            string targetDir = string.IsNullOrWhiteSpace(directory)
                ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                : Path.GetFullPath(Environment.ExpandEnvironmentVariables(directory.Trim()));

            if (!Directory.Exists(targetDir))
            {
                try
                {
                    Directory.CreateDirectory(targetDir);
                }
                catch (Exception ex)
                {
                    throw new IOException($"Failed to create directory '{targetDir}': {ex.Message}", ex);
                }
            }

            // An explicit output template in the target folder lets yt-dlp handle the file naming.
            // A trailing backslash would escape the closing quote, so the template always ends in a file name.
            string outputTemplate = Path.Combine(targetDir, "%(title)s.%(ext)s");
            string arguments = $"--no-progress -o \"{outputTemplate}\" \"{URL}\"";

            string output = ToolHelper.ExecuteProcess("yt-dlp.exe", arguments, out int exitCode);

            if (exitCode != 0)
                return new ToolResult($"yt-dlp exited with code {exitCode}:\n{output}", exitCode);

            return new ToolResult($"Video downloaded successfully to {targetDir}\n{output}", 0);
        }

        /// <summary>Downloads with curl.exe; a failure keeps curl's exit code.</summary>
        public static ToolResult DownloadFile(string filename, string URL)
        {
            filename = Environment.ExpandEnvironmentVariables(filename);

            // When the extension implies a type, check it against the server's Content-Type first.
            string[] expectedTypes = GetExpectedMimeTypes(Path.GetExtension(filename));
            string contentType = expectedTypes != null ? GetContentTypeFromURL(URL) : "";

            // application/octet-stream (generic binary stream) is always accepted.
            if (!string.IsNullOrEmpty(contentType)
                && !contentType.ToLower().Contains("application/octet-stream")
                && !Array.Exists(expectedTypes, t => contentType.ToLower().Contains(t.ToLower())))
            {
                throw new InvalidOperationException(
                    $"File type mismatch: Expected {string.Join(" or ", expectedTypes)} but got '{contentType}'. Download cancelled.");
            }

            string directory = Path.GetDirectoryName(filename);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                try
                {
                    Directory.CreateDirectory(directory);
                }
                catch (Exception ex)
                {
                    throw new IOException($"Failed to create directory '{directory}': {ex.Message}", ex);
                }
            }

            string output = CurlHelper.Execute(URL, out int exitCode,
                extraFlags: "-o \"" + filename + "\"");

            if (exitCode != 0)
                return new ToolResult($"curl exited with code {exitCode}: {output}", exitCode);

            string successMessage = $"File downloaded successfully: {filename}";
            if (!string.IsNullOrEmpty(contentType))
                successMessage += $" (Content-Type: {contentType})";

            return new ToolResult(successMessage, 0);
        }

        // Content-Type from a HEAD request, or "" when the request fails or sends none
        // (the download then proceeds unchecked).
        private static string GetContentTypeFromURL(string URL)
        {
            string headers = CurlHelper.Execute(URL, out int exitCode,
                extraFlags: "-I", combineErrorOutput: false);
            if (exitCode != 0)
                return "";

            // When following redirects, curl returns several sets of headers; search from
            // the end to get the Content-Type of the final destination.
            Regex contentTypeRegex = new Regex(@"content-type:\s*([^\r\n;]+)", RegexOptions.IgnoreCase | RegexOptions.RightToLeft);
            Match match = contentTypeRegex.Match(headers);
            return match.Success ? match.Groups[1].Value.Trim() : "";
        }

        private static readonly Dictionary<string, string[]> MimeTypeMap = new Dictionary<string, string[]>()
        {
            // Images
            { ".jpg", new[] { "image/jpeg" } },
            { ".jpeg", new[] { "image/jpeg" } },
            { ".png", new[] { "image/png" } },
            { ".gif", new[] { "image/gif" } },
            { ".webp", new[] { "image/webp" } },
            { ".bmp", new[] { "image/bmp" } },
            { ".svg", new[] { "image/svg+xml" } },
            { ".ico", new[] { "image/x-icon", "image/vnd.microsoft.icon" } },

            // Documents
            { ".pdf", new[] { "application/pdf" } },
            { ".doc", new[] { "application/msword" } },
            { ".docx", new[] { "application/vnd.openxmlformats-officedocument.wordprocessingml.document" } },
            { ".xls", new[] { "application/vnd.ms-excel" } },
            { ".xlsx", new[] { "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" } },
            { ".ppt", new[] { "application/vnd.ms-powerpoint" } },
            { ".pptx", new[] { "application/vnd.openxmlformats-officedocument.presentationml.presentation" } },

            // Text files
            { ".txt", new[] { "text/plain" } },
            { ".html", new[] { "text/html" } },
            { ".htm", new[] { "text/html" } },
            { ".css", new[] { "text/css" } },
            { ".js", new[] { "text/javascript", "application/javascript" } },
            { ".json", new[] { "application/json" } },
            { ".xml", new[] { "text/xml", "application/xml" } },
            { ".csv", new[] { "text/csv" } },

            // Archives
            { ".zip", new[] { "application/zip" } },
            { ".rar", new[] { "application/x-rar-compressed" } },
            { ".7z", new[] { "application/x-7z-compressed" } },
            { ".tar", new[] { "application/x-tar" } },
            { ".gz", new[] { "application/gzip" } },

            // Audio
            { ".mp3", new[] { "audio/mpeg" } },
            { ".wav", new[] { "audio/wav" } },
            { ".ogg", new[] { "audio/ogg" } },
            { ".flac", new[] { "audio/flac" } },

            // Video
            { ".mp4", new[] { "video/mp4" } },
            { ".avi", new[] { "video/x-msvideo" } },
            { ".mkv", new[] { "video/x-matroska" } },
            { ".mov", new[] { "video/quicktime" } },
            { ".webm", new[] { "video/webm" } },

            // Executables and binaries
            { ".exe", new[] { "application/x-msdownload", "application/x-msdos-program", "application/octet-stream" } },
            { ".dll", new[] { "application/x-msdownload", "application/x-msdos-program", "application/octet-stream" } },
            { ".bin", new[] { "application/octet-stream" } }
        };

        private static string[] GetExpectedMimeTypes(string fileExtension)
        {
            string[] result;
            if (MimeTypeMap.TryGetValue(fileExtension.ToLower(), out result))
            {
                return result;
            }
            return null;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

public static class DownloadHandler
{
    public static string DownloadVideo(string URL, out int exitCode)
    {
        try
        {
            // Get the user's desktop path
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

            // Use an explicit output template in the desktop folder so yt-dlp handles the file naming
            string outputTemplate = Path.Combine(desktopPath, "%(title)s.%(ext)s");

            // Build arguments: no progress, output template, then the URL (each argument quoted)
            string arguments = $"--no-progress -o \"{outputTemplate}\" \"{URL}\"";

            string output = ToolHandler.ExecuteProcess("yt-dlp.exe", arguments, out exitCode);

            if (exitCode != 0)
                return $"yt-dlp exited with code {exitCode}:\n{output}";

            return $"Video downloaded successfully to desktop\n{output}";
        }
        catch (Exception ex)
        {
            exitCode = -1;
            return "Error running yt-dlp.exe: " + ex.Message;
        }
    }

    public static string DownloadFile(string filename, string URL, out int exitCode)
    {
        exitCode = 0;

        try
        {
            // Expand environment variables in filename
            filename = Environment.ExpandEnvironmentVariables(filename);

            // Get expected MIME types based on file extension
            string fileExtension = Path.GetExtension(filename).ToLower();
            string[] expectedTypes = GetExpectedMimeTypes(fileExtension);

            string contentType = "";

            // Only perform HEAD request if we have expected MIME types to validate
            if (expectedTypes != null)
            {
                contentType = GetContentTypeFromURL(URL, out int headExitCode);
                
                // If HEAD request succeeds, validate the content type
                if (headExitCode == 0 && !string.IsNullOrEmpty(contentType))
                {
                    // Always accept application/octet-stream (generic binary stream)
                    if (!contentType.ToLower().Contains("application/octet-stream"))
                    {
                        // Verify content type matches expected type
                        bool isValidType = false;
                        foreach (string expectedType in expectedTypes)
                        {
                            if (contentType.ToLower().Contains(expectedType.ToLower()))
                            {
                                isValidType = true;
                                break;
                            }
                        }

                        if (!isValidType)
                        {
                            exitCode = 1;
                            return $"File type mismatch: Expected {string.Join(" or ", expectedTypes)} but got '{contentType}'. Download cancelled.";
                        }
                    }
                }
                // If HEAD request fails, proceed with download anyway
            }

            // Ensure directory exists
            string directory = Path.GetDirectoryName(filename);
            if (!FileHandler.EnsureDirectoryExists(directory, out int dirExitCode, out string dirError))
            {
                exitCode = dirExitCode;
                return dirError;
            }

            // Build curl arguments
            string arguments =
                "-L -s " +
                "-H \"User-Agent: " + ToolHandler.USER_AGENT + "\" " +
                "-o \"" + filename + "\" " +
                "\"" + URL + "\"";

            string output = ToolHandler.ExecuteProcess("curl.exe", arguments, out exitCode);

            if (exitCode != 0)
            {
                return $"curl exited with code {exitCode}: {output}";
            }

            string successMessage = $"File downloaded successfully: {filename}";
            if (!string.IsNullOrEmpty(contentType))
            {
                successMessage += $" (Content-Type: {contentType})";
            }

            return successMessage;
        }
        catch (Exception ex)
        {
            exitCode = -1;
            return "Error running curl.exe: " + ex.Message;
        }
    }

    private static string GetContentTypeFromURL(string URL, out int exitCode)
    {
        try
        {
            // Build curl HEAD request arguments
            string arguments =
                "-I -L -s " +
                "-H \"User-Agent: " + ToolHandler.USER_AGENT + "\" " +
                "\"" + URL + "\"";

            string headers = ToolHandler.ExecuteProcess("curl.exe", arguments, out exitCode, combineErrorOutput: false);

            if (exitCode != 0)
            {
                return "";
            }

            // Extract Content-Type from headers
            // When following redirects, curl returns multiple sets of headers
            // Search from the end to get the LAST Content-Type (from the final destination)
            Regex contentTypeRegex = new Regex(@"content-type:\s*([^\r\n;]+)", RegexOptions.IgnoreCase | RegexOptions.RightToLeft);
            Match match = contentTypeRegex.Match(headers);
            
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }

            return "";
        }
        catch (Exception)
        {
            exitCode = -1;
            return "";
        }
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
        { ".exe", new[] { "application/x-msdownload", "application/octet-stream" } },
        { ".dll", new[] { "application/x-msdownload", "application/octet-stream" } },
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


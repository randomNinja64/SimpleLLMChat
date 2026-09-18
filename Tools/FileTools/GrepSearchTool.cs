using System;
using System.IO;
using System.Text;

namespace FileTools
{
    public static class GrepSearchTool
    {
        public static string Search(
            string pattern,
            string directoryPath,
            string filePattern,
            string caseInsensitive,
            int maxContentLength,
            out int exitCode)
        {
            exitCode = 0;

            try
            {
                string searchPath = string.IsNullOrEmpty(directoryPath)
                    ? Environment.CurrentDirectory
                    : Environment.ExpandEnvironmentVariables(directoryPath.Trim());

                if (!Directory.Exists(searchPath))
                {
                    exitCode = 1;
                    return "error: Directory not found: " + searchPath;
                }

                StringBuilder args = new StringBuilder();
                args.Append("-r ");

                if (!string.IsNullOrEmpty(caseInsensitive) &&
                    caseInsensitive.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
                {
                    args.Append("-i ");
                }

                args.Append("-E ");

                if (!string.IsNullOrEmpty(filePattern) && filePattern.Trim().Length > 0)
                {
                    args.Append("--include=");
                    args.Append("\"" + filePattern.Trim() + "\" ");
                }

                foreach (string excludeDir in GrepExcludeLists.ExcludedDirectoryNames)
                    args.Append("--exclude-dir=").Append(excludeDir).Append(" ");
                foreach (string excludeGlob in GrepExcludeLists.ExcludedFileGlobs)
                    args.Append("--exclude=").Append(excludeGlob).Append(" ");

                string escapedPattern = pattern.Replace("\"", "\\\"");
                args.Append("\"" + escapedPattern + "\" ");
                args.Append("\"" + searchPath + "\"");

                string output = ToolHelper.ExecuteProcess("grep.exe", args.ToString(), out exitCode, combineErrorOutput: false);

                // GNU grep: 0 = matches, 1 = no matches, >=2 = error
                if (exitCode == 1 || (exitCode == 0 && string.IsNullOrWhiteSpace(output)))
                {
                    exitCode = 0;
                    return "No matches found for pattern: " + pattern;
                }

                if (exitCode != 0)
                {
                    if (string.IsNullOrWhiteSpace(output))
                        return "error: grep exited with code " + exitCode;
                    return "error: grep exited with code " + exitCode + ":\n" + output;
                }

                if (maxContentLength > 0 && output.Length > maxContentLength)
                {
                    return output.Substring(0, maxContentLength) + "\n...[truncated]";
                }

                return output;
            }
            catch (Exception ex)
            {
                exitCode = 1;
                return "error: running grep.exe: " + ex.Message;
            }
        }
    }
}

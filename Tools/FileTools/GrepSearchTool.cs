using System;
using System.IO;
using System.Text;

namespace FileTools
{
    public static class GrepSearchTool
    {
        /// <summary>Searches with grep.exe; a grep error keeps grep's exit code.</summary>
        public static ToolResult Search(
            string pattern,
            string directoryPath,
            string filePattern,
            string caseInsensitive,
            int maxContentLength)
        {
            string searchPath = string.IsNullOrEmpty(directoryPath)
                ? Environment.CurrentDirectory
                : Environment.ExpandEnvironmentVariables(directoryPath.Trim());

            if (!Directory.Exists(searchPath))
                throw new DirectoryNotFoundException("Directory not found: " + searchPath);

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

            string output = ToolHelper.ExecuteProcess("grep.exe", args.ToString(), out int exitCode, combineErrorOutput: false);

            // GNU grep: 0 = matches, 1 = no matches, >=2 = error
            if (exitCode == 1 || (exitCode == 0 && string.IsNullOrWhiteSpace(output)))
                return new ToolResult("No matches found for pattern: " + pattern, 0);

            if (exitCode != 0)
            {
                string message = "error: grep exited with code " + exitCode;
                if (!string.IsNullOrWhiteSpace(output))
                    message += ":\n" + output;
                return new ToolResult(message, exitCode);
            }

            if (maxContentLength > 0 && output.Length > maxContentLength)
                output = output.Substring(0, maxContentLength) + "\n...[truncated]";

            return new ToolResult(output, 0);
        }
    }
}

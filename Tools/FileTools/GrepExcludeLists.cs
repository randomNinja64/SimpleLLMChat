using System.Collections.Generic;

namespace FileTools
{
    /// <summary>
    /// Directory and file exclusion globs passed to grep.exe so noisy VCS/build/deps
    /// paths are skipped. Mirrors NyoCoder FileScanFilter exclude lists.
    /// </summary>
    internal static class GrepExcludeLists
    {
        public static readonly string[] ExcludedDirectoryNames =
        {
            ".git", ".svn", ".hg",
            ".venv", "venv", "__pycache__",
            "node_modules", "bin", "obj",
            ".vs", "packages", "dist",
            "build", ".idea", ".vscode",
            "target", "vendor", "bower_components",
            ".nuget", "TestResults"
        };

        public static IEnumerable<string> ExcludedFileGlobs
        {
            get
            {
                string[] extensions =
                {
                    ".pyc", ".pyo", ".exe", ".dll",
                    ".so", ".dylib", ".obj", ".o",
                    ".a", ".lib", ".pdb", ".ilk",
                    ".class", ".jar", ".war", ".ear",
                    ".zip", ".tar", ".gz", ".rar",
                    ".png", ".jpg", ".jpeg", ".gif",
                    ".bmp", ".ico", ".svg", ".pdf",
                    ".mp3", ".mp4", ".avi", ".mov",
                    ".ttf", ".woff", ".woff2", ".eot",
                    ".map", ".lock", ".cache"
                };
                foreach (string ext in extensions)
                    yield return "*" + ext;

                yield return "*.min.js";
                yield return "*.min.css";
            }
        }
    }
}

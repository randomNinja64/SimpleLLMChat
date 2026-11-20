using System;
using System.IO;
using System.Text;

namespace PythonTools
{
    public static class PythonRunner
    {
        /// <summary>Runs the script and returns its output with Python's exit code.</summary>
        public static ToolResult RunPythonScript(string scriptContent)
        {
            string pythonCommand = FindPython();
            if (pythonCommand == null)
                throw new InvalidOperationException("Python runtime not found in system PATH. Please install Python and ensure it's added to your system PATH environment variable.");

            string tempScriptPath = Path.Combine(Path.GetTempPath(), $"temp_script_{Guid.NewGuid()}.py");
            try
            {
                // The BOM is kept on purpose: Python 2 rejects non-ASCII source that has
                // neither a BOM nor a coding line.
                File.WriteAllText(tempScriptPath, scriptContent, Encoding.UTF8);

                string output = ToolHelper.ExecuteProcess(pythonCommand, $"\"{tempScriptPath}\"", out int exitCode, combineErrorOutput: true);
                return new ToolResult(output, exitCode);
            }
            finally
            {
                try { File.Delete(tempScriptPath); }
                catch { }
            }
        }

        // First of the common launcher names that answers --version, or null.
        private static string FindPython()
        {
            foreach (string cmd in new[] { "python", "python3", "py" })
            {
                try
                {
                    ToolHelper.ExecuteProcess(cmd, "--version", out int exitCode, combineErrorOutput: true);
                    if (exitCode == 0)
                        return cmd;
                }
                catch
                {
                    // Not installed under this name; try the next.
                }
            }
            return null;
        }
    }
}

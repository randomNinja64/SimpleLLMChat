using System;
using System.IO;
using System.Text;

namespace BuiltInTools
{
    public static class PythonTools
    {
        public static string RunPythonScript(string scriptContent, out int exitCode)
        {
            exitCode = 0;
            string pythonCommand = "";

            try
            {
                // Check if Python is installed by trying common commands
                string[] pythonCommands = { "python", "python3", "py" };
                bool pythonFound = false;

                foreach (string cmd in pythonCommands)
                {
                    try
                    {
                        string versionOutput = ToolHelper.ExecuteProcess(cmd, "--version", out int versionExitCode, combineErrorOutput: true);
                        if (versionExitCode == 0)
                        {
                            pythonCommand = cmd;
                            pythonFound = true;
                            break;
                        }
                    }
                    catch
                    {
                        // Continue to next command
                    }
                }

                if (!pythonFound)
                {
                    exitCode = 1;
                    return "Error: Python runtime not found in system PATH. Please install Python and ensure it's added to your system PATH environment variable.";
                }

                // Create a temporary Python script file
                string tempScriptPath = Path.Combine(Path.GetTempPath(), $"temp_script_{Guid.NewGuid()}.py");

                try
                {
                    // Write the script content to the temporary file
                    File.WriteAllText(tempScriptPath, scriptContent, Encoding.UTF8);

                    // Execute the Python script
                    string output = ToolHelper.ExecuteProcess(pythonCommand, $"\"{tempScriptPath}\"", out exitCode, combineErrorOutput: true);

                    return output;
                }
                finally
                {
                    // Clean up the temporary file
                    try
                    {
                        if (File.Exists(tempScriptPath))
                        {
                            File.Delete(tempScriptPath);
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                exitCode = -1;
                return "Error executing Python script: " + ex.Message;
            }
        }
    }
}

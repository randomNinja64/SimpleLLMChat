using System;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Threading;

/// <summary>
/// Host-side curl.exe HTTPS fallback (CLI chat/embeddings, GUI models list).
/// Tool packages keep their own curl wrappers.
/// </summary>
public static class CurlHttpsClient
{
    /// <summary>Buffered GET (e.g. /v1/models).</summary>
    public static string GetJson(string url, string apiKey, out int exitCode)
    {
        exitCode = -1;
        try
        {
            string args = "-s -X GET"
                + " -H \"Accept: application/json\""
                + AuthHeader(apiKey)
                + " \"" + url + "\"";

            string output = null;
            string error = null;
            exitCode = Run(args, process =>
            {
                Thread errThread = StartStderrReader(process, text => error = text);
                output = process.StandardOutput.ReadToEnd();
                errThread.Join(5000);
            });

            if (exitCode != 0 && string.IsNullOrEmpty(output))
                return "cURL failed (exit " + exitCode + "): " + error;
            return output;
        }
        catch (Exception ex)
        {
            exitCode = -1;
            return "cURL fallback failed: " + ex.Message;
        }
    }

    /// <summary>Buffered JSON POST via named pipe (body never touches disk).</summary>
    public static string PostJson(string url, string apiKey, string jsonBody, out int exitCode)
    {
        exitCode = -1;
        try
        {
            string output = null;
            string error = null;
            exitCode = Post(url, apiKey, Encoding.UTF8.GetBytes(jsonBody ?? ""), noBuffer: false, process =>
            {
                Thread errThread = StartStderrReader(process, text => error = text);
                output = process.StandardOutput.ReadToEnd();
                errThread.Join(5000);
            });

            if (exitCode != 0 && string.IsNullOrEmpty(output))
                return "cURL failed (exit " + exitCode + "): " + error;
            return output;
        }
        catch (Exception ex)
        {
            exitCode = -1;
            return "cURL fallback failed: " + ex.Message;
        }
    }

    /// <summary>
    /// POST JSON over a named pipe. <paramref name="consume"/> reads stdout while curl runs.
    /// Returns the process exit code.
    /// </summary>
    public static int Post(string url, string apiKey, byte[] body, bool noBuffer, Action<Process> consume)
    {
        if (body == null)
            body = new byte[0];

        string pipeName = "llmcurl_" + Guid.NewGuid().ToString("N");
        string args = "-s"
            + (noBuffer ? " -N" : "")
            + " -X POST"
            + " -H \"Content-Type: application/json\""
            + AuthHeader(apiKey)
            + " --data-binary \"@\\\\.\\pipe\\" + pipeName + "\""
            + " \"" + url + "\"";

        using (NamedPipeServerStream pipeServer = new NamedPipeServerStream(
            pipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte))
        {
            return Run(args, process =>
            {
                Thread pipeThread = new Thread(() =>
                {
                    try
                    {
                        pipeServer.WaitForConnection();
                        pipeServer.Write(body, 0, body.Length);
                        pipeServer.Flush();
                        pipeServer.Close();
                    }
                    catch { }
                });
                pipeThread.IsBackground = true;
                pipeThread.Start();

                if (consume != null)
                    consume(process);

                pipeThread.Join(5000);
            });
        }
    }

    private static string AuthHeader(string apiKey)
    {
        return string.IsNullOrEmpty(apiKey)
            ? ""
            : " -H \"Authorization: Bearer " + apiKey + "\"";
    }

    private static int Run(string args, Action<Process> consume)
    {
        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = TlsCurlFallback.DefaultCurlPath,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using (Process process = Process.Start(psi))
        {
            if (consume != null)
                consume(process);
            process.WaitForExit();
            return process.ExitCode;
        }
    }

    private static Thread StartStderrReader(Process process, Action<string> onComplete)
    {
        Thread errThread = new Thread(() =>
        {
            string text = null;
            try { text = process.StandardError.ReadToEnd(); }
            catch { }
            if (onComplete != null)
                onComplete(text);
        });
        errThread.IsBackground = true;
        errThread.Start();
        return errThread;
    }
}

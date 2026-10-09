using System;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace WebTools
{
    internal static class CurlHelper
    {
        /// <summary>
        /// Runs curl with -s -L, the configured user-agent, and the given URL.
        /// Extra flags (e.g. "-I", "-o file") and extra headers are optional.
        /// </summary>
        public static string Execute(string url, out int exitCode,
            string extraFlags = "", bool combineErrorOutput = true, params string[] extraHeaders)
        {
            string ua = "-H \"User-Agent: " + ToolHelper.GetConfigValue("useragent") + "\"";
            string hdrs = string.Concat(System.Array.ConvertAll(extraHeaders, h => " -H \"" + h + "\""));
            string flags = string.IsNullOrEmpty(extraFlags) ? "" : extraFlags + " ";
            string arguments = "-s -L " + flags + ua + hdrs + " \"" + url + "\"";
            return ToolHelper.ExecuteProcess("curl.exe", arguments, out exitCode, combineErrorOutput);
        }

        /// <summary>
        /// POSTs a JSON body via curl. The body is written to a named pipe so it
        /// is not quoted on the command line. Optional extra headers (e.g. Authorization).
        /// </summary>
        public static string PostJson(string url, string jsonBody, out int exitCode,
            bool combineErrorOutput = true, params string[] extraHeaders)
        {
            byte[] body = Encoding.UTF8.GetBytes(jsonBody ?? "");
            string pipeName = "webcurl_" + Guid.NewGuid().ToString("N");
            string hdrs = " -H \"Content-Type: application/json\""
                + string.Concat(Array.ConvertAll(extraHeaders, h => " -H \"" + h + "\""));
            string arguments = "-s -L -X POST" + hdrs
                + " --data-binary \"@\\\\.\\pipe\\" + pipeName + "\" \"" + url + "\"";

            using (NamedPipeServerStream pipe = new NamedPipeServerStream(
                pipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte))
            {
                Thread writer = new Thread(delegate()
                {
                    try
                    {
                        pipe.WaitForConnection();
                        if (body.Length > 0)
                            pipe.Write(body, 0, body.Length);
                        pipe.Flush();
                        pipe.Close();
                    }
                    catch
                    {
                    }
                });
                writer.IsBackground = true;
                writer.Start();

                string output = ToolHelper.ExecuteProcess(
                    "curl.exe", arguments, out exitCode, combineErrorOutput);
                writer.Join(5000);
                return output;
            }
        }

        public static string[] FirecrawlAuthHeaders(string apiKey)
        {
            return string.IsNullOrWhiteSpace(apiKey)
                ? new string[0]
                : new[] { "Authorization: Bearer " + apiKey.Trim() };
        }
    }
}

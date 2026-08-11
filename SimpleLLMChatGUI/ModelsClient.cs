using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace SimpleLLMChatGUI
{
    /// <summary>
    /// Minimal client for OpenAI-compatible <c>/v1/models</c>. Uses <see cref="TlsConfig"/>
    /// and an optional curl.exe HTTPS fallback (same pattern as the CLI).
    /// </summary>
    public static class ModelsClient
    {
        private sealed class ModelsException : Exception
        {
            public ModelsException(string message) : base(message) { }
            public ModelsException(string message, Exception inner) : base(message, inner) { }
        }

        /// <summary>
        /// Fetches model ids from <c>{baseUrl}/v1/models</c>.
        /// Throws if the server is unreachable or the response is invalid.
        /// </summary>
        public static IList<string> ListModels(string baseUrl, string apiKey)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new ModelsException("LLM server URL is empty.");

            TlsConfig.EnsureModernProtocols();

            string url = baseUrl.Trim().TrimEnd('/') + "/v1/models";
            string key = apiKey ?? string.Empty;
            string body = GetModelsJson(url, key);
            return ParseModelIds(body);
        }

        private static string GetModelsJson(string url, string apiKey)
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.Accept = "application/json";
                if (!string.IsNullOrEmpty(apiKey))
                    request.Headers.Add("Authorization", "Bearer " + apiKey);
                request.Timeout = 30000;
                request.ReadWriteTimeout = 30000;

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream responseStream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(responseStream, Encoding.UTF8))
                    return reader.ReadToEnd();
            }
            catch (Exception ex)
            {
                string httpBody = TryReadWebExceptionBody(ex);

                if (TlsCurlFallback.CanAttempt(url, ex))
                {
                    int exitCode;
                    string body = CurlGetJson(url, apiKey, out exitCode);
                    if (exitCode == 0 && !string.IsNullOrEmpty(body))
                        return body;
                    throw new ModelsException(
                        "Unable to reach /v1/models (curl): " + (body ?? httpBody ?? ex.Message),
                        ex);
                }

                if (!string.IsNullOrEmpty(httpBody))
                    throw new ModelsException("Unable to reach /v1/models: " + ex.Message + " — " + httpBody, ex);
                throw new ModelsException("Unable to reach /v1/models: " + ex.Message, ex);
            }
        }

        private static IList<string> ParseModelIds(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                throw new ModelsException("Models response was empty.");

            JObject root;
            try
            {
                root = JObject.Parse(body);
            }
            catch (Exception ex)
            {
                throw new ModelsException("Models response was not valid JSON.", ex);
            }

            JArray data = root["data"] as JArray;
            if (data == null)
                throw new ModelsException("Models response missing 'data' array.");

            List<string> ids = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JToken item in data)
            {
                JObject obj = item as JObject;
                if (obj == null)
                    continue;
                JToken idToken = obj["id"];
                if (idToken == null || idToken.Type != JTokenType.String)
                    continue;
                string id = ((string)idToken).Trim();
                if (id.Length == 0 || !seen.Add(id))
                    continue;
                ids.Add(id);
            }

            if (ids.Count == 0)
                throw new ModelsException("Models response contained no model ids.");

            ids.Sort(StringComparer.OrdinalIgnoreCase);
            return ids;
        }

        private static string CurlGetJson(string fullUrl, string apiKey, out int exitCode)
        {
            exitCode = -1;
            try
            {
                string authHeader = string.IsNullOrEmpty(apiKey)
                    ? ""
                    : " -H \"Authorization: Bearer " + apiKey + "\"";

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = TlsCurlFallback.DefaultCurlPath,
                    Arguments = "-s -X GET"
                        + " -H \"Accept: application/json\""
                        + authHeader
                        + " \"" + fullUrl + "\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (Process process = Process.Start(psi))
                {
                    string output = "";
                    string error = "";
                    Thread errThread = new Thread(() => { try { error = process.StandardError.ReadToEnd(); } catch { } });
                    errThread.IsBackground = true;
                    errThread.Start();

                    output = process.StandardOutput.ReadToEnd();
                    errThread.Join(5000);
                    process.WaitForExit();
                    exitCode = process.ExitCode;

                    if (exitCode != 0 && string.IsNullOrEmpty(output))
                        return "cURL failed (exit " + exitCode + "): " + error;
                    return output;
                }
            }
            catch (Exception ex)
            {
                exitCode = -1;
                return "cURL fallback failed: " + ex.Message;
            }
        }

        private static string TryReadWebExceptionBody(Exception ex)
        {
            WebException webEx = ex as WebException;
            if (webEx == null || webEx.Response == null)
                return null;

            try
            {
                using (Stream stream = webEx.Response.GetResponseStream())
                {
                    if (stream == null)
                        return null;
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        string body = reader.ReadToEnd();
                        if (string.IsNullOrWhiteSpace(body))
                            return null;
                        if (body.Length > 500)
                            return body.Substring(0, 500) + "...";
                        return body;
                    }
                }
            }
            catch
            {
                return null;
            }
        }
    }
}

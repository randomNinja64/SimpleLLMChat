using System;
using System.IO;
using System.Net;
using System.Text;

/// <summary>
/// Buffered JSON HTTP with the same curl.exe fallback as chat and embeddings.
/// Streaming chat completions stay on their own path.
/// </summary>
public static class JsonHttpClient
{
    public static string Post(string url, string apiKey, string jsonBody, int timeoutMs, out string error)
    {
        error = null;
        string key = apiKey ?? string.Empty;
        try
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Accept = "application/json";
            if (!string.IsNullOrEmpty(key))
                request.Headers.Add("Authorization", "Bearer " + key);
            request.Timeout = timeoutMs;
            request.ReadWriteTimeout = timeoutMs;

            byte[] bytes = Encoding.UTF8.GetBytes(jsonBody ?? "");
            request.ContentLength = bytes.Length;
            using (Stream stream = request.GetRequestStream())
                stream.Write(bytes, 0, bytes.Length);

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream responseStream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(responseStream, Encoding.UTF8))
                return reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            if (TlsCurlFallback.CanAttempt(url, ex))
            {
                int exitCode;
                string body = CurlHttpsClient.PostJson(url, key, jsonBody, out exitCode);
                if (exitCode == 0 && !string.IsNullOrEmpty(body))
                    return body;
                error = "curl: " + (body ?? ex.Message);
                return null;
            }

            error = ex.Message;
            return null;
        }
    }
}

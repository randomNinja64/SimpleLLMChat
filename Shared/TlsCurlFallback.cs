using System;
using System.IO;
using System.Net;

/// <summary>
/// Detects TLS/connection failures where bundled curl.exe may succeed on legacy .NET 4.0.
/// </summary>
public static class TlsCurlFallback
{
    public static string DefaultCurlPath
    {
        get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "curl.exe"); }
    }

    /// <summary>
    /// True when the URL is HTTPS, curl.exe is present beside the app, and the failure
    /// looks like a TLS/connection issue curl may work around — not ordinary HTTP API errors.
    /// </summary>
    public static bool CanAttempt(string url, Exception ex)
    {
        return !string.IsNullOrEmpty(url)
            && url.StartsWith("https:", StringComparison.OrdinalIgnoreCase)
            && File.Exists(DefaultCurlPath)
            && ShouldAttempt(ex);
    }

    /// <summary>
    /// True when the failure looks like a TLS/connection issue that curl may work around.
    /// </summary>
    private static bool ShouldAttempt(Exception ex)
    {
        if (ex == null)
            return false;

        WebException webEx = ex as WebException;
        if (webEx != null)
            return webEx.Status == WebExceptionStatus.SecureChannelFailure
                || webEx.Status == WebExceptionStatus.TrustFailure
                || webEx.Status == WebExceptionStatus.ConnectFailure
                || webEx.Status == WebExceptionStatus.ConnectionClosed
                || webEx.Status == WebExceptionStatus.SendFailure
                || webEx.Status == WebExceptionStatus.ReceiveFailure
                || webEx.Status == WebExceptionStatus.Timeout
                || webEx.Status == WebExceptionStatus.ServerProtocolViolation
                || (webEx.InnerException != null
                    && webEx.InnerException.GetType().Name.Contains("Authentication"));

        return ex.GetType().Name.Contains("Authentication")
            || ex.GetType().Name.Contains("Security")
            || ex.GetType().Name.Contains("IOException")
            || (ex.Message != null
                && ex.Message.IndexOf("connection", StringComparison.OrdinalIgnoreCase) >= 0);
    }
}

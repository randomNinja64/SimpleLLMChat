using System;
using System.Net;
using System.Threading;

/// <summary>
/// Process-wide TLS enablement for .NET Framework 4.0 HttpWebRequest clients.
/// .NET 4.0 only names Tls (1.0); newer protocols are enabled by numeric value.
/// </summary>
public static class TlsConfig
{
    private static int _enabled;

    /// <summary>
    /// Adds TLS 1.0–1.3 to <see cref="ServicePointManager.SecurityProtocol"/>.
    /// Safe to call more than once; only the first call applies.
    /// </summary>
    public static void EnsureModernProtocols()
    {
        if (Interlocked.Exchange(ref _enabled, 1) != 0)
            return;

        try
        {
            // Tls11 = 768, Tls12 = 3072, Tls13 = 12288
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls
                | (SecurityProtocolType)768
                | (SecurityProtocolType)3072
                | (SecurityProtocolType)12288;
        }
        catch
        {
            // Keep system defaults on very old hosts without TLS 1.2 support.
        }
    }
}

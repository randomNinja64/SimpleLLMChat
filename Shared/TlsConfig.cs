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

        // Tls11 = 768, Tls12 = 3072, Tls13 = 12288.
        // One at a time: the setter rejects the whole value if any bit is unknown
        // to the runtime (Tls13 before .NET 4.8, Tls11/Tls12 on plain 4.0), so a
        // combined assignment would lose TLS 1.2 along with the unsupported bit.
        TryEnable(SecurityProtocolType.Tls);
        TryEnable((SecurityProtocolType)768);
        TryEnable((SecurityProtocolType)3072);
        TryEnable((SecurityProtocolType)12288);
    }

    private static void TryEnable(SecurityProtocolType protocol)
    {
        try
        {
            ServicePointManager.SecurityProtocol |= protocol;
        }
        catch
        {
            // Not supported by this runtime; keep what is already enabled.
        }
    }
}

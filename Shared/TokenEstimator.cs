using System;

/// <summary>
/// Shared token approximation and context-usage helpers (chars / 3).
/// </summary>
public static class TokenEstimator
{
    public const double SummarizeThreshold = 0.90;

    public static int ApproximateTokens(int characterCount)
    {
        if (characterCount <= 0)
            return 0;
        return characterCount / 3;
    }

    public static bool ShouldSummarize(int characterCount, int contextWindowSize)
    {
        if (contextWindowSize <= 0)
            return false;

        int approximateTokens = ApproximateTokens(characterCount);
        double usage = (double)approximateTokens / contextWindowSize;
        return usage >= SummarizeThreshold;
    }

    /// <summary>
    /// NyoCoder-style status text: "Tokens: ~N / max (p.p%)" or "Tokens: ~N".
    /// </summary>
    public static string FormatStatus(int tokens, int? contextWindowSize)
    {
        if (contextWindowSize.HasValue && contextWindowSize.Value > 0)
        {
            double percentage = (double)tokens / contextWindowSize.Value * 100.0;
            return string.Format("Tokens: ~{0:N0} / {1:N0} ({2:F1}%)",
                tokens, contextWindowSize.Value, percentage);
        }

        return string.Format("Tokens: ~{0:N0}", tokens);
    }
}

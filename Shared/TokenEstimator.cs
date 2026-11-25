using System;

/// <summary>
/// Shared token approximation and context-usage helpers (chars / 3).
/// </summary>
public static class TokenEstimator
{
    public const double SummarizeThreshold = 0.90;
    public const int CharsPerToken = 3;

    /// <summary>
    /// Clearing old tool output is enough to skip summarizing once usage is at or
    /// under this share of the context window.
    /// </summary>
    public const double ClearToolOutputTarget = 0.60;

    /// <summary>
    /// Flat cost of one attached image. Providers bill images by resolution, not by
    /// file size: a full-size screenshot is about 1,100-1,600 tokens on the standard
    /// tiers of the major APIs, so the base64 length would overstate it many times over.
    /// </summary>
    public const int ImageTokenEstimate = 1500;

    /// <summary>The image estimate in characters, for adding to a character count.</summary>
    public const int ImageCharEstimate = ImageTokenEstimate * CharsPerToken;

    public static int ApproximateTokens(int characterCount)
    {
        if (characterCount <= 0)
            return 0;
        return characterCount / CharsPerToken;
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
    /// True when usage is at or under the tool-clearing target, so summarizing can be
    /// skipped. Never true with no window size set: usage cannot be judged.
    /// </summary>
    public static bool IsUnderClearTarget(int characterCount, int contextWindowSize)
    {
        if (contextWindowSize <= 0)
            return false;

        return ApproximateTokens(characterCount) <= contextWindowSize * ClearToolOutputTarget;
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

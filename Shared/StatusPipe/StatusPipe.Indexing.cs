using System;
using System.Globalization;

public static partial class StatusPipe
{
    public const string IndexingPrefix = "STATUS indexing=";

    public const string IndexingPhaseStart = "start";
    public const string IndexingPhaseProgress = "progress";
    public const string IndexingPhaseDone = "done";
    public const string IndexingPhaseCleared = "cleared";
    public const string IndexingPhaseError = "error";

    public static string FormatIndexingStart(int total)
    {
        return IndexingPrefix + IndexingPhaseStart + " total=" + total.ToString(CultureInfo.InvariantCulture);
    }

    public static string FormatIndexingProgress(int current, int total, string fileBasename)
    {
        string file = SanitizeToken(fileBasename);
        return IndexingPrefix + IndexingPhaseProgress + " current=" + current.ToString(CultureInfo.InvariantCulture)
            + " total=" + total.ToString(CultureInfo.InvariantCulture)
            + " file=" + file;
    }

    public static string FormatIndexingDone(int fileCount)
    {
        return IndexingPrefix + IndexingPhaseDone + " files=" + fileCount.ToString(CultureInfo.InvariantCulture);
    }

    public static string FormatIndexingCleared()
    {
        return IndexingPrefix + IndexingPhaseCleared;
    }

    public static string FormatIndexingError(string message)
    {
        return IndexingPrefix + IndexingPhaseError + " message=" + SanitizeToken(message);
    }

    public static bool TryParseIndexingLine(string line, out IndexingStatusEvent status)
    {
        status = null;
        string rest;
        if (!TryStripPrefix(line, IndexingPrefix, out rest))
            return false;

        int space = rest.IndexOf(' ');
        string phase = space >= 0 ? rest.Substring(0, space) : rest;
        string args = space >= 0 ? rest.Substring(space + 1).Trim() : string.Empty;

        status = new IndexingStatusEvent();
        status.Phase = phase;
        status.RawLine = line.Trim();

        if (string.Equals(phase, IndexingPhaseStart, StringComparison.OrdinalIgnoreCase))
        {
            status.Total = ParseIntArg(args, "total");
            return true;
        }
        if (string.Equals(phase, IndexingPhaseProgress, StringComparison.OrdinalIgnoreCase))
        {
            status.Current = ParseIntArg(args, "current");
            status.Total = ParseIntArg(args, "total");
            status.File = ParseStringArg(args, "file");
            return true;
        }
        if (string.Equals(phase, IndexingPhaseDone, StringComparison.OrdinalIgnoreCase))
        {
            status.FileCount = ParseIntArg(args, "files");
            return true;
        }
        if (string.Equals(phase, IndexingPhaseCleared, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (string.Equals(phase, IndexingPhaseError, StringComparison.OrdinalIgnoreCase))
        {
            status.Message = ParseStringArg(args, "message");
            return true;
        }

        status = null;
        return false;
    }
}

/// <summary>Parsed indexing progress event from the status pipe.</summary>
public sealed class IndexingStatusEvent
{
    public string Phase;
    public int Current;
    public int Total;
    public int FileCount;
    public string File;
    public string Message;
    public string RawLine;
}

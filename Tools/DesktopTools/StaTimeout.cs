using System;
using System.Threading;

namespace DesktopTools
{
  /// <summary>
  /// Run a short action on a background thread with a wall-clock join limit.
  /// Used so UIA/MSAA calls that open modal dialogs cannot hang the tool process.
  /// </summary>
  internal static class StaTimeout
  {
    public enum Result
    {
      Failed,
      Succeeded,
      TimedOut
    }

    public static Result Run(Action action, int timeoutMs, bool preferSta)
    {
      if (action == null)
        return Result.Failed;

      if (timeoutMs < 1)
        timeoutMs = 1;

      Exception error = null;
      var thread = new Thread(() =>
      {
        try
        {
          action();
        }
        catch (Exception ex)
        {
          error = ex;
        }
      });
      thread.IsBackground = true;

      if (preferSta)
      {
        try
        {
          thread.SetApartmentState(ApartmentState.STA);
        }
        catch
        {
        }
      }

      thread.Start();
      if (!thread.Join(timeoutMs))
        return Result.TimedOut;

      return error != null ? Result.Failed : Result.Succeeded;
    }
  }
}

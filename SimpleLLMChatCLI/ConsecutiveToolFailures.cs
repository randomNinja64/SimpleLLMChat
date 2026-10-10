namespace SimpleLLMChatCLI
{
    /// <summary>
    /// Counts tool calls that ran and returned a non-zero exit code.
    /// A success resets the streak. A call that did not run leaves it unchanged.
    /// A limit of 0 disables the stop.
    /// </summary>
    public static class ConsecutiveToolFailures
    {
        public static void Record(ref int streak, int limit, bool ran, int exitCode)
        {
            if (limit <= 0 || !ran)
                return;

            if (exitCode == 0)
                streak = 0;
            else
                streak++;
        }
    }
}

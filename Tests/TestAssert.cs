using System;

public class TestFailureException : Exception
{
    public TestFailureException(string message) : base(message) { }
}

public class TestSkipException : Exception
{
    public TestSkipException(string message) : base(message) { }
}

public static class TestAssert
{
    public static void True(bool condition, string message)
    {
        if (!condition)
            throw new TestFailureException(message ?? "Expected true.");
    }

    public static void Equal(string expected, string actual, string message)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new TestFailureException(
                (message ?? "equal") + "\n  expected: " + (expected ?? "(null)") +
                "\n  actual:   " + (actual ?? "(null)"));
        }
    }

    public static void Equal(int expected, int actual, string message)
    {
        if (expected != actual)
        {
            throw new TestFailureException(
                (message ?? "equal") + " expected=" + expected + " actual=" + actual);
        }
    }

    public static void Contains(string haystack, string needle, string message)
    {
        if (haystack == null || needle == null || haystack.IndexOf(needle, StringComparison.Ordinal) < 0)
        {
            throw new TestFailureException(
                (message ?? "contains") + "\n  needle: " + (needle ?? "(null)") +
                "\n  haystack: " + Truncate(haystack, 500));
        }
    }

    public static void ContainsIgnoreCase(string haystack, string needle, string message)
    {
        if (haystack == null || needle == null ||
            haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
        {
            throw new TestFailureException(
                (message ?? "contains") + "\n  needle: " + (needle ?? "(null)") +
                "\n  haystack: " + Truncate(haystack, 500));
        }
    }

    private static string Truncate(string s, int max)
    {
        if (s == null) return "(null)";
        if (s.Length <= max) return s;
        return s.Substring(0, max) + "...";
    }
}

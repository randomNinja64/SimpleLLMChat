namespace WebTools
{
    internal static class CurlHelper
    {
        /// <summary>
        /// Runs curl with -s -L, the configured user-agent, and the given URL.
        /// Extra flags (e.g. "-I", "-o file") and extra headers are optional.
        /// </summary>
        public static string Execute(string url, out int exitCode,
            string extraFlags = "", bool combineErrorOutput = true, params string[] extraHeaders)
        {
            string ua = "-H \"User-Agent: " + ToolHelper.GetConfigValue("useragent") + "\"";
            string hdrs = string.Concat(System.Array.ConvertAll(extraHeaders, h => " -H \"" + h + "\""));
            string flags = string.IsNullOrEmpty(extraFlags) ? "" : extraFlags + " ";
            string arguments = "-s -L " + flags + ua + hdrs + " \"" + url + "\"";
            return ToolHelper.ExecuteProcess("curl.exe", arguments, out exitCode, combineErrorOutput);
        }
    }
}

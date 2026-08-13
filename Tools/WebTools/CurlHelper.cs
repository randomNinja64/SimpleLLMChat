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

        /// <summary>
        /// POSTs a JSON body via curl (-d inline). Optional extra headers (e.g. Authorization).
        /// </summary>
        public static string PostJson(string url, string jsonBody, out int exitCode,
            bool combineErrorOutput = true, params string[] extraHeaders)
        {
            string hdrs = " -H \"Content-Type: application/json\""
                + string.Concat(System.Array.ConvertAll(extraHeaders, h => " -H \"" + h + "\""));
            string escapedJson = (jsonBody ?? "").Replace("\"", "\\\"");
            string arguments = "-s -L -X POST" + hdrs + " -d \"" + escapedJson + "\" \"" + url + "\"";
            return ToolHelper.ExecuteProcess("curl.exe", arguments, out exitCode, combineErrorOutput);
        }

        public static string[] FirecrawlAuthHeaders(string apiKey)
        {
            return string.IsNullOrWhiteSpace(apiKey)
                ? new string[0]
                : new[] { "Authorization: Bearer " + apiKey.Trim() };
        }
    }
}

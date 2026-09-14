using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace WebTools.Tests
{
    internal static class Program
    {
        private const string Exe = "WebTools.exe";

        static int Main(string[] args)
        {
            return SuiteMain.Run("WebTools", args, Run);
        }

        static void Run()
        {
            TestRunner.Run("unknown_tool", () => ToolClient.AssertUnknownTool(Exe));

            TestRunner.Run("read_website.local", () =>
            {
                if (!ToolClient.ProductExists("curl.exe"))
                    TestRunner.Skip("curl.exe not packaged beside test EXE");

                using (LocalHttpServer server = new LocalHttpServer(
                    "<html><head><title>T</title></head><body><p>HelloLocal</p><a href=\"http://example.com/a\">A</a></body></html>"))
                {
                    ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "read_website",
                        new JObject { ["URL"] = server.Url },
                        null,
                        60000);
                    TestAssert.Equal(0, r.ExitCode, "exit");
                    TestAssert.Contains(r.Text, "HelloLocal", "body");
                }
            });

            TestRunner.Run("download_file.local", () =>
            {
                if (!ToolClient.ProductExists("curl.exe"))
                    TestRunner.Skip("curl.exe not packaged beside test EXE");

                using (TempWorkspace ws = new TempWorkspace("webdl"))
                using (LocalHttpServer server = new LocalHttpServer("download-body"))
                {
                    string dest = ws.Combine("out.bin");
                    ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "download_file",
                        new JObject { ["URL"] = server.Url, ["filename"] = dest },
                        null,
                        60000);
                    TestAssert.Equal(0, r.ExitCode, "exit");
                    TestAssert.True(File.Exists(dest), "file exists");
                    TestAssert.Contains(File.ReadAllText(dest), "download-body", "content");
                }
            });

            TestRunner.Run("run_web_search.live", () =>
            {
                ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "run_web_search",
                    new JObject { ["query"] = "OpenAI" },
                    null,
                    120000);
                TestAssert.Equal(0, r.ExitCode, "exit");
                TestAssert.True(!string.IsNullOrWhiteSpace(r.Text), "non-empty results");
            });

            TestRunner.Run("download_video.live", () =>
            {
                if (!ToolClient.ProductExists("yt-dlp.exe"))
                    TestRunner.Skip("yt-dlp.exe not packaged beside test EXE");

                // Use a tiny well-known short clip; fail if offline/remote error.
                ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "download_video",
                    new JObject { ["URL"] = "https://www.youtube.com/watch?v=jNQXAC9IVRw" },
                    null,
                    300000);
                TestAssert.Equal(0, r.ExitCode, "exit");
            });
        }

        private sealed class LocalHttpServer : IDisposable
        {
            private readonly HttpListener _listener;
            private readonly Thread _thread;
            private volatile bool _running;
            private readonly string _body;

            public string Url { get; private set; }

            public LocalHttpServer(string body)
            {
                _body = body ?? "";
                string baseUrl;
                _listener = TestHttpListener.StartLoopback(out baseUrl);
                Url = baseUrl + "/";
                _running = true;
                _thread = new Thread(Loop) { IsBackground = true };
                _thread.Start();
            }

            private void Loop()
            {
                while (_running)
                {
                    try
                    {
                        HttpListenerContext ctx = _listener.GetContext();
                        byte[] bytes = Encoding.UTF8.GetBytes(_body);
                        ctx.Response.StatusCode = 200;
                        ctx.Response.ContentType = "text/html; charset=utf-8";
                        ctx.Response.ContentLength64 = bytes.Length;
                        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                        ctx.Response.Close();
                    }
                    catch
                    {
                        if (!_running) break;
                    }
                }
            }

            public void Dispose()
            {
                _running = false;
                try { _listener.Stop(); } catch { }
                try { _listener.Close(); } catch { }
            }
        }
    }
}

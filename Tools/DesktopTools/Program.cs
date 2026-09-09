using System;

namespace DesktopTools
{
  internal class Program
  {
    static int Main(string[] args)
    {
      Win32Interop.EnsureDpiAwareness();

      return ToolHelper.RunToolMain(args, (toolName, argumentsJson) =>
      {
        switch (toolName)
        {
          case "list_windows":
            {
              WindowTarget target = WindowEnumerator.ParseWindowTarget(argumentsJson);
              string output = WindowEnumerator.ListWindows(target.WindowTitle);
              return new ToolResult(output, 0);
            }

          case "get_desktop_context":
            return new ToolResult(WindowEnumerator.GetDesktopContext(), 0);

          case "list_controls":
            {
              WindowTarget target = WindowEnumerator.ParseWindowTarget(argumentsJson);
              IntPtr hwnd = WindowEnumerator.ResolveWindow(target.HwndText, target.WindowTitle);
              TreeScope scope = ControlTree.ParseScope(argumentsJson);
              string output = ControlTree.Render(hwnd, scope.MaxDepth, scope.MaxControls);
              return new ToolResult(output, 0);
            }

          case "navigate":
            {
              string action = ToolHelper.GetRequiredArg(argumentsJson, "action");
              string output = ControlActions.Navigate(action, argumentsJson);
              int exitCode = output.StartsWith("error:", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
              return new ToolResult(output, exitCode);
            }

          case "set_control_text":
            {
              string output = ControlActions.SetText(argumentsJson);
              int exitCode = output.StartsWith("error:", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
              return new ToolResult(output, exitCode);
            }

          case "send_keys":
            {
              string keys = ToolHelper.GetRequiredArg(argumentsJson, "keys");
              string output = SendKeysHelper.Send(keys);
              int exitCode = output.StartsWith("error:", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
              return new ToolResult(output, exitCode);
            }

          case "screenshot":
            {
              string imageBase64;
              string imageMime;
              string output = ScreenshotCapture.Capture(argumentsJson, out imageBase64, out imageMime);
              int exitCode = output.StartsWith("error:", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
              return new ToolResult(output, exitCode, imageBase64, imageMime);
            }

          default:
            return new ToolResult("error: unknown tool '" + toolName + "'.", 1);
        }
      });
    }
  }
}

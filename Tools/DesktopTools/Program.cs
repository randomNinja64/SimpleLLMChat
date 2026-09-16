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
              return new ToolResult(output, ExitFor(output));
            }

          case "set_control_text":
            {
              string output = ControlActions.SetText(argumentsJson);
              return new ToolResult(output, ExitFor(output));
            }

          case "send_keys":
            {
              string keys = ToolHelper.GetRequiredArg(argumentsJson, "keys");
              string output = SendKeysHelper.Send(keys);
              return new ToolResult(output, ExitFor(output));
            }

          case "screenshot":
            {
              string imageBase64;
              string imageMime;
              string output = ScreenshotCapture.Capture(argumentsJson, out imageBase64, out imageMime);
              return new ToolResult(output, ExitFor(output), imageBase64, imageMime);
            }

          default:
            return ToolHelper.Fail("unknown tool '" + toolName + "'.");
        }
      });
    }

    static int ExitFor(string output)
    {
      return !string.IsNullOrEmpty(output) &&
          output.StartsWith("error:", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    }
  }
}

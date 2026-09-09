using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace DesktopTools
{
  /// <summary>
  /// Captures a window or the virtual desktop as JPEG.
  /// Caption includes screen-space origin/size of the capture for grounding.
  /// Click x/y with the same hwnd/title (or none for desktop) are image-relative.
  /// </summary>
  internal static class ScreenshotCapture
  {
    private const long JpegQuality = 75L;

    public static string Capture(string argumentsJson, out string imageBase64, out string imageMime)
    {
      imageBase64 = null;
      imageMime = null;

      WindowTarget target = WindowEnumerator.ParseWindowTarget(argumentsJson);

      int left, top, width, height;
      string caption;

      if (!target.IsEmpty)
      {
        IntPtr hwnd;
        try
        {
          hwnd = WindowEnumerator.ResolveWindow(target.HwndText, target.WindowTitle);
        }
        catch (Exception ex)
        {
          return "error: " + ex.Message;
        }

        if (!Win32Interop.TryGetWindowOrigin(hwnd, out left, out top, out width, out height))
          return "error: GetWindowRect failed for hwnd=" + Win32Interop.FormatHwnd(hwnd) + ".";

        caption = OutputFormat.FormatCaptureCaption(hwnd, Win32Interop.GetWindowTitle(hwnd), left, top, width, height);
      }
      else
      {
        Win32Interop.GetVirtualDesktopOrigin(out left, out top, out width, out height);

        if (width <= 0 || height <= 0)
          return "error: virtual screen metrics unavailable.";

        caption = "desktop @" + left + "," + top + " " + width + "x" + height;
      }

      try
      {
        using (var bitmap = new Bitmap(width, height))
        {
          using (Graphics graphics = Graphics.FromImage(bitmap))
          {
            graphics.CopyFromScreen(left, top, 0, 0, bitmap.Size);
          }

          imageBase64 = EncodeJpegBase64(bitmap);
          imageMime = "image/jpeg";
          return caption;
        }
      }
      catch (Exception ex)
      {
        return "error: screenshot failed: " + ex.Message;
      }
    }

    private static string EncodeJpegBase64(Bitmap bitmap)
    {
      ImageCodecInfo jpegCodec = null;
      foreach (ImageCodecInfo codec in ImageCodecInfo.GetImageEncoders())
      {
        if (codec.FormatID == ImageFormat.Jpeg.Guid)
        {
          jpegCodec = codec;
          break;
        }
      }

      using (var stream = new MemoryStream())
      {
        if (jpegCodec != null)
        {
          using (var encoderParams = new EncoderParameters(1))
          {
            encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, JpegQuality);
            bitmap.Save(stream, jpegCodec, encoderParams);
          }
        }
        else
        {
          bitmap.Save(stream, ImageFormat.Jpeg);
        }

        return Convert.ToBase64String(stream.ToArray());
      }
    }
  }
}

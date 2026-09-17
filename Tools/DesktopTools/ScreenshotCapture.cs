using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace DesktopTools
{
  /// <summary>
  /// Captures a window or the virtual desktop as JPEG.
  /// Caption reports capture size in image space (WxH).
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
      IntPtr hwnd = IntPtr.Zero;

      if (!target.IsEmpty)
      {
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

        caption = OutputFormat.FormatCaptureCaption(hwnd, Win32Interop.GetWindowTitle(hwnd), width, height);
      }
      else
      {
        Win32Interop.GetVirtualDesktopOrigin(out left, out top, out width, out height);

        if (width <= 0 || height <= 0)
          return "error: virtual screen metrics unavailable.";

        caption = "desktop " + width + "x" + height;
      }

      try
      {
        using (Bitmap bitmap = hwnd != IntPtr.Zero
          ? CaptureWindow(hwnd, left, top, width, height)
          : CaptureScreenRegion(left, top, width, height))
        {
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

    /// <summary>
    /// Prefer PrintWindow (XP+) so obscured windows still capture; fall back to a
    /// screen BitBlt. Avoid Graphics.CopyFromScreen into a 32bpp ARGB bitmap — that
    /// path commonly throws GDI+ errors on XP.
    /// </summary>
    private static Bitmap CaptureWindow(IntPtr hwnd, int left, int top, int width, int height)
    {
      string error;
      Bitmap printed = CaptureSurface(width, height,
        (screen, surface) => Win32Interop.PrintWindow(hwnd, surface, 0), out error);
      if (printed != null)
        return printed;

      if (Win32Interop.IsIconic(hwnd))
        throw new InvalidOperationException("window is minimized; restore it before screenshot, or PrintWindow failed.");

      return CaptureScreenRegion(left, top, width, height);
    }

    private static Bitmap CaptureScreenRegion(int left, int top, int width, int height)
    {
      string error;
      Bitmap bitmap = CaptureSurface(width, height, (screen, surface) =>
      {
        if (!Win32Interop.BitBlt(surface, 0, 0, width, height, screen, left, top, Win32Interop.SrcCopy))
          throw new InvalidOperationException("BitBlt failed (Win32=" + Marshal.GetLastWin32Error() + ").");
        return true;
      }, out error);
      if (bitmap == null)
        throw new InvalidOperationException(error);
      return bitmap;
    }

    private static Bitmap CaptureSurface(int width, int height,
      Func<IntPtr, IntPtr, bool> draw, out string error)
    {
      error = "GetDC(NULL) failed.";
      IntPtr hdcScreen = Win32Interop.GetDC(IntPtr.Zero);
      if (hdcScreen == IntPtr.Zero)
        return null;

      IntPtr hdcMem = IntPtr.Zero;
      IntPtr hBitmap = IntPtr.Zero;
      IntPtr hOld = IntPtr.Zero;

      try
      {
        error = "CreateCompatibleDC failed.";
        hdcMem = Win32Interop.CreateCompatibleDC(hdcScreen);
        if (hdcMem == IntPtr.Zero)
          return null;

        error = "CreateCompatibleBitmap failed.";
        hBitmap = Win32Interop.CreateCompatibleBitmap(hdcScreen, width, height);
        if (hBitmap == IntPtr.Zero)
          return null;

        error = null;
        hOld = Win32Interop.SelectObject(hdcMem, hBitmap);
        if (!draw(hdcScreen, hdcMem))
          return null;

        return BitmapFromHbitmap(hBitmap, width, height);
      }
      finally
      {
        if (hOld != IntPtr.Zero)
          Win32Interop.SelectObject(hdcMem, hOld);
        if (hBitmap != IntPtr.Zero)
          Win32Interop.DeleteObject(hBitmap);
        if (hdcMem != IntPtr.Zero)
          Win32Interop.DeleteDC(hdcMem);
        Win32Interop.ReleaseDC(IntPtr.Zero, hdcScreen);
      }
    }

    /// <summary>
    /// Clone into a 24bpp RGB bitmap so JPEG encode is reliable across color depths
    /// (including 16-bit XP desktops) and we do not keep the GDI HBITMAP alive.
    /// </summary>
    private static Bitmap BitmapFromHbitmap(IntPtr hBitmap, int width, int height)
    {
      using (Bitmap source = Image.FromHbitmap(hBitmap))
      {
        var clone = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (Graphics graphics = Graphics.FromImage(clone))
        {
          graphics.DrawImage(source, 0, 0, width, height);
        }
        return clone;
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

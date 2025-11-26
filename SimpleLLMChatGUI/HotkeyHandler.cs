using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace SimpleLLMChatGUI
{
    public class HotkeyHandler : IDisposable
    {
        private readonly int hotkeyId;
        private readonly int modifiers;
        private readonly int virtualKey;
        private HwndSource hwndSource;
        private bool isRegistered;
        public bool IsEnabled { get; private set; }

        public event Action<string> ScreenshotTaken;
        public event Action<string> ErrorOccurred;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        public HotkeyHandler(HwndSource hwndSource, int hotkeyId, int modifiers, int virtualKey)
        {
            this.hwndSource = hwndSource;
            this.hotkeyId = hotkeyId;
            this.modifiers = modifiers;
            this.virtualKey = virtualKey;
            AttachHook();
        }

        public void Enable()
        {
            IsEnabled = true;
            Register();
        }

        public void Disable()
        {
            IsEnabled = false;
            Unregister();
        }

        private void AttachHook()
        {
            if (hwndSource != null)
            {
                hwndSource.AddHook(WndProc);
            }
        }

        private void Register()
        {
            if (hwndSource != null && hwndSource.Handle != IntPtr.Zero && !isRegistered)
            {
                RegisterHotKey(hwndSource.Handle, hotkeyId, modifiers, virtualKey);
                isRegistered = true;
            }
        }

        private void Unregister()
        {
            if (hwndSource != null && hwndSource.Handle != IntPtr.Zero && isRegistered)
            {
                UnregisterHotKey(hwndSource.Handle, hotkeyId);
                isRegistered = false;
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY && wParam.ToInt32() == hotkeyId && IsEnabled)
            {
                handled = true;
                TryTakeScreenshot();
            }
            return IntPtr.Zero;
        }

        private void TryTakeScreenshot()
        {
            try
            {
                string screenshotPath = Path.Combine(Path.GetTempPath(), "currentscreen.jpg");

                // Try to get the active window first
                IntPtr foregroundWindow = GetForegroundWindow();

                if (foregroundWindow != IntPtr.Zero && IsWindow(foregroundWindow))
                {
                    // Get the window rectangle
                    RECT windowRect;
                    if (GetWindowRect(foregroundWindow, out windowRect))
                    {
                        // Calculate window dimensions
                        int windowWidth = windowRect.Right - windowRect.Left;
                        int windowHeight = windowRect.Bottom - windowRect.Top;

                        // Only capture if the window has valid dimensions
                        if (windowWidth > 0 && windowHeight > 0)
                        {
                            using (Bitmap bitmap = new Bitmap(windowWidth, windowHeight))
                            {
                                using (Graphics graphics = Graphics.FromImage(bitmap))
                                {
                                    graphics.CopyFromScreen(windowRect.Left, windowRect.Top, 0, 0, bitmap.Size);
                                }
                                bitmap.Save(screenshotPath, ImageFormat.Jpeg);
                            }

                            ScreenshotTaken?.Invoke(screenshotPath);
                            return;
                        }
                    }
                }

                // Fallback to full screen capture if no valid active window
                int screenLeft = (int)SystemParameters.VirtualScreenLeft;
                int screenTop = (int)SystemParameters.VirtualScreenTop;
                int screenWidth = (int)SystemParameters.VirtualScreenWidth;
                int screenHeight = (int)SystemParameters.VirtualScreenHeight;

                using (Bitmap bitmap = new Bitmap(screenWidth, screenHeight))
                {
                    using (Graphics graphics = Graphics.FromImage(bitmap))
                    {
                        graphics.CopyFromScreen(screenLeft, screenTop, 0, 0, bitmap.Size);
                    }
                    bitmap.Save(screenshotPath, ImageFormat.Jpeg);
                }

                ScreenshotTaken?.Invoke(screenshotPath);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke("Failed to take screenshot: " + ex.Message);
            }
        }

        public void Dispose()
        {
            Unregister();
            if (hwndSource != null)
            {
                hwndSource.RemoveHook(WndProc);
                hwndSource = null;
            }
        }
    }
}



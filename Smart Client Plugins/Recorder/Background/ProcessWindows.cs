using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace Recorder.Background
{
    public struct WindowInfo
    {
        public IntPtr Handle;
        public string Title;
        public int Width;
        public int Height;
        public bool IsVisible;
        public bool IsMinimized;

        public bool HasArea => Width > 0 && Height > 0;

        public override string ToString()
            => $"[{Handle}] \"{Title}\" {Width}x{Height} visible={IsVisible} minimized={IsMinimized}";
    }

    public static class ProcessWindows
    {
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        /// <summary>
        /// Gets all WPF windows via Application.Current.Windows on the UI thread.
        /// Safe to call from a background thread — marshals to the Dispatcher automatically.
        /// </summary>
        public static List<WindowInfo> GetAllWindowsForCurrentProcess()
        {
            var app = Application.Current;
            if (app == null)
                return new List<WindowInfo>();

            // Marshal to the UI thread to read Application.Windows
            return (List<WindowInfo>)app.Dispatcher.Invoke(new Func<List<WindowInfo>>(() =>
            {
                var result = new List<WindowInfo>();
                var sb = new StringBuilder(256);

                foreach (Window win in app.Windows)
                {
                    var helper = new WindowInteropHelper(win);
                    var hwnd = helper.Handle;
                    if (hwnd == IntPtr.Zero) continue;

                    var info = BuildWindowInfo(hwnd, sb);
                    if (string.IsNullOrWhiteSpace(info.Title)) continue;

                    result.Add(info);
                }

                return result;
            }));
        }

        private static WindowInfo BuildWindowInfo(IntPtr hWnd, StringBuilder sb)
        {
            sb.Clear();
            GetWindowText(hWnd, sb, sb.Capacity);
            GetWindowRect(hWnd, out RECT rect);

            return new WindowInfo
            {
                Handle = hWnd,
                Title = sb.ToString(),
                Width = rect.Right - rect.Left,
                Height = rect.Bottom - rect.Top,
                IsVisible = IsWindowVisible(hWnd),
                IsMinimized = IsIconic(hWnd),
            };
        }
    }

    public class Capture
    {
        [DllImport("user32.dll")]
        static extern IntPtr GetWindowDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [DllImport("gdi32.dll")]
        static extern bool BitBlt(
            IntPtr hdcDest,
            int xDest,
            int yDest,
            int width,
            int height,
            IntPtr hdcSrc,
            int xSrc,
            int ySrc,
            int rop);

        [DllImport("gdi32.dll")]
        static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

        [DllImport("gdi32.dll")]
        static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool DeleteDC(IntPtr hdc);

        [DllImport("user32.dll")]
        static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        const int SRCCOPY = 0x00CC0020;

        struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        public static Bitmap CaptureWindow(IntPtr hwnd)
        {
            GetWindowRect(hwnd, out RECT rect);

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;

            if (width <= 0 || height <= 0)
                throw new InvalidOperationException($"Window has no area ({width}x{height}). It may be minimized.");

            IntPtr hdcSrc = GetWindowDC(hwnd);
            try
            {
                IntPtr hdcDest = CreateCompatibleDC(hdcSrc);
                IntPtr hBitmap = CreateCompatibleBitmap(hdcSrc, width, height);
                IntPtr hOld = SelectObject(hdcDest, hBitmap);

                BitBlt(hdcDest, 0, 0, width, height, hdcSrc, 0, 0, SRCCOPY);

                Bitmap bmp = Image.FromHbitmap(hBitmap);

                SelectObject(hdcDest, hOld);
                DeleteObject(hBitmap);
                DeleteDC(hdcDest);

                return bmp;
            }
            finally
            {
                ReleaseDC(hwnd, hdcSrc);
            }
        }
    }
}

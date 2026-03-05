using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Recorder.Background
{
    public static class ProcessWindows
    {
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        private const uint GW_OWNER = 4;

        public static IReadOnlyList<IntPtr> GetTopLevelWindowsForCurrentProcess(bool visibleOnly = true, bool ownedOnly = false)
        {
            int myPid = Process.GetCurrentProcess().Id;
            var result = new List<IntPtr>();

            EnumWindows((hWnd, lParam) =>
            {
                GetWindowThreadProcessId(hWnd, out uint pid);
                if (pid != (uint)myPid) return true;

                if (visibleOnly && !IsWindowVisible(hWnd)) return true;

                if (!ownedOnly)
                {
                    // common: only include unowned top-level windows
                    if (GetWindow(hWnd, GW_OWNER) != IntPtr.Zero) return true;
                }

                result.Add(hWnd);
                return true;
            }, IntPtr.Zero);

            return result;
        }

        public static IntPtr GetMainTopLevelWindow()
        {
            // best-effort: first visible unowned top-level window
            var windows = GetTopLevelWindowsForCurrentProcess(visibleOnly: true, ownedOnly: false);
            return windows.Count > 0 ? windows[0] : IntPtr.Zero;
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

            IntPtr hdcSrc = GetWindowDC(hwnd);
            IntPtr hdcDest = CreateCompatibleDC(hdcSrc);

            IntPtr hBitmap = CreateCompatibleBitmap(hdcSrc, width, height);
            SelectObject(hdcDest, hBitmap);

            BitBlt(hdcDest, 0, 0, width, height, hdcSrc, 0, 0, SRCCOPY);

            Bitmap bmp = Image.FromHbitmap(hBitmap);

            ReleaseDC(hwnd, hdcSrc);

            return bmp;
        }
    }
}

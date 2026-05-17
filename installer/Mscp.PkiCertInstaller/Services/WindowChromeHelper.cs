using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace Mscp.PkiCertInstaller.Services;

// Forces the Win32 title bar to repaint dark to match our theme. The
// DWM attribute alone is racy: depending on whether Avalonia's window
// is already mapped, the caption may keep its initial light paint
// until something else (move/resize) invalidates the non-client area.
// We layer four pokes:
//   1. DwmSetWindowAttribute(20)  -- official Win10 2004+/Win11
//   2. DwmSetWindowAttribute(19)  -- legacy Win10 1809..1909
//   3. SetWindowPos(SWP_FRAMECHANGED)  -- recompute the NC area
//   4. RedrawWindow(RDW_FRAME)    -- force an immediate NC paint
// Then the caller hooks Initialized + Opened so the call lands both
// before the first paint *and* right after, covering whichever
// ordering the platform actually does first.
public static class WindowChromeHelper
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, uint attr, ref int pvAttribute, uint cbAttribute);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(
        IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const uint WM_NCACTIVATE = 0x0086;

    private const uint DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
    private const uint DWMWA_USE_IMMERSIVE_DARK_MODE     = 20;

    private const uint SWP_NOMOVE       = 0x0002;
    private const uint SWP_NOSIZE       = 0x0001;
    private const uint SWP_NOZORDER     = 0x0004;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_NOACTIVATE   = 0x0010;

    private const uint RDW_INVALIDATE = 0x0001;
    private const uint RDW_UPDATENOW  = 0x0100;
    private const uint RDW_FRAME      = 0x0400;
    private const uint RDW_ALLCHILDREN = 0x0080;

    public static void ApplyDarkTitleBar(Window w)
    {
        try
        {
            var hwnd = w.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (hwnd == IntPtr.Zero) return;
            int on = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE,     ref on, sizeof(int));
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref on, sizeof(int));
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            RedrawWindow(hwnd, IntPtr.Zero, IntPtr.Zero,
                RDW_FRAME | RDW_INVALIDATE | RDW_UPDATENOW | RDW_ALLCHILDREN);
            // The DWM caption only repaints when the window receives an
            // activation message. Synthesize one (deactivate then
            // re-activate) - this is the same effect a user move/click
            // produces, but invisible to them.
            SendMessage(hwnd, WM_NCACTIVATE, IntPtr.Zero, IntPtr.Zero);
            SendMessage(hwnd, WM_NCACTIVATE, (IntPtr)1,   IntPtr.Zero);
        }
        catch
        {
            // Not Windows / DWM unavailable - silently fall back.
        }
    }

    // Wires multiple lifecycle events so the dark caption sticks
    // regardless of which order Avalonia fires Initialized vs Opened
    // on a given OS / GPU stack. Idempotent - DwmSetWindowAttribute is
    // cheap to call repeatedly.
    public static void HookDarkTitleBar(Window w)
    {
        w.Initialized += (_, _) => ApplyDarkTitleBar(w);
        w.Opened      += (_, _) => ApplyDarkTitleBar(w);
        w.Loaded      += (_, _) => ApplyDarkTitleBar(w);
        w.PropertyChanged += (_, e) =>
        {
            if (e.Property == Window.WindowStateProperty)
                ApplyDarkTitleBar(w);
        };
    }
}

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SteamBridge.Desktop;

internal static class NativeWindow
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void ApplyDarkFrame(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        int enabled = 1;
        // DWM returns an error on older Windows versions; the normal native frame remains usable.
        _ = DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
        int caption = 0x00100A09;
        _ = DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
    }
}

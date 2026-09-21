using System.Runtime.InteropServices;

namespace TrayGames.Launcher;

/// Small Win32 helpers. Without the first, a dark window keeps a light title
/// bar and reads as two applications stitched together.
internal static partial class DarkTitleBar
{
    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    const int UseImmersiveDarkMode = 20;

    public static void Apply(IntPtr handle)
    {
        int on = 1;
        DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref on, sizeof(int));
    }
}

internal static partial class MouseInput
{
    [LibraryImport("user32.dll")]
    private static partial void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);

    const uint LeftDown = 0x0002, LeftUp = 0x0004;

    public static void Click()
    {
        mouse_event(LeftDown, 0, 0, 0, UIntPtr.Zero);
        mouse_event(LeftUp, 0, 0, 0, UIntPtr.Zero);
    }
}

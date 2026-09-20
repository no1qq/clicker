using System;
using System.Text;

namespace zolsi.cc;

internal static class MinecraftHelper
{
    private static readonly StringBuilder TitleBuffer = new(512);

    public static bool IsMinecraftForeground()
    {
        IntPtr fg = NativeMethods.GetForegroundWindow();
        if (fg == IntPtr.Zero)
        {
            return false;
        }

        TitleBuffer.Clear();
        int len = NativeMethods.GetWindowText(fg, TitleBuffer, TitleBuffer.Capacity);
        if (len <= 0)
        {
            return false;
        }

        string title = TitleBuffer.ToString();
        return title.Contains("minecraft", StringComparison.OrdinalIgnoreCase);
    }
}
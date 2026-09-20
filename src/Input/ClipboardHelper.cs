using System;
using System.Runtime.InteropServices;

namespace zolsi.cc;

internal static class ClipboardHelper
{
    public static string? GetText()
    {
        if (!NativeMethods.OpenClipboard(IntPtr.Zero))
        {
            return null;
        }

        try
        {
            IntPtr handle = NativeMethods.GetClipboardData(NativeMethods.CF_UNICODETEXT);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            IntPtr ptr = NativeMethods.GlobalLock(handle);
            if (ptr == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringUni(ptr);
            }
            finally
            {
                NativeMethods.GlobalUnlock(handle);
            }
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }
}
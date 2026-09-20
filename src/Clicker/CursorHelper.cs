using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace zolsi.cc;

internal static class CursorHelper
{
    private static readonly ConcurrentDictionary<IntPtr, bool> TransparencyCache = new();

    public static bool IsCursorVisible()
    {
        var ci = new NativeMethods.CURSORINFO
        {
            cbSize = Marshal.SizeOf<NativeMethods.CURSORINFO>()
        };

        if (!NativeMethods.GetCursorInfo(ref ci) || ci.hCursor == IntPtr.Zero)
        {
            return false;
        }

        if ((ci.flags & NativeMethods.CURSOR_SHOWING) == 0)
        {
            return false;
        }

        if (TransparencyCache.TryGetValue(ci.hCursor, out bool cached))
        {
            return !cached;
        }

        bool isTransparent = CheckIfCursorIsTransparent(ci.hCursor);
        TransparencyCache[ci.hCursor] = isTransparent;
        return !isTransparent;
    }

    private static bool CheckIfCursorIsTransparent(IntPtr hCursor)
    {
        if (!NativeMethods.GetIconInfo(hCursor, out var iconInfo))
        {
            return false;
        }

        bool isTransparent = false;
        try
        {
            if (iconInfo.hbmColor != IntPtr.Zero)
            {
                if (NativeMethods.GetObject(iconInfo.hbmColor, Marshal.SizeOf<NativeMethods.BITMAP>(), out var bmColor) != 0)
                {
                    int byteCount = bmColor.bmHeight * bmColor.bmWidthBytes;
                    if (byteCount > 0 && byteCount <= 65536)
                    {
                        byte[] bits = new byte[byteCount];
                        if (NativeMethods.GetBitmapBits(iconInfo.hbmColor, byteCount, bits) > 0)
                        {
                            if (bmColor.bmBitsPixel == 32)
                            {
                                bool anyAlpha = false;
                                for (int i = 3; i < bits.Length; i += 4)
                                {
                                    if (bits[i] != 0)
                                    {
                                        anyAlpha = true;
                                        break;
                                    }
                                }
                                if (!anyAlpha)
                                {
                                    isTransparent = true;
                                }
                            }
                            else
                            {
                                bool anyColor = false;
                                for (int i = 0; i < bits.Length; i++)
                                {
                                    if (bits[i] != 0)
                                    {
                                        anyColor = true;
                                        break;
                                    }
                                }
                                if (!anyColor)
                                {
                                    isTransparent = true;
                                }
                            }
                        }
                    }
                }
            }
            else if (iconInfo.hbmMask != IntPtr.Zero)
            {
                if (NativeMethods.GetObject(iconInfo.hbmMask, Marshal.SizeOf<NativeMethods.BITMAP>(), out var bmMask) != 0)
                {
                    int byteCount = bmMask.bmHeight * bmMask.bmWidthBytes;
                    if (byteCount > 0 && byteCount <= 65536)
                    {
                        byte[] bits = new byte[byteCount];
                        if (NativeMethods.GetBitmapBits(iconInfo.hbmMask, byteCount, bits) > 0)
                        {
                            int half = bits.Length / 2;
                            bool allAnd1 = true;
                            for (int i = 0; i < half; i++)
                            {
                                if (bits[i] != 0xFF)
                                {
                                    allAnd1 = false;
                                    break;
                                }
                            }

                            bool allXor0 = true;
                            for (int i = half; i < bits.Length; i++)
                            {
                                if (bits[i] != 0x00)
                                {
                                    allXor0 = false;
                                    break;
                                }
                            }

                            if (allAnd1 && allXor0)
                            {
                                isTransparent = true;
                            }
                        }
                    }
                }
            }
        }
        catch
        {
        }
        finally
        {
            if (iconInfo.hbmColor != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(iconInfo.hbmColor);
            }
            if (iconInfo.hbmMask != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(iconInfo.hbmMask);
            }
        }

        return isTransparent;
    }
}
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace zolsi.cc;

internal static class SelfIntegrity
{
    private static IntPtr _textStart;
    private static int _textSize;
    private static ulong _baselineChecksum;
    private static bool _initialized;

    public static void Initialize()
    {
        try
        {
            IntPtr baseAddr = Process.GetCurrentProcess().MainModule?.BaseAddress ?? IntPtr.Zero;
            if (baseAddr == IntPtr.Zero)
            {
                return;
            }

            int lfanew = Marshal.ReadInt32(baseAddr, 0x3C);
            IntPtr ntHeaders = baseAddr + lfanew;
            short numSections = Marshal.ReadInt16(ntHeaders, 6);
            short optHeaderSize = Marshal.ReadInt16(ntHeaders, 20);
            IntPtr sectionHeader = ntHeaders + 24 + optHeaderSize;

            for (int i = 0; i < numSections; i++)
            {
                IntPtr curSec = sectionHeader + (i * 40);
                byte[] nameBytes = new byte[8];
                for (int b = 0; b < 8; b++)
                {
                    nameBytes[b] = Marshal.ReadByte(curSec, b);
                }
                string name = System.Text.Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');

                if (string.Equals(name, ".text", StringComparison.OrdinalIgnoreCase))
                {
                    int virtSize = Marshal.ReadInt32(curSec, 8);
                    int virtAddr = Marshal.ReadInt32(curSec, 12);
                    _textStart = baseAddr + virtAddr;
                    _textSize = virtSize;
                    _baselineChecksum = ComputeChecksum(_textStart, _textSize);
                    _initialized = true;
                    break;
                }
            }
        }
        catch
        {
        }
    }

    public static bool VerifyIntegrity()
    {
        if (!_initialized || _textStart == IntPtr.Zero || _textSize <= 0)
        {
            return true;
        }

        try
        {
            ulong currentChecksum = ComputeChecksum(_textStart, _textSize);
            return currentChecksum == _baselineChecksum;
        }
        catch
        {
            return false;
        }
    }

    private static ulong ComputeChecksum(IntPtr start, int length)
    {
        const ulong fnvPrime = 1099511628211UL;
        const ulong fnvOffset = 14695981039346656037UL;

        ulong hash = fnvOffset;
        int step = Math.Max(1, length / 4096);

        for (int offset = 0; offset < length; offset += step)
        {
            byte b = Marshal.ReadByte(start, offset);
            hash ^= b;
            hash *= fnvPrime;
        }

        return hash;
    }
}
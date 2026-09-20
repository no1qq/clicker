using System;
using System.Runtime.InteropServices;

namespace zolsi.cc;

internal static class DynamicResolver
{
    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleA", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32.dll", EntryPoint = "GetProcAddress", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryA", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpLibFileName);

    private delegate bool FnDbg1();
    private delegate bool FnDbg2(IntPtr hProcess, out bool isDebuggerPresent);
    private delegate int FnDbg3(IntPtr processHandle, int processInformationClass, IntPtr processInformation, uint processInformationLength, out uint returnLength);
    private delegate IntPtr FnDbg4();

    private static FnDbg1? _isDebuggerPresent;
    private static FnDbg2? _checkRemoteDebuggerPresent;
    private static FnDbg3? _ntQueryInfo;
    private static FnDbg4? _rtlGetCurrentPeb;

    public static IntPtr Resolve(string moduleName, string procName)
    {
        IntPtr hMod = GetModuleHandle(moduleName);
        if (hMod == IntPtr.Zero)
        {
            hMod = LoadLibrary(moduleName);
        }
        if (hMod == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }
        return GetProcAddress(hMod, procName);
    }

    public static bool VerifyFlagA()
    {
        try
        {
            if (_isDebuggerPresent == null)
            {
                IntPtr p = Resolve(StringEncryptor.GetKernel32(), StringEncryptor.GetFnA());
                if (p == IntPtr.Zero)
                {
                    return false;
                }
                _isDebuggerPresent = Marshal.GetDelegateForFunctionPointer<FnDbg1>(p);
            }
            return _isDebuggerPresent();
        }
        catch
        {
            return false;
        }
    }

    public static bool VerifyFlagB(IntPtr hProcess, out bool isRemote)
    {
        isRemote = false;
        try
        {
            if (_checkRemoteDebuggerPresent == null)
            {
                IntPtr p = Resolve(StringEncryptor.GetKernel32(), StringEncryptor.GetFnB());
                if (p == IntPtr.Zero)
                {
                    return false;
                }
                _checkRemoteDebuggerPresent = Marshal.GetDelegateForFunctionPointer<FnDbg2>(p);
            }
            return _checkRemoteDebuggerPresent(hProcess, out isRemote);
        }
        catch
        {
            return false;
        }
    }

    public static bool VerifyFlagC(IntPtr hProcess)
    {
        try
        {
            if (_ntQueryInfo == null)
            {
                IntPtr p = Resolve(StringEncryptor.GetNtdll(), StringEncryptor.GetFnC());
                if (p == IntPtr.Zero)
                {
                    return false;
                }
                _ntQueryInfo = Marshal.GetDelegateForFunctionPointer<FnDbg3>(p);
            }

            IntPtr buffer = Marshal.AllocHGlobal(IntPtr.Size);
            try
            {
                Marshal.WriteIntPtr(buffer, IntPtr.Zero);
                int status = _ntQueryInfo(hProcess, 7, buffer, (uint)IntPtr.Size, out _);
                if (status == 0)
                {
                    IntPtr val = Marshal.ReadIntPtr(buffer);
                    return val != IntPtr.Zero;
                }
                return false;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            return false;
        }
    }

    public static int QueryProcessInfo(IntPtr hProcess, ref NativeMethods.PROCESS_BASIC_INFORMATION pbi)
    {
        try
        {
            if (_ntQueryInfo == null)
            {
                IntPtr p = Resolve(StringEncryptor.GetNtdll(), StringEncryptor.GetFnC());
                if (p == IntPtr.Zero)
                {
                    return -1;
                }
                _ntQueryInfo = Marshal.GetDelegateForFunctionPointer<FnDbg3>(p);
            }

            int size = Marshal.SizeOf<NativeMethods.PROCESS_BASIC_INFORMATION>();
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                int status = _ntQueryInfo(hProcess, 0, buffer, (uint)size, out _);
                if (status == 0)
                {
                    pbi = Marshal.PtrToStructure<NativeMethods.PROCESS_BASIC_INFORMATION>(buffer);
                }
                return status;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            return -1;
        }
    }

    public static IntPtr GetNativeContext()
    {
        try
        {
            if (_rtlGetCurrentPeb == null)
            {
                IntPtr p = Resolve(StringEncryptor.GetNtdll(), StringEncryptor.GetFnD());
                if (p == IntPtr.Zero)
                {
                    return IntPtr.Zero;
                }
                _rtlGetCurrentPeb = Marshal.GetDelegateForFunctionPointer<FnDbg4>(p);
            }
            return _rtlGetCurrentPeb();
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    public static bool VerifyFlagD()
    {
        try
        {
            IntPtr peb = GetNativeContext();
            if (peb == IntPtr.Zero)
            {
                return false;
            }

            byte b = Marshal.ReadByte(peb, 2);
            if (b != 0)
            {
                return true;
            }

            int f = Marshal.ReadInt32(peb, 0xBC);
            if ((f & 0x70) != 0)
            {
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}
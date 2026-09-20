using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace zolsi.cc;

internal static class WindowManager
{
    private static IntPtr _consoleHwnd;
    private static int _cachedConhostPid;
    private static IntPtr _pTaskbar;
    private static unsafe delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int> _pfnAddTab;
    private static unsafe delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int> _pfnDeleteTab;

    public static bool IsStreamProofActive { get; private set; } = true;

    public static void PreConfigureHiddenStartup()
    {
        try
        {
            IntPtr peb = DynamicResolver.GetNativeContext();
            if (peb != IntPtr.Zero)
            {
                IntPtr procParams = Marshal.ReadIntPtr(peb, 0x20);
                if (procParams != IntPtr.Zero)
                {
                    int flags = Marshal.ReadInt32(procParams, 0xA4);
                    Marshal.WriteInt32(procParams, 0xA4, flags | 0x00000001);
                    Marshal.WriteInt16(procParams, 0xA8, 0);
                    Marshal.WriteInt32(procParams, 0x58, -32000);
                    Marshal.WriteInt32(procParams, 0x5C, -32000);
                }
            }
        }
        catch
        {
        }
    }

    public static void Initialize(bool enableStreamProof)
    {
        IsStreamProofActive = enableStreamProof;
        _consoleHwnd = NativeMethods.GetConsoleWindow();
        if (_consoleHwnd != IntPtr.Zero)
        {
            _cachedConhostPid = FindConhostPid(Environment.ProcessId);
        }
        EnsureTaskbarCom();
    }

    public static IntPtr GetContainerHandle() => _consoleHwnd != IntPtr.Zero ? _consoleHwnd : NativeMethods.GetConsoleWindow();
    public static IntPtr GetConsoleHandle() => _consoleHwnd != IntPtr.Zero ? _consoleHwnd : NativeMethods.GetConsoleWindow();
    public static IntPtr GetActiveWindowHandle() => _consoleHwnd != IntPtr.Zero ? _consoleHwnd : NativeMethods.GetConsoleWindow();

    public static void ApplyStreamProof(bool enabled)
    {
        IsStreamProofActive = enabled;

        if (_consoleHwnd == IntPtr.Zero)
        {
            _consoleHwnd = NativeMethods.GetConsoleWindow();
        }

        if (_consoleHwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            int peekVal = enabled ? 1 : 0;
            int flip3dVal = enabled ? NativeMethods.DWMFLIP3D_EXCLUDEABOVE : NativeMethods.DWMFLIP3D_DEFAULT;

            NativeMethods.DwmSetWindowAttribute(_consoleHwnd, NativeMethods.DWMWA_EXCLUDED_FROM_PEEK, ref peekVal, sizeof(int));
            NativeMethods.DwmSetWindowAttribute(_consoleHwnd, NativeMethods.DWMWA_DISALLOW_PEEK, ref peekVal, sizeof(int));
            NativeMethods.DwmSetWindowAttribute(_consoleHwnd, NativeMethods.DWMWA_FLIP3D_POLICY, ref flip3dVal, sizeof(int));

            uint affinity = enabled ? NativeMethods.WDA_EXCLUDEFROMCAPTURE : NativeMethods.WDA_NONE;
            SetConsoleWindowAffinity(_consoleHwnd, affinity);

            UpdateTaskbarList(_consoleHwnd, enabled);

            NativeMethods.SetWindowPos(
                _consoleHwnd,
                IntPtr.Zero,
                0,
                0,
                0,
                0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_FRAMECHANGED);

            if (enabled)
            {
                StartStreamProofSweep(_consoleHwnd);
            }
        }
        catch
        {
        }
    }

    public static void StartStreamProofSweep(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        Task.Run(async () =>
        {
            int[] delays = [5, 15, 30, 60, 120, 250, 500];
            foreach (int d in delays)
            {
                await Task.Delay(d).ConfigureAwait(false);
                if (!IsStreamProofActive)
                {
                    break;
                }
                IntPtr h = _consoleHwnd != IntPtr.Zero ? _consoleHwnd : NativeMethods.GetConsoleWindow();
                if (h != IntPtr.Zero)
                {
                    UpdateTaskbarList(h, true);
                    if (!NativeMethods.GetWindowDisplayAffinity(h, out uint aff) || aff != NativeMethods.WDA_EXCLUDEFROMCAPTURE)
                    {
                        SetConsoleWindowAffinity(h, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
                    }
                }
            }
        });
    }

    public static void EnsureTaskbarCom()
    {
        if (_pTaskbar != IntPtr.Zero)
        {
            return;
        }

        try
        {
            NativeMethods.CoInitializeEx(IntPtr.Zero, NativeMethods.COINIT_APARTMENTTHREADED);

            var clsid = new Guid("56FDF344-FD6D-11d0-958A-006097C9A090");
            var iid = new Guid("56FDF342-FD6D-11d0-958A-006097C9A090");
            if (NativeMethods.CoCreateInstance(clsid, IntPtr.Zero, 1, iid, out IntPtr pTaskbar) == 0 && pTaskbar != IntPtr.Zero)
            {
                IntPtr vtbl = Marshal.ReadIntPtr(pTaskbar);
                IntPtr pfnHrInit = Marshal.ReadIntPtr(vtbl, 3 * IntPtr.Size);
                IntPtr pfnAddTab = Marshal.ReadIntPtr(vtbl, 4 * IntPtr.Size);
                IntPtr pfnDeleteTab = Marshal.ReadIntPtr(vtbl, 5 * IntPtr.Size);

                unsafe
                {
                    var hrInit = (delegate* unmanaged[Stdcall]<IntPtr, int>)pfnHrInit;
                    hrInit(pTaskbar);
                    _pfnAddTab = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)pfnAddTab;
                    _pfnDeleteTab = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)pfnDeleteTab;
                }

                _pTaskbar = pTaskbar;
            }
        }
        catch
        {
        }
    }

    public static void UpdateTaskbarList(IntPtr hwnd, bool remove)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            EnsureTaskbarCom();
            if (_pTaskbar != IntPtr.Zero)
            {
                unsafe
                {
                    if (remove)
                    {
                        if (_pfnDeleteTab != null)
                        {
                            _pfnDeleteTab(_pTaskbar, hwnd);
                        }
                    }
                    else
                    {
                        if (_pfnAddTab != null)
                        {
                            _pfnAddTab(_pTaskbar, hwnd);
                        }
                    }
                }
            }
        }
        catch
        {
        }
    }

    private static bool SetConsoleWindowAffinity(IntPtr consoleHwnd, uint affinity)
    {
        int hostPid = GetTargetHostPid(consoleHwnd);
        if (hostPid != 0 && hostPid != Environment.ProcessId)
        {
            if (CallRemoteSetAffinity(hostPid, consoleHwnd, affinity))
            {
                if (NativeMethods.GetWindowDisplayAffinity(consoleHwnd, out uint aff) && aff == affinity)
                {
                    return true;
                }
            }
        }

        try
        {
            var conhosts = Process.GetProcessesByName("conhost");
            foreach (var p in conhosts)
            {
                if (p.Id != hostPid && p.Id != Environment.ProcessId)
                {
                    if (CallRemoteSetAffinity(p.Id, consoleHwnd, affinity))
                    {
                        if (NativeMethods.GetWindowDisplayAffinity(consoleHwnd, out uint aff) && aff == affinity)
                        {
                            _cachedConhostPid = p.Id;
                            p.Dispose();
                            return true;
                        }
                    }
                }
                p.Dispose();
            }
        }
        catch
        {
        }

        if (NativeMethods.GetWindowDisplayAffinity(consoleHwnd, out uint currentAff) && currentAff == affinity)
        {
            return true;
        }

        return false;
    }

    private static int GetTargetHostPid(IntPtr hwnd)
    {
        if (_cachedConhostPid != 0 && _cachedConhostPid != Environment.ProcessId)
        {
            try
            {
                var p = Process.GetProcessById(_cachedConhostPid);
                if (!p.HasExited && p.ProcessName.Equals("conhost", StringComparison.OrdinalIgnoreCase))
                {
                    return _cachedConhostPid;
                }
            }
            catch
            {
                _cachedConhostPid = 0;
            }
        }

        int found = FindConhostPid(Environment.ProcessId);
        if (found != 0)
        {
            _cachedConhostPid = found;
            return _cachedConhostPid;
        }

        return 0;
    }

    private static int FindConhostPid(int myPid)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                var conhosts = Process.GetProcessesByName("conhost");
                foreach (var p in conhosts)
                {
                    try
                    {
                        if (p.Id != myPid && GetParentProcessId(p.Id) == myPid)
                        {
                            return p.Id;
                        }
                    }
                    catch
                    {
                    }
                    finally
                    {
                        p.Dispose();
                    }
                }
            }
            catch
            {
            }

            if (attempt < 4)
            {
                System.Threading.Thread.Sleep(15);
            }
        }

        try
        {
            var conhosts = Process.GetProcessesByName("conhost");
            foreach (var p in conhosts)
            {
                try
                {
                    if (p.Id != myPid)
                    {
                        return p.Id;
                    }
                }
                catch
                {
                }
                finally
                {
                    p.Dispose();
                }
            }
        }
        catch
        {
        }

        return 0;
    }

    private static int GetParentProcessId(int pid)
    {
        IntPtr h = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_INFORMATION, false, pid);
        if (h == IntPtr.Zero)
        {
            return 0;
        }
        try
        {
            var pbi = new NativeMethods.PROCESS_BASIC_INFORMATION();
            int status = DynamicResolver.QueryProcessInfo(h, ref pbi);
            if (status == 0)
            {
                return pbi.InheritedFromUniqueProcessId.ToInt32();
            }
        }
        finally
        {
            NativeMethods.CloseHandle(h);
        }
        return 0;
    }

    private static bool CallRemoteSetAffinity(int conhostPid, IntPtr consoleHwnd, uint affinity)
    {
        IntPtr pFunc = DynamicResolver.Resolve("user32.dll", "SetWindowDisplayAffinity");
        if (pFunc == IntPtr.Zero)
        {
            return false;
        }

        IntPtr hProc = NativeMethods.OpenProcess(NativeMethods.PROCESS_ALL_ACCESS, false, conhostPid);
        if (hProc == IntPtr.Zero)
        {
            hProc = NativeMethods.OpenProcess(0x043A, false, conhostPid);
        }
        if (hProc == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            byte[] code =
            [
                0x48, 0x83, 0xEC, 0x28,
                0x48, 0xB9, 0, 0, 0, 0, 0, 0, 0, 0,
                0xBA, 0, 0, 0, 0,
                0x48, 0xB8, 0, 0, 0, 0, 0, 0, 0, 0,
                0xFF, 0xD0,
                0x48, 0x83, 0xC4, 0x28,
                0xC3
            ];

            byte[] hwndBytes = BitConverter.GetBytes(consoleHwnd.ToInt64());
            Array.Copy(hwndBytes, 0, code, 6, 8);

            byte[] affBytes = BitConverter.GetBytes(affinity);
            Array.Copy(affBytes, 0, code, 15, 4);

            byte[] funcBytes = BitConverter.GetBytes(pFunc.ToInt64());
            Array.Copy(funcBytes, 0, code, 21, 8);

            IntPtr mem = NativeMethods.VirtualAllocEx(
                hProc,
                IntPtr.Zero,
                (uint)code.Length,
                NativeMethods.MEM_COMMIT | NativeMethods.MEM_RESERVE,
                NativeMethods.PAGE_EXECUTE_READWRITE);

            if (mem == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                if (!NativeMethods.WriteProcessMemory(hProc, mem, code, code.Length, out _))
                {
                    return false;
                }

                IntPtr hThread = NativeMethods.CreateRemoteThread(
                    hProc,
                    IntPtr.Zero,
                    0,
                    mem,
                    IntPtr.Zero,
                    0,
                    out _);

                if (hThread == IntPtr.Zero)
                {
                    return false;
                }

                try
                {
                    NativeMethods.WaitForSingleObject(hThread, 2000);
                    if (NativeMethods.GetExitCodeThread(hThread, out uint exitCode))
                    {
                        return exitCode != 0;
                    }
                    return false;
                }
                finally
                {
                    NativeMethods.CloseHandle(hThread);
                }
            }
            finally
            {
                NativeMethods.VirtualFreeEx(hProc, mem, 0, NativeMethods.MEM_RELEASE);
            }
        }
        finally
        {
            NativeMethods.CloseHandle(hProc);
        }
    }

    public static void ApplyAlwaysOnTop(bool enabled)
    {
        IntPtr target = _consoleHwnd != IntPtr.Zero ? _consoleHwnd : NativeMethods.GetConsoleWindow();
        if (target == IntPtr.Zero)
        {
            return;
        }

        try
        {
            NativeMethods.SetWindowPos(
                target,
                enabled ? NativeMethods.HWND_TOPMOST : NativeMethods.HWND_NOTOPMOST,
                0,
                0,
                0,
                0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE);
        }
        catch
        {
        }
    }

    public static void BringToFront()
    {
        IntPtr target = _consoleHwnd != IntPtr.Zero ? _consoleHwnd : NativeMethods.GetConsoleWindow();
        if (target == IntPtr.Zero)
        {
            return;
        }

        try
        {
            NativeMethods.ShowWindow(target, NativeMethods.SW_RESTORE);

            if (IsStreamProofActive)
            {
                UpdateTaskbarList(target, true);
                ApplyStreamProof(true);
                StartStreamProofSweep(target);
            }

            IntPtr fg = NativeMethods.GetForegroundWindow();
            uint foreThread = NativeMethods.GetWindowThreadProcessId(fg, out _);
            uint appThread = NativeMethods.GetCurrentThreadId();

            if (foreThread != 0 && foreThread != appThread)
            {
                NativeMethods.AttachThreadInput(appThread, foreThread, true);
            }

            NativeMethods.keybd_event(NativeMethods.VK_MENU, 0, 0, UIntPtr.Zero);
            NativeMethods.keybd_event(NativeMethods.VK_MENU, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);

            NativeMethods.SetForegroundWindow(target);
            NativeMethods.BringWindowToTop(target);

            if (!UserProfileManager.CurrentProfile.AlwaysOnTop)
            {
                NativeMethods.SetWindowPos(
                    target,
                    NativeMethods.HWND_TOPMOST,
                    0,
                    0,
                    0,
                    0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);

                NativeMethods.SetWindowPos(
                    target,
                    NativeMethods.HWND_NOTOPMOST,
                    0,
                    0,
                    0,
                    0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
            }
            else
            {
                NativeMethods.SetWindowPos(
                    target,
                    NativeMethods.HWND_TOPMOST,
                    0,
                    0,
                    0,
                    0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
            }

            NativeMethods.SetFocus(target);

            if (foreThread != 0 && foreThread != appThread)
            {
                NativeMethods.AttachThreadInput(appThread, foreThread, false);
            }
        }
        catch
        {
        }
    }

    public static void Relaunch(bool enableStreamProof, string? pin, string? targetPage)
    {
        try
        {
            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
            {
                exePath = Process.GetCurrentProcess().MainModule?.FileName;
            }

            if (!string.IsNullOrEmpty(exePath))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    WorkingDirectory = Environment.CurrentDirectory
                };

                psi.ArgumentList.Add("--streamproof");
                psi.ArgumentList.Add(enableStreamProof ? "true" : "false");

                if (!string.IsNullOrEmpty(pin))
                {
                    psi.ArgumentList.Add("--pin");
                    psi.ArgumentList.Add(pin);
                }

                if (!string.IsNullOrEmpty(targetPage))
                {
                    psi.ArgumentList.Add("--page");
                    psi.ArgumentList.Add(targetPage);
                }

                Process.Start(psi);
            }
        }
        catch
        {
        }

        Environment.Exit(0);
    }

    public static string GetKeyName(int vk)
    {
        if (vk == 0)
        {
            return "none";
        }

        return vk switch
        {
            0x2D => "INSERT",
            0x24 => "HOME",
            0x23 => "END",
            0x21 => "PGUP",
            0x22 => "PGDN",
            0x2E => "DELETE",
            0x04 => "M3",
            0x05 => "M4",
            0x06 => "M5",
            >= 0x70 and <= 0x7B => $"F{vk - 0x6F}",
            >= 0x41 and <= 0x5A => ((char)vk).ToString(),
            >= 0x30 and <= 0x39 => ((char)vk).ToString(),
            _ => ((ConsoleKey)vk).ToString()
        };
    }
}
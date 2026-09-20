using System;
using System.Diagnostics;
using System.Threading;

namespace zolsi.cc;

internal static class AntiDebug
{
    public static void PerformStartupCheck()
    {
        SelfIntegrity.Initialize();

        if (AuditFlagA())
        {
            SecurityMonitor.TriggerSecurityViolation("Debugger Detected (Startup FlagA)");
        }
        else if (AuditFlagB(out string badProc))
        {
            SecurityMonitor.TriggerSecurityViolation("Blacklisted Analysis Tool Running", badProc);
        }
        else if (!SelfIntegrity.VerifyIntegrity())
        {
            SecurityMonitor.TriggerSecurityViolation("Executable Code (.text) Tampering");
        }
    }

    public static void StartWatchdog()
    {
        var thread = new Thread(WatchdogLoop)
        {
            IsBackground = true
        };
        thread.Start();
    }

    private static void WatchdogLoop()
    {
        while (true)
        {
            Thread.Sleep(1500);

            if (AuditFlagA())
            {
                SecurityMonitor.TriggerSecurityViolation("Debugger Detected (Runtime FlagA)");
            }
            else if (AuditFlagB(out string badProc))
            {
                SecurityMonitor.TriggerSecurityViolation("Blacklisted Analysis Tool Running", badProc);
            }
            else if (!SelfIntegrity.VerifyIntegrity())
            {
                SecurityMonitor.TriggerSecurityViolation("Executable Code (.text) Tampering");
            }

            if (WindowManager.IsStreamProofActive)
            {
                IntPtr h = WindowManager.GetConsoleHandle();
                if (h != IntPtr.Zero)
                {
                    WindowManager.UpdateTaskbarList(h, true);
                }
            }
        }
    }

    private static bool AuditFlagA()
    {
        try
        {
            if (Debugger.IsAttached)
            {
                return true;
            }

            if (DynamicResolver.VerifyFlagD())
            {
                return true;
            }

            if (DynamicResolver.VerifyFlagA())
            {
                return true;
            }

            IntPtr handle = Process.GetCurrentProcess().Handle;
            if (DynamicResolver.VerifyFlagB(handle, out bool isRemote) && isRemote)
            {
                return true;
            }

            if (DynamicResolver.VerifyFlagC(handle))
            {
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool AuditFlagB(out string matchedProcess)
    {
        matchedProcess = "";
        try
        {
            string[] badProcesses = StringEncryptor.GetBadProcesses();
            var processes = Process.GetProcesses();
            foreach (var p in processes)
            {
                try
                {
                    string name = p.ProcessName.ToLowerInvariant();
                    for (int i = 0; i < badProcesses.Length; i++)
                    {
                        if (name.Contains(badProcesses[i], StringComparison.OrdinalIgnoreCase))
                        {
                            matchedProcess = name;
                            return true;
                        }
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

        return false;
    }
}
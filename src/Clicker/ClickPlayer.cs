using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace zolsi.cc;

internal static class ClickPlayer
{
    private const uint MagicExtraInfo = 0x5A4F4C;
    private static Thread? _thread;
    private static Thread? _hookThread;
    private static volatile bool _isRunning;
    private static volatile bool _isPhysicalDown;
    private static volatile bool _isMouseDownInjected;
    private static volatile int _patternIndex;
    private static bool _wasBindDown;
    private static bool _wasBringToFrontDown;
    private static IntPtr _hookHandle = IntPtr.Zero;
    private static NativeMethods.LowLevelMouseProc? _hookDelegate;
    private static uint _hookThreadId;

    private static bool _isToggled;
    public static bool IsToggled
    {
        get => _isToggled;
        set
        {
            _isToggled = value;
            _patternIndex = 0;
        }
    }

    public static bool DisableInMenu { get; set; } = true;
    public static bool UseJitter { get; set; }
    public static int ToggleBindKey { get; set; }

    private static ClickPattern? _activePattern;
    public static ClickPattern? ActivePattern
    {
        get => _activePattern;
        set
        {
            _activePattern = value;
            _patternIndex = 0;
        }
    }

    public static string ActivePatternName { get; set; } = "none";
    public static Action? OnStateChanged { get; set; }

    public static void Start()
    {
        if (_isRunning)
        {
            return;
        }

        _isRunning = true;
        _patternIndex = 0;
        _isPhysicalDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) != 0;

        _hookThread = new Thread(HookThreadLoop)
        {
            IsBackground = true,
            Priority = ThreadPriority.Highest
        };
        _hookThread.Start();

        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Priority = ThreadPriority.Highest
        };
        _thread.Start();
    }

    public static void Stop()
    {
        _isRunning = false;
        _patternIndex = 0;

        if (_hookThreadId != 0)
        {
            NativeMethods.PostThreadMessage(_hookThreadId, NativeMethods.WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
        }
        _hookThread?.Join(300);

        _thread?.Join(300);

        if (_isMouseDownInjected)
        {
            NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_LEFTUP, 0, 0, 0, (UIntPtr)MagicExtraInfo);
            _isMouseDownInjected = false;
        }
    }

    public static string GetBindKeyName()
    {
        if (ToggleBindKey == 0)
        {
            return "none";
        }

        return ToggleBindKey switch
        {
            0x04 => "M3",
            0x05 => "M4",
            0x06 => "M5",
            >= 0x70 and <= 0x7B => $"F{ToggleBindKey - 0x6F}",
            >= 0x41 and <= 0x5A => ((char)ToggleBindKey).ToString(),
            >= 0x30 and <= 0x39 => ((char)ToggleBindKey).ToString(),
            _ => ((ConsoleKey)ToggleBindKey).ToString()
        };
    }

    private static void HookThreadLoop()
    {
        _hookThreadId = NativeMethods.GetCurrentThreadId();
        _hookDelegate = HookCallback;
        IntPtr hMod = NativeMethods.GetModuleHandle(null);
        _hookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _hookDelegate, hMod, 0);

        while (_isRunning)
        {
            int ret = NativeMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0);
            if (ret <= 0)
            {
                break;
            }

            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessage(ref msg);
        }

        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var hookStruct = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
            bool isInjected = (hookStruct.flags & NativeMethods.LLMHF_INJECTED) != 0 || hookStruct.dwExtraInfo == (UIntPtr)MagicExtraInfo;

            if (!isInjected)
            {
                int msg = (int)wParam;
                if (msg == NativeMethods.WM_LBUTTONDOWN)
                {
                    _isPhysicalDown = true;
                }
                else if (msg == NativeMethods.WM_LBUTTONUP)
                {
                    _isPhysicalDown = false;
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private static bool CanClick()
    {
        return IsToggled &&
               ActivePattern != null &&
               ActivePattern.Clicks.Count > 0 &&
               MinecraftHelper.IsMinecraftForeground() &&
               (!DisableInMenu || !CursorHelper.IsCursorVisible());
    }

    private static void Loop()
    {
        NativeMethods.TimeBeginPeriod(1);

        try
        {
            while (_isRunning)
            {
                CheckToggleKey();
                CheckBringToFrontKey();

                if (!CanClick())
                {
                    _patternIndex = 0;
                    Thread.Sleep(10);
                    continue;
                }

                if (!_isPhysicalDown)
                {
                    _patternIndex = 0;
                    Thread.Sleep(2);
                    continue;
                }

                if (SecurityMonitor.ShouldDegradeClick(out int extraDelay))
                {
                    continue;
                }
                if (extraDelay > 0)
                {
                    Thread.Sleep(extraDelay);
                }

                if (_patternIndex >= ActivePattern!.Clicks.Count)
                {
                    _patternIndex = 0;
                }

                var timing = ActivePattern.Clicks[_patternIndex];
                _patternIndex = (_patternIndex + 1) % ActivePattern.Clicks.Count;

                bool held = WaitDelay(timing.HoldMs);
                if (!held)
                {
                    _patternIndex = 0;
                    if (_isPhysicalDown && !CanClick() && MinecraftHelper.IsMinecraftForeground())
                    {
                        NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, (UIntPtr)MagicExtraInfo);
                    }
                    continue;
                }

                NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_LEFTUP, 0, 0, 0, (UIntPtr)MagicExtraInfo);

                int initJx = 0;
                int initJy = 0;
                if (UseJitter && ActivePattern.Clicks.Count > 1)
                {
                    initJx = ActivePattern.Clicks[_patternIndex].JitterX - timing.JitterX;
                    initJy = ActivePattern.Clicks[_patternIndex].JitterY - timing.JitterY;
                }

                bool gapped = WaitDelayWithJitter(timing.GapMs, initJx, initJy);
                if (!gapped)
                {
                    _patternIndex = 0;
                    if (_isPhysicalDown && !CanClick() && MinecraftHelper.IsMinecraftForeground())
                    {
                        NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, (UIntPtr)MagicExtraInfo);
                    }
                    continue;
                }

                while (_isRunning && _isPhysicalDown && CanClick())
                {
                    timing = ActivePattern.Clicks[_patternIndex];
                    int nextIndex = (_patternIndex + 1) % ActivePattern.Clicks.Count;

                    NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, (UIntPtr)MagicExtraInfo);
                    _isMouseDownInjected = true;

                    if (!WaitDelay(timing.HoldMs))
                    {
                        break;
                    }

                    NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_LEFTUP, 0, 0, 0, (UIntPtr)MagicExtraInfo);
                    _isMouseDownInjected = false;

                    int jx = 0;
                    int jy = 0;
                    if (UseJitter && ActivePattern.Clicks.Count > 1)
                    {
                        jx = ActivePattern.Clicks[nextIndex].JitterX - timing.JitterX;
                        jy = ActivePattern.Clicks[nextIndex].JitterY - timing.JitterY;
                    }

                    if (!WaitDelayWithJitter(timing.GapMs, jx, jy))
                    {
                        break;
                    }

                    _patternIndex = nextIndex;
                }

                if (_isMouseDownInjected)
                {
                    NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_LEFTUP, 0, 0, 0, (UIntPtr)MagicExtraInfo);
                    _isMouseDownInjected = false;
                }

                if (_isPhysicalDown && !CanClick() && MinecraftHelper.IsMinecraftForeground())
                {
                    NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, (UIntPtr)MagicExtraInfo);
                }

                _patternIndex = 0;
            }
        }
        finally
        {
            if (_isMouseDownInjected)
            {
                NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_LEFTUP, 0, 0, 0, (UIntPtr)MagicExtraInfo);
                _isMouseDownInjected = false;
            }
            _patternIndex = 0;
            NativeMethods.TimeEndPeriod(1);
        }
    }

    private static void CheckToggleKey()
    {
        if (ToggleBindKey == 0)
        {
            return;
        }

        bool isDown = (NativeMethods.GetAsyncKeyState(ToggleBindKey) & 0x8000) != 0;
        if (isDown && !_wasBindDown)
        {
            if (MinecraftHelper.IsMinecraftForeground())
            {
                IsToggled = !IsToggled;
                OnStateChanged?.Invoke();
            }
        }
        _wasBindDown = isDown;
    }

    private static void CheckBringToFrontKey()
    {
        int key = UserProfileManager.CurrentProfile.BringToFrontBindKey;
        if (key == 0)
        {
            return;
        }

        bool isDown = (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0;
        if (isDown && !_wasBringToFrontDown)
        {
            WindowManager.BringToFront();
        }
        _wasBringToFrontDown = isDown;
    }

    private static bool WaitDelay(int ms) => WaitDelayWithJitter(ms, 0, 0);

    private static bool WaitDelayWithJitter(int ms, int dx, int dy)
    {
        if (ms <= 0)
        {
            if (dx != 0 || dy != 0)
            {
                NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_MOVE, dx, dy, 0, (UIntPtr)MagicExtraInfo);
            }
            return true;
        }

        int movedX = 0;
        int movedY = 0;
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms)
        {
            CheckToggleKey();
            CheckBringToFrontKey();

            if (!_isPhysicalDown || !CanClick())
            {
                return false;
            }

            if (dx != 0 || dy != 0)
            {
                double progress = Math.Clamp(sw.Elapsed.TotalMilliseconds / ms, 0.0, 1.0);
                double smooth = progress * progress * (3.0 - 2.0 * progress);
                int targetX = (int)Math.Round(dx * smooth);
                int targetY = (int)Math.Round(dy * smooth);
                int stepX = targetX - movedX;
                int stepY = targetY - movedY;

                if (stepX != 0 || stepY != 0)
                {
                    NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_MOVE, stepX, stepY, 0, (UIntPtr)MagicExtraInfo);
                    movedX += stepX;
                    movedY += stepY;
                }
            }

            int remaining = ms - (int)sw.ElapsedMilliseconds;
            if (remaining > 2)
            {
                Thread.Sleep(1);
            }
            else
            {
                Thread.SpinWait(50);
            }
        }

        if (dx != 0 || dy != 0)
        {
            int remX = dx - movedX;
            int remY = dy - movedY;
            if (remX != 0 || remY != 0)
            {
                NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_MOVE, remX, remY, 0, (UIntPtr)MagicExtraInfo);
            }
        }

        return true;
    }
}
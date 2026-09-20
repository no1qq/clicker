using System;
using System.Collections.Generic;

namespace zolsi.cc;

internal enum InputEventType
{
    None,
    MouseMove,
    LeftClick,
    LeftUp,
    RightClick,
    KeyDown,
    WindowResize,
    Tick
}

internal readonly record struct AppInputEvent(
    InputEventType Type,
    int X,
    int Y,
    char Character,
    ushort VirtualKey,
    uint ControlState);

internal static class InputManager
{
    private static IntPtr ConsoleInputHandle => NativeMethods.GetStdHandle(NativeMethods.STD_INPUT_HANDLE);
    private static IntPtr ConsoleOutputHandle => NativeMethods.GetStdHandle(NativeMethods.STD_OUTPUT_HANDLE);
    private static IntPtr _consoleWindowHandle = IntPtr.Zero;
    private static readonly NativeMethods.INPUT_RECORD[] RecordBuffer = new NativeMethods.INPUT_RECORD[64];
    private static bool _wasLeftDown;
    private static bool _wasRightDown;
    private static int _lastBufferW = -1;
    private static int _lastBufferH = -1;
    private static int _lastUnfocusedX = -1;
    private static int _lastUnfocusedY = -1;

    public static IEnumerable<AppInputEvent> WaitForEvents()
    {
        if (!NativeMethods.GetNumberOfConsoleInputEvents(ConsoleInputHandle, out uint count) || count == 0)
        {
            if (_consoleWindowHandle == IntPtr.Zero)
            {
                _consoleWindowHandle = WindowManager.GetConsoleHandle();
                if (_consoleWindowHandle == IntPtr.Zero)
                {
                    _consoleWindowHandle = NativeMethods.GetConsoleWindow();
                }
            }

            if (_consoleWindowHandle != IntPtr.Zero)
            {
                IntPtr fg = NativeMethods.GetForegroundWindow();
                IntPtr activeHwnd = WindowManager.GetActiveWindowHandle();
                if (fg != activeHwnd && fg != _consoleWindowHandle)
                {
                    bool isLeftDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) != 0;
                    bool isRightDown = (NativeMethods.GetAsyncKeyState(0x02) & 0x8000) != 0;

                    if (NativeMethods.GetCursorPos(out var ptScreen))
                    {
                        IntPtr wndUnder = NativeMethods.WindowFromPoint(ptScreen);
                        IntPtr rootUnder = wndUnder != IntPtr.Zero ? NativeMethods.GetAncestor(wndUnder, NativeMethods.GA_ROOT) : IntPtr.Zero;
                        bool isOverConsole = (wndUnder == _consoleWindowHandle || rootUnder == _consoleWindowHandle);

                        if (isOverConsole)
                        {
                            var ptClient = ptScreen;
                            if (NativeMethods.ScreenToClient(_consoleWindowHandle, ref ptClient) &&
                                NativeMethods.GetClientRect(_consoleWindowHandle, out var rc))
                            {
                                if (ptClient.X >= 0 && ptClient.X < rc.Right && ptClient.Y >= 0 && ptClient.Y < rc.Bottom)
                                {
                                    int cellW = 0;
                                    int cellH = 0;
                                    if (NativeMethods.GetCurrentConsoleFont(ConsoleOutputHandle, false, out var fontInfo))
                                    {
                                        cellW = fontInfo.dwFontSize.X;
                                        cellH = fontInfo.dwFontSize.Y;
                                    }

                                    if (cellW <= 0 || cellH <= 0)
                                    {
                                        int winW = Math.Max(1, Console.WindowWidth);
                                        int winH = Math.Max(1, Console.WindowHeight);
                                        cellW = Math.Max(1, (rc.Right - rc.Left) / winW);
                                        cellH = Math.Max(1, (rc.Bottom - rc.Top) / winH);
                                    }

                                    int cellX = ptClient.X / cellW;
                                    int cellY = ptClient.Y / cellH;

                                    if (isLeftDown && !_wasLeftDown)
                                    {
                                        NativeMethods.SetForegroundWindow(activeHwnd);
                                        yield return new AppInputEvent(InputEventType.LeftClick, cellX, cellY, '\0', 0, 0);
                                    }
                                    else if (!isLeftDown && _wasLeftDown)
                                    {
                                        yield return new AppInputEvent(InputEventType.LeftUp, cellX, cellY, '\0', 0, 0);
                                    }

                                    if (isRightDown && !_wasRightDown)
                                    {
                                        NativeMethods.SetForegroundWindow(activeHwnd);
                                        yield return new AppInputEvent(InputEventType.RightClick, cellX, cellY, '\0', 0, 0);
                                    }

                                    if (cellX != _lastUnfocusedX || cellY != _lastUnfocusedY)
                                    {
                                        _lastUnfocusedX = cellX;
                                        _lastUnfocusedY = cellY;
                                        yield return new AppInputEvent(InputEventType.MouseMove, cellX, cellY, '\0', 0, 0);
                                    }
                                }
                                else
                                {
                                    if (_lastUnfocusedX >= 0)
                                    {
                                        _lastUnfocusedX = -1;
                                        _lastUnfocusedY = -1;
                                        yield return new AppInputEvent(InputEventType.MouseMove, -1, -1, '\0', 0, 0);
                                    }
                                }
                            }
                        }
                        else
                        {
                            if (_lastUnfocusedX >= 0)
                            {
                                _lastUnfocusedX = -1;
                                _lastUnfocusedY = -1;
                                yield return new AppInputEvent(InputEventType.MouseMove, -1, -1, '\0', 0, 0);
                            }
                        }
                    }

                    _wasLeftDown = isLeftDown;
                    _wasRightDown = isRightDown;
                }
                else
                {
                    _lastUnfocusedX = -1;
                    _lastUnfocusedY = -1;
                }
            }

            System.Threading.Thread.Sleep(25);
            yield return new AppInputEvent(InputEventType.Tick, 0, 0, '\0', 0, 0);
            yield break;
        }

        if (!NativeMethods.ReadConsoleInput(ConsoleInputHandle, RecordBuffer, (uint)RecordBuffer.Length, out uint readCount) || readCount == 0)
        {
            yield break;
        }

        for (int i = 0; i < readCount; i++)
        {
            ref readonly var rec = ref RecordBuffer[i];
            if (rec.EventType == NativeMethods.MOUSE_EVENT)
            {
                var mouse = rec.MouseEvent;
                int x = mouse.dwMousePosition.X;
                int y = mouse.dwMousePosition.Y;

                try
                {
                    x -= Console.WindowLeft;
                    y -= Console.WindowTop;
                }
                catch
                {
                }

                bool isLeftDown = (mouse.dwButtonState & NativeMethods.FROM_LEFT_1ST_BUTTON_PRESSED) != 0;
                bool isRightDown = (mouse.dwButtonState & NativeMethods.RIGHTMOST_BUTTON_PRESSED) != 0;

                if (isLeftDown && !_wasLeftDown)
                {
                    yield return new AppInputEvent(InputEventType.LeftClick, x, y, '\0', 0, mouse.dwControlKeyState);
                }
                else if (!isLeftDown && _wasLeftDown)
                {
                    yield return new AppInputEvent(InputEventType.LeftUp, x, y, '\0', 0, mouse.dwControlKeyState);
                }
                _wasLeftDown = isLeftDown;

                if (isRightDown && !_wasRightDown)
                {
                    yield return new AppInputEvent(InputEventType.RightClick, x, y, '\0', 0, mouse.dwControlKeyState);
                }
                _wasRightDown = isRightDown;

                if (mouse.dwEventFlags == NativeMethods.MOUSE_MOVED)
                {
                    yield return new AppInputEvent(InputEventType.MouseMove, x, y, '\0', 0, mouse.dwControlKeyState);
                }
            }
            else if (rec.EventType == NativeMethods.KEY_EVENT)
            {
                var key = rec.KeyEvent;
                if (key.bKeyDown != 0)
                {
                    yield return new AppInputEvent(
                        InputEventType.KeyDown,
                        0,
                        0,
                        key.UnicodeChar,
                        key.wVirtualKeyCode,
                        key.dwControlKeyState);
                }
            }
            else if (rec.EventType == NativeMethods.WINDOW_BUFFER_SIZE_EVENT)
            {
                int w = rec.WindowBufferSizeEvent.dwSize.X;
                int h = rec.WindowBufferSizeEvent.dwSize.Y;
                if (_lastBufferW > 0 && (w != _lastBufferW || h != _lastBufferH))
                {
                    _lastBufferW = w;
                    _lastBufferH = h;
                    yield return new AppInputEvent(InputEventType.WindowResize, 0, 0, '\0', 0, 0);
                }
                else if (_lastBufferW <= 0)
                {
                    _lastBufferW = w;
                    _lastBufferH = h;
                }
            }
        }
    }
}
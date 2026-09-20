using System;
using System.IO;
using System.Text;

namespace zolsi.cc;

internal static class ConsoleWindow
{
    public static void Initialize(bool streamProof)
    {
        IntPtr hwnd = NativeMethods.GetConsoleWindow();
        if (hwnd == IntPtr.Zero)
        {
            if (streamProof)
            {
                WindowManager.PreConfigureHiddenStartup();
            }
            NativeMethods.AllocConsole();
            hwnd = NativeMethods.GetConsoleWindow();
        }

        if (hwnd != IntPtr.Zero)
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);
            NativeMethods.SetWindowPos(
                hwnd,
                IntPtr.Zero,
                -32000,
                -32000,
                0,
                0,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_HIDEWINDOW);

            WindowManager.Initialize(streamProof);
            WindowManager.ApplyStreamProof(streamProof);

            long style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE).ToInt64();
            style &= ~NativeMethods.WS_MAXIMIZEBOX;
            style &= ~NativeMethods.WS_SIZEBOX;
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE, new IntPtr(style));

            IntPtr sysMenu = NativeMethods.GetSystemMenu(hwnd, false);
            if (sysMenu != IntPtr.Zero)
            {
                NativeMethods.DeleteMenu(sysMenu, NativeMethods.SC_SIZE, NativeMethods.MF_BYCOMMAND);
                NativeMethods.DeleteMenu(sysMenu, NativeMethods.SC_MAXIMIZE, NativeMethods.MF_BYCOMMAND);
            }

            NativeMethods.ShowScrollBar(hwnd, NativeMethods.SB_BOTH, false);

            int darkMode = 1;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref darkMode, sizeof(int));

            int screenW = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
            int screenH = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);
            int posX = Math.Max(0, (screenW - 800) / 2);
            int posY = Math.Max(0, (screenH - 600) / 2);

            NativeMethods.SetWindowPos(
                hwnd,
                IntPtr.Zero,
                posX,
                posY,
                800,
                600,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_NOACTIVATE);
        }

        NativeMethods.SetConsoleCP(65001);
        NativeMethods.SetConsoleOutputCP(65001);

        var outStream = Console.OpenStandardOutput();
        var stdOut = new StreamWriter(outStream, Encoding.UTF8) { AutoFlush = true };
        Console.SetOut(stdOut);

        var inStream = Console.OpenStandardInput();
        var stdIn = new StreamReader(inStream, Encoding.UTF8);
        Console.SetIn(stdIn);

        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;
        Console.Title = " ";

        IntPtr hIn = NativeMethods.GetStdHandle(NativeMethods.STD_INPUT_HANDLE);
        if (NativeMethods.GetConsoleMode(hIn, out uint inMode))
        {
            inMode &= ~NativeMethods.ENABLE_QUICK_EDIT_MODE;
            inMode &= ~NativeMethods.ENABLE_WINDOW_INPUT;
            inMode |= NativeMethods.ENABLE_EXTENDED_FLAGS;
            inMode |= NativeMethods.ENABLE_MOUSE_INPUT;
            NativeMethods.SetConsoleMode(hIn, inMode);
        }

        IntPtr hOut = NativeMethods.GetStdHandle(NativeMethods.STD_OUTPUT_HANDLE);
        if (NativeMethods.GetConsoleMode(hOut, out uint outMode))
        {
            outMode |= NativeMethods.ENABLE_PROCESSED_OUTPUT;
            outMode |= NativeMethods.ENABLE_WRAP_AT_EOL_OUTPUT;
            outMode |= NativeMethods.ENABLE_VIRTUAL_TERMINAL_PROCESSING;
            NativeMethods.SetConsoleMode(hOut, outMode);
        }

        try
        {
            Console.SetWindowPosition(0, 0);
            if (Console.WindowWidth > 0 && Console.WindowHeight > 0)
            {
                Console.SetBufferSize(Console.WindowWidth, Console.WindowHeight);
            }
        }
        catch
        {
        }

        Console.CursorVisible = false;
        Clear();

        if (hwnd != IntPtr.Zero)
        {
            if (streamProof)
            {
                WindowManager.UpdateTaskbarList(hwnd, true);
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
                WindowManager.UpdateTaskbarList(hwnd, true);
                NativeMethods.SetForegroundWindow(hwnd);
                WindowManager.UpdateTaskbarList(hwnd, true);
                WindowManager.StartStreamProofSweep(hwnd);
            }
            else
            {
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
                NativeMethods.SetForegroundWindow(hwnd);
            }
        }
    }

    public static void Clear()
    {
        Console.BackgroundColor = ConsoleColor.Black;
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write("\x1b[?25l\x1b[40m\x1b[2J\x1b[H");
    }
}
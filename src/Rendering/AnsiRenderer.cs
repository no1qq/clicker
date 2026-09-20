using System;
using System.Text;
using System.Threading;

namespace zolsi.cc;

internal readonly record struct Rgb(int R, int G, int B);

internal static class AnsiRenderer
{
    private static readonly Rgb GradientStart = new(138, 43, 226);
    private static readonly Rgb GradientEnd = new(0, 229, 255);

    public static Rgb Lerp(Rgb a, Rgb b, double t)
    {
        double clamped = Math.Clamp(t, 0.0, 1.0);
        int r = (int)(a.R + (b.R - a.R) * clamped);
        int g = (int)(a.G + (b.G - a.G) * clamped);
        int bVal = (int)(a.B + (b.B - a.B) * clamped);
        return new Rgb(r, g, bVal);
    }

    public static Rgb Scale(Rgb c, double factor)
    {
        double clamped = Math.Clamp(factor, 0.0, 1.0);
        return new Rgb((int)(c.R * clamped), (int)(c.G * clamped), (int)(c.B * clamped));
    }

    public static string ColorSequence(Rgb c)
    {
        return $"\x1b[38;2;{c.R};{c.G};{c.B}m";
    }

    public static string MoveTo(int x, int y)
    {
        return $"\x1b[{y + 1};{x + 1}H";
    }

    public static void PlayIntro()
    {
        ConsoleWindow.Clear();

        int windowWidth = Math.Max(Console.WindowWidth, 80);
        int windowHeight = Math.Max(Console.WindowHeight, 25);
        int startX = Math.Max(0, (windowWidth - AsciiArt.Width) / 2);
        int startY = Math.Max(0, (windowHeight - AsciiArt.Height) / 2);

        const int steps = 24;
        const int stepDelay = 25;

        for (int i = 1; i <= steps; i++)
        {
            double alpha = (double)i / steps;
            RenderLogoFrame(startX, startY, alpha);
            Thread.Sleep(stepDelay);
        }

        RenderLogoFrame(startX, startY, 1.0);
        Thread.Sleep(3000);

        for (int i = steps; i >= 0; i--)
        {
            double alpha = (double)i / steps;
            RenderLogoFrame(startX, startY, alpha);
            Thread.Sleep(stepDelay);
        }

        ConsoleWindow.Clear();
    }

    private static void RenderLogoFrame(int startX, int startY, double alpha)
    {
        var sb = new StringBuilder(4096);
        for (int r = 0; r < AsciiArt.Height; r++)
        {
            sb.Append(MoveTo(startX, startY + r));
            string line = AsciiArt.Logo[r];
            for (int c = 0; c < line.Length; c++)
            {
                char ch = line[c];
                if (ch == ' ')
                {
                    sb.Append(' ');
                    continue;
                }
                double ratio = (double)c / Math.Max(1, line.Length - 1);
                Rgb baseColor = Lerp(GradientStart, GradientEnd, ratio);
                Rgb faded = Scale(baseColor, alpha);
                sb.Append(ColorSequence(faded));
                sb.Append(ch);
            }
        }
        sb.Append("\x1b[0m");
        Console.Write(sb.ToString());
    }
}
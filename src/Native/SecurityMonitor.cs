using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace zolsi.cc;

internal static class SecurityMonitor
{
    private static int _isPoisoned;
    private static long _poisonTimestamp;
    private static int _alertSent;
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };

    public static bool IsPoisoned => Volatile.Read(ref _isPoisoned) == 1;

    public static void TriggerSecurityViolation(string reason, string? detail = null)
    {
        Interlocked.Exchange(ref _isPoisoned, 1);
        if (Interlocked.CompareExchange(ref _alertSent, 1, 0) == 0)
        {
            _poisonTimestamp = Stopwatch.GetTimestamp();
            string hwid = HwidHelper.GetHwid();
            string user = AccountSession.Current?.Username ?? "unauthenticated";
            string procPath = Environment.ProcessPath ?? "unknown";
            string procName = Path.GetFileName(procPath);

            Task.Run(async () =>
            {
                try
                {
                    string webhookUrl = StringEncryptor.GetDiscordWebhookUrl();
                    if (string.IsNullOrWhiteSpace(webhookUrl))
                    {
                        return;
                    }

                    string timeStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
                    string safeReason = EscapeJson(reason + (string.IsNullOrEmpty(detail) ? "" : $" ({detail})"));
                    string safeHwid = EscapeJson(hwid);
                    string safeUser = EscapeJson(user);
                    string safeProc = EscapeJson($"{procName} [{procPath}]");

                    string payload = "{\"embeds\":[{\"title\":\"Security Violation Detected\",\"color\":15548997,\"fields\":[" +
                        "{\"name\":\"Reason\",\"value\":\"" + safeReason + "\",\"inline\":false}," +
                        "{\"name\":\"HWID\",\"value\":\"" + safeHwid + "\",\"inline\":true}," +
                        "{\"name\":\"Session\",\"value\":\"" + safeUser + "\",\"inline\":true}," +
                        "{\"name\":\"Process\",\"value\":\"" + safeProc + "\",\"inline\":false}," +
                        "{\"name\":\"Timestamp\",\"value\":\"" + timeStr + "\",\"inline\":true}" +
                        "],\"footer\":{\"text\":\"zolsi.cc security watchdog\"}}]}";

                    using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                    await _httpClient.PostAsync(webhookUrl, content).ConfigureAwait(false);
                }
                catch
                {
                }
            });
        }
    }

    public static void AuditTiming(long startTimestamp, long maxAllowedMicroseconds, string routine)
    {
        long elapsed = (Stopwatch.GetTimestamp() - startTimestamp) * 1_000_000 / Stopwatch.Frequency;
        if (elapsed > maxAllowedMicroseconds)
        {
            TriggerSecurityViolation("Debugger Stepping Delay Anomaly", $"{routine}: {elapsed}us > {maxAllowedMicroseconds}us");
        }
    }

    public static bool ShouldDegradeClick(out int addedDelayMs)
    {
        addedDelayMs = 0;
        if (!IsPoisoned)
        {
            return false;
        }

        if (_poisonTimestamp > 0)
        {
            long elapsedSec = (Stopwatch.GetTimestamp() - _poisonTimestamp) / Stopwatch.Frequency;
            if (elapsedSec > 75)
            {
                Environment.Exit(0);
            }
        }

        if (Random.Shared.Next(100) < 55)
        {
            return true;
        }

        addedDelayMs = Random.Shared.Next(120, 500);
        return false;
    }

    private static string EscapeJson(string str)
    {
        var sb = new StringBuilder(str.Length + 8);
        foreach (char c in str)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ')
                    {
                        sb.Append($"\\u{(int)c:x4}");
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        return sb.ToString();
    }
}
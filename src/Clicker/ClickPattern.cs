using System;
using System.Collections.Generic;
using System.Text.Json;

namespace zolsi.cc;

internal readonly record struct ClickTiming(int HoldMs, int GapMs, int JitterX = 0, int JitterY = 0);

internal sealed class ClickPattern
{
    public List<ClickTiming> Clicks { get; }
    public double Cps { get; }

    public ClickPattern(List<ClickTiming> clicks, double cps)
    {
        Clicks = clicks;
        Cps = cps;
    }

    public string ToEncryptedPayload()
    {
        var rawList = new List<int[]>(Clicks.Count);
        for (int i = 0; i < Clicks.Count; i++)
        {
            rawList.Add([Clicks[i].HoldMs, Clicks[i].GapMs, Clicks[i].JitterX, Clicks[i].JitterY]);
        }

        string json = JsonSerializer.Serialize(rawList, SupabaseJsonContext.Default.ListInt32Array);
        return StringEncryptor.EncryptWithPin(json, AuthManager.LastVerifiedPin);
    }

    public static ClickPattern? FromEncryptedPayload(string payload, double cps)
    {
        try
        {
            string json = StringEncryptor.DecryptWithPin(payload, AuthManager.LastVerifiedPin);
            var rawList = JsonSerializer.Deserialize(json, SupabaseJsonContext.Default.ListInt32Array);
            if (rawList == null || rawList.Count == 0)
            {
                return null;
            }

            var timings = new List<ClickTiming>(rawList.Count);
            for (int i = 0; i < rawList.Count; i++)
            {
                int[] item = rawList[i];
                int hold = item.Length > 0 ? Math.Max(1, item[0]) : 30;
                int gap = item.Length > 1 ? Math.Max(1, item[1]) : 40;
                int jx = item.Length > 2 ? item[2] : 0;
                int jy = item.Length > 3 ? item[3] : 0;
                timings.Add(new ClickTiming(hold, gap, jx, jy));
            }

            return new ClickPattern(timings, cps);
        }
        catch
        {
            return null;
        }
    }
}
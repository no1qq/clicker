using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace zolsi.cc;

internal static class LockoutManager
{
    private static string GetLockFilePath()
    {
        string tempDir = Path.GetTempPath();
        return Path.Combine(tempDir, ".zl_lockout.bin");
    }

    private static string ComputeHash(long ticks)
    {
        byte[] data = Encoding.UTF8.GetBytes($"{ticks}_zolsi_security_lock");
        byte[] hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool IsLockedOut()
    {
        try
        {
            string path = GetLockFilePath();
            if (!File.Exists(path))
            {
                return false;
            }

            string raw = File.ReadAllText(path).Trim();
            string[] parts = raw.Split(':');
            if (parts.Length == 2 && long.TryParse(parts[0], out long expiryTicks))
            {
                string expectedHash = ComputeHash(expiryTicks);
                if (string.Equals(parts[1], expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    if (DateTime.UtcNow.Ticks < expiryTicks)
                    {
                        return true;
                    }
                }
            }

            File.Delete(path);
        }
        catch
        {
        }

        return false;
    }

    public static void EngageLockout(int seconds = 30)
    {
        try
        {
            string path = GetLockFilePath();
            long expiryTicks = DateTime.UtcNow.AddSeconds(seconds).Ticks;
            string hash = ComputeHash(expiryTicks);
            File.WriteAllText(path, $"{expiryTicks}:{hash}");
        }
        catch
        {
        }
    }
}
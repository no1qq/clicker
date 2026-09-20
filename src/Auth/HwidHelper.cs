using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace zolsi.cc;

internal static class HwidHelper
{
    private static string? _cachedHwid;

    public static string GetHwid()
    {
        if (!string.IsNullOrEmpty(_cachedHwid))
        {
            return _cachedHwid;
        }

        string machineGuid = "";
        string productId = "";

        try
        {
            using var cryptoKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            machineGuid = cryptoKey?.GetValue("MachineGuid")?.ToString() ?? "";
        }
        catch
        {
        }

        try
        {
            using var currentVersionKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            productId = currentVersionKey?.GetValue("ProductId")?.ToString() ?? "";
        }
        catch
        {
        }

        string rawHwid = $"{machineGuid}_{productId}_{Environment.MachineName}_{Environment.ProcessorCount}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawHwid));
        string hex = Convert.ToHexString(hash);
        _cachedHwid = $"ZOLSI-{hex[..24]}";
        return _cachedHwid;
    }
}
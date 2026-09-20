using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace zolsi.cc;

internal sealed class AccountRow
{
    public Guid id { get; set; }
    public string username { get; set; } = "";
    public string pin_hash { get; set; } = "";
    public string? hwid { get; set; }
    public string? config { get; set; }
}

internal sealed class ClickRecordRow
{
    public Guid id { get; set; }
    public Guid user_id { get; set; }
    public string name { get; set; } = "";
    public string data { get; set; } = "";
    public double cps { get; set; }
    public DateTime created_at { get; set; }
}

internal sealed class BlacklistedBuildRow
{
    public string hash { get; set; } = "";
}

internal static class SupabaseClient
{
    private static readonly HttpClient Http = CreateSecureClient();

    private static HttpClient CreateSecureClient()
    {
        var handler = new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
        {
            if (cert == null)
            {
                SecurityMonitor.TriggerSecurityViolation("TLS Handshake Missing Certificate");
                return false;
            }

            string issuer = cert.Issuer.ToLowerInvariant();
            if (issuer.Contains("fiddler") || issuer.Contains("charles") || issuer.Contains("mitmproxy") ||
                issuer.Contains("portswigger") || issuer.Contains("burp") || issuer.Contains("http toolkit") ||
                issuer.Contains("do_not_trust"))
            {
                SecurityMonitor.TriggerSecurityViolation("Man-in-the-Middle TLS Proxy Interception", cert.Issuer);
                return false;
            }

            if (errors == System.Net.Security.SslPolicyErrors.None)
            {
                return true;
            }

            SecurityMonitor.TriggerSecurityViolation("Invalid SSL/TLS Certificate on Supabase Endpoint");
            return false;
        };

        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url, string? jsonBody = null)
    {
        var req = new HttpRequestMessage(method, url);
        string secret = StringEncryptor.GetSecretKey();
        if (SecurityMonitor.IsPoisoned)
        {
            secret = "corrupted_token_" + Guid.NewGuid().ToString("N");
        }
        req.Headers.Add(StringEncryptor.GetApiKeyHeader(), secret);
        req.Headers.Add(StringEncryptor.GetAuthHeader(), $"{StringEncryptor.GetBearerPrefix()}{secret}");

        if (jsonBody != null)
        {
            req.Content = new StringContent(jsonBody, Encoding.UTF8, StringEncryptor.GetAppJson());
        }

        return req;
    }

    public static async Task<(AccountRow? Account, string? Error)> FindAccountByPinHashAsync(string pinHash)
    {
        try
        {
            string url = $"{StringEncryptor.GetAccountsUrl()}{StringEncryptor.GetQAccountByPin()}{pinHash}{StringEncryptor.GetQAccountSelect()}";
            using var req = CreateRequest(HttpMethod.Get, url);
            using var resp = await Http.SendAsync(req).ConfigureAwait(false);
            string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                if (json.Contains("permission denied", StringComparison.OrdinalIgnoreCase))
                {
                    return (null, "database permission denied - re-run schema.sql");
                }
                return (null, $"database error ({(int)resp.StatusCode})");
            }

            var list = JsonSerializer.Deserialize(json, SupabaseJsonContext.Default.ListAccountRow);
            return (list != null && list.Count > 0 ? list[0] : null, null);
        }
        catch
        {
            return (null, "connection failed - check network");
        }
    }

    public static async Task<bool> BindHwidAsync(Guid accountId, string hwid)
    {
        try
        {
            string url = $"{StringEncryptor.GetAccountsUrl()}{StringEncryptor.GetQIdEq()}{accountId}";
            string body = JsonSerializer.Serialize(new HwidUpdatePayload(hwid), SupabaseJsonContext.Default.HwidUpdatePayload);
            using var req = CreateRequest(HttpMethod.Patch, url, body);
            using var resp = await Http.SendAsync(req).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<List<ClickRecordRow>> GetRecordsAsync(Guid userId)
    {
        try
        {
            string url = $"{StringEncryptor.GetRecordsUrl()}{StringEncryptor.GetQUserIdEq()}{userId}{StringEncryptor.GetQRecordsSelect()}";
            using var req = CreateRequest(HttpMethod.Get, url);
            using var resp = await Http.SendAsync(req).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return [];
            }

            string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            var list = JsonSerializer.Deserialize(json, SupabaseJsonContext.Default.ListClickRecordRow);
            if (list == null)
            {
                return [];
            }

            return list.FindAll(r => !r.name.StartsWith("__", StringComparison.Ordinal));
        }
        catch
        {
            return [];
        }
    }

    public static async Task<bool> UpdateAccountConfigAsync(Guid accountId, string config)
    {
        try
        {
            string url = $"{StringEncryptor.GetAccountsUrl()}{StringEncryptor.GetQIdEq()}{accountId}";
            string body = JsonSerializer.Serialize(new ConfigUpdatePayload(config), SupabaseJsonContext.Default.ConfigUpdatePayload);
            using var req = CreateRequest(HttpMethod.Patch, url, body);
            using var resp = await Http.SendAsync(req).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<string?> GetAccountConfigAsync(Guid accountId)
    {
        try
        {
            string url = $"{StringEncryptor.GetAccountsUrl()}{StringEncryptor.GetQIdEq()}{accountId}{StringEncryptor.GetQConfigSelect()}";
            using var req = CreateRequest(HttpMethod.Get, url);
            using var resp = await Http.SendAsync(req).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return null;
            }

            string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            var list = JsonSerializer.Deserialize(json, SupabaseJsonContext.Default.ListAccountRow);
            return list != null && list.Count > 0 ? list[0].config : null;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<ClickRecordRow?> CreateRecordAsync(Guid userId, string name, string data, double cps)
    {
        try
        {
            string url = StringEncryptor.GetRecordsUrl();
            string body = JsonSerializer.Serialize(new CreateRecordPayload(userId, name, data, cps), SupabaseJsonContext.Default.CreateRecordPayload);

            using var req = CreateRequest(HttpMethod.Post, url, body);
            req.Headers.Add(StringEncryptor.GetPreferHeaderKey(), StringEncryptor.GetPreferHeader());
            using var resp = await Http.SendAsync(req).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return null;
            }

            string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            var list = JsonSerializer.Deserialize(json, SupabaseJsonContext.Default.ListClickRecordRow);
            return list != null && list.Count > 0 ? list[0] : null;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<bool> RenameRecordAsync(Guid recordId, string newName)
    {
        try
        {
            string url = $"{StringEncryptor.GetRecordsUrl()}{StringEncryptor.GetQIdEq()}{recordId}";
            string body = JsonSerializer.Serialize(new RenameRecordPayload(newName), SupabaseJsonContext.Default.RenameRecordPayload);
            using var req = CreateRequest(HttpMethod.Patch, url, body);
            using var resp = await Http.SendAsync(req).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> DeleteRecordAsync(Guid recordId)
    {
        try
        {
            string url = $"{StringEncryptor.GetRecordsUrl()}{StringEncryptor.GetQIdEq()}{recordId}";
            using var req = CreateRequest(HttpMethod.Delete, url);
            using var resp = await Http.SendAsync(req).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> IsBuildBlacklistedAsync(string hash)
    {
        try
        {
            string url = $"{StringEncryptor.GetBlacklistUrl()}{StringEncryptor.GetQHashEq()}{hash}{StringEncryptor.GetQHashSelect()}";
            using var req = CreateRequest(HttpMethod.Get, url);
            using var resp = await Http.SendAsync(req).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return false;
            }

            string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            var list = JsonSerializer.Deserialize(json, SupabaseJsonContext.Default.ListBlacklistedBuildRow);
            return list != null && list.Count > 0;
        }
        catch
        {
            return false;
        }
    }
}
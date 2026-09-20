using System;

namespace zolsi.cc;

internal sealed class AccountSession
{
    public static AccountSession? Current { get; set; }

    public Guid UserId { get; }
    public string Username { get; }
    public string Hwid { get; }
    public string? InitialConfig { get; }

    public AccountSession(Guid userId, string username, string hwid, string? initialConfig = null)
    {
        UserId = userId;
        Username = username;
        Hwid = hwid;
        InitialConfig = initialConfig;
    }
}
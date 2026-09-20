using System;
using System.Threading.Tasks;

namespace zolsi.cc;

internal static class AuthManager
{
    public static string? LastVerifiedPin { get; private set; }

    public static async Task<(bool Success, string Message)> AuthenticatePinAsync(string pin)
    {
        string pinHash = StringEncryptor.HashPin(pin);
        var (account, error) = await SupabaseClient.FindAccountByPinHashAsync(pinHash).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(error))
        {
            return (false, error);
        }

        if (account == null)
        {
            return (false, "invalid pin or account not found");
        }

        string clientHwid = HwidHelper.GetHwid();

        if (string.IsNullOrEmpty(account.hwid))
        {
            bool bound = await SupabaseClient.BindHwidAsync(account.id, clientHwid).ConfigureAwait(false);
            if (!bound)
            {
                return (false, "failed to bind hwid to account");
            }

            LastVerifiedPin = pin;
            AccountSession.Current = new AccountSession(account.id, account.username, clientHwid, account.config);
            return (true, "authenticated");
        }

        if (string.Equals(account.hwid, clientHwid, StringComparison.OrdinalIgnoreCase))
        {
            LastVerifiedPin = pin;
            AccountSession.Current = new AccountSession(account.id, account.username, clientHwid, account.config);
            return (true, "authenticated");
        }

        return (false, "hwid mismatch - account locked");
    }
}
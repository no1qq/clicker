using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace zolsi.cc;

internal sealed class UserProfile
{
    public int ToggleBindKey { get; set; }
    public bool DisableInMenu { get; set; } = true;
    public bool UseJitter { get; set; }
    public string? ActivePatternName { get; set; }
    public bool StreamProof { get; set; } = true;
    public int BringToFrontBindKey { get; set; } = 0x2D;
    public bool AlwaysOnTop { get; set; }
}

internal static class UserProfileManager
{
    public static UserProfile CurrentProfile { get; private set; } = new();

    public static void LoadFromConfig(string? rawConfig)
    {
        if (string.IsNullOrEmpty(rawConfig))
        {
            CurrentProfile = new UserProfile();
            ApplyProfileToPlayer();
            return;
        }

        try
        {
            string json = StringEncryptor.DecryptWithPin(rawConfig, AuthManager.LastVerifiedPin);
            var loaded = JsonSerializer.Deserialize(json, SupabaseJsonContext.Default.UserProfile);
            CurrentProfile = loaded ?? new UserProfile();
        }
        catch
        {
            CurrentProfile = new UserProfile();
        }

        ApplyProfileToPlayer();
    }

    public static async Task LoadProfileAsync(Guid userId)
    {
        string? config = await SupabaseClient.GetAccountConfigAsync(userId).ConfigureAwait(false);
        LoadFromConfig(config);
    }

    public static void SaveCurrentProfile()
    {
        if (AccountSession.Current == null)
        {
            return;
        }

        Guid userId = AccountSession.Current.UserId;
        CurrentProfile.ToggleBindKey = ClickPlayer.ToggleBindKey;
        CurrentProfile.DisableInMenu = ClickPlayer.DisableInMenu;
        CurrentProfile.UseJitter = ClickPlayer.UseJitter;
        CurrentProfile.ActivePatternName = ClickPlayer.ActivePatternName != "none" ? ClickPlayer.ActivePatternName : null;

        string json = JsonSerializer.Serialize(CurrentProfile, SupabaseJsonContext.Default.UserProfile);
        string encrypted = StringEncryptor.EncryptWithPin(json, AuthManager.LastVerifiedPin);

        Task.Run(async () =>
        {
            try
            {
                await SupabaseClient.UpdateAccountConfigAsync(userId, encrypted).ConfigureAwait(false);
            }
            catch
            {
            }
        });
    }

    private static void ApplyProfileToPlayer()
    {
        ClickPlayer.ToggleBindKey = CurrentProfile.ToggleBindKey;
        ClickPlayer.DisableInMenu = CurrentProfile.DisableInMenu;
        ClickPlayer.UseJitter = CurrentProfile.UseJitter;
        ClickPlayer.ActivePatternName = !string.IsNullOrEmpty(CurrentProfile.ActivePatternName)
            ? CurrentProfile.ActivePatternName
            : "none";
        WindowManager.ApplyStreamProof(CurrentProfile.StreamProof);
        WindowManager.ApplyAlwaysOnTop(CurrentProfile.AlwaysOnTop);
    }
}
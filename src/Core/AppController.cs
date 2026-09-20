using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace zolsi.cc;

internal enum AppPage
{
    PinInput,
    Menu,
    Clicker,
    Record,
    Settings
}

internal static class AppController
{
    private static AppPage _currentPage = AppPage.PinInput;
    private static readonly StringBuilder _pinBuffer = new(8);
    private static string _authStatus = "";
    private static bool _isAuthenticating;
    private static int _failedPinAttempts;

    private static List<ClickRecordRow> _records = [];
    private static bool _isLoadingRecords;
    private static bool _isListeningForBind;
    private static bool _isListeningForBringToFrontBind;

    private static bool _isRenaming;
    private static Guid _renamingRecordId;
    private static readonly StringBuilder _renameBuffer = new(32);

    private static readonly ClickRecorder _recorder = new();
    private static bool _recordFinished;
    private static ClickPattern? _recordedPattern;
    private static double _recordDurationSec;
    private static readonly StringBuilder _recordNameBuffer = new(32);
    private static string _recordSummary = "";
    private static double _lastLiveCps = -1;
    private static int _multiplierIndex;
    private static readonly double[] Multipliers = [1.00, 1.25, 1.50, 1.75, 2.00];
    private static readonly List<(int X, int Y)> _recorderDots = [];

    private static int _lastHover = -1;

    public static void Run(string[] args)
    {
        if (LockoutManager.IsLockedOut())
        {
            Environment.Exit(0);
            return;
        }

        AntiDebug.PerformStartupCheck();
        AntiDebug.StartWatchdog();

        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            exePath = Process.GetCurrentProcess().MainModule?.FileName;
        }

        if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
        {
            string currentHash = StringEncryptor.HashFile(exePath);
            var isBlacklistedTask = Task.Run(async () => await SupabaseClient.IsBuildBlacklistedAsync(currentHash).ConfigureAwait(false));
            if (isBlacklistedTask.GetAwaiter().GetResult())
            {
                SecurityMonitor.TriggerSecurityViolation("Execution of Blacklisted Binary Hash");
                Environment.Exit(0);
                return;
            }
        }

        bool? streamProofArg = null;
        string? autoPin = null;
        string? targetPageName = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--streamproof", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (bool.TryParse(args[i + 1], out bool sp))
                {
                    streamProofArg = sp;
                }
                i++;
            }
            else if (args[i].Equals("--pin", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                autoPin = args[i + 1];
                i++;
            }
            else if (args[i].Equals("--page", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                targetPageName = args[i + 1];
                i++;
            }
        }

        bool streamProof = streamProofArg ?? true;
        ConsoleWindow.Initialize(streamProof);
        ClickPlayer.Start();

        ClickPlayer.OnStateChanged = () =>
        {
            if (_currentPage == AppPage.Clicker)
            {
                RenderCurrentPage();
            }
        };

        if (!string.IsNullOrEmpty(autoPin))
        {
            var authTask = Task.Run(async () => await AuthManager.AuthenticatePinAsync(autoPin).ConfigureAwait(false));
            var authResult = authTask.GetAwaiter().GetResult();
            if (authResult.Success && AccountSession.Current != null)
            {
                UserProfileManager.LoadFromConfig(AccountSession.Current.InitialConfig);
                if (streamProofArg.HasValue)
                {
                    UserProfileManager.CurrentProfile.StreamProof = streamProofArg.Value;
                    WindowManager.ApplyStreamProof(streamProofArg.Value);
                }
                _ = LoadCloudRecordsAsync();

                AppPage dest = AppPage.Menu;
                if (string.Equals(targetPageName, "options", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(targetPageName, "settings", StringComparison.OrdinalIgnoreCase))
                {
                    dest = AppPage.Settings;
                }
                else if (string.Equals(targetPageName, "clicker", StringComparison.OrdinalIgnoreCase))
                {
                    dest = AppPage.Clicker;
                }

                _currentPage = dest;
                RenderCurrentPage();
            }
            else
            {
                _currentPage = AppPage.PinInput;
                RenderCurrentPage();
            }
        }
        else
        {
            AnsiRenderer.PlayIntro();
            _currentPage = AppPage.PinInput;
            RenderCurrentPage();
        }

        while (true)
        {
            foreach (var ev in InputManager.WaitForEvents())
            {
                HandleEvent(ev);
            }
        }
    }

    private static void HandleEvent(AppInputEvent ev)
    {
        switch (ev.Type)
        {
            case InputEventType.MouseMove:
                HandleMouseMove(ev.X, ev.Y);
                break;

            case InputEventType.LeftClick:
                HandleLeftClick(ev.X, ev.Y);
                break;

            case InputEventType.LeftUp:
                HandleLeftUp(ev.X, ev.Y);
                break;

            case InputEventType.RightClick:
                HandleRightClick(ev.X, ev.Y);
                break;

            case InputEventType.KeyDown:
                HandleKeyDown(ev.Character, ev.VirtualKey, ev.ControlState);
                break;

            case InputEventType.WindowResize:
                RenderCurrentPage();
                break;

            case InputEventType.Tick:
                HandleTick();
                break;
        }
    }

    private static void HandleTick()
    {
        if (_currentPage == AppPage.Record && _recorder.IsRecording)
        {
            if (_recorder.ShouldAutoFinish())
            {
                FinishRecord();
                return;
            }

            double cps = Math.Round(_recorder.GetLiveCps() * Multipliers[_multiplierIndex], 1);
            if (Math.Abs(cps - _lastLiveCps) > 0.05)
            {
                _lastLiveCps = cps;
                RenderCurrentPage();
            }
        }
    }

    private static void HandleMouseMove(int x, int y)
    {
        if (_currentPage == AppPage.Record && _recorder.IsRecording && _recorder.ShouldAutoFinish())
        {
            FinishRecord();
            return;
        }

        int hover = GetHoverTarget(x, y);
        if (hover != _lastHover)
        {
            _lastHover = hover;
            RenderCurrentPage();
        }
    }

    private static int GetHoverTarget(int x, int y)
    {
        int winW = Math.Max(Console.WindowWidth, 80);
        int winH = Math.Max(Console.WindowHeight, 25);
        int centerX = winW / 2;
        int centerY = winH / 2;

        if (_currentPage != AppPage.Menu && _currentPage != AppPage.PinInput && x >= 1 && x <= 3 && y == 1)
        {
            return 1;
        }

        if (_currentPage == AppPage.Menu)
        {
            int clickerX = centerX - 5;
            int clickerY = centerY - 2;
            if (x >= clickerX && x < clickerX + 11 && y == clickerY)
            {
                return 2;
            }

            int optionsX = centerX - 5;
            int optionsY = centerY;
            if (x >= optionsX && x < optionsX + 11 && y == optionsY)
            {
                return 4;
            }

            int leaveX = centerX - 4;
            int leaveY = centerY + 2;
            if (x >= leaveX && x < leaveX + 9 && y == leaveY)
            {
                return 3;
            }
        }
        else if (_currentPage == AppPage.Settings)
        {
            if (y == 4 && x >= 25 && x <= 36)
            {
                return 20;
            }
            if (y == 6 && x >= 25 && x <= 46)
            {
                return 21;
            }
            if (y == 8 && x >= 25 && x <= 36)
            {
                return 22;
            }
        }
        else if (_currentPage == AppPage.Clicker)
        {
            if (y == 4 && x >= 25 && x <= 36)
            {
                return 10;
            }
            if (y == 6 && x >= 25 && x <= 46)
            {
                return 11;
            }
            if (y == 8 && x >= 25 && x <= 36)
            {
                return 14;
            }
            if (y == 10 && x >= 25 && x <= 36)
            {
                return 12;
            }
            if (y == 15 && x >= 5 && x <= 38)
            {
                return 13;
            }

            int recStartY = 18;
            for (int i = 0; i < _records.Count && i < 6; i++)
            {
                int rowY = recStartY + (i * 2);
                if (y == rowY)
                {
                    if (x >= 49 && x <= 59)
                    {
                        return 100 + (i * 3);
                    }
                    if (x >= 62 && x <= 72)
                    {
                        return 100 + (i * 3) + 1;
                    }
                    if (x >= 75 && x <= 85)
                    {
                        return 100 + (i * 3) + 2;
                    }
                }
            }
        }
        else if (_currentPage == AppPage.Record)
        {
            int boxW = 42;
            int boxH = 13;
            int boxX = Math.Max(2, centerX - (boxW / 2));
            int boxY = Math.Max(4, centerY - (boxH / 2) - 2);

            if (x >= boxX && x < boxX + boxW && y >= boxY && y < boxY + boxH)
            {
                return 30;
            }

            int stopY = boxY + boxH + 1;
            if (!_recordFinished && x >= centerX - 10 && x <= centerX + 10 && y == stopY)
            {
                return 31;
            }

            int multY = stopY + 2;
            int multX = centerX - 10;
            if (y == multY && x >= multX - 1 && x <= multX + 22)
            {
                return 34;
            }

            if (_recordFinished)
            {
                int promptY = multY + 2;
                int btnY = promptY + 2;
                if (x >= centerX - 16 && x <= centerX && y == btnY)
                {
                    return 32;
                }
                if (x >= centerX + 5 && x <= centerX + 15 && y == btnY)
                {
                    return 33;
                }
            }
        }

        return 0;
    }

    private static void HandleLeftClick(int x, int y)
    {
        int hover = GetHoverTarget(x, y);

        if (hover == 1)
        {
            NavigateBack();
            return;
        }

        if (_currentPage == AppPage.Menu)
        {
            if (hover == 2)
            {
                SwitchPage(AppPage.Clicker);
            }
            else if (hover == 4)
            {
                SwitchPage(AppPage.Settings);
            }
            else if (hover == 3)
            {
                Environment.Exit(0);
            }
        }
        else if (_currentPage == AppPage.Settings)
        {
            if (hover == 20)
            {
                bool newStreamProof = !UserProfileManager.CurrentProfile.StreamProof;
                UserProfileManager.CurrentProfile.StreamProof = newStreamProof;
                WindowManager.ApplyStreamProof(newStreamProof);
                UserProfileManager.SaveCurrentProfile();
                RenderCurrentPage();
            }
            else if (hover == 21)
            {
                _isListeningForBringToFrontBind = true;
                RenderCurrentPage();
            }
            else if (hover == 22)
            {
                UserProfileManager.CurrentProfile.AlwaysOnTop = !UserProfileManager.CurrentProfile.AlwaysOnTop;
                WindowManager.ApplyAlwaysOnTop(UserProfileManager.CurrentProfile.AlwaysOnTop);
                UserProfileManager.SaveCurrentProfile();
                RenderCurrentPage();
            }
        }
        else if (_currentPage == AppPage.Clicker)
        {
            if (hover == 10)
            {
                ClickPlayer.IsToggled = !ClickPlayer.IsToggled;
                RenderCurrentPage();
            }
            else if (hover == 11)
            {
                _isListeningForBind = true;
                RenderCurrentPage();
            }
            else if (hover == 14)
            {
                ClickPlayer.UseJitter = !ClickPlayer.UseJitter;
                UserProfileManager.SaveCurrentProfile();
                RenderCurrentPage();
            }
            else if (hover == 12)
            {
                ClickPlayer.DisableInMenu = !ClickPlayer.DisableInMenu;
                UserProfileManager.SaveCurrentProfile();
                RenderCurrentPage();
            }
            else if (hover == 13)
            {
                StartNewRecord();
            }
            else if (hover >= 100)
            {
                int rel = hover - 100;
                int recordIndex = rel / 3;
                int actionType = rel % 3;

                if (recordIndex < _records.Count)
                {
                    var rec = _records[recordIndex];
                    if (actionType == 0)
                    {
                        SelectRecord(rec);
                    }
                    else if (actionType == 1)
                    {
                        _isRenaming = true;
                        _renamingRecordId = rec.id;
                        _renameBuffer.Clear();
                        _renameBuffer.Append(rec.name);
                        RenderCurrentPage();
                    }
                    else if (actionType == 2)
                    {
                        DeleteRecord(rec.id);
                    }
                }
            }
        }
        else if (_currentPage == AppPage.Record)
        {
            if (hover == 30)
            {
                int winW = Math.Max(Console.WindowWidth, 80);
                int winH = Math.Max(Console.WindowHeight, 25);
                int centerX = winW / 2;
                int centerY = winH / 2;
                int boxW = 42;
                int boxH = 13;
                int boxX = Math.Max(2, centerX - (boxW / 2));
                int boxY = Math.Max(4, centerY - (boxH / 2) - 2);

                int relX = Math.Clamp(x - (boxX + 1), 0, boxW - 3);
                int relY = Math.Clamp(y - (boxY + 1), 0, boxH - 3);
                _recorderDots.Add((relX, relY));

                if (!_recorder.IsRecording && !_recordFinished)
                {
                    _recorder.Start();
                }

                _recorder.OnMouseDown(relX, relY);
                RenderCurrentPage();
            }
            else if (hover == 31)
            {
                FinishRecord();
            }
            else if (hover == 32)
            {
                SaveRecordedPattern();
            }
            else if (hover == 33)
            {
                StartNewRecord();
            }
            else if (hover == 34)
            {
                _multiplierIndex = (_multiplierIndex + 1) % Multipliers.Length;
                UpdateRecordSummary();
                RenderCurrentPage();
            }
        }
    }

    private static void HandleLeftUp(int x, int y)
    {
        if (_currentPage == AppPage.Record && _recorder.IsRecording)
        {
            _recorder.OnMouseUp();
            RenderCurrentPage();
        }
    }

    private static void HandleRightClick(int x, int y)
    {
        if (_currentPage == AppPage.PinInput)
        {
            PasteClipboard();
            return;
        }

        if (_currentPage == AppPage.Record)
        {
            int hover = GetHoverTarget(x, y);
            if (hover == 34)
            {
                _multiplierIndex = 0;
                UpdateRecordSummary();
                RenderCurrentPage();
            }
        }
    }

    private static void HandleKeyDown(char ch, ushort vk, uint ctrl)
    {
        if (_isListeningForBind)
        {
            if (vk == 0x1B || vk == 0x08)
            {
                ClickPlayer.ToggleBindKey = 0;
            }
            else
            {
                ClickPlayer.ToggleBindKey = vk;
            }
            _isListeningForBind = false;
            UserProfileManager.SaveCurrentProfile();
            RenderCurrentPage();
            return;
        }

        if (_isListeningForBringToFrontBind)
        {
            if (vk == 0x1B || vk == 0x08)
            {
                UserProfileManager.CurrentProfile.BringToFrontBindKey = 0;
            }
            else
            {
                UserProfileManager.CurrentProfile.BringToFrontBindKey = vk;
            }
            _isListeningForBringToFrontBind = false;
            UserProfileManager.SaveCurrentProfile();
            RenderCurrentPage();
            return;
        }

        if (_currentPage == AppPage.PinInput)
        {
            if ((vk == 'V' || vk == 'v') && ((ctrl & 0x0008) != 0 || (ctrl & 0x0004) != 0))
            {
                PasteClipboard();
                return;
            }

            if (vk == 0x08 || ch == '\b')
            {
                if (_pinBuffer.Length > 0)
                {
                    _pinBuffer.Remove(_pinBuffer.Length - 1, 1);
                    _authStatus = "";
                    RenderCurrentPage();
                }
                return;
            }

            if (vk == 0x1B)
            {
                Environment.Exit(0);
                return;
            }

            if (ch >= '0' && ch <= '9' && _pinBuffer.Length < 6)
            {
                _pinBuffer.Append(ch);
                _authStatus = "";
                RenderCurrentPage();

                if (_pinBuffer.Length == 6)
                {
                    VerifyPin();
                }
            }
        }
        else if (_currentPage == AppPage.Menu)
        {
            if (vk == 'C' || vk == 'c')
            {
                SwitchPage(AppPage.Clicker);
            }
            else if (vk == 'O' || vk == 'o' || vk == 'S' || vk == 's')
            {
                SwitchPage(AppPage.Settings);
            }
            else if (vk == 'L' || vk == 'l' || vk == 'Q' || vk == 'q' || vk == 0x1B)
            {
                Environment.Exit(0);
            }
        }
        else if (_currentPage == AppPage.Settings)
        {
            if (vk == 0x1B || vk == 0x08)
            {
                NavigateBack();
            }
        }
        else if (_currentPage == AppPage.Clicker)
        {
            if (_isRenaming)
            {
                if (vk == 0x1B)
                {
                    _isRenaming = false;
                    RenderCurrentPage();
                    return;
                }

                if (vk == 0x0D)
                {
                    SubmitRename();
                    return;
                }

                if (vk == 0x08 || ch == '\b')
                {
                    if (_renameBuffer.Length > 0)
                    {
                        _renameBuffer.Remove(_renameBuffer.Length - 1, 1);
                        RenderCurrentPage();
                    }
                    return;
                }

                if (ch >= 32 && ch <= 126 && _renameBuffer.Length < 20)
                {
                    _renameBuffer.Append(ch);
                    RenderCurrentPage();
                }
                return;
            }

            if (vk == 0x1B || vk == 0x08)
            {
                NavigateBack();
            }
        }
        else if (_currentPage == AppPage.Record)
        {
            if (_recordFinished)
            {
                if (vk == 0x1B)
                {
                    SwitchPage(AppPage.Clicker);
                    return;
                }

                if (vk == 0x0D)
                {
                    SaveRecordedPattern();
                    return;
                }

                if (vk == 0x08 || ch == '\b')
                {
                    if (_recordNameBuffer.Length > 0)
                    {
                        _recordNameBuffer.Remove(_recordNameBuffer.Length - 1, 1);
                        RenderCurrentPage();
                    }
                    return;
                }

                if (ch >= 32 && ch <= 126 && _recordNameBuffer.Length < 20)
                {
                    _recordNameBuffer.Append(ch);
                    RenderCurrentPage();
                }
            }
            else
            {
                if (vk == 0x1B || vk == 0x0D || vk == 0x20)
                {
                    FinishRecord();
                }
            }
        }
    }

    private static void PasteClipboard()
    {
        string? text = ClipboardHelper.GetText();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        text = text.Trim();
        foreach (char c in text)
        {
            if (c >= '0' && c <= '9')
            {
                _pinBuffer.Append(c);
                if (_pinBuffer.Length >= 6)
                {
                    break;
                }
            }
        }

        RenderCurrentPage();
        if (_pinBuffer.Length == 6)
        {
            VerifyPin();
        }
    }

    private static void VerifyPin()
    {
        if (_isAuthenticating || _pinBuffer.Length < 6)
        {
            return;
        }

        _isAuthenticating = true;
        _authStatus = "authenticating...";
        RenderCurrentPage();

        string pin = _pinBuffer.ToString();

        Task.Run(async () =>
        {
            var result = await AuthManager.AuthenticatePinAsync(pin).ConfigureAwait(false);
            _isAuthenticating = false;

            if (result.Success)
            {
                _failedPinAttempts = 0;
                _pinBuffer.Clear();
                _authStatus = "";
                if (AccountSession.Current != null)
                {
                    UserProfileManager.LoadFromConfig(AccountSession.Current.InitialConfig);
                    _ = LoadCloudRecordsAsync();
                }

                SwitchPage(AppPage.Menu);
            }
            else
            {
                _pinBuffer.Clear();
                _failedPinAttempts++;
                if (_failedPinAttempts >= 3)
                {
                    LockoutManager.EngageLockout(30);
                    SecurityMonitor.TriggerSecurityViolation("PIN Brute Force Lockout (3 Failed Tries)");
                    Environment.Exit(0);
                    return;
                }
                _authStatus = result.Message;
                RenderCurrentPage();
            }
        });
    }

    private static void StartNewRecord()
    {
        _recordFinished = false;
        _multiplierIndex = 0;
        _recordSummary = "";
        _recordedPattern = null;
        _recordDurationSec = 0.0;
        _recorderDots.Clear();
        _recordNameBuffer.Clear();
        _recordNameBuffer.Append($"pattern_{DateTime.Now:HHmm}");
        _lastLiveCps = -1;
        SwitchPage(AppPage.Record);
    }

    private static void FinishRecord()
    {
        if (_recorder.IsRecording)
        {
            var res = _recorder.Finish();
            _recordedPattern = res.Pattern;
            _recordDurationSec = res.DurationSeconds;
            _recordFinished = true;
            UpdateRecordSummary();
            ConsoleWindow.Clear();
            RenderCurrentPage();
        }
        else if (!_recordFinished)
        {
            SwitchPage(AppPage.Clicker);
        }
    }

    private static void UpdateRecordSummary()
    {
        if (_recordedPattern == null)
        {
            return;
        }

        double mult = Multipliers[_multiplierIndex];
        double effCps = Math.Round(_recordedPattern.Cps * mult, 1);
        if (_multiplierIndex == 0)
        {
            _recordSummary = $"recorded {_recordedPattern.Clicks.Count} clicks in {_recordDurationSec:0.0}s (~{effCps:0.0} CPS)";
        }
        else
        {
            _recordSummary = $"recorded {_recordedPattern.Clicks.Count} clicks in {_recordDurationSec:0.0}s (~{effCps:0.0} CPS @ x{mult:0.00})";
        }
    }

    private static void SaveRecordedPattern()
    {
        if (_recordedPattern == null || AccountSession.Current == null)
        {
            SwitchPage(AppPage.Clicker);
            return;
        }

        string name = _recordNameBuffer.ToString().Trim();
        if (string.IsNullOrEmpty(name) || name.StartsWith("__", StringComparison.Ordinal))
        {
            name = "unnamed_pattern";
        }

        double mult = Multipliers[_multiplierIndex];
        ClickPattern finalPattern;
        if (Math.Abs(mult - 1.0) < 0.001)
        {
            finalPattern = _recordedPattern;
        }
        else
        {
            var scaledClicks = new List<ClickTiming>(_recordedPattern.Clicks.Count);
            for (int i = 0; i < _recordedPattern.Clicks.Count; i++)
            {
                var t = _recordedPattern.Clicks[i];
                int hold = Math.Max(1, (int)Math.Round(t.HoldMs / mult));
                int gap = Math.Max(1, (int)Math.Round(t.GapMs / mult));
                scaledClicks.Add(new ClickTiming(hold, gap, t.JitterX, t.JitterY));
            }
            double finalCps = Math.Round(_recordedPattern.Cps * mult, 1);
            finalPattern = new ClickPattern(scaledClicks, finalCps);
        }

        string data = finalPattern.ToEncryptedPayload();
        double cps = finalPattern.Cps;
        Guid userId = AccountSession.Current.UserId;

        Task.Run(async () =>
        {
            var created = await SupabaseClient.CreateRecordAsync(userId, name, data, cps).ConfigureAwait(false);
            if (created != null)
            {
                ClickPlayer.ActivePattern = finalPattern;
                ClickPlayer.ActivePatternName = name;
                UserProfileManager.SaveCurrentProfile();
            }
            await LoadCloudRecordsAsync().ConfigureAwait(false);
            SwitchPage(AppPage.Clicker);
        });
    }

    private static void SelectRecord(ClickRecordRow row)
    {
        var pattern = ClickPattern.FromEncryptedPayload(row.data, row.cps);
        if (pattern != null)
        {
            ClickPlayer.ActivePattern = pattern;
            ClickPlayer.ActivePatternName = row.name;
            UserProfileManager.SaveCurrentProfile();
            RenderCurrentPage();
        }
    }

    private static void DeleteRecord(Guid recordId)
    {
        var target = _records.Find(r => r.id == recordId);
        Task.Run(async () =>
        {
            await SupabaseClient.DeleteRecordAsync(recordId).ConfigureAwait(false);
            if (target != null && string.Equals(ClickPlayer.ActivePatternName, target.name, StringComparison.OrdinalIgnoreCase))
            {
                ClickPlayer.ActivePattern = null;
                ClickPlayer.ActivePatternName = "none";
                UserProfileManager.SaveCurrentProfile();
            }
            await LoadCloudRecordsAsync().ConfigureAwait(false);
            RenderCurrentPage();
        });
    }

    private static void SubmitRename()
    {
        string newName = _renameBuffer.ToString().Trim();
        if (string.IsNullOrEmpty(newName) || newName.StartsWith("__", StringComparison.Ordinal))
        {
            _isRenaming = false;
            RenderCurrentPage();
            return;
        }

        Guid targetId = _renamingRecordId;
        var oldRec = _records.Find(r => r.id == targetId);
        _isRenaming = false;

        Task.Run(async () =>
        {
            await SupabaseClient.RenameRecordAsync(targetId, newName).ConfigureAwait(false);
            if (oldRec != null && string.Equals(ClickPlayer.ActivePatternName, oldRec.name, StringComparison.OrdinalIgnoreCase))
            {
                ClickPlayer.ActivePatternName = newName;
                UserProfileManager.SaveCurrentProfile();
            }
            await LoadCloudRecordsAsync().ConfigureAwait(false);
            RenderCurrentPage();
        });
    }

    private static async Task LoadCloudRecordsAsync()
    {
        if (AccountSession.Current == null)
        {
            return;
        }

        _isLoadingRecords = true;
        _records = await SupabaseClient.GetRecordsAsync(AccountSession.Current.UserId).ConfigureAwait(false);
        _isLoadingRecords = false;

        if (ClickPlayer.ActivePattern == null && !string.IsNullOrEmpty(UserProfileManager.CurrentProfile.ActivePatternName))
        {
            var match = _records.Find(r => string.Equals(r.name, UserProfileManager.CurrentProfile.ActivePatternName, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                var pattern = ClickPattern.FromEncryptedPayload(match.data, match.cps);
                if (pattern != null)
                {
                    ClickPlayer.ActivePattern = pattern;
                    ClickPlayer.ActivePatternName = match.name;
                }
            }
        }
    }

    private static void NavigateBack()
    {
        if (_currentPage == AppPage.Record)
        {
            SwitchPage(AppPage.Clicker);
        }
        else if (_currentPage == AppPage.Clicker || _currentPage == AppPage.Settings)
        {
            SwitchPage(AppPage.Menu);
        }
    }

    private static void SwitchPage(AppPage newPage)
    {
        _currentPage = newPage;
        _lastHover = -1;
        ConsoleWindow.Clear();

        if (newPage == AppPage.Clicker)
        {
            Task.Run(async () =>
            {
                await LoadCloudRecordsAsync().ConfigureAwait(false);
                RenderCurrentPage();
            });
        }

        RenderCurrentPage();
    }

    private static void RenderCurrentPage()
    {
        switch (_currentPage)
        {
            case AppPage.PinInput:
                RenderPinInput();
                break;
            case AppPage.Menu:
                RenderMenu();
                break;
            case AppPage.Clicker:
                RenderClicker();
                break;
            case AppPage.Record:
                RenderRecord();
                break;
            case AppPage.Settings:
                RenderSettings();
                break;
        }
    }

    private static void RenderPinInput()
    {
        int winW = Math.Max(Console.WindowWidth, 80);
        int winH = Math.Max(Console.WindowHeight, 25);
        int centerX = winW / 2;
        int centerY = winH / 2;

        var sb = new StringBuilder(2048);
        sb.Append("\x1b[H");

        string title = "enter pin";
        int titleX = Math.Max(0, centerX - (title.Length / 2));
        int titleY = Math.Max(2, centerY - 3);
        sb.Append(AnsiRenderer.MoveTo(titleX, titleY));
        sb.Append("\x1b[38;2;138;43;226m");
        sb.Append(title);
        sb.Append("\x1b[0m\x1b[K");

        int slotsX = Math.Max(0, centerX - 5);
        int slotsY = centerY;
        sb.Append(AnsiRenderer.MoveTo(slotsX, slotsY));

        for (int i = 0; i < 6; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }

            if (i < _pinBuffer.Length)
            {
                sb.Append("\x1b[38;2;0;229;255m\x1b[1m");
                sb.Append('*');
                sb.Append("\x1b[0m");
            }
            else
            {
                sb.Append("\x1b[38;2;100;100;110m_\x1b[0m");
            }
        }
        sb.Append("\x1b[K");

        string hint = "type pin or right-click to paste";
        int hintX = Math.Max(0, centerX - (hint.Length / 2));
        int hintY = centerY + 3;
        sb.Append(AnsiRenderer.MoveTo(hintX, hintY));
        sb.Append("\x1b[38;2;80;80;90m");
        sb.Append(hint);
        sb.Append("\x1b[0m\x1b[K");

        int statusY = centerY + 5;
        sb.Append(AnsiRenderer.MoveTo(0, statusY));
        if (!string.IsNullOrEmpty(_authStatus))
        {
            int statusX = Math.Max(0, centerX - (_authStatus.Length / 2));
            sb.Append(AnsiRenderer.MoveTo(statusX, statusY));
            if (_authStatus.Contains("authenticating", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append("\x1b[38;2;0;229;255m");
            }
            else
            {
                sb.Append("\x1b[38;2;255;70;70m");
            }
            sb.Append(_authStatus);
            sb.Append("\x1b[0m");
        }
        sb.Append("\x1b[K");

        Console.Write(sb.ToString());
    }

    private static void RenderMenu()
    {
        int winW = Math.Max(Console.WindowWidth, 80);
        int winH = Math.Max(Console.WindowHeight, 25);
        int centerX = winW / 2;
        int centerY = winH / 2;

        var sb = new StringBuilder(2048);
        sb.Append("\x1b[H");

        RenderUserHeader(sb, winW);

        string logoText = "zolsi.cc";
        int logoX = Math.Max(0, centerX - (logoText.Length / 2));
        int logoY = Math.Max(2, centerY - 5);
        sb.Append(AnsiRenderer.MoveTo(logoX, logoY));
        sb.Append("\x1b[38;2;138;43;226m");
        sb.Append(logoText);
        sb.Append("\x1b[0m\x1b[K");

        int clickerX = centerX - 5;
        int clickerY = centerY - 2;
        sb.Append(AnsiRenderer.MoveTo(clickerX, clickerY));
        if (_lastHover == 2)
        {
            sb.Append("\x1b[38;2;0;229;255m\x1b[1m[ clicker ]\x1b[0m");
        }
        else
        {
            sb.Append("\x1b[38;2;150;150;160m[ clicker ]\x1b[0m");
        }
        sb.Append("\x1b[K");

        int optionsX = centerX - 5;
        int optionsY = centerY;
        sb.Append(AnsiRenderer.MoveTo(optionsX, optionsY));
        if (_lastHover == 4)
        {
            sb.Append("\x1b[38;2;0;229;255m\x1b[1m[ options ]\x1b[0m");
        }
        else
        {
            sb.Append("\x1b[38;2;150;150;160m[ options ]\x1b[0m");
        }
        sb.Append("\x1b[K");

        int leaveX = centerX - 4;
        int leaveY = centerY + 2;
        sb.Append(AnsiRenderer.MoveTo(leaveX, leaveY));
        if (_lastHover == 3)
        {
            sb.Append("\x1b[38;2;0;229;255m\x1b[1m[ leave ]\x1b[0m");
        }
        else
        {
            sb.Append("\x1b[38;2;150;150;160m[ leave ]\x1b[0m");
        }
        sb.Append("\x1b[K");

        Console.Write(sb.ToString());
    }

    private static void RenderClicker()
    {
        int winW = Math.Max(Console.WindowWidth, 80);
        var sb = new StringBuilder(4096);
        sb.Append("\x1b[H");

        RenderTopBar(sb, winW);

        sb.Append(AnsiRenderer.MoveTo(6, 4));
        sb.Append("\x1b[38;2;180;180;190mclicker status:\x1b[0m");
        sb.Append(AnsiRenderer.MoveTo(26, 4));
        if (_lastHover == 10)
        {
            sb.Append(ClickPlayer.IsToggled ? "\x1b[38;2;0;255;150m\x1b[1m[ ON ]\x1b[0m" : "\x1b[38;2;255;70;70m\x1b[1m[ OFF ]\x1b[0m");
        }
        else
        {
            sb.Append(ClickPlayer.IsToggled ? "\x1b[38;2;0;200;120m[ ON ]\x1b[0m" : "\x1b[38;2;150;150;160m[ OFF ]\x1b[0m");
        }
        sb.Append("\x1b[K");

        sb.Append(AnsiRenderer.MoveTo(6, 6));
        sb.Append("\x1b[38;2;180;180;190mtoggle bind:\x1b[0m");
        sb.Append(AnsiRenderer.MoveTo(26, 6));
        if (_isListeningForBind)
        {
            sb.Append("\x1b[38;2;0;229;255m\x1b[1m[ press any key ]\x1b[0m");
        }
        else if (_lastHover == 11)
        {
            sb.Append($"\x1b[38;2;0;229;255m\x1b[1m[ bind: {ClickPlayer.GetBindKeyName()} ]\x1b[0m");
        }
        else
        {
            sb.Append($"\x1b[38;2;150;150;160m[ bind: {ClickPlayer.GetBindKeyName()} ]\x1b[0m");
        }
        sb.Append("\x1b[K");

        sb.Append(AnsiRenderer.MoveTo(6, 8));
        sb.Append("\x1b[38;2;180;180;190muse jitter:\x1b[0m");
        sb.Append(AnsiRenderer.MoveTo(26, 8));
        if (_lastHover == 14)
        {
            sb.Append(ClickPlayer.UseJitter ? "\x1b[38;2;0;255;150m\x1b[1m[ ON ]\x1b[0m" : "\x1b[38;2;255;70;70m\x1b[1m[ OFF ]\x1b[0m");
        }
        else
        {
            sb.Append(ClickPlayer.UseJitter ? "\x1b[38;2;0;200;120m[ ON ]\x1b[0m" : "\x1b[38;2;150;150;160m[ OFF ]\x1b[0m");
        }
        sb.Append("\x1b[K");

        sb.Append(AnsiRenderer.MoveTo(6, 10));
        sb.Append("\x1b[38;2;180;180;190mdisable in menu:\x1b[0m");
        sb.Append(AnsiRenderer.MoveTo(26, 10));
        if (_lastHover == 12)
        {
            sb.Append(ClickPlayer.DisableInMenu ? "\x1b[38;2;0;255;150m\x1b[1m[ ON ]\x1b[0m" : "\x1b[38;2;255;70;70m\x1b[1m[ OFF ]\x1b[0m");
        }
        else
        {
            sb.Append(ClickPlayer.DisableInMenu ? "\x1b[38;2;0;200;120m[ ON ]\x1b[0m" : "\x1b[38;2;150;150;160m[ OFF ]\x1b[0m");
        }
        sb.Append("\x1b[K");

        sb.Append(AnsiRenderer.MoveTo(6, 12));
        sb.Append("\x1b[38;2;180;180;190mactive pattern:\x1b[0m");
        sb.Append(AnsiRenderer.MoveTo(26, 12));
        if (ClickPlayer.ActivePattern != null)
        {
            sb.Append($"\x1b[38;2;0;229;255m{ClickPlayer.ActivePatternName}\x1b[0m  \x1b[38;2;120;120;130m(~{ClickPlayer.ActivePattern.Cps:0.0} CPS)\x1b[0m");
        }
        else
        {
            sb.Append("\x1b[38;2;120;120;130mnone\x1b[0m");
        }
        sb.Append("\x1b[K");

        sb.Append(AnsiRenderer.MoveTo(6, 15));
        if (_lastHover == 13)
        {
            sb.Append("\x1b[38;2;0;229;255m\x1b[1m[ + record new click pattern ]\x1b[0m");
        }
        else
        {
            sb.Append("\x1b[38;2;138;43;226m\x1b[1m[ + record new click pattern ]\x1b[0m");
        }
        sb.Append("\x1b[K");

        int recStartY = 18;
        if (_isLoadingRecords)
        {
            sb.Append(AnsiRenderer.MoveTo(0, recStartY));
            sb.Append("\x1b[2K");
            sb.Append(AnsiRenderer.MoveTo(6, recStartY));
            sb.Append("\x1b[38;2;100;100;110mloading patterns from cloud...\x1b[0m\x1b[K");
            for (int r = 1; r < 6; r++)
            {
                sb.Append(AnsiRenderer.MoveTo(0, recStartY + (r * 2)));
                sb.Append("\x1b[2K\x1b[K");
            }
        }
        else if (_records.Count == 0)
        {
            sb.Append(AnsiRenderer.MoveTo(0, recStartY));
            sb.Append("\x1b[2K");
            sb.Append(AnsiRenderer.MoveTo(6, recStartY));
            sb.Append("\x1b[38;2;100;100;110mno click patterns found in cloud\x1b[0m\x1b[K");
            for (int r = 1; r < 6; r++)
            {
                sb.Append(AnsiRenderer.MoveTo(0, recStartY + (r * 2)));
                sb.Append("\x1b[2K\x1b[K");
            }
        }
        else
        {
            for (int i = 0; i < 6; i++)
            {
                int rowY = recStartY + (i * 2);

                if (i < _records.Count)
                {
                    var r = _records[i];
                    bool isSelected = string.Equals(ClickPlayer.ActivePatternName, r.name, StringComparison.OrdinalIgnoreCase);

                    sb.Append(AnsiRenderer.MoveTo(6, rowY));
                    sb.Append(isSelected ? "\x1b[38;2;0;255;150m►\x1b[0m " : "  ");

                    sb.Append(AnsiRenderer.MoveTo(8, rowY));
                    string displayName = r.name.Length > 24 ? r.name[..24] : r.name;
                    string paddedName = displayName.PadRight(28);
                    sb.Append(isSelected ? $"\x1b[38;2;0;255;150m\x1b[1m{paddedName}\x1b[0m" : $"\x1b[38;2;210;210;220m{paddedName}\x1b[0m");

                    sb.Append(AnsiRenderer.MoveTo(36, rowY));
                    string cpsStr = $"(~{r.cps:0.0} CPS)".PadRight(14);
                    sb.Append($"\x1b[38;2;130;130;140m{cpsStr}\x1b[0m");

                    sb.Append(AnsiRenderer.MoveTo(50, rowY));
                    int selHov = 100 + (i * 3);
                    if (_lastHover == selHov)
                    {
                        sb.Append("\x1b[38;2;0;229;255m\x1b[1m[ select ]\x1b[0m   ");
                    }
                    else
                    {
                        sb.Append("\x1b[38;2;130;130;140m[ select ]\x1b[0m   ");
                    }

                    sb.Append(AnsiRenderer.MoveTo(63, rowY));
                    int renHov = 100 + (i * 3) + 1;
                    if (_lastHover == renHov)
                    {
                        sb.Append("\x1b[38;2;0;229;255m\x1b[1m[ rename ]\x1b[0m   ");
                    }
                    else
                    {
                        sb.Append("\x1b[38;2;130;130;140m[ rename ]\x1b[0m   ");
                    }

                    sb.Append(AnsiRenderer.MoveTo(76, rowY));
                    int delHov = 100 + (i * 3) + 2;
                    if (_lastHover == delHov)
                    {
                        sb.Append("\x1b[38;2;255;70;70m\x1b[1m[ delete ]\x1b[0m");
                    }
                    else
                    {
                        sb.Append("\x1b[38;2;130;130;140m[ delete ]\x1b[0m");
                    }
                    sb.Append("\x1b[K");
                }
                else
                {
                    sb.Append(AnsiRenderer.MoveTo(0, rowY));
                    sb.Append("\x1b[2K");
                }
            }
        }

        int renameY = recStartY + 13;
        if (_isRenaming)
        {
            sb.Append(AnsiRenderer.MoveTo(6, renameY));
            sb.Append($"\x1b[38;2;0;229;255mnew pattern name: \x1b[1m{_renameBuffer}_\x1b[0m   \x1b[38;2;100;100;110m(enter to save, esc to cancel)\x1b[0m\x1b[K");
        }
        else
        {
            sb.Append(AnsiRenderer.MoveTo(0, renameY));
            sb.Append("\x1b[K");
        }

        Console.Write(sb.ToString());
    }

    private static void RenderRecord()
    {
        int winW = Math.Max(Console.WindowWidth, 80);
        int winH = Math.Max(Console.WindowHeight, 25);
        int centerX = winW / 2;
        int centerY = winH / 2;

        var sb = new StringBuilder(4096);
        sb.Append("\x1b[H");

        RenderTopBar(sb, winW);

        int boxW = 42;
        int boxH = 13;
        int boxX = Math.Max(2, centerX - (boxW / 2));
        int boxY = Math.Max(4, centerY - (boxH / 2) - 2);

        double liveCps = _recordFinished && _recordedPattern != null
            ? Math.Round(_recordedPattern.Cps * Multipliers[_multiplierIndex], 1)
            : Math.Round(_recorder.GetLiveCps() * Multipliers[_multiplierIndex], 1);
        string cpsText = $"live CPS:  {liveCps:0.0}";
        string clicksText = $"clicks:    {_recorder.TotalClicks}";

        sb.Append(AnsiRenderer.MoveTo(boxX, boxY - 1));
        sb.Append("\x1b[38;2;0;229;255m\x1b[1m");
        sb.Append(cpsText);
        sb.Append("\x1b[0m");

        int clicksX = boxX + boxW - clicksText.Length;
        int gap = clicksX - (boxX + cpsText.Length);
        if (gap > 0)
        {
            sb.Append(new string(' ', gap));
        }
        sb.Append("\x1b[38;2;150;150;160m");
        sb.Append(clicksText);
        sb.Append("\x1b[0m\x1b[K");

        sb.Append(AnsiRenderer.MoveTo(boxX, boxY));
        sb.Append("\x1b[38;2;0;229;255m╭");
        sb.Append(new string('─', boxW - 2));
        sb.Append("╮\x1b[0m");

        int innerW = boxW - 2;
        int midRow = boxH / 2;
        int[] cellDotIdx = new int[innerW];

        for (int row = 1; row < boxH - 1; row++)
        {
            sb.Append(AnsiRenderer.MoveTo(boxX, boxY + row));
            sb.Append("\x1b[38;2;0;229;255m│\x1b[0m");

            if (row == midRow && !_recorder.IsRecording && !_recordFinished && _recorder.TotalClicks == 0)
            {
                string actionLabel = "CLICK TO RECORD";
                int padLeft = (innerW - actionLabel.Length) / 2;
                int padRight = innerW - actionLabel.Length - padLeft;
                sb.Append(new string(' ', padLeft));
                sb.Append("\x1b[38;2;138;43;226m\x1b[1m");
                sb.Append(actionLabel);
                sb.Append("\x1b[0m");
                sb.Append(new string(' ', padRight));
            }
            else
            {
                Array.Fill(cellDotIdx, -1);
                for (int i = 0; i < _recorderDots.Count; i++)
                {
                    if (_recorderDots[i].Y == row - 1)
                    {
                        int rx = _recorderDots[i].X;
                        if (rx >= 0 && rx < innerW)
                        {
                            cellDotIdx[rx] = i;
                        }
                    }
                }

                for (int c = 0; c < innerW; c++)
                {
                    int dIdx = cellDotIdx[c];
                    if (dIdx == -1)
                    {
                        sb.Append(' ');
                    }
                    else if (dIdx == _recorderDots.Count - 1 && _recorder.IsRecording)
                    {
                        sb.Append("\x1b[38;2;0;255;150m\x1b[1m•\x1b[0m");
                    }
                    else
                    {
                        sb.Append("\x1b[38;2;0;229;255m•\x1b[0m");
                    }
                }
            }

            sb.Append("\x1b[38;2;0;229;255m│\x1b[0m");
        }

        sb.Append(AnsiRenderer.MoveTo(boxX, boxY + boxH - 1));
        sb.Append("\x1b[38;2;0;229;255m╰");
        sb.Append(new string('─', boxW - 2));
        sb.Append("╯\x1b[0m");

        int stopY = boxY + boxH + 1;
        if (!_recordFinished)
        {
            int stopX = centerX - 9;
            sb.Append(AnsiRenderer.MoveTo(stopX, stopY));
            if (_lastHover == 31)
            {
                sb.Append("\x1b[38;2;255;70;70m\x1b[1m[ stop recording ]\x1b[0m");
            }
            else
            {
                sb.Append("\x1b[38;2;150;150;160m[ stop recording ]\x1b[0m");
            }
        }
        else
        {
            int sumX = Math.Max(0, centerX - (_recordSummary.Length / 2));
            sb.Append(AnsiRenderer.MoveTo(sumX, stopY));
            sb.Append("\x1b[38;2;0;255;150m\x1b[1m");
            sb.Append(_recordSummary);
            sb.Append("\x1b[0m");
        }
        sb.Append("\x1b[K");

        int multY = stopY + 2;
        int multX = centerX - 10;
        sb.Append(AnsiRenderer.MoveTo(multX, multY));
        string multStr = $"x{Multipliers[_multiplierIndex]:0.00}";
        if (_lastHover == 34)
        {
            sb.Append($"\x1b[38;2;0;229;255m\x1b[1m[ multiplier: {multStr} ]\x1b[0m");
        }
        else if (_multiplierIndex > 0)
        {
            sb.Append($"\x1b[38;2;180;180;190m[ multiplier: \x1b[38;2;0;255;150m\x1b[1m{multStr}\x1b[0m\x1b[38;2;180;180;190m ]\x1b[0m");
        }
        else
        {
            sb.Append($"\x1b[38;2;150;150;160m[ multiplier: {multStr} ]\x1b[0m");
        }
        sb.Append("\x1b[K");

        int promptY = multY + 2;
        if (!_recordFinished)
        {
            string hint = "click inside the box to record | press esc or enter to finish";
            int hintX = Math.Max(0, centerX - (hint.Length / 2));
            sb.Append(AnsiRenderer.MoveTo(hintX, promptY));
            sb.Append("\x1b[38;2;80;80;90m");
            sb.Append(hint);
            sb.Append("\x1b[0m\x1b[K");
        }
        else
        {
            string prompt = $"pattern name: [{_recordNameBuffer}_]";
            int promptX = Math.Max(0, centerX - (prompt.Length / 2));
            sb.Append(AnsiRenderer.MoveTo(promptX, promptY));
            sb.Append("\x1b[38;2;200;200;210m");
            sb.Append(prompt);
            sb.Append("\x1b[0m\x1b[K");
        }

        int btnY = promptY + 2;
        if (_recordFinished)
        {
            int saveX = centerX - 16;
            sb.Append(AnsiRenderer.MoveTo(saveX, btnY));
            if (_lastHover == 32)
            {
                sb.Append("\x1b[38;2;0;229;255m\x1b[1m[ save to cloud ]\x1b[0m");
            }
            else
            {
                sb.Append("\x1b[38;2;0;200;120m[ save to cloud ]\x1b[0m");
            }

            int discX = centerX + 5;
            sb.Append(AnsiRenderer.MoveTo(discX, btnY));
            if (_lastHover == 33)
            {
                sb.Append("\x1b[38;2;255;70;70m\x1b[1m[ discard ]\x1b[0m");
            }
            else
            {
                sb.Append("\x1b[38;2;150;150;160m[ discard ]\x1b[0m");
            }
            sb.Append("\x1b[K");
        }
        else
        {
            sb.Append(AnsiRenderer.MoveTo(0, btnY));
            sb.Append("\x1b[K");
        }

        Console.Write(sb.ToString());
    }

    private static void RenderSettings()
    {
        int winW = Math.Max(Console.WindowWidth, 80);

        var sb = new StringBuilder(4096);
        sb.Append("\x1b[H");

        RenderTopBar(sb, winW);

        bool streamProof = UserProfileManager.CurrentProfile.StreamProof;
        sb.Append(AnsiRenderer.MoveTo(6, 4));
        sb.Append("\x1b[38;2;180;180;190mstream proof:\x1b[0m");
        sb.Append(AnsiRenderer.MoveTo(26, 4));
        if (_lastHover == 20)
        {
            sb.Append(streamProof ? "\x1b[38;2;0;255;150m\x1b[1m[ ON ]\x1b[0m" : "\x1b[38;2;255;70;70m\x1b[1m[ OFF ]\x1b[0m");
        }
        else
        {
            sb.Append(streamProof ? "\x1b[38;2;0;200;120m[ ON ]\x1b[0m" : "\x1b[38;2;150;150;160m[ OFF ]\x1b[0m");
        }
        sb.Append("\x1b[K");

        sb.Append(AnsiRenderer.MoveTo(6, 6));
        sb.Append("\x1b[38;2;180;180;190mbring to front:\x1b[0m");
        sb.Append(AnsiRenderer.MoveTo(26, 6));
        string bringKeyName = WindowManager.GetKeyName(UserProfileManager.CurrentProfile.BringToFrontBindKey);
        if (_isListeningForBringToFrontBind)
        {
            sb.Append("\x1b[38;2;0;229;255m\x1b[1m[ press any key ]\x1b[0m");
        }
        else if (_lastHover == 21)
        {
            sb.Append($"\x1b[38;2;0;229;255m\x1b[1m[ bind: {bringKeyName} ]\x1b[0m");
        }
        else
        {
            sb.Append($"\x1b[38;2;150;150;160m[ bind: {bringKeyName} ]\x1b[0m");
        }
        sb.Append("\x1b[K");

        bool alwaysOnTop = UserProfileManager.CurrentProfile.AlwaysOnTop;
        sb.Append(AnsiRenderer.MoveTo(6, 8));
        sb.Append("\x1b[38;2;180;180;190malways on top:\x1b[0m");
        sb.Append(AnsiRenderer.MoveTo(26, 8));
        if (_lastHover == 22)
        {
            sb.Append(alwaysOnTop ? "\x1b[38;2;0;255;150m\x1b[1m[ ON ]\x1b[0m" : "\x1b[38;2;255;70;70m\x1b[1m[ OFF ]\x1b[0m");
        }
        else
        {
            sb.Append(alwaysOnTop ? "\x1b[38;2;0;200;120m[ ON ]\x1b[0m" : "\x1b[38;2;150;150;160m[ OFF ]\x1b[0m");
        }
        sb.Append("\x1b[K");

        Console.Write(sb.ToString());
    }

    private static void RenderTopBar(StringBuilder sb, int winW)
    {
        sb.Append(AnsiRenderer.MoveTo(2, 1));
        if (_lastHover == 1)
        {
            sb.Append("\x1b[38;2;0;229;255m\x1b[1m←\x1b[0m");
        }
        else
        {
            sb.Append("\x1b[38;2;120;120;130m←\x1b[0m");
        }

        RenderUserHeader(sb, winW);
    }

    private static void RenderUserHeader(StringBuilder sb, int winW)
    {
        if (AccountSession.Current != null)
        {
            string userTag = AccountSession.Current.Username;
            int userX = Math.Max(0, winW - userTag.Length - 2);
            sb.Append(AnsiRenderer.MoveTo(userX, 1));
            sb.Append("\x1b[38;2;0;229;255m\x1b[1m");
            sb.Append(userTag);
            sb.Append("\x1b[0m\x1b[K");
        }
    }
}
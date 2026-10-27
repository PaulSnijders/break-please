using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace BreakPlease;

class TrayApp : ApplicationContext
{
    static readonly TimeSpan MeetingCheckInterval = TimeSpan.FromSeconds(5);
    static readonly TimeSpan PostponeTime = TimeSpan.FromMinutes(5);

    readonly Settings settings = Settings.Load();
    readonly PhotoProvider photos = new();
    readonly NotifyIcon tray;
    readonly Icon activeIcon = CreateIcon(Color.FromArgb(46, 160, 120));
    readonly Icon disabledIcon = CreateIcon(Color.Gray);
    readonly ToolStripMenuItem statusItem = new() { Enabled = false };
    readonly ToolStripMenuItem toggleItem = new("Pauzes uitzetten");
    readonly System.Windows.Forms.Timer tick = new() { Interval = 500 };

    DateTime nextBreak;
    DateTime? idleSince;
    DateTime lastMeetingCheck;
    DateTime lastPhotoRefresh;
    string? meetingApp;
    bool disabled;
    WarningForm? warning;
    BreakSession? breakSession;
    SettingsForm? settingsForm;

    public TrayApp()
    {
        Log.Cleanup();
        Log.Write($"Gestart: elke {settings.IntervalMinutes} min, {settings.BreakSeconds}s pauze, reset na {settings.IdleResetSeconds}s inactief");
#if !DEBUG
        settings.ApplyAutostart(); // keep the Run key pointing at the current exe
#endif

        var menu = new ContextMenuStrip();
        menu.Items.Add(statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Nu pauze", null, (_, _) => StartBreak("handmatig"));
        menu.Items.Add(toggleItem);
        menu.Items.Add("Instellingen...", null, (_, _) => ShowSettings());
        menu.Items.Add("Logs openen", null, (_, _) => OpenLogs());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Afsluiten", null, (_, _) => Exit());
        menu.Opening += (_, _) => UpdateStatus();
        toggleItem.Click += (_, _) => ToggleDisabled();

        tray = new NotifyIcon { Icon = activeIcon, ContextMenuStrip = menu, Visible = true, Text = "Break Please" };
        tray.DoubleClick += (_, _) => ShowSettings();

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;

        ResetTimer();
        tick.Tick += (_, _) => OnTick();
        tick.Start();
    }

    void ResetTimer() => nextBreak = DateTime.Now.AddMinutes(settings.IntervalMinutes);

    void OnTick()
    {
        var now = DateTime.Now;

        if (now - lastPhotoRefresh > TimeSpan.FromHours(12))
        {
            lastPhotoRefresh = now;
            _ = photos.RefreshAsync();
        }

        if (breakSession != null) return;

        // Away from keyboard long enough: that counts as a break.
        var idle = Idle.Get();
        if (idle.TotalSeconds >= settings.IdleResetSeconds)
        {
            if (idleSince == null)
            {
                idleSince = now - idle;
                Log.Write($"Inactief sinds {idleSince:HH:mm:ss}: timer gereset");
                CloseWarning();
            }
            ResetTimer();
            UpdateStatus();
            return;
        }
        if (idleSince != null)
        {
            Log.Write($"Weer actief na {Format(now - idleSince.Value)} inactiviteit");
            idleSince = null;
            ResetTimer();
        }

        CheckMeeting(now);

        if (disabled)
        {
            ResetTimer();
            UpdateStatus();
            return;
        }

        var remaining = nextBreak - now;
        int warnSeconds = settings.WarningSeconds;

        if (meetingApp != null)
        {
            // Keep the break pending; it comes shortly after the call ends.
            CloseWarning();
            var minimum = TimeSpan.FromSeconds(warnSeconds + 60);
            if (remaining < minimum) nextBreak = now + minimum;
        }
        else if (remaining <= TimeSpan.Zero)
        {
            StartBreak("gepland");
            return;
        }
        else if (remaining.TotalSeconds <= warnSeconds)
        {
            ShowWarning((int)Math.Ceiling(remaining.TotalSeconds));
        }

        UpdateStatus();
    }

    void CheckMeeting(DateTime now)
    {
        if (!settings.DetectMeetings)
        {
            meetingApp = null;
            return;
        }
        if (now - lastMeetingCheck < MeetingCheckInterval) return;
        lastMeetingCheck = now;

        var app = MeetingDetector.ActiveApp();
        if (app == meetingApp) return;
        Log.Write(app != null ? $"Videobellen gedetecteerd ({app}): pauzes uitgesteld" : $"Videobellen voorbij ({meetingApp})");
        meetingApp = app;
    }

    void ShowWarning(int seconds)
    {
        if (warning == null)
        {
            Log.Write("Waarschuwing getoond");
            warning = new WarningForm();
            warning.PostponeClicked += () =>
            {
                Log.Write("Pauze 5 min uitgesteld");
                CloseWarning();
                nextBreak = DateTime.Now + PostponeTime;
            };
            warning.SkipClicked += () =>
            {
                Log.Write("Pauze overgeslagen (vanuit waarschuwing)");
                CloseWarning();
                ResetTimer();
            };
            warning.SetSeconds(seconds);
            warning.Show();
        }
        warning.SetSeconds(seconds);
    }

    void CloseWarning()
    {
        warning?.Close();
        warning = null;
    }

    void StartBreak(string reason)
    {
        if (breakSession != null) return;
        CloseWarning();
        Log.Write($"Pauze gestart ({reason}, {settings.BreakSeconds}s)");

        breakSession = new BreakSession(photos.PickRandom(), TimeSpan.FromSeconds(settings.BreakSeconds));
        breakSession.Finished += skipped =>
        {
            Log.Write(skipped ? "Pauze overgeslagen" : "Pauze afgerond");
            breakSession = null;
            ResetTimer();
        };
        breakSession.Start();
    }

    void ToggleDisabled()
    {
        disabled = !disabled;
        Log.Write(disabled ? "Pauzes uitgezet" : "Pauzes aangezet");
        toggleItem.Text = disabled ? "Pauzes aanzetten" : "Pauzes uitzetten";
        tray.Icon = disabled ? disabledIcon : activeIcon;
        CloseWarning();
        ResetTimer();
        UpdateStatus();
    }

    void UpdateStatus()
    {
        string status;
        if (disabled) status = "Pauzes staan uit";
        else if (idleSince != null) status = "Inactief, timer staat stil";
        else if (meetingApp != null) status = $"In gesprek ({meetingApp})";
        else status = $"Volgende pauze over {Format(nextBreak - DateTime.Now)}";

        statusItem.Text = status;
        var text = $"Break Please – {status}";
        tray.Text = text.Length > 127 ? text[..127] : text;
    }

    void ShowSettings()
    {
        if (settingsForm != null)
        {
            settingsForm.Activate();
            return;
        }
        using (settingsForm = new SettingsForm(settings))
        {
            if (settingsForm.ShowDialog() == DialogResult.OK)
            {
                settingsForm.ApplyTo(settings);
                try
                {
                    settings.Save();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Opslaan mislukt: {ex.Message}", "Break Please");
                }
                Log.Write($"Instellingen gewijzigd: elke {settings.IntervalMinutes} min, {settings.BreakSeconds}s pauze, " +
                          $"waarschuwing {settings.WarningSeconds}s, reset na {settings.IdleResetSeconds}s, " +
                          $"meetings={settings.DetectMeetings}, autostart={settings.StartWithWindows}");
                ResetTimer();
            }
        }
        settingsForm = null;
    }

    static void OpenLogs()
    {
        Directory.CreateDirectory(Log.Folder);
        System.Diagnostics.Process.Start("explorer.exe", Log.Folder);
    }

    void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) Log.Write("Slaapstand");
        if (e.Mode != PowerModes.Resume) return;
        Log.Write("Uit slaapstand: timer gereset");
        ResetTimer();
    }

    void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock) Log.Write("Scherm vergrendeld");
        if (e.Reason == SessionSwitchReason.SessionUnlock) Log.Write("Scherm ontgrendeld");
    }

    static string Format(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes} min {t.Seconds:00} s" : $"{t.Seconds} s";
    }

    static Icon CreateIcon(Color color)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var fill = new SolidBrush(color);
            using var white = new SolidBrush(Color.White);
            g.FillEllipse(fill, 1, 1, 30, 30);
            g.FillRectangle(white, 10, 9, 4, 14);
            g.FillRectangle(white, 18, 9, 4, 14);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    void Exit()
    {
        Log.Write("Afgesloten");
        tick.Stop();
        CloseWarning();
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        tray.Visible = false;
        tray.Dispose();
        ExitThread();
    }
}

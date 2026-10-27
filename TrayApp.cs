using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace BreakPlease;

class TrayApp : ApplicationContext
{
    static readonly TimeSpan MeetingCheckInterval = TimeSpan.FromSeconds(5);
    static readonly TimeSpan SnoozeTime = TimeSpan.FromMinutes(1);
    // Ticks are 0.5 s apart; a bigger gap means the computer was asleep (or the app was suspended).
    static readonly TimeSpan SleepGap = TimeSpan.FromSeconds(30);
    static readonly TimeSpan PhotoRefreshInterval = TimeSpan.FromHours(6);
    static readonly TimeSpan PhotoRetryInterval = TimeSpan.FromMinutes(10);

    readonly Settings settings = Settings.Load();
    readonly PhotoProvider photos = new();
    readonly NotifyIcon tray;
    readonly Icon activeIcon = CreateIcon(Color.FromArgb(46, 160, 120));
    readonly Icon disabledIcon = CreateIcon(Color.Gray);
    readonly ToolStripMenuItem statusItem = new() { Enabled = false };
    readonly ToolStripMenuItem[] pauseItems;
    readonly ToolStripMenuItem resumeItem;
    readonly System.Windows.Forms.Timer tick = new() { Interval = 500 };

    // The break comes at the first quiet moment between windowStart and windowEnd, or at windowEnd anyway.
    DateTime windowStart, windowEnd;
    DateTime lastTick = DateTime.Now;
    DateTime? idleSince;
    DateTime lastMeetingCheck;
    DateTime nextPhotoRefresh;
    DateTime? pausedUntil; // DateTime.MaxValue: until turned back on
    DateTime warningShown;
    bool warningForced;
    volatile bool locked; // set from the SystemEvents thread
    string? meetingApp;
    WarningForm? warning;
    BreakSession? breakSession;
    SettingsForm? settingsForm;

    public TrayApp()
    {
        Log.Cleanup();
        Log.Write($"Gestart: pauze na {settings.MinIntervalMinutes}-{settings.MaxIntervalMinutes} min, {settings.BreakSeconds}s pauze, reset na {settings.IdleResetSeconds}s inactief");
#if !DEBUG
        settings.ApplyAutostart(); // keep the Run key pointing at the current exe
#endif

        pauseItems =
        [
            new("Pauzes 2 uur uitzetten", null, (_, _) => Pause(DateTime.Now.AddHours(2), "2 uur")),
            new("Pauzes uitzetten tot morgen", null, (_, _) => Pause(DateTime.Today.AddDays(1), "tot morgen")),
            new("Pauzes uitzetten", null, (_, _) => Pause(DateTime.MaxValue, "tot weer aangezet")),
        ];
        resumeItem = new("Pauzes weer aanzetten", null, (_, _) => Resume("handmatig"));

        var menu = new ContextMenuStrip();
        menu.Items.Add(statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Nu pauze", null, (_, _) => StartBreak("handmatig"));
        menu.Items.AddRange(pauseItems);
        menu.Items.Add(resumeItem);
        menu.Items.Add("Instellingen...", null, (_, _) => ShowSettings());
        menu.Items.Add("Logs openen", null, (_, _) => OpenLogs());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Afsluiten", null, (_, _) => Exit());
        menu.Opening += (_, _) =>
        {
            foreach (var item in pauseItems) item.Visible = pausedUntil == null;
            resumeItem.Visible = pausedUntil != null;
            UpdateStatus();
        };

        tray = new NotifyIcon { Icon = activeIcon, ContextMenuStrip = menu, Visible = true, Text = "Break Please" };
        tray.DoubleClick += (_, _) => ShowSettings();

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;

        ResetTimer();
        tick.Tick += (_, _) => OnTick();
        tick.Start();
    }

    void ResetTimer()
    {
        windowStart = DateTime.Now.AddMinutes(settings.MinIntervalMinutes);
        windowEnd = DateTime.Now.AddMinutes(Math.Max(settings.MinIntervalMinutes, settings.MaxIntervalMinutes));
    }

    void OnTick()
    {
        var now = DateTime.Now;
        bool slept = now - lastTick > SleepGap;
        lastTick = now;

        if (now >= nextPhotoRefresh)
        {
            nextPhotoRefresh = now + PhotoRefreshInterval;
            RefreshPhotos();
        }

        if (breakSession != null) return;

        if (slept)
        {
            Log.Write("Computer sliep: timer gereset");
            CloseWarning();
            ResetTimer();
        }

        // Locked counts as away: the timer starts fresh after unlocking.
        if (locked)
        {
            CloseWarning();
            ResetTimer();
            UpdateStatus();
            return;
        }

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

        if (pausedUntil != null)
        {
            if (now < pausedUntil)
            {
                ResetTimer();
                UpdateStatus();
                return;
            }
            Resume("tijd voorbij");
        }

        if (meetingApp != null)
        {
            // Keep the break pending; it comes at a quiet moment after the call, or a minute after it at the latest.
            CloseWarning();
            var minimum = now + SnoozeTime;
            if (windowEnd < minimum) windowEnd = minimum;
        }
        else if (warning != null)
        {
            var shownFor = now - warningShown;
            if (!warningForced && idle < shownFor)
            {
                Log.Write("Waarschuwing weg door activiteit: 1 min uitgesteld");
                CloseWarning();
                windowStart = now + SnoozeTime;
            }
            else if (shownFor.TotalSeconds >= settings.WarningSeconds)
            {
                StartBreak(warningForced ? "uiterlijke tijd" : "rustig moment");
                return;
            }
            else
            {
                warning.SetSeconds((int)Math.Ceiling(settings.WarningSeconds - shownFor.TotalSeconds));
            }
        }
        else if (now >= windowEnd)
        {
            ShowWarning(forced: true);
        }
        else if (now >= windowStart && idle.TotalSeconds >= settings.QuietSeconds)
        {
            ShowWarning(forced: false);
        }

        UpdateStatus();
    }

    async void RefreshPhotos()
    {
        try
        {
            if (!await photos.RefreshAsync())
                nextPhotoRefresh = DateTime.Now + PhotoRetryInterval;
        }
        catch (Exception ex)
        {
            Log.Write($"Foto's ophalen mislukt: {ex.Message}");
            nextPhotoRefresh = DateTime.Now + PhotoRetryInterval;
        }
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

    void ShowWarning(bool forced)
    {
        if (settings.WarningSeconds <= 0)
        {
            StartBreak(forced ? "uiterlijke tijd" : "rustig moment");
            return;
        }
        Log.Write(forced ? "Waarschuwing getoond (uiterlijke tijd)" : "Waarschuwing getoond (rustig moment)");
        warningShown = DateTime.Now;
        warningForced = forced;
        warning = new WarningForm(dismissable: !forced);
        warning.SetSeconds(settings.WarningSeconds);
        warning.Show();
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

    void Pause(DateTime until, string description)
    {
        Log.Write($"Pauzes uitgezet ({description})");
        pausedUntil = until;
        tray.Icon = disabledIcon;
        CloseWarning();
        UpdateStatus();
    }

    void Resume(string reason)
    {
        Log.Write($"Pauzes weer aan ({reason})");
        pausedUntil = null;
        tray.Icon = activeIcon;
        ResetTimer();
        UpdateStatus();
    }

    void UpdateStatus()
    {
        var now = DateTime.Now;
        string status;
        if (pausedUntil == DateTime.MaxValue) status = "Pauzes staan uit";
        else if (pausedUntil != null) status = pausedUntil.Value.Date == now.Date ? $"Pauzes uit tot {pausedUntil:HH:mm}" : "Pauzes uit tot morgen";
        else if (locked) status = "Vergrendeld, timer staat stil";
        else if (idleSince != null) status = "Inactief, timer staat stil";
        else if (meetingApp != null) status = $"In gesprek ({meetingApp})";
        else if (warning != null) status = "Pauze komt eraan";
        else if (now < windowStart) status = $"Volgende pauze over {Format(windowStart - now)} (uiterlijk {Format(windowEnd - now)})";
        else status = $"Pauze bij eerste rustige moment (uiterlijk over {Format(windowEnd - now)})";

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
                Log.Write($"Instellingen gewijzigd: pauze na {settings.MinIntervalMinutes}-{settings.MaxIntervalMinutes} min, " +
                          $"rustig na {settings.QuietSeconds}s, {settings.BreakSeconds}s pauze, " +
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

    // These run on the SystemEvents thread: only log and set flags, OnTick does the rest.
    void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) Log.Write("Slaapstand");
        if (e.Mode == PowerModes.Resume) Log.Write("Uit slaapstand");
    }

    void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock)
        {
            Log.Write("Scherm vergrendeld");
            locked = true;
        }
        if (e.Reason == SessionSwitchReason.SessionUnlock)
        {
            Log.Write("Scherm ontgrendeld: timer gereset");
            locked = false;
        }
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

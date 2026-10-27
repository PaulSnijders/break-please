namespace BreakPlease;

class SettingsForm : Form
{
    readonly NumericUpDown minInterval = Number(1, 240);
    readonly NumericUpDown maxInterval = Number(1, 240);
    readonly NumericUpDown quietSeconds = Number(1, 120);
    readonly NumericUpDown breakSeconds = Number(5, 600);
    readonly NumericUpDown warningSeconds = Number(0, 120);
    readonly NumericUpDown idleSeconds = Number(10, 3600);
    readonly CheckBox detectMeetings = new() { Text = "Geen pauzes tijdens videobellen (microfoon/camera in gebruik)", AutoSize = true };
    readonly CheckBox autostart = new() { Text = "Starten met Windows", AutoSize = true };

    public SettingsForm(Settings s)
    {
        Text = "Break Please – instellingen";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = new Font("Segoe UI", 9.5f);
        Padding = new Padding(12);

        minInterval.Value = s.MinIntervalMinutes;
        maxInterval.Value = s.MaxIntervalMinutes;
        quietSeconds.Value = s.QuietSeconds;
        breakSeconds.Value = s.BreakSeconds;
        warningSeconds.Value = s.WarningSeconds;
        idleSeconds.Value = s.IdleResetSeconds;
        detectMeetings.Checked = s.DetectMeetings;
        autostart.Checked = s.StartWithWindows;

        var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
        AddRow(grid, "Pauze op rustig moment na (minuten)", minInterval);
        AddRow(grid, "Pauze uiterlijk na (minuten)", maxInterval);
        AddRow(grid, "Rustig moment: geen invoer (seconden)", quietSeconds);
        AddRow(grid, "Pauzeduur (seconden)", breakSeconds);
        AddRow(grid, "Waarschuwing vooraf (seconden)", warningSeconds);
        AddRow(grid, "Timer resetten na inactiviteit (seconden)", idleSeconds);
        AddFull(grid, detectMeetings);
        AddFull(grid, autostart);

        var ok = new Button { Text = "Opslaan", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Annuleren", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 0) };
        buttons.Controls.AddRange([cancel, ok]);
        grid.Controls.Add(buttons);
        grid.SetColumnSpan(buttons, 2);

        Controls.Add(grid);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public void ApplyTo(Settings s)
    {
        s.MinIntervalMinutes = (int)minInterval.Value;
        s.MaxIntervalMinutes = Math.Max(s.MinIntervalMinutes, (int)maxInterval.Value);
        s.QuietSeconds = (int)quietSeconds.Value;
        s.BreakSeconds = (int)breakSeconds.Value;
        s.WarningSeconds = (int)warningSeconds.Value;
        s.IdleResetSeconds = (int)idleSeconds.Value;
        s.DetectMeetings = detectMeetings.Checked;
        s.StartWithWindows = autostart.Checked;
    }

    static NumericUpDown Number(int min, int max) => new() { Minimum = min, Maximum = max, Width = 80 };

    static void AddRow(TableLayoutPanel grid, string text, Control control)
    {
        grid.Controls.Add(new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 12, 6) });
        grid.Controls.Add(control);
    }

    static void AddFull(TableLayoutPanel grid, Control control)
    {
        control.Margin = new Padding(3, 8, 3, 0);
        grid.Controls.Add(control);
        grid.SetColumnSpan(control, 2);
    }
}

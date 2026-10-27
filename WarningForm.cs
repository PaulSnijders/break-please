namespace BreakPlease;

/// <summary>Small, non-focus-stealing popup in the bottom-right corner before a break.</summary>
class WarningForm : Form
{
    readonly Label label;

    /// <param name="dismissable">True when any mouse/keyboard input cancels the break (shown as a hint).</param>
    public WarningForm(bool dismissable)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = Color.FromArgb(32, 34, 38);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10f);
        Padding = new Padding(14, 10, 14, 10);

        label = new Label { AutoSize = true, Font = new Font("Segoe UI Semibold", 11f), Margin = Padding.Empty };

        var layout = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = Padding.Empty,
        };
        layout.Controls.Add(label);
        if (dismissable)
        {
            layout.Controls.Add(new Label
            {
                Text = "Beweeg de muis of typ om uit te stellen",
                AutoSize = true,
                ForeColor = Color.FromArgb(170, 175, 185),
                Margin = new Padding(0, 4, 0, 0),
            });
        }
        Controls.Add(layout);
    }

    public void SetSeconds(int seconds) => label.Text = $"Pauze over {seconds} s";

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        var area = Screen.PrimaryScreen!.WorkingArea;
        int margin = LogicalToDeviceUnits(16);
        Location = new Point(area.Right - Width - margin, area.Bottom - Height - margin);
    }
}

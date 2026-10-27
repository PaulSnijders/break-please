namespace BreakPlease;

/// <summary>Small, non-focus-stealing popup in the bottom-right corner before a break.</summary>
class WarningForm : Form
{
    readonly Label label;

    public event Action? PostponeClicked;
    public event Action? SkipClicked;

    public WarningForm()
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

        label = new Label { AutoSize = true, Font = new Font("Segoe UI Semibold", 11f), Margin = new Padding(0, 0, 0, 6) };

        var postpone = MakeLink("5 min uitstellen", () => PostponeClicked?.Invoke());
        var skip = MakeLink("Overslaan", () => SkipClicked?.Invoke());

        var links = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty, WrapContents = false };
        links.Controls.AddRange([postpone, skip]);

        var layout = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = Padding.Empty,
        };
        layout.Controls.AddRange([label, links]);
        Controls.Add(layout);
    }

    static LinkLabel MakeLink(string text, Action onClick)
    {
        var link = new LinkLabel
        {
            Text = text,
            AutoSize = true,
            LinkColor = Color.FromArgb(140, 200, 255),
            ActiveLinkColor = Color.White,
            LinkBehavior = LinkBehavior.HoverUnderline,
            Margin = new Padding(0, 0, 16, 0),
        };
        link.LinkClicked += (_, _) => onClick();
        return link;
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

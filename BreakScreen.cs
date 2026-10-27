using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace BreakPlease;

/// <summary>One break: a full-screen form on every monitor, closed when time is up or skipped.</summary>
class BreakSession
{
    static readonly TimeSpan FadeTime = TimeSpan.FromMilliseconds(400);

    readonly List<BreakForm> forms = [];
    readonly System.Windows.Forms.Timer timer = new() { Interval = 30 };
    readonly Photo? photo;
    readonly TimeSpan duration;
    DateTime start;
    bool done;

    /// <summary>Raised when the break ends; argument is true when skipped.</summary>
    public event Action<bool>? Finished;

    public BreakSession(Photo? photo, TimeSpan duration)
    {
        this.photo = photo;
        this.duration = duration;
        // Capture all screens first, before any break form covers them.
        foreach (var screen in Screen.AllScreens)
        {
            var form = new BreakForm(screen, photo);
            form.SkipRequested += () => End(skipped: true);
            forms.Add(form);
        }
        timer.Tick += (_, _) => OnTick();
    }

    public void Start()
    {
        start = DateTime.Now;
        foreach (var form in forms) form.Show();
        forms.FirstOrDefault(f => f.IsPrimary)?.Activate();
        timer.Start();
    }

    void OnTick()
    {
        var elapsed = DateTime.Now - start;
        if (elapsed >= duration)
        {
            End(skipped: false);
            return;
        }
        var remaining = duration - elapsed;
        double opacity = Math.Min(1, Math.Min(elapsed / FadeTime, remaining / FadeTime));
        foreach (var form in forms)
            form.UpdateProgress(remaining / duration, (int)Math.Ceiling(remaining.TotalSeconds), opacity);
    }

    void End(bool skipped)
    {
        if (done) return;
        done = true;
        timer.Dispose();
        foreach (var form in forms) form.Close();
        photo?.Dispose();
        Finished?.Invoke(skipped);
    }
}

class BreakForm : Form
{
    readonly Bitmap background;
    readonly Photo? photo;
    readonly Font captionFont, timeFont, titleFont, hintFont;
    double fraction = 1;
    int secondsLeft;
    Rectangle skipRect;

    public event Action? SkipRequested;
    public bool IsPrimary { get; }

    public BreakForm(Screen screen, Photo? photo)
    {
        this.photo = photo;
        IsPrimary = screen.Primary;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        Bounds = screen.Bounds;
        TopMost = true;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        KeyPreview = true;
        BackColor = Color.Black;
        Opacity = 0;

        background = CaptureBlurred(screen.Bounds);

        float h = screen.Bounds.Height;
        titleFont = new Font("Segoe UI Light", h / 22, GraphicsUnit.Pixel);
        captionFont = new Font("Segoe UI", h / 70, GraphicsUnit.Pixel);
        timeFont = new Font("Segoe UI Semibold", h / 50, GraphicsUnit.Pixel);
        hintFont = new Font("Segoe UI", h / 85, GraphicsUnit.Pixel);

        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) SkipRequested?.Invoke(); };
        MouseClick += (_, e) => { if (skipRect.Contains(e.Location)) SkipRequested?.Invoke(); };
        MouseMove += (_, e) => Cursor = skipRect.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
        Shown += (_, _) => Bounds = screen.Bounds; // guard against DPI rescaling on other monitors
    }

    public void UpdateProgress(double fraction, int secondsLeft, double opacity)
    {
        bool changed = secondsLeft != this.secondsLeft || Math.Abs(fraction - this.fraction) > 0.001;
        this.fraction = fraction;
        this.secondsLeft = secondsLeft;
        Opacity = opacity;
        if (changed) Invalidate();
    }

    static Bitmap CaptureBlurred(Rectangle bounds)
    {
        using var shot = new Bitmap(bounds.Width, bounds.Height);
        using (var g = Graphics.FromImage(shot))
            g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);

        // Blur by scaling down a lot and back up smoothly.
        int sw = Math.Max(1, bounds.Width / 32), sh = Math.Max(1, bounds.Height / 32);
        using var small = new Bitmap(sw, sh);
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.DrawImage(shot, 0, 0, sw, sh);
        }

        var result = new Bitmap(bounds.Width, bounds.Height);
        using (var g = Graphics.FromImage(result))
        using (var attrs = new ImageAttributes())
        using (var dim = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
        {
            attrs.SetWrapMode(WrapMode.TileFlipXY); // no dark edges
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(small, new Rectangle(0, 0, bounds.Width, bounds.Height), 0, 0, sw, sh, GraphicsUnit.Pixel, attrs);
            g.FillRectangle(dim, 0, 0, bounds.Width, bounds.Height);
        }
        return result;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.DrawImageUnscaled(background, 0, 0);

        int w = ClientSize.Width, h = ClientSize.Height;
        using var white = new SolidBrush(Color.White);
        using var soft = new SolidBrush(Color.FromArgb(190, 255, 255, 255));
        using var faint = new SolidBrush(Color.FromArgb(120, 255, 255, 255));
        var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        int barTop = (int)(h * 0.86);

        if (photo != null)
        {
            // Fit the photo in the upper area, keep aspect ratio.
            float maxW = w * 0.72f, maxH = h * 0.70f;
            float scale = Math.Min(maxW / photo.Image.Width, maxH / photo.Image.Height);
            int pw = (int)(photo.Image.Width * scale), ph = (int)(photo.Image.Height * scale);
            var rect = new Rectangle((w - pw) / 2, (int)(h * 0.07), pw, ph);
            int radius = h / 90;

            using (var shadow = RoundedRect(Rectangle.Inflate(rect, radius, radius), radius * 2))
            using (var shadowBrush = new SolidBrush(Color.FromArgb(60, 0, 0, 0)))
                g.FillPath(shadowBrush, shadow);

            using (var clip = RoundedRect(rect, radius))
            {
                g.SetClip(clip);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(photo.Image, rect);
                g.ResetClip();
            }

            var captionRect = new RectangleF(rect.Left, rect.Bottom + h * 0.01f, rect.Width, captionFont.Height * 2.2f);
            g.DrawString(photo.Caption, captionFont, soft, captionRect,
                new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisWord });
        }
        else
        {
            g.DrawString("Even pauze. Kijk eens uit het raam.", titleFont, white, new RectangleF(0, 0, w, barTop), center);
        }

        // Progress bar with remaining time.
        int barW = (int)(w * 0.36), barH = Math.Max(6, h / 150);
        var track = new Rectangle((w - barW) / 2, barTop, barW, barH);
        using (var trackPath = RoundedRect(track, barH / 2))
        using (var trackBrush = new SolidBrush(Color.FromArgb(70, 255, 255, 255)))
            g.FillPath(trackBrush, trackPath);

        int fillW = (int)(barW * fraction);
        if (fillW > barH)
        {
            using var fillPath = RoundedRect(new Rectangle(track.X, track.Y, fillW, barH), barH / 2);
            using var fillBrush = new SolidBrush(Color.FromArgb(120, 220, 170));
            g.FillPath(fillBrush, fillPath);
        }

        g.DrawString($"nog {secondsLeft} s", timeFont, white,
            new RectangleF(0, track.Top - timeFont.Height * 1.4f, w, timeFont.Height * 1.2f), center);

        const string hint = "Overslaan (Esc)";
        var hintSize = g.MeasureString(hint, hintFont);
        skipRect = new Rectangle((int)((w - hintSize.Width) / 2), track.Bottom + (int)(hintFont.Height * 1.2f),
            (int)hintSize.Width, (int)hintSize.Height);
        g.DrawString(hint, hintFont, faint, skipRect.Location);
    }

    static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        int d = Math.Max(1, radius * 2);
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            background.Dispose();
            titleFont.Dispose();
            captionFont.Dispose();
            timeFont.Dispose();
            hintFont.Dispose();
        }
        base.Dispose(disposing);
    }
}

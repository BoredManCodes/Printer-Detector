using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace PrinterDetector;

internal static class Gfx
{
    public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        int d = radius * 2;
        var arc = new Rectangle(bounds.Location, new Size(d, d));

        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - d;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - d;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRoundedRect(Graphics g, Rectangle bounds, int radius, Color color)
    {
        using var path = RoundedRect(bounds, radius);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    public static void DrawRoundedRect(Graphics g, Rectangle bounds, int radius, Color color, float width = 1f)
    {
        using var path = RoundedRect(bounds, radius);
        using var pen = new Pen(color, width);
        g.DrawPath(pen, path);
    }
}

/// <summary>Rounded pill label used for flags, port chips and status badges.</summary>
internal sealed class Pill : Control
{
    public Color BgColor { get; set; } = Theme.ChipIpBg;
    public Color FgColor { get; set; } = Theme.ChipIpFg;
    public Color Backdrop { get; set; } = Color.White;
    public bool Bold { get; set; } = true;

    public Pill()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Font = new Font("Segoe UI", 8f, FontStyle.Bold);
        Padding = new Padding(10, 4, 10, 4);
        Cursor = Cursors.Default;
    }

#pragma warning disable CS8765 // base Control.Text setter is [AllowNull] but typed as non-nullable string
    public override string Text
    {
        get => base.Text;
        set { base.Text = value ?? ""; UpdateSize(); }
    }
#pragma warning restore CS8765

    private void UpdateSize()
    {
        using var g = CreateGraphics();
        var sz = g.MeasureString(Text, Font);
        Size = new Size((int)sz.Width + Padding.Left + Padding.Right, (int)sz.Height + Padding.Top + Padding.Bottom);
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        UpdateSize();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var backBrush = new SolidBrush(Backdrop)) e.Graphics.FillRectangle(backBrush, ClientRectangle);
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        Gfx.FillRoundedRect(e.Graphics, rect, Height / 2, BgColor);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, FgColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

internal enum ButtonStyle { Primary, Outline, OutlineOnDark, Ghost, Danger, AccentOutline }

/// <summary>Flat, owner-drawn, rounded-corner button matching the redesign's pill buttons.</summary>
internal sealed class FlatButton : Button
{
    public ButtonStyle Style { get; set; } = ButtonStyle.Outline;
    public int Radius { get; set; } = 6;
    public Color Backdrop { get; set; } = Color.White;
    private bool _hover;

    public FlatButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        MouseEnter += (_, _) => { _hover = true; Invalidate(); };
        MouseLeave += (_, _) => { _hover = false; Invalidate(); };
    }

    protected override void OnPaint(PaintEventArgs pe)
    {
        var g = pe.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var backBrush = new SolidBrush(Backdrop)) g.FillRectangle(backBrush, ClientRectangle);
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);

        Color fill, border, fore;
        switch (Style)
        {
            case ButtonStyle.Primary:
                fill = _hover ? Theme.AccentHover : Theme.Accent;
                border = fill;
                fore = Color.White;
                break;
            case ButtonStyle.OutlineOnDark:
                fill = _hover ? Color.FromArgb(20, Color.White) : Color.Transparent;
                border = Color.FromArgb(90, Color.White);
                fore = Color.White;
                break;
            case ButtonStyle.Ghost:
                fill = _hover ? Color.FromArgb(20, Color.White) : Color.Transparent;
                border = Color.Transparent;
                fore = Theme.NavItem;
                break;
            case ButtonStyle.Danger:
                fill = _hover ? Theme.FlagRedirBg : Color.White;
                border = Theme.DangerBorder;
                fore = Theme.DangerText;
                break;
            case ButtonStyle.AccentOutline:
                fill = Theme.ChipIpBg;
                border = Theme.Accent;
                fore = Theme.AccentText;
                break;
            default:
                fill = _hover ? Theme.ChipIpBg : Color.White;
                border = _hover ? Theme.Accent : Theme.ButtonBorder;
                fore = _hover ? Theme.AccentText : Theme.TextDark;
                break;
        }

        if (fill.A > 0) Gfx.FillRoundedRect(g, rect, Radius, fill);
        if (border != Color.Transparent) Gfx.DrawRoundedRect(g, rect, Radius, border);

        var textRect = Rectangle.Inflate(ClientRectangle, -6, 0);
        TextRenderer.DrawText(g, Text, Font, textRect, Enabled ? fore : Color.Gray,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }
}

/// <summary>Single-line label that ticker-scrolls its text left/right when it doesn't fit the control width.</summary>
internal sealed class MarqueeLabel : Control
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 30 };
    private float _offset;
    private int _holdFrames;
    private bool _forward = true;
    public Color Backdrop { get; set; } = Color.White;

    public MarqueeLabel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Font = new Font("Segoe UI", 11.5f, FontStyle.Bold);
        ForeColor = Theme.TextDark;
        _timer.Tick += (_, _) => Tick();
    }

    public override string Text
    {
        get => base.Text;
        set { base.Text = value ?? ""; ResetScroll(); Invalidate(); }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ResetScroll();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        ResetScroll();
    }

    private int TextWidth()
    {
        using var g = CreateGraphics();
        return TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
    }

    private void ResetScroll()
    {
        _offset = 0;
        _forward = true;
        _holdFrames = 30;
        bool overflow = IsHandleCreated && TextWidth() > Width;
        if (overflow && !_timer.Enabled) _timer.Start();
        if (!overflow) _timer.Stop();
    }

    private void Tick()
    {
        int overflow = TextWidth() - Width;
        if (overflow <= 0) { _timer.Stop(); Invalidate(); return; }

        if (_holdFrames > 0) { _holdFrames--; Invalidate(); return; }

        _offset += _forward ? 1f : -1f;
        if (_offset >= overflow) { _offset = overflow; _forward = false; _holdFrames = 24; }
        else if (_offset <= 0) { _offset = 0; _forward = true; _holdFrames = 36; }
        Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ResetScroll();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using (var backBrush = new SolidBrush(Backdrop)) e.Graphics.FillRectangle(backBrush, ClientRectangle);
        var rect = new Rectangle(-(int)_offset, 0, Math.Max(Width, TextWidth()) + (int)_offset, Height);
        TextRenderer.DrawText(e.Graphics, Text, Font, rect, ForeColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Thin progress bar with a sweeping highlight block, matching the design's marquee refresh indicator.</summary>
internal sealed class MarqueeBar : Panel
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private float _t;

    public MarqueeBar()
    {
        Height = 3;
        DoubleBuffered = true;
        BackColor = ColorTranslator.FromHtml("#1E2F43");
        _timer.Tick += (_, _) =>
        {
            _t += 0.016f / 1.4f;
            if (_t > 1.3f) _t = -0.3f;
            Invalidate();
        };
    }

    public void Run(bool run)
    {
        if (run) { _t = -0.3f; _timer.Start(); }
        else _timer.Stop();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!_timer.Enabled) return;
        int w = (int)(Width * 0.3f);
        int x = (int)(_t * Width);
        using var brush = new SolidBrush(Theme.Accent);
        e.Graphics.FillRectangle(brush, x, 0, w, Height);
    }
}

/// <summary>Animated gray gradient placeholder for values still loading, matching the design's shimmer state.</summary>
internal sealed class ShimmerStrip
{
    private static readonly System.Windows.Forms.Timer SharedTimer = new() { Interval = 30 };
    private static readonly List<Control> Subscribers = new();
    private static float _phase;

    static ShimmerStrip()
    {
        SharedTimer.Tick += (_, _) =>
        {
            _phase += 0.02f;
            if (_phase > 1f) _phase = 0f;
            foreach (var c in Subscribers.ToArray())
            {
                if (c.IsDisposed) { Subscribers.Remove(c); continue; }
                c.Invalidate();
            }
        };
    }

    public static void Subscribe(Control c)
    {
        if (!Subscribers.Contains(c)) Subscribers.Add(c);
        if (!SharedTimer.Enabled) SharedTimer.Start();
    }

    public static void Unsubscribe(Control c) => Subscribers.Remove(c);

    public static void Paint(Graphics g, Rectangle rect)
    {
        using var path = Gfx.RoundedRect(rect, 3);
        float pos = _phase * (rect.Width * 2) - rect.Width;
        using var brush = new LinearGradientBrush(
            new RectangleF(pos, rect.Top, rect.Width, rect.Height),
            ColorTranslator.FromHtml("#EDF0F4"), ColorTranslator.FromHtml("#F8F9FB"), 0f);
        var blend = new ColorBlend(3)
        {
            Colors = new[] { ColorTranslator.FromHtml("#EDF0F4"), ColorTranslator.FromHtml("#F8F9FB"), ColorTranslator.FromHtml("#EDF0F4") },
            Positions = new[] { 0f, 0.5f, 1f }
        };
        brush.InterpolationColors = blend;
        g.FillPath(brush, path);
    }
}

/// <summary>TextBox with a native cue-banner placeholder (no focus-swap hacks).</summary>
internal static class CueBanner
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

    private const int EM_SETCUEBANNER = 0x1501;

    public static void Set(TextBox box, string text) =>
        SendMessage(box.Handle, EM_SETCUEBANNER, (IntPtr)1, text);
}

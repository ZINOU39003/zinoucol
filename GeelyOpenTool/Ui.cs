using System.Drawing.Drawing2D;
using System.Reflection;

namespace GeelyOpenTool;

internal static class Theme
{
    public const string AppName = "ZINOU COOLRAY";
    public const string Tagline = "Coolray IHU624G  •  Flash & Customization Suite";
    public const string Version = "v1.0";

    public static readonly Color Bg = Color.FromArgb(10, 12, 22);
    public static readonly Color Sidebar = Color.FromArgb(13, 15, 26);
    public static readonly Color CardFill = Color.FromArgb(168, 12, 14, 26);
    public static readonly Color Input = Color.FromArgb(18, 20, 34);
    public static readonly Color Terminal = Color.FromArgb(7, 8, 15);
    public static readonly Color Gold = Color.FromArgb(247, 185, 64);
    public static readonly Color Copper = Color.FromArgb(226, 104, 42);
    public static readonly Color Sand = Color.FromArgb(240, 218, 170);
    public static readonly Color Text = Color.FromArgb(242, 238, 230);
    public static readonly Color Muted = Color.FromArgb(178, 170, 156);
    public static readonly Color Success = Color.FromArgb(52, 211, 153);
    public static readonly Color Warning = Color.FromArgb(250, 204, 21);
    public static readonly Color Danger = Color.FromArgb(239, 68, 68);

    public static readonly Font Body = new("Segoe UI", 10f);
    public static readonly Font Bold = new("Segoe UI Semibold", 10f);
    public static readonly Font CardTitle = new("Segoe UI Semibold", 11.5f);
    public static readonly Font Icons = new("Segoe MDL2 Assets", 13f);
    public static readonly Font Mono = new(FontFamily.Families.Any(f => f.Name == "Cascadia Mono") ? "Cascadia Mono" : "Consolas", 9.5f);

    public static Image? Image(string name)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        return s is null ? null : new Bitmap(System.Drawing.Image.FromStream(s));
    }

    public static Icon? AppIcon()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico");
        return s is null ? null : new Icon(s);
    }

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>Draws an image scaled to cover <paramref name="area"/>, anchored vertically at <paramref name="anchorY"/> (0 top .. 1 bottom).</summary>
    public static void Cover(Graphics g, Image img, Rectangle area, float anchorY = 0.5f)
    {
        var scale = Math.Max((float)area.Width / img.Width, (float)area.Height / img.Height);
        var w = img.Width * scale;
        var h = img.Height * scale;
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        g.DrawImage(img, area.X + (area.Width - w) / 2, area.Y + (area.Height - h) * anchorY, w, h);
    }

    [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? appName, string? idList);

    /// <summary>Styles standard inputs created anywhere in the page builders.</summary>
    public static void Apply(Control root)
    {
        foreach (Control c in root.Controls)
        {
            switch (c)
            {
                case TextBox t when t.Multiline == false:
                    t.BackColor = Input; t.ForeColor = Text; t.BorderStyle = BorderStyle.FixedSingle; t.Font = Body;
                    break;
                case ComboBox cb:
                    cb.FlatStyle = FlatStyle.Flat; cb.BackColor = Input; cb.ForeColor = Text; cb.Font = Body;
                    if (cb.IsHandleCreated) SetWindowTheme(cb.Handle, "DarkMode_CFD", null);
                    else cb.HandleCreated += (_, _) => SetWindowTheme(cb.Handle, "DarkMode_CFD", null);
                    break;
                case CheckBox chk:
                    chk.ForeColor = Text; chk.BackColor = Color.Transparent; chk.FlatStyle = FlatStyle.Flat;
                    chk.FlatAppearance.BorderColor = Gold; chk.FlatAppearance.CheckedBackColor = Color.FromArgb(150, 96, 20);
                    chk.Font = Body;
                    break;
                case Label l when l.Tag is null:
                    l.ForeColor = Muted; l.BackColor = Color.Transparent; l.Font = Body;
                    break;
            }
            Apply(c);
        }
    }
}

internal enum ButtonKind { Primary, Secondary, Danger }

internal sealed class NeonButton : Button
{
    private bool hover, down;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public ButtonKind Kind { get; set; }

    public NeonButton(ButtonKind kind)
    {
        Kind = kind;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.Opaque, false);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        ForeColor = Color.White;
        Font = Theme.Bold;
        Cursor = Cursors.Hand;
        AutoSize = true;
        Margin = new Padding(4, 4, 4, 4);
        Padding = new Padding(18, 0, 18, 0);
    }

    public override Size GetPreferredSize(Size proposed)
    {
        var s = TextRenderer.MeasureText(Text, Font);
        return new Size(s.Width + Padding.Horizontal + 8, 40);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (Parent is { } p && p.BackColor.A == 255) e.Graphics.Clear(p.BackColor);
        else base.OnPaintBackground(e);
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var press = down ? 1 : 0;
        var r = new RectangleF(2, 1 + press, Width - 5, Height - 6);
        using var path = Theme.Round(r, 10);
        Color text;

        if (Enabled && !down)
        {
            using var shadow = Theme.Round(new RectangleF(r.X + 1, r.Y + 3, r.Width - 2, r.Height), 10);
            using var sb = new SolidBrush(Color.FromArgb(90, 0, 0, 0));
            g.FillPath(sb, shadow);
        }

        if (!Enabled)
        {
            using var b = new SolidBrush(Color.FromArgb(120, 40, 42, 54));
            using var pen = new Pen(Color.FromArgb(70, 120, 120, 130), 1f);
            g.FillPath(b, path);
            g.DrawPath(pen, path);
            text = Color.FromArgb(120, 120, 130);
        }
        else if (Kind == ButtonKind.Secondary)
        {
            using var b = new LinearGradientBrush(r, Color.FromArgb(hover ? 120 : 200, 26, 24, 34), Color.FromArgb(hover ? 120 : 200, 14, 14, 22), LinearGradientMode.Vertical);
            g.FillPath(b, path);
            if (hover)
            {
                using var hb = new SolidBrush(Color.FromArgb(down ? 70 : 40, Theme.Gold));
                g.FillPath(hb, path);
            }
            using var border = new LinearGradientBrush(r, Theme.Gold, Theme.Copper, LinearGradientMode.Vertical);
            using var pen = new Pen(border, hover ? 1.8f : 1.2f);
            g.DrawPath(pen, path);
            text = hover ? Color.White : Theme.Gold;
        }
        else
        {
            var danger = Kind == ButtonKind.Danger;
            var top = danger ? Color.FromArgb(244, 86, 86) : Color.FromArgb(255, 210, 102);
            var bottom = danger ? Color.FromArgb(150, 22, 30) : Color.FromArgb(222, 108, 32);
            if (hover && !down) { top = ControlPaint.Light(top, 0.25f); bottom = ControlPaint.Light(bottom, 0.12f); }
            if (down) { top = ControlPaint.Dark(top, 0.08f); bottom = ControlPaint.Dark(bottom, 0.08f); }
            using (var b = new LinearGradientBrush(r, top, bottom, LinearGradientMode.Vertical))
                g.FillPath(b, path);

            var glossRect = new RectangleF(r.X + 1, r.Y + 1, r.Width - 2, r.Height * 0.48f);
            using (var gloss = Theme.Round(glossRect, 9))
            using (var gb = new LinearGradientBrush(glossRect, Color.FromArgb(95, Color.White), Color.FromArgb(8, Color.White), LinearGradientMode.Vertical))
                g.FillPath(gb, gloss);

            using (var pen = new Pen(Color.FromArgb(hover ? 230 : 150, danger ? Color.FromArgb(255, 190, 190) : Color.FromArgb(255, 236, 180)), hover ? 1.6f : 1f))
                g.DrawPath(pen, path);
            text = danger ? Color.White : Color.FromArgb(34, 22, 8);
        }

        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        if (RightToLeft == RightToLeft.Yes) flags |= TextFormatFlags.RightToLeft;
        TextRenderer.DrawText(g, Text, Font, Rectangle.Round(r), text, flags);
    }
}

internal class SmoothFlow : FlowLayoutPanel
{
    public SmoothFlow()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }
}

internal sealed class Card : SmoothFlow
{
    public string Title { get; }

    public Card(string title)
    {
        Title = title;
        SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(18, 50, 18, 14);
        Margin = new Padding(6, 6, 6, 12);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
        using var path = Theme.Round(r, 14);
        using var fill = new SolidBrush(Theme.CardFill);
        g.FillPath(fill, path);

        var head = new RectangleF(r.X, r.Y, r.Width, 44);
        using (var hp = new GraphicsPath())
        {
            hp.AddArc(head.X, head.Y, 28, 28, 180, 90);
            hp.AddArc(head.Right - 28, head.Y, 28, 28, 270, 90);
            hp.AddLine(head.Right, head.Bottom, head.X, head.Bottom);
            hp.CloseFigure();
            using var hb = new LinearGradientBrush(head, Color.FromArgb(10, Theme.Gold), Color.FromArgb(55, Theme.Copper), LinearGradientMode.Horizontal);
            g.FillPath(hb, hp);
        }

        using var border = new LinearGradientBrush(r, Color.FromArgb(70, Theme.Copper), Color.FromArgb(170, Theme.Gold), LinearGradientMode.Horizontal);
        using var pen = new Pen(border, 1.2f);
        g.DrawPath(pen, path);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Diamond badge next to the title.
        var cx = Width - 26f; var cy = 23f;
        using (var diamond = new GraphicsPath())
        {
            diamond.AddPolygon(new[] { new PointF(cx, cy - 7), new PointF(cx + 7, cy), new PointF(cx, cy + 7), new PointF(cx - 7, cy) });
            using var db = new LinearGradientBrush(new RectangleF(cx - 7, cy - 7, 14, 14), Theme.Gold, Theme.Copper, LinearGradientMode.Vertical);
            g.FillPath(db, diamond);
        }

        var titleRect = new Rectangle(18, 9, Width - 60, 28);
        TextRenderer.DrawText(g, Title, Theme.CardTitle, titleRect, Theme.Sand,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.RightToLeft | TextFormatFlags.EndEllipsis);
        using var line = new LinearGradientBrush(new Rectangle(18, 44, Width - 36, 1), Color.FromArgb(0, Theme.Gold), Color.FromArgb(160, Theme.Gold), LinearGradientMode.Horizontal);
        g.FillRectangle(line, 18, 44, Width - 36, 1);
    }
}

/// <summary>Scrollable page that keeps the background image fixed behind the cards.</summary>
internal sealed class BackdropPage : SmoothFlow
{
    private readonly Image? backdrop;

    public BackdropPage(Image? backdrop)
    {
        this.backdrop = backdrop;
        SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Bg;
        Dock = DockStyle.Fill;
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        AutoScroll = true;
        Padding = new Padding(14, 14, 14, 14);
        Scroll += (_, _) => Invalidate(true);
        MouseWheel += (_, _) => Invalidate(true);
        Resize += (_, _) => FitCards();
        ControlAdded += (_, _) => FitCards();
    }

    protected override Point ScrollToControl(Control activeControl) => DisplayRectangle.Location;

    private Point lastScroll;

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (AutoScrollPosition == lastScroll) return;
        lastScroll = AutoScrollPosition;
        Invalidate(true);
    }

    private void FitCards()
    {
        var w = ClientSize.Width - Padding.Horizontal - 14;
        foreach (Control c in Controls) c.MinimumSize = new Size(Math.Max(600, w), 0);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Bg);
        if (backdrop is not null) Theme.Cover(g, backdrop, ClientRectangle, 0.75f);
        using var veil = new LinearGradientBrush(ClientRectangle, Color.FromArgb(120, Theme.Bg), Color.FromArgb(25, Theme.Bg), LinearGradientMode.Horizontal);
        g.FillRectangle(veil, ClientRectangle);
    }
}

internal sealed class NavButton : Control
{
    private bool hover;
    public string Glyph { get; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Selected { get => selected; set { selected = value; Invalidate(); } }
    private bool selected;

    public NavButton(string glyph, string text)
    {
        Glyph = glyph;
        Text = text;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 50;
        Dock = DockStyle.Top;
        Cursor = Cursors.Hand;
        BackColor = Theme.Sidebar;
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Sidebar);
        var r = new RectangleF(10, 4, Width - 20, Height - 8);
        if (selected || hover)
        {
            using var path = Theme.Round(r, 10);
            using var b = new LinearGradientBrush(r, Color.FromArgb(selected ? 10 : 4, Theme.Copper), Color.FromArgb(selected ? 70 : 28, Theme.Gold), LinearGradientMode.Horizontal);
            g.FillPath(b, path);
            if (selected)
            {
                using var pen = new Pen(Color.FromArgb(90, Theme.Gold), 1f);
                g.DrawPath(pen, path);
            }
        }
        if (selected)
        {
            var bar = new RectangleF(Width - 6, 10, 4, Height - 20);
            using var accent = new LinearGradientBrush(bar, Theme.Gold, Theme.Copper, LinearGradientMode.Vertical);
            using var p = Theme.Round(bar, 2);
            g.FillPath(accent, p);
        }
        var fg = selected ? Color.White : hover ? Theme.Text : Theme.Muted;
        TextRenderer.DrawText(g, Glyph, Theme.Icons, new Rectangle(Width - 52, 0, 34, Height), selected ? Theme.Gold : fg,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, Text, selected ? Theme.Bold : Theme.Body, new Rectangle(12, 0, Width - 66, Height), fg,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.RightToLeft | TextFormatFlags.EndEllipsis);
    }
}

internal enum OpState { Idle, Running, Done, Failed, Cancelled }

/// <summary>Shared animation/state logic for the progress ring and line. Value &lt; 0 means indeterminate.</summary>
internal abstract class ProgressControl : Control
{
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 30 };
    protected double Target = -1, Shown;
    protected float Phase;
    protected OpState State = OpState.Idle;

    protected ProgressControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        timer.Tick += (_, _) =>
        {
            Phase = (Phase + 0.022f) % 1f;
            if (Target >= 0) Shown += (Target - Shown) * 0.18;
            Invalidate();
        };
    }

    public void Set(OpState state, double value)
    {
        if (state == OpState.Running && State != OpState.Running) Shown = 0;
        State = state;
        Target = Math.Clamp(value, -1, 1);
        if (state is OpState.Done) Target = Shown = 1;
        timer.Enabled = state == OpState.Running;
        Invalidate();
    }

    protected Color StateColor => State switch
    {
        OpState.Done => Theme.Success,
        OpState.Failed => Theme.Danger,
        OpState.Cancelled => Theme.Warning,
        _ => Theme.Gold,
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class ProgressRing : ProgressControl
{
    private static readonly Font PercentFont = new("Segoe UI Semibold", 8.5f);
    private static readonly Font MarkFont = new("Segoe UI Symbol", 11f, FontStyle.Bold);

    public ProgressRing()
    {
        Size = new Size(44, 44);
        Margin = new Padding(6, 3, 6, 3);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Terminal);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(4, 4, Width - 9, Height - 9);
        using (var track = new Pen(Color.FromArgb(45, 255, 255, 255), 4.5f))
            g.DrawEllipse(track, r);

        string label;
        if (State == OpState.Running)
        {
            using var brush = new LinearGradientBrush(RectangleF.Inflate(r, 3, 3), Color.FromArgb(255, 224, 140), Theme.Copper, LinearGradientMode.ForwardDiagonal);
            using var pen = new Pen(brush, 4.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            if (Target < 0)
            {
                g.DrawArc(pen, r, Phase * 360f - 90f, 110f);
                label = "•••";
            }
            else
            {
                var sweep = (float)Math.Max(Shown * 360, 4);
                g.DrawArc(pen, r, -90f, sweep);
                label = $"{Shown * 100:0}%";
            }
        }
        else if (State == OpState.Idle)
        {
            label = "0%";
        }
        else
        {
            using var pen = new Pen(StateColor, 4.5f);
            g.DrawEllipse(pen, r);
            label = State switch { OpState.Done => "✔", OpState.Failed => "✖", _ => "■" };
        }

        var font = label.Length <= 1 ? MarkFont : PercentFont;
        var color = State is OpState.Running or OpState.Idle ? (State == OpState.Idle ? Theme.Muted : Theme.Text) : StateColor;
        TextRenderer.DrawText(g, label, font, ClientRectangle, color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

internal sealed class ProgressLine : ProgressControl
{
    public ProgressLine()
    {
        Dock = DockStyle.Top;
        Height = 5;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Terminal);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var track = new SolidBrush(Color.FromArgb(28, 255, 255, 255)))
            g.FillRectangle(track, 0, 1, Width, Height - 2);

        if (State == OpState.Idle) return;
        RectangleF fill;
        if (State == OpState.Running && Target < 0)
        {
            var seg = Width * 0.28f;
            fill = new RectangleF(-seg + (Width + seg) * Phase, 0, seg, Height);
        }
        else fill = new RectangleF(0, 0, (float)(Width * (State == OpState.Running ? Shown : 1)), Height);
        if (fill.Width < 1) return;

        if (State == OpState.Running)
        {
            using var b = new LinearGradientBrush(new RectangleF(fill.X - 1, 0, fill.Width + 2, Height), Theme.Copper, Color.FromArgb(255, 224, 140), LinearGradientMode.Horizontal);
            g.FillRectangle(b, fill);
            if (Target >= 0 && fill.Width > 40)
            {
                var shineX = fill.X + (fill.Width + 60) * Phase - 60;
                using var shine = new LinearGradientBrush(new RectangleF(shineX, 0, 30, Height), Color.FromArgb(0, Color.White), Color.FromArgb(150, Color.White), LinearGradientMode.Horizontal) { WrapMode = WrapMode.TileFlipX };
                g.SetClip(fill);
                g.FillRectangle(shine, shineX, 0, 60, Height);
                g.ResetClip();
            }
        }
        else
        {
            using var b = new SolidBrush(StateColor);
            g.FillRectangle(b, fill);
        }
    }
}

/// <summary>Non-interactive replica of a button, used in the guide to name the button to press.</summary>
internal sealed class KeyChip : Control
{
    private readonly ButtonKind kind;

    public KeyChip(string text, ButtonKind kind)
    {
        this.kind = kind;
        Text = text;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Font = Theme.Bold;
        Margin = new Padding(4, 5, 4, 5);
        var s = TextRenderer.MeasureText(text, Font);
        Size = new Size(s.Width + 28, 30);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(1, 1, Width - 3, Height - 3);
        using var path = Theme.Round(r, 8);
        Color text;
        if (kind == ButtonKind.Secondary)
        {
            using var b = new SolidBrush(Color.FromArgb(170, 18, 18, 28));
            using var pen = new Pen(Theme.Gold, 1.1f);
            g.FillPath(b, path);
            g.DrawPath(pen, path);
            text = Theme.Gold;
        }
        else
        {
            var danger = kind == ButtonKind.Danger;
            using var b = new LinearGradientBrush(r, danger ? Color.FromArgb(244, 86, 86) : Color.FromArgb(255, 210, 102),
                danger ? Color.FromArgb(150, 22, 30) : Color.FromArgb(222, 108, 32), LinearGradientMode.Vertical);
            g.FillPath(b, path);
            text = danger ? Color.White : Color.FromArgb(34, 22, 8);
        }
        TextRenderer.DrawText(g, Text, Font, Rectangle.Round(r), text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.RightToLeft);
    }
}

/// <summary>Round numbered badge for guide steps.</summary>
internal sealed class StepBadge : Control
{
    private static readonly Font NumberFont = new("Segoe UI Black", 10f);

    public StepBadge(int number)
    {
        Text = number.ToString();
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Size = new Size(30, 30);
        Margin = new Padding(4, 5, 6, 5);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(1, 1, Width - 3, Height - 3);
        using var b = new LinearGradientBrush(r, Color.FromArgb(255, 224, 140), Theme.Copper, LinearGradientMode.ForwardDiagonal);
        g.FillEllipse(b, r);
        using var pen = new Pen(Color.FromArgb(200, 255, 240, 200), 1f);
        g.DrawEllipse(pen, r);
        TextRenderer.DrawText(g, Text, NumberFont, ClientRectangle, Color.FromArgb(34, 22, 8),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

internal sealed class HeaderBar : Control
{
    private readonly Image? logo;
    private readonly Image? backdrop;
    public string DeviceText { get; private set; } = "جارٍ البحث عن جهاز...";
    public Color DeviceColor { get; private set; } = Theme.Warning;

    public HeaderBar(Image? logo, Image? backdrop)
    {
        this.logo = logo;
        this.backdrop = backdrop;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Dock = DockStyle.Top;
        Height = 100;
    }

    public void SetDevice(string text, Color color)
    {
        if (text == DeviceText && color == DeviceColor) return;
        DeviceText = text;
        DeviceColor = color;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        g.Clear(Theme.Bg);
        if (backdrop is not null) Theme.Cover(g, backdrop, ClientRectangle, 0.42f);
        using (var veil = new LinearGradientBrush(ClientRectangle, Color.FromArgb(235, Theme.Bg), Color.FromArgb(150, Theme.Bg), LinearGradientMode.Horizontal))
            g.FillRectangle(veil, ClientRectangle);

        // Dune-shaped gold line along the bottom edge.
        using (var dune = new GraphicsPath())
        {
            float w = Width, y = Height - 3;
            dune.AddBezier(0, y, w * 0.25f, y - 7, w * 0.4f, y + 3, w * 0.62f, y - 4);
            dune.AddBezier(w * 0.62f, y - 4, w * 0.8f, y - 9, w * 0.9f, y + 1, w, y - 2);
            using var dp = new LinearGradientBrush(ClientRectangle, Theme.Gold, Theme.Copper, LinearGradientMode.Horizontal);
            using var pen = new Pen(dp, 2f);
            g.DrawPath(pen, dune);
        }

        const int logoSize = 74;
        const int logoX = 20;
        const int logoY = 11;
        if (logo is not null)
        {
            var lr = new RectangleF(logoX, logoY, logoSize, logoSize);
            for (var i = 3; i >= 1; i--)
            {
                using var glow = Theme.Round(RectangleF.Inflate(lr, i * 2, i * 2), 16 + i * 2);
                using var gb = new SolidBrush(Color.FromArgb(18, Theme.Gold));
                g.FillPath(gb, glow);
            }
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            using var clip = Theme.Round(lr, 16);
            g.SetClip(clip);
            g.DrawImage(logo, lr);
            g.ResetClip();
            using var ring = new LinearGradientBrush(lr, Theme.Gold, Theme.Copper, LinearGradientMode.ForwardDiagonal);
            using var ringPen = new Pen(ring, 1.6f);
            g.DrawPath(ringPen, clip);
        }

        var textX = logoX + logoSize + 18f;
        using var titleFont = new Font("Segoe UI Black", 24f, FontStyle.Bold, GraphicsUnit.Point);
        var em = g.DpiY * titleFont.SizeInPoints / 72f;
        RectangleF zinouBounds;
        using (var zinou = new GraphicsPath())
        {
            zinou.AddString("ZINOU", titleFont.FontFamily, (int)titleFont.Style, em, new PointF(textX, 8), StringFormat.GenericTypographic);
            zinouBounds = zinou.GetBounds();
            using var zb = new LinearGradientBrush(new RectangleF(zinouBounds.X, zinouBounds.Y - 1, zinouBounds.Width, zinouBounds.Height + 2), Color.FromArgb(255, 224, 140), Theme.Copper, LinearGradientMode.Vertical);
            g.FillPath(zb, zinou);
        }
        using (var coolray = new GraphicsPath())
        {
            coolray.AddString("COOLRAY", titleFont.FontFamily, (int)titleFont.Style, em, new PointF(zinouBounds.Right + 12, 8), StringFormat.GenericTypographic);
            using var cb = new SolidBrush(Color.White);
            g.FillPath(cb, coolray);
        }
        TextRenderer.DrawText(g, Theme.Tagline, Theme.Body, new Point((int)textX + 1, 60), Theme.Sand, TextFormatFlags.NoPrefix);

        var pillText = DeviceText;
        var pillSize = TextRenderer.MeasureText(pillText, Theme.Bold);
        var pill = new RectangleF(Width - pillSize.Width - 66, 31, pillSize.Width + 44, 38);
        using (var pp = Theme.Round(pill, 19))
        {
            using var pb = new SolidBrush(Color.FromArgb(150, 10, 12, 22));
            using var tint = new SolidBrush(Color.FromArgb(40, DeviceColor));
            using var pen = new Pen(Color.FromArgb(190, DeviceColor), 1.3f);
            g.FillPath(pb, pp);
            g.FillPath(tint, pp);
            g.DrawPath(pen, pp);
        }
        using (var halo = new SolidBrush(Color.FromArgb(70, DeviceColor)))
            g.FillEllipse(halo, pill.Right - 28, pill.Y + 11, 16, 16);
        using (var dot = new SolidBrush(DeviceColor))
            g.FillEllipse(dot, pill.Right - 25, pill.Y + 14, 10, 10);
        TextRenderer.DrawText(g, pillText, Theme.Bold, new Rectangle((int)pill.X + 10, (int)pill.Y, (int)pill.Width - 42, (int)pill.Height), Theme.Text,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.RightToLeft);
    }
}

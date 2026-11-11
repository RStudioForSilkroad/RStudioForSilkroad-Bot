using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

enum ButtonKind
{
    Secondary,
    Primary,
    Success,
    Danger,
    Ghost
}

// Flat buttons with rounded corners, a hover fade, a pressed state and a visible keyboard focus ring.
static class Fancy
{
    sealed class State
    {
        internal float T;
        internal bool Over, Down;
        internal ButtonKind Kind;
    }

    static readonly Dictionary<Button, State> states = new Dictionary<Button, State>();
    static readonly Timer timer = new Timer { Interval = 16 };

    static Fancy()
    {
        timer.Tick += Step;
    }

    static Color BackOf(Control c)
    {
        for (Control p = c.Parent; p != null; p = p.Parent)
            if (p.BackColor.A == 255)
                return p.BackColor;
        return Ui.Canvas;
    }

    internal static void Kind(Button b, ButtonKind kind)
    {
        Style(b);
        states[b].Kind = kind;
        b.Invalidate();
    }

    internal static void Style(Button b)
    {
        if (states.ContainsKey(b))
        {
            b.Invalidate();
            return;
        }
        var s = new State();
        states[b] = s;
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.UseVisualStyleBackColor = false;
        b.Cursor = Cursors.Hand;
        b.Font = Ui.BodyBold;
        b.BackColor = Ui.Surface;
        b.ForeColor = Ui.Surface;
        b.FlatAppearance.MouseOverBackColor = Ui.Surface;
        b.FlatAppearance.MouseDownBackColor = Ui.Surface;
        Ui.DoubleBuffer(b);
        b.MouseEnter += delegate
        {
            s.Over = true;
            timer.Start();
        };
        b.MouseLeave += delegate
        {
            s.Over = false;
            s.Down = false;
            timer.Start();
        };
        b.MouseDown += delegate
        {
            s.Down = true;
            b.Invalidate();
        };
        b.MouseUp += delegate
        {
            s.Down = false;
            b.Invalidate();
        };
        b.EnabledChanged += delegate { b.Invalidate(); };
        b.GotFocus += delegate { b.Invalidate(); };
        b.LostFocus += delegate { b.Invalidate(); };
        b.Disposed += delegate { states.Remove(b); };
        b.Paint += delegate(object o, PaintEventArgs e) { Draw(b, e.Graphics, s); };
        b.Invalidate();
    }

    static void Draw(Button b, Graphics g, State s)
    {
        if (b.Width < 4 || b.Height < 4)
            return;
        g.Clear(BackOf(b));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Color fill, border, text;
        bool filled = s.Kind == ButtonKind.Primary || s.Kind == ButtonKind.Success || s.Kind == ButtonKind.Danger;
        if (!b.Enabled)
        {
            fill = s.Kind == ButtonKind.Ghost ? BackOf(b) : Color.FromArgb(238, 241, 246);
            border = s.Kind == ButtonKind.Ghost ? BackOf(b) : Color.FromArgb(225, 230, 238);
            text = Ui.TextDisabled;
        }
        else
        {
            Color baseFill, hoverFill, pressedFill;
            switch (s.Kind)
            {
                case ButtonKind.Primary:
                    baseFill = Ui.Accent;
                    hoverFill = Ui.AccentHover;
                    pressedFill = Ui.AccentPressed;
                    break;
                case ButtonKind.Success:
                    baseFill = Ui.Success;
                    hoverFill = Ui.SuccessHover;
                    pressedFill = Ui.Mix(Ui.Success, Color.Black, 0.4f);
                    break;
                case ButtonKind.Danger:
                    baseFill = Ui.Danger;
                    hoverFill = Ui.DangerHover;
                    pressedFill = Ui.Mix(Ui.Danger, Color.Black, 0.4f);
                    break;
                case ButtonKind.Ghost:
                    baseFill = BackOf(b);
                    hoverFill = Ui.AccentTint;
                    pressedFill = Ui.AccentTintStrong;
                    break;
                default:
                    baseFill = Ui.Surface;
                    hoverFill = Ui.AccentTint;
                    pressedFill = Ui.AccentTintStrong;
                    break;
            }
            fill = s.Down ? pressedFill : Ui.Mix(baseFill, hoverFill, s.T);
            border = filled || s.Kind == ButtonKind.Ghost ? fill : Ui.Mix(Ui.BorderStrong, Ui.Accent, s.T);
            text = filled ? Color.White : s.Kind == ButtonKind.Ghost ? Ui.Accent : Ui.Text;
        }
        var r = new Rectangle(1, 1, b.Width - 3, b.Height - 3);
        using (var path = Ui.RoundRect(r, 6))
        {
            using (var brush = new SolidBrush(fill))
                g.FillPath(brush, path);
            using (var pen = new Pen(border, 1f))
                g.DrawPath(pen, path);
        }
        if (b.Focused && b.Enabled)
        {
            var ring = new Rectangle(0, 0, b.Width - 1, b.Height - 1);
            using (var path = Ui.RoundRect(ring, 7))
                using (var pen = new Pen(filled ? Ui.Text : Ui.Accent, 2f))
                    g.DrawPath(pen, path);
        }
        TextRenderer.DrawText(g, b.Text, b.Font, new Rectangle(6, 0, b.Width - 12, b.Height), text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }

    static void Step(object sender, EventArgs e)
    {
        bool busy = false;
        foreach (var pair in new List<KeyValuePair<Button, State>>(states))
        {
            var s = pair.Value;
            float target = s.Over ? 1f : 0f, delta = target - s.T;
            if (Math.Abs(delta) > 0.02f)
            {
                s.T += delta * 0.35f;
                busy = true;
                pair.Key.Invalidate();
            }
            else if (s.T != target)
            {
                s.T = target;
                pair.Key.Invalidate();
            }
        }
        if (!busy)
            timer.Stop();
    }
}

// A white rounded card with a title and a hairline under it; the content goes into the card's padding area.
sealed class CardBox : Panel
{
    internal const int TitleHeight = 44;
    string title;

    internal CardBox(string title)
    {
        this.title = title;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(Ui.Pad, TitleHeight + 6, Ui.Pad, Ui.Pad);
        Margin = new Padding(0, 0, 0, 12);
        BackColor = Ui.Surface;
        Ui.DoubleBuffer(this);
        ResizeRedraw = true;
    }

    internal string Title
    {
        get { return title; }
        set
        {
            title = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent == null ? Ui.Canvas : Parent.BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        if (r.Width < 20 || r.Height < 20)
            return;
        using (var path = Ui.RoundRect(r, Ui.Radius))
        {
            using (var fill = new SolidBrush(Ui.Surface))
                g.FillPath(fill, path);
            using (var pen = new Pen(Ui.Border))
                g.DrawPath(pen, path);
        }
        TextRenderer.DrawText(g, title ?? "", Ui.Heading, new Rectangle(Ui.Pad, 0, Width - 2 * Ui.Pad, TitleHeight), Ui.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        using (var line = new Pen(Ui.Divider))
            g.DrawLine(line, 1, TitleHeight, Width - 2, TitleHeight);
    }
}

// Page host without header strip and without the frame the system draws around tab pages.
sealed class FlatTabs : TabControl
{
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x1328 && !DesignMode)
        {
            m.Result = IntPtr.Zero;
            return;
        } // TCM_ADJUSTRECT: pages use the whole control
        base.WndProc(ref m);
    }
}

// Text tabs with an accent underline for the selected one; drives a FlatTabs control, works with the keyboard.
sealed class TabStrip : Control
{
    readonly TabControl target;
    int hover = -1;

    internal TabStrip(TabControl target)
    {
        this.target = target;
        Height = 42;
        Dock = DockStyle.Top;
        TabStop = true;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        target.SelectedIndexChanged += delegate { Invalidate(); };
        target.ControlAdded += delegate { Invalidate(); };
        GotFocus += delegate { Invalidate(); };
        LostFocus += delegate { Invalidate(); };
    }

    Rectangle[] TabRects()
    {
        var list = new List<Rectangle>();
        int x = 8;
        foreach (TabPage page in target.TabPages)
        {
            int w = TextRenderer.MeasureText(page.Text, Ui.BodyBold).Width + 32;
            list.Add(new Rectangle(x, 0, w, Height - 1));
            x += w + 2;
        }
        return list.ToArray();
    }

    int HitTest(Point p)
    {
        var rects = TabRects();
        for (int i = 0; i < rects.Length; i++)
            if (rects[i].Contains(p))
                return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int index = HitTest(e.Location);
        if (index != hover)
        {
            hover = index;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hover = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        int index = HitTest(e.Location);
        if (index >= 0)
        {
            Focus();
            target.SelectedIndex = index;
        }
    }

    protected override bool IsInputKey(Keys keyData)
    {
        return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        int count = target.TabPages.Count;
        if (count == 0)
            return;
        if (e.KeyCode == Keys.Right)
            target.SelectedIndex = (target.SelectedIndex + 1) % count;
        else if (e.KeyCode == Keys.Left)
            target.SelectedIndex = (target.SelectedIndex + count - 1) % count;
        else
            return;
        e.Handled = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ui.Surface);
        var rects = TabRects();
        for (int i = 0; i < rects.Length; i++)
        {
            bool selected = i == target.SelectedIndex;
            var r = rects[i];
            if (!selected && i == hover)
                using (var tint = new SolidBrush(Ui.AccentTint))
                    g.FillRectangle(tint, r.X, 4, r.Width, r.Height - 4);
            TextRenderer.DrawText(g, target.TabPages[i].Text, selected ? Ui.BodyBold : Ui.Body, r, selected ? Ui.Accent : Ui.TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            if (selected)
                using (var bar = new SolidBrush(Ui.Accent))
                    g.FillRectangle(bar, r.X, Height - 4, r.Width, 3);
            if (selected && Focused)
                using (var pen = new Pen(Ui.Accent) { DashStyle = DashStyle.Dot })
                    g.DrawRectangle(pen, r.X + 2, 6, r.Width - 5, r.Height - 12);
        }
        using (var line = new Pen(Ui.Border))
            g.DrawLine(line, 0, Height - 1, Width, Height - 1);
    }
}

// A rounded pill that works as a check box: filled with the accent colour when on, outlined when off.
sealed class ChipBox : CheckBox
{
    bool over;

    internal ChipBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw, true);
        AutoSize = false;
        Size = new Size(104, 32);
        Cursor = Cursors.Hand;
        Margin = new Padding(0, 0, 8, 0);
    }

    protected override void OnMouseEnter(EventArgs e) { over = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { over = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Color back = Parent != null ? Parent.BackColor : Ui.Canvas;
        e.Graphics.Clear(back.A == 255 ? back : Ui.Canvas);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(1, 1, Width - 3, Height - 3);
        Color fill = Checked ? Ui.Accent : (over ? Ui.AccentTint : Ui.Surface);
        Color line = Checked ? Ui.Accent : (over ? Ui.Accent : Ui.Border);
        Color ink = Checked ? Color.White : Ui.TextSecondary;
        if (!Enabled)
        {
            fill = Ui.SurfaceAlt;
            line = Ui.Border;
            ink = Ui.TextMuted;
        }
        using (var path = Ui.RoundRect(rect, rect.Height / 2))
        using (var brush = new SolidBrush(fill))
        using (var pen = new Pen(line))
        {
            e.Graphics.FillPath(brush, path);
            e.Graphics.DrawPath(pen, path);
        }
        if (Focused && ShowFocusCues)
        {
            var ring = new Rectangle(rect.X + 3, rect.Y + 3, rect.Width - 6, rect.Height - 6);
            using (var path = Ui.RoundRect(ring, ring.Height / 2))
            using (var pen = new Pen(Color.FromArgb(Checked ? 200 : 140, Ui.Accent)) { DashStyle = DashStyle.Dot })
                e.Graphics.DrawPath(pen, path);
        }
        TextRenderer.DrawText(e.Graphics, Text, Ui.Body, rect, ink,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }
}

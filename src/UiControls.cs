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
        return Ui.Stone;
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
        b.BackColor = Ui.Card;
        b.ForeColor = Ui.Card;
        b.FlatAppearance.MouseOverBackColor = Ui.Card;
        b.FlatAppearance.MouseDownBackColor = Ui.Card;
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
            fill = s.Kind == ButtonKind.Ghost ? BackOf(b) : Ui.SurfaceAlt;
            border = s.Kind == ButtonKind.Ghost ? BackOf(b) : Ui.Divider;
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
                    pressedFill = Ui.Mix(Ui.Success, Color.Black, 0.25f);
                    break;
                case ButtonKind.Danger:
                    baseFill = Ui.Danger;
                    hoverFill = Ui.DangerHover;
                    pressedFill = Ui.Mix(Ui.Danger, Color.Black, 0.25f);
                    break;
                case ButtonKind.Ghost:
                    baseFill = BackOf(b);
                    hoverFill = Ui.AccentTint;
                    pressedFill = Ui.AccentTintStrong;
                    break;
                default:
                    // the plain push button: a dark olive plate with a bronze edge that turns gold under the pointer
                    baseFill = Ui.SurfaceAlt;
                    hoverFill = Ui.Mix(Ui.SurfaceAlt, Ui.Accent, 0.16f);
                    pressedFill = Ui.Mix(Ui.SurfaceAlt, Ui.Accent, 0.30f);
                    break;
            }
            fill = s.Down ? pressedFill : Ui.Mix(baseFill, hoverFill, s.T);
            border = filled || s.Kind == ButtonKind.Ghost ? fill : Ui.Mix(Ui.Bronze, Ui.Accent, s.T);
            text = s.Kind == ButtonKind.Primary ? Ui.OnAccent : filled ? Ui.OnStatus : s.Kind == ButtonKind.Ghost ? Ui.Accent : Ui.Ink;
        }
        var r = new Rectangle(1, 1, b.Width - 3, b.Height - 3);
        using (var path = Ui.RoundRect(r, Ui.Radius))
        {
            using (var brush = new SolidBrush(fill))
                g.FillPath(brush, path);
            using (var pen = new Pen(border, 1f))
                g.DrawPath(pen, path);
        }
        if (b.Focused && b.Enabled)
        {
            var ring = new Rectangle(0, 0, b.Width - 1, b.Height - 1);
            using (var path = Ui.RoundRect(ring, Ui.Radius))
                using (var pen = new Pen(filled ? Ui.Ink : Ui.Accent, 1.5f))
                    g.DrawPath(pen, path);
        }
        TextRenderer.DrawText(g, b.Text, b.Font, new Rectangle(6, 0, b.Width - 12, b.Height), text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }

    // Check boxes in the gold-framed manner: a small dark box with a bronze frame and, when ticked, a gold frame and a gold tick.
    static readonly System.Collections.Generic.HashSet<CheckBox> styledChecks = new System.Collections.Generic.HashSet<CheckBox>();

    internal static void StyleCheck(CheckBox c)
    {
        if (c is ChipBox || styledChecks.Contains(c))
            return;
        styledChecks.Add(c);
        c.FlatStyle = FlatStyle.Standard;
        c.UseVisualStyleBackColor = false;
        c.Cursor = Cursors.Hand;
        Ui.DoubleBuffer(c);
        bool over = false;
        c.MouseEnter += delegate
        {
            over = true;
            c.Invalidate();
        };
        c.MouseLeave += delegate
        {
            over = false;
            c.Invalidate();
        };
        c.CheckedChanged += delegate { c.Invalidate(); };
        c.EnabledChanged += delegate { c.Invalidate(); };
        c.GotFocus += delegate { c.Invalidate(); };
        c.LostFocus += delegate { c.Invalidate(); };
        c.Disposed += delegate { styledChecks.Remove(c); };
        c.Paint += delegate(object o, PaintEventArgs e) { DrawCheck(c, e.Graphics, over); };
        c.Invalidate();
    }

    static void DrawCheck(CheckBox c, Graphics g, bool over)
    {
        if (c.Width < 8 || c.Height < 8)
            return;
        g.Clear(BackOf(c));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var box = new Rectangle(1, Math.Max(1, (c.Height - 14) / 2), 14, 14);
        bool on = c.Checked, enabled = c.Enabled;
        using (var fill = new SolidBrush(enabled ? Ui.Field : Ui.Card))
            g.FillRectangle(fill, box);
        Color edge = !enabled ? Ui.Divider : on ? Ui.Accent : over ? Ui.Mix(Ui.BorderStrong, Ui.Accent, 0.5f) : Ui.BorderStrong;
        using (var pen = new Pen(edge, 1f))
            g.DrawRectangle(pen, box.X, box.Y, box.Width - 1, box.Height - 1);
        if (on)
            Ui.DrawTick(g, Rectangle.Inflate(box, -1, -1), enabled ? Ui.Accent : Ui.TextDisabled);
        if (c.Focused)
            using (var pen = new Pen(Color.FromArgb(150, Ui.Accent)) { DashStyle = DashStyle.Dot })
                g.DrawRectangle(pen, box.X - 2, box.Y - 2, box.Width + 3, box.Height + 3);
        var area = new Rectangle(20, 0, Math.Max(0, c.Width - 20), c.Height);
        TextRenderer.DrawText(g, c.Text, c.Font, area, enabled ? c.ForeColor : Ui.TextDisabled,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
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

// A group box in the MSRO manner: a thin bronze frame with the title sunk into its top line (the line breaks behind the title) and an optional
// counter badge after it ("Saldırı 6"). The content goes into the padding area.
sealed class CardBox : Panel
{
    internal const int TitleHeight = 18;
    string title, badge;

    internal CardBox(string title)
    {
        this.title = title;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(Ui.Pad, TitleHeight + 6, Ui.Pad, Ui.Pad);
        Margin = new Padding(0, 0, 0, 10);
        BackColor = Ui.Card;
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

    // The counter drawn in the accent colour after the title; null or empty for none.
    internal string Badge
    {
        get { return badge; }
        set
        {
            badge = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent == null ? Ui.Stone : Parent.BackColor);
        var frame = new Rectangle(0, 8, Width - 1, Height - 9);
        if (frame.Width < 20 || frame.Height < 12)
            return;
        using (var fill = new SolidBrush(Ui.Card))
            g.FillRectangle(fill, frame);
        const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix |
            TextFormatFlags.SingleLine;
        string text = title ?? "";
        int max = Math.Max(10, Width - 2 * Ui.Pad - 8);
        int titleWidth = Math.Min(max, TextRenderer.MeasureText(text, Ui.Heading, new Size(2000, TitleHeight), flags).Width);
        int badgeWidth = string.IsNullOrEmpty(badge) ? 0 : TextRenderer.MeasureText(badge, Ui.Heading, new Size(2000, TitleHeight), flags).Width + 8;
        int label = Ui.Pad - 4, gap = titleWidth + badgeWidth + 12;
        // the frame, broken where the title sits
        using (var pen = new Pen(Ui.Bronze))
        {
            g.DrawLine(pen, frame.X, frame.Y, label, frame.Y);
            g.DrawLine(pen, label + gap, frame.Y, frame.Right, frame.Y);
            g.DrawLine(pen, frame.X, frame.Y, frame.X, frame.Bottom);
            g.DrawLine(pen, frame.Right, frame.Y, frame.Right, frame.Bottom);
            g.DrawLine(pen, frame.X, frame.Bottom, frame.Right, frame.Bottom);
        }
        TextRenderer.DrawText(g, text, Ui.Heading, new Rectangle(label + 6, 0, titleWidth, TitleHeight), Ui.Gold, flags | TextFormatFlags.EndEllipsis);
        if (badgeWidth > 0)
            TextRenderer.DrawText(g, badge, Ui.Heading, new Rectangle(label + 6 + titleWidth + 8, 0, badgeWidth, TitleHeight), Ui.Accent, flags);
    }
}

// Page host without header strip and without the frame the system draws around tab pages.
sealed class FlatTabs : TabControl
{
    // Switches the page with the layout frozen, then lays out the new page once.
    internal void Choose(TabPage page)
    {
        if (page == null || SelectedTab == page)
            return;
        Ui.Freeze(this);
        try
        {
            SelectedTab = page;
        }
        finally
        {
            Ui.Thaw(this);
        }
        page.PerformLayout();
    }

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
        Height = 36;
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
        g.Clear(Ui.Stone);
        var rects = TabRects();
        for (int i = 0; i < rects.Length; i++)
        {
            bool selected = i == target.SelectedIndex;
            var r = rects[i];
            if (!selected && i == hover)
                using (var tint = new SolidBrush(Ui.AccentTint))
                    g.FillRectangle(tint, r.X, 4, r.Width, r.Height - 4);
            TextRenderer.DrawText(g, target.TabPages[i].Text, selected ? Ui.BodyBold : Ui.Body, r, selected ? Ui.Gold : Ui.TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            if (selected)
                using (var bar = new SolidBrush(Ui.Accent))
                    g.FillRectangle(bar, r.X, Height - 4, r.Width, 3);
            if (selected && Focused)
                using (var pen = new Pen(Ui.Accent) { DashStyle = DashStyle.Dot })
                    g.DrawRectangle(pen, r.X + 2, 6, r.Width - 5, r.Height - 12);
        }
        using (var line = new Pen(Ui.Bronze))
            g.DrawLine(line, 0, Height - 1, Width, Height - 1);
    }
}

// A wide tile that works as a check box (MSRO's "Mob türleri" boxes): a near-black tile when off, a bronze tile with a gold frame and a gold tick when on.
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
        Color back = Parent != null ? Parent.BackColor : Ui.Stone;
        e.Graphics.Clear(back.A == 255 ? back : Ui.Stone);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        Color fill = Checked ? Ui.RowFill : (over ? Ui.SurfaceAlt : Ui.Field);
        Color line = Checked ? Ui.RowEdge : (over ? Ui.BorderStrong : Ui.Divider);
        Color ink = Checked ? Ui.Gold : Ui.TextSecondary;
        if (!Enabled)
        {
            fill = Ui.Card;
            line = Ui.Divider;
            ink = Ui.TextDisabled;
        }
        using (var brush = new SolidBrush(fill))
            e.Graphics.FillRectangle(brush, rect);
        using (var pen = new Pen(line))
            e.Graphics.DrawRectangle(pen, rect);
        var box = new Rectangle(9, (Height - 14) / 2, 14, 14);
        using (var pen = new Pen(Checked ? Ui.Accent : Ui.BorderStrong))
            e.Graphics.DrawRectangle(pen, box.X, box.Y, box.Width - 1, box.Height - 1);
        if (Checked)
            Ui.DrawTick(e.Graphics, Rectangle.Inflate(box, -1, -1), Enabled ? Ui.Accent : Ui.TextDisabled);
        if (Focused && ShowFocusCues)
            using (var pen = new Pen(Color.FromArgb(160, Ui.Accent)) { DashStyle = DashStyle.Dot })
                e.Graphics.DrawRectangle(pen, 3, 3, Width - 7, Height - 7);
        TextRenderer.DrawText(e.Graphics, Text, Ui.Body, new Rectangle(30, 0, Width - 36, Height), ink,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }
}

// A panel whose background is the stone surface (faint gradient and grain) in its BackColor; used for the outer frame of the window.
sealed class StonePanel : Panel
{
    internal StonePanel()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        Ui.PaintStone(e.Graphics, ClientRectangle, BackColor.A == 255 ? BackColor : Ui.Back, false);
    }
}

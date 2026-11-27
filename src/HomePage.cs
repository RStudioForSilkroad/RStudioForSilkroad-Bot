using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

// The page the window opens on: what the current version brought, then the notes of the earlier versions.
// It is drawn as one lacquer panel; every item carries a small tag (Yeni / Düzeltme) taken from the start of its text.
sealed class ReleaseNotesView : Control
{
    static readonly Font chipFont = new Font("Segoe UI Semibold", 8f);
    const TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.Top;
    const int Pad = 22, TagWidth = 66, TextLeft = 104, ItemGap = 9;
    readonly string current;

    internal ReleaseNotesView(string currentVersion)
    {
        current = currentVersion;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        TabStop = false;
    }

    // Sizes the view for a width; the height follows the text.
    internal void Fit(int width)
    {
        Width = width;
        Height = Flow(null, width);
    }

    // "Yeni: text" and "Düzeltme: text" are split into the tag and the text; other items have no tag.
    internal static void SplitTag(string item, out string tag, out string rest)
    {
        tag = null;
        rest = item;
        int colon = item.IndexOf(':');
        if (colon <= 0 || colon > 10)
            return;
        string head = item.Substring(0, colon);
        if (head == "Yeni" || head == "Düzeltme")
        {
            tag = head;
            rest = item.Substring(colon + 1).Trim();
        }
    }

    static int TextHeight(string text, Font font, int width)
    {
        return TextRenderer.MeasureText(text, font, new Size(width, 100000), Wrap).Height;
    }

    // Lays the notes out for a width and returns the height they need; draws them when g is given.
    int Flow(Graphics g, int width)
    {
        var current = ReleaseNotes.Find(this.current) ?? ReleaseNotes.All[0];
        int y = 20, textWidth = Math.Max(160, width - TextLeft - Pad), fullWidth = Math.Max(200, width - 2 * Pad);
        foreach (var entry in ReleaseNotes.All)
        {
            bool now = entry == current;
            if (!now && g != null)
                using (var line = new Pen(Ui.Divider))
                    g.DrawLine(line, Pad, y, width - Pad, y);
            if (!now)
                y += 14;
            if (now)
            {
                if (g != null)
                    TextRenderer.DrawText(g, "Sürüm " + entry.Version, Ui.PageTitle, new Rectangle(Pad, y, fullWidth, 30), Ui.Ink,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                y += 34;
                int h = TextHeight(entry.Title, Ui.Heading, fullWidth);
                if (g != null)
                    TextRenderer.DrawText(g, entry.Title, Ui.Heading, new Rectangle(Pad, y, fullWidth, h), Ui.Accent, Wrap);
                y += h + 16;
            }
            else
            {
                string head = entry.Version + " · " + entry.Title;
                int h = TextHeight(head, Ui.BodyBold, fullWidth);
                if (g != null)
                    TextRenderer.DrawText(g, head, Ui.BodyBold, new Rectangle(Pad, y, fullWidth, h), Ui.TextSecondary, Wrap);
                y += h + 8;
            }
            foreach (string item in entry.Items)
            {
                string tag, rest;
                SplitTag(item, out tag, out rest);
                int height = TextHeight(rest, Ui.Body, textWidth);
                if (g != null)
                {
                    DrawItemMark(g, tag, y);
                    TextRenderer.DrawText(g, rest, Ui.Body, new Rectangle(TextLeft, y, textWidth, height), now ? Ui.Ink : Ui.TextSecondary, Wrap);
                }
                y += Math.Max(height, 18) + ItemGap;
            }
            y += now ? 12 : 6;
        }
        return y + Pad - 6;
    }

    static void DrawItemMark(Graphics g, string tag, int y)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (tag == null)
        {
            int cx = Pad + TagWidth / 2, cy = y + 9;
            using (var brush = new SolidBrush(Ui.BorderStrong))
                g.FillPolygon(brush, new[] { new Point(cx - 4, cy), new Point(cx, cy - 4), new Point(cx + 4, cy), new Point(cx, cy + 4) });
            return;
        }
        Color color = tag == "Yeni" ? Ui.Jade : Ui.Warning;
        var chip = new Rectangle(Pad, y, TagWidth, 19);
        using (var path = Ui.RoundRect(chip, 9))
        {
            using (var fill = new SolidBrush(Ui.Mix(Ui.Card, color, 0.22f)))
                g.FillPath(fill, path);
            using (var pen = new Pen(Ui.Mix(Ui.Card, color, 0.55f)))
                g.DrawPath(pen, path);
        }
        TextRenderer.DrawText(g, tag, chipFont, chip, color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ui.Card);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        Flow(g, Width);
        using (var pen = new Pen(Ui.Bronze))
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}

// The first page: the changes of the version the program was updated to.
sealed partial class MemorySelectV3Form
{
    TabPage homeTab;

    void BuildHomePage()
    {
        homeTab = new TabPage("Yenilikler");
        var scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var view = new ReleaseNotesView(ReleaseNotes.CurrentVersion) { Location = new Point(0, 0) };
        scroller.Controls.Add(view);
        Action fit = delegate { view.Fit(Math.Max(360, scroller.ClientSize.Width)); };
        scroller.Resize += delegate { fit(); };
        view.MouseEnter += delegate { scroller.Focus(); };
        homeTab.Controls.Add(scroller);
        tabs.TabPages.Add(homeTab);
        fit();
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

// One line of the ordered skill list as the list view shows it (a reflection of one row of the skill grid, which stays the data model).
sealed class SkillEntry
{
    internal int Row;
    internal bool On;
    internal string Name = "", Detail = "", Kind = "";
    // A line that is never used (no hotbar key, or an old party buff): drawn in the warning colour.
    internal bool Warn;
}

// The ordered, numbered skill list of the Skill page in the MSRO manner: every line is a bronze bar with a framed kind icon on the left, "N. Name" in bold
// cream, a muted detail line, an on/off box and small up / down arrows on the right. It only draws and reports clicks; the form owns the data.
sealed class SkillListView : ScrollableControl
{
    internal const int RowHeight = 46, RowGap = 4;
    readonly List<SkillEntry> items = new List<SkillEntry>();
    int selectedRow = -1, hoverIndex = -1, hoverPart;
    string emptyText = "";

    // The grid row that was chosen / switched / asked to move up (-1) or down (+1).
    internal event Action<int> Chosen;
    internal event Action<int> Toggled;
    internal event Action<int, int> Moved;

    internal SkillListView(string emptyText)
    {
        this.emptyText = emptyText;
        AutoScroll = true;
        TabStop = true;
        Height = 5 * (RowHeight + RowGap) + RowGap;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
            ControlStyles.Selectable, true);
        Ui.DarkNative(this);
        Scroll += delegate { Invalidate(); };
    }

    internal int Count
    {
        get { return items.Count; }
    }

    internal void SetItems(List<SkillEntry> list, int selected)
    {
        int keep = -AutoScrollPosition.Y;
        items.Clear();
        items.AddRange(list);
        selectedRow = selected;
        AutoScrollMinSize = new Size(0, items.Count == 0 ? 0 : items.Count * (RowHeight + RowGap) + RowGap);
        AutoScrollPosition = new Point(0, Math.Max(0, keep));
        EnsureVisible(selected);
        Invalidate();
    }

    internal void Select(int row)
    {
        if (row == selectedRow)
            return;
        selectedRow = row;
        EnsureVisible(row);
        Invalidate();
    }

    void EnsureVisible(int row)
    {
        int at = items.FindIndex(i => i.Row == row);
        if (at < 0)
            return;
        int top = RowGap + at * (RowHeight + RowGap), bottom = top + RowHeight, view = ClientSize.Height, offset = -AutoScrollPosition.Y;
        if (top < offset)
            AutoScrollPosition = new Point(0, top - RowGap);
        else if (bottom > offset + view)
            AutoScrollPosition = new Point(0, bottom - view + RowGap);
    }

    Rectangle RowRect(int index)
    {
        return new Rectangle(RowGap, RowGap + index * (RowHeight + RowGap), Math.Max(10, ClientSize.Width - 2 * RowGap), RowHeight);
    }

    Rectangle CheckRect(Rectangle r)
    {
        return new Rectangle(r.X + 8, r.Y + (r.Height - 14) / 2, 14, 14);
    }

    Rectangle ArrowRect(Rectangle r, bool up)
    {
        return new Rectangle(r.Right - 24, r.Y + (up ? 5 : r.Height / 2 + 1), 18, r.Height / 2 - 6);
    }

    // The line and the part (1 up, 2 down, 3 on/off box, 4 the rest) under a point of the control; -1 when none.
    int HitTest(Point p, out int part)
    {
        part = 0;
        int y = p.Y - AutoScrollPosition.Y;
        for (int i = 0; i < items.Count; i++)
        {
            var r = RowRect(i);
            if (!r.Contains(p.X, y))
                continue;
            var local = new Point(p.X, y);
            part = ArrowRect(r, true).Contains(local) ? 1 : ArrowRect(r, false).Contains(local) ? 2 : Rectangle.Inflate(CheckRect(r), 4, 4).Contains(local) ? 3 : 4;
            return i;
        }
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int part, index = HitTest(e.Location, out part);
        if (index != hoverIndex || part != hoverPart)
        {
            hoverIndex = index;
            hoverPart = part;
            Cursor = part >= 1 && part <= 3 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hoverIndex = -1;
        hoverPart = 0;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        int part, index = HitTest(e.Location, out part);
        if (index < 0 || e.Button != MouseButtons.Left)
            return;
        int row = items[index].Row;
        if (part == 1 || part == 2)
        {
            if (Moved != null)
                Moved(row, part == 1 ? -1 : 1);
            return;
        }
        if (part == 3 && Toggled != null)
            Toggled(row);
        if (Chosen != null)
            Chosen(row);
    }

    protected override bool IsInputKey(Keys keyData)
    {
        return keyData == Keys.Up || keyData == Keys.Down || keyData == Keys.Space || base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        int at = items.FindIndex(i => i.Row == selectedRow);
        if (e.KeyCode == Keys.Space && at >= 0 && Toggled != null)
            Toggled(items[at].Row);
        else if ((e.KeyCode == Keys.Up || e.KeyCode == Keys.Down) && e.Control && at >= 0 && Moved != null)
            Moved(items[at].Row, e.KeyCode == Keys.Up ? -1 : 1);
        else if ((e.KeyCode == Keys.Up || e.KeyCode == Keys.Down) && items.Count > 0 && Chosen != null)
            Chosen(items[Math.Max(0, Math.Min(items.Count - 1, (at < 0 ? 0 : at) + (e.KeyCode == Keys.Up ? -1 : 1)))].Row);
        else
            return;
        e.Handled = true;
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    static string IconOf(string kind)
    {
        switch (kind)
        {
            case SkillRotation.Buff_:
                return "skill.buff";
            case SkillRotation.PartyBuff_:
                return "skill.party";
            case SkillRotation.Emergency_:
                return "skill.emergency";
            case SkillRotation.Heal_:
                return "skill.heal";
            case SkillRotation.Mana_:
                return "skill.mana";
            default:
                return "skill.attack";
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Color back = Parent != null && Parent.BackColor.A == 255 ? Parent.BackColor : Ui.Card;
        g.Clear(back);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        const TextFormatFlags text = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix |
            TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
        if (items.Count == 0)
        {
            TextRenderer.DrawText(g, emptyText, Ui.Body, new Rectangle(RowGap + 6, 8, Math.Max(10, ClientSize.Width - 20), 40), Ui.InkMuted,
                TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            return;
        }
        g.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);
        bool live = Enabled;
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var r = RowRect(i);
            if (r.Bottom + AutoScrollPosition.Y < 0 || r.Y + AutoScrollPosition.Y > ClientSize.Height)
                continue;
            bool selected = item.Row == selectedRow, over = i == hoverIndex;
            Color fill = !live || !item.On ? Ui.Mix(Ui.Card, Ui.RowFill, 0.35f) : over ? Ui.Mix(Ui.RowFill, Ui.Accent, 0.12f) : Ui.RowFill;
            Color edge = selected ? Ui.Accent : item.Warn && item.On ? Ui.Warning : item.On && live ? Ui.RowEdge : Ui.Divider;
            using (var brush = new SolidBrush(fill))
                g.FillRectangle(brush, r);
            using (var pen = new Pen(edge, selected ? 1.5f : 1f))
                g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
            // on / off box
            var box = CheckRect(r);
            using (var brush = new SolidBrush(Ui.Field))
                g.FillRectangle(brush, box);
            using (var pen = new Pen(item.On ? Ui.Accent : Ui.BorderStrong))
                g.DrawRectangle(pen, box.X, box.Y, box.Width - 1, box.Height - 1);
            if (item.On)
                Ui.DrawTick(g, Rectangle.Inflate(box, -1, -1), Ui.Accent);
            // the framed kind icon
            var icon = new Rectangle(r.X + 30, r.Y + (r.Height - 30) / 2, 30, 30);
            using (var brush = new SolidBrush(Ui.Field))
                g.FillRectangle(brush, icon);
            using (var pen = new Pen(item.On ? Ui.RowEdge : Ui.Divider))
                g.DrawRectangle(pen, icon.X, icon.Y, icon.Width - 1, icon.Height - 1);
            SroIcons.Draw(g, IconOf(item.Kind), new Rectangle(icon.X + 5, icon.Y + 5, 20, 20), item.On ? Ui.Gold : Ui.TextDisabled);
            // name and detail
            int left = icon.Right + 10, width = Math.Max(10, r.Right - 30 - left);
            string name = (i + 1) + ". " + item.Name;
            TextRenderer.DrawText(g, name, Ui.BodyBold, new Rectangle(left, r.Y + 5, width, 20), item.On && live ? Ui.Gold : Ui.TextSecondary, text);
            TextRenderer.DrawText(g, item.Detail, Ui.Small, new Rectangle(left, r.Y + 25, width, 16),
                item.Warn && item.On ? Ui.Warning : item.On && live ? Ui.TextSecondary : Ui.InkMuted, text);
            // up / down arrows
            for (int k = 0; k < 2; k++)
            {
                bool up = k == 0;
                var a = ArrowRect(r, up);
                bool possible = up ? i > 0 : i < items.Count - 1;
                bool hot = over && hoverPart == (up ? 1 : 2) && possible;
                using (var brush = new SolidBrush(hot ? Ui.SurfaceAlt : Ui.Field))
                    g.FillRectangle(brush, a);
                using (var pen = new Pen(hot ? Ui.Accent : Ui.Divider))
                    g.DrawRectangle(pen, a.X, a.Y, a.Width - 1, a.Height - 1);
                float cx = a.X + a.Width / 2f, cy = a.Y + a.Height / 2f;
                Color ink = possible ? (hot ? Ui.Accent : Ui.TextSecondary) : Ui.TextDisabled;
                using (var brush = new SolidBrush(ink))
                    g.FillPolygon(brush, up ? new[] { new PointF(cx - 4, cy + 2), new PointF(cx + 4, cy + 2), new PointF(cx, cy - 2.5f) } :
                        new[] { new PointF(cx - 4, cy - 2), new PointF(cx + 4, cy - 2), new PointF(cx, cy + 2.5f) });
            }
        }
    }
}

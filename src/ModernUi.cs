using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows.Forms;

sealed partial class MemorySelectV3Form
{
    readonly ComboBox themePicker = new ComboBox();

    void BuildModernTabs()
    {
        var page = new TabPage("Nanzhang NPC");
        tabs.TabPages.Add(page);
        var split = new SplitContainer { Size = new Size(1400, 650), Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1,
            SplitterDistance = 330 };
        page.Controls.Add(split);
        var search = new TextBox { Dock = DockStyle.Top };
        var list = new ListBox { Dock = DockStyle.Fill };
        var viewer = new MapViewer { Dock = DockStyle.Fill };
        split.Panel1.Controls.Add(list);
        split.Panel1.Controls.Add(search);
        split.Panel2.Controls.Add(viewer);
        viewer.LoadResource("nanzhang-map.jpg");
        List<NpcMapRecord> records;
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("nanzhang-data.json"))
            using (var r = new StreamReader(stream))
            {
                records = new JavaScriptSerializer { MaxJsonLength = 16000000 }.Deserialize<NpcMapData>(r.ReadToEnd()).NPCs;
            }
        Action filter = delegate
        {
            list.Items.Clear();
            foreach (var n in records.Where(n => (n.name + " " + n.id).IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) >= 0))
                list.Items.Add(n);
        };
        search.TextChanged += delegate { filter(); };
        filter();
        var hint = new Label { Dock = DockStyle.Bottom, Height = 70, Text =
            "İsim veya kimlik ara · Fare tekerleği: yakınlaştır · Sol tuş: sürükle\n134 canlı kayıt; şehir krokisi yaklaşık yapı izdüşümüdür." };
        split.Panel1.Controls.Add(hint);
        list.SelectedIndexChanged += delegate
        {
            var n = list.SelectedItem as NpcMapRecord;
            if (n != null)
            {
                hint.Text = n.name + " · Kimlik " + n.id + "\nX " + n.x.ToString("F1") + " / Y " + n.y.ToString("F1") +
                    " · Harita no " + n.number;
                viewer.FocusNpc(n.x, n.y);
            }
        };
        var themeLabel = new Label { Text = "Tema", AutoSize = true };
        themePicker.Width = 190;
        themePicker.DropDownStyle = ComboBoxStyle.DropDownList;
        themePicker.Items.AddRange(new[] { "Lacivert · Altın", "Siyah · Turkuaz", "Beyaz · Mavi", "Siyah",
            "Mavi · Gümüş", "Siyah · Zümrüt", "Gece · Mor" });
        PlaceThemePicker(themeLabel);
        headerPanel.Resize += delegate { PlaceThemePicker(themeLabel); };
        themePicker.SelectedIndexChanged += delegate
        {
            ApplyTheme();
            if (settings != null)
            {
                settings.Theme = themePicker.SelectedIndex;
                SaveSettings();
            }
        };
        themePicker.SelectedIndex = Math.Max(0, Math.Min(themePicker.Items.Count - 1, settings.Theme));
    }

    Color accentColor = Color.FromArgb(193, 151, 72), sidebarColor = Color.FromArgb(14, 24, 40);

    void ApplyTheme()
    {
        int n = Math.Max(0, Math.Min(6, themePicker.SelectedIndex));
        Color[][] palettes = { new[] { C(20, 34, 55), C(30, 48, 74), C(234, 238, 244), C(193, 151, 72), C(14, 24, 40) },
            new[] { C(22, 25, 30), C(34, 38, 46), C(234, 238, 244), C(30, 155, 161), C(15, 17, 21) }, new[] { C(245, 247,
            251), C(255, 255, 255), C(30, 45, 65), C(55, 112, 183), C(228, 233, 242) }, new[] { C(22, 25, 30), C(36, 38,
            44), C(234, 238, 244), C(96, 102, 114), C(15, 17, 21) }, new[] { C(20, 34, 55), C(30, 48, 74), C(234, 238,
            244), C(55, 112, 183), C(14, 24, 40) }, new[] { C(18, 30, 28), C(28, 46, 42), C(232, 242, 238), C(46, 160,
            120), C(11, 21, 19) }, new[] { C(26, 22, 40), C(40, 34, 60), C(236, 233, 246), C(135, 100, 210), C(18, 15,
            30) } };
        Color bg = palettes[n][0], input = palettes[n][1], fg = palettes[n][2], accent = palettes[n][3];
        sidebarColor = palettes[n][4];
        accentColor = accent;
        Color hover = Color.FromArgb(Math.Min(255, accent.R + 28), Math.Min(255, accent.G + 28), Math.Min(255, accent.B +
            28)), down = Color.FromArgb(accent.R * 4 / 5, accent.G * 4 / 5, accent.B * 4 / 5);
        Action<Control> paint = null;
        paint = c =>
        {
            if (c is MapViewer || c is DungeonRadar || c is StatPanel)
                return;
            c.BackColor = bg;
            c.ForeColor = fg;
            if (c is TextBox || c is ComboBox || c is NumericUpDown || c is ListBox)
                c.BackColor = input;
            if (c is GroupBox)
                c.ForeColor = accent;
            var b = c as Button;
            if (b != null)
                Fancy.Style(b, accent, hover, down);
            if (c is GroupBox)
                c.Font = Fancy.GroupFont;
            if (headerPanel != null && c.Parent == headerPanel && (c is Label || c is FlowLayoutPanel))
                c.BackColor = Color.Transparent;
            var g = c as DataGridView;
            if (g != null)
            {
                g.EnableHeadersVisualStyles = false;
                g.BorderStyle = BorderStyle.None;
                g.GridColor = bg;
                g.BackgroundColor = bg;
                g.DefaultCellStyle.BackColor = input;
                g.DefaultCellStyle.ForeColor = fg;
                g.DefaultCellStyle.SelectionBackColor = accent;
                g.DefaultCellStyle.SelectionForeColor = Color.White;
                g.ColumnHeadersDefaultCellStyle.BackColor = accent;
                g.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
                g.ColumnHeadersDefaultCellStyle.SelectionBackColor = accent;
                g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
                foreach (DataGridViewRow r in g.Rows)
                {
                    r.DefaultCellStyle.BackColor = input;
                    r.DefaultCellStyle.ForeColor = fg;
                }
            }
            foreach (Control child in c.Controls)
                paint(child);
        };
        paint(this);
        statPanel.PanelColor = bg;
        statPanel.TextColor = fg;
        statPanel.Invalidate();
        if (sidebar != null)
        {
            sidebar.BackColor = sidebarColor;
            sidebar.Invalidate();
        }
        if (aurora != null)
            aurora.Accent = accent;
        if (logHost != null)
            logHost.Invalidate();
        if (headerPanel != null)
            headerPanel.Invalidate(true);
    }

    Panel logHost;
    // Window title starts with the character name, so two running copies can be told apart in the taskbar.

    void UpdateTitle()
    {
        string name = snapshot == null ? null : snapshot.CharacterName;
        string title = (string.IsNullOrEmpty(name) ? "" : name + " - ") + AppPaths.AppName + " " + Application.ProductVersion;
        if (Text != title)
            Text = title;
    }

    // Theme choice sits in the top right corner, under the brand text.

    void PlaceThemePicker(Label label)
    {
        if (!headerPanel.Controls.Contains(themePicker))
        {
            headerPanel.Controls.Add(label);
            headerPanel.Controls.Add(themePicker);
            label.BringToFront();
            themePicker.BringToFront();
        }
        themePicker.Location = new Point(Math.Max(10, headerPanel.ClientSize.Width - themePicker.Width - 18), 48);
        label.Location = new Point(themePicker.Left - label.PreferredWidth - 8, 52);
    }

    // Activity log in a rounded card with a title instead of a bare text box.

    Panel BuildLogHost()
    {
        logHost = new Panel { Dock = DockStyle.Bottom, Height = 156, Padding = new Padding(16, 34, 16, 14) };
        Fancy.Buffer(logHost);
        activityView.Dock = DockStyle.Fill;
        activityView.BorderStyle = BorderStyle.None;
        activityView.Font = new Font("Consolas", 9.5f);
        logHost.Controls.Add(activityView);
        logHost.Paint += delegate(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var r = new Rectangle(8, 6, logHost.Width - 17, logHost.Height - 13);
            if (r.Width < 20 || r.Height < 20)
                return;
            using (var path = Fancy.RoundPath(r, 10))
            {
                using (var fill = new SolidBrush(activityView.BackColor))
                    g.FillPath(fill, path);
                using (var pen = new Pen(Color.FromArgb(150, accentColor), 1.2f))
                    g.DrawPath(pen, path);
            }
            using (var font = new Font("Segoe UI Semibold", 9.5f))
                using (var brush = new SolidBrush(accentColor))
                    g.DrawString("Etkinlik günlüğü", font, brush, 18, 11);
        };
        return logHost;
    }

    Panel headerPanel;
    AuroraBar aurora;

    void PaintHeader(object sender, PaintEventArgs e)
    {
        var r = headerPanel.ClientRectangle;
        if (r.Width < 2 || r.Height < 2)
            return;
        Color from = headerPanel.BackColor, to = Fancy.Mix(from, accentColor, 0.30f);
        using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(r, from, to, 0f))
            e.Graphics.FillRectangle(brush, r);
    }

    static Color C(int r, int g, int b)
    {
        return Color.FromArgb(r, g, b);
    }

    void OpenManual()
    {
        string path = Path.Combine(V3Settings.DirectoryPath, "Kullanim-Kilavuzu.pdf");
        Directory.CreateDirectory(V3Settings.DirectoryPath);
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("manual.pdf"))
            using (var file = File.Create(path))
                stream.CopyTo(file);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    sealed class NpcMapData
    {
        public List<NpcMapRecord> NPCs;
    }

    sealed class NpcMapRecord
    {
        public string name;
        public int id, number;
        public double x, y;

        public override string ToString()
        {
            return number.ToString("000") + " · " + name;
        }
    }
}

// Buttons with rounded corners, a soft gradient and a hover/press fade.

static class Fancy
{
    sealed class State
    {
        internal float T;
        internal bool Over, Down;
        internal Color Base, Hover, Pressed;
    }

    static readonly Dictionary<Button, State> states = new Dictionary<Button, State>();
    static readonly Timer timer = new Timer { Interval = 16 };
    static Font groupFont;

    static Fancy()
    {
        timer.Tick += Step;
    }

    internal static Font GroupFont
    {
        get { return groupFont ?? (groupFont = new Font("Segoe UI Semibold", 10f)); }
    }

    internal static Color Mix(Color a, Color b, float t)
    {
        return Color.FromArgb(255, (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    internal static void Buffer(Control c)
    {
        try
        {
            typeof(Control).GetProperty("DoubleBuffered", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(c,
                true, null);
        }
        catch { }
    }

    static Color BackOf(Control c)
    {
        for (Control p = c.Parent; p != null; p = p.Parent)
            if (p.BackColor.A == 255)
                return p.BackColor;
        return SystemColors.Control;
    }

    internal static void Style(Button b, Color accent, Color hover, Color down)
    {
        State s;
        if (!states.TryGetValue(b, out s))
        {
            s = new State();
            states[b] = s;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.UseVisualStyleBackColor = false;
            b.Cursor = Cursors.Hand;
            b.Font = new Font("Segoe UI Semibold", b.Font.SizeInPoints, FontStyle.Regular);
            Buffer(b);
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
            b.Disposed += delegate { states.Remove(b); };
            b.Paint += delegate(object o, PaintEventArgs e) { Draw(b, e.Graphics, s); };
        }
        s.Base = accent;
        s.Hover = hover;
        s.Pressed = down;
        // The default painter is hidden behind the custom one: same colour as the surroundings.
        Color back = BackOf(b);
        b.BackColor = back;
        b.ForeColor = back;
        b.FlatAppearance.MouseOverBackColor = back;
        b.FlatAppearance.MouseDownBackColor = back;
        b.Invalidate();
    }

    internal static GraphicsPath RoundPath(Rectangle r, int radius)
    {
        return Round(r, radius);
    }

    static GraphicsPath Round(Rectangle r, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    static void Draw(Button b, Graphics g, State s)
    {
        if (b.Width < 4 || b.Height < 4)
            return;
        Color back = BackOf(b);
        g.Clear(back);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Color c = !b.Enabled ? Mix(back, s.Base, 0.35f) : s.Down ? s.Pressed : Mix(s.Base, s.Hover, s.T);
        var r = new Rectangle(0, 0, b.Width - 1, b.Height - 1);
        using (var path = Round(r, 8))
        {
            using (var brush = new LinearGradientBrush(r, Mix(c, Color.White, 0.16f), c, 90f))
                g.FillPath(brush, path);
            if (s.T > 0.02f && b.Enabled)
                using (var pen = new Pen(Color.FromArgb((int)(110 * s.T), Color.White), 1f))
                    g.DrawPath(pen, path);
        }
        var text = new Rectangle(4, 0, b.Width - 8, b.Height);
        TextRenderer.DrawText(g, b.Text, b.Font, text, b.Enabled ? Color.White : Color.FromArgb(215, 225, 225, 225),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
    }

    static void Step(object sender, EventArgs e)
    {
        bool busy = false;
        foreach (var pair in new List<KeyValuePair < Button, State>>(states))
        {
            var s = pair.Value;
            float target = s.Over ? 1f : 0f, delta = target - s.T;
            if (Math.Abs(delta) > 0.02f)
            {
                s.T += delta * 0.3f;
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

// Thin accent bar under the header with a light that keeps sliding across.

sealed class AuroraBar : Control
{
    readonly Timer timer = new Timer { Interval = 33 };
    float offset;
    Color accent = Color.FromArgb(193, 151, 72);

    internal Color Accent
    {
        get { return accent; }
        set
        {
            accent = value;
            Invalidate();
        }
    }

    internal AuroraBar()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw,
            true);
        timer.Tick += delegate
        {
            offset = (offset + 3f) % 1000f;
            Invalidate();
        };
        timer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            timer.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var r = ClientRectangle;
        if (r.Width < 2 || r.Height < 1)
            return;
        Color dark = Fancy.Mix(Color.Black, accent, 0.25f), bright = Fancy.Mix(accent, Color.White, 0.55f);
        e.Graphics.Clear(dark);
        using (var brush = new LinearGradientBrush(new Rectangle(0, 0, 500, Math.Max(1, r.Height)), dark, dark, 0f))
        {
            var blend = new ColorBlend(5);
            blend.Colors = new[] { dark, accent, bright, accent, dark };
            blend.Positions = new[] { 0f, 0.3f, 0.5f, 0.7f, 1f };
            brush.InterpolationColors = blend;
            brush.WrapMode = WrapMode.Tile;
            brush.TranslateTransform(offset - 500f, 0);
            e.Graphics.FillRectangle(brush, r);
        }
    }
}

// Page host without header strip and without the white frame the system draws around tab pages.

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

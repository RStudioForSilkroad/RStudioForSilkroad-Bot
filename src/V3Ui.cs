using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

sealed class MapViewer : UserControl
{
    readonly Panel viewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(232, 236, 243) };
    readonly PictureBox picture = new PictureBox { SizeMode = PictureBoxSizeMode.StretchImage };
    Image source;
    double zoom = 1;
    bool dragging;
    Point dragStart, scrollStart;

    internal MapViewer()
    {
        viewport.Controls.Add(picture);
        Controls.Add(viewport);
        picture.MouseEnter += delegate { viewport.Focus(); };
        viewport.MouseWheel += Wheel;
        picture.MouseWheel += Wheel;
        picture.MouseDown += delegate(object o, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;
            dragging = true;
            dragStart = picture.PointToScreen(e.Location);
            scrollStart = new Point(-viewport.AutoScrollPosition.X, -viewport.AutoScrollPosition.Y);
            picture.Capture = true;
        };
        picture.MouseMove += delegate(object o, MouseEventArgs e)
        {
            if (!dragging)
                return;
            var q = picture.PointToScreen(e.Location);
            viewport.AutoScrollPosition = new Point(scrollStart.X + dragStart.X - q.X, scrollStart.Y + dragStart.Y - q.Y);
        };
        picture.MouseUp += delegate
        {
            dragging = false;
            picture.Capture = false;
        };
        VisibleChanged += delegate
        {
            if (Visible)
                Fit();
        };
    }

    void Wheel(object o, MouseEventArgs e)
    {
        var p = viewport.PointToClient(Cursor.Position);
        double old = zoom;
        int x = -viewport.AutoScrollPosition.X, y = -viewport.AutoScrollPosition.Y;
        SetZoom(zoom * (e.Delta > 0 ? 1.2 : 1 / 1.2));
        viewport.AutoScrollPosition = new Point((int)((x + p.X) * zoom / old - p.X), (int)((y + p.Y) * zoom / old - p.Y));
        var handled = e as HandledMouseEventArgs;
        if (handled != null)
            handled.Handled = true;
    }

    internal void LoadResource(string name)
    {
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
        {
            if (stream == null)
                throw new IOException("Gömülü harita yok: " + name);
            using (var im = Image.FromStream(stream))
            {
                picture.Image = null;
                if (source != null)
                    source.Dispose();
                source = new Bitmap(im);
                picture.Image = source;
            }
        }
        Fit();
    }

    internal void FocusNpc(double x, double y)
    {
        SetZoom(.65);
        viewport.AutoScrollPosition = new Point(Math.Max(0, (int)((160 + (x - 5500) * 5.5) * zoom - viewport.ClientSize.Width / 2)),
            Math.Max(0, (int)((220 + (-1400 - y) * 5.5) * zoom - viewport.ClientSize.Height / 2)));
    }

    internal void Fit()
    {
        if (source != null)
            SetZoom(Math.Min((double)Math.Max(100, viewport.ClientSize.Width - 20) / source.Width, (double)Math.Max(100,
                viewport.ClientSize.Height - 20) / source.Height));
    }

    void SetZoom(double value)
    {
        if (source == null)
            return;
        zoom = Math.Max(.08, Math.Min(5, value));
        picture.Size = new Size((int)(source.Width * zoom), (int)(source.Height * zoom));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && source != null)
        {
            picture.Image = null;
            source.Dispose();
            source = null;
        }
        base.Dispose(disposing);
    }
}

enum OperationMode
{
    Hunt,
    Travel,
    Grim
}

sealed partial class MemorySelectV3Form : Form
{
    readonly List<CheckBox> grimModes = new List<CheckBox>();
    readonly HashSet<string> visitedDungeons = new HashSet<string>();
    bool fillingDungeons;
    readonly Panel huntPanel = new Panel();
    readonly ListBox mobList = new ListBox();
    readonly NumericUpDown reviveSeconds = new NumericUpDown(), deathLimit = new NumericUpDown();
    readonly CheckBox autoRecovery = new CheckBox();
    readonly Label deathsLabel = new Label(), grimStatus = new Label(), gridCount = new Label();
    readonly TabControl tabs = new FlatTabs();
    readonly DataGridView dungeonGrid = new DataGridView();
    readonly MapViewer explorerView = new MapViewer(), dungeonView = new MapViewer();
    readonly DungeonRadar dungeonRadar = new DungeonRadar();
    readonly TabControl grimViews = new FlatTabs();
    readonly DeathRecovery recovery = new DeathRecovery();
    readonly GrimRun grim = new GrimRun();
    readonly TravelWatch travel = new TravelWatch();
    OperationMode operation;
    readonly HashSet<string> selectedMobs = new HashSet<string>(StringComparer.Ordinal);
    readonly StringBuilder eventLog = new StringBuilder();
    long readErrorSince;
    V3Settings settings;

    MemorySelectV3Form()
    {
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Silkroad.ico"))
            using (var icon = new Icon(stream))
                Icon = (Icon)icon.Clone();
        Text = AppPaths.AppName + " " + Application.ProductVersion;
        ClientSize = new Size(1480, 900);
        MinimumSize = new Size(1120, 700);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Ui.Canvas;
        Font = Ui.Body;
        AutoScaleMode = AutoScaleMode.Dpi;

        // Header, row 1: connection controls. Row 2: connection state, accent colour and release notes.
        var header = new Panel { Dock = DockStyle.Top, Height = 84 };
        var connection = new FlowLayoutPanel { Location = new Point(264, 12), Size = new Size(1180, 36), WrapContents = false };
        processes.Width = 300;
        processes.DropDownStyle = ComboBoxStyle.DropDownList;
        processes.Margin = new Padding(0, 3, 10, 0);
        connection.Controls.Add(processes);
        connection.Controls.Add(Btn("Yenile", RefreshProcesses, 96, true, ButtonKind.Secondary));
        connection.Controls.Add(Btn("Bağlan / Oku", Connect, 140, true, ButtonKind.Primary));
        connection.Controls.Add(Btn("Client’ı gizle", delegate { SetClientVisibility(false); }, 124, false, ButtonKind.Secondary));
        connection.Controls.Add(Btn("Client’ı göster", delegate { SetClientVisibility(true); }, 130, false, ButtonKind.Secondary));
        foreach (Control item in connection.Controls)
            if (item is Button)
                item.Margin = new Padding(0, 0, 8, 0);
        locked.Add(processes);
        header.Controls.Add(connection);
        position.SetBounds(40, 52, 900, 24);
        position.Text = "Bağlı değil.";
        position.TextAlign = ContentAlignment.MiddleLeft;
        header.Controls.Add(position);
        whatsNewButton = Btn("Yenilikler", delegate { ShowWhatsNewNow(); }, 108, false, ButtonKind.Ghost);
        header.Controls.Add(whatsNewButton);

        // Footer: the two buttons that start and stop the automation, and the current status line.
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 68 };
        var start = new Button { Text = "Başlat", Location = new Point(16, 14), Size = new Size(172, 40) };
        start.Click += delegate { Safe(StartRequested); };
        Fancy.Kind(start, ButtonKind.Success);
        var stop = new Button { Text = "Durdur", Location = new Point(198, 14), Size = new Size(140, 40) };
        stop.Click += delegate { Stop("Durduruldu."); };
        Fancy.Kind(stop, ButtonKind.Danger);
        status.Location = new Point(358, 14);
        status.Size = new Size(1000, 40);
        status.TextAlign = ContentAlignment.MiddleLeft;
        status.AutoEllipsis = true;
        footer.Controls.AddRange(new Control[] { start, stop, status });
        status.Text = "Otomasyon kapalı.";
        footer.Paint += delegate(object sender, PaintEventArgs e)
        {
            using (var line = new Pen(Ui.Border))
                e.Graphics.DrawLine(line, 0, 0, footer.Width, 0);
        };
        footer.Resize += delegate { status.Width = Math.Max(100, footer.ClientSize.Width - status.Left - Ui.Pad); };
        footerPanel = footer;

        tabs.Dock = DockStyle.Fill;
        tabs.Appearance = TabAppearance.FlatButtons;
        tabs.SizeMode = TabSizeMode.Fixed;
        tabs.ItemSize = new Size(1, 1);
        var huntTab = new TabPage("Av / Ayarlar");
        var explorerTab = new TabPage("Explorer Yerleri");
        var cryptTab = new TabPage("Grim Crypt");
        tabs.TabPages.AddRange(new[] { huntTab, explorerTab, cryptTab });
        huntPanel.Dock = DockStyle.Fill;
        huntPanel.AutoScroll = true;
        huntTab.Controls.Add(huntPanel);
        BuildHunt();
        BuildUpdateControls();
        explorerView.Dock = DockStyle.Fill;
        BuildExplorer(explorerTab, explorerView);
        BuildGrim(cryptTab);
        var workspace = new Panel { Dock = DockStyle.Fill };
        workspace.Controls.Add(tabs);
        headerPanel = header;
        Ui.DoubleBuffer(header);
        header.Paint += PaintHeader;
        header.Resize += delegate
        {
            connection.Width = Math.Max(300, header.ClientSize.Width - connection.Left - Ui.Pad);
            position.Width = Math.Max(200, header.ClientSize.Width - position.Left - 360);
            header.Invalidate();
        };
        Controls.Add(workspace);
        Controls.Add(footer);
        Controls.Add(header);
        explorerView.LoadResource("explorer-52.jpg");
        tabs.SelectedIndexChanged += delegate
        {
            stop.Text = tabs.SelectedIndex == 2 ? "Grim Crypt Durdur" : "Durdur";
            start.Text = tabs.SelectedIndex == 2 ? "Grim Crypt Başlat" : "Başlat";
            stop.Width = tabs.SelectedIndex == 2 ? 190 : 140;
            start.Width = tabs.SelectedIndex == 2 ? 200 : 172;
            stop.Left = start.Right + 10;
            status.Left = stop.Right + 20;
            status.Width = Math.Max(100, footer.ClientSize.Width - status.Left - Ui.Pad);
            if (tabs.SelectedIndex == 1)
            {
                explorerView.Fit();
                explorerMap.Refit();
            }
            if (tabs.SelectedIndex == 2)
                dungeonView.Fit();
        };
        settings = V3Settings.Load();
        RestoreSettings();
        RestoreHotkey();
        RestoreUpdateControls();
        poll.Interval = 250;
        poll.Tick += delegate
        {
            Tick();
            RecordStatusLog();
            UpdateTitle();
        };
        poll.Start();
        release.Interval = 35;
        release.Tick += delegate { ReleaseKey(); };
        RefreshProcesses();
        BuildDualAccount();
        BuildModernTabs();
        BuildCaravan();
        BuildSkillsPage();
        RestoreSkillSettings(settings);
        BuildLogTab();
        BuildNavigation(workspace);
        ApplyTheme();
        header.PerformLayout();
        PlaceHeaderRight();
        // The window is built with layout held back and laid out once when it is first shown (see OnShown).
        Ui.Freeze(this);
        startupFrozen = true;
    }

    bool startupFrozen;

    ListBox sidebar;
    int sidebarHover = -1;

    static readonly Dictionary<string, string> navIcons = new Dictionary<string, string>
    {
        { "Av / Ayarlar", "" },
        { "Explorer Yerleri", "" },
        { "Grim Crypt", "" },
        { "Güncelleme/Kılavuz", "" },
        { "Çift Hesap · Deneme", "" },
        { "Nanzhang NPC", "" },
        { "Kervan Rotası", "" },
        { "Skiller", "" },
        { "Log", "" }
    };

    // Navigation: a white sidebar with an icon and a label per page, two captions, a tinted pill for the current page.
    void BuildNavigation(Panel workspace)
    {
        sidebarHost = new Panel { Dock = DockStyle.Left, Width = 224, Padding = new Padding(0, 0, 1, 0), BackColor = Ui.Border };
        sidebar = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawVariable,
            BorderStyle = BorderStyle.None, Font = Ui.Body, BackColor = Ui.Surface };
        // Sidebar order: the caravan page belongs to the work group, right after Grim Crypt (tab indexes stay as they are).
        var order = tabs.TabPages.Cast<TabPage>().ToList();
        var caravanPage = order.FirstOrDefault(t => t.Text == "Kervan Rotası");
        if (caravanPage != null)
        {
            order.Remove(caravanPage);
            order.Insert(3, caravanPage);
        }
        // The skills page sits with the work pages too, after the caravan page.
        var skillsPage = order.FirstOrDefault(t => t.Text == "Skiller");
        if (skillsPage != null)
        {
            order.Remove(skillsPage);
            order.Insert(4, skillsPage);
        }
        foreach (TabPage page in order)
            sidebar.Items.Add(page);
        Func<int, string> caption = i => i == 0 ? "ÇALIŞMA" : i == 5 ? "ARAÇLAR" : null;
        sidebar.MeasureItem += delegate(object sender, MeasureItemEventArgs e)
        {
            e.ItemHeight = (caption(e.Index) == null ? 0 : 34) + 44;
        };
        sidebar.DrawItem += delegate(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0)
                return;
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var back = new SolidBrush(Ui.Surface))
                g.FillRectangle(back, e.Bounds);
            var bounds = e.Bounds;
            string title = caption(e.Index);
            if (title != null)
            {
                TextRenderer.DrawText(g, title, Ui.Small, new Rectangle(bounds.X + 22, bounds.Y + 8, bounds.Width - 30, 22), Ui.TextMuted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                bounds.Y += 34;
                bounds.Height -= 34;
            }
            bool selected = (e.State & DrawItemState.Selected) != 0, hover = e.Index == sidebarHover;
            var pill = new Rectangle(bounds.X + 10, bounds.Y + 2, bounds.Width - 21, bounds.Height - 4);
            if (selected || hover)
                using (var path = Ui.RoundRect(pill, 6))
                    using (var fill = new SolidBrush(selected ? Ui.AccentTint : Ui.SurfaceAlt))
                        g.FillPath(fill, path);
            if (selected)
                using (var bar = new SolidBrush(Ui.Accent))
                    g.FillRectangle(bar, pill.X, pill.Y + 8, 3, pill.Height - 16);
            var page = (TabPage)sidebar.Items[e.Index];
            Color ink = selected ? Ui.Accent : Ui.TextSecondary;
            int textLeft = pill.X + 16;
            string glyph;
            if (Ui.IconsAvailable && navIcons.TryGetValue(page.Text, out glyph))
            {
                TextRenderer.DrawText(g, glyph, Ui.IconFont, new Rectangle(pill.X + 12, pill.Y, 28, pill.Height), ink,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                textLeft = pill.X + 46;
            }
            TextRenderer.DrawText(g, page.Text, selected ? Ui.BodyBold : Ui.Body, new Rectangle(textLeft, pill.Y, pill.Right - textLeft - 6,
                pill.Height), selected ? Ui.Accent : Ui.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
            if ((e.State & DrawItemState.Focus) != 0 && sidebar.Focused)
                using (var ring = new Pen(Ui.Accent, 2f))
                    using (var path = Ui.RoundRect(pill, 6))
                        g.DrawPath(ring, path);
        };
        sidebar.MouseMove += delegate(object sender, MouseEventArgs e)
        {
            int index = sidebar.IndexFromPoint(e.Location);
            if (index != sidebarHover)
            {
                sidebarHover = index;
                sidebar.Invalidate();
            }
        };
        sidebar.MouseLeave += delegate
        {
            sidebarHover = -1;
            sidebar.Invalidate();
        };
        sidebar.SelectedItem = tabs.SelectedTab;
        sidebar.SelectedIndexChanged += delegate
        {
            var page = sidebar.SelectedItem as TabPage;
            if (page != null && tabs.SelectedTab != page)
                ((FlatTabs)tabs).Choose(page);
        };
        tabs.SelectedIndexChanged += delegate
        {
            if (tabs.SelectedTab != null)
                sidebar.SelectedItem = tabs.SelectedTab;
        };
        sidebarHost.Controls.Add(sidebar);
        workspace.Controls.Add(sidebarHost);
        if (tabs.SelectedTab != null)
            sidebar.SelectedItem = tabs.SelectedTab;
    }

    void Safe(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            Stop(e.Message);
            Log(e.ToString());
        }
    }

    // A label that is as wide as its text, with the text centred on the height of a normal control.
    static Label LabelOf(string text, int width)
    {
        return new Label { Text = text, Width = width, Height = 30, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 1, 6, 1) };
    }

    static Label Caption(string text)
    {
        int w = TextRenderer.MeasureText(text, Ui.Body).Width + 10;
        return LabelOf(text, w);
    }

    static Label Hint(string text, int width = 0)
    {
        var label = new Label { Text = text, AutoSize = false, Height = 22, TextAlign = ContentAlignment.MiddleLeft, Tag = "muted", Margin = new Padding(0, 0, 0, 4) };
        if (width > 0)
            label.Width = width;
        return label;
    }

    Button Btn(string text, Action action, int width = 130, bool freeze = true, ButtonKind kind = ButtonKind.Secondary)
    {
        var b = new Button { Text = text, Width = width, Height = Ui.ControlHeight, Margin = new Padding(0, 0, 8, 6) };
        b.Click += delegate { Safe(action); };
        Fancy.Kind(b, kind);
        if (freeze)
            locked.Add(b);
        return b;
    }

    void Num(NumericUpDown n, decimal min, decimal max, decimal val, int places = 0, int width = 95)
    {
        n.Minimum = min;
        n.Maximum = max;
        n.Value = val;
        n.DecimalPlaces = places;
        n.Width = width;
        n.Height = 29;
        n.Margin = new Padding(0, 5, 12, 1);
        n.ThousandsSeparator = false;
        locked.Add(n);
    }

    void ConfigureCheck(CheckBox c, string text, bool value, int width)
    {
        c.Text = text;
        c.Checked = value;
        c.AutoSize = true;
        c.MinimumSize = new Size(0, 30);
        c.Margin = new Padding(0, 1, 16, 1);
        locked.Add(c);
    }

    // A single card at the top of a page, used by the settings-like pages; returns the area to put the content in.
    FlowLayoutPanel PageCard(TabPage page, string title, int width)
    {
        var scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var card = new CardBox(title) { MinimumSize = new Size(width, 0), Margin = new Padding(0), Location = new Point(Ui.Pad, Ui.Pad) };
        var content = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Dock = DockStyle.Top, Margin = new Padding(0) };
        card.Controls.Add(content);
        scroller.Controls.Add(card);
        page.Controls.Add(scroller);
        return content;
    }

    // ---- Hunt page: cards in two columns when there is room, one column otherwise ----

    FlowLayoutPanel huntRoot, leftColumn, rightColumn;
    TableLayoutPanel huntColumns;
    CardBox liveCard;
    readonly List<CardBox> leftCards = new List<CardBox>(), rightCards = new List<CardBox>();
    readonly List<FlowLayoutPanel> huntRows = new List<FlowLayoutPanel>();
    readonly List<Control> huntStretch = new List<Control>();

    // column: 0 = left, 1 = right, 2 = full width below both columns
    FlowLayoutPanel Card(string title, int column = 0)
    {
        var g = new CardBox(title);
        var f = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection =
            FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Top, Margin = new Padding(0) };
        g.Controls.Add(f);
        if (column == 2)
            liveCard = g;
        else
            (column == 0 ? leftCards : rightCards).Add(g);
        return f;
    }

    FlowLayoutPanel Line(FlowLayoutPanel card)
    {
        var r = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true,
            Margin = new Padding(0, 2, 0, 2) };
        card.Controls.Add(r);
        huntRows.Add(r);
        return r;
    }

    // Controls that should always be as wide as the card's inner area.
    T Stretch<T>(T control) where T : Control
    {
        control.Margin = new Padding(0, 0, 0, 6);
        huntStretch.Add(control);
        return control;
    }

    bool arranged, arrangedTwo;
    int arrangedUsable;
    internal static int ArrangeRuns;

    void ArrangeHunt()
    {
        if (huntRoot == null)
            return;
        // Before the window has been laid out the panel has no width yet; assume the default window size.
        int panelWidth = huntPanel.ClientSize.Width < 200 ? 1240 : huntPanel.ClientSize.Width;
        int usable = Math.Max(520, panelWidth - huntRoot.Padding.Horizontal);
        bool two = usable >= 1060;
        int gap = Ui.Pad;
        int cardWidth = two ? (usable - gap) / 2 : usable;
        // The panel is resized many times while tabs switch and scrollbars appear; rebuilding all cards each time is what made the page slow.
        if (arranged && two == arrangedTwo && usable == arrangedUsable)
            return;
        arranged = true;
        arrangedTwo = two;
        arrangedUsable = usable;
        ArrangeRuns++;
        huntRoot.SuspendLayout();
        huntColumns.SuspendLayout();
        var all = leftCards.Concat(rightCards).ToList();
        foreach (var card in all)
            card.Parent = null;
        var primary = two ? leftCards : all;
        foreach (var card in primary)
            leftColumn.Controls.Add(card);
        if (two)
            foreach (var card in rightCards)
                rightColumn.Controls.Add(card);
        huntColumns.ColumnStyles[0].SizeType = SizeType.Absolute;
        huntColumns.ColumnStyles[0].Width = cardWidth;
        huntColumns.ColumnStyles[1].SizeType = SizeType.Absolute;
        huntColumns.ColumnStyles[1].Width = two ? cardWidth + gap : 0;
        rightColumn.Visible = two;
        rightColumn.Margin = new Padding(gap, 0, 0, 0);
        foreach (var card in all)
            card.MinimumSize = new Size(cardWidth, 0);
        if (liveCard != null)
            liveCard.MinimumSize = new Size(usable, 0);
        int inner = cardWidth - 2 * Ui.Pad - 2, liveInner = usable - 2 * Ui.Pad - 2;
        foreach (var row in huntRows)
        {
            var host = row.Parent == null ? null : row.Parent.Parent as CardBox;
            row.MaximumSize = new Size(host == liveCard ? liveInner : inner, 0);
        }
        foreach (var control in huntStretch)
        {
            var host = control.Parent == null || control.Parent.Parent == null ? null : control.Parent.Parent.Parent as CardBox;
            control.Width = host == liveCard ? liveInner : inner;
        }
        huntColumns.ResumeLayout(true);
        huntRoot.ResumeLayout(true);
    }

    void BuildHunt()
    {
        huntRoot = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(Ui.Pad) };
        huntPanel.Controls.Add(huntRoot);
        huntColumns = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 1,
            Margin = new Padding(0) };
        huntColumns.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 600));
        huntColumns.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 600));
        huntColumns.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        leftColumn = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection =
            FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0), Dock = DockStyle.Top };
        rightColumn = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection =
            FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0), Dock = DockStyle.Top };
        huntColumns.Controls.Add(leftColumn, 0, 0);
        huntColumns.Controls.Add(rightColumn, 1, 0);
        huntRoot.Controls.Add(huntColumns);

        // Left column: what to hunt and where
        BuildStatCard();
        var card = Card("Hedefler");
        var row = Line(card);
        row.Controls.Add(Caption("Yaratık"));
        monsters.Width = 250;
        monsters.DropDownStyle = ComboBoxStyle.DropDownList;
        monsters.AutoCompleteSource = AutoCompleteSource.ListItems;
        monsters.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        monsters.Margin = new Padding(0, 4, 8, 0);
        monsters.Items.AddRange(names.Values.Distinct().OrderBy(n => n).Cast<object>().ToArray());
        row.Controls.Add(monsters);
        locked.Add(monsters);
        row.Controls.Add(Btn("+ Ekle", AddMob, 84, true, ButtonKind.Primary));
        row.Controls.Add(Btn("− Çıkar", RemoveMob, 84));
        row.Controls.Add(Btn("Yakındaki isimler", Nearby, 150));
        row = Line(card);
        row.Controls.Add(Stretch(Hint("Hedef listesi · saldırılacak yaratıklar")));
        row = Line(card);
        mobList.Height = 112;
        mobList.BorderStyle = BorderStyle.FixedSingle;
        mobList.IntegralHeight = false;
        row.Controls.Add(Stretch(mobList));
        locked.Add(mobList);
        card = Card("Av bölgesi");
        row = Line(card);
        Num(xBox, -25000, 23000, 0, 1, 112);
        Num(yBox, -17000, 6500, 0, 1, 112);
        row.Controls.Add(Caption("X"));
        row.Controls.Add(xBox);
        row.Controls.Add(Caption("Y"));
        row.Controls.Add(yBox);
        row = Line(card);
        row.Controls.Add(Btn("Mevcut X/Y", FillCurrent, 116));
        row.Controls.Add(Btn("Bölgeyi set et", SetCenter, 132, true, ButtonKind.Primary));
        row = Line(card);
        area.Height = 40;
        area.AutoSize = false;
        area.TextAlign = ContentAlignment.MiddleLeft;
        area.Tag = "muted";
        area.Text = "Bölge set edilmedi. Mob eklemek merkezi değiştirmez.";
        row.Controls.Add(Stretch(area));
        row = Line(card);
        Num(radius, 1, 500, 150);
        Num(timeout, 5, 120, 10);
        row.Controls.Add(Caption("Range"));
        row.Controls.Add(radius);
        row.Controls.Add(Caption("Takılma (sn)"));
        row.Controls.Add(timeout);
        row = Line(card);
        row.Controls.Add(Stretch(Hint("Saldırılamayan hedef 10 sn sonra atlanır.")));
        card = Card("Saldırı");
        row = Line(card);
        keys.Width = 220;
        keys.Text = "1 2 3 4 5 6";
        keys.Margin = new Padding(0, 3, 12, 0);
        row.Controls.Add(Caption("Tuşlar"));
        row.Controls.Add(keys);
        locked.Add(keys);
        Num(delay, 60, 10000, 100);
        row.Controls.Add(Caption("Tuş aralığı (ms)"));
        row.Controls.Add(delay);
        row = Line(card);
        ConfigureCheck(sendKeys, "Hedef doğrulanınca tuşlara bas", false, 0);
        row.Controls.Add(sendKeys);
        row = Line(card);
        ConfigureCheck(goHome, "Hedef yoksa / alan dışındaysa merkeze dön", true, 0);
        row.Controls.Add(goHome);

        // Right column: control, maintenance, travel
        card = Card("Kontrol", 1);
        row = Line(card);
        row.Controls.Add(Caption("Başlat / Durdur kısayolu"));
        shortcutLabel.AutoSize = false;
        shortcutLabel.Size = new Size(120, 30);
        shortcutLabel.TextAlign = ContentAlignment.MiddleCenter;
        shortcutLabel.BorderStyle = BorderStyle.FixedSingle;
        shortcutLabel.Text = "Atanmadı";
        shortcutLabel.Margin = new Padding(0, 1, 10, 1);
        row.Controls.Add(shortcutLabel);
        row.Controls.Add(Btn("Tuş ata / değiştir", ChooseHotkey, 160));
        card = Card("Otomatik bakım", 1);
        row = Line(card);
        ConfigureCheck(autoZerk, "Berserk 5/5 → Tab", true, 0);
        berserkClass.Width = 170;
        berserkClass.DropDownStyle = ComboBoxStyle.DropDownList;
        berserkClass.Items.AddRange(V2Rules.BerserkClasses);
        berserkClass.SelectedItem = "Giant";
        berserkClass.Margin = new Padding(0, 4, 0, 1);
        locked.Add(berserkClass);
        row.Controls.Add(autoZerk);
        row.Controls.Add(berserkClass);
        row = Line(card);
        ConfigureCheck(autoSpeed, "Hızlı Koşma Taşı Kullan %100", true, 0);
        row.Controls.Add(autoSpeed);
        row = Line(card);
        ConfigureCheck(autoRepair, "Ekipman dayanıklılığı ≤ 4 → Repair", true, 0);
        row.Controls.Add(autoRepair);
        row = Line(card);
        row.Controls.Add(Btn("Bakım hata kilidini sıfırla", delegate
        {
            transaction.Reset();
            maintenance.Text = transaction.Message;
        }, 220));
        row = Line(card);
        ConfigureCheck(autoRecovery, "Ölümde şehre dön + Reverse", true, 0);
        row.Controls.Add(autoRecovery);
        row = Line(card);
        Num(reviveSeconds, 0, 3600, 30);
        Num(deathLimit, 1, 1000, 5);
        row.Controls.Add(Caption("Bekle (sn)"));
        row.Controls.Add(reviveSeconds);
        row.Controls.Add(Caption("Ölüm sınırı"));
        row.Controls.Add(deathLimit);
        row = Line(card);
        deathsLabel.AutoSize = false;
        deathsLabel.Size = new Size(230, 30);
        deathsLabel.TextAlign = ContentAlignment.MiddleLeft;
        deathsLabel.Margin = new Padding(0, 0, 8, 6);
        row.Controls.Add(deathsLabel);
        row.Controls.Add(Btn("Ölüm sayacını sıfırla", delegate
        {
            recovery.ResetCount();
            SaveSettings();
            UpdateDisplay();
        }, 190));
        row = Line(card);
        ConfigureCheck(uniqueAlertBox, "Yakında unique canavar çıkınca sesle uyar", false, 0);
        row.Controls.Add(uniqueAlertBox);
        card = Card("Yolculuk", 1);
        row = Line(card);
        ConfigureCheck(travelMount, "Dungeon’a giderken ata bin · Oyundaki aktif mount", false, 0);
        row.Controls.Add(travelMount);
        row = Line(card);
        ConfigureCheck(backgroundWalk, "Yürüyüş işleri arka planda da çalışsın (Explorer, kervan, dungeon yolu)", true, 0);
        row.Controls.Add(backgroundWalk);
        row = Line(card);
        ConfigureCheck(notifyDone, "İş bitince bildirim ver (ses, balon, görev çubuğu)", true, 0);
        row.Controls.Add(notifyDone);
        row = Line(card);
        ConfigureCheck(returnBeforeTravel, "Farklı bölgeye giderken önce Return Scroll kullan · İsteğe bağlı", false, 0);
        row.Controls.Add(returnBeforeTravel);

        // Full width: live view of the monsters in range
        card = Card("Canlı durum", 2);
        row = Line(card);
        details.AutoSize = false;
        details.Height = 30;
        details.TextAlign = ContentAlignment.MiddleLeft;
        row.Controls.Add(Stretch(details));
        grid.Height = 210;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.RowHeadersVisible = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        foreach (string text in new[] { "UID", "Yaratık", "Sınıf", "X", "Y", "Merkeze mesafe", "HP", "Durum" })
            grid.Columns.Add(text, text);
        row = Line(card);
        row.Controls.Add(Stretch(grid));
        maintenance.AutoSize = false;
        maintenance.Height = 32;
        maintenance.TextAlign = ContentAlignment.MiddleLeft;
        maintenance.Tag = "muted";
        maintenance.Text = transaction.Message;
        row = Line(card);
        row.Controls.Add(Stretch(maintenance));
        huntRoot.Controls.Add(liveCard);
        huntPanel.SizeChanged += delegate { ArrangeHunt(); };
        huntPanel.MouseEnter += delegate
        {
            if (!(ActiveControl is TextBoxBase) && !(ActiveControl is NumericUpDown) && !(ActiveControl is ComboBox))
                huntPanel.Focus();
        };
        ArrangeHunt();
    }

    void AddMob()
    {
        string name = monsters.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Önce yaratık seç.");
        if (selectedMobs.Add(name))
        {
            mobList.Items.Add(name);
            SaveSettings();
        }
        else
            status.Text = "Bu yaratık zaten listede.";
    }

    void RemoveMob()
    {
        string name = mobList.SelectedItem as string;
        if (name != null)
        {
            selectedMobs.Remove(name);
            mobList.Items.Remove(name);
            SaveSettings();
        }
    }

    void Log(string text)
    {
        string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " · " + text;
        eventLog.AppendLine(line);
        try
        {
            Directory.CreateDirectory(V3Settings.DirectoryPath);
            File.AppendAllText(Path.Combine(V3Settings.DirectoryPath, "events.log"), line + Environment.NewLine,
                Encoding.UTF8);
        }
        catch { }
    }

    void RestoreSettings()
    {
        RestoreQueue();
        foreach (string id in settings.VisitedDungeons ?? new List<string>())
            visitedDungeons.Add(id);
        RefreshDungeonGrid();
        foreach (string name in settings.Mobs ?? new List<string>())
            if (!string.IsNullOrWhiteSpace(name) && selectedMobs.Add(name))
                mobList.Items.Add(name);
        returnBeforeTravel.Checked = settings.ReturnBeforeTravel;
        travelMount.Checked = settings.TravelMount;
        backgroundWalk.Checked = settings.BackgroundWalk;
        notifyDone.Checked = settings.NotifyDone;
        uniqueAlertBox.Checked = settings.UniqueAlert;
        autoEnterGrim.Checked = settings.AutoEnterGrim;
        if (settings.ExplorerDone == null)
            settings.ExplorerDone = new List<int>();
        explorerSyncing = true;
        try
        {
            explorerSkipDone.Checked = settings.ExplorerSkipDone;
            SetValue(explorerModel, settings.ExplorerModel);
            explorerTalk.Checked = settings.ExplorerTalk;
            SetValue(explorerOption, settings.ExplorerTalkOption < 1 ? 1 : settings.ExplorerTalkOption);
        }
        finally
        {
            explorerSyncing = false;
        }
        RefreshExplorerUi();
        SetValue(xBox, settings.X);
        SetValue(yBox, settings.Y);
        SetValue(radius, settings.Range);
        SetValue(timeout, settings.Timeout == 15 ? 10 : settings.Timeout);
        SetValue(delay, settings.Delay);
        SetValue(reviveSeconds, settings.ReviveSeconds);
        SetValue(deathLimit, settings.DeathLimit);
        keys.Text = settings.Keys ?? "1";
        berserkClass.SelectedItem = V2Rules.BerserkClasses.Contains(settings.BerserkClass) ? settings.BerserkClass :
            "Giant";
        sendKeys.Checked = true;
        goHome.Checked = settings.GoHome;
        autoZerk.Checked = settings.Berserk;
        autoSpeed.Checked = settings.Speed;
        autoRepair.Checked = settings.Repair;
        autoRecovery.Checked = settings.Recovery;
        recovery.Deaths = Math.Max(0, settings.Deaths);
        deathsLabel.Text = "Ölüm sayısı: " + recovery.Deaths + " / " + deathLimit.Value;
    }

    static void SetValue(NumericUpDown n, double v)
    {
        if (double.IsNaN(v) || double.IsInfinity(v))
            return;
        n.Value = (decimal)Math.Max((double)n.Minimum, Math.Min((double)n.Maximum, v));
    }

    void SaveSettings()
    {
        if (settings == null)
            return;
        settings.DungeonQueue = queue.Select(item => item.Code).ToList();
        settings.QueueReturnScroll = queueReturnScroll.Checked;
        settings.ReturnBeforeTravel = returnBeforeTravel.Checked;
        settings.TravelMount = travelMount.Checked;
        settings.BackgroundWalk = backgroundWalk.Checked;
        settings.NotifyDone = notifyDone.Checked;
        settings.UniqueAlert = uniqueAlertBox.Checked;
        SaveSkillSettings(settings);
        settings.AutoEnterGrim = autoEnterGrim.Checked;
        settings.ExplorerSkipDone = explorerSkipDone.Checked;
        settings.ExplorerModel = (int)explorerModel.Value;
        settings.ExplorerTalk = explorerTalk.Checked;
        settings.ExplorerTalkOption = (int)explorerOption.Value;
        settings.VisitedDungeons = visitedDungeons.OrderBy(id => id).ToList();
        settings.Mobs = selectedMobs.OrderBy(x => x).ToList();
        settings.X = (double)xBox.Value;
        settings.Y = (double)yBox.Value;
        settings.Range = (double)radius.Value;
        settings.Timeout = (int)timeout.Value;
        settings.Delay = (int)delay.Value;
        settings.ReviveSeconds = (int)reviveSeconds.Value;
        settings.DeathLimit = (int)deathLimit.Value;
        settings.Deaths = recovery.Deaths;
        settings.Keys = keys.Text;
        settings.BerserkClass = berserkClass.SelectedItem as string;
        settings.SendKeys = sendKeys.Checked;
        settings.GoHome = goHome.Checked;
        settings.Berserk = autoZerk.Checked;
        settings.Speed = autoSpeed.Checked;
        settings.Repair = autoRepair.Checked;
        settings.Recovery = autoRecovery.Checked;
        settings.AutoUpdate = autoUpdate.Checked;
        settings.UpdateManifestUrl = updateUrl.Text.Trim();
        settings.HotkeyKey = shortcutKey;
        settings.HotkeyModifiers = shortcutMods;
        settings.Save();
    }

    void BuildGrim(TabPage outerPage)
    {
        var page = outerPage;
        page.AutoScroll = true;
        page.AutoScrollMinSize = new Size(960, 720);
        // Row 1: difficulty chips on the left, the walk button on the right.
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(Ui.Pad, 12, Ui.Pad, 2) };
        foreach (string modeName in new[] { "Novice", "Expert", "Master", "Solo Trial" })
        {
            var chip = new ChipBox { Text = modeName, Checked = modeName == "Solo Trial" };
            grimModes.Add(chip);
            chip.CheckedChanged += delegate { RefreshDungeonGrid(); };
            toolbar.Controls.Add(chip);
        }
        var walk = Btn("Girişe yürü", WalkDungeon, 140, true, ButtonKind.Primary);
        walk.Margin = new Padding(16, 0, 0, 0);
        walk.Height = 32;
        toolbar.Controls.Add(walk);
        // Row 2: the auto-enter switch and the one-line beta note.
        var options = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(Ui.Pad, 2, Ui.Pad, 6) };
        autoEnterGrim.Text = "Girişe varınca Solo Trial’a gir ve otomatik avlan";
        autoEnterGrim.AutoSize = true;
        autoEnterGrim.Margin = new Padding(0, 6, 24, 0);
        locked.Add(autoEnterGrim);
        options.Controls.Add(autoEnterGrim);
        var grimNote = new Label { Text = "Beta · 72/72 canlı bitiriş testi henüz yapılmadı", AutoSize = true, Tag = "warn",
            Margin = new Padding(0, 7, 0, 0) };
        options.Controls.Add(grimNote);
        // Two separate parts, one above the other: the dungeon list with the run order on top, the three views (picture, live inside
        // view, world map) across the whole width below.
        var split = new SplitContainer { Size = new Size(1200, 700), Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
            FixedPanel = FixedPanel.Panel1, SplitterDistance = 310, SplitterWidth = 6 };
        split.Panel1MinSize = 220;
        split.Panel2MinSize = 260;
        split.Panel1.Padding = new Padding(Ui.Pad, 0, Ui.Pad, 4);
        split.Panel2.Padding = new Padding(Ui.Pad, 4, Ui.Pad, Ui.Pad);
        dungeonGrid.Dock = DockStyle.Fill;
        dungeonGrid.ReadOnly = false;
        dungeonGrid.AllowUserToAddRows = false;
        dungeonGrid.RowHeadersVisible = false;
        dungeonGrid.MultiSelect = false;
        dungeonGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        dungeonGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        var visitedColumn = new DataGridViewCheckBoxColumn { Name = "Visited", HeaderText = "Girdim", FillWeight = 48 };
        dungeonGrid.Columns.Add(visitedColumn);
        foreach (string c in new[] { "Kod", "Bölge / Dungeon", "Şehir", "Mod", "Giriş X", "Giriş Y", "Instance",
            "İç rota" })
        {
            int column = dungeonGrid.Columns.Add(c, c);
            var col = dungeonGrid.Columns[column];
            col.ReadOnly = true;
            col.Width = c == "Şehir" ? 150 : c == "Kod" ? 52 : 90;
            // Coordinates, instance id and route note stay in the data (queue and walker read them) but not on screen.
            if (c == "Giriş X" || c == "Giriş Y" || c == "Instance" || c == "İç rota")
                col.Visible = false;
        }
        dungeonGrid.Columns["Bölge / Dungeon"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        dungeonGrid.Columns["Bölge / Dungeon"].MinimumWidth = 150;
        visitedColumn.Width = 72;
        visitedColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        dungeonGrid.CurrentCellDirtyStateChanged += delegate
        {
            if (dungeonGrid.IsCurrentCellDirty && dungeonGrid.CurrentCell.ColumnIndex == 0)
                dungeonGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        dungeonGrid.CellValueChanged += delegate(object source, DataGridViewCellEventArgs ev)
        {
            if (fillingDungeons || ev.RowIndex < 0 || ev.ColumnIndex != 0)
                return;
            var row = dungeonGrid.Rows[ev.RowIndex];
            var location = row.Tag as DungeonLocation;
            if (location == null)
                return;
            string id = location.Code + "|" + location.Mode;
            if (Convert.ToBoolean(row.Cells[0].Value))
                visitedDungeons.Add(id);
            else
                visitedDungeons.Remove(id);
            row.DefaultCellStyle.BackColor = visitedDungeons.Contains(id) ? Ui.SuccessTint : dungeonGrid.DefaultCellStyle.BackColor;
            SaveSettings();
        };
        dungeonGrid.SelectionChanged += delegate
        {
            var d = SelectedDungeon();
            if (d != null && !fillingDungeons)
            {
                ShowDungeonImage(d);
            }
        };
        var gridHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), Tag = "border" };
        gridHost.Controls.Add(dungeonGrid);
        gridCount.Dock = DockStyle.Top;
        gridCount.Height = 34;
        gridCount.Font = Ui.Heading;
        gridCount.Padding = new Padding(12, 0, 0, 0);
        gridCount.TextAlign = ContentAlignment.MiddleLeft;
        gridHost.Controls.Add(gridCount);
        split.Panel1.Controls.Add(gridHost);
        BuildQueuePanel(split.Panel1);
        grimViews.Dock = DockStyle.Fill;
        grimViews.Appearance = TabAppearance.FlatButtons;
        grimViews.SizeMode = TabSizeMode.Fixed;
        grimViews.ItemSize = new Size(1, 1);
        var visual = new TabPage("Dungeon görseli");
        dungeonView.Dock = DockStyle.Fill;
        visual.Controls.Add(dungeonView);
        var live = new TabPage("Canlı iç görünüm");
        live.Controls.Add(dungeonRadar);
        grimViews.TabPages.AddRange(new[] { visual, live });
        grimViews.SelectedIndexChanged += delegate
        {
            if (grimViews.SelectedIndex == 0)
                dungeonView.Fit();
        };
        var mapHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), Tag = "border" };
        mapHost.Controls.Add(grimViews);
        mapHost.Controls.Add(new TabStrip(grimViews));
        split.Panel2.Controls.Add(mapHost);
        grimStatus.Dock = DockStyle.Bottom;
        grimStatus.Height = 36;
        grimStatus.Font = Ui.Small;
        grimStatus.TextAlign = ContentAlignment.MiddleLeft;
        grimStatus.Padding = new Padding(Ui.Pad, 0, Ui.Pad, 0);
        grimStatus.Tag = "muted";
        grimStatus.Text = "Dungeon seç. Otomatik giriş açıksa Solo Trial’a girer; kapalıysa girişte bekler.";
        page.Controls.Add(split);
        page.Controls.Add(grimStatus);
        page.Controls.Add(options);
        page.Controls.Add(toolbar);
        BuildWorldMap();
        RefreshDungeonGrid();
        dungeonView.LoadResource("Grim.D01.jpg");
    }

    void RefreshDungeonGrid()
    {
        fillingDungeons = true;
        try
        {
            dungeonGrid.Rows.Clear();
            foreach (var d in V3Data.Dungeons.Where(d => grimModes.Any(c => c.Checked && c.Text == d.Mode)))
            {
                bool visited = visitedDungeons.Contains(d.Code + "|" + d.Mode);
                int r = dungeonGrid.Rows.Add(visited, d.Code, d.Name, d.Area, d.Mode, d.X.ToString("F1"), d.Y.ToString("F1"),
                    d.InstanceId, d.Mode == "Solo Trial" ? "Kayıtlı harita · alan kontrolü" :
                    "Canlı tarama · alan kontrolü");
                dungeonGrid.Rows[r].Tag = d;
                dungeonGrid.Rows[r].DefaultCellStyle.BackColor = visited ? Ui.SuccessTint : dungeonGrid.DefaultCellStyle.BackColor;
            }
        }
        finally
        {
            gridCount.Text = "Dungeon listesi · " + dungeonGrid.Rows.Count + " kayıt";
            fillingDungeons = false;
            var selected = SelectedDungeon();
            if (selected != null)
                ShowDungeonImage(selected);
        }
    }

    void ShowDungeonImage(DungeonLocation d)
    {
        dungeonRadar.Preview = d;
        dungeonRadar.Invalidate();
        dungeonView.LoadResource("Grim." + d.Code + ".jpg");
        if (grimViews.SelectedTab != worldTab)
            grimViews.SelectedIndex = 0;
        dungeonView.Fit();
        SyncWorldMap();
    }

    DungeonLocation SelectedDungeon()
    {
        var row = dungeonGrid.SelectedRows.Count > 0 ? dungeonGrid.SelectedRows[0] : dungeonGrid.CurrentRow;
        return row == null ? null : row.Tag as DungeonLocation;
    }

    void WalkDungeon()
    {
        WalkDungeon(SelectedDungeon());
    }

    void WalkDungeon(DungeonLocation d, bool roadsOnly = false)
    {
        RequireReader();
        if (d == null)
            throw new InvalidOperationException("Dungeon seç.");
        Stop("Giriş rotası hazırlanıyor.");
        caravanWalk = roadsOnly;
        snapshot = reader.Capture();
        if (!snapshot.PlayerAlive || !snapshot.Player.Outside || new[] { 30082, 30083, 30084, 30085, 30086, 30338, 30339,
            30340, 30341, 30342 }.Contains((int)snapshot.Player.Region))
            throw new InvalidOperationException("Canlı karakter açık dünyada olmalı.");
        PrepareExpedition(d);
        BeginOperation(OperationMode.Travel);
        travel.Set(d.Entrance(), snapshot.Player, clock.ElapsedMilliseconds);
        grim.Location = d;
        Log("Girişe yürü: " + d);
    }

    void ArmDungeon()
    {
        RequireReader();
        var d = SelectedDungeon();
        if (d == null)
            throw new InvalidOperationException("Dungeon seç.");
        Stop("Dungeon hazırlandı.");
        snapshot = reader.Capture();
        knownExit = null;
        exitHolding = false;
        completionNotice.Begin(reader.Process.Id);
        grim.Arm(d, snapshot.Player);
        grimStatus.Text = grim.Message;
        Log("Dungeon giriş beklemesi: " + d);
    }
}

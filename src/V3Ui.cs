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
    readonly Panel viewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
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
    readonly TextBox activityView = new TextBox();
    readonly Panel huntPanel = new Panel();
    readonly ListBox mobList = new ListBox();
    readonly NumericUpDown reviveSeconds = new NumericUpDown(), deathLimit = new NumericUpDown();
    readonly CheckBox autoRecovery = new CheckBox();
    readonly Label deathsLabel = new Label(), grimStatus = new Label();
    readonly TabControl tabs = new FlatTabs();
    readonly DataGridView dungeonGrid = new DataGridView();
    readonly MapViewer explorerView = new MapViewer(), dungeonView = new MapViewer();
    readonly DungeonRadar dungeonRadar = new DungeonRadar();
    readonly TabControl grimViews = new TabControl();
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
        MinimumSize = new Size(980, 700);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(246, 248, 252);
        Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        var header = new Panel { Dock = DockStyle.Top, Height = 88, Padding = new Padding(8) };
        var brand = new Label { Text = AppPaths.AppName, Dock = DockStyle.Right, Width = 220, TextAlign = ContentAlignment.TopRight,
            ForeColor = Color.RoyalBlue, Font = new Font("Segoe UI", 14, FontStyle.Bold | FontStyle.Italic) };
        header.Controls.Add(brand);
        var connection = new FlowLayoutPanel { Location = new Point(8, 8), Size = new Size(1130, 36), WrapContents =
            false };
        processes.Width = 300;
        processes.DropDownStyle = ComboBoxStyle.DropDownList;
        connection.Controls.Add(processes);
        connection.Controls.Add(Btn("Yenile", RefreshProcesses, 105));
        connection.Controls.Add(Btn("Bağlan / Oku", Connect, 150));
        connection.Controls.Add(Btn("Client’ı gizle", delegate { SetClientVisibility(false); }, 130, false));
        connection.Controls.Add(Btn("Client’ı göster", delegate { SetClientVisibility(true); }, 135, false));
        connection.Controls.Add(Btn("Yenilikler", delegate { ShowWhatsNewNow(); }, 110, false));
        locked.Add(processes);
        header.Controls.Add(connection);
        position.SetBounds(10, 46, 700, 28);
        position.Text = "Bağlı değil.";
        header.Controls.Add(position);
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 55, Padding = new Padding(8) };
        var start = new Button { Text = "Başlat", Location = new Point(12, 12), Size = new Size(185, 32) };
        start.Click += delegate { Safe(StartRequested); };
        var stop = new Button { Text = "Durdur", Location = new Point(207, 12), Size = new Size(185, 32) };
        stop.Click += delegate { Stop("Durduruldu."); };
        status.Location = new Point(410, 10);
        status.Size = new Size(730, 36);
        status.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        footer.Controls.AddRange(new Control[] { start, stop, status });
        status.Text = "Otomasyon kapalı.";
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
        explorerTab.Controls.Add(explorerView);
        BuildGrim(cryptTab);
        activityView.Multiline = true;
        activityView.BorderStyle = BorderStyle.None;
        activityView.ReadOnly = true;
        activityView.ScrollBars = ScrollBars.Vertical;
        activityView.WordWrap = false;
        activityView.TabStop = false;
        var workspace = new Panel { Dock = DockStyle.Fill };
        workspace.Controls.Add(tabs);
        workspace.Controls.Add(BuildLogHost());
        headerPanel = header;
        Fancy.Buffer(header);
        header.Paint += PaintHeader;
        aurora = new AuroraBar { Dock = DockStyle.Top, Height = 5 };
        Controls.Add(workspace);
        Controls.Add(footer);
        Controls.Add(aurora);
        Controls.Add(header);
        explorerView.LoadResource("explorer-52.jpg");
        tabs.SelectedIndexChanged += delegate
        {
            stop.Text = tabs.SelectedIndex == 2 ? "Grim Crypt Durdur" : "Durdur";
            start.Text = tabs.SelectedIndex == 2 ? "Grim Crypt Başlat" : "Başlat";
            if (tabs.SelectedIndex == 1)
                explorerView.Fit();
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
        BuildLogTab();
        BuildNavigation(workspace);
        ApplyTheme();
    }

    ListBox sidebar;

    void BuildNavigation(Panel workspace)
    {
        sidebar = new ListBox { Dock = DockStyle.Left, Width = 190, IntegralHeight = false, ItemHeight = 38, DrawMode =
            DrawMode.OwnerDrawFixed, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 10.5f) };
        foreach (TabPage page in tabs.TabPages)
            sidebar.Items.Add(page);
        sidebar.DrawItem += delegate(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0)
                return;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var back = new SolidBrush(sel ? Color.FromArgb(40, accentColor) : sidebar.BackColor))
                e.Graphics.FillRectangle(back, e.Bounds);
            if (sel)
                using (var bar = new SolidBrush(accentColor))
                    e.Graphics.FillRectangle(bar, e.Bounds.X, e.Bounds.Y, 4, e.Bounds.Height);
            var page = (TabPage)sidebar.Items[e.Index];
            var bounds = e.Bounds;
            bounds.X += 16;
            bounds.Width -= 16;
            TextRenderer.DrawText(e.Graphics, page.Text, sidebar.Font, bounds, sidebar.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
        sidebar.SelectedIndex = tabs.SelectedIndex;
        sidebar.SelectedIndexChanged += delegate
        {
            var page = sidebar.SelectedItem as TabPage;
            if (page != null && tabs.SelectedTab != page)
                tabs.SelectedTab = page;
        };
        tabs.SelectedIndexChanged += delegate
        {
            if (tabs.SelectedTab != null)
                sidebar.SelectedItem = tabs.SelectedTab;
        };
        workspace.Controls.Add(sidebar);
        sidebar.SelectedIndex = Math.Max(0, tabs.SelectedIndex);
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

    static Label LabelOf(string text, int width)
    {
        return new Label { Text = text, Width = width, Height = 29, TextAlign = ContentAlignment.MiddleLeft };
    }

    FlowLayoutPanel Row(int y, int height = 40)
    {
        var row = new FlowLayoutPanel { Location = new Point(14, y), Size = new Size(1040, height), WrapContents = false };
        huntPanel.Controls.Add(row);
        return row;
    }

    Button Btn(string text, Action action, int width = 130, bool freeze = true)
    {
        var b = new Button { Text = text, Width = width, Height = 32 };
        b.Click += delegate { Safe(action); };
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
        n.ThousandsSeparator = false;
        locked.Add(n);
    }

    void ConfigureCheck(CheckBox c, string text, bool value, int width)
    {
        c.Text = text;
        c.Checked = value;
        c.Width = width;
        c.Height = 29;
        locked.Add(c);
    }

    FlowLayoutPanel huntRoot;

    FlowLayoutPanel Card(string title)
    {
        var g = new GroupBox { Text = title, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(14,
            8, 14, 12), Margin = new Padding(0, 0, 0, 14), MinimumSize = new Size(1080, 0) };
        var f = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection =
            FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Top };
        g.Controls.Add(f);
        huntRoot.Controls.Add(g);
        return f;
    }

    FlowLayoutPanel Line(FlowLayoutPanel card)
    {
        var r = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
            Margin = new Padding(0, 2, 0, 2) };
        card.Controls.Add(r);
        return r;
    }

    void BuildHunt()
    {
        huntRoot = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(14, 12, 14, 12) };
        huntPanel.Controls.Add(huntRoot);
        BuildStatCard();
        var card = Card("Kontrol");
        var row = Line(card);
        row.Controls.Add(LabelOf("Başlat / Durdur", 155));
        shortcutLabel.Width = 300;
        shortcutLabel.Height = 29;
        shortcutLabel.Text = "Atanmadı";
        row.Controls.Add(shortcutLabel);
        row.Controls.Add(Btn("Tuş ata / değiştir", ChooseHotkey, 200));
        card = Card("Hedefler");
        row = Line(card);
        row.Controls.Add(LabelOf("Yaratık", 80));
        monsters.Width = 420;
        monsters.DropDownStyle = ComboBoxStyle.DropDownList;
        monsters.AutoCompleteSource = AutoCompleteSource.ListItems;
        monsters.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        monsters.Items.AddRange(names.Values.Distinct().OrderBy(n => n).Cast<object>().ToArray());
        row.Controls.Add(monsters);
        locked.Add(monsters);
        row.Controls.Add(Btn("+ Ekle", AddMob, 105));
        row.Controls.Add(Btn("− Çıkar", RemoveMob, 105));
        row.Controls.Add(Btn("Yakındaki isimler", Nearby, 165));
        row = Line(card);
        row.Controls.Add(LabelOf("Hedef listesi", 100));
        mobList.Width = 750;
        mobList.Height = 96;
        row.Controls.Add(mobList);
        locked.Add(mobList);
        card = Card("Av bölgesi");
        row = Line(card);
        Num(xBox, -25000, 23000, 0, 1, 120);
        Num(yBox, -17000, 6500, 0, 1, 120);
        row.Controls.Add(LabelOf("Bölge X", 75));
        row.Controls.Add(xBox);
        row.Controls.Add(LabelOf("Y", 25));
        row.Controls.Add(yBox);
        row.Controls.Add(Btn("Mevcut X/Y", FillCurrent, 155));
        row.Controls.Add(Btn("Bölgeyi set et", SetCenter, 175));
        row = Line(card);
        area.Width = 1020;
        area.Height = 30;
        area.Text = "Bölge set edilmedi. Mob eklemek merkezi değiştirmez.";
        row.Controls.Add(area);
        row = Line(card);
        Num(radius, 1, 500, 150);
        Num(timeout, 5, 120, 10);
        row.Controls.Add(LabelOf("Range", 75));
        row.Controls.Add(radius);
        row.Controls.Add(LabelOf("Takılma (sn)", 125));
        row.Controls.Add(timeout);
        row.Controls.Add(LabelOf("Saldırılamayan hedef 10 sn sonra atlanır.", 420));
        card = Card("Saldırı");
        row = Line(card);
        keys.Width = 360;
        keys.Text = "1 2 3 4 5 6";
        row.Controls.Add(LabelOf("Tuşlar", 75));
        row.Controls.Add(keys);
        locked.Add(keys);
        Num(delay, 60, 10000, 100);
        row.Controls.Add(LabelOf("Tuş aralığı (ms)", 155));
        row.Controls.Add(delay);
        row = Line(card);
        ConfigureCheck(sendKeys, "Hedef doğrulanınca tuşlara bas", false, 410);
        ConfigureCheck(goHome, "Hedef yoksa / alan dışındaysa merkeze dön", true, 570);
        row.Controls.Add(sendKeys);
        row.Controls.Add(goHome);
        card = Card("Otomatik bakım");
        row = Line(card);
        ConfigureCheck(autoZerk, "Berserk 5/5 → Tab", true, 215);
        berserkClass.Width = 175;
        berserkClass.DropDownStyle = ComboBoxStyle.DropDownList;
        berserkClass.Items.AddRange(V2Rules.BerserkClasses);
        berserkClass.SelectedItem = "Giant";
        locked.Add(berserkClass);
        row.Controls.Add(autoZerk);
        row.Controls.Add(berserkClass);
        ConfigureCheck(autoSpeed, "Hızlı Koşma Taşı Kullan %100", true, 540);
        row.Controls.Add(autoSpeed);
        row = Line(card);
        ConfigureCheck(autoRepair, "Ekipman dayanıklılığı ≤ 4 → Repair", true, 410);
        row.Controls.Add(autoRepair);
        row.Controls.Add(Btn("Bakım hata kilidini sıfırla", delegate
        {
            transaction.Reset();
            maintenance.Text = transaction.Message;
        }, 290));
        row = Line(card);
        ConfigureCheck(autoRecovery, "Ölümde şehre dön + Reverse", true, 320);
        Num(reviveSeconds, 0, 3600, 30);
        Num(deathLimit, 1, 1000, 5);
        row.Controls.Add(autoRecovery);
        row.Controls.Add(LabelOf("Bekle (sn)", 100));
        row.Controls.Add(reviveSeconds);
        row.Controls.Add(LabelOf("Ölüm sınırı", 110));
        row.Controls.Add(deathLimit);
        row = Line(card);
        deathsLabel.Width = 410;
        deathsLabel.Height = 29;
        row.Controls.Add(deathsLabel);
        row.Controls.Add(Btn("Ölüm sayacını sıfırla", delegate
        {
            recovery.ResetCount();
            SaveSettings();
            UpdateDisplay();
        }, 240));
        card = Card("Yolculuk");
        row = Line(card);
        ConfigureCheck(travelMount, "Dungeon’a giderken ata bin · Oyundaki aktif mount", false, 650);
        row.Controls.Add(travelMount);
        row = Line(card);
        ConfigureCheck(returnBeforeTravel, "Farklı bölgeye giderken önce Return Scroll kullan · İsteğe bağlı", false,
            850);
        row.Controls.Add(returnBeforeTravel);
        card = Card("Canlı durum");
        row = Line(card);
        details.Width = 1020;
        details.Height = 32;
        row.Controls.Add(details);
        grid.Size = new Size(1030, 205);
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.RowHeadersVisible = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        foreach (string text in new[] { "UID", "Yaratık", "Sınıf", "X", "Y", "Merkeze mesafe", "HP", "Durum" })
            grid.Columns.Add(text, text);
        row = Line(card);
        row.Controls.Add(grid);
        maintenance.Size = new Size(1030, 55);
        maintenance.Text = transaction.Message;
        row = Line(card);
        row.Controls.Add(maintenance);
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
        if (activityView.TextLength > 16000)
            activityView.Text = activityView.Text.Substring(6000);
        activityView.AppendText(line + Environment.NewLine);
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
        autoEnterGrim.Checked = settings.AutoEnterGrim;
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
        settings.AutoEnterGrim = autoEnterGrim.Checked;
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
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 88, Padding = new Padding(8) };
        foreach (string modeName in new[] { "Novice", "Expert", "Master", "Solo Trial" })
        {
            var check = new CheckBox { Text = modeName, AutoSize = true, Checked = modeName == "Solo Trial", Margin =
                new Padding(8) };
            grimModes.Add(check);
            check.CheckedChanged += delegate { RefreshDungeonGrid(); };
            toolbar.Controls.Add(check);
        }
        toolbar.Controls.Add(Btn("Girişe yürü", WalkDungeon, 150));
        autoEnterGrim.Text = "Girişe varınca Solo Trial’a gir ve otomatik avlan";
        autoEnterGrim.AutoSize = true;
        locked.Add(autoEnterGrim);
        toolbar.Controls.Add(autoEnterGrim);
        toolbar.Controls.Add(Btn("Grim Crypt Durdur", delegate { Stop("Dungeon işlemi durduruldu."); }, 235, false));
        toolbar.Controls.Add(new Label { Text =
            "72 kayıt / 18 giriş · Mob bitince kontrol edilmemiş odalara ilerler · 72/72 canlı bitiriş testi henüz yapılmadı.",
            Width = 1040, Height = 27, ForeColor = Color.DarkGoldenrod });
        var split = new SplitContainer { Size = new Size(1040, 660), Dock = DockStyle.Fill, Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel1, SplitterDistance = 510 };
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
            dungeonGrid.Columns[column].ReadOnly = true;
            dungeonGrid.Columns[column].Width = c == "Bölge / Dungeon" ? 210 : c == "Şehir" ? 120 : c == "Kod" ? 55 : 85;
        }
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
            row.DefaultCellStyle.BackColor = visitedDungeons.Contains(id) ? Color.DarkOliveGreen : dungeonGrid.DefaultCellStyle.BackColor;
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
        split.Panel1.Controls.Add(dungeonGrid);
        BuildQueuePanel(split.Panel1);
        grimViews.Dock = DockStyle.Fill;
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
        split.Panel2.Controls.Add(grimViews);
        grimStatus.Dock = DockStyle.Bottom;
        grimStatus.Height = 62;
        grimStatus.Padding = new Padding(10);
        grimStatus.Text = "Dungeon seç. Otomatik giriş kutusu açıksa Solo Trial’a girer; kapalıysa girişte bekler.";
        page.Controls.Add(split);
        page.Controls.Add(grimStatus);
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
                dungeonGrid.Rows[r].DefaultCellStyle.BackColor = visited ? Color.DarkOliveGreen : dungeonGrid.DefaultCellStyle.BackColor;
            }
        }
        finally
        {
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

    void WalkDungeon(DungeonLocation d)
    {
        RequireReader();
        if (d == null)
            throw new InvalidOperationException("Dungeon seç.");
        Stop("Giriş rotası hazırlanıyor.");
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

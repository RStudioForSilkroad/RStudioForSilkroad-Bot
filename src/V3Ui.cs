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
    readonly Panel viewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Ui.Field };
    readonly PictureBox picture = new PictureBox { SizeMode = PictureBoxSizeMode.StretchImage };
    Image source;
    string resource;
    PointF? marker;
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
            if (Visible && marker == null)
                Fit();
        };
        // The spot being shown on the game's own map: a ring with a dot.
        picture.Paint += delegate(object o, PaintEventArgs e)
        {
            if (marker == null)
                return;
            float x = (float)(marker.Value.X * zoom), y = (float)(marker.Value.Y * zoom);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var halo = new Pen(Color.White, 5))
                e.Graphics.DrawEllipse(halo, x - 13, y - 13, 26, 26);
            using (var ring = new Pen(Color.FromArgb(220, 38, 38), 3))
                e.Graphics.DrawEllipse(ring, x - 13, y - 13, 26, 26);
            using (var dot = new SolidBrush(Color.FromArgb(220, 38, 38)))
                e.Graphics.FillEllipse(dot, x - 4, y - 4, 8, 8);
        };
    }

    // Shows a place on the game's world map (the whole Nanzhang land), centred and marked.
    internal void FocusWorld(double x, double y)
    {
        if (resource != WorldMapView.ResourceName)
            LoadResource(WorldMapView.ResourceName);
        marker = WorldMapView.ToImage(x, y);
        SetZoom(.8);
        picture.Invalidate();
        viewport.AutoScrollPosition = new Point(Math.Max(0, (int)(marker.Value.X * zoom - viewport.ClientSize.Width / 2)),
            Math.Max(0, (int)(marker.Value.Y * zoom - viewport.ClientSize.Height / 2)));
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
                resource = name;
                marker = null;
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
        if (resource != "nanzhang-map.jpg")
            LoadResource("nanzhang-map.jpg");
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
    readonly NumericUpDown reviveSeconds = new NumericUpDown();
    readonly CheckBox autoRecovery = new CheckBox();
    readonly Label recoveryLabel = new Label(), grimStatus = new Label(), gridCount = new Label();
    readonly TabControl tabs = new FlatTabs();
    readonly DataGridView dungeonGrid = new DataGridView();
    readonly MapViewer explorerView = new MapViewer();
    readonly DeathRecovery recovery = new DeathRecovery();
    readonly GrimRun grim = new GrimRun();
    readonly TravelWatch travel = new TravelWatch();
    OperationMode operation;
    readonly HashSet<string> selectedMobs = new HashSet<string>(StringComparer.Ordinal);
    readonly CappedLog eventLog = new CappedLog();
    long readErrorSince;
    V3Settings settings;

    MemorySelectV3Form()
    {
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Silkroad.ico"))
            using (var icon = new Icon(stream))
                Icon = (Icon)icon.Clone();
        Text = AppPaths.AppName + " " + Application.ProductVersion;
        ClientSize = new Size(1120, 780);
        MinimumSize = new Size(1120, 700);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Ui.Stone;
        Font = Ui.Body;
        AutoScaleMode = AutoScaleMode.Dpi;

        // The frame (page list, live log, buttons, status strip) is built after the pages, in BuildShell.
        position.Text = "Bağlı değil.";
        whatsNewButton = Btn("Yenilikler", delegate { ShowWhatsNewNow(); }, 108, false, ButtonKind.Secondary);
        status.Text = "Otomasyon kapalı.";

        tabs.Dock = DockStyle.Fill;
        tabs.Appearance = TabAppearance.FlatButtons;
        tabs.SizeMode = TabSizeMode.Fixed;
        tabs.ItemSize = new Size(1, 1);
        var huntTab = new TabPage("Av Alanı");
        var explorerTab = new TabPage("Explorer Yerleri");
        var cryptTab = new TabPage("Grim Crypt");
        tabs.TabPages.AddRange(new[] { huntTab, explorerTab, cryptTab });
        statsTab = new TabPage("İstatistik");
        protectTab = new TabPage("Koruma");
        inventoryTab = new TabPage("Envanter");
        notifyTab = new TabPage("Bildirimler");
        questTab = new TabPage("Quest");
        huntPanel.Dock = DockStyle.Fill;
        huntPanel.AutoScroll = false;
        huntTab.Controls.Add(huntPanel);
        BuildHunt();
        BuildUpdateControls();
        explorerView.Dock = DockStyle.Fill;
        BuildExplorer(explorerTab, explorerView);
        BuildGrim(cryptTab);
        var workspace = new Panel { Dock = DockStyle.Fill };
        workspace.Controls.Add(tabs);
        explorerView.LoadResource("explorer-52.jpg");
        tabs.SelectedIndexChanged += delegate
        {
            if (tabs.SelectedIndex == 1)
            {
                explorerView.Fit();
                explorerMap.Refit();
            }
            if (tabs.SelectedIndex == 2)
                EnsureRouteSummaries();
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
        BuildPhPages();
        BuildHomePage();
        // The window opens on the page that tells what the update brought.
        tabs.SelectedTab = homeTab;
        BuildShell(workspace);
        InitTray();
        ApplyTheme();
        // The window is built with layout held back and laid out once when it is first shown (see OnShown).
        Ui.Freeze(this);
        startupFrozen = true;
    }

    bool startupFrozen;

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
        // The card grows with the window (up to a comfortable width) instead of staying a narrow strip on the left.
        scroller.Resize += delegate { card.MinimumSize = new Size(Math.Max(width, Math.Min(1180, scroller.ClientSize.Width - 2 * Ui.Pad)), 0); };
        page.Controls.Add(scroller);
        return content;
    }

    // ---- Cards: group boxes of a fixed width, stacked inside the sub-pages of a page ----

    FlowLayoutPanel cardHost;
    int cardInner = 600;
    const int CardWidth = 640, WideCardWidth = 860, TileWidth = 410;
    TabPage statsTab, protectTab, inventoryTab, notifyTab;
    readonly CheckBox autoHpPotion = new CheckBox(), autoMpPotion = new CheckBox();
    readonly NumericUpDown hpPotionPercent = new NumericUpDown(), mpPotionPercent = new NumericUpDown();
    long nextHpPotion, nextMpPotion;
    bool potionLogged;

    // The sub-pages of a page: a page host without the system frame, driven by the text tab strip above it.
    TabControl SubTabs(Control host)
    {
        var t = new FlatTabs { Dock = DockStyle.Fill };
        host.Controls.Add(t);
        host.Controls.Add(new TabStrip(t));
        return t;
    }

    // One sub-page: a scrolling area whose flow takes the cards; it becomes the place the next cards go to.
    FlowLayoutPanel SubPage(TabControl t, string title)
    {
        var tp = new TabPage(title) { AutoScroll = true };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection =
            FlowDirection.TopDown, WrapContents = false, Padding = new Padding(Ui.Pad) };
        tp.Controls.Add(flow);
        t.TabPages.Add(tp);
        cardHost = flow;
        return flow;
    }

    // One page of cards laid out as boxes: they sit side by side and wrap to the next row with the window width; the page scrolls.
    FlowLayoutPanel TilePage(Control host)
    {
        var scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var flow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true, Location = new Point(0, 0), Padding = new Padding(Ui.Pad), Margin = new Padding(0), MaximumSize = new Size(880, 0) };
        scroller.Controls.Add(flow);
        scroller.Resize += delegate { flow.MaximumSize = new Size(Math.Max(300, scroller.ClientSize.Width), 0); };
        host.Controls.Add(scroller);
        cardHost = flow;
        return flow;
    }

    FlowLayoutPanel Card(string title, bool wide = false, int width = 0)
    {
        var g = new CardBox(title);
        var f = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection =
            FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Top, Margin = new Padding(0) };
        g.Controls.Add(f);
        int w = width > 0 ? width : wide ? WideCardWidth : CardWidth;
        g.MinimumSize = new Size(w, 0);
        g.Margin = new Padding(0, 0, Ui.Gap, Ui.Gap);
        cardInner = w - 2 * Ui.Pad - 2;
        cardHost.Controls.Add(g);
        return f;
    }

    FlowLayoutPanel Line(FlowLayoutPanel card)
    {
        var r = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true,
            Margin = new Padding(0, 2, 0, 2), MaximumSize = new Size(cardInner, 0) };
        card.Controls.Add(r);
        return r;
    }

    // Controls that should always be as wide as the card's inner area.
    T Stretch<T>(T control) where T : Control
    {
        control.Margin = new Padding(0, 0, 0, 6);
        control.Width = cardInner;
        return control;
    }

    // The hunt settings are spread over three pages like phBot's: Istatistik (character and surroundings), Koruma (upkeep, returns, berserk)
    // and Av Alani (targets, area, keys, shortcut).
    void BuildHunt()
    {
        var statsFlow = TilePage(statsTab);
        BuildStatCard();
        var protectFlow = TilePage(protectTab);

        // ---- Av Alani ----
        TilePage(huntPanel);
        var card = Card("Hedefler · saldırılacak yaratıklar", false, TileWidth);
        var row = Line(card);
        row.Controls.Add(Caption("Yaratık"));
        monsters.Width = 250;
        monsters.DropDownStyle = ComboBoxStyle.DropDownList;
        monsters.AutoCompleteSource = AutoCompleteSource.ListItems;
        monsters.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        monsters.Margin = new Padding(0, 2, 8, 0);
        monsters.Items.AddRange(names.Values.Distinct().OrderBy(n => n).Cast<object>().ToArray());
        row.Controls.Add(monsters);
        locked.Add(monsters);
        row.Controls.Add(Btn("+ Ekle", AddMob, 84, true, ButtonKind.Primary));
        row.Controls.Add(Btn("− Çıkar", RemoveMob, 84));
        row.Controls.Add(Btn("Yakındaki isimler", Nearby, 130));
        row = Line(card);
        mobList.Height = 150;
        mobList.BorderStyle = BorderStyle.FixedSingle;
        mobList.IntegralHeight = false;
        row.Controls.Add(Stretch(mobList));
        locked.Add(mobList);
        card = Card("Av bölgesi", false, TileWidth);
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
        card = Card("Saldırı", false, TileWidth);
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
        card = Card("Kontrol", false, TileWidth);
        row = Line(card);
        row.Controls.Add(Caption("Başlat / Durdur kısayolu"));
        shortcutLabel.AutoSize = false;
        shortcutLabel.Size = new Size(120, 26);
        shortcutLabel.TextAlign = ContentAlignment.MiddleCenter;
        shortcutLabel.BorderStyle = BorderStyle.FixedSingle;
        shortcutLabel.Text = "Atanmadı";
        shortcutLabel.Margin = new Padding(0, 2, 10, 1);
        row.Controls.Add(shortcutLabel);
        row.Controls.Add(Btn("Tuş ata / değiştir", ChooseHotkey, 140));

        // ---- Koruma ----
        cardHost = protectFlow;
        card = Card("Otomatik iksir", false, TileWidth);
        row = Line(card);
        ConfigureCheck(autoHpPotion, "HP düşünce iksir kullan", false, 0);
        Num(hpPotionPercent, 1, 99, 50, 0, 70);
        row.Controls.Add(autoHpPotion);
        row.Controls.Add(hpPotionPercent);
        row.Controls.Add(Caption("%"));
        row = Line(card);
        ConfigureCheck(autoMpPotion, "MP düşünce iksir kullan", false, 0);
        Num(mpPotionPercent, 1, 99, 30, 0, 70);
        row.Controls.Add(autoMpPotion);
        row.Controls.Add(mpPotionPercent);
        row.Controls.Add(Caption("%"));
        row = Line(card);
        var potionHint = Hint("Çantadaki ilk HP / MP iksiri (ya da tanesi) kullanılır; her tür için 1,5 sn bekleme. Oyunun makrosu da iksir basıyorsa ikisini birden açma.");
        potionHint.Height = 40;
        row.Controls.Add(Stretch(potionHint));
        card = Card("Berserk", false, TileWidth);
        row = Line(card);
        ConfigureCheck(autoZerk, "Berserk 5/5 → Tab", true, 0);
        berserkClass.Width = 170;
        berserkClass.DropDownStyle = ComboBoxStyle.DropDownList;
        berserkClass.Items.AddRange(V2Rules.BerserkClasses);
        berserkClass.SelectedItem = "Giant";
        berserkClass.Margin = new Padding(0, 2, 0, 1);
        locked.Add(berserkClass);
        row.Controls.Add(autoZerk);
        row.Controls.Add(berserkClass);
        card = Card("Otomatik bakım", false, TileWidth);
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
        card = Card("Ölüm ve dönüş", false, TileWidth);
        row = Line(card);
        ConfigureCheck(autoRecovery, "Ölümde şehre dön + Reverse", true, 0);
        row.Controls.Add(autoRecovery);
        row = Line(card);
        Num(reviveSeconds, 0, 3600, 30);
        row.Controls.Add(Caption("Bekle (sn)"));
        row.Controls.Add(reviveSeconds);
        row = Line(card);
        recoveryLabel.AutoSize = false;
        recoveryLabel.Size = new Size(230, 26);
        recoveryLabel.TextAlign = ContentAlignment.MiddleLeft;
        recoveryLabel.Margin = new Padding(0, 0, 8, 6);
        row.Controls.Add(recoveryLabel);
        card = Card("Uyarılar", false, TileWidth);
        row = Line(card);
        ConfigureCheck(uniqueAlertBox, "Unique canavar çıkınca sesle uyar", false, 0);
        row.Controls.Add(uniqueAlertBox);
        row = Line(card);
        ConfigureCheck(notifyDone, "İş bitince bildirim ver", true, 0);
        row.Controls.Add(notifyDone);
        card = Card("Yolculuk", false, TileWidth);
        row = Line(card);
        ConfigureCheck(travelMount, "Dungeon’a giderken ata bin", false, 0);
        row.Controls.Add(travelMount);
        row = Line(card);
        ConfigureCheck(backgroundWalk, "Yürüyüş işleri arka planda da çalışsın", true, 0);
        row.Controls.Add(backgroundWalk);
        row = Line(card);
        ConfigureCheck(returnBeforeTravel, "Farklı bölgeye giderken önce Return Scroll", false, 0);
        row.Controls.Add(returnBeforeTravel);
        row = Line(card);
        ConfigureCheck(useReverseTravel, "Uzaksa dungeon'a en yakın Reverse noktasına git (Reverse Scroll)", true, 0);
        row.Controls.Add(useReverseTravel);
        card = Card("Oyun penceresi", false, TileWidth);
        row = Line(card);
        displayState = Hint("", cardInner);
        displayState.Height = 58;
        row.Controls.Add(displayState);
        row = Line(card);
        row.Controls.Add(Btn("Arka planda çizimi aç", delegate { EnableBackgroundRender(); }, 190, false));
        row = Line(card);
        ConfigureCheck(autoWake, "Çizim donarsa otomatik canlandır", true, 0);
        row.Controls.Add(autoWake);
        row = Line(card);
        ConfigureCheck(autoResume, "Oyun kapanırsa yeni oyuna bağlan, işe devam et", false, 0);
        row.Controls.Add(autoResume);
        var resumeHint = Hint("Oyunu launcher'ın AutoLogin'i yeniden açar; bot aynı karakteri bulunca bağlanır. Av ve kervan yolu devam eder, şifre girilmez.", cardInner);
        resumeHint.Height = 40;
        row.Controls.Add(resumeHint);
        row = Line(card);
        row.Controls.Add(Btn("Oyunu canlandır", delegate { SetClientVisibility(true); }, 150, false));
        var wakeHint = Hint("Görüntü donarsa (karakter ilerler, ekran aynı kalır) pencereyi küçültüp açar.", cardInner);
        wakeHint.Height = 40;
        row.Controls.Add(wakeHint);

        // ---- Istatistik, second sub-page: the monsters in range ----
        cardHost = statsFlow;
        card = Card("Çevre · canlı durum", true);
        row = Line(card);
        details.AutoSize = false;
        details.Height = 26;
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
        maintenance.Height = 28;
        maintenance.TextAlign = ContentAlignment.MiddleLeft;
        maintenance.Tag = "muted";
        maintenance.Text = transaction.Message;
        row = Line(card);
        row.Controls.Add(Stretch(maintenance));
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
        lock (eventLog)
        {
            eventLog.AppendLine(line);
            try
            {
                LogFiles.Append(LogFiles.EventLogPath, line, LogFiles.MaxFileBytes);
            }
            catch { }
        }
        try
        {
            string shown = DateTime.Now.ToString("[HH:mm:ss] ") + text.Split('\n')[0].TrimEnd('\r');
            if (InvokeRequired)
                BeginInvoke(new Action<string>(AppendLiveLog), shown);
            else
                AppendLiveLog(shown);
        }
        catch (InvalidOperationException) { }
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
        useReverseTravel.Checked = settings.UseReverseTravel;
        travelMount.Checked = settings.TravelMount;
        backgroundWalk.Checked = settings.BackgroundWalk;
        notifyDone.Checked = settings.NotifyDone;
        autoWake.Checked = settings.AutoWake;
        autoResume.Checked = settings.AutoResume;
        uniqueAlertBox.Checked = settings.UniqueAlert;
        autoEnterGrim.Checked = settings.AutoEnterGrim;
        exitAfterGrim.Checked = settings.GrimAutoExit;
        if (settings.ExplorerDone == null)
            settings.ExplorerDone = new List<int>();
        explorerSyncing = true;
        try
        {
            explorerSkipDone.Checked = settings.ExplorerSkipDone;
            SetValue(explorerModel, settings.ExplorerModel);
            explorerTalk.Checked = settings.ExplorerTalk;
            explorerReturn.Checked = settings.ExplorerReturnBetween;
            collectNpcs.Checked = settings.NanzhangCollect;
            autoHpPotion.Checked = settings.AutoHpPotion;
            autoMpPotion.Checked = settings.AutoMpPotion;
            SetValue(hpPotionPercent, settings.HpPotionPercent < 1 ? 50 : settings.HpPotionPercent);
            SetValue(mpPotionPercent, settings.MpPotionPercent < 1 ? 30 : settings.MpPotionPercent);
            if (settings.ExplorerTalkOptionRevision < 1)
            {
                settings.ExplorerTalkOption = 1;
                settings.ExplorerTalkOptionRevision = 1;
            }
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
        keys.Text = settings.Keys ?? "1";
        berserkClass.SelectedItem = V2Rules.BerserkClasses.Contains(settings.BerserkClass) ? settings.BerserkClass :
            "Giant";
        sendKeys.Checked = true;
        goHome.Checked = settings.GoHome;
        autoZerk.Checked = settings.Berserk;
        autoSpeed.Checked = settings.Speed;
        autoRepair.Checked = settings.Repair;
        autoRecovery.Checked = settings.Recovery;
        recoveryLabel.Text = "Kurtarma: " + recovery.Phase;
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
        settings.DungeonQueue = queue.Select(item => item.QueueKey).ToList();
        settings.QueueReturnScroll = queueReturnScroll.Checked;
        settings.ReturnBeforeTravel = returnBeforeTravel.Checked;
        settings.UseReverseTravel = useReverseTravel.Checked;
        settings.TravelMount = travelMount.Checked;
        settings.BackgroundWalk = backgroundWalk.Checked;
        settings.NotifyDone = notifyDone.Checked;
        settings.AutoWake = autoWake.Checked;
        settings.AutoResume = autoResume.Checked;
        if (lotteryReady)
            SaveLotterySettings();
        settings.UniqueAlert = uniqueAlertBox.Checked;
        SaveSkillSettings(settings);
        settings.AutoEnterGrim = autoEnterGrim.Checked;
        settings.GrimAutoExit = exitAfterGrim.Checked;
        settings.ExplorerSkipDone = explorerSkipDone.Checked;
        settings.ExplorerModel = (int)explorerModel.Value;
        settings.NanzhangCollect = collectNpcs.Checked;
        settings.AutoHpPotion = autoHpPotion.Checked;
        settings.AutoMpPotion = autoMpPotion.Checked;
        settings.HpPotionPercent = (int)hpPotionPercent.Value;
        settings.MpPotionPercent = (int)mpPotionPercent.Value;
        settings.ExplorerTalk = explorerTalk.Checked;
        settings.ExplorerReturnBetween = explorerReturn.Checked;
        settings.ExplorerTalkOption = (int)explorerOption.Value;
        settings.VisitedDungeons = visitedDungeons.OrderBy(id => id).ToList();
        settings.Mobs = selectedMobs.OrderBy(x => x).ToList();
        settings.X = (double)xBox.Value;
        settings.Y = (double)yBox.Value;
        settings.Range = (double)radius.Value;
        settings.Timeout = (int)timeout.Value;
        settings.Delay = (int)delay.Value;
        settings.ReviveSeconds = (int)reviveSeconds.Value;
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
        if (savedLabel != null)
            savedLabel.Text = "Kaydedildi " + DateTime.Now.ToString("HH:mm");
    }

    void BuildGrim(TabPage outerPage)
    {
        var page = outerPage;
        page.AutoScroll = true;
        page.AutoScrollMinSize = new Size(860, 640);
        // Row 1: difficulty chips on the left, the walk button on the right.
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(Ui.Pad, 12, Ui.Pad, 2) };
        foreach (string modeName in new[] { "Novice", "Expert", "Master", "Solo Trial", DungeonLocation.Solo85 })
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
        autoEnterGrim.Text = "Girişe varınca Solo’ya gir ve otomatik avlan";
        autoEnterGrim.AutoSize = true;
        autoEnterGrim.Margin = new Padding(0, 6, 24, 0);
        locked.Add(autoEnterGrim);
        options.Controls.Add(autoEnterGrim);
        exitAfterGrim.Text = "Bitince Dungeon Exit ile çık";
        exitAfterGrim.AutoSize = true;
        exitAfterGrim.Margin = new Padding(0, 6, 24, 0);
        exitAfterGrim.CheckedChanged += delegate { SaveSettingsQuietly(); };
        locked.Add(exitAfterGrim);
        options.Controls.Add(exitAfterGrim);
        var grimNote = new Label { Text = "Beta · canlı bitiriş testi henüz yapılmadı · Solo 85: seviye 85 gerekir", AutoSize = true, Tag = "warn",
            Margin = new Padding(0, 7, 0, 0) };
        options.Controls.Add(grimNote);
        // Two parts, one above the other: the dungeon list with the run order on top, the picture and the card of the chosen dungeon below.
        var split = new SplitContainer { Size = new Size(1200, 700), Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
            FixedPanel = FixedPanel.Panel1, SplitterDistance = 270, SplitterWidth = 6 };
        split.Panel1MinSize = 200;
        split.Panel2MinSize = 224;
        split.Panel1.Padding = new Padding(Ui.Pad, 0, Ui.Pad, 4);
        split.Panel2.Padding = new Padding(Ui.Pad, 4, Ui.Pad, Ui.Pad);
        dungeonGrid.Dock = DockStyle.Fill;
        dungeonGrid.ReadOnly = false;
        dungeonGrid.AllowUserToAddRows = false;
        dungeonGrid.RowHeadersVisible = false;
        dungeonGrid.MultiSelect = true;
        dungeonGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        dungeonGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        var visitedColumn = new DataGridViewCheckBoxColumn { Name = "Visited", HeaderText = "Girdim", FillWeight = 48 };
        dungeonGrid.Columns.Add(visitedColumn);
        foreach (string c in new[] { "Kod", "Bölge / Dungeon", "Şehir", "Mod", "Giriş X", "Giriş Y", "Instance",
            "İç rota", "Rota" })
        {
            int column = dungeonGrid.Columns.Add(c, c);
            var col = dungeonGrid.Columns[column];
            col.ReadOnly = true;
            col.Width = c == "Şehir" ? 150 : c == "Kod" ? 52 : c == "Rota" ? 230 : 90;
            // Coordinates, instance id and route note stay in the data (queue and walker read them) but not on screen.
            if (c == "Giriş X" || c == "Giriş Y" || c == "Instance" || c == "İç rota")
                col.Visible = false;
        }
        dungeonGrid.Columns["Rota"].DisplayIndex = 5;
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
            StyleDungeonRow(row, visitedDungeons.Contains(id));
            SaveSettings();
            RefreshDungeonDetails();
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
        BuildGrimBottom(split.Panel2);
        grimStatus.Dock = DockStyle.Bottom;
        grimStatus.Height = 36;
        grimStatus.Font = Ui.Small;
        grimStatus.TextAlign = ContentAlignment.MiddleLeft;
        grimStatus.Padding = new Padding(Ui.Pad, 0, Ui.Pad, 0);
        grimStatus.Tag = "muted";
        grimStatus.Text = "Dungeon seç. Otomatik giriş açıksa Solo Trial’a girer; kapalıysa girişte bekler.";
        page.Controls.Add(split);
        page.Controls.Add(grimStatus);
        BuildLearnTools(page);
        page.Controls.Add(options);
        page.Controls.Add(toolbar);
        RefreshDungeonGrid();
        dungeonShot.LoadResource("Grim.D01.jpg");
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
                string text;
                int r = dungeonGrid.Rows.Add(visited, d.Code, d.Name, d.Area, d.Mode, d.X.ToString("F1"), d.Y.ToString("F1"),
                    d.InstanceId, d.IsSolo ? "Kayıtlı harita · alan kontrolü" :
                    "Canlı tarama · alan kontrolü", routeSummaries == null ? "…" : routeSummaries.TryGetValue(d.Code, out text) ? text : "—");
                dungeonGrid.Rows[r].Tag = d;
                StyleDungeonRow(dungeonGrid.Rows[r], visited);
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

    // Not entered yet: white. Entered: yellow (also while the row is selected).
    static void StyleDungeonRow(DataGridViewRow row, bool visited)
    {
        row.DefaultCellStyle.ForeColor = visited ? Ui.Done : Ui.Fresh;
        row.DefaultCellStyle.SelectionForeColor = visited ? Ui.Done : Ui.Fresh;
    }

    void ShowDungeonImage(DungeonLocation d)
    {
        dungeonShot.LoadResource("Grim." + d.Code + ".jpg");
        RefreshDungeonDetails();
    }

    DungeonLocation SelectedDungeon()
    {
        // the one chosen row (the selection event arrives before the current row follows); with several chosen, the row with the focus
        var chosen = dungeonGrid.SelectedRows;
        var row = chosen.Count == 1 ? chosen[0] : dungeonGrid.CurrentRow ?? (chosen.Count > 0 ? chosen[0] : null);
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

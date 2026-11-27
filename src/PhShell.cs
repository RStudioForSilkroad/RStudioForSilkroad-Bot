using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

// The window shell in the manner of phBot: a list of pages with small icons on the left, the page in the middle, and under it the live log
// with a grid of six buttons on the right. The game picker sits above the page list; the connection and status text in a strip at the bottom.
sealed partial class MemorySelectV3Form
{
    Panel leftPanel, bottomPanel, statusStrip, pageFrame;
    Label savedLabel;
    RouteHeader routeHeader;
    readonly TextBox liveLog = new TextBox();
    Button startButton, stopButton;
    ListBox sidebar;
    int sidebarHover = -1;
    const int LiveLogLimit = 60000, NavRow = 26, NavHead = 22;

    // Page order of the list (the page's tab text) and the icon of each (Segoe MDL2 Assets).
    static readonly string[] pageOrder =
    {
        "Yenilikler", "İstatistik", "Koruma", "Av Alanı", "Saldırı", "Grim Crypt", "Explorer Yerleri", "Kervan Rotası", "Envanter", "Bildirimler",
        "Nanzhang NPC", "Quest", "Lottery", "Çift Hesap · Deneme", "Güncelleme/Kılavuz", "Log"
    };

    // The groups of the page list. A page that is in none of them is listed last without a heading.
    static readonly string[][] navGroups =
    {
        new[] { "Başlangıç", "Yenilikler" },
        new[] { "Karakter", "İstatistik", "Koruma", "Envanter", "Bildirimler" },
        new[] { "Av", "Av Alanı", "Saldırı", "Grim Crypt" },
        new[] { "Yolculuk", "Explorer Yerleri", "Kervan Rotası", "Nanzhang NPC", "Quest" },
        new[] { "Araçlar", "Lottery", "Çift Hesap · Deneme", "Güncelleme/Kılavuz", "Log" }
    };

    static string GroupOf(string page, out bool first)
    {
        first = false;
        foreach (var group in navGroups)
        {
            int at = Array.IndexOf(group, page, 1);
            if (at > 0)
            {
                first = at == 1;
                return group[0];
            }
        }
        return null;
    }

    static readonly Dictionary<string, string> navIcons = new Dictionary<string, string>
    {
        { "İstatistik", "" },
        { "Koruma", "" },
        { "Av Alanı", "" },
        { "Saldırı", "" },
        { "Grim Crypt", "" },
        { "Explorer Yerleri", "" },
        { "Kervan Rotası", "" },
        { "Envanter", "" },
        { "Bildirimler", "" },
        { "Nanzhang NPC", "" },
        { "Quest", "" },
        { "Lottery", "\uE7BF" },
        { "Çift Hesap · Deneme", "" },
        { "Güncelleme/Kılavuz", "" },
        { "Log", "" }
    };

    // Builds the frame around the pages. workspace is the panel that already holds the (page-less header) tab control.
    void BuildShell(Panel workspace)
    {
        // Bottom: the live log and the six buttons.
        bottomPanel = new StonePanel { Dock = DockStyle.Bottom, Height = 150, Padding = new Padding(0, 6, 8, 4) };
        liveLog.Multiline = true;
        liveLog.ReadOnly = true;
        liveLog.ScrollBars = ScrollBars.Vertical;
        liveLog.BorderStyle = BorderStyle.FixedSingle;
        liveLog.Font = Ui.Mono;
        liveLog.WordWrap = true;
        liveLog.Dock = DockStyle.Fill;
        liveLog.TabStop = false;
        var buttons = new TableLayoutPanel { Dock = DockStyle.Right, Width = 316, ColumnCount = 2, RowCount = 3, Padding = new Padding(8, 0, 0, 0) };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (int i = 0; i < 3; i++)
            buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
        Func<string, Action, bool, ButtonKind, Button> make = (text, action, freeze, kind) =>
        {
            var b = Btn(text, action, 120, freeze, kind);
            b.Dock = DockStyle.Fill;
            b.Margin = new Padding(0, 0, 6, 6);
            return b;
        };
        startButton = new Button { Text = "Başlat", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 6) };
        startButton.Click += delegate { Safe(StartRequested); };
        Fancy.Kind(startButton, ButtonKind.Secondary);
        stopButton = new Button { Text = "Durdur", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 6) };
        stopButton.Click += delegate { Stop("Durduruldu."); };
        Fancy.Kind(stopButton, ButtonKind.Secondary);
        buttons.Controls.Add(make("Bağlan / Oku", Connect, true, ButtonKind.Primary), 0, 0);
        buttons.Controls.Add(startButton, 1, 0);
        buttons.Controls.Add(make("Yenile", RefreshProcesses, true, ButtonKind.Secondary), 0, 1);
        buttons.Controls.Add(stopButton, 1, 1);
        buttons.Controls.Add(make("Client’ı gizle", delegate { SetClientVisibility(false); }, false, ButtonKind.Secondary), 0, 2);
        var showButton = make("Client’ı göster", delegate { SetClientVisibility(true); }, false, ButtonKind.Secondary);
        showButton.Margin = new Padding(0, 0, 0, 6);
        buttons.Controls.Add(showButton, 1, 2);
        bottomPanel.Controls.Add(liveLog);
        bottomPanel.Controls.Add(buttons);
        liveLog.Text = "Silkroad R Studio " + Application.ProductVersion + " · hazır." + Environment.NewLine;

        // Status strip under it: the connection line and the current status.
        statusStrip = new StonePanel { Dock = DockStyle.Bottom, Height = 28 };
        position.AutoSize = false;
        position.Dock = DockStyle.Left;
        position.Width = 340;
        position.TextAlign = ContentAlignment.MiddleLeft;
        position.AutoEllipsis = true;
        position.Padding = new Padding(8, 0, 0, 0);
        status.AutoSize = false;
        status.Dock = DockStyle.Fill;
        status.TextAlign = ContentAlignment.MiddleLeft;
        status.AutoEllipsis = true;
        status.Location = new Point(0, 0);
        status.Size = new Size(100, 28);
        // right end, as in the MSRO footer: the page arrows and the small "saved" note
        savedLabel = new Label { Dock = DockStyle.Right, Width = 130, TextAlign = ContentAlignment.MiddleRight, Font = Ui.Small, Tag = "muted", Text = "Kaydedildi",
            Padding = new Padding(0, 0, 8, 0) };
        Func<string, int, Button> arrow = (glyph, step) =>
        {
            var b = new Button { Text = glyph, Dock = DockStyle.Right, Width = 30, Margin = new Padding(0) };
            Fancy.Kind(b, ButtonKind.Secondary);
            b.Click += delegate { StepPage(step); };
            return b;
        };
        var down = arrow("▼", 1);
        var up = arrow("▲", -1);
        statusStrip.Controls.Add(status);
        statusStrip.Controls.Add(position);
        statusStrip.Controls.Add(savedLabel);
        statusStrip.Controls.Add(up);
        statusStrip.Controls.Add(down);
        statusStrip.Paint += delegate(object o, PaintEventArgs e)
        {
            using (var line = new Pen(Ui.Bronze))
                e.Graphics.DrawLine(line, 0, 0, statusStrip.Width, 0);
        };

        // Left: the game picker above the page list.
        leftPanel = new StonePanel { Dock = DockStyle.Left, Width = 216, Padding = new Padding(8, 8, 0, 8) };
        var picker = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Color.Transparent };
        var pickerLabel = new Label { Text = "Oyun", Dock = DockStyle.Top, Height = 18, Tag = "muted", TextAlign = ContentAlignment.MiddleLeft };
        processes.Dock = DockStyle.Top;
        processes.DropDownStyle = ComboBoxStyle.DropDownList;
        processes.Margin = new Padding(0);
        picker.Controls.Add(processes);
        picker.Controls.Add(pickerLabel);
        sidebarHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 0), BackColor = Ui.Back };
        BuildNavigation();
        leftPanel.Controls.Add(sidebarHost);
        leftPanel.Controls.Add(picker);
        leftPanel.Controls.Add(new ArtPlate { Dock = DockStyle.Bottom });

        pageFrame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 8, 8, 0) };
        pageFrame.Controls.Add(workspace);
        workspace.Dock = DockStyle.Fill;
        routeHeader = new RouteHeader();
        pageFrame.Controls.Add(routeHeader);
        if (tabs.SelectedTab != null)
            routeHeader.SetTitle(tabs.SelectedTab.Text);
        // Dock order: what is added last docks first. The left panel reaches the full height, the strip lies at the very bottom.
        Controls.Add(pageFrame);
        Controls.Add(bottomPanel);
        Controls.Add(statusStrip);
        Controls.Add(leftPanel);
    }

    // phBot's page list: a white list with an icon and a name per page, the current one in the selection colour.
    void BuildNavigation()
    {
        sidebar = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawVariable, ItemHeight = NavRow,
            BorderStyle = BorderStyle.None, Font = Ui.Body, BackColor = Ui.Back };
        var pages = tabs.TabPages.Cast<TabPage>().ToList();
        foreach (string name in pageOrder)
        {
            var page = pages.FirstOrDefault(p => p.Text == name);
            if (page != null)
            {
                sidebar.Items.Add(page);
                pages.Remove(page);
            }
        }
        foreach (var page in pages)
            sidebar.Items.Add(page);
        // A page row is NavRow high; the first page of a group carries the group's name above it (NavHead more).
        sidebar.MeasureItem += delegate(object sender, MeasureItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= sidebar.Items.Count)
                return;
            bool first;
            GroupOf(((TabPage)sidebar.Items[e.Index]).Text, out first);
            e.ItemHeight = NavRow + (first ? (e.Index == 0 ? NavHead : NavHead + 6) : 0);
        };
        sidebar.DrawItem += delegate(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0)
                return;
            var g = e.Graphics;
            var page = (TabPage)sidebar.Items[e.Index];
            bool selected = (e.State & DrawItemState.Selected) != 0, hover = e.Index == sidebarHover;
            bool first;
            string group = GroupOf(page.Text, out first);
            Ui.PaintStone(g, e.Bounds, Ui.Back, false);
            var row = new Rectangle(e.Bounds.X, e.Bounds.Bottom - NavRow, e.Bounds.Width, NavRow);
            if (first && group != null)
            {
                // the group's name in small capitals, with a hairline running on to the right
                string caption = group.ToUpper(System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));
                int top = e.Bounds.Y + (e.Index == 0 ? 0 : 6);
                var label = new Rectangle(e.Bounds.X + 4, top, e.Bounds.Width - 12, NavHead - 2);
                int w = TextRenderer.MeasureText(g, caption, Ui.GroupLabel, label.Size, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
                TextRenderer.DrawText(g, caption, Ui.GroupLabel, label, Ui.InkMuted,
                    TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                using (var pen = new Pen(Ui.Divider))
                    g.DrawLine(pen, label.X + w + 8, label.Bottom - 5, e.Bounds.Right - 8, label.Bottom - 5);
            }
            // the entry: a bordered dark button, bronze-gold when chosen
            var button = new Rectangle(row.X + 2, row.Y + 1, row.Width - 6, NavRow - 4);
            Color fill = selected ? Ui.NavOn : hover ? Ui.SurfaceAlt : Ui.Stone;
            Color edge = selected ? Ui.NavOnEdge : hover ? Ui.BorderStrong : Ui.Mix(Ui.Divider, Ui.Bronze, 0.5f);
            using (var brush = new SolidBrush(fill))
                g.FillRectangle(brush, button);
            using (var pen = new Pen(edge))
                g.DrawRectangle(pen, button.X, button.Y, button.Width - 1, button.Height - 1);
            Color ink = selected ? Ui.Gold : hover ? Ui.Ink : Ui.TextSecondary;
            TextRenderer.DrawText(g, (e.Index + 1).ToString("00"), Ui.GroupLabel, new Rectangle(button.X + 6, button.Y, 18, button.Height),
                selected ? Ui.Mix(Ui.Gold, Ui.NavOn, 0.35f) : Ui.InkMuted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            int textLeft = button.X + 28;
            if (SroIcons.Has(page.Text))
            {
                SroIcons.Draw(g, page.Text, new Rectangle(button.X + 27, button.Y + (button.Height - 14) / 2, 14, 14), ink);
                textLeft = button.X + 47;
            }
            TextRenderer.DrawText(g, page.Text, selected ? Ui.BodyBold : Ui.Body, new Rectangle(textLeft, button.Y, button.Right - textLeft - 4, button.Height), ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
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
            if (routeHeader != null && tabs.SelectedTab != null)
                routeHeader.SetTitle(tabs.SelectedTab.Text);
            bool grim = tabs.SelectedTab != null && tabs.SelectedTab.Text == "Grim Crypt";
            if (startButton != null)
            {
                startButton.Text = grim ? "Grim Crypt Başlat" : "Başlat";
                stopButton.Text = grim ? "Grim Crypt Durdur" : "Durdur";
            }
        };
        sidebarHost.Controls.Add(sidebar);
        Ui.DarkNative(sidebar);
        if (tabs.SelectedTab != null)
        {
            sidebar.SelectedItem = tabs.SelectedTab;
        }
    }

    // The footer arrows: the previous or next page of the list.
    void StepPage(int step)
    {
        int at = sidebar.SelectedIndex + step;
        if (at >= 0 && at < sidebar.Items.Count)
            sidebar.SelectedIndex = at;
    }

    // Adds a line to the live log under the pages (the last LiveLogLimit characters are kept).
    void AppendLiveLog(string line)
    {
        if (liveLog.IsDisposed)
            return;
        if (liveLog.TextLength > LiveLogLimit)
        {
            // keep the second half; a cut-off first line is dropped
            string text = liveLog.Text;
            text = text.Substring(text.Length - LiveLogLimit / 2);
            int cut = text.IndexOf('\n');
            liveLog.Text = cut >= 0 ? text.Substring(cut + 1) : text;
        }
        liveLog.AppendText(line + Environment.NewLine);
        liveLog.SelectionStart = liveLog.TextLength;
        liveLog.ScrollToCaret();
    }
}

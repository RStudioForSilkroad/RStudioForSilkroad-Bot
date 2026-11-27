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

// Application shell: colour application, header, log card and the Nanzhang NPC page. Tokens live in Ui.cs.
sealed partial class MemorySelectV3Form
{
    readonly ComboBox themePicker = new ComboBox();
    readonly Label themeLabel = new Label { Text = "Vurgu", AutoSize = true };
    Button whatsNewButton;
    Panel headerPanel, footerPanel, sidebarHost;

    void BuildModernTabs()
    {
        var page = new TabPage("Nanzhang NPC");
        tabs.TabPages.Add(page);
        var split = new SplitContainer { Size = new Size(1200, 650), Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1,
            SplitterDistance = 340, SplitterWidth = 1 };
        page.Controls.Add(split);
        split.Panel1.Padding = new Padding(Ui.Pad);
        var searchLabel = new Label { Text = "İsim veya kimlik ara", Dock = DockStyle.Top, Height = 24, Tag = "muted" };
        var search = new TextBox { Dock = DockStyle.Top, BorderStyle = BorderStyle.FixedSingle };
        var list = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false };
        var viewer = new MapViewer { Dock = DockStyle.Fill };
        var hint = new Label { Dock = DockStyle.Bottom, Height = 96, Padding = new Padding(0, 10, 0, 0), Tag = "muted", Text =
            "Fare tekerleği: yakınlaştır · Sol tuş: sürükle\nŞehir krokisi yaklaşık yapı izdüşümüdür." };
        split.Panel1.Controls.Add(list);
        split.Panel1.Controls.Add(hint);
        split.Panel1.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 8 });
        split.Panel1.Controls.Add(search);
        split.Panel1.Controls.Add(searchLabel);
        split.Panel2.Padding = new Padding(Ui.Pad);
        var nanzhangFrame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), Tag = "border" };
        nanzhangFrame.Controls.Add(viewer);
        split.Panel2.Controls.Add(nanzhangFrame);
        viewer.LoadResource("nanzhang-map.jpg");
        List<NpcMapRecord> records;
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("nanzhang-data.json"))
            using (var r = new StreamReader(stream))
            {
                records = new JavaScriptSerializer { MaxJsonLength = 16000000 }.Deserialize<NpcMapData>(r.ReadToEnd()).NPCs;
            }
        nanzhangBuiltIn = MergeTableNpcs(records);
        nanzhangList = list;
        nanzhangSearch = search;
        nanzhangHint = hint;
        nzStore = NanzhangData.Load(NanzhangData.StorePath);
        ConfigureCheck(collectNpcs, "Gezerken NPC’leri kaydet (Nanzhang haritası)", true, 0);
        collectNpcs.Dock = DockStyle.Top;
        collectNpcs.Margin = new Padding(0);
        collectNpcs.CheckedChanged += delegate
        {
            if (settings != null)
                SaveSettingsQuietly();
        };
        split.Panel1.Controls.Add(collectNpcs);
        search.TextChanged += delegate { RefreshNanzhangList(); };
        RefreshNanzhangList();
        list.SelectedIndexChanged += delegate
        {
            var n = list.SelectedItem as NpcMapRecord;
            if (n != null)
            {
                int quests = NanzhangData.World.quests.Count(q => q.npc == n.name);
                int rewarded = NanzhangData.World.quests.Count(q => q.npc == n.name && q.HasReward);
                hint.Text = n.name + " · Kimlik " + n.id + "\nX " + n.x.ToString("F1") + " / Y " + n.y.ToString("F1") +
                    " · Harita no " + n.number + (n.collected ? " · toplandı " + n.seen : n.fromTable ? " · oyun tablosu" : "") +
                    "\n" + (quests == 0 ? "Görevi yok." : quests + " görev (" + rewarded + " ödüllü) · Quest sayfasında.");
                // The city is shown on its sketch; an NPC outside it is marked on the game's own world map.
                if (n.x == 0 && n.y == 0)
                    hint.Text += "\nKonumu henüz bilinmiyor; haritada gezerken bot kaydeder.";
                else if (n.x >= 5400 && n.x <= 6300 && n.y >= -2000 && n.y <= -1500)
                    viewer.FocusNpc(n.x, n.y);
                else
                {
                    viewer.FocusWorld(n.x, n.y);
                    hint.Text += "\nOyunun harita görselinde işaretli.";
                }
            }
        };
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

    // Colours come from Ui; the accent colour is the only thing the user can change.
    void ApplyTheme()
    {
        Ui.SetAccent(Math.Max(0, themePicker.SelectedIndex));
        Action<Control, Color> paint = null;
        paint = (c, bg) =>
        {
            if (c is MapViewer || c is ExplorerMapView || c is DungeonShot || c is DungeonDetails || c is StatPanel || c is TabStrip || c is ArtPlate ||
                c is SkillListView || c is RouteHeader)
            {
                c.Invalidate();
                return;
            }
            if (c == updateBanner)
            {
                c.BackColor = Ui.Danger;
                c.ForeColor = Ui.OnStatus;
                return;
            }
            if (c == sidebar)
            {
                sidebar.BackColor = Ui.Back;
                sidebar.Invalidate();
                return;
            }
            // the outer frame (page list, log, status strip) is the darker stone; the pages are the lighter one
            if (c == sidebarHost || c == leftPanel || c == bottomPanel || c == statusStrip)
                bg = Ui.Back;
            if (c is CardBox)
                bg = Ui.Card;
            var page = c as TabPage;
            if (page != null)
                page.UseVisualStyleBackColor = false;
            if (c is TextBox || c is ComboBox || c is NumericUpDown || c is ListBox)
            {
                // inputs are sunk into their panel; Windows draws the scroll bars and spin buttons in its dark variant
                c.BackColor = Ui.Field;
                c.ForeColor = Ui.Ink;
                Ui.DarkNative(c);
            }
            else if (c is SplitContainer)
                c.BackColor = Ui.Divider;
            else if ((c is Label || (c is Panel && !(c is StonePanel))) && bg == Ui.Back)
            {
                // on the textured frame labels and plain panels let the stone show through
                c.BackColor = Color.Transparent;
                c.ForeColor = "muted".Equals(c.Tag) ? Ui.InkMuted : Ui.Ink;
            }
            else if (c is Button)
                Fancy.Style((Button)c);
            else if ("border".Equals(c.Tag))
            {
                c.BackColor = Ui.Bronze;
                bg = Ui.Card;
            }
            else
            {
                c.BackColor = bg;
                c.ForeColor = c is Label && "muted".Equals(c.Tag) ? Ui.InkMuted : c is Label && "warn".Equals(c.Tag) ? Ui.Warning :
                    c is Label && "gold".Equals(c.Tag) ? Ui.Gold : Ui.Ink;
            }
            var g = c as DataGridView;
            if (g != null)
            {
                Ui.StyleGrid(g);
                Ui.DarkNative(g);
            }
            if (c is CheckBox)
                Fancy.StyleCheck((CheckBox)c);
            if (c is CheckBox || c is RadioButton)
                Ui.DarkNative(c);
            else if (c is ScrollableControl && ((ScrollableControl)c).AutoScroll)
                Ui.DarkNative(c);
            foreach (Control child in c.Controls)
                paint(child, bg);
        };
        BackColor = Ui.Back;
        Ui.DarkCaption(this);
        paint(this, Ui.Stone);
        statPanel.Invalidate();
        dungeonDetails.Invalidate();
        Invalidate(true);
    }

    // Window title starts with the character name, so two running copies can be told apart in the taskbar.
    void UpdateTitle()
    {
        string name = snapshot == null ? null : snapshot.CharacterName;
        string state = reader == null ? "Bağlı değil" : "Bağlı";
        string title = AppPaths.AppName + " v" + Application.ProductVersion + (string.IsNullOrEmpty(name) ? "" : " - " + name) + " - " + state;
        if (routeHeader != null && tabs.SelectedTab != null)
            routeHeader.SetTitle(tabs.SelectedTab.Text);
        if (routeHeader != null)
            routeHeader.SetState((string.IsNullOrEmpty(name) ? "" : name + (snapshot.StatsKnown ? " · Seviye " + snapshot.Level : "") + " · ") + state, reader != null);
        if (Text != title)
            Text = title;
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

    internal sealed class NpcMapData
    {
        public List<NpcMapRecord> NPCs;
    }

    internal sealed class NpcMapRecord
    {
        public string name, seen;
        public int id, number;
        public double x, y;
        public bool collected, fromTable;

        public override string ToString()
        {
            return (collected ? "★ " : "") + number.ToString("000") + " · " + name + (x == 0 && y == 0 ? " (konum yok)" : "");
        }
    }
}

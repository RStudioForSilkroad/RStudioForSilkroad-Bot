using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

// Names of items from the game folder's data\iteminfo.txt (id TAB icon TAB name), for the inventory page.
static class ItemCatalog
{
    static Dictionary<uint, string> names = new Dictionary<uint, string>();

    internal static bool Loaded
    {
        get { return names.Count > 0; }
    }

    internal static void Parse(IEnumerable<string> lines)
    {
        var map = new Dictionary<uint, string>();
        foreach (string line in lines)
        {
            var c = line.TrimEnd('\r', '\n').Split('\t');
            uint id;
            if (c.Length >= 3 && uint.TryParse(c[0], out id) && c[2].Trim().Length > 0)
                map[id] = c[2].Trim();
        }
        names = map;
    }

    internal static bool LoadFrom(string gameFolder)
    {
        try
        {
            string path = Path.Combine(gameFolder, "data", "iteminfo.txt");
            if (!File.Exists(path))
                return false;
            Parse(File.ReadAllLines(path, Encoding.UTF8));
            return Loaded;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal static string NameOf(uint id)
    {
        string name;
        return names.TryGetValue(id, out name) ? name : "Eşya #" + id;
    }
}

// The pages phBot has and this bot can fill from the game's memory: Envanter (the bag) and Bildirimler (the events).
sealed partial class MemorySelectV3Form
{
    readonly DataGridView bagGrid = new DataGridView();
    readonly Label bagInfo = new Label();
    readonly DataGridView eventGrid = new DataGridView();
    long nextBagRead;
    int lastEventCount = -1;

    // Adds the four pages built earlier and fills the two new ones. They come after the pages that already existed, so those keep their indexes.
    void BuildPhPages()
    {
        BuildInventoryPage();
        BuildNotificationsPage();
        BuildQuestPage();
        lotteryTab = new TabPage("Lottery");
        BuildLotteryPage();
        tabs.TabPages.AddRange(new[] { statsTab, protectTab, inventoryTab, notifyTab, questTab, lotteryTab });
    }

    static void PlainGrid(DataGridView g)
    {
        g.Dock = DockStyle.Fill;
        g.ReadOnly = true;
        g.AllowUserToAddRows = false;
        g.AllowUserToDeleteRows = false;
        g.AllowUserToResizeRows = false;
        g.RowHeadersVisible = false;
        g.MultiSelect = false;
        g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
    }

    void BuildInventoryPage()
    {
        var tabsHere = SubTabs(inventoryTab);
        var page = new TabPage("Çanta");
        tabsHere.TabPages.Add(page);
        PlainGrid(bagGrid);
        bagGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Slot", HeaderText = "Yuva", Width = 60 });
        bagGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Item", HeaderText = "Eşya", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 200 });
        bagGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Quantity", HeaderText = "Adet", Width = 90 });
        bagGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "Eşya ID", Width = 90 });
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(Ui.Pad, 4, Ui.Pad, 0) };
        tools.Controls.Add(Btn("Yenile", delegate { RefreshInventory(true); }, 100, false, ButtonKind.Secondary));
        bagInfo.AutoSize = false;
        bagInfo.Width = 520;
        bagInfo.Height = 26;
        bagInfo.TextAlign = ContentAlignment.MiddleLeft;
        bagInfo.Margin = new Padding(8, 0, 0, 0);
        bagInfo.Tag = "muted";
        bagInfo.Text = "Oyuna bağlanınca çanta listelenir.";
        tools.Controls.Add(bagInfo);
        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Ui.Pad, 4, Ui.Pad, Ui.Pad) };
        host.Controls.Add(bagGrid);
        page.Controls.Add(host);
        page.Controls.Add(tools);
    }

    // Reads the bag when the page is in front: once when it is shown and then every two seconds.
    void InventoryTick(long now)
    {
        if (reader == null || tabs.SelectedTab != inventoryTab || now < nextBagRead)
            return;
        nextBagRead = now + 2000;
        RefreshInventory(false);
    }

    void RefreshInventory(bool report)
    {
        if (reader == null)
        {
            bagInfo.Text = "Oyuna bağlı değil.";
            return;
        }
        if (!ItemCatalog.Loaded)
            ItemCatalog.LoadFrom(Path.GetDirectoryName(reader.Process.MainModule.FileName));
        List<BagEntry> bag;
        try
        {
            bag = reader.ReadBag();
        }
        catch (IOException e)
        {
            bagInfo.Text = "Çanta okunamadı: " + e.Message;
            return;
        }
        int selected = bagGrid.CurrentRow == null ? -1 : bagGrid.CurrentRow.Index, first = bagGrid.FirstDisplayedScrollingRowIndex;
        bagGrid.Rows.Clear();
        foreach (var item in bag)
            bagGrid.Rows.Add(item.Slot, ItemCatalog.NameOf(item.Model), item.Quantity.ToString("N0"), item.Model);
        if (selected >= 0 && selected < bagGrid.Rows.Count)
            bagGrid.CurrentCell = bagGrid.Rows[selected].Cells[0];
        if (first >= 0 && first < bagGrid.Rows.Count)
            bagGrid.FirstDisplayedScrollingRowIndex = first;
        bagInfo.Text = "Eşya sayısı: " + bag.Count + (ItemCatalog.Loaded ? "" : " · adlar için oyunun data klasörü okunamadı");
    }

    void BuildNotificationsPage()
    {
        var tabsHere = SubTabs(notifyTab);
        var page = new TabPage("Olaylar");
        tabsHere.TabPages.Add(page);
        PlainGrid(eventGrid);
        eventGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Time", HeaderText = "Zaman", Width = 150 });
        eventGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Text", HeaderText = "Olay", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 300 });
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(Ui.Pad, 4, Ui.Pad, 0) };
        tools.Controls.Add(Btn("Listeyi temizle", delegate
        {
            eventGrid.Rows.Clear();
            lastEventCount = -1;
        }, 140, false, ButtonKind.Secondary));
        var hint = new Label { AutoSize = false, Width = 560, Height = 26, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(8, 0, 0, 0), Tag = "muted",
            Text = "Bot olayları: iş bitti, durdurma nedeni, ölüm, güncelleme. Bildirim seçenekleri Koruma > Uyarı / Yolculuk sayfasında." };
        tools.Controls.Add(hint);
        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Ui.Pad, 4, Ui.Pad, Ui.Pad) };
        host.Controls.Add(eventGrid);
        page.Controls.Add(host);
        page.Controls.Add(tools);
    }

    // phBot's Protection > Potions: uses an HP or MP potion from the bag when the stat is under the limit. Never blocks the run.
    void PotionTick(long now)
    {
        if (!(autoHpPotion.Checked || autoMpPotion.Checked) || snapshot == null || !snapshot.StatsKnown || !snapshot.PlayerAlive || reader == null ||
            sender == null || snapshot.MaxHp == 0)
            return;
        int hp = (int)(snapshot.Hp * 100L / snapshot.MaxHp), mp = snapshot.MaxMp == 0 ? 100 : (int)(snapshot.Mp * 100L / snapshot.MaxMp);
        var kind = autoHpPotion.Checked && hp < (int)hpPotionPercent.Value && now >= nextHpPotion ? V2Rules.Potion.Hp :
            autoMpPotion.Checked && mp < (int)mpPotionPercent.Value && now >= nextMpPotion ? V2Rules.Potion.Mp : V2Rules.Potion.None;
        if (kind == V2Rules.Potion.None || !CommandsAllowed())
            return;
        List<BagEntry> bag;
        try
        {
            bag = reader.ReadBag();
        }
        catch (IOException)
        {
            return;
        }
        var items = bag.Select(e => new BagItem { Model = e.Model, Slot = e.Slot, Quantity = e.Quantity, Address = e.Address, Type = reader.ItemType(e.Model) });
        var item = V2Rules.PickPotion(items, kind);
        // A potion kind that is not in the bag is looked for again in 10 seconds, not on every tick.
        if (item == null)
        {
            if (kind == V2Rules.Potion.Hp)
                nextHpPotion = now + 10000;
            else
                nextMpPotion = now + 10000;
            return;
        }
        if (!reader.ValidateItem(item))
            return;
        bool sent;
        try
        {
            sent = sender.Use(item);
        }
        catch (IOException e)
        {
            LogOnce("potion", "İksir gönderilemedi · " + e.Message);
            return;
        }
        if (kind == V2Rules.Potion.Hp)
            nextHpPotion = now + 1500;
        else
            nextMpPotion = now + 1500;
        if (!potionLogged)
        {
            potionLogged = true;
            Log((kind == V2Rules.Potion.Hp ? "HP" : "MP") + " iksiri kullanıldı: " + ItemCatalog.NameOf(item.Model) + " · yuva " + item.Slot + (sent ? "" : " (istemci reddetti)"));
        }
    }

    // Shows the events of this run (the lines the bot logged) while the page is in front.
    void NotificationsTick()
    {
        if (tabs.SelectedTab != notifyTab)
            return;
        if (eventLog.Version == lastEventCount)
            return;
        lastEventCount = eventLog.Version;
        var lines = eventLog.ToString().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r'))
            .Where(l => l.Length > 0 && !char.IsWhiteSpace(l[0])).ToList();
        eventGrid.Rows.Clear();
        foreach (string line in lines.Skip(Math.Max(0, lines.Count - 300)).Reverse())
        {
            int cut = line.IndexOf(" · ", StringComparison.Ordinal);
            if (cut > 0 && cut <= 20)
                eventGrid.Rows.Add(line.Substring(0, cut), line.Substring(cut + 3));
            else
                eventGrid.Rows.Add("", line);
        }
    }
}

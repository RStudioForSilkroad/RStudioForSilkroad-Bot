using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

// Nanzhang pages: the NPC list holds every NPC of the Nanzhang map (the first survey, the game's spawn table and the ones seen while walking),
// and the Quest page lists the quests of those NPCs with their rewards, read from the game's own quest tables.
sealed partial class MemorySelectV3Form
{
    NanzhangStore nzStore = new NanzhangStore();
    List<NpcMapRecord> nanzhangBuiltIn = new List<NpcMapRecord>();
    ListBox nanzhangList;
    TextBox nanzhangSearch;
    Label nanzhangHint;
    readonly CheckBox collectNpcs = new CheckBox();
    readonly CheckBox questRewardOnly = new CheckBox();
    readonly TextBox questSearch = new TextBox();
    readonly DataGridView questGrid = new DataGridView();
    readonly TextBox questText = new TextBox();
    readonly Label questInfo = new Label();
    TabPage questTab;
    long nextNzScan, nzSaveAt;
    bool nzDirty;

    // The built-in list: the first survey plus the spawns of the game's table that the survey did not see (same model within 14 units counts as seen).
    internal static List<NpcMapRecord> MergeTableNpcs(List<NpcMapRecord> survey)
    {
        var all = new List<NpcMapRecord>(survey);
        int number = survey.Count;
        foreach (var t in NanzhangData.World.npcs)
            if (!survey.Any(s => s.id == t.id && (!t.Located || Math.Abs(s.x - t.x) < 14 && Math.Abs(s.y - t.y) < 14)))
                all.Add(new NpcMapRecord { name = t.name, id = t.id, number = ++number, x = t.x, y = t.y, fromTable = true });
        return all;
    }

    // The list shows the built-in NPCs and the ones collected since (marked with a star).
    void RefreshNanzhangList()
    {
        if (nanzhangList == null)
            return;
        // An NPC the tables name without a position disappears from the list once the bot has seen it and recorded the position (★).
        var all = nanzhangBuiltIn.Where(b => b.x != 0 || b.y != 0 || !nzStore.Npcs.Any(c => c.Model == b.id)).ToList();
        int number = nanzhangBuiltIn.Count;
        foreach (var n in nzStore.Npcs)
            all.Add(new NpcMapRecord { name = n.Name, id = (int)n.Model, number = ++number, x = n.X, y = n.Y, collected = true, seen = n.Seen });
        string text = nanzhangSearch == null ? "" : nanzhangSearch.Text;
        var shown = all.Where(n => (n.name + " " + n.id).IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        var selected = nanzhangList.SelectedItem as NpcMapRecord;
        nanzhangList.BeginUpdate();
        nanzhangList.Items.Clear();
        foreach (var n in shown)
            nanzhangList.Items.Add(n);
        if (selected != null)
        {
            int at = shown.FindIndex(n => n.id == selected.id && n.number == selected.number);
            if (at >= 0)
                nanzhangList.SelectedIndex = at;
        }
        nanzhangList.EndUpdate();
        if (nanzhangHint != null && nanzhangList.SelectedItem == null)
            nanzhangHint.Text = "Fare tekerleği: yakınlaştır · Sol tuş: sürükle\n" + nanzhangBuiltIn.Count + " NPC (ilk gözlem + oyun tablosu) + " +
                nzStore.Npcs.Count + " toplanan (★). Nanzhang haritasında gezdikçe yeni NPC'ler eklenir.";
    }

    string NpcNameOf(uint model)
    {
        var known = nanzhangBuiltIn.FirstOrDefault(n => n.id == (int)model);
        if (known != null)
            return known.name;
        string name;
        return names.TryGetValue(model, out name) ? name : "NPC #" + model;
    }

    // While the character is on the Nanzhang map every NPC in view that is not known yet is recorded.
    void NanzhangTick(long now)
    {
        if (nzDirty && now >= nzSaveAt)
        {
            nzDirty = false;
            SaveNanzhangStore();
        }
        if (!collectNpcs.Checked || reader == null || snapshot == null || snapshot.Player == null || now < nextNzScan)
            return;
        nextNzScan = now + 1500;
        if (!NanzhangData.InMap(snapshot.Player))
            return;
        int added = 0;
        foreach (var npc in snapshot.Npcs)
        {
            if (npc.Position == null || !NanzhangData.InMap(npc.Position))
                continue;
            if (nanzhangBuiltIn.Any(b => b.id == (int)npc.Model && Math.Abs(b.x - npc.Position.X) < 14 && Math.Abs(b.y - npc.Position.Y) < 14) ||
                NanzhangData.Known(nzStore.Npcs, npc.Model, npc.Position.X, npc.Position.Y))
                continue;
            nzStore.Npcs.Add(new CollectedNpc { Model = npc.Model, Name = NpcNameOf(npc.Model), X = Math.Round(npc.Position.X, 1), Y = Math.Round(npc.Position.Y, 1),
                Region = npc.Position.Region, Seen = DateTime.Now.ToString("yyyy-MM-dd HH:mm") });
            added++;
        }
        if (added > 0)
        {
            nzDirty = true;
            nzSaveAt = now + 3000;
            RefreshNanzhangList();
            Log("Nanzhang: " + added + " yeni NPC kaydedildi · toplam " + nzStore.Npcs.Count);
        }
    }

    // ---- Quest page ----

    void BuildQuestPage()
    {
        var page = questTab;

        questGrid.Dock = DockStyle.Fill;
        questGrid.ReadOnly = true;
        questGrid.AllowUserToAddRows = false;
        questGrid.AllowUserToResizeRows = false;
        questGrid.RowHeadersVisible = false;
        questGrid.MultiSelect = false;
        questGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        questGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        questGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "QNpc", HeaderText = "NPC", Width = 190 });
        questGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "QTitle", HeaderText = "Görev", Width = 220 });
        questGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "QLevel", HeaderText = "Sev.", Width = 44 });
        questGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "QMission", HeaderText = "Yapılacak", Width = 260 });
        questGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "QReward", HeaderText = "Ödül", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 200 });
        questGrid.SelectionChanged += delegate { ShowQuestDetail(); };
        // Right click on a quest: the row is selected and the menu offers "Göreve Git" (walk to the NPC who gives it); double click does the same.
        var questMenu = new ContextMenuStrip();
        questMenu.Items.Add("Göreve Git", null, delegate { Safe(GoToQuest); });
        questGrid.ContextMenuStrip = questMenu;
        questGrid.MouseDown += delegate(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
                return;
            var hit = questGrid.HitTest(e.X, e.Y);
            if (hit.RowIndex >= 0)
            {
                questGrid.ClearSelection();
                questGrid.Rows[hit.RowIndex].Selected = true;
                questGrid.CurrentCell = questGrid.Rows[hit.RowIndex].Cells[0];
            }
        };
        questMenu.Opening += delegate(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = questGrid.CurrentRow == null || !(questGrid.CurrentRow.Tag is NzQuest);
        };
        questGrid.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0)
                Safe(GoToQuest);
        };

        questText.Multiline = true;
        questText.ReadOnly = true;
        questText.ScrollBars = ScrollBars.Vertical;
        questText.BorderStyle = BorderStyle.FixedSingle;
        questText.Dock = DockStyle.Bottom;
        questText.Height = 190;
        questText.Font = Ui.Body;

        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(Ui.Pad, 4, Ui.Pad, 0) };
        questSearch.Width = 150;
        questSearch.BorderStyle = BorderStyle.FixedSingle;
        questSearch.Margin = new Padding(0, 3, 8, 0);
        questSearch.TextChanged += delegate { RefreshQuestGrid(); };
        tools.Controls.Add(new Label { Text = "Ara", AutoSize = false, Width = 32, Height = 26, TextAlign = ContentAlignment.MiddleLeft, Tag = "muted" });
        tools.Controls.Add(questSearch);
        ConfigureCheck(questRewardOnly, "Yalnız ödüllü görevler", true, 0);
        questRewardOnly.Width = 170;
        questRewardOnly.CheckedChanged += delegate { RefreshQuestGrid(); };
        tools.Controls.Add(questRewardOnly);
        tools.Controls.Add(Btn("Göreve git", GoToQuest, 100, true, ButtonKind.Primary));
        tools.Controls.Add(Btn("Metne aktar", ExportQuests, 100, false, ButtonKind.Secondary));
        questInfo.AutoSize = false;
        questInfo.Width = 190;
        questInfo.AutoEllipsis = true;
        questInfo.Height = 26;
        questInfo.TextAlign = ContentAlignment.MiddleLeft;
        questInfo.Margin = new Padding(8, 0, 0, 0);
        questInfo.Tag = "muted";
        tools.Controls.Add(questInfo);
        // Where the list comes from, in one line under the buttons.
        var source = new Label { Text = "Kaynak: oyunun görev tabloları (Media) · ödüller görev penceresindeki “Reward” satırıdır · ödülsüzler yalnızca konuşmadır.",
            Dock = DockStyle.Top, Height = 22, Tag = "muted", Padding = new Padding(Ui.Pad, 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft };

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Ui.Pad, 4, Ui.Pad, Ui.Pad) };
        body.Controls.Add(questGrid);
        body.Controls.Add(questText);
        page.Controls.Add(body);
        page.Controls.Add(source);
        page.Controls.Add(tools);

        RefreshQuestGrid();
    }

    List<NzQuest> ShownQuests()
    {
        string text = questSearch.Text;
        return NanzhangData.World.quests.Where(q => (!questRewardOnly.Checked || q.HasReward) &&
            (text.Length == 0 || (q.npc + " " + q.title + " " + q.mission + " " + q.rewards).IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
    }

    void RefreshQuestGrid()
    {
        var shown = ShownQuests();
        var selected = questGrid.CurrentRow == null ? null : questGrid.CurrentRow.Tag as NzQuest;
        questGrid.Rows.Clear();
        foreach (var q in shown)
        {
            int r = questGrid.Rows.Add(q.npc, q.title, q.level, q.mission, q.rewards);
            questGrid.Rows[r].Tag = q;
            if (q == selected)
                questGrid.CurrentCell = questGrid.Rows[r].Cells[0];
        }
        questInfo.Text = shown.Count + " / " + NanzhangData.World.quests.Count + " görev · " + shown.Count(q => q.HasReward) + " ödüllü";
        ShowQuestDetail();
    }

    string QuestPlace(NzQuest q)
    {
        var npc = nanzhangBuiltIn.FirstOrDefault(n => n.name == q.npc);
        return npc == null ? "konumu bilinmiyor" : "X " + npc.x.ToString("F0") + " / Y " + npc.y.ToString("F0");
    }

    void ShowQuestDetail()
    {
        var row = questGrid.CurrentRow;
        var q = row == null ? null : row.Tag as NzQuest;
        questText.Text = q == null ? "" : q.title + "\r\n" + q.npc + " (" + QuestPlace(q) + ") · seviye " + q.level + "\r\nYapılacak: " + q.mission +
            "\r\nÖdül: " + q.rewards + "\r\n\r\n" + string.Join("\r\n\r\n", q.dialog);
    }

    // Where the NPC who gives a quest stands: the survey / table list first, then the NPCs the bot has seen itself.
    NpcMapRecord QuestNpc(NzQuest q)
    {
        var known = nanzhangBuiltIn.FirstOrDefault(n => n.name == q.npc && (n.x != 0 || n.y != 0));
        if (known != null)
            return known;
        var seen = nzStore.Npcs.FirstOrDefault(n => n.Name == q.npc);
        return seen == null ? null : new NpcMapRecord { name = seen.Name, id = (int)seen.Model, x = seen.X, y = seen.Y, collected = true };
    }

    // "Göreve Git": the character walks on foot to the NPC who gives the selected quest (roads only: no Return Scroll, no riding).
    void GoToQuest()
    {
        var row = questGrid.CurrentRow;
        var q = row == null ? null : row.Tag as NzQuest;
        if (q == null)
            throw new InvalidOperationException("Önce listeden bir görev seç.");
        var npc = QuestNpc(q);
        if (npc == null)
            throw new InvalidOperationException(q.npc + " NPC’sinin konumu henüz bilinmiyor; Nanzhang haritasında gezerken bot kaydeder (Nanzhang NPC sayfası).");
        RequireReader();
        var target = V2Rules.Center(npc.x, npc.y, 0);
        float height;
        if (!ExpeditionRules.TryGround(target, out height))
            throw new InvalidOperationException(q.npc + " konumunda yürünebilir zemin bulunamadı.");
        var d = new DungeonLocation { Code = "GOREV", Name = q.npc + " · " + q.title, Area = ExpeditionRules.Area(target), Mode = "Serbest",
            Region = target.Region, X = npc.x, Y = npc.y, LocalX = target.LocalX, LocalY = target.LocalY, Height = height };
        WalkDungeon(d, true);
        questInfo.Text = "Yürüyor: " + q.npc + " (X " + npc.x.ToString("F0") + " / Y " + npc.y.ToString("F0") + ")";
        Log("Göreve git: " + q.npc + " · " + q.title);
    }

    void ExportQuests()
    {
        var shown = ShownQuests();
        if (shown.Count == 0)
            throw new InvalidOperationException("Dışa aktarılacak görev yok.");
        string path = Path.Combine(V3Settings.DirectoryPath, "nanzhang-gorevler.txt");
        Directory.CreateDirectory(V3Settings.DirectoryPath);
        var sb = new StringBuilder();
        foreach (var q in shown)
        {
            sb.AppendLine("NPC: " + q.npc + " (" + QuestPlace(q) + ")");
            sb.AppendLine("Görev: " + q.title + " · seviye " + q.level);
            sb.AppendLine("Yapılacak: " + q.mission);
            sb.AppendLine("Ödül: " + q.rewards);
            foreach (string line in q.dialog)
                sb.AppendLine("  " + line);
            sb.AppendLine();
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        questInfo.Text = "Yazıldı: " + path;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    }

    void SaveNanzhangStore()
    {
        try
        {
            NanzhangData.Save(NanzhangData.StorePath, nzStore);
        }
        catch (IOException e)
        {
            Log("Nanzhang kaydı yazılamadı: " + e.Message);
        }
    }
}

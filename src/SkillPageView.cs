using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

// The "Skill ve Buff" overview of the Saldırı page (MSRO's Skill ve Buff layout): the Imbue selector, the numbered attack list and the numbered buff list
// side by side with their counters and arrows, the editor of the chosen skill, and the one-line footer. The skill grid (skillGrid) stays the data model;
// everything here reads from it and writes back into its cells, so profiles, templates, settings and the rotation see exactly what they saw before.
sealed partial class MemorySelectV3Form
{
    SkillListView attackView, buffView;
    CardBox attackCard, buffCard, imbueCard, editCard;
    readonly TextBox edName = new TextBox(), edKey = new TextBox(), edId = new TextBox(), edMobs = new TextBox(), edSeconds = new TextBox(),
        edPercent = new TextBox();
    readonly ComboBox edKind = new ComboBox(), edTarget = new ComboBox(), imbueBox = new ComboBox();
    readonly Label imbueHint = new Label(), skillCaption = new Label();
    int skillSel = -1;
    bool skillWriting, skillFilling, imbueFilling, skillRefreshQueued;
    const int SkillColumn = 410;

    sealed class ImbueChoice
    {
        // 0 = no Imbue line; otherwise the number (1 up) of the grid row of the Imbue line
        internal int Id;
        internal string Name = "", Text = "";

        public override string ToString()
        {
            return Text;
        }
    }

    static bool IsImbueName(string name)
    {
        return !string.IsNullOrEmpty(name) && name.IndexOf("imbue", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // Builds the overview into a sub-page flow (a vertical flow of cards).
    void BuildSkillOverview(FlowLayoutPanel flow)
    {
        cardHost = flow;
        int full = 2 * SkillColumn + Ui.Gap;

        // ---- Imbue ----
        var card = Card("Imbue", false, full);
        imbueCard = (CardBox)card.Parent;
        FlowLayoutPanel row;
        imbueBox.DropDownStyle = ComboBoxStyle.DropDownList;
        imbueBox.Width = cardInner;
        imbueBox.Margin = new Padding(0, 2, 0, 4);
        imbueBox.SelectionChangeCommitted += delegate { Safe(ApplyImbue); };
        locked.Add(imbueBox);
        card.Controls.Add(imbueBox);
        imbueHint.AutoSize = false;
        imbueHint.Width = cardInner;
        imbueHint.Height = 36;
        imbueHint.Tag = "muted";
        imbueHint.Text = "Savaş sırasında etkisi bitince yenilenir. Liste, adında “Imbue” geçen ve tuşu olan satırlardan oluşur; boşsa aşağıdan Imbue tuşunu bir satır olarak ekle (adına Imbue yaz).";
        card.Controls.Add(imbueHint);

        // ---- attack and buff lists, side by side ----
        var columns = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, Margin = new Padding(0) };
        flow.Controls.Add(columns);
        cardHost = columns;
        card = Card("Saldırı", false, SkillColumn);
        attackCard = (CardBox)card.Parent;
        attackView = new SkillListView("Saldırı skilli yok. “+ Skill ekle” ile bir hotbar tuşu ekle.") { Width = cardInner, Margin = new Padding(0, 0, 0, 6) };
        BindSkillView(attackView);
        locked.Add(attackView);
        card.Controls.Add(attackView);
        row = Line(card);
        AddMoveButtons(row);
        card = Card("Buff", false, SkillColumn);
        buffCard = (CardBox)card.Parent;
        buffView = new SkillListView("Buff, iyileştirme ya da mana skilli yok. “+ Skill ekle”, sonra Tür.") { Width = cardInner, Margin = new Padding(0, 0, 0, 6) };
        BindSkillView(buffView);
        locked.Add(buffView);
        card.Controls.Add(buffView);
        row = Line(card);
        AddMoveButtons(row);
        cardHost = flow;

        // ---- the chosen skill ----
        card = Card("Seçili skill", false, full);
        editCard = (CardBox)card.Parent;
        skillCaption.AutoSize = false;
        skillCaption.Width = cardInner;
        skillCaption.Height = 20;
        skillCaption.Tag = "muted";
        skillCaption.Text = "Listeden bir skill seç.";
        card.Controls.Add(skillCaption);
        row = Line(card);
        // the key comes first: a skill is cast only by pressing it (click the box and press the hotbar key, or type 1, F1, Q ...)
        EditorField(row, "Hotbar tuşu", edKey, 70);
        EditorField(row, "Ad", edName, 180);
        edKind.DropDownStyle = ComboBoxStyle.DropDownList;
        edKind.Items.AddRange(SkillRotation.Kinds);
        EditorField(row, "Tür", edKind, 120);
        row = Line(card);
        edTarget.DropDownStyle = ComboBoxStyle.DropDownList;
        edTarget.Items.AddRange(SkillRotation.Targets);
        EditorField(row, "Hedef türü", edTarget, 120);
        EditorField(row, "Mob ≥", edMobs, 56);
        EditorField(row, "Süre sn", edSeconds, 56);
        EditorField(row, "HP/MP %", edPercent, 56);
        row = Line(card);
        EditorField(row, "Buff ID (yalnız buff’ın açık olup olmadığını okumak için)", edId, 80);
        edName.TextChanged += delegate { WriteSkillCell("Name", edName.Text); };
        edKey.TextChanged += delegate { WriteSkillCell("Key", edKey.Text); };
        edKey.KeyDown += delegate(object s, KeyEventArgs e) { CaptureHotbarKey(e); };
        edId.TextChanged += delegate { WriteSkillCell("SkillId", edId.Text); };
        edMobs.TextChanged += delegate { WriteSkillCell("Mobs", edMobs.Text); };
        edSeconds.TextChanged += delegate { WriteSkillCell("Seconds", edSeconds.Text); };
        edPercent.TextChanged += delegate { WriteSkillCell("Percent", edPercent.Text); };
        edKind.SelectionChangeCommitted += delegate
        {
            WriteSkillCell("Kind", edKind.SelectedItem as string);
            SaveSettings();
        };
        edTarget.SelectionChangeCommitted += delegate
        {
            WriteSkillCell("Target", edTarget.SelectedItem as string);
            SaveSettings();
        };
        foreach (var box in new[] { edName, edKey, edId, edMobs, edSeconds, edPercent })
            box.Validated += delegate { SaveSettings(); };
        row = Line(card);
        row.Controls.Add(Btn("+ Skill ekle", delegate { AddSkillRow(new SkillSlot { Key = NewRowKey() }); }, 120, true, ButtonKind.Primary));
        row.Controls.Add(Btn("− Sil", RemoveSkillRow, 80));
        row.Controls.Add(Btn("Buff ID’sini öğren", LearnBuff, 170, false));
        skillInfo.AutoSize = false;
        skillInfo.Width = cardInner;
        skillInfo.Height = 22;
        skillInfo.Tag = "muted";
        skillInfo.Margin = new Padding(0, 2, 0, 0);
        card.Controls.Add(skillInfo);
        buffInfo.AutoSize = false;
        buffInfo.Width = cardInner;
        buffInfo.Height = 40;
        buffInfo.Tag = "muted";
        buffInfo.Margin = new Padding(0, 0, 0, 4);
        card.Controls.Add(buffInfo);

        var note = new Label { AutoSize = false, Width = full, Height = 40, Tag = "muted", Margin = new Padding(0, 0, 0, 6), Text =
            "Sıra yukarıdan aşağıya uygulanır; çalışma önceliği türe göredir: acil → iyileştirme → mana → buff → saldırı. Skill’ler yalnız hotbar tuşuna basılarak " +
            "kullanılır; oyun penceresi önde olmalı. Tuşu olmayan satır (turuncu) hiç kullanılmaz. Ayrıntılar Seçenekler ve Nasıl çalışır sekmelerinde." };
        flow.Controls.Add(note);

        // the columns follow the width of the page (between a narrow and a wide column)
        var host = flow.Parent;
        if (host != null)
            host.Resize += delegate { FitSkillColumns(host.ClientSize.Width); };

        skillGrid.CellValueChanged += delegate
        {
            if (!skillLoading)
                ScheduleSkillRefresh(true);
        };
        skillGrid.RowsAdded += delegate { ScheduleSkillRefresh(false); };
        skillGrid.RowsRemoved += delegate { ScheduleSkillRefresh(false); };
        skillGrid.CurrentCellChanged += delegate
        {
            if (skillLoading || skillWriting || skillGrid.CurrentRow == null)
                return;
            int index = skillGrid.CurrentRow.Index;
            if (index != skillSel)
                ChooseSkill(index, false);
        };
    }

    // The arrows under a list move the chosen line within its list.
    void AddMoveButtons(FlowLayoutPanel row)
    {
        row.Controls.Add(Btn("▲", delegate { MoveSkillWithinKind(skillSel, -1); }, 44, false));
        row.Controls.Add(Btn("▼", delegate { MoveSkillWithinKind(skillSel, 1); }, 44, false));
    }

    // The key box takes the hotbar key that is pressed in it (1-9, 0, A-Z, F1-F12); Backspace / Delete clear it as usual.
    void CaptureHotbarKey(KeyEventArgs e)
    {
        string name = SkillRotation.NameOfKey(e.KeyCode);
        if (name == null || e.Control || e.Alt)
            return;
        edKey.Text = name;
        edKey.SelectAll();
        e.SuppressKeyPress = true;
        e.Handled = true;
    }

    // The key a new line starts with: the first hotbar key no line uses yet.
    string NewRowKey()
    {
        return SkillRotation.NextFreeKey(skillGrid.Rows.Cast<DataGridViewRow>().Select(r => CellText(r.Cells["Key"].Value)));
    }

    void EditorField(FlowLayoutPanel row, string caption, Control field, int width)
    {
        row.Controls.Add(Caption(caption));
        field.Width = width;
        field.Margin = new Padding(0, 4, 14, 0);
        var box = field as TextBox;
        if (box != null)
            box.BorderStyle = BorderStyle.FixedSingle;
        locked.Add(field);
        row.Controls.Add(field);
    }

    void BindSkillView(SkillListView view)
    {
        view.Chosen += delegate(int row) { ChooseSkill(row, true); };
        view.Toggled += delegate(int row)
        {
            if (row < 0 || row >= skillGrid.Rows.Count)
                return;
            var cell = skillGrid.Rows[row].Cells["On"];
            cell.Value = !(cell.Value != null && Convert.ToBoolean(cell.Value));
        };
        view.Moved += delegate(int row, int direction)
        {
            ChooseSkill(row, true);
            MoveSkillWithinKind(row, direction);
        };
    }

    void FitSkillColumns(int pageWidth)
    {
        if (attackCard == null || pageWidth < 100)
            return;
        int column = Math.Max(360, Math.Min(640, (pageWidth - 2 * Ui.Pad - Ui.Gap - SystemInformation.VerticalScrollBarWidth - 4) / 2));
        int full = 2 * column + Ui.Gap, inner = column - 2 * Ui.Pad - 2, wide = full - 2 * Ui.Pad - 2;
        attackCard.MinimumSize = new Size(column, 0);
        buffCard.MinimumSize = new Size(column, 0);
        imbueCard.MinimumSize = new Size(full, 0);
        editCard.MinimumSize = new Size(full, 0);
        attackView.Width = inner;
        buffView.Width = inner;
        imbueBox.Width = wide;
        imbueHint.Width = wide;
        skillCaption.Width = wide;
        skillInfo.Width = wide;
        buffInfo.Width = wide;
    }

    // ---- reflecting the grid ----

    void ScheduleSkillRefresh(bool now)
    {
        if (attackView == null || skillLoading)
            return;
        if (now || !IsHandleCreated)
        {
            RefreshSkillViews();
            return;
        }
        if (skillRefreshQueued)
            return;
        skillRefreshQueued = true;
        BeginInvoke((MethodInvoker)delegate
        {
            skillRefreshQueued = false;
            RefreshSkillViews();
        });
    }

    SkillEntry EntryOf(int index)
    {
        var cells = skillGrid.Rows[index].Cells;
        string kind = CellText(cells["Kind"].Value);
        if (!SkillRotation.Kinds.Contains(kind) && kind != SkillRotation.PartyBuff_)
            kind = SkillRotation.Attack_;
        string name = CellText(cells["Name"].Value), key = CellText(cells["Key"].Value), target = CellText(cells["Target"].Value);
        int id = ToInt(cells["SkillId"].Value, 0), mobs = ToInt(cells["Mobs"].Value, 0), seconds = ToInt(cells["Seconds"].Value, 0), percent = ToInt(cells["Percent"].Value, 50);
        var detail = new List<string>();
        // a line that is never used says so first: no usable hotbar key, or an old party buff line
        bool legacy = kind == SkillRotation.PartyBuff_, keyless = !legacy && SkillRotation.ParseKey(key) == 0;
        if (legacy)
            detail.Add("Parti buff · kullanılmaz");
        else if (keyless)
            detail.Add(key.Length == 0 ? "⚠ TUŞ YOK · atılmaz" : "⚠ tuş geçersiz · atılmaz");
        // a line without a name is called after its key, and its detail then does not repeat it
        bool called = name.Length == 0 && key.Length > 0;
        if (!called && !keyless && !legacy)
            detail.Add("Tuş " + key);
        if (id > 0)
            detail.Add("Buff ID " + id);
        if (legacy)
        { }
        else if (kind != SkillRotation.Attack_)
            detail.Add(kind);
        else
            detail.Add("Hedef " + (target.Length > 0 ? target : SkillRotation.AnyTarget));
        if (mobs > 0)
            detail.Add("Mob ≥" + mobs);
        if (seconds > 0)
            detail.Add(seconds + " sn");
        if (kind == SkillRotation.Heal_ || kind == SkillRotation.Mana_ || kind == SkillRotation.Emergency_)
            detail.Add("%" + percent);
        if (name.Length == 0)
            name = key.Length > 0 ? "Tuş " + key + " skilli" : kind + " skilli";
        return new SkillEntry { Row = index, On = cells["On"].Value != null && Convert.ToBoolean(cells["On"].Value), Name = name, Kind = kind,
            Detail = string.Join(" · ", detail), Warn = legacy || keyless };
    }

    void RefreshSkillViews()
    {
        if (attackView == null || skillLoading)
            return;
        var attack = new List<SkillEntry>();
        var support = new List<SkillEntry>();
        for (int i = 0; i < skillGrid.Rows.Count; i++)
        {
            var entry = EntryOf(i);
            (entry.Kind == SkillRotation.Attack_ ? attack : support).Add(entry);
        }
        if (skillSel >= skillGrid.Rows.Count)
            skillSel = -1;
        attackView.SetItems(attack, skillSel);
        buffView.SetItems(support, skillSel);
        attackCard.Badge = attack.Count.ToString();
        buffCard.Badge = support.Count.ToString();
        if (!skillWriting)
            FillSkillEditor();
        RefreshImbueChoices();
    }

    void ChooseSkill(int row, bool fromView)
    {
        if (row < 0 || row >= skillGrid.Rows.Count)
            return;
        skillSel = row;
        attackView.Select(row);
        buffView.Select(row);
        FillSkillEditor();
        if (fromView && skillGrid.CurrentRow != null && skillGrid.CurrentRow.Index == row)
            return;
        // the grid's current row follows, because Buff ID learning, the id test and the key test work on it
        skillWriting = true;
        try
        {
            skillGrid.CurrentCell = skillGrid.Rows[row].Cells["Name"];
        }
        catch (InvalidOperationException) { }
        finally
        {
            skillWriting = false;
        }
    }

    void FillSkillEditor()
    {
        skillFilling = true;
        try
        {
            bool has = skillSel >= 0 && skillSel < skillGrid.Rows.Count;
            foreach (Control c in new Control[] { edName, edKey, edId, edMobs, edSeconds, edPercent, edKind, edTarget })
                c.Enabled = has && !active;
            if (!has)
            {
                foreach (var box in new[] { edName, edKey, edId, edMobs, edSeconds, edPercent })
                    box.Text = "";
                edKind.SelectedIndex = -1;
                edTarget.SelectedIndex = -1;
                skillCaption.Text = "Listeden bir skill seç.";
                return;
            }
            var cells = skillGrid.Rows[skillSel].Cells;
            edName.Text = CellText(cells["Name"].Value);
            edKey.Text = CellText(cells["Key"].Value);
            edId.Text = CellText(cells["SkillId"].Value);
            edMobs.Text = CellText(cells["Mobs"].Value);
            edSeconds.Text = CellText(cells["Seconds"].Value);
            edPercent.Text = CellText(cells["Percent"].Value);
            // an old party buff line shows no kind (it is not one of the kinds that can be chosen) and is never used
            if (SkillRotation.Kinds.Contains(CellText(cells["Kind"].Value)))
                edKind.SelectedItem = CellText(cells["Kind"].Value);
            else
                edKind.SelectedIndex = CellText(cells["Kind"].Value) == SkillRotation.PartyBuff_ ? -1 : SkillRotation.Kinds.ToList().IndexOf(SkillRotation.Attack_);
            edTarget.SelectedItem = SkillRotation.Targets.Contains(CellText(cells["Target"].Value)) ? CellText(cells["Target"].Value) : SkillRotation.AnyTarget;
            skillCaption.Text = (skillSel + 1) + ". satır · " + EntryOf(skillSel).Name;
        }
        finally
        {
            skillFilling = false;
        }
    }

    // The editor writes into the grid's cells; the settings are saved when a box is left (or a choice is made), not on every key.
    void WriteSkillCell(string column, string value)
    {
        if (skillFilling || skillSel < 0 || skillSel >= skillGrid.Rows.Count || skillLoading)
            return;
        skillWriting = true;
        try
        {
            skillGrid.Rows[skillSel].Cells[column].Value = value ?? "";
        }
        finally
        {
            skillWriting = false;
        }
    }

    // A line moves past the next line of its own list (attack or buff); lines of the other list in between stay where they are.
    void MoveSkillWithinKind(int row, int direction)
    {
        if (row < 0 || row >= skillGrid.Rows.Count)
            throw new InvalidOperationException("Önce listeden bir skill seç.");
        bool attack = EntryOf(row).Kind == SkillRotation.Attack_;
        int to = row + direction;
        while (to >= 0 && to < skillGrid.Rows.Count && (EntryOf(to).Kind == SkillRotation.Attack_) != attack)
            to += direction;
        if (to < 0 || to >= skillGrid.Rows.Count)
            return;
        var moving = skillGrid.Rows[row];
        skillLoading = true;
        try
        {
            skillGrid.Rows.RemoveAt(row);
            skillGrid.Rows.Insert(to, moving);
        }
        finally
        {
            skillLoading = false;
        }
        skillSel = to;
        RefreshSkillViews();
        ChooseSkill(to, false);
        SaveSettings();
    }

    // ---- Imbue ----

    // The Imbue lines of the list: rows whose name says Imbue (the choice carries the row number, 1 up).
    List<ImbueChoice> ImbueChoices()
    {
        var list = new List<ImbueChoice> { new ImbueChoice { Id = 0, Text = "Kullanma" } };
        for (int i = 0; i < skillGrid.Rows.Count; i++)
        {
            var cells = skillGrid.Rows[i].Cells;
            string name = CellText(cells["Name"].Value), key = CellText(cells["Key"].Value);
            if (IsImbueName(name))
                list.Add(new ImbueChoice { Id = i + 1, Name = name, Text = (i + 1) + ". " + name + (SkillRotation.ParseKey(key) == 0 ? " · TUŞ YOK" : " · Tuş " + key) });
        }
        return list;
    }

    void RefreshImbueChoices()
    {
        if (imbueFilling || imbueBox.Width <= 0)
            return;
        imbueFilling = true;
        try
        {
            var choices = ImbueChoices();
            int chosen = 0;
            for (int i = 0; i < skillGrid.Rows.Count; i++)
            {
                var cells = skillGrid.Rows[i].Cells;
                if (IsImbueName(CellText(cells["Name"].Value)) && cells["On"].Value != null && Convert.ToBoolean(cells["On"].Value))
                {
                    chosen = i + 1;
                    break;
                }
            }
            imbueBox.Items.Clear();
            foreach (var choice in choices)
                imbueBox.Items.Add(choice);
            int at = choices.FindIndex(c => c.Id == chosen);
            imbueBox.SelectedIndex = at < 0 ? 0 : at;
        }
        finally
        {
            imbueFilling = false;
        }
    }

    // Chosen Imbue: its line is turned on; every other Imbue line is turned off.
    // "Kullanma" turns all Imbue lines off. Nothing else about the lines changes.
    void ApplyImbue()
    {
        var choice = imbueBox.SelectedItem as ImbueChoice;
        if (choice == null || imbueFilling)
            return;
        for (int i = 0; i < skillGrid.Rows.Count; i++)
        {
            var cells = skillGrid.Rows[i].Cells;
            if (!IsImbueName(CellText(cells["Name"].Value)))
                continue;
            bool mine = choice.Id == i + 1;
            cells["On"].Value = mine;
            if (mine && CellText(cells["Kind"].Value) == SkillRotation.Attack_)
                cells["Kind"].Value = SkillRotation.Buff_;
        }
        SaveSettings();
        RefreshSkillViews();
        skillInfo.Text = choice.Id > 0 ? "Imbue: " + choice.Name : "Imbue kullanılmıyor.";
    }
}

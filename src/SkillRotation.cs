using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

// One line of the skill list: a hotbar key and when to press it.
//   Saldırı      pressed in turn while a target is selected; Seconds = this skill's own cooldown (0 = none)
//   Buff         pressed when due, no target needed; Seconds = repeat interval (0 = once per run)
//   İyileştirme  pressed when HP falls below Percent; Seconds = cooldown
//   Mana         pressed when MP falls below Percent; Seconds = cooldown
public sealed class SkillSlot
{
    public bool On = true;
    public string Key = "1";
    public string Kind = "Saldırı";
    public int Seconds;
    public int Percent = 50;
}

sealed class SkillRotation
{
    internal const string Attack_ = "Saldırı", Buff_ = "Buff", Heal_ = "İyileştirme", Mana_ = "Mana";
    internal static readonly string[] Kinds = { Attack_, Buff_, Heal_, Mana_ };

    sealed class Entry
    {
        internal ushort Key;
        internal string Kind;
        internal int Seconds, Percent;
        internal long Due;
    }

    readonly List<Entry> entries = new List<Entry>();
    int attackIndex;

    internal bool HasAttack
    {
        get { return entries.Any(e => e.Kind == Attack_); }
    }

    // The virtual-key code of a hotbar key name (1-9, 0, A-Z, F1-F12), or 0 when the name is not usable.
    internal static ushort ParseKey(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
        string t = text.Trim().ToUpperInvariant();
        if (t.Length == 1 && ((t[0] >= '0' && t[0] <= '9') || (t[0] >= 'A' && t[0] <= 'Z')))
            return t[0];
        int number;
        if (t.Length >= 2 && t.Length <= 3 && t[0] == 'F' && int.TryParse(t.Substring(1), out number) && number >= 1 && number <= 12)
            return (ushort)(0x70 + number - 1);
        return 0;
    }

    // An error message for the first unusable line, or null.
    internal static string Validate(IList<SkillSlot> slots)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            var s = slots[i];
            if (!s.On)
                continue;
            if (ParseKey(s.Key) == 0)
                return (i + 1) + ". skill: tuş geçersiz (1-9, 0, A-Z veya F1-F12).";
            if (!Kinds.Contains(s.Kind))
                return (i + 1) + ". skill: tür seçilmedi.";
            if (s.Seconds < 0 || s.Seconds > 3600)
                return (i + 1) + ". skill: süre 0-3600 sn olmalı.";
            if ((s.Kind == Heal_ || s.Kind == Mana_) && (s.Percent < 1 || s.Percent > 99))
                return (i + 1) + ". skill: yüzde 1-99 olmalı.";
        }
        return null;
    }

    internal void Load(IEnumerable<SkillSlot> slots)
    {
        entries.Clear();
        attackIndex = 0;
        foreach (var s in slots.Where(s => s.On))
        {
            ushort key = ParseKey(s.Key);
            if (key != 0 && Kinds.Contains(s.Kind))
                entries.Add(new Entry { Key = key, Kind = s.Kind, Seconds = Math.Max(0, s.Seconds), Percent = s.Percent });
        }
    }

    // All keys of the enabled lines, for the check against the start/stop shortcut.
    internal List<ushort> Keys()
    {
        return entries.Select(e => e.Key).Distinct().ToList();
    }

    // Heal first, then mana, then buffs: the key to press now without a target, or 0.
    internal ushort Support(long now, int hpPercent, int mpPercent, bool statsKnown)
    {
        foreach (string kind in new[] { Heal_, Mana_, Buff_ })
            foreach (var e in entries)
            {
                if (e.Kind != kind || now < e.Due)
                    continue;
                if (kind == Heal_ && !(statsKnown && hpPercent < e.Percent))
                    continue;
                if (kind == Mana_ && !(statsKnown && mpPercent < e.Percent))
                    continue;
                e.Due = kind == Buff_ ? (e.Seconds == 0 ? long.MaxValue : now + e.Seconds * 1000L) : now + Math.Max(2, e.Seconds) * 1000L;
                return e.Key;
            }
        return 0;
    }

    // The next attack key in turn whose own cooldown is over, or 0.
    internal ushort NextAttack(long now)
    {
        int count = entries.Count;
        for (int step = 0; step < count; step++)
        {
            int index = (attackIndex + step) % count;
            var e = entries[index];
            if (e.Kind != Attack_ || now < e.Due)
                continue;
            e.Due = now + e.Seconds * 1000L;
            attackIndex = (index + 1) % count;
            return e.Key;
        }
        return 0;
    }
}

sealed partial class MemorySelectV3Form
{
    readonly CheckBox skillListBox = new CheckBox();
    readonly DataGridView skillGrid = new DataGridView();
    readonly SkillRotation rotation = new SkillRotation();
    bool useRotation, skillLoading;

    static List<SkillSlot> DefaultSkills()
    {
        return new List<SkillSlot>
        {
            new SkillSlot { Key = "1", Kind = SkillRotation.Attack_ },
            new SkillSlot { Key = "2", Kind = SkillRotation.Attack_ },
            new SkillSlot { On = false, Key = "7", Kind = SkillRotation.Heal_, Seconds = 3, Percent = 50 },
            new SkillSlot { On = false, Key = "8", Kind = SkillRotation.Mana_, Seconds = 3, Percent = 40 },
            new SkillSlot { On = false, Key = "9", Kind = SkillRotation.Buff_, Seconds = 120 }
        };
    }

    void BuildSkillCard()
    {
        var card = Card("Skill listesi");
        var row = Line(card);
        ConfigureCheck(skillListBox, "Skill listesini kullan · üstteki Tuşlar kutusu yerine geçer", false, 0);
        row.Controls.Add(skillListBox);
        row = Line(card);
        skillGrid.Height = 212;
        skillGrid.AllowUserToAddRows = false;
        skillGrid.AllowUserToResizeRows = false;
        skillGrid.RowHeadersVisible = false;
        skillGrid.MultiSelect = false;
        skillGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        skillGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        skillGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "On", HeaderText = "Açık", Width = 52 });
        skillGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Key", HeaderText = "Tuş", Width = 56 });
        var kind = new DataGridViewComboBoxColumn { Name = "Kind", HeaderText = "Tür", Width = 124, FlatStyle = FlatStyle.Flat };
        kind.Items.AddRange(SkillRotation.Kinds);
        skillGrid.Columns.Add(kind);
        skillGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Seconds", HeaderText = "Süre (sn)", Width = 84 });
        skillGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Percent", HeaderText = "HP/MP altı %", Width = 108 });
        skillGrid.CurrentCellDirtyStateChanged += delegate
        {
            if (skillGrid.IsCurrentCellDirty && skillGrid.CurrentCell is DataGridViewCheckBoxCell)
                skillGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        skillGrid.CellValueChanged += delegate
        {
            if (!skillLoading)
                SaveSettings();
        };
        skillGrid.DataError += delegate(object sender, DataGridViewDataErrorEventArgs e) { e.ThrowException = false; };
        locked.Add(skillGrid);
        row.Controls.Add(Stretch(skillGrid));
        row = Line(card);
        row.Controls.Add(Btn("+ Skill ekle", delegate { AddSkillRow(new SkillSlot { Key = "3" }); }, 120, true, ButtonKind.Primary));
        row.Controls.Add(Btn("− Sil", RemoveSkillRow, 80));
        row.Controls.Add(Btn("▲", delegate { MoveSkillRow(-1); }, 44));
        row.Controls.Add(Btn("▼", delegate { MoveSkillRow(1); }, 44));
        row = Line(card);
        var help = new Label { AutoSize = false, Height = 88, Tag = "muted", Margin = new Padding(0, 0, 0, 4), Text =
            "Saldırı: hedef seçiliyken sırayla basılır; süre bu skill'in kendi bekleme süresidir.\r\n" +
            "Buff: süre dolunca basılır, hedef gerekmez (0 = çalıştırma başına bir kez).\r\n" +
            "İyileştirme / Mana: HP / MP yüzdesi sınırın altına inince basılır; süre bekleme süresidir.\r\n" +
            "Tuş: 1-9, 0, A-Z veya F1-F12. Öncelik: iyileştirme, mana, buff, saldırı." };
        row.Controls.Add(Stretch(help));
        LoadSkillRows(DefaultSkills());
    }

    void AddSkillRow(SkillSlot s)
    {
        int index = skillGrid.Rows.Add(s.On, s.Key, SkillRotation.Kinds.Contains(s.Kind) ? s.Kind : SkillRotation.Attack_,
            s.Seconds.ToString(), s.Percent.ToString());
        skillGrid.CurrentCell = skillGrid.Rows[index].Cells[1];
    }

    void LoadSkillRows(IEnumerable<SkillSlot> slots)
    {
        skillLoading = true;
        try
        {
            skillGrid.Rows.Clear();
            foreach (var s in slots.ToList())
                skillGrid.Rows.Add(s.On, s.Key, SkillRotation.Kinds.Contains(s.Kind) ? s.Kind : SkillRotation.Attack_, s.Seconds.ToString(),
                    s.Percent.ToString());
        }
        finally
        {
            skillLoading = false;
        }
    }

    void RemoveSkillRow()
    {
        if (skillGrid.CurrentRow != null)
        {
            skillGrid.Rows.RemoveAt(skillGrid.CurrentRow.Index);
            SaveSettings();
        }
    }

    void MoveSkillRow(int direction)
    {
        var row = skillGrid.CurrentRow;
        if (row == null)
            return;
        int from = row.Index, to = from + direction;
        if (to < 0 || to >= skillGrid.Rows.Count)
            return;
        skillGrid.Rows.RemoveAt(from);
        skillGrid.Rows.Insert(to, row);
        skillGrid.CurrentCell = skillGrid.Rows[to].Cells[1];
        SaveSettings();
    }

    static int ToInt(object value, int fallback)
    {
        int number;
        return value != null && int.TryParse(value.ToString().Trim(), out number) ? number : fallback;
    }

    List<SkillSlot> SkillSlotsFromGrid()
    {
        var list = new List<SkillSlot>();
        foreach (DataGridViewRow row in skillGrid.Rows)
            list.Add(new SkillSlot
            {
                On = row.Cells["On"].Value != null && Convert.ToBoolean(row.Cells["On"].Value),
                Key = row.Cells["Key"].Value == null ? "" : row.Cells["Key"].Value.ToString().Trim(),
                Kind = row.Cells["Kind"].Value == null ? "" : row.Cells["Kind"].Value.ToString(),
                Seconds = ToInt(row.Cells["Seconds"].Value, 0),
                Percent = ToInt(row.Cells["Percent"].Value, 50)
            });
        return list;
    }

    // Presses a heal, mana or buff key when one is due (no target needed). True when a key was sent this tick.
    bool PressSupport(long now)
    {
        if (!useRotation || !RunSendKeys || heldScan != 0 || now < nextKey)
            return false;
        int hp = snapshot.MaxHp > 0 ? (int)(snapshot.Hp * 100L / snapshot.MaxHp) : 100;
        int mp = snapshot.MaxMp > 0 ? (int)(snapshot.Mp * 100L / snapshot.MaxMp) : 100;
        ushort key = rotation.Support(now, hp, mp, snapshot.StatsKnown);
        if (key == 0 || !Press(key))
            return false;
        nextKey = now + RunDelay;
        return true;
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

// The "Skiller" page: the skill list, its options, profiles, buff-id learning and the background-key test.
sealed partial class MemorySelectV3Form
{
    readonly CheckBox skillListBox = new CheckBox(), backgroundKeys = new CheckBox(), optBuffWhileAttacking = new CheckBox(),
        optRearm = new CheckBox(), optInOrder = new CheckBox(), optFallback = new CheckBox(), optBuffOnly = new CheckBox();
    readonly DataGridView skillGrid = new DataGridView();
    readonly ComboBox profileBox = new ComboBox(), templateBox = new ComboBox();
    readonly Label skillInfo = new Label(), buffInfo = new Label();
    readonly SkillRotation rotation = new SkillRotation();
    bool useRotation, skillLoading;
    uint lastRegion;
    // Learning a buff id: the buffs present when the button was pressed, the row to fill, and the deadline.
    HashSet<uint> learnBase;
    int learnRow = -1;
    long learnUntil;
    // The background-key test: 0 idle, 1 waiting for the game to leave the foreground, 2 key sent and watching the effect.
    int bgPhase;
    long bgUntil;
    uint bgMp;
    HashSet<uint> bgBuffs;
    ushort bgKey;
    bool heldBackground;
    ushort heldVk;

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

    FlowLayoutPanel SkillRow(Control parent, int top = 0)
    {
        var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = new Padding(0, top, 0, 2),
            MaximumSize = new Size(900, 0) };
        parent.Controls.Add(row);
        return row;
    }

    void BuildSkillsPage()
    {
        var page = new TabPage("Skiller");
        tabs.TabPages.Add(page);
        var content = PageCard(page, "Skiller · buff · savunma", 940);
        var intro = new Label { AutoSize = false, Width = 900, Height = 46, Tag = "muted", Margin = new Padding(0, 0, 0, 6), Text =
            "Hotbar tuşlarını türe göre sıraya diz: saldırı skill'leri hedef türüne ve mob sayısına göre seçilir, buff'lar karakterin gerçek buff listesine " +
            "bakılarak bittiğinde yenilenir, acil buff'lar can düşünce ya da etrafı mob sarınca basılır. Liste Av ve Grim Crypt için ortaktır." };
        content.Controls.Add(intro);

        content.Controls.Add(SectionTitle("Genel"));
        var row = SkillRow(content);
        ConfigureCheck(skillListBox, "Skill listesini kullan · Av / Ayarlar’daki Tuşlar kutusu yerine geçer", false, 0);
        row.Controls.Add(skillListBox);
        row = SkillRow(content);
        ConfigureCheck(backgroundKeys, "Oyun önde değilken de tuşları gönder (arka plan)", false, 0);
        row.Controls.Add(backgroundKeys);
        row.Controls.Add(Btn("Arka plan tuşunu sına", BackgroundKeyTest, 210, false, ButtonKind.Secondary));
        row = SkillRow(content);
        ConfigureCheck(optBuffWhileAttacking, "Saldırırken de buff yenile", true, 0);
        ConfigureCheck(optRearm, "Dirilince / yeni bölgeye girince buff’ları yenile", true, 0);
        row.Controls.Add(optBuffWhileAttacking);
        row.Controls.Add(optRearm);
        row = SkillRow(content);
        ConfigureCheck(optInOrder, "Yeni hedefte saldırı sırasını baştan başlat", true, 0);
        ConfigureCheck(optFallback, "Türe özel skill hazır değilse Hepsi listesini kullan", true, 0);
        row.Controls.Add(optInOrder);
        row.Controls.Add(optFallback);
        row = SkillRow(content);
        ConfigureCheck(optBuffOnly, "Saldırma, yalnız buff / iyileştirme yap (Cleric, Bard gibi)", false, 0);
        row.Controls.Add(optBuffOnly);

        content.Controls.Add(SectionTitle("Profil ve şablon"));
        row = SkillRow(content);
        profileBox.Width = 220;
        profileBox.Margin = new Padding(0, 4, 8, 4);
        locked.Add(profileBox);
        row.Controls.Add(profileBox);
        row.Controls.Add(Btn("Kaydet", SaveSkillProfile, 90, true, ButtonKind.Primary));
        row.Controls.Add(Btn("Yükle", LoadSkillProfile, 90));
        row.Controls.Add(Btn("Sil", DeleteSkillProfile, 80, true, ButtonKind.Ghost));
        row = SkillRow(content);
        templateBox.Width = 260;
        templateBox.DropDownStyle = ComboBoxStyle.DropDownList;
        templateBox.Margin = new Padding(0, 4, 8, 4);
        templateBox.Items.AddRange(SkillRotation.TemplateNames);
        templateBox.SelectedIndex = 0;
        locked.Add(templateBox);
        row.Controls.Add(templateBox);
        row.Controls.Add(Btn("Şablonu listeye ekle", AddTemplate, 190));

        BuildLearnedSection(content);
        content.Controls.Add(SectionTitle("Skill listesi"));
        skillGrid.Width = 900;
        skillGrid.Height = 270;
        skillGrid.Margin = new Padding(0, 0, 0, 6);
        skillGrid.AllowUserToAddRows = false;
        skillGrid.AllowUserToResizeRows = false;
        skillGrid.RowHeadersVisible = false;
        skillGrid.MultiSelect = false;
        skillGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        skillGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        skillGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "On", HeaderText = "Açık", Width = 50 });
        skillGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Ad", Width = 150 });
        skillGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Key", HeaderText = "Tuş", Width = 52 });
        var kind = new DataGridViewComboBoxColumn { Name = "Kind", HeaderText = "Tür", Width = 112, FlatStyle = FlatStyle.Flat };
        kind.Items.AddRange(SkillRotation.Kinds);
        skillGrid.Columns.Add(kind);
        skillGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "SkillId", HeaderText = "Skill ID", Width = 76 });
        var target = new DataGridViewComboBoxColumn { Name = "Target", HeaderText = "Hedef türü", Width = 118, FlatStyle = FlatStyle.Flat };
        target.Items.AddRange(SkillRotation.Targets);
        skillGrid.Columns.Add(target);
        skillGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Mobs", HeaderText = "Mob ≥", Width = 82 });
        skillGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Seconds", HeaderText = "Süre sn", Width = 76 });
        skillGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Percent", HeaderText = "HP/MP %", Width = 98 });
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
        content.Controls.Add(skillGrid);
        row = SkillRow(content);
        row.Controls.Add(Btn("+ Skill ekle", delegate { AddSkillRow(new SkillSlot { Key = "3" }); }, 120, true, ButtonKind.Primary));
        row.Controls.Add(Btn("− Sil", RemoveSkillRow, 80));
        row.Controls.Add(Btn("▲", delegate { MoveSkillRow(-1); }, 44));
        row.Controls.Add(Btn("▼", delegate { MoveSkillRow(1); }, 44));
        row.Controls.Add(Btn("Buff ID’sini öğren", LearnBuff, 170, false));
        skillInfo.AutoSize = false;
        skillInfo.Width = 900;
        skillInfo.Height = 24;
        skillInfo.Tag = "muted";
        skillInfo.Margin = new Padding(0, 2, 0, 0);
        content.Controls.Add(skillInfo);
        buffInfo.AutoSize = false;
        buffInfo.Width = 900;
        buffInfo.Height = 48;
        buffInfo.Tag = "muted";
        buffInfo.Margin = new Padding(0, 0, 0, 6);
        content.Controls.Add(buffInfo);

        content.Controls.Add(SectionTitle("Nasıl çalışır"));
        var help = new Label { AutoSize = false, Width = 900, Height = 248, Tag = "muted", Margin = new Padding(0, 0, 0, 4), Text =
            "Saldırı: hedef seçiliyken sırayla basılır. Hedef türü General / Champion / Giant / Unique ... seçilirse yalnız o türdeki canavara basılır; " +
            "o türe özel satır yoksa Hepsi satırları kullanılır. “En az mob”: yakında (14 birim) en az bu kadar canavar varsa basılır (alan hasarı için). Süre = skill’in kendi bekleme süresi.\r\n" +
            "Buff: Skill ID varsa karakterin gerçek buff listesine bakılır, buff gidince tuş basılır. Skill ID yoksa süre dolunca basılır (0 = çalıştırma başına bir kez). " +
            "Skill ID’yi “Karakterin skilleri” listesinden seçersin (Skilleri oku → Listeye ekle) ya da “Buff ID’sini öğren” ile bulursun.\r\n" +
            "Tuşu boş, Skill ID’si dolu satır tuşsuz çalışır: bot skill’i kimliğiyle kullanır ve oyunun önde olması gerekmez. Tuşlu satır tuşa basar.\r\n" +
            "Acil: can yüzdesi sınırın altına inince ya da “En az mob” kadar canavar etrafı sarınca basılır; Skill ID varsa buff zaten açıksa basılmaz.\r\n" +
            "İyileştirme / Mana: HP / MP yüzdesi sınırın altına inince basılır. Öncelik: acil, iyileştirme, mana, buff, saldırı.\r\n" +
            "Arka plan: tuşlar oyun penceresine Windows mesajıyla gönderilir; oyun ön planda değilken de çalışır ama istemcinin bunu kabul ettiği " +
            "“Arka plan tuşunu sına” ile doğrulanmalıdır. Pencere simge durumundaysa bekler." };
        content.Controls.Add(help);
        LoadSkillRows(DefaultSkills());
        templateBox.SelectedIndex = 0;
    }

    void AddSkillRow(SkillSlot s)
    {
        int index = skillGrid.Rows.Add(RowValues(s));
        skillGrid.CurrentCell = skillGrid.Rows[index].Cells[2];
    }

    static object[] RowValues(SkillSlot s)
    {
        return new object[]
        {
            s.On, s.Name ?? "", s.Key, SkillRotation.Kinds.Contains(s.Kind) ? s.Kind : SkillRotation.Attack_, s.SkillId.ToString(),
            SkillRotation.Targets.Contains(s.Target) ? s.Target : SkillRotation.AnyTarget, s.MinMobs.ToString(), s.Seconds.ToString(), s.Percent.ToString()
        };
    }

    void LoadSkillRows(IEnumerable<SkillSlot> slots)
    {
        skillLoading = true;
        try
        {
            skillGrid.Rows.Clear();
            foreach (var s in slots.ToList())
                skillGrid.Rows.Add(RowValues(s));
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
        skillGrid.CurrentCell = skillGrid.Rows[to].Cells[2];
        SaveSettings();
    }

    static int ToInt(object value, int fallback)
    {
        int number;
        return value != null && int.TryParse(value.ToString().Trim(), out number) ? number : fallback;
    }

    static string CellText(object value)
    {
        return value == null ? "" : value.ToString().Trim();
    }

    List<SkillSlot> SkillSlotsFromGrid()
    {
        var list = new List<SkillSlot>();
        foreach (DataGridViewRow row in skillGrid.Rows)
            list.Add(new SkillSlot
            {
                On = row.Cells["On"].Value != null && Convert.ToBoolean(row.Cells["On"].Value),
                Name = CellText(row.Cells["Name"].Value),
                Key = CellText(row.Cells["Key"].Value),
                Kind = row.Cells["Kind"].Value == null ? "" : row.Cells["Kind"].Value.ToString(),
                SkillId = ToInt(row.Cells["SkillId"].Value, 0),
                Target = CellText(row.Cells["Target"].Value) == "" ? SkillRotation.AnyTarget : CellText(row.Cells["Target"].Value),
                MinMobs = ToInt(row.Cells["Mobs"].Value, 0),
                Seconds = ToInt(row.Cells["Seconds"].Value, 0),
                Percent = ToInt(row.Cells["Percent"].Value, 50)
            });
        return list;
    }

    SkillOptions SkillOptionsFromUi()
    {
        return new SkillOptions
        {
            BuffWhileAttacking = optBuffWhileAttacking.Checked, Rearm = optRearm.Checked, InOrder = optInOrder.Checked,
            Fallback = optFallback.Checked, BuffOnly = optBuffOnly.Checked
        };
    }

    void ApplySkillOptions(SkillOptions o)
    {
        o = o ?? new SkillOptions();
        optBuffWhileAttacking.Checked = o.BuffWhileAttacking;
        optRearm.Checked = o.Rearm;
        optInOrder.Checked = o.InOrder;
        optFallback.Checked = o.Fallback;
        optBuffOnly.Checked = o.BuffOnly;
    }

    void RestoreSkillSettings(V3Settings s)
    {
        skillListBox.Checked = s.SkillList;
        backgroundKeys.Checked = s.BackgroundKeys;
        ApplySkillOptions(s.SkillOptions);
        if (s.Skills != null && s.Skills.Count > 0)
            LoadSkillRows(s.Skills);
        RefreshProfileList(s.SkillProfiles, s.ActiveSkillProfile);
    }

    void SaveSkillSettings(V3Settings s)
    {
        // The page is built late; until then the loaded settings stay as they are.
        if (skillGrid.Columns.Count == 0)
            return;
        s.SkillList = skillListBox.Checked;
        s.BackgroundKeys = backgroundKeys.Checked;
        s.SkillOptions = SkillOptionsFromUi();
        s.Skills = SkillSlotsFromGrid();
        s.ActiveSkillProfile = profileBox.Text.Trim();
    }

    // ---- profiles ----

    void RefreshProfileList(List<SkillProfile> profiles, string select)
    {
        profileBox.Items.Clear();
        if (profiles != null)
            foreach (var p in profiles)
                profileBox.Items.Add(p.Name);
        profileBox.Text = select ?? "";
    }

    string ProfileName()
    {
        string name = profileBox.Text.Trim();
        if (name.Length == 0)
            throw new InvalidOperationException("Profil için bir ad yaz (örneğin karakterinin adı).");
        if (name.Length > 40)
            throw new InvalidOperationException("Profil adı 40 karakterden uzun olamaz.");
        return name;
    }

    void SaveSkillProfile()
    {
        string name = ProfileName();
        var slots = SkillSlotsFromGrid();
        string problem = SkillRotation.Validate(slots);
        if (problem != null)
            throw new InvalidOperationException(problem);
        if (settings.SkillProfiles == null)
            settings.SkillProfiles = new List<SkillProfile>();
        settings.SkillProfiles.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        settings.SkillProfiles.Add(new SkillProfile { Name = name, Slots = slots, Options = SkillOptionsFromUi() });
        SaveSettings();
        RefreshProfileList(settings.SkillProfiles, name);
        skillInfo.Text = "Profil kaydedildi: " + name;
    }

    void LoadSkillProfile()
    {
        string name = ProfileName();
        var profile = (settings.SkillProfiles ?? new List<SkillProfile>()).FirstOrDefault(p => string.Equals(p.Name, name,
            StringComparison.OrdinalIgnoreCase));
        if (profile == null)
            throw new InvalidOperationException("“" + name + "” adlı profil yok.");
        LoadSkillRows(profile.Slots);
        ApplySkillOptions(profile.Options);
        SaveSettings();
        skillInfo.Text = "Profil yüklendi: " + profile.Name;
    }

    void DeleteSkillProfile()
    {
        string name = ProfileName();
        if (settings.SkillProfiles == null || settings.SkillProfiles.RemoveAll(p => string.Equals(p.Name, name,
            StringComparison.OrdinalIgnoreCase)) == 0)
            throw new InvalidOperationException("“" + name + "” adlı profil yok.");
        SaveSettings();
        RefreshProfileList(settings.SkillProfiles, "");
        skillInfo.Text = "Profil silindi: " + name;
    }

    void AddTemplate()
    {
        var name = templateBox.SelectedItem as string;
        foreach (var s in SkillRotation.Template(name))
            skillGrid.Rows.Add(RowValues(s));
        SaveSettings();
        skillInfo.Text = "“" + name + "” eklendi. Satırlar kapalı: tuşları hotbar’a göre ayarlayıp Açık kutusunu işaretle.";
    }

    // ---- live context for the rotation ----

    int MonstersNear()
    {
        return snapshot.Monsters.Count(m => m.Alive && m.Health != 0 && m.Position.Distance(snapshot.Player) <= 14);
    }

    SkillContext SkillContextNow()
    {
        int near = MonstersNear();
        return new SkillContext
        {
            StatsKnown = snapshot.StatsKnown,
            Hp = snapshot.MaxHp > 0 ? (int)(snapshot.Hp * 100L / snapshot.MaxHp) : 100,
            Mp = snapshot.MaxMp > 0 ? (int)(snapshot.Mp * 100L / snapshot.MaxMp) : 100,
            Buffs = snapshot.BuffsKnown ? snapshot.Buffs : null,
            Attackers = near,
            InCombat = near > 0 && snapshot.Selected != 0
        };
    }

    // Presses an emergency, heal, mana or buff key when one is due (no target needed). True when a key was sent this tick.
    bool PressSupport(long now)
    {
        if (!useRotation || !RunSendKeys || heldScan != 0 || now < nextKey)
            return false;
        ushort key = rotation.Support(now, SkillContextNow());
        if (key == 0 || !PressSkill(key, 0))
            return false;
        nextKey = now + RunDelay;
        return true;
    }

    // Called every look at the game: buffs are due again after a revival or a change of area; learns buff ids; runs the key test.
    void SkillTick(long now)
    {
        if (snapshot == null)
            return;
        if (snapshot.Player != null && snapshot.Player.Region != lastRegion)
        {
            if (lastRegion != 0 && active && useRotation && rotation.Options.Rearm)
            {
                rotation.Rearm();
                Log("Yeni bölge: buff’lar yenilenecek.");
            }
            lastRegion = snapshot.Player.Region;
        }
        if (learnUntil != 0)
        {
            var added = snapshot.BuffsKnown ? snapshot.Buffs.Where(b => !learnBase.Contains(b)).OrderBy(b => b).ToList() : new List<uint>();
            if (added.Count > 0 && learnRow >= 0 && learnRow < skillGrid.Rows.Count)
            {
                skillGrid.Rows[learnRow].Cells["SkillId"].Value = added[0].ToString();
                buffInfo.Text = "Skill ID bulundu: " + added[0] + (added.Count > 1 ? " (aynı anda gelen diğerleri: " + string.Join(", ",
                    added.Skip(1).Select(b => b.ToString())) + ")" : "") + " · " + (learnRow + 1) + ". satıra yazıldı.";
                Log("Buff ID öğrenildi: " + string.Join(", ", added.Select(b => b.ToString())));
                learnUntil = 0;
                SaveSettings();
            }
            else if (now > learnUntil)
            {
                learnUntil = 0;
                buffInfo.Text = "Yeni buff görülmedi. Düğmeye bastıktan sonra buff’ı oyunda kendin kullanmalısın.";
            }
        }
        BackgroundKeyTestTick(now);
        CastTestTick(now);
        if (buffInfo.Visible && learnUntil == 0 && bgPhase == 0 && !buffInfo.Text.StartsWith("Skill ID bulundu") && !buffInfo.Text.StartsWith("Arka plan") &&
            !buffInfo.Text.StartsWith("Yeni buff"))
        {
            string text = snapshot.BuffsKnown ? "Aktif buff ID’leri: " + (snapshot.Buffs.Count == 0 ? "yok" : string.Join(", ",
                snapshot.Buffs.OrderBy(b => b).Select(b => b.ToString()))) : "Aktif buff listesi okunamadı.";
            if (buffInfo.Text != text)
                buffInfo.Text = text;
        }
    }

    void LearnBuff()
    {
        RequireReader();
        if (skillGrid.CurrentRow == null)
            throw new InvalidOperationException("Önce listeden buff satırını seç.");
        snapshot = reader.Capture();
        if (!snapshot.BuffsKnown)
            throw new InvalidOperationException("Karakterin buff listesi okunamadı.");
        learnRow = skillGrid.CurrentRow.Index;
        learnBase = new HashSet<uint>(snapshot.Buffs);
        learnUntil = clock.ElapsedMilliseconds + 30000;
        buffInfo.Text = "Öğreniliyor: oyunda " + (learnRow + 1) + ". satırın buff’ını şimdi kendin kullan (30 sn). Zaten açık olan buff’lar sayılmaz.";
    }

    // ---- background keys ----

    IntPtr ClientWindow()
    {
        return reader == null ? IntPtr.Zero : FindClientWindow(reader.Process);
    }

    void BackgroundKeyTest()
    {
        RequireReader();
        if (active)
            throw new InvalidOperationException("Önce çalışan işi durdur.");
        if (skillGrid.CurrentRow == null)
            throw new InvalidOperationException("Sınamak için listeden bir skill satırı seç; o satırın tuşu basılır.");
        bgKey = SkillRotation.ParseKey(CellText(skillGrid.CurrentRow.Cells["Key"].Value));
        if (bgKey == 0)
            throw new InvalidOperationException("Seçili satırın tuşu geçersiz.");
        if (ClientWindow() == IntPtr.Zero)
            throw new InvalidOperationException("Oyun penceresi bulunamadı.");
        bgPhase = 1;
        bgUntil = clock.ElapsedMilliseconds + 25000;
        buffInfo.Text = "Arka plan sınaması: oyun penceresini arkaya al (başka bir pencereye tıkla). " +
            "Oyun öndeyken tuş basılmaz; arkadayken seçili satırın tuşu bir kez gönderilir ve etkisi (mana ya da yeni buff) izlenir.";
    }

    void FinishBackgroundTest(bool worked, string text, bool disable = true)
    {
        bgPhase = 0;
        buffInfo.Text = "Arka plan sınaması: " + text;
        Log("Arka plan tuş sınaması: " + (worked ? "çalıştı" : "çalışmadı") + " · " + text);
        status.Text = buffInfo.Text;
        if (!worked && disable && backgroundKeys.Checked)
            backgroundKeys.Checked = false;
        SaveSettings();
    }

    void BackgroundKeyTestTick(long now)
    {
        if (bgPhase == 0)
            return;
        if (active || reader == null)
        {
            bgPhase = 0;
            return;
        }
        if (bgPhase == 1)
        {
            if (now > bgUntil)
            {
                FinishBackgroundTest(false, "oyun 25 sn içinde arka plana alınmadı; sınama iptal.", false);
                return;
            }
            if (reader.IsForeground() || reader.IsMinimized())
                return;
            bgMp = snapshot.Mp;
            bgBuffs = new HashSet<uint>(snapshot.Buffs);
            if (!PressBackground(bgKey))
            {
                FinishBackgroundTest(false, "tuş gönderilemedi.");
                return;
            }
            bgPhase = 2;
            bgUntil = now + 3500;
            return;
        }
        if (reader.IsForeground())
        {
            FinishBackgroundTest(false, "oyun sınama sırasında öne geldi; sonuç geçersiz, yeniden dene.", false);
            return;
        }
        bool newBuff = snapshot.BuffsKnown && snapshot.Buffs.Any(b => !bgBuffs.Contains(b));
        if (newBuff || (snapshot.StatsKnown && snapshot.Mp < bgMp))
            FinishBackgroundTest(true, "ÇALIŞTI. Oyun arkadayken tuş işlendi; arka plan tuşlarını açık bırakabilirsin.");
        else if (now > bgUntil)
            FinishBackgroundTest(false, "ÇALIŞMADI. Tuş gönderildi ama mana düşmedi, yeni buff gelmedi (mana ya da hedef yoksa da böyle olur). " +
                "Buff kullanan bir satırla tekrar dene; yine olmazsa bu istemci arka plan tuşunu kabul etmiyor, oyun önde kalmalı. Arka plan tuşları kapatıldı.");
    }

    // Sends one key to the game window with Windows keyboard messages; the release follows from the release timer.
    bool PressBackground(ushort key)
    {
        IntPtr window = ClientWindow();
        ushort scan = (ushort)Native.MapVirtualKeyEx(key, 0, Native.GetKeyboardLayout(0));
        if (window == IntPtr.Zero || scan == 0 || !KeyMessage.Post(window, key, scan, false))
            return false;
        heldScan = scan;
        heldVk = key;
        heldBackground = true;
        release.Start();
        return true;
    }
}

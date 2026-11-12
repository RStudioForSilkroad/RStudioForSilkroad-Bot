using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

// The "Karakterin skilleri" part of the Skiller page: the skills the connected character has learned (found in the game's memory),
// chosen by name and used by their id, so no hotbar key has to be set. The game's own skill table names them.
sealed partial class MemorySelectV3Form
{
    readonly DataGridView learnedGrid = new DataGridView();
    readonly TextBox skillSearch = new TextBox();
    readonly CheckBox learnedAll = new CheckBox();
    readonly Label learnedInfo = new Label();
    List<uint> learnedIds;
    bool skillScanning, filteringLearned;
    // The use-by-id test: 0 idle, 1 skill sent and watching the effect.
    int castPhase;
    long castUntil;
    uint castMp;
    HashSet<uint> castBuffs;
    uint castSkill;

    void BuildLearnedSection(Control content)
    {
        content.Controls.Add(SectionTitle("Karakterin skilleri"));
        var info = new Label { AutoSize = false, Width = 900, Height = 46, Tag = "muted", Margin = new Padding(0, 0, 0, 4), Text =
            "Oyuna bağlıyken “Skilleri oku” karakterin öğrendiği skill’leri bulur. Listeden seçip “Listeye ekle” dersen satır tuşsuz eklenir: " +
            "bot skill’i kimliğiyle kullanır, oyunun önde olması gerekmez. Skill adı bulunamazsa “Tüm skiller” ile oyunun skill tablosunda ada göre ara." };
        content.Controls.Add(info);
        var row = SkillRow(content);
        row.Controls.Add(Btn("Skilleri oku", ReadLearnedSkills, 140, false, ButtonKind.Primary));
        ConfigureCheck(learnedAll, "Tüm skiller (oyun tablosu)", false, 0);
        learnedAll.Margin = new Padding(8, 4, 16, 1);
        learnedAll.CheckedChanged += delegate { RefreshLearnedGrid(); };
        row.Controls.Add(learnedAll);
        row.Controls.Add(new Label { Text = "Ara", AutoSize = false, Width = 32, Height = 30, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 3, 0, 0) });
        skillSearch.Width = 200;
        skillSearch.Margin = new Padding(0, 5, 8, 0);
        skillSearch.TextChanged += delegate { RefreshLearnedGrid(); };
        row.Controls.Add(skillSearch);

        learnedGrid.Width = 900;
        learnedGrid.Height = 190;
        learnedGrid.Margin = new Padding(0, 4, 0, 6);
        learnedGrid.ReadOnly = true;
        learnedGrid.AllowUserToAddRows = false;
        learnedGrid.AllowUserToResizeRows = false;
        learnedGrid.RowHeadersVisible = false;
        learnedGrid.MultiSelect = false;
        learnedGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        learnedGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        learnedGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "LName", HeaderText = "Skill", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 200 });
        learnedGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "LLevel", HeaderText = "Sv", Width = 60 });
        learnedGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "LId", HeaderText = "Skill ID", Width = 100 });
        learnedGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "LReq", HeaderText = "Gereken seviye", Width = 130 });
        learnedGrid.CellDoubleClick += delegate(object o, DataGridViewCellEventArgs ev)
        {
            if (ev.RowIndex >= 0)
                Safe(AddLearnedToList);
        };
        content.Controls.Add(learnedGrid);
        row = SkillRow(content);
        row.Controls.Add(Btn("Listeye ekle", AddLearnedToList, 140, false, ButtonKind.Primary));
        row.Controls.Add(Btn("Kimlikle sına", CastTest, 140, false, ButtonKind.Secondary));
        learnedInfo.AutoSize = false;
        learnedInfo.Width = 900;
        learnedInfo.Height = 46;
        learnedInfo.Tag = "muted";
        learnedInfo.Margin = new Padding(0, 2, 0, 6);
        learnedInfo.Text = "Skiller henüz okunmadı.";
        content.Controls.Add(learnedInfo);
    }

    bool EnsureCatalog()
    {
        if (SkillCatalog.Loaded)
            return true;
        if (reader == null)
            return false;
        try
        {
            return SkillCatalog.LoadFrom(Path.GetDirectoryName(reader.Process.MainModule.FileName));
        }
        catch (Exception)
        {
            return false;
        }
    }

    // Skill ids the character certainly has: the buffs it carries and the ids already in the list.
    List<uint> SkillSeeds()
    {
        var seeds = new HashSet<uint>();
        if (snapshot != null && snapshot.BuffsKnown)
            foreach (uint b in snapshot.Buffs)
                if (SkillCatalog.IsSkill(b))
                    seeds.Add(b);
        foreach (var s in SkillSlotsFromGrid())
            if (s.SkillId > 0 && SkillCatalog.IsSkill((uint)s.SkillId))
                seeds.Add((uint)s.SkillId);
        return seeds.ToList();
    }

    // The skill ids of the newest macro file of the game (config\macro-*.ini): the game's own macro page keeps the skills the player chose.
    List<uint> MacroSeeds()
    {
        var seeds = new List<uint>();
        try
        {
            string dir = Path.Combine(Path.GetDirectoryName(reader.Process.MainModule.FileName), "config");
            if (!Directory.Exists(dir))
                return seeds;
            foreach (var file in new DirectoryInfo(dir).GetFiles("macro*.ini").OrderByDescending(f => f.LastWriteTimeUtc))
            {
                foreach (string line in File.ReadAllLines(file.FullName))
                {
                    var m = Regex.Match(line, @"^(?:skillId|buffId)\d+=(\d+)\s*$");
                    uint id;
                    if (m.Success && uint.TryParse(m.Groups[1].Value, out id) && SkillCatalog.IsSkill(id))
                        seeds.Add(id);
                }
                if (seeds.Count > 0)
                    break;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return seeds.Distinct().ToList();
    }

    void ReadLearnedSkills()
    {
        RequireReader();
        if (skillScanning)
            throw new InvalidOperationException("Skill taraması sürüyor.");
        if (!EnsureCatalog())
            throw new InvalidOperationException("Oyunun data\\skillname.txt dosyası okunamadı; skill adları için gerekli.");
        snapshot = reader.Capture();
        var seeds = SkillSeeds();
        string source = "aktif buff ve listedeki ID'ler";
        if (seeds.Count == 0)
        {
            seeds = MacroSeeds();
            source = "oyunun makro ayarı";
        }
        skillScanning = true;
        learnedInfo.Text = "Taranıyor… oyun belleği okunuyor, birkaç saniye sürebilir.";
        var game = reader;
        ThreadPool.QueueUserWorkItem(delegate
        {
            List<uint> ids;
            string report;
            try
            {
                ids = SkillScan.Read(game, seeds, out report);
            }
            catch (Exception e)
            {
                ids = new List<uint>();
                report = e.Message;
            }
            try
            {
                BeginInvoke((MethodInvoker)delegate { FinishScan(ids, report, source); });
            }
            catch (InvalidOperationException) { }
        });
    }

    void FinishScan(List<uint> ids, string report, string source)
    {
        skillScanning = false;
        learnedIds = ids;
        if (ids.Count == 0)
        {
            learnedInfo.Text = "Karakterin skill listesi bellekte bulunamadı (" + report + "). “Tüm skiller” kutusuyla oyunun skill tablosunda ada göre arayıp ekleyebilirsin.";
            Log("Skill taraması: bulunamadı · " + report);
        }
        else
        {
            learnedInfo.Text = ids.Count + " skill bulundu · " + report + ". Listeden seç, “Listeye ekle”.";
            Log("Skill taraması: " + ids.Count + " skill · " + report);
        }
        learnedAll.Checked = false;
        RefreshLearnedGrid();
    }

    void RefreshLearnedGrid()
    {
        if (filteringLearned || learnedGrid.Columns.Count == 0)
            return;
        filteringLearned = true;
        try
        {
            string text = skillSearch.Text.Trim();
            IEnumerable<SkillInfo> list;
            if (learnedAll.Checked)
            {
                if (!EnsureCatalog())
                {
                    learnedInfo.Text = "Skill tablosu okunamadı: oyuna bağlan; tablo oyunun data klasöründedir.";
                    learnedGrid.Rows.Clear();
                    return;
                }
                list = SkillCatalog.Search(text, 300);
                learnedInfo.Text = text.Length < 2 ? "Aramak için en az 2 harf yaz." : "Oyunun skill tablosu · " + SkillCatalog.Count + " kayıt.";
            }
            else
                list = (learnedIds ?? new List<uint>()).Select(id => SkillCatalog.Find(id) ?? new SkillInfo { Id = id, Name = "Bilinmeyen #" + id })
                    .Where(s => text.Length == 0 || s.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(s => s.Name)
                    .ThenBy(s => s.Level).ToList();
            learnedGrid.Rows.Clear();
            foreach (var s in list)
            {
                int r = learnedGrid.Rows.Add(s.Name, s.Level > 0 ? s.Level.ToString() : "", s.Id.ToString(), s.ReqLevel > 0 ? s.ReqLevel.ToString() : "");
                learnedGrid.Rows[r].Tag = s;
            }
        }
        finally
        {
            filteringLearned = false;
        }
    }

    SkillInfo SelectedLearned()
    {
        var row = learnedGrid.CurrentRow;
        var info = row == null ? null : row.Tag as SkillInfo;
        if (info == null)
            throw new InvalidOperationException("Önce yukarıdaki listeden bir skill seç.");
        return info;
    }

    void AddLearnedToList()
    {
        var info = SelectedLearned();
        var slot = new SkillSlot { On = true, Key = "", Name = info.Display, SkillId = (int)info.Id, Kind = SkillCatalog.GuessKind(info) };
        int index = skillGrid.Rows.Add(RowValues(slot));
        skillGrid.CurrentCell = skillGrid.Rows[index].Cells["Kind"];
        SaveSettings();
        skillInfo.Text = "“" + info.Display + "” eklendi (tuşsuz, ID " + info.Id + "). Türünü (Saldırı / Buff ...) kontrol et.";
    }

    // ---- use-by-id test ----

    void CastTest()
    {
        RequireReader();
        if (active)
            throw new InvalidOperationException("Önce çalışan işi durdur.");
        if (skillGrid.CurrentRow == null)
            throw new InvalidOperationException("Sınamak için Skill listesinden Skill ID’si olan bir satır seç.");
        int id = ToInt(skillGrid.CurrentRow.Cells["SkillId"].Value, 0);
        if (id <= 0)
            throw new InvalidOperationException("Seçili satırın Skill ID’si yok.");
        snapshot = reader.Capture();
        if (!snapshot.PlayerAlive)
            throw new InvalidOperationException("Karakter canlı değil.");
        string kind = skillGrid.CurrentRow.Cells["Kind"].Value == null ? "" : skillGrid.CurrentRow.Cells["Kind"].Value.ToString();
        uint target = kind == SkillRotation.Attack_ ? snapshot.Selected : 0;
        if (kind == SkillRotation.Attack_ && target == 0)
            throw new InvalidOperationException("Saldırı skill’ini sınamak için oyunda bir canavar seç (tıkla).");
        castMp = snapshot.Mp;
        castBuffs = new HashSet<uint>(snapshot.Buffs);
        castSkill = (uint)id;
        bool sent;
        using (var s = new TargetSender(reader))
            sent = s.Cast(castSkill, target);
        if (!sent)
        {
            learnedInfo.Text = "Kimlik sınaması: istemci komutu reddetti.";
            return;
        }
        castPhase = 1;
        castUntil = clock.ElapsedMilliseconds + 3000;
        learnedInfo.Text = "Kimlik sınaması: " + SkillLabel(id) + " gönderildi, etkisi izleniyor (3 sn).";
    }

    void CastTestTick(long now)
    {
        if (castPhase == 0)
            return;
        bool newBuff = snapshot.BuffsKnown && snapshot.Buffs.Any(b => !castBuffs.Contains(b));
        bool manaDown = snapshot.StatsKnown && snapshot.Mp < castMp;
        if (newBuff || manaDown)
        {
            castPhase = 0;
            learnedInfo.Text = "Kimlik sınaması: ÇALIŞTI. " + SkillLabel((int)castSkill) + " kimliğiyle kullanıldı (" + (newBuff ? "yeni buff geldi" : "mana düştü") +
                "). Bu satırı tuşsuz bırakabilirsin; oyun arkadayken de çalışır.";
            Log("Skill kimlik sınaması: çalıştı · " + castSkill);
        }
        else if (now > castUntil)
        {
            castPhase = 0;
            learnedInfo.Text = "Kimlik sınaması: etki görülmedi (mana düşmedi, yeni buff yok). Mana, bekleme süresi ya da hedef yoksa da böyle olur; " +
                "tekrar dene. Yine olmazsa o satıra bir hotbar tuşu yaz: tuşlu satır tuşu kullanır.";
            Log("Skill kimlik sınaması: etki yok · " + castSkill);
        }
    }
}

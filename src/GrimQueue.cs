using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

// Dungeon queue: the player picks Solo Trial dungeons in the order they should run, and the bot walks to
// each entrance, enters, clears it, leaves it (the "Leave dungeon" button, else the Dungeon Exit NPC's dialog) and goes on with the next one.
// After the last dungeon the bot can read a Return Scroll to go back to town.

// The order in which the cities are cleared: first the Constantinople dungeons, then Samarkand, Hotan, Roc Mountain, Taklamakan, Donwhang and Jangan.
// The names are the ones of the "Area" column in data/grim-locations.json. Inside a city the dungeons go by code, and a dungeon's Solo Trial comes
// right before its Solo 85 (same entrance, so no walk in between).
static class GrimCityOrder
{
    static readonly string[] Cities = { "Constantinople", "Samarkand", "Hotan", "Roc Mountain", "Hotan - Taklamakan", "Downhang", "Jangan" };

    internal const string Route = "Constantinople → Samarkand → Hotan → Roc Mountain → Taklamakan → Donwhang → Jangan";

    // A city that is not in the list (a new one) runs last.
    internal static int Rank(string area)
    {
        int i = Array.IndexOf(Cities, area);
        return i < 0 ? Cities.Length : i;
    }

    internal static List<DungeonLocation> Sort(IEnumerable<DungeonLocation> dungeons)
    {
        return dungeons.OrderBy(d => Rank(d.Area)).ThenBy(d => d.Code, StringComparer.Ordinal).ThenBy(d => d.Mode == DungeonLocation.Solo85 ? 1 : 0)
            .ToList();
    }
}

sealed partial class MemorySelectV3Form
{
    readonly List<DungeonLocation> queue = new List<DungeonLocation>();
    readonly ListBox queueList = new ListBox();
    readonly CheckBox queueReturnScroll = new CheckBox();
    readonly Label queueInfo = new Label();
    bool queueRunning, queueAdvancing;
    int queueExitPhase, queueExitStep, queueExitTries;
    long queueExitSince;
    uint queueExitUid;
    int queueIndex;
    // Shown as "done + 1 / total": a finished dungeon leaves the list, so the list's own length is not the total.
    int queueDone, queueTotal;

    internal List<string> QueueOrder()
    {
        return queue.Select(d => d.QueueKey).ToList();
    }

    void BuildQueuePanel(Control host)
    {
        var panel = new Panel { Dock = DockStyle.Right, Width = 430, Padding = new Padding(14, 0, 0, 0) };
        var title = new Label { Text = "Çalıştırma sırası · Solo · baştan sona çalışır", Dock = DockStyle.Top,
            Height = 28, TextAlign = ContentAlignment.MiddleLeft, Font = Ui.Heading };
        queueInfo.Dock = DockStyle.Top;
        queueInfo.Height = 38;
        queueInfo.Font = Ui.Small;
        queueInfo.Tag = "muted";
        queueInfo.Text = QueueEmptyText;
        queueList.Dock = DockStyle.Fill;
        queueList.IntegralHeight = false;
        queueList.BorderStyle = BorderStyle.FixedSingle;
        queueList.HorizontalScrollbar = true;
        queueList.DoubleClick += delegate { Safe(RemoveSelectedFromQueue); };
        queueList.IntegralHeight = false;
        var side = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 226, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true, Padding = new Padding(10, 0, 0, 0) };
        side.Controls.Add(Btn("Ekle", AddSelectedToQueue, 98, true, ButtonKind.Primary));
        side.Controls.Add(Btn("Tümünü ekle", AddAllToQueue, 98, true, ButtonKind.Primary));
        side.Controls.Add(Btn("Çıkar", RemoveSelectedFromQueue, 98));
        side.Controls.Add(Btn("Temizle", ClearQueue, 98));
        side.Controls.Add(Btn("▲ Yukarı", delegate { MoveQueue(-1); }, 98));
        side.Controls.Add(Btn("▼ Aşağı", delegate { MoveQueue(1); }, 98));
        side.Controls.Add(Btn("Şehir sırası", SortQueueByCity, 98));
        side.Controls.Add(Btn("Girdi sıfırla", ResetVisited, 98));
        side.Controls.Add(Btn("Sırayı başlat", StartQueue, 204, true, ButtonKind.Success));
        queueReturnScroll.Text = "Bitince Return Scroll ile dön";
        queueReturnScroll.AutoSize = false;
        queueReturnScroll.Width = 204;
        queueReturnScroll.Height = 24;
        queueReturnScroll.CheckedChanged += delegate { SaveSettings(); };
        locked.Add(queueReturnScroll);
        side.Controls.Add(queueReturnScroll);
        panel.Controls.Add(queueList);
        panel.Controls.Add(side);
        panel.Controls.Add(queueInfo);
        panel.Controls.Add(title);
        host.Controls.Add(panel);        dungeonGrid.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex == 0)
                return;
            var location = dungeonGrid.Rows[e.RowIndex].Tag as DungeonLocation;
            if (location != null)
                Safe(delegate { ToggleQueue(location); });
        };
    }

    const string QueueEmptyText = "Sıra boş. Tabloda dungeon seç, Ekle'ye ya da Tümünü ekle'ye bas (çift tık da çalışır). Bitenler listeden silinir, tabloda sarı olur.";

    // The rows chosen in the table, top to bottom (the current row when nothing is selected).
    List<DungeonLocation> SelectedDungeons()
    {
        var rows = dungeonGrid.SelectedRows.Cast<DataGridViewRow>().OrderBy(r => r.Index).ToList();
        if (rows.Count == 0 && dungeonGrid.CurrentRow != null)
            rows.Add(dungeonGrid.CurrentRow);
        return rows.Select(r => r.Tag as DungeonLocation).Where(d => d != null).ToList();
    }

    // Adds dungeons to the end of the list; the ones already in it are skipped. Only Solo dungeons can be queued. Works while the queue runs too.
    int AddToQueue(IEnumerable<DungeonLocation> dungeons)
    {
        var solo = dungeons.Where(d => d.IsSolo).ToList();
        if (solo.Count == 0)
            throw new InvalidOperationException("Sıra yalnızca Solo içindir. Solo Trial ya da Solo 85'i işaretle, sonra dungeon seç.");
        int added = 0;
        foreach (var d in solo)
            if (queue.All(x => x.QueueKey != d.QueueKey))
            {
                queue.Add(d);
                added++;
                if (queueRunning)
                    queueTotal++;
            }
        QueueChanged();
        return added;
    }

    void AddSelectedToQueue()
    {
        var chosen = SelectedDungeons();
        if (chosen.Count == 0)
            throw new InvalidOperationException("Tablodan dungeon seç.");
        int added = AddToQueue(chosen);
        status.Text = added == 0 ? "Seçilenler zaten sırada." : added + " dungeon sıraya eklendi.";
    }

    // Every dungeon the table shows, except the ones that are already done (green): those would only run again.
    void AddAllToQueue()
    {
        var shown = dungeonGrid.Rows.Cast<DataGridViewRow>().Select(r => r.Tag as DungeonLocation).Where(d => d != null).ToList();
        if (shown.Count == 0)
            throw new InvalidOperationException("Tabloda dungeon yok. Solo Trial ya da Solo 85'i işaretle.");
        var todo = shown.Where(d => !d.IsSolo || !visitedDungeons.Contains(d.Code + "|" + d.Mode)).ToList();
        if (todo.Count == 0)
            throw new InvalidOperationException("Tablodaki hepsi sarı (girilmiş). Tekrar çalıştırmak için Girdileri sıfırla'ya bas.");
        // map by map: the table's own order (by code) would walk back and forth between the cities
        int added = AddToQueue(GrimCityOrder.Sort(todo));
        int skipped = shown.Count - todo.Count;
        status.Text = added + " dungeon sıraya şehir sırasıyla eklendi" + (skipped > 0 ? " · " + skipped + " sarı (girilmiş) eklenmedi." : ".");
    }

    // Puts the list in city order: Constantinople, Samarkand, Hotan, Roc Mountain, Taklamakan, Donwhang, Jangan. The dungeon that is running
    // (the first entry while the queue runs) stays where it is.
    void SortQueueByCity()
    {
        if (queue.Count < 2)
            throw new InvalidOperationException("Dizilecek en az iki dungeon gerekir.");
        int fixedCount = queueRunning ? 1 : 0;
        var rest = GrimCityOrder.Sort(queue.Skip(fixedCount));
        queue.RemoveRange(fixedCount, queue.Count - fixedCount);
        queue.AddRange(rest);
        QueueChanged();
        status.Text = "Sıra şehir sırasına dizildi: " + GrimCityOrder.Route;
    }

    // A double click in the table: adds the dungeon, or takes it out again when it is already in the list.
    void ToggleQueue(DungeonLocation d)
    {
        if (!d.IsSolo)
            throw new InvalidOperationException("Sıra yalnızca Solo içindir. Solo Trial ya da Solo 85'i işaretle.");
        var existing = queue.FirstOrDefault(x => x.QueueKey == d.QueueKey);
        if (existing == null)
        {
            AddToQueue(new[] { d });
            return;
        }
        RemoveFromQueue(queue.IndexOf(existing));
    }

    // The running dungeon (the first entry while the queue runs) cannot be taken out; every other entry can.
    void RemoveFromQueue(int index)
    {
        if (index < 0 || index >= queue.Count)
            throw new InvalidOperationException("Listeden çıkarılacak satırı seç.");
        if (queueRunning && index == 0)
            throw new InvalidOperationException("Çalışan dungeon çıkarılamaz. Önce durdur.");
        queue.RemoveAt(index);
        if (queueRunning)
            queueTotal--;
        QueueChanged();
    }

    void RemoveSelectedFromQueue()
    {
        RemoveFromQueue(queueList.SelectedIndex);
    }

    void MoveQueue(int delta)
    {
        int i = queueList.SelectedIndex, j = i + delta;
        if (i < 0 || j < 0 || j >= queue.Count)
            return;
        if (queueRunning && (i == 0 || j == 0))
            throw new InvalidOperationException("Çalışan dungeon yerinden oynatılamaz. Önce durdur.");
        var item = queue[i];
        queue[i] = queue[j];
        queue[j] = item;
        QueueChanged();
        queueList.SelectedIndex = j;
    }

    void ClearQueue()
    {
        if (queueRunning)
            throw new InvalidOperationException("Sıra çalışırken temizlenemez. Önce durdur.");
        queue.Clear();
        QueueChanged();
    }

    // Takes the yellow mark off every dungeon (the table's "Girdim" column), so they can be queued and run again.
    void ResetVisited()
    {
        foreach (DataGridViewRow row in dungeonGrid.Rows)
            row.Cells[0].Value = false;
        visitedDungeons.Clear();
        SaveSettings();
        RefreshDungeonDetails();
        status.Text = "Girdi işaretleri sıfırlandı.";
    }

    void RestoreQueue()
    {
        queue.Clear();
        foreach (string code in settings.DungeonQueue ?? new List<string>())
        {
            var d = V3Data.Dungeons.FirstOrDefault(x => x.IsSolo && x.QueueKey == code);
            if (d != null && queue.All(q => q.QueueKey != code))
                queue.Add(d);
        }
        queueReturnScroll.Checked = settings.QueueReturnScroll;
        UpdateQueueUi();
    }

    void QueueChanged()
    {
        UpdateQueueUi();
        SaveSettings();
    }

    void UpdateQueueUi()
    {
        int selected = queueList.SelectedIndex;
        queueList.BeginUpdate();
        queueList.Items.Clear();
        for (int i = 0; i < queue.Count; i++)
            queueList.Items.Add((queueRunning && i == 0 ? "▶ " : "   ") + (i + 1) + " · " + queue[i].QueueKey +
                " · " + queue[i].Name);
        queueList.EndUpdate();
        if (selected >= 0 && selected < queueList.Items.Count)
            queueList.SelectedIndex = selected;
        if (queue.Count == 0)
            queueInfo.Text = QueueEmptyText;
        else if (queueRunning)
            queueInfo.Text = "Çalışıyor " + (queueDone + 1) + "/" + queueTotal + " · kalan: " + string.Join(" → ", queue.Select(d =>
                d.QueueKey));
        else
            queueInfo.Text = "Hazır: " + string.Join(" → ", queue.Select(d => d.QueueKey)) +
                " · Sırayı dungeon dışından başlat.";
        RefreshDungeonDetails();
    }

    void SelectDungeonRow(DungeonLocation d)
    {
        foreach (DataGridViewRow row in dungeonGrid.Rows)
        {
            var location = row.Tag as DungeonLocation;
            if (location != null && location.Code == d.Code && location.Mode == d.Mode)
            {
                dungeonGrid.CurrentCell = row.Cells[1];
                return;
            }
        }
    }

    void StartQueue()
    {
        RequireReader();
        if (queue.Count == 0)
            throw new InvalidOperationException("Sıra boş. Önce dungeon ekle.");
        if (!autoEnterGrim.Checked)
        {
            autoEnterGrim.Checked = true;
            Log("Sıra için otomatik giriş açıldı.");
        }
        snapshot = reader.Capture();
        if (V3Data.IsCrypt(snapshot.Player))
            throw new InvalidOperationException("Sırayı dungeon dışından başlat.");
        queueRunning = true;
        queueIndex = 0;
        queueDone = 0;
        queueTotal = queue.Count;
        Log("Sıra başladı: " + string.Join(" → ", queue.Select(d => d.QueueKey)));
        BeginQueueEntry();
    }

    void BeginQueueEntry()
    {
        var d = queue[queueIndex];
        queueExitPhase = 0;
        queueExitSince = 0;
        queueExitUid = 0;
        queueExitTries = 0;
        queueExitStep = 0;
        SelectDungeonRow(d);
        queueAdvancing = true;
        try
        {
            WalkDungeon(d);
        }
        catch (Exception)
        {
            queueRunning = false;
            throw;
        }
        finally
        {
            queueAdvancing = false;
            UpdateQueueUi();
        }
        Log("Sıra " + (queueDone + 1) + "/" + queueTotal + " · " + d);
    }

    void MarkVisited(DungeonLocation d)
    {
        foreach (DataGridViewRow row in dungeonGrid.Rows)
        {
            var location = row.Tag as DungeonLocation;
            if (location != null && location.Code == d.Code && location.Mode == d.Mode)
            {
                row.Cells[0].Value = true;
                return;
            }
        }
        visitedDungeons.Add(d.Code + "|" + d.Mode);
        SaveSettingsQuietly();
    }

    bool QueueUseReturnScroll()
    {
        var item = snapshot.Items.Where(i => ExpeditionRules.ReturnIds.Contains(i.Model) && i.Quantity > 0 && i.Type != 0 &&
            i.Slot >= 13).OrderBy(i => i.Slot).FirstOrDefault();
        if (item == null || !reader.ValidateItem(item))
        {
            Log("Sıra: kullanılabilir Return Scroll yok.");
            return false;
        }
        if (!sender.UseReturn(item))
        {
            Log("Sıra: Return Scroll isteği reddedildi.");
            return false;
        }
        Log("Sıra: Return Scroll ile şehre dönüş istendi · ID " + item.Model);
        return true;
    }

    // Called every tick while the bot is inside a Grim run. Returns true when the queue handled this tick.

    bool QueueAfterComplete(long now)
    {
        if (!queueRunning || queueAdvancing || queueIndex >= queue.Count)
            return false;
        var d = queue[queueIndex];
        if (grim.Location == null || grim.Location.Code != d.Code || !grim.Complete)
            return false;
        if (!grim.IsRegion(snapshot.Player))
        {
            // The dungeon is finished and the character is back outside: it is marked as done (green in the table), taken out of the list, and the
            // next entry starts. What is left in the list is what still has to run, also after a restart.
            MarkVisited(d);
            queueDone++;
            Log("Sıra " + queueDone + "/" + queueTotal + " bitti · " + d.Code + " · listeden silindi");
            queue.RemoveAt(queueIndex);
            QueueChanged();
            if (queue.Count == 0)
            {
                queueRunning = false;
                UpdateQueueUi();
                if (queueReturnScroll.Checked)
                    QueueUseReturnScroll();
                Stop("Sıra tamamlandı: " + queueDone + " dungeon.");
                return true;
            }
            try
            {
                BeginQueueEntry();
            }
            catch (Exception e)
            {
                queueRunning = false;
                Stop(e.Message);
            }
            return true;
        }
        // The dungeon is cleared: CompletionExit walks to the Dungeon Exit NPC and holds there (exitHolding); only then the dialog is used,
        // because the NPC has to be close (it is read from the surroundings, and the dialog opens by talking to it).
        if (!exitHolding)
            return false;
        // A treasure box that appeared late (or one not broken yet): the exit waits until it is dead. Once the exit click is sent there is no going back.
        if (queueExitPhase != 2 && snapshot.Monsters.Any(m => m.Alive && m.Health != 0 && grim.IsRegion(m.Position) && GrimRooms.IsBox(m)))
        {
            exitHolding = false;
            queueExitPhase = 0;
            queueExitStep = 0;
            queueExitSince = 0;
            completeIdleSince = 0;
            Log("Sıra: Grim kutusu bulundu · çıkıştan önce kırılacak.");
            return false;
        }
        return QueueExit(now);
    }

    // A finished dungeon is left the way a player leaves it: select the Dungeon Exit NPC, open its dialog (the talk request), click the first line
    // "1. Exit Dungeon.". All three steps were tried live: the dialog opens by itself and the click took the character out of the dungeon at once.
    // (The red "Leave dungeon" button of the Grim Crypt panel did not react to a click, so it is not used.) The click is repeated up to three times;
    // the line's position is the distance from the middle of the drawing area, measured in a 1366 x 768 window. When it does not work the bot says
    // so and waits for a manual exit; the next dungeon starts as soon as the character is outside.
    static readonly Point ExitDialogLine = new Point(-505, -111);
    const int ExitTriesMax = 3;

    bool QueueExit(long now)
    {
        return QueueExit(now, "Sıra " + (queueDone + 1) + "/" + queueTotal + " bitti · ");
    }

    bool QueueExit(long now, string waiting)
    {
        if (queueExitPhase == 3)
        {
            status.Text = waiting + "çıkış otomatik yapılamadı; Dungeon Exit penceresinde \"Exit Dungeon\"a elle bas, çıkınca sıradaki otomatik başlar.";
            return true;
        }
        if (queueExitSince == 0)
            queueExitSince = now;
        if (queueExitPhase == 2)
        {
            // a click was sent; leaving moves the character out of the dungeon's region, which ends the wait in QueueAfterComplete
            status.Text = waiting + "çıkış bekleniyor.";
            if (now - queueExitSince > 8000)
            {
                if (queueExitTries >= ExitTriesMax)
                {
                    Log("Sıra: çıkış " + queueExitTries + " denemede de olmadı; elle çıkış bekleniyor.");
                    queueExitPhase = 3;
                }
                else
                {
                    queueExitPhase = 0;
                    queueExitStep = 0;
                    queueExitSince = now;
                }
            }
            return true;
        }
        // a short wait for the loot before the first try
        if (queueExitTries == 0 && queueExitStep == 0 && now - queueExitSince < 2500)
        {
            status.Text = waiting + "çıkışa hazırlanıyor.";
            return true;
        }
        IntPtr window = ClientWindow();
        if (window == IntPtr.Zero)
        {
            status.Text = waiting + "oyun penceresi bulunamadı.";
            if (now - queueExitSince > 15000)
                queueExitPhase = 3;
            return true;
        }
        if (!reader.IsForeground() || reader.IsMinimized())
        {
            ForceForeground(window);
            status.Text = waiting + "oyun öne alınıyor.";
            if (now - queueExitSince > 15000)
            {
                Log("Sıra: oyun öne alınamadı; elle çıkış bekleniyor.");
                queueExitPhase = 3;
            }
            return true;
        }
        return QueueExitByDialog(window, now, waiting);
    }

    bool QueueExitClick(IntPtr window, Point offset)
    {
        Point before = LotteryInput.Cursor();
        bool clicked = LotteryInput.Click(window, offset);
        LotteryInput.MoveTo(before);
        return clicked;
    }

    // Select the exit NPC, open its dialog, click "1. Exit Dungeon.".
    bool QueueExitByDialog(IntPtr window, long now, string waiting)
    {
        status.Text = waiting + "Dungeon Exit penceresi kullanılıyor.";
        if (queueExitStep == 0)
        {
            var npc = snapshot.Npcs.Where(n => n.Model == 36031 || n.Model == 36032).OrderBy(n => n.Position.Distance(snapshot.Player))
                .FirstOrDefault();
            if (npc == null || npc.Position.Distance(snapshot.Player) > 40 || !reader.ValidateNpc(npc))
            {
                if (now - queueExitSince > 10000)
                {
                    Log("Sıra: Dungeon Exit NPC'si okunamadı; elle çıkış bekleniyor.");
                    queueExitPhase = 3;
                }
                return true;
            }
            if (!sender.Send(npc.Uid))
            {
                Log("Sıra: Dungeon Exit seçimi reddedildi; elle çıkış bekleniyor.");
                queueExitPhase = 3;
                return true;
            }
            queueExitUid = npc.Uid;
            queueExitStep = 1;
            queueExitSince = now;
            return true;
        }
        if (queueExitStep == 1)
        {
            if (snapshot.Selected != queueExitUid)
            {
                if (now - queueExitSince > 5000)
                {
                    Log("Sıra: Dungeon Exit seçimi doğrulanamadı; elle çıkış bekleniyor.");
                    queueExitPhase = 3;
                }
                return true;
            }
            if (now - queueExitSince < 700)
                return true;
            try
            {
                sender.Talk(queueExitUid, 1);
            }
            catch (Exception e)
            {
                Log("Sıra: Dungeon Exit konuşması gönderilemedi · " + e.Message);
            }
            queueExitStep = 2;
            queueExitSince = now;
            return true;
        }
        // step 2: the dialog has had time to open; click its first line
        if (now - queueExitSince < 1500)
            return true;
        if (!QueueExitClick(window, ExitDialogLine))
        {
            Log("Sıra: Dungeon Exit penceresine tıklanamadı; elle çıkış bekleniyor.");
            queueExitPhase = 3;
            return true;
        }
        queueExitTries++;
        Log("Sıra: \"Exit Dungeon\" satırı tıklandı (" + queueExitTries + "/" + ExitTriesMax + ")");
        queueExitSince = now;
        queueExitPhase = 2;
        return true;
    }
}

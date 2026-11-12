using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

sealed partial class MemorySelectV3Form : Form
{
    readonly TargetWatch approachWatch = new TargetWatch();
    Position movementPoint, previousMovementPoint;
    readonly ObstacleWalker routeWalker = new ObstacleWalker();
    uint routeTarget;
    long nextVisibility;
    bool grimHolding, grimWaiting;
    // Grim walking that finds no route does not stop the run: it falls back to the checked floor cells, then skips the point.
    List<Position> graphPath;
    int graphIndex, grimFailures;
    long grimRetryAt;
    bool targetWalkable;
    readonly Dictionary<uint, string> names = Catalog.Load();
    readonly ComboBox processes = new ComboBox(), monsters = new ComboBox(), berserkClass = new ComboBox();
    readonly NumericUpDown xBox = new NumericUpDown(), yBox = new NumericUpDown(), radius = new NumericUpDown(), delay =
        new NumericUpDown(), timeout = new NumericUpDown();
    readonly Label position = new Label(), area = new Label(), status = new Label(), maintenance = new Label(), details =
        new Label();
    readonly DataGridView grid = new DataGridView();
    readonly TextBox keys = new TextBox();
    readonly CheckBox sendKeys = new CheckBox(), autoZerk = new CheckBox(), autoSpeed = new CheckBox(), autoRepair = new CheckBox(),
        goHome = new CheckBox();
    readonly List<Control> locked = new List<Control>();
    readonly Timer poll = new Timer(), release = new Timer();
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly TargetWatch watch = new TargetWatch();
    readonly ItemTransaction transaction = new ItemTransaction();
    readonly Dictionary<uint, long> banned = new Dictionary<uint, long>();
    GameReader reader;
    TargetSender sender;
    Snapshot snapshot;
    Position center;
    bool active, zerkLatched, returning;
    ushort heldScan;
    ushort[] parsedKeys = new ushort[0];
    int keyIndex, attempts;
    uint wanted, lastRequest;
    long nextRequest, nextKey, nextMove, returnProgressAt, lastTick;
    string lastEvent = "";

    sealed class ProcessItem
    {
        internal int Id;
        internal string Name;

        public override string ToString()
        {
            return Name;
        }
    }

    delegate bool ClientWindowCallback(IntPtr window, IntPtr state);
    [DllImport("user32.dll")]
    static extern bool EnumWindows(ClientWindowCallback callback, IntPtr state);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowText(IntPtr window, StringBuilder title, int capacity);
    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr window);

    static IntPtr FindClientWindow(Process process)
    {
        IntPtr mainWindow = process.MainWindowHandle;
        if (mainWindow != IntPtr.Zero)
            return mainWindow;
        IntPtr hiddenWindow = IntPtr.Zero;
        var title = new StringBuilder(128);
        EnumWindows(delegate(IntPtr window, IntPtr state)
        {
            uint owner;
            Native.GetWindowThreadProcessId(window, out owner);
            if (owner != (uint)process.Id)
                return true;
            title.Clear();
            GetWindowText(window, title, title.Capacity);
            if (!string.Equals(title.ToString(), "SRO_Client", StringComparison.OrdinalIgnoreCase))
                return true;
            hiddenWindow = window;
            return false;
        }, IntPtr.Zero);
        return hiddenWindow;
    }

    void SetClientVisibility(bool show)
    {
        var selected = processes.SelectedItem as ProcessItem;
        if (selected == null)
            throw new InvalidOperationException("Listeden bir oyun işlemi seç.");
        using (var process = Process.GetProcessById(selected.Id))
        {
            if (!string.Equals(process.ProcessName, "sro_client", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Seçili oyun işlemi artık açık değil.");
            IntPtr window = FindClientWindow(process);
            if (window == IntPtr.Zero)
                throw new InvalidOperationException("Seçili oyun penceresi bulunamadı.");
            if (IsWindowVisible(window) != show && !ShowWindowAsync(window, show ? 5 : 0))
                throw new InvalidOperationException("Windows pencere isteğini kabul etmedi.");
            if (!show && reader != null && reader.Process.Id == selected.Id)
                ReleaseKey();
            status.Text = (show ? "Client gösterme isteği gönderildi" : "Client gizleme isteği gönderildi") + " · PID " +
                selected.Id;
        }
    }

    void RefreshProcesses()
    {
        var previous = processes.SelectedItem as ProcessItem;
        processes.Items.Clear();
        foreach (var p in Process.GetProcessesByName("sro_client"))
        {
            try
            {
                processes.Items.Add(new ProcessItem { Id = p.Id, Name = p.ProcessName + ".exe | PID " + p.Id + " | " + (p.MainWindowHandle == IntPtr.Zero &&
                    FindClientWindow(p) != IntPtr.Zero ? "SRO_Client (gizli)" : p.MainWindowTitle) });
            }
            finally
            {
                p.Dispose();
            }
        }
        if (previous != null)
            processes.SelectedItem = processes.Items.Cast<ProcessItem>().FirstOrDefault(item => item.Id == previous.Id);
        if (processes.Items.Count == 1)
            processes.SelectedIndex = 0;
    }

    void Connect()
    {
        if (active)
            return;
        characterStats.Reset();
        if (transaction.Pending)
            throw new InvalidOperationException("Bekleyen eşya işlemi sonuçlanana kadar hedef değiştirilemez.");
        Stop("Bağlanıyor...");
        if (reader != null)
        {
            reader.Dispose();
            reader = null;
        }
        center = null;
        area.Text = "Bölge set edilmedi. Mevcut X/Y → Bölgeyi set et.";
        var p = processes.SelectedItem as ProcessItem;
        if (p == null)
            throw new InvalidOperationException("Bir oyun işlemi seç.");
        ClaimClient(p.Id);
        reader = new GameReader(p.Id, names);
        snapshot = reader.Capture();
        FillBoxes(snapshot.Player);
        UpdateDisplay();
        status.Text = "Bağlı. Yaratığı seç, ardından bölgeyi set et.";
    }

    void FillBoxes(Position p)
    {
        xBox.Value = Math.Max(xBox.Minimum, Math.Min(xBox.Maximum, (decimal)p.X));
        yBox.Value = Math.Max(yBox.Minimum, Math.Min(yBox.Maximum, (decimal)p.Y));
    }

    void FillCurrent()
    {
        RequireReader();
        snapshot = reader.Capture();
        FillBoxes(snapshot.Player);
        status.Text = "X/Y alındı; kaydetmek için Bölgeyi set et.";
    }

    void SetCenter()
    {
        RequireReader();
        snapshot = reader.Capture();
        if (!snapshot.Player.Outside)
            throw new InvalidOperationException("Kapalı harita desteklenmiyor.");
        center = V2Rules.Center((double)xBox.Value, (double)yBox.Value, snapshot.Player.Height);
        banned.Clear();
        area.Text = string.Format("SET · X {0:F1}  Y {1:F1} · Sabit merkez · Range {2}", center.X, center.Y, radius.Value);
        UpdateDisplay();
        status.Text = "Bölge kaydedildi.";
    }

    void RequireReader()
    {
        if (reader == null)
            throw new InvalidOperationException("Önce Bağlan / Oku.");
    }

    void Nearby()
    {
        RequireReader();
        snapshot = reader.Capture();
        string old = monsters.SelectedItem as string;
        monsters.Items.Clear();
        monsters.Items.AddRange(snapshot.Monsters.Select(m => m.Name).Distinct().OrderBy(n => n).Cast<object>().ToArray());
        if (old != null && monsters.Items.Contains(old))
            monsters.SelectedItem = old;
        if (monsters.Items.Count == 1)
            monsters.SelectedIndex = 0;
        status.Text = "Yakındaki isimler listelendi. Bölge merkezi değişmedi.";
    }

    void Start()
    {
        if (active)
            return;
        RequireReader();
        if (center == null || selectedMobs.Count == 0)
            throw new InvalidOperationException("+ ile en az bir yaratık ekle ve Bölgeyi set et.");
        grim.Stop();
        BeginOperation(OperationMode.Hunt);
    }

    internal const int IdlePollMs = 250, ActivePollMs = 100;

    void BeginOperation(OperationMode mode)
    {
        if (active)
            return;
        if (dualTimer.Enabled)
            throw new InvalidOperationException("Önce çift hesap pencere geçişini durdur.");
        RequireReader();
        if (transaction.Pending)
            throw new InvalidOperationException("Bekleyen eşya işlemi var.");
        operation = mode;
        if (recovery.Deaths >= RunDeathLimit)
            throw new InvalidOperationException("Ölüm sınırı dolu. İnceledikten sonra ölüm sayacını sıfırla.");
        snapshot = reader.Capture();
        if (!snapshot.PlayerAlive)
            throw new InvalidOperationException("Karakter canlı değil.");
        if (!snapshot.Player.Outside)
            throw new InvalidOperationException("Bu kapalı koordinat türü desteklenmiyor.");
        if (mode == OperationMode.Hunt && (V3Data.IsCrypt(snapshot.Player) || V3Data.IsCrypt(center)))
            throw new InvalidOperationException("Bu alan için Grim Crypt sekmesini kullan.");
        var list = new List<ushort>();
        useRotation = skillListBox.Checked;
        if (useRotation)
        {
            var slots = SkillSlotsFromGrid();
            string problem = SkillRotation.Validate(slots);
            if (problem != null)
                throw new InvalidOperationException(problem);
            rotation.Load(slots, SkillOptionsFromUi());
            list.AddRange(rotation.Keys());
            if (mode != OperationMode.Travel && RunSendKeys && !rotation.HasAttack && !(rotation.Options.BuffOnly && mode == OperationMode.Hunt))
                throw new InvalidOperationException("Skill listesinde en az bir açık Saldırı skill'i olmalı.");
        }
        else
        {
            foreach (string t in RunKeys.ToUpperInvariant().Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (t.Length != 1 || !((t[0] >= '0' && t[0] <= '9') || (t[0] >= 'A' && t[0] <= 'Z')))
                    throw new InvalidOperationException("Tuşlar: A-Z, 0-9; boşlukla ayır.");
                list.Add(t[0]);
            }
            if (mode != OperationMode.Travel && RunSendKeys && list.Count == 0)
                throw new InvalidOperationException("Saldırı tuşu seç.");
        }
        if (mode == OperationMode.Grim && !RunSendKeys)
            throw new InvalidOperationException("Av / Ayarlar içindeki Hedef doğrulanınca tuşlara bas seçeneğini aç.");
        if (HotkeyPolicy.Conflicts(shortcutKey, shortcutMods, list, RunZerk))
            throw new InvalidOperationException("Başlat/durdur kısayolu saldırı veya Berserk tuşuyla aynı. Kısayolu değiştir.");
        SaveSettings();
        routeWalker.Reset();
        routeTarget = 0;
        approachWatch.Reset();
        movementPoint = previousMovementPoint = null;
        nextVisibility = 0;
        parsedKeys = list.ToArray();
        sender = new TargetSender(reader);
        active = true;
        // While a job runs the window looks at the game four times a second faster: a quicker reaction, and the route search (a few steps per
        // look) finishes sooner.
        poll.Interval = ActivePollMs;
        operation = mode;
        wanted = lastRequest = 0;
        attempts = keyIndex = 0;
        nextRequest = nextKey = nextMove = 0;
        returning = false;
        grimHolding = grimWaiting = false;
        watch.Reset();
        zerkLatched = false;
        recovery.Begin();
        banned.Clear();
        foreach (var c in locked)
            c.Enabled = false;
        status.Text = "Başladı. Oyuna geç.";
        Log("Başlatıldı: " + mode);
    }

    void Stop(string text)
    {
        if (queueRunning && !queueAdvancing)
        {
            queueRunning = false;
            UpdateQueueUi();
            Log("Sıra durduruldu · " + text);
        }
        bool wasReturning = active && snapshot != null && snapshot.PlayerAlive && recovery.Phase == RecoveryPhase.Ready &&
            (returning || operation == OperationMode.Travel || operation == OperationMode.Grim);
        routeWalker.Reset();
        graphPath = null;
        grimFailures = 0;
        grimRetryAt = 0;
        caravanWalk = false;
        if (explorerRun != null && !explorerAdvancing)
        {
            explorerRun = null;
            RefreshExplorerUi();
        }
        grim.Stop();
        recovery.Stop();
        active = false;
        poll.Interval = IdlePollMs;
        ReleaseKey();
        if (sender != null)
        {
            if (wasReturning && reader != null && snapshot != null)
            {
                try
                {
                    if (CommandsAllowed())
                        sender.Move(snapshot.Player.Copy());
                }
                catch { }
            }
            sender.Dispose();
            sender = null;
        }
        wanted = 0;
        returning = false;
        watch.Reset();
        foreach (var c in locked)
            c.Enabled = true;
        completionNotice.Dispose();
        status.Text = text;
        Log(text);
        try
        {
            SaveSettings();
        }
        catch (Exception e)
        {
            status.Text += " Ayarlar kaydedilemedi: " + e.Message;
        }
    }

    void UpdateDisplay()
    {
        if (snapshot == null)
            return;
        deathsLabel.Text = "Ölüm sayısı: " + recovery.Deaths + " / " + deathLimit.Value + " · " + recovery.Phase;
        int count = center == null ? 0 : V2Rules.InRange(snapshot, center, (double)radius.Value).Count;
        position.Text = string.Format("Karakter X {0:F1}  Y {1:F1} | Yakındaki mob {2} | Seçili UID {3}", snapshot.Player.X,
            snapshot.Player.Y, snapshot.Monsters.Count, snapshot.Selected);
        details.Text = string.Format("Zerk {0}/5 · Speed {1} · En düşük dayanıklılık {2} · Drug {3} · Hammer {4} · Reverse {5}",
            snapshot.Berserk, snapshot.SpeedKnown ? (snapshot.SpeedActive ? "aktif" : "yok") : "okunamadı", snapshot.MinDurability.HasValue ?
            snapshot.MinDurability.Value.ToString() : "—", snapshot.Items.Where(i => i.Model == 7100 || i.Model == 63361)
            .Sum(i => (long)i.Quantity), snapshot.Items.Where(i => i.Model == 3784 || i.Model == 24458).Sum(i => (long)i.Quantity),
            snapshot.Items.Where(RecoveryRules.IsReverse).Sum(i => (long)i.Quantity));
        grid.Rows.Clear();
        if (center == null)
            return;
        long now = clock.ElapsedMilliseconds;
        foreach (var m in V2Rules.InRange(snapshot, center, (double)radius.Value).OrderBy(m => center == null ? 0 : m.Position.Distance(center)))
        {
            long until;
            bool skip = banned.TryGetValue(m.Uid, out until) && until > now;
            grid.Rows.Add(m.Uid, m.Name, V2Rules.ClassName(m), m.Position.X.ToString("F1"), m.Position.Y.ToString("F1"),
                (center == null ? "—" : m.Position.Distance(center).ToString("F1")), m.Health.HasValue ? m.Health.ToString() :
                "—", !m.Alive ? "Ölü" : skip ? "Geçici atlandı" : "Bölge içinde");
        }
    }

    void Skip(uint uid, string reason, long now)
    {
        banned[uid] = now + 60000;
        wanted = 0;
        attempts = 0;
        lastRequest = 0;
        nextRequest = 0;
        watch.Reset();
        ReleaseKey();
        lastEvent = reason + " · UID " + uid + " 60 sn atlandı.";
        status.Text = lastEvent;
        Log(lastEvent);
    }

    bool ProcessMaintenance(long now)
    {
        transaction.Observe(snapshot, now);
        maintenance.Text = transaction.Message;
        if (transaction.Pending)
        {
            ReleaseKey();
            status.Text = "Eşya işleminin sonucu bekleniyor.";
            return true;
        }
        string kind = null;
        BagItem item = null;
        if (RunRepair && !transaction.RepairPaused && V2Rules.NeedsRepair(snapshot))
        {
            kind = "repair";
            item = V2Rules.PickItem(snapshot.Items, true);
        }
        else if (RunSpeed && !transaction.SpeedPaused && snapshot.InventoryKnown && snapshot.SpeedKnown && !snapshot.SpeedActive)
        {
            kind = "speed";
            item = V2Rules.PickItem(snapshot.Items, false);
        }
        if (item == null)
            return false;
        if (!CommandsAllowed() || !reader.ValidateItem(item))
            return true;
        ReleaseKey();
        transaction.Begin(kind, item, snapshot, now);
        if (!sender.Use(item))
        {
            transaction.Kind = null;
            transaction.Rejected(kind);
        }
        maintenance.Text = transaction.Message;
        watch.Pause(10000);
        return true;
    }

    // Commands may be sent while the game is in front. Walking jobs (travel, Explorer, caravan) send no keys, only the client's own move,
    // select and transfer calls, so they may also run while the game is behind other windows, unless it is minimised.
    bool CommandsAllowed()
    {
        if (reader == null)
            return false;
        if (reader.IsForeground())
            return true;
        if (reader.IsMinimized())
            return false;
        if (operation == OperationMode.Travel)
            return backgroundWalk.Checked;
        // Lines that use a skill by its id send no key at all, so they work with the game behind other windows.
        return backgroundKeys.Checked || (active && useRotation && rotation.HasIdOnly);
    }

    // Presses the key of a skill line or, for a line with a Skill ID and no key, tells the client to use that skill on the target
    // (attack lines) or on the character itself (buffs, heals, mana).
    bool PressSkill(ushort key, uint target)
    {
        if (key != SkillRotation.IdOnly)
            return Press(key);
        int id = rotation.LastSkillId;
        if (sender == null || id <= 0 || !CommandsAllowed())
            return false;
        try
        {
            if (sender.Cast((uint)id, rotation.LastKind == SkillRotation.Attack_ ? target : 0))
                return true;
            Log("Skill " + SkillLabel(id) + " gönderilemedi (istemci reddetti).");
        }
        catch (IOException e)
        {
            Log("Skill " + SkillLabel(id) + " gönderilemedi · " + e.Message);
        }
        return false;
    }

    static string SkillLabel(int id)
    {
        string name = SkillCatalog.NameOf((uint)id);
        return name.Length == 0 ? id.ToString() : name + " (" + id + ")";
    }

    bool WalkChecked(Position destination, long now, Func<Position, bool> allowed, string label, bool skipOnFailure)
    {
        ReleaseKey();
        if (!CommandsAllowed())
            return false;
        if (operation == OperationMode.Travel && travelMount.Checked && !caravanWalk && !EnsureTravelMount(true, now))
            return false;
        var activeSender = sender;
        routeWalker.Prefetch = activeSender == null ? null : (Func<List<Position[]>, sbyte[]>)delegate(List<Position[]> pairs)
        {
            try
            {
                return activeSender.NavigationProbeMany(pairs);
            }
            catch (IOException)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        };
        routeWalker.GridStep = operation == OperationMode.Travel || (operation == OperationMode.Grim && grim.Rooms != null) ?
            4 : 8;
        var result = routeWalker.Tick(snapshot.Player, destination, now, ClimbSafeProbe(sender, operation == OperationMode.Travel),
            p =>
        {
            bool accepted = sender.Move(p);
            Log("Hareket · X " + p.X.ToString("F1") + " Y " + p.Y.ToString("F1") + " kabul=" + accepted);
            return accepted;
        }, allowed, operation == OperationMode.Travel ? ExpeditionRules.ArrivalDistance : 3);
        status.Text = label + " · " + routeWalker.Message;
        if (result == RouteResult.Failed)
        {
            string reason = routeWalker.Message;
            routeWalker.Reset();
            if (operation == OperationMode.Grim && !skipOnFailure && GrimWalkFailed(reason, destination, now))
                return false;
            if (TryReplanRoad(reason))
                return false;
            if (skipOnFailure && wanted != 0)
            {
                if (CommandsAllowed())
                    sender.Move(snapshot.Player.Copy());
                Skip(wanted, reason, now);
                routeTarget = 0;
            }
            else
                Stop(reason);
        }
        return result == RouteResult.Arrived;
    }

    void ReturnHome(long now)
    {
        ReleaseKey();
        if (snapshot.Player.Distance(center) <= 2.5)
        {
            returning = false;
            routeWalker.Reset();
            status.Text = "Merkezde; uygun hedef bekleniyor.";
            return;
        }
        if (!goHome.Checked)
        {
            if (routeWalker.Active)
            {
                routeWalker.Reset();
                if (CommandsAllowed())
                    sender.Move(snapshot.Player.Copy());
            }
            status.Text = "Uygun hedef yok; merkeze dönüş kapalı.";
            return;
        }
        if (!returning)
        {
            routeWalker.Reset();
            returning = true;
        }
        if (WalkChecked(center, now, null, "Merkeze rota", false))
            returning = false;
    }

    void Tick()
    {
        long now = clock.ElapsedMilliseconds, elapsed = lastTick == 0 ? 0 : now - lastTick;
        lastTick = now;
        if (reader == null)
        {
            if (statPanel.Sample != null)
            {
                statPanel.Sample = null;
                statPanel.Invalidate();
            }
            return;
        }
        try
        {
            try
            {
                snapshot = reader.Capture();
            }
            catch (IOException e)
            {
                ReleaseKey();
                reader.Process.Refresh();
                if (reader.Process.HasExited)
                {
                    Stop("Oyun kapandı.");
                    return;
                }
                if (readErrorSince == 0)
                    readErrorSince = now;
                if (active && now - readErrorSince < 90000)
                {
                    watch.Pause(elapsed);
                    approachWatch.Pause(elapsed);
                    travel.Pause(elapsed);
                    routeWalker.Pause(elapsed);
                    if (returning)
                        returnProgressAt += elapsed;
                    status.Text = "Dünya yükleniyor / bellek yeniden okunuyor: " + e.Message;
                    return;
                }
                Stop(e.Message);
                return;
            }
            if (active && snapshot.PlayerAlive)
            {
                if (movementPoint == null || movementPoint.Distance(snapshot.Player) > 96)
                {
                    movementPoint = snapshot.Player.Copy();
                    previousMovementPoint = null;
                }
                else if (movementPoint.Distance(snapshot.Player) > 2)
                {
                    previousMovementPoint = movementPoint;
                    movementPoint = snapshot.Player.Copy();
                }
            }
            readErrorSince = 0;
            CheckUniques();
            transaction.Observe(snapshot, now);
            UpdateStatPanel(now);
            maintenance.Text = transaction.Message;
            UpdateDisplay();
            dungeonRadar.Snapshot = snapshot;
            dungeonRadar.Run = grim;
            dungeonRadar.Invalidate();
            RecordPath();
            if (explorerMap.Visible)
            {
                explorerMap.Player = snapshot.Player;
                explorerMap.Invalidate();
            }
            if (worldMap.Visible)
            {
                worldMap.Player = snapshot.Player;
                worldMap.Invalidate();
                RefreshRoutes();
            }
            SkillTick(now);
            if (grim.Running && grim.Location != null && ExpeditionRules.Cleared(completionNotice.Message, grim.Location.Code))
                snapshot.Grim.Text = completionNotice.Message;
            bool began = (!active || (snapshot.PlayerAlive && recovery.Phase == RecoveryPhase.Ready)) && grim.Observe(snapshot);
            grimStatus.Text = grim.Message + "\r\nHUD: " + snapshot.Grim.Title + " · " + snapshot.Grim.Text;
            if (began && !active)
            {
                BeginOperation(OperationMode.Grim);
                Log("Dungeon algılandı: " + grim.Location);
            }
            if (!active)
                return;
            bool foreground = CommandsAllowed();
            int before = recovery.Deaths;
            RecoveryAction recoveryAction = recovery.Observe(snapshot, now, foreground, RunRecovery, RunWait,
                RunDeathLimit);
            if (before != recovery.Deaths)
            {
                routeWalker.Reset();
                ReleaseKey();
                wanted = 0;
                watch.Reset();
                SaveSettings();
                Log("Ölüm " + recovery.Deaths + " · X " + snapshot.Player.X + " Y " + snapshot.Player.Y);
            }
            if (recoveryAction == RecoveryAction.Stop)
            {
                Stop(recovery.Message);
                return;
            }
            if (recoveryAction == RecoveryAction.ReturnTown)
            {
                ReleaseKey();
                if (!CommandsAllowed())
                    return;
                if (!sender.ReturnTown())
                {
                    Stop("Şehre dönüş isteği reddedildi; tekrar gönderilmedi.");
                    return;
                }
                Log("Şehre dönüş isteği gönderildi.");
            }
            if (recoveryAction == RecoveryAction.Reverse)
            {
                ReleaseKey();
                BagItem reverse = RecoveryRules.PickReverse(snapshot);
                if (reverse == null || !CommandsAllowed() || !reader.ValidateItem(reverse))
                {
                    Stop("Reverse eşyası / ön pencere doğrulanamadı.");
                    return;
                }
                if (!sender.ReverseToDeath(reverse))
                {
                    Stop("Reverse isteği reddedildi; tekrar eşya kullanılmadı.");
                    return;
                }
                Log("Reverse ölüm noktası: eşya " + reverse.Model);
            }
            if (recoveryAction == RecoveryAction.Resume)
            {
                routeWalker.Reset();
                wanted = lastRequest = 0;
                watch.Reset();
                returning = false;
                if (travel.Destination != null)
                    travel.Set(travel.Destination, snapshot.Player, now);
                nextMove = nextKey = now + 1000;
                if (useRotation && rotation.Options.Rearm)
                    rotation.Rearm();
                Log(recovery.Message);
            }
            if (recovery.Phase != RecoveryPhase.Ready)
            {
                ReleaseKey();
                status.Text = recovery.Message;
                watch.Pause(elapsed);
                return;
            }
            if (operation == OperationMode.Grim && !grim.Running && !grim.Complete)
            {
                Stop(grim.Message);
                return;
            }
            if (!foreground)
            {
                ReleaseKey();
                watch.Pause(elapsed);
                approachWatch.Pause(elapsed);
                travel.Pause(elapsed);
                routeWalker.Pause(elapsed);
                if (returning)
                    returnProgressAt += elapsed;
                status.Text = reader.IsMinimized() ? "Bekliyor: oyun penceresi simge durumunda; büyüt ya da arka plana al." :
                    "Bekliyor: oyun önde değil (Skiller sayfasında arka plan tuşları kapalı).";
                return;
            }
            if (!snapshot.Player.Outside)
            {
                Stop("Desteklenmeyen kapalı koordinat türüne geçildi.");
                return;
            }
            if (snapshot.Berserk >= 0 && snapshot.Berserk < 5)
                zerkLatched = false;
            if (ProcessMaintenance(now))
            {
                approachWatch.Pause(elapsed);
                routeWalker.Pause(elapsed);
                return;
            }
            RecordExit();
            if (operation == OperationMode.Grim && QueueAfterComplete(now))
                return;
            if (operation == OperationMode.Travel)
            {
                ExpeditionTick(now);
                return;
            }
            if (PressSupport(now))
                return;
            if (useRotation && rotation.Options.BuffOnly && operation == OperationMode.Hunt)
            {
                status.Text = "Yalnız buff / iyileştirme modu: saldırı kapalı.";
                return;
            }
            Monster target;
            if (operation == OperationMode.Grim)
            {
                int mapBudget = snapshot.Monsters.Any(m => m.Alive && grim.IsRegion(m.Position)) ? 0 : 48;
                grim.MapStep(snapshot, sender, mapBudget);
                target = grim.Choose(snapshot, banned, now, wanted);
            }
            else
            {
                if (!TargetPolicy.InArea(snapshot.Player, center, (double)radius.Value))
                {
                    wanted = 0;
                    watch.Reset();
                    ReturnHome(now);
                    return;
                }
                target = MultiTarget.Choose(snapshot, selectedMobs, center, (double)radius.Value, banned, now, wanted);
            }
            uint nextWanted = target == null ? 0 : target.Uid;
            if (wanted != nextWanted)
            {
                wanted = nextWanted;
                watch.Reset();
                attempts = 0;
                if (useRotation)
                    rotation.NewTarget();
            }
            if (target == null)
            {
                if (operation != OperationMode.Grim)
                    ReturnHome(now);
                else if (grim.Complete)
                    CompletionExit(now);
                else
                    GrimExplore(now);
                return;
            }
            grimHolding = false;
            returning = false;
            if (routeTarget != target.Uid)
            {
                if (operation == OperationMode.Grim)
                {
                    grim.Route.Clear();
                    grim.Waypoint = 0;
                }
                approachWatch.Reset();
                routeWalker.Reset();
                routeTarget = target.Uid;
                nextVisibility = 0;
            }
            if (RunSendKeys && now >= nextVisibility && !routeWalker.Active)
            {
                targetWalkable = snapshot.Player.Distance(target.Position) <= 3 || sender.NavigationProbe(snapshot.Player,
                    target.Position);
                nextVisibility = now + 1000;
            }
            if (!routeWalker.Active && RunSendKeys && snapshot.Player.Distance(target.Position) > 4 && approachWatch.Stuck(target,
                snapshot.Player, now, 4000))
            {
                ReleaseKey();
                sender.Move(snapshot.Player.Copy());
                Func<Position, bool> limit = operation == OperationMode.Hunt ? (Func<Position, bool>)(p => TargetPolicy.InArea(p,
                    center, (double)radius.Value)) : null;
                routeWalker.Recover(snapshot.Player, target.Position, previousMovementPoint, now, limit);
                approachWatch.Reset();
            }
            if (RunSendKeys && (routeWalker.Active || !targetWalkable))
            {
                // Do not keep attack-generated movement pushing into an obstacle while
                // the incremental planner searches for a walkable approach.
                if (!routeWalker.Active)
                {
                    ReleaseKey();
                    sender.Move(snapshot.Player.Copy());
                }
                watch.Pause(elapsed);
                approachWatch.Reset();
                Func<Position, bool> limit = operation == OperationMode.Hunt ? (Func<Position, bool>)(p => TargetPolicy.InArea(p,
                    center, (double)radius.Value)) : null;
                if (WalkChecked(target.Position, now, limit, "Hedefe engel çevresi rota", true))
                {
                    nextVisibility = 0;
                    targetWalkable = true;
                    watch.Reset();
                }
                return;
            }
            if (RunSendKeys && watch.Stuck(target, snapshot.Player, now, (long)RunTimeout * 1000))
            {
                Skip(target.Uid, "Can/mesafe ilerlemesi yok", now);
                return;
            }
            if (snapshot.Selected != target.Uid)
            {
                ReleaseKey();
                if (now < nextRequest)
                    return;
                if (lastRequest != target.Uid)
                {
                    lastRequest = target.Uid;
                    attempts = 0;
                }
                if (attempts++ >= 3)
                {
                    Skip(target.Uid, "Hedef seçimi yanıt vermedi", now);
                    return;
                }
                bool valid = operation == OperationMode.Grim ? reader.ValidateAnyTarget(target) : reader.ValidateTarget(target,
                    center, (double)radius.Value);
                if (!CommandsAllowed() || !valid)
                    return;
                if (!sender.Send(target.Uid))
                {
                    Skip(target.Uid, "Seçim reddedildi", now);
                    return;
                }
                nextRequest = now + 350;
                status.Text = "Hedef doğrulanıyor: " + target.Uid;
                return;
            }
            status.Text = "Hedef: " + target.Name + " · " + V2Rules.ClassName(target) + " · UID " + target.Uid;
            if (RunZerk && snapshot.Berserk == 5 && !zerkLatched && heldScan == 0 && V2Rules.BerserkMatches(target,
                RunBerserkClass))
            {
                if (Press(9))
                {
                    zerkLatched = true;
                    maintenance.Text = "Berserk: " + V2Rules.ClassName(target) + " hedefinde Tab.";
                    nextKey = now + 200;
                }
                return;
            }
            if (RunSendKeys && useRotation && now >= nextKey && heldScan == 0)
            {
                ushort attackKey = rotation.NextAttack(now, V2Rules.ClassName(target), MonstersNear());
                if (attackKey != 0 && PressSkill(attackKey, target.Uid))
                    nextKey = now + RunDelay;
            }
            else if (RunSendKeys && parsedKeys.Length > 0 && now >= nextKey && heldScan == 0)
            {
                if (Press(parsedKeys[keyIndex]))
                {
                    keyIndex = (keyIndex + 1) % parsedKeys.Length;
                    nextKey = now + RunDelay;
                }
            }
        }
        catch (Exception e)
        {
            Stop(e.Message);
            Log(e.ToString());
        }
    }

    void HoldGrim(string message)
    {
        ReleaseKey();
        if (!grimWaiting && reader != null && sender != null && CommandsAllowed())
            sender.Move(snapshot.Player.Copy());
        grimWaiting = true;
        routeWalker.Reset();
        grim.Route.Clear();
        grim.Waypoint = 0;
        status.Text = message;
        grim.Message = message;
    }

    void GrimExplore(long now)
    {
        ReleaseKey();
        routeTarget = 0;
        if (!grimHolding)
        {
            if (CommandsAllowed())
                sender.Move(snapshot.Player.Copy());
            routeWalker.Reset();
            grim.Route.Clear();
            grim.Waypoint = 0;
            grimHolding = true;
            grimWaiting = true;
        }
        if (now < grimRetryAt)
        {
            status.Text = "Bu noktaya rota bulunamadı; kısa bekleyip sıradaki noktayı deneyecek.";
            grim.Message = status.Text;
            return;
        }
        if (graphPath != null)
        {
            if (graphIndex < graphPath.Count)
            {
                int hop = Math.Min(graphIndex + 3, graphPath.Count - 1);
                if (WalkChecked(graphPath[hop], now, grim.IsRegion, "Zemin ağı rotası · " + (hop + 1) + " / " + graphPath.Count, false))
                    graphIndex = hop + 1;
            }
            if (graphPath != null && graphIndex >= graphPath.Count)
            {
                graphPath = null;
                grimFailures = 0;
                if (grim.Rooms != null)
                    grim.Rooms.Arrived();
                else
                {
                    grim.Route.Clear();
                    grim.Waypoint = 0;
                }
            }
            grim.Message = status.Text;
            return;
        }
        if (grim.Rooms != null)
        {
            Position point = grim.Rooms.Next(snapshot.Player);
            if (point == null)
            {
                HoldGrim(grim.Rooms.Status + " · bulunduğu yerde bekliyor");
                return;
            }
            grimWaiting = false;
            if (WalkChecked(point, now, grim.IsRegion, grim.Rooms.Status, false))
            {
                grim.Rooms.Arrived();
                grimFailures = 0;
            }
            grim.Message = status.Text;
            return;
        }
        var floor = grim.Floor;
        if (floor == null)
        {
            Stop("Dungeon zemin haritası yok.");
            return;
        }
        bool alive = snapshot.Monsters.Any(m => m.Alive && grim.IsRegion(m.Position));
        if (!floor.Done)
        {
            status.Text = "Diğer odalara rota için zemin okunuyor · " + floor.Cells.Count + " nokta · " + floor.Queries +
                " geçiş";
            return;
        }
        if (floor.Cells.Count < 2)
        {
            Stop("Bağlantılı dungeon zemini bulunamadı; tamamlandı sayılmadı.");
            return;
        }
        if (floor.FullyCovered)
        {
            if (alive)
            {
                grim.EmptySince = -1;
                HoldGrim("Odalar kontrol edildi; geçici atlanan canlı moblar yeniden denenecek.");
                return;
            }
            bool stable = grim.CanFinish(snapshot, now);
            HoldGrim(stable ?
                "Haritadaki tüm alanlar kontrol edildi; 15 sn canlı mob görülmedi. Son konumda bekliyor (sunucu bitiş onayı yok)." :
                "Odalar kontrol edildi; son konumda son mob / dalga kontrolü bekleniyor.");
            return;
        }
        grim.EmptySince = -1;
        if (grim.Route.Count == 0)
        {
            try
            {
                grim.Route = floor.RouteToUncovered(snapshot.Player, sender.NavigationProbe);
            }
            catch (InvalidOperationException e)
            {
                // Pushed off the checked floor (a knock-back, a fight): walk back to the nearest checked point instead of giving up.
                Position nearest = floor.NearestCell(snapshot.Player);
                grim.Route = nearest == null ? new List<Position>() : new List<Position> { nearest };
                Log("Zemin ağına dönülüyor · " + e.Message);
            }
            grim.Waypoint = 0;
            routeWalker.Reset();
            if (grim.Route.Count == 0)
            {
                HoldGrim("Kontrol edilmemiş alana bağlı yol bulunamadı · bulunduğu yerde bekliyor, yeniden denenecek.");
                grimRetryAt = now + 3000;
                return;
            }
        }
        Position goal = grim.Route[grim.Route.Count - 1];
        grimWaiting = false;
        if (WalkChecked(goal, now, grim.IsRegion, "Diğer oda / alan kontrolü · " + floor.Covered.Count + " / " + floor.Cells.Count,
            false))
        {
            grim.Route.Clear();
            grim.Waypoint = 0;
            grimFailures = 0;
        }
        grim.Message = status.Text;
    }

    // A Grim walk found no route. First try the checked floor cells between here and the goal; if that fails too, drop this point
    // (the sweep goes on with the next one) and pause briefly. The run is never stopped for this; it used to end here.
    bool GrimWalkFailed(string reason, Position goal, long now)
    {
        grimFailures++;
        Log("Grim yürüyüşü takıldı (" + grimFailures + "): " + reason);
        try
        {
            if (CommandsAllowed() && sender != null)
                sender.Move(snapshot.Player.Copy());
        }
        catch (IOException) { }
        var floor = grim.Floor;
        if (floor != null && graphPath == null && goal != null && grimFailures <= 4)
        {
            var path = floor.PathBetween(snapshot.Player, goal);
            if (path.Count > 1)
            {
                graphPath = path;
                graphIndex = 0;
                status.Text = "Rota bulunamadı; doğrulanmış zemin ağı üzerinden gidiliyor.";
                Log("Zemin ağı rotası · " + path.Count + " nokta");
                return true;
            }
        }
        graphPath = null;
        if (grim.Rooms != null)
            grim.Rooms.Arrived();
        else
        {
            grim.Route.Clear();
            grim.Waypoint = 0;
            if (floor != null && goal != null)
                floor.MarkCovered(goal);
        }
        grimRetryAt = now + (grimFailures >= 6 ? 6000 : 1200);
        if (grimFailures >= 6)
            grimFailures = 0;
        status.Text = "Bu noktaya rota bulunamadı; sıradaki noktaya geçiliyor.";
        return true;
    }

    bool Press(ushort key)
    {
        uint pid;
        uint thread = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out pid);
        if (reader == null)
            return false;
        if (pid != reader.Process.Id)
        {
            // The game is behind other windows: with background keys on the key goes to its window as keyboard messages.
            if (!backgroundKeys.Checked || reader.IsMinimized())
                return false;
            if (!PressBackground(key))
            {
                Stop("Arka plan tuşu gönderilemedi.");
                return false;
            }
            return true;
        }
        ushort scan = (ushort)Native.MapVirtualKeyEx(key, 0, Native.GetKeyboardLayout(thread));
        if (scan == 0 || !SendScan(scan, false))
        {
            Stop("Tuş gönderimi reddedildi.");
            return false;
        }
        heldScan = scan;
        release.Start();
        return true;
    }

    bool SendScan(ushort scan, bool up)
    {
        var input = new Native.INPUT();
        input.type = 1;
        input.data.key.scan = scan;
        input.data.key.flags = 8u | (up ? 2u : 0u);
        return Native.SendInput(1, new[] { input }, Marshal.SizeOf(typeof(Native.INPUT))) == 1;
    }

    void ReleaseKey()
    {
        release.Stop();
        if (heldScan != 0)
        {
            if (heldBackground ? KeyMessage.Post(ClientWindow(), heldVk, heldScan, true) : SendScan(heldScan, true))
            {
                heldScan = 0;
                heldBackground = false;
            }
            else
            {
                active = false;
                release.Start();
                status.Text = "Tuş bırakılamadı; otomasyon durdu.";
            }
        }
    }

    protected override void OnShown(EventArgs e)
    {
        if (startupFrozen)
        {
            startupFrozen = false;
            Ui.Thaw(this);
            PerformLayout();
        }
        base.OnShown(e);
        RegisterShortcut();
        SetupUpdates();
        BeginInvoke(new Action(ShowWhatsNew));
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x312 && m.WParam.ToInt32() == 1)
        {
            try
            {
                HandleShortcut();
            }
            catch (Exception e)
            {
                Stop(e.Message);
            }
        }
        base.WndProc(ref m);
    }

    // Asked when the user closes the window; the updater and the unit checks skip it.
    internal static bool SkipClosePrompt;
    internal static Func<IWin32Window, bool, bool> CloseConfirm = (owner, running) => ConfirmDialog.Ask(owner, "Uygulama kapatılsın mı?",
        running ? "Otomasyon çalışıyor. Kapatırsanız karakter bulunduğu yerde durur ve işlem yarıda kalır." : "Silkroad R Studio kapatılacak. Ayarlarınız kaydedilir.",
        "Kapat", "Vazgeç", running);

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.Cancel || SkipClosePrompt || e.CloseReason != CloseReason.UserClosing)
            return;
        if (!CloseConfirm(this, active || grim.Armed || queueRunning))
            e.Cancel = true;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        completionNotice.Dispose();
        DisposeDoneNotice();
        dualTimer.Stop();
        dualTimer.Dispose();
        ReleaseClientClaim();
        poll.Stop();
        updateTimer.Stop();
        updateTimer.Dispose();
        Stop("Kapatıldı.");
        Native.UnregisterHotKey(Handle, 1);
        try
        {
            SaveSettings();
        }
        catch { }
        if (reader != null)
            reader.Dispose();
        poll.Dispose();
        release.Dispose();
        base.OnFormClosed(e);
    }

    internal static MemorySelectV3Form Create()
    {
        return new MemorySelectV3Form();
    }

    [STAThread]
    static void Main(string[] args)
    {
        try
        {
            if (args.Length == 3 && (args[0] == "--notice-probe" || args[0] == "--notice-watch"))
            {
                using (var monitor = new CompletionNotice())
                {
                    monitor.Begin(int.Parse(args[1]));
                    var limit = Stopwatch.StartNew();
                    while (!monitor.Ready && string.IsNullOrEmpty(monitor.Error) && limit.ElapsedMilliseconds < 30000)
                        System.Threading.Thread.Sleep(100);
                    if (args[0] == "--notice-watch")
                    {
                        File.WriteAllText(args[2] + ".ready", "Ready=" + monitor.Ready + "; baseline=" + monitor.Candidates +
                            "; accepted=" + monitor.Message);
                        while (monitor.Ready && string.IsNullOrEmpty(monitor.Message) && string.IsNullOrEmpty(monitor.Error) &&
                            limit.ElapsedMilliseconds < 300000)
                            System.Threading.Thread.Sleep(200);
                    }
                    else if (monitor.Ready)
                        System.Threading.Thread.Sleep(6500);
                    File.WriteAllText(args[2], "Ready=" + monitor.Ready + "; Baseline notices=" + monitor.Candidates +
                        "; Accepted=" + monitor.Message + "; Error=" + monitor.Error);
                }
                return;
            }
            if (args.Length == 2 && args[0] == "--apply-update")
            {
                UpdateService.Apply(args[1]);
                return;
            }
            if (args.Length == 2 && args[0] == "--updated")
                UpdateService.Acknowledgment = args[1];
            if (args.Length > 0 && args[0] == "--diagnose")
            {
                using (var r = new GameReader(int.Parse(args[1]), Catalog.Load()))
                {
                    var s = r.Capture();
                    var text = new StringBuilder();
                    text.AppendLine(string.Format("Player {0:F2},{1:F2}; alive={2}; zerk={3}; speedKnown={4}; speedActive={5}; inventoryKnown={6}; minDurability={7}; monsters={8}",
                        s.Player.X, s.Player.Y, s.PlayerAlive, s.Berserk, s.SpeedKnown, s.SpeedActive, s.InventoryKnown,
                        s.MinDurability, s.Monsters.Count));
                    text.AppendLine("Profile: " + r.Compatibility + "; send=" + r.SenderAddress.ToString("X8") +
                        " move=" + r.MoveAddress.ToString("X8") + " reverse=" + r.ReverseAddress.ToString("X8"));
                    text.AppendLine("Grim: known=" + s.Grim.Known + " id=" + s.Grim.Id + " title=" + s.Grim.Title +
                        " text=" + s.Grim.Text);
                    text.AppendLine("Mount known=" + s.MountKnown + " uid=" + s.MountUid);
                    text.AppendLine("Region=" + s.Player.Region + " local=" + s.Player.LocalX + "," + s.Player.Height +
                        "," + s.Player.LocalY);
                    foreach (var n in s.Npcs)
                        text.AppendLine("NPC " + n.Model + " uid=" + n.Uid + " x=" + n.Position.X + " y=" + n.Position.Y);
                    foreach (var m in s.Monsters)
                        text.AppendLine("Mob " + m.Uid + " " + m.Name + " alive=" + m.Alive + " hp=" + m.Health + " x=" +
                            m.Position.X + " y=" + m.Position.Y + " region=" + m.Position.Region);
                    foreach (var i in s.Items)
                        text.AppendLine(string.Format("Item ID={0} slot={1} quantity={2} type={3:X4} allowed={4}", i.Model,
                            i.Slot, i.Quantity, i.Type, V2Rules.AllowedItem(i)));
                    File.WriteAllText(args[2], text.ToString(), Encoding.UTF8);
                }
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string slot = Environment.GetEnvironmentVariable("SRSTUDIO_ACCOUNT");
            bool unique;
            using (var instance = new System.Threading.Mutex(false, "Local\\SilkroadRStudioAccount_" + (slot ?? "Main"), out unique))
            {
                if (slot != null && !unique)
                {
                    MessageBox.Show("Bu hesap paneli zaten açık.");
                    return;
                }
                StartupSplash splash = args.Length == 0 && Environment.GetEnvironmentVariable("SRSTUDIO_NOSPLASH") != "1" ?
                    StartupSplash.Open() : null;
                MemorySelectV3Form window;
                try
                {
                    window = Startup.Build(delegate(string message, int index)
                    {
                        if (splash != null)
                            splash.Report(message, index);
                    });
                }
                catch
                {
                    if (splash != null)
                        splash.Finish();
                    throw;
                }
                if (splash != null)
                    window.Shown += delegate { splash.Finish(); };
                Application.Run(window);
            }
        }
        catch (Exception e)
        {
            if (args.Length > 0)
            {
                File.WriteAllText("work/v2-diagnostic-error.txt", e.ToString());
                Environment.ExitCode = 1;
            }
            else
                MessageBox.Show(e.Message, AppPaths.AppName);
        }
    }
}

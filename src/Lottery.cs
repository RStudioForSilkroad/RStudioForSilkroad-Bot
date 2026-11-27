using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

// The game's own lottery window (Menu > New Era Lottery) takes no packet the bot knows, so it is used the way a player uses it: Fill with quick
// picks, Buy, and again while there is gold. The buttons are found from positions the user records once (the window opens in the middle of
// the game's drawing area, so a position is kept as the distance from that middle). Every purchase is checked against the gold in the bag.
static class LotteryRules
{
    // How many more coupons the gold allows: what is left over the reserve, divided by the price, within the limit (0 = no limit).
    internal static int Coupons(ulong gold, long reserve, long cost, int max, int already)
    {
        if (cost < 1 || gold <= (ulong)Math.Max(0, reserve))
            return 0;
        ulong n = (gold - (ulong)Math.Max(0, reserve)) / (ulong)cost;
        if (max > 0)
            n = Math.Min(n, (ulong)Math.Max(0, max - already));
        return (int)Math.Min(n, int.MaxValue);
    }
}

static class LotteryInput
{
    [DllImport("user32.dll")]
    static extern bool GetClientRect(IntPtr window, out Rc rect);
    [DllImport("user32.dll")]
    static extern bool ClientToScreen(IntPtr window, ref Pt point);
    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out Pt point);
    [DllImport("user32.dll")]
    static extern bool SetCursorPos(int x, int y);

    [StructLayout(LayoutKind.Sequential)]
    struct Pt
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Rc
    {
        public int Left, Top, Right, Bottom;
    }

    // The middle of the game's drawing area on the screen.
    internal static bool Centre(IntPtr window, out Point centre)
    {
        centre = Point.Empty;
        Rc r;
        if (window == IntPtr.Zero || !GetClientRect(window, out r) || r.Right < 64 || r.Bottom < 64)
            return false;
        var p = new Pt { X = r.Right / 2, Y = r.Bottom / 2 };
        if (!ClientToScreen(window, ref p))
            return false;
        centre = new Point(p.X, p.Y);
        return true;
    }

    internal static Point Cursor()
    {
        Pt p;
        GetCursorPos(out p);
        return new Point(p.X, p.Y);
    }

    internal static void MoveTo(Point p)
    {
        SetCursorPos(p.X, p.Y);
    }

    // The mouse pointer's distance from the middle of the game's drawing area.
    internal static bool Offset(IntPtr window, out Point offset)
    {
        offset = Point.Empty;
        Point c;
        if (!Centre(window, out c))
            return false;
        Point m = Cursor();
        offset = new Point(m.X - c.X, m.Y - c.Y);
        return true;
    }

    static bool Button(uint flag)
    {
        var input = new Native.INPUT();
        input.type = 0;
        input.data.mouse.flags = flag;
        return Native.SendInput(1, new[] { input }, Marshal.SizeOf(typeof(Native.INPUT))) == 1;
    }

    // Moves the pointer onto the recorded spot and clicks it (the game must be in front).
    internal static bool Click(IntPtr window, Point offset)
    {
        Point c;
        if (!Centre(window, out c))
            return false;
        var target = new Point(c.X + offset.X, c.Y + offset.Y);
        if (!SystemInformation.VirtualScreen.Contains(target))
            return false;
        SetCursorPos(target.X, target.Y);
        Thread.Sleep(90);
        if (!Button(2))
            return false;
        Thread.Sleep(70);
        return Button(4);
    }
}

sealed partial class MemorySelectV3Form
{
    TabPage lotteryTab;
    static readonly string[] LotteryPointNames = { "Fill with quick picks", "Buy", "Clear coupon (isteğe bağlı)", "Onay düğmesi (isteğe bağlı)" };
    readonly Point?[] lotteryPoints = new Point?[4];
    readonly Label[] lotteryPointLabels = new Label[4];
    readonly Label lotteryGold = new Label(), lotteryStatus = new Label();
    readonly NumericUpDown lotteryCost = new NumericUpDown(), lotteryReserve = new NumericUpDown(), lotteryMax = new NumericUpDown();
    readonly System.Windows.Forms.Timer lotteryTimer = new System.Windows.Forms.Timer { Interval = 250 };
    Button lotteryStart, lotteryStop;
    bool lotteryRunning, lotteryReady;
    int lotteryStep, lotteryBought, lotteryFails, lotteryWaits, lotteryCapture = -1;
    long lotteryNext, lotteryUntil, lotteryCaptureEnd, lotteryNextGold;
    ulong lotteryFirstGold, lotteryBefore;
    Point lotteryCursor;

    void BuildLotteryPage()
    {
        TilePage(lotteryTab);
        var card = Card("Otomatik kupon alımı", true);
        var row = Line(card);
        row.Controls.Add(Hint("NewEra Lottery penceresini aç (NewEra Lottery sekmesi, Play), aşağıdan başlat. Bot kuponu doldurur, satın alır, altın bitene kadar tekrarlar.", cardInner));
        row = Line(card);
        row.Controls.Add(Hint("Çalışırken fareye ve klavyeye dokunma; oyun önde kalır. Durdurmak için Durdur.", cardInner));
        row = Line(card);
        lotteryGold.AutoSize = false;
        lotteryGold.Width = cardInner;
        lotteryGold.Margin = new Padding(0, 0, 0, 4);
        lotteryGold.Height = 26;
        lotteryGold.TextAlign = ContentAlignment.MiddleLeft;
        lotteryGold.Text = "Altın: oyuna bağlanınca okunur.";
        row.Controls.Add(lotteryGold);
        row = Line(card);
        Num(lotteryCost, 1, 1000000000, 20000, 0, 110);
        row.Controls.Add(Caption("Kupon (8 bahis) fiyatı"));
        row.Controls.Add(lotteryCost);
        Num(lotteryReserve, 0, 10000000000m, 0, 0, 140);
        row.Controls.Add(Caption("Dokunulmayacak altın"));
        row.Controls.Add(lotteryReserve);
        row = Line(card);
        Num(lotteryMax, 0, 100000, 0, 0, 90);
        row.Controls.Add(Caption("En çok kupon (0 = altın bitene kadar)"));
        row.Controls.Add(lotteryMax);
        row = Line(card);
        lotteryStart = Btn("Satın almayı başlat", LotteryStartRequested, 170, false, ButtonKind.Primary);
        lotteryStop = Btn("Durdur", delegate { LotteryHalt("Durduruldu."); }, 100, false, ButtonKind.Secondary);
        row.Controls.Add(lotteryStart);
        row.Controls.Add(lotteryStop);
        row = Line(card);
        lotteryStatus.AutoSize = false;
        lotteryStatus.Width = cardInner;
        lotteryStatus.Margin = new Padding(0, 0, 0, 4);
        lotteryStatus.Height = 26;
        lotteryStatus.TextAlign = ContentAlignment.MiddleLeft;
        lotteryStatus.Tag = "muted";
        lotteryStatus.Text = "Hazır.";
        row.Controls.Add(lotteryStatus);

        card = Card("Düğme konumları (bir kez kaydet)", true);
        row = Line(card);
        row.Controls.Add(Hint("Lottery penceresi açıkken: “Kaydet”e bas, 4 sn içinde fareyi o düğmenin üstüne götür ve bekle.", cardInner));
        for (int i = 0; i < LotteryPointNames.Length; i++)
        {
            int index = i;
            row = Line(card);
            row.Controls.Add(LabelOf(LotteryPointNames[i], 240));
            row.Controls.Add(Btn("Kaydet", delegate { LotteryBeginCapture(index); }, 90, false, ButtonKind.Secondary));
            lotteryPointLabels[i] = new Label { AutoSize = false, Width = 260, Height = 30, TextAlign = ContentAlignment.MiddleLeft, Tag = "muted", Margin = new Padding(0, 0, 0, 6) };
            row.Controls.Add(lotteryPointLabels[i]);
            row.Controls.Add(Btn("Sil", delegate { lotteryPoints[index] = null; ShowLotteryPoints(); SaveSettings(); }, 60, false, ButtonKind.Secondary));
        }
        row = Line(card);
        row.Controls.Add(Hint("Satın alınca bir onay penceresi çıkıyorsa onun düğmesini de kaydet. Fiyat ilk alımda altından öğrenilir.", cardInner));

        RestoreLotterySettings();
        lotteryReady = true;
        lotteryTimer.Tick += delegate { Safe(LotteryTick); };
        lotteryTimer.Start();
    }

    void RestoreLotterySettings()
    {
        var saved = settings.LotteryPoints ?? new System.Collections.Generic.List<int>();
        for (int i = 0; i < lotteryPoints.Length; i++)
            lotteryPoints[i] = saved.Count >= i * 2 + 2 && saved[i * 2] != int.MinValue ? new Point(saved[i * 2], saved[i * 2 + 1]) : (Point?)null;
        SetValue(lotteryCost, settings.LotteryCost < 1 ? 20000 : settings.LotteryCost);
        SetValue(lotteryReserve, settings.LotteryReserve);
        SetValue(lotteryMax, settings.LotteryMax);
        ShowLotteryPoints();
    }

    void SaveLotterySettings()
    {
        var list = new System.Collections.Generic.List<int>();
        foreach (Point? p in lotteryPoints)
        {
            list.Add(p.HasValue ? p.Value.X : int.MinValue);
            list.Add(p.HasValue ? p.Value.Y : 0);
        }
        settings.LotteryPoints = list;
        settings.LotteryCost = (int)lotteryCost.Value;
        settings.LotteryReserve = (long)lotteryReserve.Value;
        settings.LotteryMax = (int)lotteryMax.Value;
    }

    void ShowLotteryPoints()
    {
        for (int i = 0; i < lotteryPoints.Length; i++)
            lotteryPointLabels[i].Text = lotteryPoints[i].HasValue ? "kayıtlı (" + lotteryPoints[i].Value.X + ", " + lotteryPoints[i].Value.Y + ")" :
                i < 2 ? "kayıtlı değil (gerekli)" : "kayıtlı değil";
    }

    void LotteryBeginCapture(int index)
    {
        RequireReader();
        if (lotteryRunning)
            throw new InvalidOperationException("Satın alma çalışırken konum kaydedilemez.");
        lotteryCapture = index;
        lotteryCaptureEnd = clock.ElapsedMilliseconds + 4000;
    }

    void LotteryTick()
    {
        long now = clock.ElapsedMilliseconds;
        if (lotteryCapture >= 0)
        {
            long left = lotteryCaptureEnd - now;
            if (left > 0)
            {
                lotteryStatus.Text = "“" + LotteryPointNames[lotteryCapture] + "” düğmesinin üstüne git: " + (left + 999) / 1000 + " sn…";
                return;
            }
            int index = lotteryCapture;
            lotteryCapture = -1;
            Point offset;
            if (!LotteryInput.Offset(ClientWindow(), out offset))
            {
                lotteryStatus.Text = "Oyun penceresi bulunamadı; konum kaydedilemedi.";
                return;
            }
            lotteryPoints[index] = offset;
            ShowLotteryPoints();
            SaveSettings();
            lotteryStatus.Text = "Kaydedildi: " + LotteryPointNames[index] + " (" + offset.X + ", " + offset.Y + ")";
            Log("Lottery: " + LotteryPointNames[index] + " konumu kaydedildi (" + offset.X + ", " + offset.Y + ")");
            return;
        }
        if (reader != null && now >= lotteryNextGold && (lotteryRunning || tabs.SelectedTab == lotteryTab))
        {
            lotteryNextGold = now + 1000;
            try
            {
                lotteryGold.Text = "Altın: " + reader.ReadGold().ToString("N0") + (lotteryRunning ? " · alınan kupon: " + lotteryBought : "");
            }
            catch (Exception)
            {
                lotteryGold.Text = "Altın okunamadı.";
            }
        }
        if (lotteryRunning && now >= lotteryNext)
            LotteryStepNow(now);
    }

    void LotteryStartRequested()
    {
        RequireReader();
        if (active)
            throw new InvalidOperationException("Önce çalışan işi durdur.");
        if (lotteryRunning)
            return;
        if (!lotteryPoints[0].HasValue || !lotteryPoints[1].HasValue)
            throw new InvalidOperationException("Önce “Fill with quick picks” ve “Buy” düğmelerinin konumunu kaydet.");
        ulong gold = reader.ReadGold();
        long cost = (long)lotteryCost.Value, reserve = (long)lotteryReserve.Value;
        int max = (int)lotteryMax.Value, count = LotteryRules.Coupons(gold, reserve, cost, max, 0);
        if (count < 1)
            throw new InvalidOperationException("Altın yetmiyor: " + gold.ToString("N0") + " altın, bir kupon " + cost.ToString("N0") + ".");
        string question = "Envanterdeki " + gold.ToString("N0") + " altından " + (reserve > 0 ? reserve.ToString("N0") + " bırakılarak " : "") + "en çok " +
            count + " kupon (" + ((long)count * cost).ToString("N0") + " altın) satın alınacak. Başlatılsın mı?";
        if (MessageBox.Show(this, question, "Lottery", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        SaveSettings();
        lotteryCursor = LotteryInput.Cursor();
        lotteryFirstGold = gold;
        lotteryBought = 0;
        lotteryFails = 0;
        lotteryWaits = 0;
        lotteryStep = 0;
        lotteryNext = 0;
        lotteryRunning = true;
        lotteryStart.Enabled = false;
        Log("Lottery: otomatik alım başladı · altın " + gold.ToString("N0") + " · kupon fiyatı " + cost.ToString("N0"));
    }

    void LotteryHalt(string why)
    {
        if (!lotteryRunning)
            return;
        lotteryRunning = false;
        lotteryStart.Enabled = true;
        LotteryInput.MoveTo(lotteryCursor);
        ulong gold = 0;
        try
        {
            gold = reader == null ? 0 : reader.ReadGold();
        }
        catch (Exception) { }
        string text = why + " · alınan kupon: " + lotteryBought + (lotteryFirstGold >= gold ? " · harcanan: " + (lotteryFirstGold - gold).ToString("N0") + " altın" : "");
        lotteryStatus.Text = text;
        Log("Lottery: " + text);
    }

    // One step of the buying cycle: Clear (if recorded), Fill with quick picks, Buy, (Confirm), then the gold must have dropped.
    void LotteryStepNow(long now)
    {
        IntPtr window = ClientWindow();
        if (reader == null || window == IntPtr.Zero)
        {
            LotteryHalt("Oyun bulunamadı");
            return;
        }
        if (lotteryStep != 4 && !reader.IsForeground())
        {
            if (++lotteryWaits > 16)
            {
                LotteryHalt("Oyun öne getirilemedi");
                return;
            }
            ForceForeground(window);
            lotteryNext = now + 500;
            return;
        }
        lotteryWaits = 0;
        switch (lotteryStep)
        {
            case 0:
            {
                ulong gold = reader.ReadGold();
                long cost = (long)lotteryCost.Value, reserve = (long)lotteryReserve.Value;
                if (LotteryRules.Coupons(gold, reserve, cost, (int)lotteryMax.Value, lotteryBought) < 1)
                {
                    LotteryHalt(lotteryMax.Value > 0 && lotteryBought >= lotteryMax.Value ? "Kupon sınırına ulaşıldı" : "Altın bitti");
                    return;
                }
                lotteryBefore = gold;
                lotteryStatus.Text = "Kupon " + (lotteryBought + 1) + " hazırlanıyor…";
                if (lotteryPoints[2].HasValue)
                {
                    LotteryClick(window, 2);
                    lotteryNext = now + 350;
                }
                lotteryStep = 1;
                return;
            }
            case 1:
                LotteryClick(window, 0);
                lotteryStep = 2;
                lotteryNext = now + 800;
                return;
            case 2:
                LotteryClick(window, 1);
                lotteryUntil = now + 5000;
                lotteryStep = lotteryPoints[3].HasValue ? 3 : 4;
                lotteryNext = now + 600;
                return;
            case 3:
                LotteryClick(window, 3);
                lotteryStep = 4;
                lotteryNext = now + 500;
                return;
            default:
            {
                ulong gold = reader.ReadGold();
                if (gold < lotteryBefore)
                {
                    ulong paid = lotteryBefore - gold;
                    lotteryBought++;
                    lotteryFails = 0;
                    if (lotteryBought == 1 && paid > 0 && paid < 1000000000UL && (long)paid != (long)lotteryCost.Value)
                    {
                        lotteryCost.Value = (decimal)paid;
                        Log("Lottery: bir kupon " + paid.ToString("N0") + " altın; fiyat güncellendi.");
                    }
                    if (lotteryBought == 1 || lotteryBought % 25 == 0)
                        Log("Lottery: " + lotteryBought + " kupon alındı · kalan altın " + gold.ToString("N0"));
                    lotteryStatus.Text = lotteryBought + " kupon alındı · kalan altın " + gold.ToString("N0");
                    lotteryStep = 0;
                    lotteryNext = now + 450;
                    return;
                }
                if (now < lotteryUntil)
                {
                    lotteryNext = now + 300;
                    return;
                }
                if (++lotteryFails >= 2)
                {
                    LotteryHalt("Satın alma doğrulanamadı (altın azalmadı): pencere açık mı, düğme konumları doğru mu, satış kapandı mı?");
                    return;
                }
                lotteryStep = 0;
                lotteryNext = now + 600;
                return;
            }
        }
    }

    void LotteryClick(IntPtr window, int point)
    {
        if (!lotteryPoints[point].HasValue || !LotteryInput.Click(window, lotteryPoints[point].Value))
            throw new InvalidOperationException("Fare tıklaması gönderilemedi (" + LotteryPointNames[point] + ").");
    }
}

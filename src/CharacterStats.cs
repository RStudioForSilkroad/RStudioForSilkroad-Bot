using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

// EXP needed to finish each level, taken from the game's own level table.

static class LevelData
{
    sealed class Row
    {
        public int Level;
        public long Exp;
    }

    static readonly Dictionary<int, long> required = Load();

    static Dictionary<int, long> Load()
    {
        var map = new Dictionary<int, long>();
        foreach (var row in V3Data.Load<List < Row>>("level-exp.json"))
            map[row.Level] = row.Exp;
        return map;
    }

    internal static long Required(int level)
    {
        long value;
        return required.TryGetValue(level, out value) ? value : 0;
    }
}

// Session totals and rates for EXP and SP. Rates use the last five minutes (or the whole session while it is shorter).

sealed class CharacterStats
{
    const long WindowMs = 5 * 60 * 1000;
    readonly List<long[]> history = new List<long[]>();
    bool started;
    int lastLevel;
    long lastExp, lastSp400, lastRecord;

    internal long StartMs { get; private set; }

    internal long ExpGained { get; private set; }

    internal long Sp400Gained { get; private set; }

    internal int LevelsGained { get; private set; }

    internal double SpGained
    {
        get { return Sp400Gained / 400.0; }
    }

    internal void Reset()
    {
        history.Clear();
        started = false;
        ExpGained = 0;
        Sp400Gained = 0;
        LevelsGained = 0;
    }

    internal bool Started
    {
        get { return started; }
    }

    internal void Observe(long now, Snapshot s)
    {
        if (s == null || !s.StatsKnown)
            return;
        long sp400 = s.Sp * 400L + s.SpExp;
        if (!started)
        {
            started = true;
            StartMs = now;
            lastLevel = s.Level;
            lastExp = (long)s.Exp;
            lastSp400 = sp400;
            Record(now);
            return;
        }
        long exp = (long)s.Exp;
        if (s.Level == lastLevel)
        {
            if (exp > lastExp)
                ExpGained += exp - lastExp; // a lower value after a death is not a gain
        }
        else if (s.Level == lastLevel + 1)
        {
            long left = LevelData.Required(lastLevel) - lastExp;
            ExpGained += (left > 0 ? left : 0) + exp; // level up: the rest of the old level plus the new exp
            LevelsGained++;
        }
        lastLevel = s.Level;
        lastExp = exp;
        if (sp400 > lastSp400)
            Sp400Gained += sp400 - lastSp400;
        lastSp400 = sp400;
        if (now - lastRecord >= 5000)
            Record(now);
    }

    void Record(long now)
    {
        history.Add(new[] { now, ExpGained, Sp400Gained });
        lastRecord = now;
        while (history.Count > 2 && now - history[1][0] > WindowMs + 60000)
            history.RemoveAt(0);
    }

    // idx 1 = exp, 2 = sp (in 1/400 units); returns the gain per minute.

    double PerMinute(long now, int idx)
    {
        if (!started || history.Count == 0)
            return 0;
        long since = now - WindowMs;
        long[] from = history.FirstOrDefault(h => h[0] >= since) ?? history[history.Count - 1];
        if (since < StartMs)
            from = history[0];
        double minutes = (now - from[0]) / 60000.0;
        if (minutes < 0.1)
            return 0;
        long current = idx == 1 ? ExpGained : Sp400Gained;
        return (current - from[idx]) / minutes;
    }

    internal double ExpPerMinute(long now)
    {
        return PerMinute(now, 1);
    }

    internal double SpPerMinute(long now)
    {
        return PerMinute(now, 2) / 400.0;
    }

    // Minutes until the level is full at the current EXP rate; -1 when unknown.

    internal double MinutesToLevel(long now, Snapshot s)
    {
        double rate = ExpPerMinute(now);
        long need = LevelData.Required(s.Level) - (long)s.Exp;
        return rate > 0 && need > 0 ? need / rate : -1;
    }

    // Minutes until the next full SP point (400 SP-exp points make one SP); -1 when unknown.

    internal double MinutesToNextSp(long now, Snapshot s)
    {
        double rate = SpPerMinute(now) * 400.0;
        return rate > 0 ? (400 - s.SpExp) / rate : -1;
    }
}

// The character status card: HP (red), MP (blue), EXP (green) and SP (yellow) bars plus per-minute and per-hour rates.

sealed class StatPanel : Control
{
    internal static readonly Color HpColor = Color.FromArgb(214, 48, 48), MpColor = Color.FromArgb(52, 120, 235),
        ExpColor = Color.FromArgb(52, 176, 84), SpColor = Color.FromArgb(240, 196, 40);
    internal Color PanelColor = Color.FromArgb(20, 34, 55), TextColor = Color.White;
    internal Snapshot Sample;
    internal CharacterStats Stats;
    internal long Now;

    internal StatPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    internal static string Duration(double minutes)
    {
        if (minutes < 0 || double.IsNaN(minutes) || double.IsInfinity(minutes))
            return "—";
        if (minutes > 60 * 24 * 99)
            return "99+ gün";
        long seconds = (long)(minutes * 60);
        if (seconds >= 86400)
            return (seconds / 86400) + " gün " + ((seconds % 86400) / 3600) + " sa";
        if (seconds >= 3600)
            return (seconds / 3600) + " sa " + ((seconds % 3600) / 60) + " dk";
        if (seconds >= 60)
            return (seconds / 60) + " dk " + (seconds % 60) + " sn";
        return seconds + " sn";
    }

    static string Num(double value)
    {
        return Math.Round(value).ToString("N0");
    }

    static string Signed(double value)
    {
        return "+" + Num(value);
    }

    void Bar(Graphics g, Font font, int y, string label, double fraction, Color color, string text)
    {
        const int left = 50, height = 24;
        int width = Width - left - 6;
        using (var track = new SolidBrush(Color.FromArgb(40, 42, 54)))
            g.FillRectangle(track, left, y, width, height);
        fraction = Math.Max(0, Math.Min(1, double.IsNaN(fraction) ? 0 : fraction));
        using (var fill = new LinearGradientBrush(new Rectangle(left, y, Math.Max(1, width), height), ControlPaint.Light(color,
            0.25f), color, LinearGradientMode.Vertical))
            g.FillRectangle(fill, left, y, (int)(width * fraction), height);
        using (var edge = new Pen(Color.FromArgb(90, 255, 255, 255)))
            g.DrawRectangle(edge, left, y, width - 1, height - 1);
        using (var labelBrush = new SolidBrush(color))
            g.DrawString(label, font, labelBrush, 4, y + 3);
        var size = g.MeasureString(text, font);
        float tx = left + (width - size.Width) / 2, ty = y + (height - size.Height) / 2;
        foreach (var o in new[] { new PointF(-1, 0), new PointF(1, 0), new PointF(0, -1), new PointF(0, 1) })
            g.DrawString(text, font, Brushes.Black, tx + o.X, ty + o.Y);
        g.DrawString(text, font, Brushes.White, tx, ty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.Clear(PanelColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bold = new Font("Segoe UI", 10f, FontStyle.Bold))
            using (var normal = new Font("Segoe UI", 10f))
                using (var text = new SolidBrush(TextColor))
                {
                    var s = Sample;
                    if (s == null || !s.StatsKnown)
                    {
                        g.DrawString("Karakter verisi yok · oyun penceresini seçip Bağlan / Oku'ya bas.", normal, text,
                            4, 8);
                        return;
                    }
                    using (var big = new Font("Segoe UI", 16f, FontStyle.Bold))
                        using (var gold = new SolidBrush(Color.FromArgb(255, 205, 80)))
                        {
                            float x0 = 4;
                            if (!string.IsNullOrEmpty(s.CharacterName))
                            {
                                g.DrawString(s.CharacterName, big, text, 4, 0);
                                x0 = 4 + g.MeasureString(s.CharacterName, big).Width + 6;
                            }
                            string level = "Seviye " + s.Level;
                            g.DrawString(level, big, gold, x0, 0);
                            float x = x0 + g.MeasureString(level, big).Width + 8;
                            string more = "";
                            if (Stats != null && Stats.Started)
                            {
                                more = "Oturum süresi " + Duration((Now - Stats.StartMs) / 60000.0);
                                if (Stats.LevelsGained > 0)
                                    more += "   ·   +" + Stats.LevelsGained + " seviye atlandı";
                            }
                            if (more.Length > 0)
                                g.DrawString(more, bold, text, x, 8);
                        }
                    int y = 36;
                    Bar(g, bold, y, "HP", s.MaxHp == 0 ? 0 : (double)s.Hp / s.MaxHp, HpColor, s.Hp.ToString("N0") +
                        " / " + s.MaxHp.ToString("N0") + "  ·  %" + (s.MaxHp == 0 ? 0 : 100.0 * s.Hp / s.MaxHp).ToString("0.0"));
                    y += 30;
                    Bar(g, bold, y, "MP", s.MaxMp == 0 ? 0 : (double)s.Mp / s.MaxMp, MpColor, s.Mp.ToString("N0") +
                        " / " + s.MaxMp.ToString("N0") + "  ·  %" + (s.MaxMp == 0 ? 0 : 100.0 * s.Mp / s.MaxMp).ToString("0.0"));
                    y += 30;
                    long need = LevelData.Required(s.Level);
                    double expFraction = need > 0 ? (double)s.Exp / need : 0;
                    Bar(g, bold, y, "EXP", expFraction, ExpColor, need > 0 ? ((long)s.Exp).ToString("N0") + " / " + need.ToString("N0") +
                        "  ·  %" + (100.0 * expFraction).ToString("0.00") : ((long)s.Exp).ToString("N0"));
                    y += 30;
                    Bar(g, bold, y, "SP", s.SpExp / 400.0, SpColor, s.Sp.ToString("N0") + " SP  ·  sonraki SP'ye %" + (100.0 * s.SpExp / 400.0)
                        .ToString("0.0"));
                    y += 36;
                    int[] columns = { 4, 60, 240, 430, 700 };
                    string[] titles = { "", "Oturum", "Dakikada", "Saatte", "Kalan" };
                    for (int i = 1; i < titles.Length; i++)
                        g.DrawString(titles[i], bold, text, columns[i], y);
                    y += 22;
                    if (Stats == null || !Stats.Started)
                        return;
                    double expMin = Stats.ExpPerMinute(Now), spMin = Stats.SpPerMinute(Now);
                    using (var expBrush = new SolidBrush(ExpColor))
                        using (var spBrush = new SolidBrush(SpColor))
                        {
                            g.DrawString("EXP", bold, expBrush, columns[0], y);
                            g.DrawString(Signed(Stats.ExpGained), normal, expBrush, columns[1], y);
                            g.DrawString(Signed(expMin) + " /dk", normal, expBrush, columns[2], y);
                            g.DrawString(Signed(expMin * 60) + " /sa" + (need > 0 ? " (%" + (100.0 * expMin * 60 / need)
                                .ToString("0.0") + ")" : ""), normal, expBrush, columns[3], y);
                            g.DrawString("Seviyeye " + Duration(Stats.MinutesToLevel(Now, s)), normal, expBrush, columns[4],
                                y);
                            y += 22;
                            g.DrawString("SP", bold, spBrush, columns[0], y);
                            g.DrawString("+" + Stats.SpGained.ToString("0.0"), normal, spBrush, columns[1], y);
                            g.DrawString("+" + spMin.ToString("0.00") + " /dk", normal, spBrush, columns[2], y);
                            g.DrawString("+" + (spMin * 60).ToString("0.0") + " /sa", normal, spBrush, columns[3], y);
                            g.DrawString("Sonraki SP " + Duration(Stats.MinutesToNextSp(Now, s)), normal, spBrush,
                                columns[4], y);
                        }
                }
    }
}

sealed partial class MemorySelectV3Form
{
    readonly StatPanel statPanel = new StatPanel();
    readonly CharacterStats characterStats = new CharacterStats();

    void BuildStatCard()
    {
        var card = Card("Karakter durumu");
        statPanel.Stats = characterStats;
        statPanel.Size = new Size(1030, 240);
        Line(card).Controls.Add(statPanel);
        Line(card).Controls.Add(Btn("Sayaçları sıfırla", delegate
        {
            characterStats.Reset();
            statPanel.Invalidate();
        }, 180, false));
    }

    void UpdateStatPanel(long now)
    {
        characterStats.Observe(now, snapshot);
        statPanel.Sample = snapshot;
        statPanel.Now = now;
        statPanel.Invalidate();
    }
}

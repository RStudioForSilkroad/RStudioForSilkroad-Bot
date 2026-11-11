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

// The character status card: HP (red), MP (blue), EXP (green) and SP (amber) bars plus per-minute and per-hour rates.

sealed class StatPanel : Control
{
    internal static readonly Color HpColor = Color.FromArgb(239, 68, 68), MpColor = Color.FromArgb(59, 130, 246),
        ExpColor = Color.FromArgb(34, 197, 94), SpColor = Color.FromArgb(245, 158, 11);
    // Darker variants for text on white (at least 4.5:1)
    static readonly Color HpInk = Color.FromArgb(185, 28, 28), MpInk = Color.FromArgb(29, 78, 216), ExpInk = Color.FromArgb(21, 128, 61),
        SpInk = Color.FromArgb(180, 83, 9);
    internal const int EmptyHeight = 48, FullHeight = 262;
    internal Snapshot Sample;
    internal CharacterStats Stats;
    internal long Now;

    internal StatPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        Height = EmptyHeight;
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

    const TextFormatFlags Flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

    static void DrawLine(Graphics g, string text, Font font, Color color, int x, int y, int width, int height = 24)
    {
        TextRenderer.DrawText(g, text, font, new Rectangle(x, y, Math.Max(10, width), height), color, Flags);
    }

    void Bar(Graphics g, int y, string label, double fraction, Color fill, Color ink, string text)
    {
        int width = Width;
        int valueWidth = Math.Min(270, Math.Max(150, width * 45 / 100)), left = 48;
        int barWidth = Math.Max(80, width - left - valueWidth - 12);
        DrawLine(g, label, Ui.BodyBold, ink, 0, y, 44);
        var track = new Rectangle(left, y + 5, barWidth, 14);
        using (var path = Ui.RoundRect(track, 7))
            using (var brush = new SolidBrush(Color.FromArgb(230, 234, 241)))
                g.FillPath(brush, path);
        fraction = Math.Max(0, Math.Min(1, double.IsNaN(fraction) ? 0 : fraction));
        int fillWidth = (int)(barWidth * fraction);
        if (fillWidth >= 2)
        {
            var part = new Rectangle(left, y + 5, Math.Max(14, fillWidth), 14);
            using (var path = Ui.RoundRect(part, 7))
                using (var brush = new SolidBrush(fill))
                    g.FillPath(brush, path);
        }
        DrawLine(g, text, Ui.Body, Ui.Text, left + barWidth + 12, y, valueWidth);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.Clear(Ui.Surface);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var s = Sample;
        if (s == null || !s.StatsKnown)
        {
            DrawLine(g, "Karakter verisi yok · oyun penceresini seçip Bağlan / Oku'ya bas.", Ui.Body, Ui.TextMuted, 0, 4, Width, 28);
            return;
        }
        int x = 0;
        if (!string.IsNullOrEmpty(s.CharacterName))
        {
            int w = TextRenderer.MeasureText(s.CharacterName, Ui.Title, new Size(2000, 40), Flags).Width;
            DrawLine(g, s.CharacterName, Ui.Title, Ui.Text, 0, 0, w + 4, 32);
            x = w + 14;
        }
        string level = "Seviye " + s.Level;
        int levelWidth = TextRenderer.MeasureText(level, Ui.BodyBold, new Size(2000, 40), Flags).Width + 20;
        var badge = new Rectangle(x, 4, levelWidth, 24);
        using (var path = Ui.RoundRect(badge, 12))
            using (var brush = new SolidBrush(Ui.AccentTint))
                g.FillPath(brush, path);
        TextRenderer.DrawText(g, level, Ui.BodyBold, badge, Ui.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        x = badge.Right + 14;
        if (Stats != null && Stats.Started)
        {
            string more = "Oturum süresi " + Duration((Now - Stats.StartMs) / 60000.0);
            if (Stats.LevelsGained > 0)
                more += "  ·  +" + Stats.LevelsGained + " seviye atlandı";
            DrawLine(g, more, Ui.Body, Ui.TextSecondary, x, 4, Width - x, 24);
        }
        int y = 42;
        Bar(g, y, "HP", s.MaxHp == 0 ? 0 : (double)s.Hp / s.MaxHp, HpColor, HpInk, s.Hp.ToString("N0") + " / " + s.MaxHp.ToString("N0") +
            "  ·  %" + (s.MaxHp == 0 ? 0 : 100.0 * s.Hp / s.MaxHp).ToString("0.0"));
        y += 32;
        Bar(g, y, "MP", s.MaxMp == 0 ? 0 : (double)s.Mp / s.MaxMp, MpColor, MpInk, s.Mp.ToString("N0") + " / " + s.MaxMp.ToString("N0") +
            "  ·  %" + (s.MaxMp == 0 ? 0 : 100.0 * s.Mp / s.MaxMp).ToString("0.0"));
        y += 32;
        long need = LevelData.Required(s.Level);
        double expFraction = need > 0 ? (double)s.Exp / need : 0;
        Bar(g, y, "EXP", expFraction, ExpColor, ExpInk, need > 0 ? ((long)s.Exp).ToString("N0") + " / " + need.ToString("N0") + "  ·  %" +
            (100.0 * expFraction).ToString("0.00") : ((long)s.Exp).ToString("N0"));
        y += 32;
        Bar(g, y, "SP", s.SpExp / 400.0, SpColor, SpInk, s.Sp.ToString("N0") + " SP  ·  sonraki SP'ye %" + (100.0 * s.SpExp / 400.0).ToString("0.0"));
        y += 40;
        using (var line = new Pen(Ui.Divider))
            g.DrawLine(line, 0, y - 6, Width, y - 6);
        int span = Math.Max(300, Width - 52);
        int[] columns = { 0, 52, 52 + span * 20 / 100, 52 + span * 44 / 100, 52 + span * 70 / 100 };
        string[] titles = { "", "Oturum", "Dakikada", "Saatte", "Kalan" };
        for (int i = 1; i < titles.Length; i++)
            DrawLine(g, titles[i], Ui.Small, Ui.TextMuted, columns[i], y, columns[i] + 100 > Width ? 80 : 120, 20);
        y += 22;
        if (Stats == null || !Stats.Started)
        {
            DrawLine(g, "Sayaçlar bot çalışırken dolar.", Ui.Body, Ui.TextMuted, 0, y, Width, 24);
            return;
        }
        double expMin = Stats.ExpPerMinute(Now), spMin = Stats.SpPerMinute(Now);
        int cell = Math.Max(90, span * 24 / 100);
        DrawLine(g, "EXP", Ui.BodyBold, ExpInk, columns[0], y, 48);
        DrawLine(g, Signed(Stats.ExpGained), Ui.Body, Ui.Text, columns[1], y, cell);
        DrawLine(g, Signed(expMin) + " /dk", Ui.Body, Ui.Text, columns[2], y, cell);
        DrawLine(g, Signed(expMin * 60) + " /sa" + (need > 0 ? " (%" + (100.0 * expMin * 60 / need).ToString("0.0") + ")" : ""), Ui.Body, Ui.Text,
            columns[3], y, cell + 20);
        DrawLine(g, "Seviyeye " + Duration(Stats.MinutesToLevel(Now, s)), Ui.Body, Ui.Text, columns[4], y, Width - columns[4]);
        y += 26;
        DrawLine(g, "SP", Ui.BodyBold, SpInk, columns[0], y, 48);
        DrawLine(g, "+" + Stats.SpGained.ToString("0.0"), Ui.Body, Ui.Text, columns[1], y, cell);
        DrawLine(g, "+" + spMin.ToString("0.00") + " /dk", Ui.Body, Ui.Text, columns[2], y, cell);
        DrawLine(g, "+" + (spMin * 60).ToString("0.0") + " /sa", Ui.Body, Ui.Text, columns[3], y, cell);
        DrawLine(g, "Sonraki SP " + Duration(Stats.MinutesToNextSp(Now, s)), Ui.Body, Ui.Text, columns[4], y, Width - columns[4]);
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
        Line(card).Controls.Add(Stretch(statPanel));
        Line(card).Controls.Add(Btn("Sayaçları sıfırla", delegate
        {
            characterStats.Reset();
            statPanel.Invalidate();
        }, 160, false, ButtonKind.Ghost));
    }

    void UpdateStatPanel(long now)
    {
        characterStats.Observe(now, snapshot);
        statPanel.Sample = snapshot;
        statPanel.Now = now;
        int wanted = snapshot != null && snapshot.StatsKnown ? StatPanel.FullHeight : StatPanel.EmptyHeight;
        if (statPanel.Height != wanted)
            statPanel.Height = wanted;
        statPanel.Invalidate();
    }
}

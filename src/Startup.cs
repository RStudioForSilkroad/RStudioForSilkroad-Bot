using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

// Start-up procedure: check the program's own files, read the settings, load the game data and the road network, build the window.
// The splash screen runs on its own thread, so it keeps moving while the main thread is busy building the window.

static class Startup
{
    // Every file the program needs is embedded; a build that lost one is reported by name instead of failing somewhere later.
    internal static readonly string[] RequiredResources =
    {
        "Silkroad.ico", "grim-locations.json", "town-regions.json", "transport-network.json", "return-scrolls.json", "gate-npcs.json", "jangan-pass-road.json", "caravan-scripts.json", "Launcher.frame00.jpg", "Launcher.frame11.jpg", "Launcher.plate.jpg", "Unique.tiger.png", "Unique.shaitan.png", "skill-weapons.json", "weapon-types.json", "skill-timing.json", "reverse-points.json", "explorer-spots.json",
        "level-exp.json", "monster-names.tsv", "road-mesh.bin.gz", "road-surface.bin.gz", "updates-public-key.xml", "world-map-game.jpg",
        "world-map.jpg", "explorer-52.jpg"
    };

    internal static readonly string[] StepNames =
    {
        "Dosyalar denetleniyor", "Ayarlar okunuyor", "Oyun verileri yükleniyor", "Yol ağı hazırlanıyor", "Pencere kuruluyor"
    };

    internal static List<string> MissingResources(IEnumerable<string> present)
    {
        var have = new HashSet<string>(present, StringComparer.Ordinal);
        return RequiredResources.Where(name => !have.Contains(name)).ToList();
    }

    // Progress of a finished step, 0-100; the last step ends at 100 only when the window is shown.
    internal static int Percent(int finished, int total)
    {
        if (total <= 0)
            return 0;
        return Math.Max(0, Math.Min(100, finished * 100 / total));
    }

    // Touches the data that would otherwise be read the first time a page needs it.
    internal static void WarmData()
    {
        int count = V3Data.Dungeons.Count + V3Data.Towns.Count + ExpeditionRules.Network.Gates.Count + ExplorerSpots.All.Count +
            Catalog.Load().Count;
        if (count <= 0)
            throw new InvalidOperationException("Oyun verileri boş.");
    }

    // Runs the steps and returns the finished window. Each step is reported before it starts.
    internal static MemorySelectV3Form Build(Action<string, int> report)
    {
        report(StepNames[0], 0);
        var missing = MissingResources(Assembly.GetExecutingAssembly().GetManifestResourceNames());
        if (missing.Count > 0)
            throw new InvalidOperationException("Programdan şu gömülü dosyalar eksik: " + string.Join(", ", missing) +
                ". Programı yeniden indir.");
        report(StepNames[1], 1);
        var settings = V3Settings.Load();
        Ui.SetAccent(Math.Max(0, settings.Theme));
        report(StepNames[2], 2);
        WarmData();
        report(StepNames[3], 3);
        ExpeditionRules.Warm();
        report(StepNames[4], 4);
        return MemorySelectV3Form.Create();
    }
}

sealed class StartupSplash : Form
{
    const int CardWidth = 640, CardHeight = 488;
    readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 16 };
    readonly Font nameFont = new Font(Ui.Title.FontFamily, 21f, FontStyle.Bold), textFont = new Font("Segoe UI", 10.5f),
        smallFont = new Font("Segoe UI", 9f), headFont = new Font(Ui.Title.FontFamily, 14f, FontStyle.Bold), bigFont = new Font(Ui.Title.FontFamily, 22f, FontStyle.Bold), cityFont = new Font(Ui.Title.FontFamily, 9.5f, FontStyle.Italic);

    // The stops of the route: the towns of the game from east to west, starting at Nanzhang, the new city above Jangan.
    static readonly string[] Cities = { "Nanzhang", "Jangan", "Donwhang", "Hotan", "Samarkand", "Constantinople" };
    readonly object gate = new object();
    string text = "Başlatılıyor";
    int step, total = Startup.StepNames.Length;
    double shown, target;
    bool finishing;
    double phase;
    readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    double previewFilm = -1;

    StartupSplash()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        ClientSize = new Size(CardWidth, CardHeight);
        BackColor = Ui.Surface;
        using (var path = Ui.RoundRect(new Rectangle(0, 0, CardWidth, CardHeight), 14))
            Region = new Region(path);
        timer.Tick += delegate { Advance(); };
    }

    // Opens the splash on its own thread; returns when its window exists.
    internal static StartupSplash Open()
    {
        StartupSplash splash = null;
        var ready = new ManualResetEvent(false);
        var thread = new Thread(delegate()
        {
            splash = new StartupSplash();
            splash.Shown += delegate { ready.Set(); };
            try
            {
                Application.Run(splash);
            }
            catch
            {
                ready.Set();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Name = "Açılış ekranı";
        thread.Start();
        ready.WaitOne(5000);
        return splash;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var p = base.CreateParams;
            p.ClassStyle |= 0x20000; // CS_DROPSHADOW
            return p;
        }
    }

    protected override bool ShowWithoutActivation
    {
        get { return true; }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        timer.Start();
    }

    internal void Report(string message, int stepIndex)
    {
        lock (gate)
        {
            text = message;
            step = stepIndex;
            target = Startup.Percent(stepIndex, total);
        }
    }

    // Fills the bar, then fades the splash out and closes it.
    internal void Finish()
    {
        lock (gate)
        {
            text = "Hazır";
            target = 100;
            finishing = true;
        }
    }

    void Advance()
    {
        double goal;
        bool done;
        lock (gate)
        {
            goal = target;
            done = finishing;
        }
        phase = (phase + 0.025) % 1.0;
        // Ease toward the finished steps and creep a little into the current one, so the bar never looks stuck.
        double ceiling = Math.Min(99, goal + 100.0 / total * 0.8);
        double want = done ? 100 : Math.Min(ceiling, Math.Max(goal, shown + 0.1));
        shown += (want - shown) * (done ? 0.3 : 0.15);
        if (shown > 100)
            shown = 100;
        if (done && shown >= 99.5)
        {
            Opacity = Math.Max(0, Opacity - 0.12);
            if (Opacity <= 0.02)
            {
                timer.Stop();
                Close();
                return;
            }
        }
        Invalidate();
    }

    // A picture of the splash at a given progress, for the layout tests.
    internal static Bitmap Preview(double percent, int stepIndex, string message)
    {
        using (var splash = new StartupSplash())
        {
            splash.shown = percent;
            splash.step = stepIndex;
            splash.text = message;
            splash.phase = 0.37;
            splash.previewFilm = 4.2;
            var bitmap = new Bitmap(CardWidth, CardHeight);
            splash.DrawToBitmap(bitmap, new Rectangle(0, 0, CardWidth, CardHeight));
            return bitmap;
        }
    }

    // The top band: the game's own launcher film, the golden dragon of Nanzhang turning to breathe fire over the city gate. Twelve frames half a second
    // apart, blended into each other; the film runs once and stops on the breath of fire. It fades into the card at the bottom.
    const int ArtHeight = 250;

    void PaintArt(Graphics g)
    {
        double seconds = previewFilm >= 0 ? previewFilm : clock.Elapsed.TotalSeconds;
        GameArt.DrawFilm(g, new Rectangle(0, 0, CardWidth, ArtHeight), new Rectangle(0, 8, CardWidth, ArtHeight), seconds);
        using (var fade = new LinearGradientBrush(new Rectangle(0, ArtHeight - 70, CardWidth, 71), Color.FromArgb(0, Ui.Surface), Ui.Surface, 90f))
            g.FillRectangle(fade, 0, ArtHeight - 70, CardWidth, 71);
        // the city the route begins at, named over its gate
        TextRenderer.DrawText(g, "Nanzhang", cityFont, new Rectangle(CardWidth - 170, ArtHeight - 50, 150, 24), Ui.Accent,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }

    // Letters drawn one by one with a little space between them, as the launcher sets its headings.
    static void Spaced(Graphics g, string text, Font font, Color color, int x, int y, float tracking)
    {
        float at = x;
        foreach (char c in text)
        {
            string s = c.ToString();
            TextRenderer.DrawText(g, s, font, new Point((int)Math.Round(at), y), color, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            at += TextRenderer.MeasureText(g, c == ' ' ? "i" : s, font, new Size(100, 40), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width + tracking +
                (c == ' ' ? 2 : 0);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var night = Ui.Mix(Ui.Canvas, Color.Black, 0.55f);
        var goldLight = Ui.Mix(Ui.Accent, Color.White, 0.38f);
        var goldDark = Ui.Mix(Ui.Accent, Color.Black, 0.38f);
        using (var back = new LinearGradientBrush(new Rectangle(0, 0, CardWidth, CardHeight), Ui.Mix(Ui.Canvas, Color.Black, 0.3f), night, 90f))
            g.FillRectangle(back, 0, 0, CardWidth, CardHeight);
        PaintArt(g);
        // The panel, as the launcher's news panel: dark glass, a thin gold edge, a rule under the heading.
        var panel = new Rectangle(16, ArtHeight - 14, CardWidth - 33, CardHeight - ArtHeight - 4);
        using (var path = Ui.RoundRect(panel, 5))
        {
            using (var fill = new SolidBrush(Color.FromArgb(214, night)))
                g.FillPath(fill, path);
            using (var edge = new Pen(Color.FromArgb(150, Ui.Accent), 1f))
                g.DrawPath(edge, path);
        }
        using (var path = Ui.RoundRect(new Rectangle(0, 0, CardWidth - 1, CardHeight - 1), 14))
        using (var pen = new Pen(Ui.Border))
            g.DrawPath(pen, path);
        // Heading: the camel on a gold plate, the name spaced out in capitals, the version under it, the percentage at the right.
        int top = panel.Y + 14;
        var tile = new Rectangle(panel.X + 16, top, 46, 46);
        using (var path = Ui.RoundRect(tile, 9))
        using (var brush = new LinearGradientBrush(tile, goldLight, goldDark, 90f))
            g.FillPath(brush, path);
        using (var path = Ui.RoundRect(tile, 9))
        using (var rim = new Pen(Color.FromArgb(120, 255, 240, 200), 1f))
            g.DrawPath(rim, path);
        SroIcons.Draw(g, "camel", new Rectangle(tile.X + 6, tile.Y + 6, 34, 34), Ui.OnAccent);
        Spaced(g, AppPaths.AppName.ToUpperInvariant(), headFont, goldLight, tile.Right + 16, top + 1, 2.6f);
        TextRenderer.DrawText(g, "Sürüm " + Application.ProductVersion, textFont, new Rectangle(tile.Right + 16, top + 27, 260, 20), Ui.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        string message;
        int stepIndex;
        lock (gate)
        {
            message = text;
            stepIndex = step;
        }
        double percent = Math.Max(0, Math.Min(100, shown));
        TextRenderer.DrawText(g, "%" + (int)Math.Round(percent), bigFont, new Rectangle(panel.Right - 110, top - 2, 94, 50), goldLight,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        int rule = top + 58;
        using (var line = new Pen(Color.FromArgb(110, Ui.Accent), 1f))
            g.DrawLine(line, panel.X + 16, rule, panel.Right - 16, rule);
        // Status line and the step counter
        TextRenderer.DrawText(g, message + (message == "Hazır" ? "" : "…"), textFont, new Rectangle(panel.X + 16, rule + 8, panel.Width - 170, 22), Ui.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, "Adım " + Math.Min(total, stepIndex + 1) + " / " + total, smallFont, new Rectangle(panel.Right - 150, rule + 8, 134, 22),
            Ui.TextMuted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        // The bar: a dark track with a gold edge, filled with the launcher's gold, a soft shine running over it.
        var track = new Rectangle(panel.X + 16, rule + 36, panel.Width - 32, 14);
        using (var path = Ui.RoundRect(track, 4))
        {
            using (var hollow = new SolidBrush(Color.FromArgb(230, 20, 14, 10)))
                g.FillPath(hollow, path);
            using (var edge = new Pen(Color.FromArgb(170, Ui.Accent), 1f))
                g.DrawPath(edge, path);
        }
        int filled = (int)Math.Round((track.Width - 4) * percent / 100.0);
        if (filled > 3)
        {
            var fillRect = new Rectangle(track.X + 2, track.Y + 2, filled, track.Height - 4);
            using (var path = Ui.RoundRect(fillRect, 3))
            {
                using (var gold = new LinearGradientBrush(fillRect, goldLight, goldDark, 90f))
                    g.FillPath(gold, path);
                var state = g.Save();
                g.SetClip(path, CombineMode.Intersect);
                int shineX = fillRect.X + (int)((fillRect.Width + 80) * phase) - 40;
                using (var shine = new LinearGradientBrush(new Rectangle(shineX - 30, fillRect.Y, 60, fillRect.Height), Color.FromArgb(0, 255, 255, 255),
                    Color.FromArgb(90, 255, 255, 255), 0f))
                {
                    var blend = new ColorBlend { Colors = new[] { Color.FromArgb(0, 255, 255, 255), Color.FromArgb(110, 255, 255, 255), Color.FromArgb(0, 255, 255, 255) },
                        Positions = new[] { 0f, 0.5f, 1f } };
                    shine.InterpolationColors = blend;
                    g.FillRectangle(shine, shineX - 30, fillRect.Y, 60, fillRect.Height);
                }
                g.Restore(state);
            }
        }
        // The route: the towns on a dotted road, the part already travelled in gold, the camel at the current position.
        int left = panel.X + 40, right = panel.Right - 40, roadY = rule + 92;
        double span = right - left;
        float camelX = (float)(left + span * percent / 100.0);
        using (var road = new Pen(Color.FromArgb(150, Ui.Accent), 1.5f) { DashStyle = DashStyle.Dot })
            g.DrawLine(road, left, roadY, right, roadY);
        if (camelX > left)
            using (var travelled = new Pen(goldLight, 2.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(travelled, left, roadY, camelX, roadY);
        for (int i = 0; i < Cities.Length; i++)
        {
            int x = (int)Math.Round(left + span * i / (Cities.Length - 1));
            bool reached = camelX >= x - 1;
            var diamond = new[] { new Point(x - 6, roadY), new Point(x, roadY - 6), new Point(x + 6, roadY), new Point(x, roadY + 6) };
            if (reached)
                using (var brush = new SolidBrush(goldLight))
                    g.FillPolygon(brush, diamond);
            else
                using (var fill = new SolidBrush(night))
                using (var pen = new Pen(Color.FromArgb(190, Ui.Accent), 1.5f))
                {
                    g.FillPolygon(fill, diamond);
                    g.DrawPolygon(pen, diamond);
                }
            TextRenderer.DrawText(g, Cities[i], cityFont, new Rectangle(x - 60, roadY + 12, 120, 22), reached ? Ui.Text : Ui.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }
        // The camel walks with a small bob; it stands still once the window is ready.
        float bob = percent >= 100 ? 0 : (float)(Math.Sin(phase * Math.PI * 2 * 8) * 1.6);
        SroIcons.Draw(g, "camel", new Rectangle((int)camelX - 15, roadY - 34 + (int)Math.Round(bob), 30, 30), goldLight);
        TextRenderer.DrawText(g, "Karakter belleği yalnızca oyun açıkken ve sen bağlanınca okunur.", smallFont,
            new Rectangle(panel.X, panel.Bottom - 26, panel.Width, 20), Ui.TextMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Dispose();
            nameFont.Dispose();
            textFont.Dispose();
            smallFont.Dispose();
            cityFont.Dispose();
            headFont.Dispose();
            bigFont.Dispose();
        }
        base.Dispose(disposing);
    }
}

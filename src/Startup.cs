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
        "Silkroad.ico", "grim-locations.json", "town-regions.json", "transport-network.json", "return-scrolls.json", "explorer-spots.json",
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
    const int CardWidth = 500, CardHeight = 290;
    readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 16 };
    readonly Font nameFont = new Font("Segoe UI Semibold", 18f), textFont = new Font("Segoe UI", 10.5f), smallFont = new Font("Segoe UI", 9f),
        logoFont = new Font("Segoe UI Semibold", 26f);
    readonly object gate = new object();
    string text = "Başlatılıyor";
    int step, total = Startup.StepNames.Length;
    double shown, target;
    bool finishing;
    double phase;

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

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(Ui.Surface);
        using (var path = Ui.RoundRect(new Rectangle(0, 0, CardWidth - 1, CardHeight - 1), 14))
        using (var pen = new Pen(Ui.Border))
            g.DrawPath(pen, path);
        // Logo tile
        var tile = new Rectangle(36, 36, 64, 64);
        using (var path = Ui.RoundRect(tile, 14))
        using (var brush = new SolidBrush(Ui.Accent))
            g.FillPath(brush, path);
        TextRenderer.DrawText(g, "R", logoFont, tile, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, AppPaths.AppName, nameFont, new Rectangle(116, 36, CardWidth - 150, 36), Ui.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, "Sürüm " + Application.ProductVersion, textFont, new Rectangle(116, 72, CardWidth - 150, 28), Ui.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        string message;
        int stepIndex;
        lock (gate)
        {
            message = text;
            stepIndex = step;
        }
        TextRenderer.DrawText(g, message + (message == "Hazır" ? "" : "…"), textFont, new Rectangle(36, 150, CardWidth - 72, 28), Ui.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        // Progress bar
        var track = new Rectangle(36, 188, CardWidth - 72, 8);
        using (var path = Ui.RoundRect(track, 4))
        using (var brush = new SolidBrush(Ui.Divider))
            g.FillPath(brush, path);
        int fill = (int)Math.Round(track.Width * Math.Max(0, Math.Min(100, shown)) / 100.0);
        if (fill >= 8)
        {
            var bar = new Rectangle(track.X, track.Y, fill, track.Height);
            using (var path = Ui.RoundRect(bar, 4))
            {
                using (var brush = new SolidBrush(Ui.Accent))
                    g.FillPath(brush, path);
                // A soft highlight sweeps along the filled part while the program is working.
                g.SetClip(path);
                int w = Math.Max(40, fill / 4), x = bar.X - w + (int)((bar.Width + w) * phase);
                using (var sweep = new LinearGradientBrush(new Rectangle(x, bar.Y, w, bar.Height), Color.FromArgb(0, 255, 255, 255),
                    Color.FromArgb(120, 255, 255, 255), LinearGradientMode.Horizontal))
                    g.FillRectangle(sweep, x, bar.Y, w, bar.Height);
                g.ResetClip();
            }
        }
        TextRenderer.DrawText(g, "Adım " + Math.Min(total, stepIndex + 1) + " / " + total, smallFont, new Rectangle(36, 206, 200, 22), Ui.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, "%" + (int)Math.Round(Math.Max(0, Math.Min(100, shown))), smallFont, new Rectangle(CardWidth - 36 - 100, 206, 100, 22),
            Ui.TextMuted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, "Karakter belleği yalnızca oyun açıkken ve sen bağlanınca okunur.", smallFont,
            new Rectangle(36, CardHeight - 40, CardWidth - 72, 22), Ui.TextMuted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Dispose();
            nameFont.Dispose();
            textFont.Dispose();
            smallFont.Dispose();
            logoFont.Dispose();
        }
        base.Dispose(disposing);
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Design tokens of the "night caravan" theme: warm leather-brown surfaces, parchment text, one aged-gold accent. Every colour, font and size
// used by the interface comes from here. Rules (see docs/design-system.md): panels are lifted one step from the canvas by a thin
// bronze border, the accent marks primary actions and the chosen page, jade/cinnabar are only for start/stop and status, text is at
// least 4.5:1 against its background and text on an accent fill is the dark lacquer colour (OnAccent), never white.
static class Ui
{
    // The splash palette. The opening screen (Startup.cs) reads these five and nothing else of the surfaces, and it must look the same
    // as before, so they keep their original warm-brown values. The window itself uses the stone palette below (docs/design-msro-style.md).
    internal static readonly Color Canvas = Color.FromArgb(54, 39, 32);
    internal static readonly Color Surface = Color.FromArgb(70, 51, 41);
    internal static readonly Color Border = Color.FromArgb(122, 94, 72);
    internal static readonly Color Text = Color.FromArgb(246, 236, 216);
    internal static readonly Color TextMuted = Color.FromArgb(204, 184, 154);

    // The stone palette (MSRO BOT style): a dark stone-grey page, group boxes one step lighter, thin bronze frames, near-black fields.
    internal static readonly Color Back = Color.FromArgb(27, 28, 28);
    internal static readonly Color Stone = Color.FromArgb(36, 37, 36);
    internal static readonly Color Card = Color.FromArgb(45, 46, 42);
    internal static readonly Color SurfaceAlt = Color.FromArgb(56, 57, 48);
    // Inputs and lists sit sunk into the panel: almost black.
    internal static readonly Color Field = Color.FromArgb(16, 17, 20);
    internal static readonly Color Bronze = Color.FromArgb(110, 94, 56);
    internal static readonly Color BorderStrong = Color.FromArgb(150, 128, 72);
    internal static readonly Color Divider = Color.FromArgb(64, 62, 52);
    // Bronze list rows and ticked tiles; the chosen navigation entry.
    internal static readonly Color RowFill = Color.FromArgb(74, 65, 40);
    internal static readonly Color RowEdge = Color.FromArgb(140, 118, 64);
    internal static readonly Color NavOn = Color.FromArgb(104, 90, 48);
    internal static readonly Color NavOnEdge = Color.FromArgb(190, 164, 96);

    // Text: Ink 11:1, TextSecondary 8.5:1, InkMuted 4.9:1 on Card; Gold is the cream-gold of headings.
    internal static readonly Color Ink = Color.FromArgb(232, 226, 208);
    internal static readonly Color TextSecondary = Color.FromArgb(206, 200, 182);
    internal static readonly Color InkMuted = Color.FromArgb(160, 154, 134);
    internal static readonly Color TextDisabled = Color.FromArgb(118, 114, 100);
    internal static readonly Color Gold = Color.FromArgb(244, 226, 170);

    // Semantic colours: jade and cinnabar fills carry parchment text at 4.5:1 or better; Warning is an amber used as text on dark.
    internal static readonly Color Success = Color.FromArgb(48, 122, 88);
    internal static readonly Color SuccessHover = Color.FromArgb(44, 112, 80);
    internal static readonly Color SuccessTint = Color.FromArgb(52, 86, 66);
    internal static readonly Color Danger = Color.FromArgb(168, 56, 40);
    internal static readonly Color DangerHover = Color.FromArgb(182, 64, 46);
    internal static readonly Color DangerTint = Color.FromArgb(96, 48, 40);
    // The dungeon table: not entered yet = white, entered = yellow.
    internal static readonly Color Fresh = Color.White, Done = Color.FromArgb(255, 214, 64);
    internal static readonly Color Jade = Color.FromArgb(100, 188, 152);
    internal static readonly Color Warning = Color.FromArgb(236, 176, 76);
    internal static readonly Color WarningTint = Color.FromArgb(92, 70, 38);

    // The text colour on an accent fill: the lacquer itself (6.7:1 or better on every accent below).
    internal static readonly Color OnAccent = Color.FromArgb(36, 26, 21);
    // The text colour on a Success / Danger fill.
    internal static readonly Color OnStatus = Color.FromArgb(250, 242, 226);

    // Accent colours the user can pick; each one is also at least 4.7:1 as text on Surface.
    internal static readonly string[] AccentNames = { "Altın", "Yeşim", "Lapis", "Bakır", "Kızıl", "Turkuaz", "Gümüş" };
    static readonly Color[] accents = { Color.FromArgb(218, 166, 72), Color.FromArgb(100, 188, 152), Color.FromArgb(132, 176, 240),
        Color.FromArgb(240, 150, 86), Color.FromArgb(248, 128, 108), Color.FromArgb(76, 194, 202), Color.FromArgb(198, 205, 215) };

    internal static Color Accent = accents[0];
    internal static Color AccentHover, AccentPressed, AccentTint, AccentTintStrong;

    // Spacing (4 px grid)
    internal const int Gap = 6, Pad = 10, Radius = 2, ControlHeight = 26;

    // Typography: Constantia (a humanist serif that ships with Windows) for titles, Segoe UI for text, Consolas for the log and numbers,
    // Segoe MDL2 Assets for icons. Georgia stands in when Constantia is missing.
    static readonly string serif = FontExists("Constantia") ? "Constantia" : "Georgia";
    internal static readonly Font Body = new Font("Segoe UI", 9f);
    internal static readonly Font BodyBold = new Font("Segoe UI Semibold", 9f);
    internal static readonly Font Heading = new Font("Segoe UI Semibold", 9f);
    internal static readonly Font Title = new Font(serif, 13f, FontStyle.Bold);
    internal static readonly Font PageTitle = new Font("Segoe UI Semibold", 15f);
    internal static readonly Font GroupLabel = new Font("Segoe UI", 7.5f);
    internal static readonly Font Small = new Font("Segoe UI", 8.25f);
    internal static readonly Font Mono = new Font("Consolas", 9f);

    static bool FontExists(string name)
    {
        try
        {
            return new System.Drawing.Text.InstalledFontCollection().Families.Any(f => f.Name == name);
        }
        catch
        {
            return false;
        }
    }

    // A stone surface: a very faint vertical gradient with a fixed ±3-level noise tile over it (drawn with code, no image file).
    static TextureBrush noiseBrush;

    static TextureBrush Noise()
    {
        if (noiseBrush == null)
        {
            var tile = new Bitmap(64, 64, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var rnd = new Random(7);
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    int v = rnd.Next(0, 100);
                    tile.SetPixel(x, y, v < 30 ? Color.FromArgb(6, 255, 255, 255) : v < 62 ? Color.FromArgb(12, 0, 0, 0) : Color.FromArgb(0, 0, 0, 0));
                }
            noiseBrush = new TextureBrush(tile, WrapMode.Tile);
        }
        return noiseBrush;
    }

    internal static void PaintStone(Graphics g, Rectangle r, Color baseColor, bool gradient = true)
    {
        if (r.Width <= 0 || r.Height <= 0)
            return;
        if (gradient)
        {
            using (var brush = new LinearGradientBrush(new Rectangle(r.X, r.Y - 1, r.Width, r.Height + 2), Mix(baseColor, Color.White, 0.02f),
                Mix(baseColor, Color.Black, 0.05f), 90f))
                g.FillRectangle(brush, r);
        }
        else
            using (var flat = new SolidBrush(baseColor))
                g.FillRectangle(flat, r);
        var noise = Noise();
        noise.ResetTransform();
        noise.TranslateTransform(r.X % 64, r.Y % 64);
        g.FillRectangle(noise, r);
    }

    // The gold tick of a ticked box, drawn inside r.
    internal static void DrawTick(Graphics g, Rectangle r, Color ink)
    {
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var pen = new Pen(ink, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            g.DrawLines(pen, new[] { new PointF(r.X + r.Width * 0.2f, r.Y + r.Height * 0.54f), new PointF(r.X + r.Width * 0.42f, r.Y + r.Height * 0.76f),
                new PointF(r.X + r.Width * 0.82f, r.Y + r.Height * 0.24f) });
        g.SmoothingMode = old;
    }

    static Font iconFont;
    static bool? iconFontOk;

    static Ui()
    {
        SetAccent(0);
    }

    internal static void SetAccent(int index)
    {
        Accent = accents[Math.Max(0, Math.Min(accents.Length - 1, index))];
        AccentHover = Mix(Accent, Color.White, 0.16f);
        AccentPressed = Mix(Accent, Color.Black, 0.22f);
        AccentTint = Mix(Card, Accent, 0.16f);
        AccentTintStrong = Mix(Card, Accent, 0.30f);
    }

    internal static Color Mix(Color a, Color b, float t)
    {
        return Color.FromArgb(255, (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    // Segoe MDL2 Assets ships with Windows 10 and 11. When it is missing the icons are simply left out.
    internal static bool IconsAvailable
    {
        get
        {
            if (iconFontOk == null)
                iconFontOk = new System.Drawing.Text.InstalledFontCollection().Families.Any(f => f.Name == "Segoe MDL2 Assets");
            return iconFontOk.Value;
        }
    }

    internal static Font IconFont
    {
        get { return iconFont ?? (iconFont = new Font("Segoe MDL2 Assets", 13f)); }
    }

    static Font smallIconFont;

    // The icon size of the page list.
    internal static Font SmallIcon
    {
        get { return smallIconFont ?? (smallIconFont = new Font("Segoe MDL2 Assets", 10.5f)); }
    }

    internal static GraphicsPath RoundRect(Rectangle r, int radius)
    {
        int d = Math.Max(1, radius * 2);
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    // Native controls (scroll bars, combo boxes, spin buttons, check boxes) are drawn by Windows; this asks Windows for its dark variant.
    // It does nothing on systems that lack one, and the control then simply stays light.
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, object> darkened = new System.Runtime.CompilerServices.ConditionalWeakTable<Control, object>();

    internal static void DarkNative(Control c)
    {
        object seen;
        if (darkened.TryGetValue(c, out seen))
            return;
        darkened.Add(c, c);
        string theme = c is ComboBox ? "DarkMode_CFD" : "DarkMode_Explorer";
        Action apply = delegate
        {
            try
            {
                SetWindowTheme(c.Handle, theme, null);
            }
            catch { }
        };
        if (c.IsHandleCreated)
            apply();
        else
            c.HandleCreated += delegate { apply(); };
    }

    // A dark title bar in the colour of the canvas (Windows 10 20H1 and later; ignored elsewhere).
    internal static void DarkCaption(Form f)
    {
        Action apply = delegate
        {
            try
            {
                int on = 1, caption = Canvas.R | (Canvas.G << 8) | (Canvas.B << 16), ink = Text.R | (Text.G << 8) | (Text.B << 16);
                DwmSetWindowAttribute(f.Handle, 20, ref on, 4);
                DwmSetWindowAttribute(f.Handle, 35, ref caption, 4);
                DwmSetWindowAttribute(f.Handle, 36, ref ink, 4);
            }
            catch { }
        };
        if (f.IsHandleCreated)
            apply();
        else
            f.HandleCreated += delegate { apply(); };
    }

    // Lays a control tree out bottom-up once; used for the page shown first after the window was built with layout held back.
    internal static void Settle(Control root)
    {
        foreach (Control child in root.Controls)
            Settle(child);
        root.PerformLayout();
    }

    // Layout is held back for a whole control tree while it changes a lot at once (a page becoming visible, the window being built).
    // Without this every child that turns visible makes its parent lay out again: hundreds of passes for the Hunt page.
    internal static void Freeze(Control root)
    {
        root.SuspendLayout();
        foreach (Control child in root.Controls)
            Freeze(child);
    }

    internal static void Thaw(Control root)
    {
        foreach (Control child in root.Controls)
            Thaw(child);
        root.ResumeLayout(false);
    }

    internal static void DoubleBuffer(Control c)
    {
        try
        {
            typeof(Control).GetProperty("DoubleBuffered", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(c, true, null);
        }
        catch { }
    }

    // Tables: a slightly lifted header, horizontal hairlines only, accent tint for the selected row.
    internal static void StyleGrid(DataGridView g)
    {
        g.EnableHeadersVisualStyles = false;
        g.BorderStyle = BorderStyle.None;
        g.BackgroundColor = Field;
        g.GridColor = Divider;
        g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        g.ColumnHeadersHeight = 34;
        g.RowTemplate.Height = 30;
        g.DefaultCellStyle.BackColor = Field;
        g.DefaultCellStyle.ForeColor = Text;
        g.DefaultCellStyle.Font = Body;
        g.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        g.DefaultCellStyle.SelectionBackColor = RowFill;
        g.DefaultCellStyle.SelectionForeColor = Text;
        g.AlternatingRowsDefaultCellStyle.BackColor = Field;
        g.ColumnHeadersDefaultCellStyle.BackColor = SurfaceAlt;
        g.ColumnHeadersDefaultCellStyle.ForeColor = Gold;
        g.ColumnHeadersDefaultCellStyle.Font = BodyBold;
        g.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        g.ColumnHeadersDefaultCellStyle.SelectionBackColor = SurfaceAlt;
        g.ColumnHeadersDefaultCellStyle.SelectionForeColor = TextSecondary;
        foreach (DataGridViewRow r in g.Rows)
            if (r.DefaultCellStyle.BackColor != SuccessTint)
                r.DefaultCellStyle.BackColor = Color.Empty;
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

// Design tokens of the light theme: every colour, font and size used by the interface comes from here.
// Rules (see docs/design-system.md): white surfaces on a pale canvas, one accent colour for primary actions and
// selection, green/red only for start/stop and status, body text at least 4.5:1 against its background.
static class Ui
{
    // Surfaces
    internal static readonly Color Canvas = Color.FromArgb(243, 245, 249);
    internal static readonly Color Surface = Color.White;
    internal static readonly Color SurfaceAlt = Color.FromArgb(248, 250, 252);
    internal static readonly Color Border = Color.FromArgb(217, 223, 232);
    internal static readonly Color BorderStrong = Color.FromArgb(172, 182, 198);
    internal static readonly Color Divider = Color.FromArgb(230, 234, 241);

    // Text
    internal static readonly Color Text = Color.FromArgb(15, 23, 42);
    internal static readonly Color TextSecondary = Color.FromArgb(71, 85, 105);
    internal static readonly Color TextMuted = Color.FromArgb(89, 101, 121);
    internal static readonly Color TextDisabled = Color.FromArgb(148, 160, 178);

    // Semantic colours (white text on Success/Danger fills is at least 4.5:1)
    internal static readonly Color Success = Color.FromArgb(21, 128, 61);
    internal static readonly Color SuccessHover = Color.FromArgb(22, 101, 52);
    internal static readonly Color SuccessTint = Color.FromArgb(220, 252, 231);
    internal static readonly Color Danger = Color.FromArgb(185, 28, 28);
    internal static readonly Color DangerHover = Color.FromArgb(153, 27, 27);
    internal static readonly Color DangerTint = Color.FromArgb(254, 226, 226);
    internal static readonly Color Warning = Color.FromArgb(180, 83, 9);
    internal static readonly Color WarningTint = Color.FromArgb(254, 243, 199);

    // Accent colours the user can pick; all of them carry white text at 4.5:1 or better.
    internal static readonly string[] AccentNames = { "Mavi", "Zümrüt", "Mor", "Turuncu", "Gül", "Turkuaz", "Grafit" };
    static readonly Color[] accents = { Color.FromArgb(37, 99, 235), Color.FromArgb(4, 120, 87), Color.FromArgb(109, 40, 217),
        Color.FromArgb(194, 65, 12), Color.FromArgb(190, 18, 60), Color.FromArgb(14, 116, 144), Color.FromArgb(51, 65, 85) };

    internal static Color Accent = accents[0];
    internal static Color AccentHover, AccentPressed, AccentTint, AccentTintStrong;

    // Spacing (4 px grid)
    internal const int Gap = 8, Pad = 16, Radius = 8, ControlHeight = 32;

    // Typography: Segoe UI for text, Consolas for the log and numbers, Segoe MDL2 Assets for icons.
    internal static readonly Font Body = new Font("Segoe UI", 10f);
    internal static readonly Font BodyBold = new Font("Segoe UI Semibold", 10f);
    internal static readonly Font Heading = new Font("Segoe UI Semibold", 11f);
    internal static readonly Font Title = new Font("Segoe UI Semibold", 14f);
    internal static readonly Font Small = new Font("Segoe UI", 9f);
    internal static readonly Font Mono = new Font("Consolas", 9.5f);

    static Font iconFont;
    static bool? iconFontOk;

    static Ui()
    {
        SetAccent(0);
    }

    internal static void SetAccent(int index)
    {
        Accent = accents[Math.Max(0, Math.Min(accents.Length - 1, index))];
        AccentHover = Mix(Accent, Color.Black, 0.14f);
        AccentPressed = Mix(Accent, Color.Black, 0.28f);
        AccentTint = Mix(Color.White, Accent, 0.10f);
        AccentTintStrong = Mix(Color.White, Accent, 0.20f);
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

    // Tables: pale header, horizontal hairlines only, accent tint for the selected row.
    internal static void StyleGrid(DataGridView g)
    {
        g.EnableHeadersVisualStyles = false;
        g.BorderStyle = BorderStyle.None;
        g.BackgroundColor = Surface;
        g.GridColor = Divider;
        g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        g.ColumnHeadersHeight = 34;
        g.RowTemplate.Height = 30;
        g.DefaultCellStyle.BackColor = Surface;
        g.DefaultCellStyle.ForeColor = Text;
        g.DefaultCellStyle.Font = Body;
        g.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        g.DefaultCellStyle.SelectionBackColor = AccentTintStrong;
        g.DefaultCellStyle.SelectionForeColor = Text;
        g.ColumnHeadersDefaultCellStyle.BackColor = SurfaceAlt;
        g.ColumnHeadersDefaultCellStyle.ForeColor = TextSecondary;
        g.ColumnHeadersDefaultCellStyle.Font = BodyBold;
        g.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        g.ColumnHeadersDefaultCellStyle.SelectionBackColor = SurfaceAlt;
        g.ColumnHeadersDefaultCellStyle.SelectionForeColor = TextSecondary;
        foreach (DataGridViewRow r in g.Rows)
            if (r.DefaultCellStyle.BackColor != SuccessTint)
                r.DefaultCellStyle.BackColor = Color.Empty;
    }
}

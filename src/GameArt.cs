using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Reflection;

// Pictures taken from the game itself: the golden dragon over the gate of Nanzhang from the launcher's background film (twelve frames, half a second
// apart, and one cut-out for the page list's plate) and the portraits of the seven unique monsters from the game's own monster thumbnails.
// They are embedded files (assets/launcher, assets/uniques); tools/common.ps1 names them Launcher.* and Unique.*.
static class GameArt
{
    internal const int FrameCount = 12;
    internal const double FrameSeconds = 0.5;

    // The uniques by their short name, in the order of the game's list.
    internal static readonly string[] Uniques = { "tiger", "kerberos", "ivy", "uruchi", "isyutaru", "yarkan", "shaitan" };
    internal static readonly string[] UniqueNames = { "Tiger Girl", "Cerberus", "Captain Ivy", "Uruchi", "Isyutaru", "Lord Yarkan", "Demon Shaitan" };

    // Which unique stands beside which page.
    static readonly Dictionary<string, string> pageUniques = new Dictionary<string, string>
    {
        { "Yenilikler", "yarkan" }, { "İstatistik", "isyutaru" }, { "Koruma", "shaitan" }, { "Envanter", "ivy" }, { "Bildirimler", "ivy" },
        { "Av Alanı", "tiger" }, { "Saldırı", "tiger" }, { "Grim Crypt", "kerberos" },
        { "Explorer Yerleri", "uruchi" }, { "Kervan Rotası", "uruchi" }, { "Nanzhang NPC", "uruchi" }, { "Quest", "uruchi" },
        { "Lottery", "ivy" }, { "Çift Hesap · Deneme", "isyutaru" }, { "Güncelleme/Kılavuz", "yarkan" }, { "Log", "yarkan" }
    };

    internal static string UniqueOf(string page)
    {
        string key;
        return page != null && pageUniques.TryGetValue(page, out key) ? key : Uniques[0];
    }

    static readonly Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>();

    // A picture embedded in the program, decoded once and kept; null when it is missing (the caller then draws nothing).
    internal static Bitmap Load(string resource)
    {
        lock (cache)
        {
            Bitmap bitmap;
            if (cache.TryGetValue(resource, out bitmap))
                return bitmap;
            try
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
                    bitmap = stream == null ? null : new Bitmap(new Bitmap(stream));
            }
            catch (Exception)
            {
                bitmap = null;
            }
            cache[resource] = bitmap;
            return bitmap;
        }
    }

    internal static Bitmap Frame(int index)
    {
        return Load("Launcher.frame" + Math.Max(0, Math.Min(FrameCount - 1, index)).ToString("00") + ".jpg");
    }

    internal static Bitmap Plate
    {
        get { return Load("Launcher.plate.jpg"); }
    }

    internal static Bitmap Unique(string key)
    {
        return Load("Unique." + key + ".png");
    }

    // The film at a time (seconds from its first frame): two neighbouring frames blended, the film stopping on its last frame.
    internal static void DrawFilm(Graphics g, Rectangle target, Rectangle source, double seconds)
    {
        double at = Math.Max(0, Math.Min(FrameCount - 1, seconds / FrameSeconds));
        int first = (int)Math.Floor(at);
        float mix = (float)(at - first);
        var a = Frame(first);
        if (a == null)
            return;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(a, target, source.X, source.Y, source.Width, source.Height, GraphicsUnit.Pixel);
        var b = first + 1 < FrameCount ? Frame(first + 1) : null;
        if (b == null || mix <= 0.01f)
            return;
        var matrix = new ColorMatrix { Matrix33 = mix };
        using (var attributes = new ImageAttributes())
        {
            attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
            g.DrawImage(b, target, source.X, source.Y, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        }
    }

    // A unique's portrait in a thin gold frame.
    internal static void DrawPortrait(Graphics g, string key, Rectangle box, Color frame)
    {
        var picture = Unique(key);
        if (picture == null)
            return;
        var old = g.InterpolationMode;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(picture, box, 0, 0, picture.Width, picture.Height, GraphicsUnit.Pixel);
        g.InterpolationMode = old;
        using (var pen = new Pen(frame, 1.5f))
            g.DrawRectangle(pen, box.X, box.Y, box.Width - 1, box.Height - 1);
    }
}

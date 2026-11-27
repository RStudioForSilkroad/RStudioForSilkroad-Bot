using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

// The game's own display options (config\display.ini, read by the game when it starts). With render_in_background=0 the game stops drawing the
// moment its window is no longer the one in front: the picture freezes while the character goes on (measured: the drawing thread uses no CPU at
// all and the logic thread runs about six times slower). That is what a frozen window next to another program looks like; it is the game's
// setting, not a fault of the bot. render_in_background=1 keeps the picture alive.
static class GameDisplay
{
    internal static string IniPath(string gameDirectory)
    {
        return Path.Combine(gameDirectory, "config", "display.ini");
    }

    // true / false as written in the [display] section; null when the key is not there (the game's own default applies).
    internal static bool? ParseRenderInBackground(IEnumerable<string> lines)
    {
        bool inDisplay = false;
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                inDisplay = string.Equals(line, "[display]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inDisplay || line.StartsWith(";") || line.StartsWith("#"))
                continue;
            int eq = line.IndexOf('=');
            if (eq <= 0 || !string.Equals(line.Substring(0, eq).Trim(), "render_in_background", StringComparison.OrdinalIgnoreCase))
                continue;
            string value = line.Substring(eq + 1).Trim().ToLowerInvariant();
            return value == "1" || value == "true" || value == "yes" || value == "on";
        }
        return null;
    }

    // The lines with render_in_background set; every other line stays as it was.
    internal static List<string> WithRenderInBackground(IEnumerable<string> lines, bool on)
    {
        var result = lines.ToList();
        string text = "render_in_background=" + (on ? "1" : "0");
        bool inDisplay = false;
        int lastOfDisplay = -1;
        for (int i = 0; i < result.Count; i++)
        {
            string line = result[i].Trim();
            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                inDisplay = string.Equals(line, "[display]", StringComparison.OrdinalIgnoreCase);
                if (inDisplay)
                    lastOfDisplay = i;
                continue;
            }
            if (!inDisplay)
                continue;
            if (line.Length > 0)
                lastOfDisplay = i;
            int eq = line.IndexOf('=');
            if (!line.StartsWith(";") && !line.StartsWith("#") && eq > 0 &&
                string.Equals(line.Substring(0, eq).Trim(), "render_in_background", StringComparison.OrdinalIgnoreCase))
            {
                result[i] = text;
                return result;
            }
        }
        if (lastOfDisplay >= 0)
            result.Insert(lastOfDisplay + 1, text);
        else
        {
            result.Add("[display]");
            result.Add(text);
        }
        return result;
    }

    internal static bool? RenderInBackground(string gameDirectory)
    {
        try
        {
            string path = IniPath(gameDirectory);
            return File.Exists(path) ? ParseRenderInBackground(File.ReadAllLines(path)) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Writes the option into the game's file; the first time the original is kept next to it as display.ini.bak.
    internal static void SetRenderInBackground(string gameDirectory, bool on)
    {
        string path = IniPath(gameDirectory);
        var lines = File.Exists(path) ? File.ReadAllLines(path) : new string[0];
        if (File.Exists(path) && !File.Exists(path + ".bak"))
            File.Copy(path, path + ".bak");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, string.Join(Environment.NewLine, WithRenderInBackground(lines, on)) + Environment.NewLine, new UTF8Encoding(false));
    }
}

sealed partial class MemorySelectV3Form
{
    Label displayState;

    // The folder of the selected (or connected) game; null when no game is running.
    string GameDirectory()
    {
        try
        {
            if (reader != null)
                return Path.GetDirectoryName(reader.Process.MainModule.FileName);
            var selected = processes.SelectedItem as ProcessItem;
            if (selected == null)
                return null;
            using (var process = Process.GetProcessById(selected.Id))
                return Path.GetDirectoryName(process.MainModule.FileName);
        }
        catch (Exception)
        {
            return null;
        }
    }

    void RefreshDisplayState()
    {
        if (displayState == null)
            return;
        string dir = GameDirectory();
        if (dir == null)
        {
            displayState.Text = "Arka planda çizim: oyun seçilince okunur.";
            return;
        }
        bool? on = GameDisplay.RenderInBackground(dir);
        displayState.Text = on == true ? "Arka planda çizim: AÇIK. Oyun başka bir pencerenin arkasındayken de çizer." :
            on == false ? "Arka planda çizim: KAPALI. Oyun öndeki pencere olmayınca görüntüsü donar (oyunun kendi ayarı). Aç, sonra oyunu yeniden başlat." :
            "Arka planda çizim: oyunun ayar dosyası okunamadı.";
    }

    void EnableBackgroundRender()
    {
        string dir = GameDirectory();
        if (dir == null)
            throw new InvalidOperationException("Önce listeden bir oyun işlemi seç.");
        GameDisplay.SetRenderInBackground(dir, true);
        RefreshDisplayState();
        Log("Arka planda çizim açıldı (config\\display.ini · render_in_background=1); oyunu yeniden başlatınca geçerli olur.");
        status.Text = "Arka planda çizim açıldı; oyunu kapatıp yeniden aç.";
    }

    // Said once per connection: a frozen picture is not a bot fault when the game is told not to draw in the background.
    void WarnBackgroundRender()
    {
        RefreshDisplayState();
        string dir = GameDirectory();
        if (dir != null && GameDisplay.RenderInBackground(dir) == false)
            Log("Oyun arka planda çizmiyor (config\\display.ini · render_in_background=0): pencere öndeyken değilse görüntü donar, karakter ilerlemeye devam eder. Koruma sayfasındaki \"Arka planda çizimi aç\" ile açıp oyunu yeniden başlat.");
    }
}

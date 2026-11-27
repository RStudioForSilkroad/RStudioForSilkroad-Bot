using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

// The lower part of the Grim page: the picture of the chosen dungeon at one fixed size, and a card with what is known about it. The routes and the
// learned paths of the old world map live here and in the table now.

// The dungeon's picture in a frame, always the same size: the picture is scaled to fit, never dragged or zoomed.
sealed class DungeonShot : Control
{
    internal static readonly Size Fixed = new Size(352, 198);
    Bitmap picture;
    string missing;

    internal DungeonShot()
    {
        Size = new Size(Fixed.Width + 2, Fixed.Height + 2);
        MinimumSize = MaximumSize = Size;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        TabStop = false;
    }

    internal void LoadResource(string name)
    {
        missing = null;
        try
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (stream == null)
                    throw new IOException(name);
                using (var image = Image.FromStream(stream))
                {
                    if (picture != null)
                        picture.Dispose();
                    picture = new Bitmap(image);
                }
            }
        }
        catch (Exception)
        {
            if (picture != null)
                picture.Dispose();
            picture = null;
            missing = "Bu dungeonun görseli yok.";
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ui.Field);
        if (picture != null)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            double scale = Math.Min((double)Fixed.Width / picture.Width, (double)Fixed.Height / picture.Height);
            int w = (int)(picture.Width * scale), h = (int)(picture.Height * scale);
            g.DrawImage(picture, 1 + (Fixed.Width - w) / 2, 1 + (Fixed.Height - h) / 2, w, h);
        }
        else if (missing != null)
            TextRenderer.DrawText(g, missing, Ui.Body, ClientRectangle, Ui.InkMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        using (var pen = new Pen(Ui.Bronze))
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && picture != null)
        {
            picture.Dispose();
            picture = null;
        }
        base.Dispose(disposing);
    }
}

// What the table knows about the chosen dungeon, in one card: where it is, how to get there, and where it stands in the run.
sealed class DungeonDetails : Control
{
    string title = "", state = "";
    readonly List<KeyValuePair<string, string>> rows = new List<KeyValuePair<string, string>>();
    bool done;

    internal DungeonDetails()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        TabStop = false;
    }

    internal string Title
    {
        get { return title; }
    }

    internal void Set(string heading, string status, bool visited, IEnumerable<KeyValuePair<string, string>> facts)
    {
        title = heading ?? "";
        state = status ?? "";
        done = visited;
        rows.Clear();
        if (facts != null)
            rows.AddRange(facts);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ui.Card);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var pen = new Pen(Ui.Bronze))
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        const TextFormatFlags Flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        int x = 16, y = 12, width = Width - 32;
        if (title.Length == 0)
        {
            TextRenderer.DrawText(g, "Tablodan bir dungeon seç.", Ui.Body, new Rectangle(x, y, width, 24), Ui.InkMuted, Flags);
            return;
        }
        TextRenderer.DrawText(g, title, Ui.Title, new Rectangle(x, y, width, 30), done ? Ui.Done : Ui.Ink, Flags);
        y += 34;
        if (state.Length > 0)
        {
            var chip = new Rectangle(x, y, Math.Min(width, TextRenderer.MeasureText(state, Ui.BodyBold).Width + 20), 22);
            using (var path = Ui.RoundRect(chip, 11))
            {
                using (var fill = new SolidBrush(Ui.Mix(Ui.Card, done ? Ui.Done : Ui.Accent, 0.22f)))
                    g.FillPath(fill, path);
                using (var pen = new Pen(Ui.Mix(Ui.Card, done ? Ui.Done : Ui.Accent, 0.6f)))
                    g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, state, Ui.BodyBold, chip, done ? Ui.Done : Ui.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            y += 32;
        }
        using (var line = new Pen(Ui.Divider))
            g.DrawLine(line, x, y, Width - 16, y);
        y += 8;
        foreach (var pair in rows)
        {
            TextRenderer.DrawText(g, pair.Key, Ui.Body, new Rectangle(x, y, 96, 24), Ui.InkMuted, Flags);
            TextRenderer.DrawText(g, pair.Value, Ui.Body, new Rectangle(x + 104, y, width - 104, 24), Ui.Ink, Flags);
            y += 26;
        }
    }
}

sealed partial class MemorySelectV3Form
{
    readonly DungeonShot dungeonShot = new DungeonShot();
    readonly DungeonDetails dungeonDetails = new DungeonDetails();
    Label pathInfo;
    ChipBox learnBox;
    // code -> "Hotan'dan 2400 birim · 1 ışınlanma", planned in the background; null until planned
    Dictionary<string, string> routeSummaries;
    bool routeBusy;
    static readonly KeyValuePair<int, string>[] RouteTowns = { new KeyValuePair<int, string>(20, "Constantinople"),
        new KeyValuePair<int, string>(25, "Samarkand"), new KeyValuePair<int, string>(5, "Hotan"), new KeyValuePair<int, string>(2, "Donwhang"),
        new KeyValuePair<int, string>(1, "Jangan"), new KeyValuePair<int, string>(252, "Nanzhang") };

    // The picture and the card at the bottom of the page.
    void BuildGrimBottom(Control host)
    {
        var row = new Panel { Dock = DockStyle.Fill };
        dungeonShot.Location = new Point(0, 0);
        dungeonDetails.Location = new Point(DungeonShot.Fixed.Width + 2 + 12, 0);
        dungeonDetails.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        dungeonDetails.Size = new Size(Math.Max(300, host.Width - dungeonDetails.Left), DungeonShot.Fixed.Height + 2);
        row.Controls.Add(dungeonShot);
        row.Controls.Add(dungeonDetails);
        row.Resize += delegate
        {
            dungeonDetails.Width = Math.Max(300, row.ClientSize.Width - dungeonDetails.Left);
        };
        host.Controls.Add(row);
    }

    // The tools of the learned paths (stairs, gates and building interiors are not in the map data: walk them once by hand, with the mark on).
    void BuildLearnTools(Control host)
    {
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(Ui.Pad, 0, Ui.Pad, 6) };
        learnBox = new ChipBox { Text = "Yol öğret", Margin = new Padding(0, 0, 8, 0) };
        learnBox.CheckedChanged += delegate { Safe(delegate { SetRecording(learnBox.Checked); }); };
        tools.Controls.Add(learnBox);
        tools.Controls.Add(Btn("Yolları kopyala", ExportLearned, 120, false));
        tools.Controls.Add(Btn("Yolları yapıştır", ImportLearned, 128, false));
        tools.Controls.Add(Btn("Son yolu sil", DeleteLastLearned, 100, false));
        pathInfo = new Label { AutoSize = true, Tag = "muted", Margin = new Padding(8, 6, 0, 0), Text = "" };
        tools.Controls.Add(pathInfo);
        var hint = new Label { AutoSize = true, Tag = "muted", Margin = new Padding(16, 6, 0, 0),
            Text = "Yol öğret: işaretle, karakteri kapı ya da merdivenden elle yürüt, işareti kaldır." };
        tools.Controls.Add(hint);
        host.Controls.Add(tools);
        ShowLearned();
    }

    void DeleteLastLearned()
    {
        if (settings == null || settings.LearnedPaths == null || settings.LearnedPaths.Count == 0)
            throw new InvalidOperationException("Silinecek, kendi öğrettiğin yol yok.");
        settings.LearnedPaths.RemoveAt(settings.LearnedPaths.Count - 1);
        try
        {
            settings.Save();
        }
        catch (Exception) { }
        Log("Son öğretilen yol silindi · kalan " + settings.LearnedPaths.Count);
        ShowLearned();
        RefreshDungeonDetails();
    }

    // The card for the chosen dungeon.
    void RefreshDungeonDetails()
    {
        var d = SelectedDungeon();
        if (d == null)
        {
            dungeonDetails.Set(null, null, false, null);
            return;
        }
        string id = d.Code + "|" + d.Mode;
        bool visited = visitedDungeons.Contains(id);
        int place = queue.FindIndex(x => x.QueueKey == d.QueueKey);
        string state = visited ? "Girildi" : place >= 0 ? (queueRunning && place == 0 ? "Şimdi çalışıyor" : "Sırada " + (place + 1) + ".") : "Girilmedi";
        string route;
        if (routeSummaries != null && routeSummaries.TryGetValue(d.Code, out route))
        { }
        else
            route = routeSummaries == null ? "hesaplanıyor…" : "rota bulunamadı";
        dungeonDetails.Set(d.Code + " · " + d.Name, state, visited, new[]
        {
            new KeyValuePair<string, string>("Mod", d.Mode),
            new KeyValuePair<string, string>("Şehir", d.Area),
            new KeyValuePair<string, string>("Giriş", "X " + Math.Round(d.X) + " · Y " + Math.Round(d.Y)),
            new KeyValuePair<string, string>("Rota", route)
        });
        RefreshRouteColumn();
    }

    // The table's route column: one line per dungeon, from the summaries.
    void RefreshRouteColumn()
    {
        if (!dungeonGrid.Columns.Contains("Rota"))
            return;
        foreach (DataGridViewRow row in dungeonGrid.Rows)
        {
            var d = row.Tag as DungeonLocation;
            if (d == null)
                continue;
            string text;
            row.Cells["Rota"].Value = routeSummaries == null ? "…" : routeSummaries.TryGetValue(d.Code, out text) ? text : "—";
        }
    }

    // Plans the road to every dungeon entrance from its best town (teleport hops included), once in the background; the table and the card
    // show it. A new learned path plans it again.
    void EnsureRouteSummaries()
    {
        if (routeSummaries != null || routeBusy)
            return;
        routeBusy = true;
        var targets = V3Data.Dungeons.GroupBy(d => d.Code).Select(g => g.First()).OrderBy(d => d.Code).ToList();
        System.Threading.ThreadPool.QueueUserWorkItem(delegate
        {
            var found = new Dictionary<string, string>();
            foreach (var d in targets)
            {
                try
                {
                    var goal = d.Entrance();
                    float ground;
                    if (ExpeditionRules.TryGround(goal, out ground))
                        goal.Height = ground;
                    string best = null;
                    double bestCost = double.MaxValue;
                    foreach (var town in RouteTowns)
                    {
                        var gate = ExpeditionRules.Network.Gates.FirstOrDefault(x => x.Id == town.Key);
                        if (gate == null)
                            continue;
                        try
                        {
                            var start = gate.Point();
                            double walk = 0, px = start.X, py = start.Y, cost = 0;
                            int hops = 0;
                            foreach (var step in ExpeditionRules.Plan(start, goal))
                            {
                                if (step.From != null)
                                {
                                    var f = step.From.Point();
                                    double leg = Math.Sqrt((f.X - px) * (f.X - px) + (f.Y - py) * (f.Y - py));
                                    walk += leg;
                                    cost += leg + RoadMesh.HopCost;
                                    hops++;
                                    px = f.X;
                                    py = f.Y;
                                }
                                var q = step.To.Point();
                                if (step.From == null)
                                {
                                    double leg = Math.Sqrt((q.X - px) * (q.X - px) + (q.Y - py) * (q.Y - py));
                                    walk += leg;
                                    cost += leg;
                                }
                                px = q.X;
                                py = q.Y;
                            }
                            walk += Math.Sqrt((goal.X - px) * (goal.X - px) + (goal.Y - py) * (goal.Y - py));
                            if (cost < bestCost)
                            {
                                bestCost = cost;
                                best = town.Value + "'dan " + (int)walk + " birim" + (hops > 0 ? " · " + hops + " ışınlanma" : "");
                            }
                        }
                        catch (Exception) { }
                    }
                    if (best != null)
                        found[d.Code] = best;
                }
                catch (Exception) { }
            }
            try
            {
                BeginInvoke(new Action(delegate
                {
                    routeBusy = false;
                    routeSummaries = found;
                    RefreshRouteColumn();
                    RefreshDungeonDetails();
                }));
            }
            catch (Exception) { }
        });
    }
}

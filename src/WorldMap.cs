using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

// The game's own world map (stitched from its minimap tiles) is still the picture behind the Explorer and the Nanzhang pages; this is its
// coordinate system. Zoom 6, 4x4 regions per tile, so world coordinates map to pixels exactly: pixel = coordinate / 6 + offset, Y flipped.
static class WorldMapView
{
    internal const double AX = 1.0 / 6, AY = 0.0, AC = 2848.0, BX = 0.0, BY = -1.0 / 6, BC = 704.0;
    internal const string ResourceName = "world-map-game.jpg";

    internal static PointF ToImage(double x, double y)
    {
        return new PointF((float)(AX * x + AY * y + AC), (float)(BX * x + BY * y + BC));
    }
}


sealed partial class MemorySelectV3Form
{
    // The client's walkability query gives no 0/1 answer for points at different heights (stairs, ramps); the walker then lets the
    // client climb and relies on its progress check, instead of stopping on the unexpected result.
    // The client gives no 0/1 answer when the two points differ in height (stairs, ramps). While travelling, a short steep step is trusted
    // (the map now models stairs and walls, and the walker still checks progress); a longer or flat step with that answer still stops the walk.

    static Func<Position, Position, bool> ClimbSafeProbe(TargetSender sender, bool allowed)
    {
        return delegate(Position a, Position b)
        {
            try
            {
                return sender.NavigationProbe(a, b);
            }
            catch (RemoteBackoffException)
            {
                // no answer was asked for: never read this as a slope step that may be walked
                throw;
            }
            catch (IOException)
            {
                if (allowed && Math.Abs(a.Height - b.Height) > 8 && a.Distance(b) <= 40)
                    return true;
                throw;
            }
        };
    }

    // ---- Learned passages: stairs, gates and building interiors are not in the map data, so the user walks them once by hand. ----
    bool recording;
    readonly List<Position> recorded = new List<Position>();

    static List<Position> ParseLearned(string text)
    {
        var points = new List<Position>();
        foreach (string item in (text ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var part = item.Split(',');
            double x, y, h;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (part.Length == 3 && double.TryParse(part[0], System.Globalization.NumberStyles.Float, inv, out x) &&
                double.TryParse(part[1], System.Globalization.NumberStyles.Float, inv, out y) && double.TryParse(part[2],
                System.Globalization.NumberStyles.Float, inv, out h))
                points.Add(V2Rules.Center(x, y, (float)h));
        }
        return points;
    }

    // The number of learned paths is shown next to the learning buttons of the Grim page; the routes are planned again with them.
    void ShowLearned()
    {
        if (settings == null || settings.LearnedPaths == null)
            return;
        int own = settings.LearnedPaths.Count, shipped = AllLearned().Count - own;
        if (pathInfo != null)
            pathInfo.Text = "Öğrenilmiş yol: " + own + " senin · " + shipped + " hazır";
        routeSummaries = null;
    }

    void SetRecording(bool on)
    {
        if (on)
        {
            recorded.Clear();
            recording = true;
            Log("Yol öğrenme başladı: karakteri elle yürüt, bitince işareti kaldır.");
            return;
        }
        recording = false;
        if (recorded.Count < 2)
        {
            Log("Öğrenilen yol çok kısa; kaydedilmedi.");
            return;
        }
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        settings.LearnedPaths.Add(string.Join(";", recorded.Select(p => p.X.ToString("F1", inv) + "," + p.Y.ToString("F1",
            inv) + "," + p.Height.ToString("F1", inv))));
        while (settings.LearnedPaths.Count > 60)
            settings.LearnedPaths.RemoveAt(0);
        try
        {
            settings.Save();
        }
        catch (Exception) { }
        Log("Yol öğrenildi · " + recorded.Count + " nokta · X " + Math.Round(recorded[0].X) + " Y " + Math.Round(recorded[0].Y) +
            " → X " + Math.Round(recorded[recorded.Count - 1].X) + " Y " + Math.Round(recorded[recorded.Count - 1].Y));
        ShowLearned();
    }

    void RecordPath()
    {
        if (!recording || snapshot == null || snapshot.Player == null || !snapshot.Player.Outside)
            return;
        if (recorded.Count == 0 || recorded[recorded.Count - 1].Distance(snapshot.Player) >= 6)
            recorded.Add(snapshot.Player.Copy());
    }

    sealed class LearnedItem
    {
        internal string Text;
        internal bool Builtin;
    }

    static string LearnedKey(string text)
    {
        return text.Substring(0, Math.Min(40, text.Length)) + "|" + text.Length;
    }

    // Paths shipped with the bot (walked once by hand) that every user starts with; each user may hide them and add their own.

    static List<string> DefaultLearned()
    {
        var list = new List<string>();
        try
        {
            using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("learned-defaults.txt"))
                using (var reader = new StreamReader(stream))
                    foreach (string line in reader.ReadToEnd().Split(new[] { (char)10, (char)13 }, StringSplitOptions.RemoveEmptyEntries))
                        if (line.Contains(","))
                            list.Add(line.Trim());
        }
        catch (Exception) { }
        return list;
    }

    List<LearnedItem> AllLearned()
    {
        var items = new List<LearnedItem>();
        if (settings == null || settings.LearnedPaths == null)
            return items;
        foreach (string text in settings.LearnedPaths)
            items.Add(new LearnedItem { Text = text });
        var ignored = settings.IgnoredDefaults ?? new List<string>();
        foreach (string text in DefaultLearned())
            if (!ignored.Contains(LearnedKey(text)))
                items.Add(new LearnedItem { Text = text, Builtin = true });
        return items;
    }

    // Friends can swap their own paths through the clipboard (one path per line).

    void ExportLearned()
    {
        if (settings.LearnedPaths.Count == 0)
        {
            Log("Dışa aktarılacak kendi öğrettiğin yol yok.");
            return;
        }
        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, settings.LearnedPaths));
            Log("Öğrenilen yollar panoya kopyalandı · " + settings.LearnedPaths.Count + " yol");
        }
        catch (Exception)
        {
            Log("Pano kullanılamadı.");
        }
    }

    void ImportLearned()
    {
        string text;
        try
        {
            text = Clipboard.GetText();
        }
        catch (Exception)
        {
            text = null;
        }
        int added = 0;
        foreach (string line in (text ?? "").Split(new[] { (char)10, (char)13 }, StringSplitOptions.RemoveEmptyEntries))
        {
            string path = line.Trim();
            if (ParseLearned(path).Count < 2 || settings.LearnedPaths.Contains(path) || DefaultLearned().Contains(path))
                continue;
            settings.LearnedPaths.Add(path);
            added++;
        }
        if (added > 0)
        {
            try
            {
                settings.Save();
            }
            catch (Exception) { }
            ShowLearned();
        }
        Log(added > 0 ? "Panodan " + added + " yol eklendi." : "Panoda eklenecek yeni yol bulunamadı.");
    }

    sealed class LearnedChain
    {
        internal List<Position> Points = new List<Position>();
        internal double Length;

        internal Position Start
        {
            get { return Points[0]; }
        }

        internal Position End
        {
            get { return Points[Points.Count - 1]; }
        }
    }

    // One point about every 24 units (plus the points where the ground height changes quickly, i.e. stairs).

    static List<Position> Thin(List<Position> points)
    {
        var kept = new List<Position> { points[0] };
        for (int i = 1; i < points.Count - 1; i++)
            if (kept[kept.Count - 1].Distance(points[i]) >= 24 || Math.Abs(points[i].Height - kept[kept.Count - 1].Height) >= 40)
                kept.Add(points[i]);
        kept.Add(points[points.Count - 1]);
        return kept;
    }

    static double TrackLength(List<Position> points)
    {
        double length = 0;
        for (int i = 1; i < points.Count; i++)
            length += points[i - 1].Distance(points[i]);
        return length;
    }

    // Walking cost of a planned route; a teleport counts as 180 units.

    static double PlanLength(Position from, List<TransitStep> plan)
    {
        double length = 0;
        var at = from;
        foreach (var step in plan)
        {
            if (step.From != null)
            {
                var gate = step.From.Point();
                length += at.Distance(gate) + RoadMesh.HopCost;
                at = step.To.Point();
                continue;
            }
            var to = step.To.Point();
            length += at.Distance(to);
            at = to;
        }
        return length;
    }

    static Position PlanEnd(Position from, List<TransitStep> plan)
    {
        return plan.Count == 0 ? from : plan[plan.Count - 1].To.Point();
    }

    // Compares the map route with the routes the user walked by hand (alone or two in a row, either direction) and keeps the shortest.

    List<TransitStep> ChooseLearned(Position from, Position goal, List<TransitStep> meshPlan)
    {
        var paths = new List<List < Position>>();
        foreach (var item in AllLearned())
        {
            var points = ParseLearned(item.Text);
            if (points.Count >= 2)
                paths.Add(Thin(points));
        }
        if (paths.Count == 0)
            return meshPlan;
        var chains = new List<LearnedChain>();
        for (int a = 0; a < paths.Count; a++)
            for (int da = 0; da < 2; da++)
            {
                var first = da == 0 ? paths[a] : Enumerable.Reverse(paths[a]).ToList();
                chains.Add(new LearnedChain { Points = first, Length = TrackLength(first) });
                for (int b = 0; b < paths.Count; b++)
                    if (b != a)
                        for (int db = 0; db < 2; db++)
                        {
                            var second = db == 0 ? paths[b] : Enumerable.Reverse(paths[b]).ToList();
                            if (first[first.Count - 1].Distance(second[0]) > 90)
                                continue;
                            var joined = new List<Position>(first);
                            joined.AddRange(second);
                            chains.Add(new LearnedChain { Points = joined, Length = TrackLength(joined) });
                        }
            }
        double meshCost = PlanLength(from, meshPlan) + 3 * PlanEnd(from, meshPlan).Distance(goal);
        double bestCost = meshCost;
        LearnedChain best = null;
        List<TransitStep> bestLead = null, bestTail = null;
        var cache = new Dictionary<string, List < TransitStep>>();
        Func<Position, Position, List < TransitStep>> plan = (s, g) =>
        {
            string key = (int)s.X + "," + (int)s.Y + ">" + (int)g.X + "," + (int)g.Y;
            List<TransitStep> found;
            if (cache.TryGetValue(key, out found))
                return found;
            try
            {
                found = PlanRoad(s, g);
            }
            catch (Exception)
            {
                found = null;
            }
            cache[key] = found;
            return found;
        };
        foreach (var chain in chains)
        {
            double leadGap = from.Distance(chain.Start), tailGap = chain.End.Distance(goal);
            bool leadFixed = leadGap <= 140, tailFixed = tailGap <= 140;
            if (!leadFixed && !tailFixed)
                continue;
            double cost = 0.85 * chain.Length;
            if (cost + leadGap + tailGap >= bestCost)
                continue;
            List<TransitStep> lead = null, tail = null;
            if (leadFixed)
                cost += leadGap;
            else
            {
                lead = plan(from, chain.Start);
                if (lead == null)
                    continue;
                cost += PlanLength(from, lead) + 3 * PlanEnd(from, lead).Distance(chain.Start);
            }
            if (tailFixed)
                cost += tailGap;
            else
            {
                tail = plan(chain.End, goal);
                if (tail == null)
                    continue;
                cost += PlanLength(chain.End, tail) + 3 * PlanEnd(chain.End, tail).Distance(goal);
            }
            if (cost < bestCost)
            {
                bestCost = cost;
                best = chain;
                bestLead = lead;
                bestTail = tail;
            }
        }
        if (best == null)
            return meshPlan;
        var result = new List<TransitStep>();
        if (bestLead != null)
            result.AddRange(bestLead);
        var last = bestLead == null ? from : PlanEnd(from, bestLead);
        foreach (var p in best.Points)
        {
            if (last.Distance(p) < 6)
                continue;
            result.Add(new TransitStep { CellId = -1, To = new TransitGate { Code = "Öğrenilmiş yol", X = p.X, Y = p.Y,
                Height = p.Height } });
            last = p;
        }
        if (bestTail != null)
            result.AddRange(bestTail);
        Log("Öğrenilmiş yol seçildi · yaklaşık " + (int)bestCost + " birim (harita rotası " + (int)meshCost + ")");
        return result;
    }
}

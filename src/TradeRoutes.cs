using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

// A trader NPC the user selected once in game. Positions of the three trade-town traders are not shipped with the bot:
// they are learned from the client (the NPC the user has selected) and kept in trade-npcs.json.
sealed class TradeNpc
{
    public string Town;
    public double X, Y;
    public uint Model;
}

// Caravan routes between the trade towns. Trade goods cannot be teleported or shipped, so these routes are planned over roads only.
static class TradeRoutes
{
    internal static readonly string[] Towns = { "Hotan", "Jangan", "Donwhang", "Samarkand", "Constantinople", "Nanzhang", "Jangan Pass" };
    internal static readonly string[][] Routes =
    {
        new[] { "Hotan", "Jangan" }, new[] { "Jangan", "Hotan" }, new[] { "Hotan", "Donwhang" }, new[] { "Donwhang", "Hotan" },
        new[] { "Jangan", "Donwhang" }, new[] { "Donwhang", "Jangan" },
        new[] { "Samarkand", "Constantinople" }, new[] { "Constantinople", "Samarkand" },
        // Nanzhang: on foot along the road marked on the world map (see JanganNanzhangRoad), not by the gate teleport.
        new[] { "Jangan", "Nanzhang" }, new[] { "Nanzhang", "Jangan" },
        // Jangan Pass: the road walked by hand once and kept as way points (jangan-pass-road.json), in both directions.
        new[] { "Jangan", "Jangan Pass" }, new[] { "Jangan Pass", "Jangan" }
    };

    internal static string StorePath
    {
        get { return Path.Combine(AppPaths.DataRoot, "trade-npcs.json"); }
    }

    internal static string ScriptFolder
    {
        get { return Path.Combine(AppPaths.DataRoot, "scripts"); }
    }

    // The trade towns by their short names; null for anywhere else.
    internal static string TownKey(Position p)
    {
        if (InPass(p))
            return "Jangan Pass";
        string name = V3Data.Town(p);
        if (name == null)
            return null;
        if (name.IndexOf("Donwhang", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Donwhang";
        // The city itself is the "Hotan Kingdom" sector, its surroundings are "Hotan".
        if (name.IndexOf("Hotan", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Hotan";
        return Towns.Contains(name) ? name : null;
    }

    internal static Dictionary<string, TradeNpc> Load(string path)
    {
        var result = new Dictionary<string, TradeNpc>();
        try
        {
            if (!File.Exists(path))
                return result;
            var list = new JavaScriptSerializer().Deserialize<List<TradeNpc>>(File.ReadAllText(path, Encoding.UTF8));
            foreach (var npc in list)
                if (npc != null && Towns.Contains(npc.Town))
                    result[npc.Town] = npc;
        }
        catch (Exception)
        {
            // A damaged file only means the NPCs have to be saved again.
        }
        return result;
    }

    // The traders the bot knows without being told: the NPC the Jangan Pass road walk ended at (the last one selected on it). The user's own
    // saved one for the same stop wins.
    internal static Dictionary<string, TradeNpc> WithDefaults(Dictionary<string, TradeNpc> saved)
    {
        if (!saved.ContainsKey("Jangan Pass"))
            saved["Jangan Pass"] = new TradeNpc { Town = "Jangan Pass", X = 6045, Y = -285, Model = 60850 };
        return saved;
    }

    internal static void Save(string path, Dictionary<string, TradeNpc> npcs)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, new JavaScriptSerializer().Serialize(npcs.Values.OrderBy(n => n.Town).ToList()), new UTF8Encoding(false));
    }

    internal static Position PositionOf(TradeNpc npc)
    {
        var p = V2Rules.Center(npc.X, npc.Y, 0);
        float height;
        if (ExpeditionRules.TryGround(p, out height))
            p.Height = height;
        return p;
    }

    // Road-only plan: no teleport, ferry or flyship hop.
    // Jangan's walls. The caravan road leaves and enters the city by the south gate. The map data does not know where the gate opens:
    // it only prices the wall, so a start in the north of the city would leave by the east gate and loop around the outside.
    // The gate has three arches, each about 6 units wide (x about 6418, 6434, 6450; the game's own walkability query confirms them).
    // The route is led straight through the west arch by three points on one line: inside the wall, in the arch, and a little way out.
    internal const double JanganLeft = 6170, JanganRight = 6675, JanganBottom = 955, JanganTop = 1350, GateX = 6418;
    internal static readonly double[] GateY = { 985, 935, 880 };

    internal static bool InJangan(Position p)
    {
        return p != null && p.X >= JanganLeft && p.X <= JanganRight && p.Y >= JanganBottom && p.Y <= JanganTop;
    }

    static Position GatePoint(double y)
    {
        return GatePoint(GateX, y);
    }

    static Position GatePoint(double x, double y)
    {
        var p = V2Rules.Center(x, y, 0);
        float height;
        if (ExpeditionRules.TryGround(p, out height))
            p.Height = height;
        return p;
    }

    // The arch points in the order the route passes them when it crosses Jangan's wall (one end inside the city, the other
    // outside), otherwise null. Leaving: inside, arch, outside. Entering: the reverse.
    internal static Position[] GatePoints(Position start, Position goal)
    {
        bool from = InJangan(start), to = InJangan(goal);
        if (from == to)
            return null;
        var points = GateY.Select(y => GatePoint(y)).ToArray();
        if (!from)
            Array.Reverse(points);
        return points;
    }

    static TransitStep GateStep(Position p)
    {
        return new TransitStep { CellId = -1, To = new TransitGate { Code = "Güney kapı", X = p.X, Y = p.Y, Height = p.Height } };
    }

    // The road from Jangan to Nanzhang as marked on the game's world map, in the order walked from Jangan: it leaves the city's east side, runs
    // down over Jangan Pass and reaches Nanzhang's east gate (Gate Guard Ma Teng stands at 6100, -1711). The map points were converted with the
    // scale between the two city icons; each leg is planned over the road network separately because the network does not join the two ends.
    // The road is cut by the Tiger Rim cliff (Blightwood, X 6944 / Y -770): the north part from Jangan ends on the cliff top (height about 735),
    // the south part starts at the Climbers Camp below it (height about 483). The two are joined only by the climbers' teleport: Cliff Warden
    // Lu Feng on the top (gate 442) and Ropemaster Gao Shan at the camp (gate 441), 30000 gold each way, from level 75.
    internal static readonly double[][] RoadNorth =
    {
        new[] { 7186.0, 1170 }, new[] { 7228.0, 760 }, new[] { 7222.0, 398 }, new[] { 7035.0, 156 }, new[] { 6842.0, -116 }, new[] { 6812.0, -328 },
        new[] { 6854.0, -569 }
    };
    internal static readonly double[][] RoadSouth =
    {
        new[] { 6927.0, -931 }, new[] { 6930.0, -1040 }, new[] { 6866.0, -1125 }, new[] { 6842.0, -1275 }, new[] { 6824.0, -1426 },
        new[] { 6763.0, -1547 }, new[] { 6733.0, -1668 }, new[] { 6672.0, -1698 }, new[] { 6582.0, -1710 }, new[] { 6159.0, -1710 }
    };
    internal const int CliffCampGate = 441, CliffTopGate = 442;
    internal static readonly double[] CliffTop = { 6944.0, -787.5, 735 }, CliffCamp = { 6945.0, -754.5, 483 };

    // The road between Jangan and Jangan Pass: recorded by walking it (x, y, height per way point, from Jangan's south gate to the pass).
    static readonly List<double[]> passRoad = V3Data.Load<List<double[]>>("jangan-pass-road.json");
    internal const double PassX = 6041.3, PassY = -283.5, PassReach = 120, PassRoadReach = 60;

    internal static bool InPass(Position p)
    {
        if (p == null)
            return false;
        double dx = p.X - PassX, dy = p.Y - PassY;
        return dx * dx + dy * dy <= PassReach * PassReach;
    }

    // The way points still ahead on the Jangan - Jangan Pass road for this start and goal, in walking order; null for any other route.
    // A start in the middle of the road (the walk was stopped) continues from the nearest point of the road.
    internal static List<Position> PassRoadAhead(Position start, Position goal)
    {
        bool onRoad = false;
        int nearest = -1;
        double best = PassRoadReach;
        for (int i = 0; i < passRoad.Count; i++)
        {
            double d = RoadDistance(start, HeightPoint(passRoad[i]));
            if (d < best)
            {
                best = d;
                nearest = i;
                onRoad = true;
            }
        }
        bool toPass = InPass(goal) && !InPass(start) && (InJangan(start) || onRoad);
        bool toJangan = InJangan(goal) && !InJangan(start) && (InPass(start) || onRoad);
        if (!toPass && !toJangan)
            return null;
        var points = passRoad.Select(r => HeightPoint(r)).ToList();
        int from = toPass ? (InJangan(start) ? 0 : nearest) : (InPass(start) ? 0 : points.Count - 1 - nearest);
        if (toJangan)
            points.Reverse();
        var ahead = points.Skip(from).ToList();
        // the nearest point of a road already begun is skipped when it is right under the character
        if (ahead.Count > 1 && onRoad && RoadDistance(start, ahead[0]) < 6)
            ahead.RemoveAt(0);
        return ahead;
    }

    static bool InNanzhang(Position p)
    {
        return p != null && V3Data.Town(p) == "Nanzhang";
    }

    // One stop of the Jangan - Nanzhang road: a point to walk to, or the cliff teleport (Hop, with the gate it is taken from and the gate it lands at).
    internal sealed class RoadStop
    {
        internal Position Point;
        internal TransitGate From, To;
        internal bool Hop { get { return From != null; } }
    }

    static Position HeightPoint(double[] spot)
    {
        var p = V2Rules.Center(spot[0], spot[1], (float)spot[2]);
        return p;
    }

    static TransitGate NetworkGate(int id)
    {
        return ExpeditionRules.Network.Gates.First(g => g.Id == id);
    }

    // The whole road in walking order from Jangan: north part, the cliff top, the teleport down, the camp, south part.
    static List<RoadStop> FullRoad()
    {
        var stops = new List<RoadStop>();
        foreach (var r in RoadNorth)
            stops.Add(new RoadStop { Point = GatePoint(r[0], r[1]) });
        stops.Add(new RoadStop { Point = HeightPoint(CliffTop) });
        stops.Add(new RoadStop { From = NetworkGate(CliffTopGate), To = NetworkGate(CliffCampGate), Point = HeightPoint(CliffTop) });
        stops.Add(new RoadStop { Point = HeightPoint(CliffCamp) });
        foreach (var r in RoadSouth)
            stops.Add(new RoadStop { Point = GatePoint(r[0], r[1]) });
        return stops;
    }

    // Closeness of a spot to the road in 3D: the camp and the cliff top lie on top of each other, so the height has to count.
    internal static double RoadDistance(Position p, Position q)
    {
        double dx = p.X - q.X, dy = p.Y - q.Y, dz = (p.Height - q.Height) * 1.5;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    // The stops of the Jangan - Nanzhang road still ahead for this start and goal, in walking order, or null for any other route.
    // A start in the middle of the road (after the teleport, or after the walk was stopped) continues from the nearest stop.
    internal static List<RoadStop> RoadAhead(Position start, Position goal)
    {
        bool toNanzhang = InNanzhang(goal) && !InNanzhang(start), toJangan = InJangan(goal) && !InJangan(start);
        if (!toNanzhang && !toJangan)
            return null;
        var stops = FullRoad();
        int from = -1;
        if (InJangan(start) || InNanzhang(start))
            from = InJangan(start) ? 0 : stops.Count - 1;
        else
        {
            double best = 500;
            for (int i = 0; i < stops.Count; i++)
            {
                double d = RoadDistance(start, stops[i].Point);
                if (!stops[i].Hop && d < best)
                {
                    best = d;
                    from = i;
                }
            }
            if (from < 0)
                return null;
        }
        if (toJangan)
        {
            // Walked the other way: the stops in reverse, the teleport taken from the camp up to the cliff top.
            stops.Reverse();
            foreach (var s in stops)
                if (s.Hop)
                {
                    var down = s.From;
                    s.From = s.To;
                    s.To = down;
                    s.Point = HeightPoint(CliffCamp);
                }
            from = stops.Count - 1 - from;
        }
        // A start from Jangan or Nanzhang begins at the first stop; one on the road begins at the nearest stop, which is skipped when it is
        // already behind the character on the way to the goal.
        var ahead = stops.Skip(from).ToList();
        if (ahead.Count > 1 && !ahead[0].Hop && RoadDistance(start, ahead[0].Point) < 12)
            ahead.RemoveAt(0);
        return ahead;
    }

    // The way points of the road ahead without the teleport (the points to walk to); null for any other route.
    internal static List<Position> RoadWaypoints(Position start, Position goal)
    {
        var road = RoadAhead(start, goal);
        return road == null ? null : road.Where(s => !s.Hop).Select(s => s.Point).ToList();
    }

    // A ready-made script road (CaravanScripts) followed point by point. A character in the script's first town first walks to its first point (planned
    // over the network); one already on the road continues from the nearest point. The last point is within a few units of the trader: any gap that is
    // left is closed over the network, or by a short straight walk when it is small.
    static List<TransitStep> PlanScript(Position start, Position goal, List<TransitStep> script, bool atStart, Func<Position, Position, List<TransitStep>> planner)
    {
        var plan = new List<TransitStep>();
        if (atStart && script[0].From == null && start.Distance(script[0].To.Point()) > 12)
            plan.AddRange(PlanLeg(start, script[0].To.Point(), planner));
        plan.AddRange(script);
        var end = script[script.Count - 1].To.Point();
        double gap = end.Distance(goal);
        if (gap > CaravanScripts.Reach)
            plan.AddRange(PlanLeg(end, goal, planner));
        else if (gap > 12)
            plan.Add(new TransitStep { CellId = -1, To = new TransitGate { Code = "Kervan scripti", X = goal.X, Y = goal.Y, Height = goal.Height } });
        return plan;
    }

    internal static List<TransitStep> PlanVia(Position start, Position goal, Func<Position, Position, List<TransitStep>> planner)
    {
        bool atStart;
        var script = CaravanScripts.Ahead(start, goal, out atStart);
        if (script != null)
            return PlanScript(start, goal, script, atStart, planner);
        var pass = PassRoadAhead(start, goal);
        if (pass != null)
        {
            var walk = new List<TransitStep>();
            var here = start;
            foreach (var point in pass)
            {
                // the walk up to the first way point (inside Jangan, or from the pass) is planned over the network; the road itself is walked point by point
                if (walk.Count == 0 && here.Distance(point) > 12)
                    walk.AddRange(PlanLeg(here, point, planner));
                walk.Add(new TransitStep { CellId = -1, To = new TransitGate { Code = "Jangan Pass yolu", X = point.X, Y = point.Y, Height = point.Height } });
                here = point;
            }
            if (here.Distance(goal) > 12)
                walk.AddRange(PlanLeg(here, goal, planner));
            return walk;
        }
        var road = RoadAhead(start, goal);
        if (road == null)
            return PlanLeg(start, goal, planner);
        var plan = new List<TransitStep>();
        var at = start;
        foreach (var stop in road)
        {
            if (stop.Hop)
            {
                // The walk up to the teleporting NPC is the step before it (the cliff top / camp point); the hop itself is one step.
                plan.Add(new TransitStep { CellId = -1, From = stop.From, To = stop.To });
                at = HeightPoint(stop.To.Id == CliffCampGate ? CliffCamp : CliffTop);
                continue;
            }
            // The camp point right after the teleport is where the character already stands.
            if (RoadDistance(at, stop.Point) < 12)
                continue;
            // Legs over the road network are planned only on one level: the cliff top and the camp are not joined by the map data.
            plan.AddRange(PlanLeg(at, stop.Point, planner));
            at = stop.Point;
        }
        plan.AddRange(PlanLeg(at, goal, planner));
        return plan;
    }

    static List<TransitStep> PlanLeg(Position start, Position goal, Func<Position, Position, List<TransitStep>> planner)
    {
        var gate = GatePoints(start, goal);
        if (gate == null)
            return planner(start, goal);
        var plan = new List<TransitStep>(planner(start, gate[0]));
        foreach (var p in gate)
            plan.Add(GateStep(p));
        plan.AddRange(planner(gate[gate.Length - 1], goal));
        return plan;
    }

    internal static List<TransitStep> PlanWalk(Position start, Position goal)
    {
        RoadMesh.WalkOnly = true;
        RoadMesh.AllowFerries = true;
        try
        {
            return PlanVia(start, goal, (a, b) => ExpeditionRules.Plan(a, b));
        }
        finally
        {
            RoadMesh.WalkOnly = false;
            RoadMesh.AllowFerries = false;
        }
    }

    // A text script in the walk,X,Y,Z form that phBot-style bots read. One line per way point.
    internal static string Script(string from, string to, Position start, IEnumerable<TransitStep> plan)
    {
        var text = new StringBuilder();
        var ci = CultureInfo.InvariantCulture;
        var steps = plan.ToList();
        int ferries = steps.Count(RoadMesh.IsFerry);
        text.AppendLine("# Silkroad R Studio · kervan rotası");
        text.AppendLine("# " + from + " → " + to + " · " + (steps.Count + 1) + " nokta · yürüyüş" + (ferries > 0 ? " + " + ferries +
            " feribot geçişi" : "") + " (teleport yok)");
        text.AppendLine("# Biçim: walk,X,Y,Z");
        text.AppendLine("walk," + start.X.ToString("F0", ci) + "," + start.Y.ToString("F0", ci) + "," + start.Height.ToString("F0", ci));
        foreach (var step in steps)
        {
            if (RoadMesh.IsFerry(step))
                text.AppendLine("# feribot: " + step.From.Code + " NPC’sine tıkla, " + step.To.Code + " yakasına geç");
            else if (step.From != null)
            {
                var npcPoint = step.From.Point();
                text.AppendLine("# ışınlanma (30000 altın, seviye 75): " + (step.From.Id == CliffTopGate ? "Cliff Warden Lu Feng" : "Ropemaster Gao Shan") +
                    " ile konuş, " + (step.To.Id == CliffCampGate ? "Tiger Rim - Climbers Camp" : "Tiger Rim - Cliff Top") + " noktasına geç");
                text.AppendLine("walk," + npcPoint.X.ToString("F0", ci) + "," + npcPoint.Y.ToString("F0", ci) + "," + npcPoint.Height.ToString("F0", ci));
            }
            var p = step.To.Point();
            text.AppendLine("walk," + p.X.ToString("F0", ci) + "," + p.Y.ToString("F0", ci) + "," + p.Height.ToString("F0", ci));
        }
        return text.ToString();
    }

    internal static string ScriptName(string from, string to)
    {
        return from + "-" + to + ".txt";
    }
}

sealed partial class MemorySelectV3Form
{
    Dictionary<string, TradeNpc> tradeNpcs = new Dictionary<string, TradeNpc>();
    readonly Dictionary<string, Label> tradeNpcLabels = new Dictionary<string, Label>();
    readonly Label caravanStatus = new Label(), caravanAnimal = new Label();

    void BuildCaravan()
    {
        var page = new TabPage("Kervan Rotası");
        tabs.TabPages.Add(page);
        var content = PageCard(page, "Kervan rotası", 940);
        var intro = new Label { AutoSize = true, MaximumSize = new Size(900, 0), Tag = "muted", Margin = new Padding(0, 0, 0, 4), Text =
            "Ticaret malı ışınlanamaz: rota yürüyerek hesaplanır, teleport, Return Scroll ve binek kullanılmaz. Nehir geçişlerinde (örneğin Hotan - Donwhang) " +
            "en uygun feribot geçidi seçilir ve karakter feribot NPC’sine kendisi tıklayıp karşıya geçer. " +
            "Önce her şehirde tüccar NPC’ye oyunda tıklayıp “Seçili NPC’yi kaydet” düğmesine bas; konum bir kez öğrenilir. " +
            "Jangan - Nanzhang rotası, haritada işaretlenen karayolunu izler (Jangan Pass üzerinden Nanzhang’ın doğu kapısına). Yol Tiger Rim uçurumunda kesilir: " +
            "orada uçurum üstündeki Cliff Warden Lu Feng (Jangan → Nanzhang) ya da uçurum dibindeki Ropemaster Gao Shan (Nanzhang → Jangan) ile ışınlanılır; " +
            "her yönde 30000 altın, seviye 75 gerekir. Karakter ışınlanmayı kendisi ister, karşıya geçince yürüyüşe kaldığı yerden devam eder. " +
            "Jangan - Jangan Pass rotası elle yürünüp kaydedilmiş yolu iki yönde izler (145 nokta, Jangan’ın güney kapısından Pass’e). " +
            "Hotan - Jangan, Hotan - Donwhang, Jangan - Donwhang ve Constantinople → Samarkand rotaları phBot kervan scriptlerindeki hazır yolu nokta nokta izler " +
            "(feribotlar dahil); Samarkand → Constantinople dahil diğer rotalar haritadan hesaplanır." };
        content.Controls.Add(intro);

        content.Controls.Add(SectionTitle("Tüccar NPC’leri"));
        foreach (string town in TradeRoutes.Towns)
        {
            var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 0, 0, 2) };
            var name = LabelOf(town, 110);
            name.Font = Ui.BodyBold;
            var state = new Label { AutoSize = false, Width = 760, Height = 30, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 1, 0, 1) };
            tradeNpcLabels[town] = state;
            row.Controls.Add(name);
            row.Controls.Add(state);
            content.Controls.Add(row);
        }
        var actions = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 4, 0, 6) };
        actions.Controls.Add(Btn("Seçili NPC’yi kaydet", RecordTradeNpc, 190, true, ButtonKind.Primary));
        actions.Controls.Add(Btn("Kayıtları temizle", ClearTradeNpcs, 160, true, ButtonKind.Ghost));
        content.Controls.Add(actions);

        content.Controls.Add(SectionTitle("Kervan hayvanı"));
        caravanAnimal.AutoSize = true;
        caravanAnimal.MaximumSize = new Size(900, 0);
        caravanAnimal.Margin = new Padding(0, 0, 0, 4);
        content.Controls.Add(caravanAnimal);
        var animalRow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 0, 0, 6) };
        animalRow.Controls.Add(Btn("Bindiğim hayvanı kervan hayvanı yap", AddCaravanAnimal, 280, false, ButtonKind.Secondary));
        animalRow.Controls.Add(Btn("Kervan hayvanı listesini sıfırla", ResetCaravanAnimals, 250, false, ButtonKind.Ghost));
        content.Controls.Add(animalRow);

        content.Controls.Add(SectionTitle("Rotalar"));
        // Three routes per row: the route button and its script button side by side.
        var grid = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, MaximumSize = new Size(1120, 0),
            Margin = new Padding(0) };
        foreach (string[] route in TradeRoutes.Routes)
        {
            string from = route[0], to = route[1];
            var cell = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0, 0, 14, 0) };
            cell.Controls.Add(Btn(from + " - " + to + " Sc", delegate { StartCaravan(from, to); }, 262, true, ButtonKind.Success));
            cell.Controls.Add(Btn("Script", delegate { ExportCaravanScript(from, to, true); }, 80, false));
            grid.Controls.Add(cell);
        }
        content.Controls.Add(grid);
        content.Controls.Add(Btn("Script klasörünü aç", OpenScriptFolder, 190, false, ButtonKind.Ghost));
        caravanStatus.AutoSize = true;
        caravanStatus.MaximumSize = new Size(900, 0);
        caravanStatus.Tag = "muted";
        caravanStatus.Margin = new Padding(0, 2, 0, 6);
        caravanStatus.Text = "Rota düğmesi scripti yazar ve karakteri hedef tüccara yürütür.";
        content.Controls.Add(caravanStatus);

        tradeNpcs = TradeRoutes.WithDefaults(TradeRoutes.Load(TradeRoutes.StorePath));
        RefreshCaravanUi();
        UpdateCaravanAnimalText();
    }

    static Label SectionTitle(string text)
    {
        return new Label { Text = text, Font = Ui.Heading, AutoSize = false, Width = 820, Height = 30, TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 4, 0, 2), Tag = "gold" };
    }

    void RefreshCaravanUi()
    {
        foreach (string town in TradeRoutes.Towns)
        {
            TradeNpc npc;
            var label = tradeNpcLabels[town];
            if (tradeNpcs.TryGetValue(town, out npc))
            {
                label.Text = "Kayıtlı · X " + npc.X.ToString("F0") + " / Y " + npc.Y.ToString("F0") + " · model " + npc.Model;
                label.Tag = null;
            }
            else
            {
                label.Text = "Kayıtlı değil";
                label.Tag = "warn";
            }
        }
        ApplyTheme();
    }

    TradeNpc RequireTradeNpc(string town)
    {
        TradeNpc npc;
        if (!tradeNpcs.TryGetValue(town, out npc))
            throw new InvalidOperationException(town + " tüccarı kayıtlı değil. Oyunda o NPC’ye tıkla ve “Seçili NPC’yi kaydet”e bas.");
        return npc;
    }

    // The NPC the user has selected in the client becomes that town's trader.
    void RecordTradeNpc()
    {
        RequireReader();
        snapshot = reader.Capture();
        if (snapshot.Selected == 0)
            throw new InvalidOperationException("Önce oyunda tüccar NPC’ye tıkla; sonra tekrar dene.");
        var npc = snapshot.Npcs.FirstOrDefault(n => n.Uid == snapshot.Selected);
        if (npc == null)
            throw new InvalidOperationException("Seçili hedef bir NPC olarak okunamadı.");
        string town = TradeRoutes.TownKey(npc.Position);
        if (town == null)
            throw new InvalidOperationException("Seçili NPC tüccar şehirlerinden birinde değil (Hotan, Jangan, Donwhang, Samarkand, Constantinople, Nanzhang, Jangan Pass).");
        tradeNpcs[town] = new TradeNpc { Town = town, X = npc.Position.X, Y = npc.Position.Y, Model = npc.Model };
        TradeRoutes.Save(TradeRoutes.StorePath, tradeNpcs);
        Log("Tüccar NPC kaydedildi · " + town + " · model " + npc.Model);
        caravanStatus.Text = town + " tüccarı kaydedildi.";
        RefreshCaravanUi();
    }

    // The animal being ridden becomes a caravan animal (saved by its model): the bot then leaves it alone like a trade horse.
    void AddCaravanAnimal()
    {
        RequireReader();
        snapshot = reader.Capture();
        if (!snapshot.MountKnown || snapshot.MountUid == 0)
            throw new InvalidOperationException("Önce kervan hayvanına bin; sonra tekrar dene.");
        if (snapshot.MountModel == 0)
            throw new InvalidOperationException("Binilen hayvanın modeli okunamadı.");
        if (settings.CaravanAnimals == null)
            settings.CaravanAnimals = new List<int>();
        if (!settings.CaravanAnimals.Contains((int)snapshot.MountModel))
            settings.CaravanAnimals.Add((int)snapshot.MountModel);
        SaveSettingsQuietly();
        Log("Kervan hayvanı eklendi · model " + snapshot.MountModel);
        UpdateCaravanAnimalText();
    }

    void ResetCaravanAnimals()
    {
        settings.CaravanAnimals = new List<int> { 23909 };
        SaveSettingsQuietly();
        UpdateCaravanAnimalText();
    }

    string CaravanAnimalList()
    {
        var models = settings == null || settings.CaravanAnimals == null ? new List<int>() : settings.CaravanAnimals;
        return models.Count == 0 ? "yok" : string.Join(", ", models.Select(m => m == 23909 ? "Buffalo (23909)" : m.ToString()));
    }

    // Shown on the page: what is ridden now and which animals count as caravan animals.
    void UpdateCaravanAnimalText()
    {
        string now = snapshot == null || !snapshot.MountKnown ? "oyuna bağlı değil" : snapshot.MountUid == 0 ? "yaya" :
            "binili hayvan modeli " + snapshot.MountModel + (RidingCaravanAnimal() ? " · kervan hayvanı: mount komutu gönderilmez, binili yürür" : " · normal binek");
        string text = "Şu an: " + now + "\r\nKayıtlı kervan hayvanları: " + CaravanAnimalList();
        if (caravanAnimal.Text != text)
            caravanAnimal.Text = text;
    }

    void ClearTradeNpcs()
    {
        if (!ConfirmDialog.Ask(this, "Kayıtlar silinsin mi?", "Kaydedilen tüm tüccar konumları silinir; yeniden seçmen gerekir.", "Sil", "Vazgeç", true))
            return;
        tradeNpcs.Clear();
        TradeRoutes.Save(TradeRoutes.StorePath, tradeNpcs);
        TradeRoutes.WithDefaults(tradeNpcs);
        caravanStatus.Text = "Tüccar kayıtları temizlendi.";
        RefreshCaravanUi();
    }

    // Writes the way points of a road-only route between the two saved traders. Needs no running game.
    string ExportCaravanScript(string from, string to, bool report)
    {
        string text;
        int points;
        var ready = CaravanScripts.Find(from, to);
        if (ready != null)
        {
            // a ready-made road: written as it is, needs no saved trader
            text = CaravanScripts.Text(ready);
            points = CaravanScripts.WalkPoints(ready);
        }
        else
        {
            var a = RequireTradeNpc(from);
            var b = RequireTradeNpc(to);
            var start = TradeRoutes.PositionOf(a);
            var plan = TradeRoutes.PlanWalk(start, TradeRoutes.PositionOf(b));
            text = TradeRoutes.Script(from, to, start, plan);
            points = plan.Count + 1;
        }
        Directory.CreateDirectory(TradeRoutes.ScriptFolder);
        string path = Path.Combine(TradeRoutes.ScriptFolder, TradeRoutes.ScriptName(from, to));
        File.WriteAllText(path, text, new UTF8Encoding(false));
        string note = from + " → " + to + " scripti yazıldı · " + points + " nokta · " + path;
        Log(note);
        caravanStatus.Text = note;
        if (report)
            status.Text = note;
        return path;
    }

    void StartCaravan(string from, string to)
    {
        var a = RequireTradeNpc(from);
        var b = RequireTradeNpc(to);
        RequireReader();
        ExportCaravanScript(from, to, false);
        var target = TradeRoutes.PositionOf(b);
        float height;
        if (!ExpeditionRules.TryGround(target, out height))
            throw new InvalidOperationException(to + " tüccarının konumunda yürünebilir zemin bulunamadı; NPC’yi yeniden kaydet.");
        var d = new DungeonLocation { Code = "KERVAN", Name = from + " → " + to, Area = ExpeditionRules.Area(target), Mode = "Serbest",
            Region = target.Region, X = b.X, Y = b.Y, LocalX = target.LocalX, LocalY = target.LocalY, Height = height };
        WalkDungeon(d, true);
        // remembered for the resume after a closed game (cleared by every Stop)
        caravanFrom = from;
        caravanTo = to;
        double away = snapshot.Player.Distance(TradeRoutes.PositionOf(a));
        caravanStatus.Text = from + " → " + to + " yürüyüşü başladı" + (away > 300 ?
            " · karakter " + from + " tüccarından " + (int)away + " birim uzakta, bulunduğu yerden gidiyor." : ".");
    }

    void OpenScriptFolder()
    {
        Directory.CreateDirectory(TradeRoutes.ScriptFolder);
        System.Diagnostics.Process.Start("explorer.exe", "\"" + TradeRoutes.ScriptFolder + "\"");
    }
}

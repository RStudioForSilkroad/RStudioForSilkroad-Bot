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
    internal static readonly string[] Towns = { "Hotan", "Jangan", "Donwhang", "Samarkand", "Constantinople", "Nanzhang" };
    internal static readonly string[][] Routes =
    {
        new[] { "Hotan", "Jangan" }, new[] { "Jangan", "Hotan" }, new[] { "Hotan", "Donwhang" }, new[] { "Donwhang", "Hotan" },
        new[] { "Jangan", "Donwhang" }, new[] { "Donwhang", "Jangan" },
        new[] { "Samarkand", "Constantinople" }, new[] { "Constantinople", "Samarkand" },
        // Nanzhang: on foot along the road marked on the world map (see JanganNanzhangRoad), not by the gate teleport.
        new[] { "Jangan", "Nanzhang" }, new[] { "Nanzhang", "Jangan" }
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
    internal static readonly double[][] JanganNanzhangRoad =
    {
        new[] { 7186.0, 1170 }, new[] { 7228.0, 760 }, new[] { 7222.0, 398 }, new[] { 7035.0, 156 }, new[] { 6842.0, -116 }, new[] { 6812.0, -328 },
        new[] { 6854.0, -569 }, new[] { 6945.0, -750 }, new[] { 6927.0, -931 }, new[] { 6930.0, -1040 }, new[] { 6866.0, -1125 }, new[] { 6842.0, -1275 },
        new[] { 6824.0, -1426 }, new[] { 6763.0, -1547 }, new[] { 6733.0, -1668 }, new[] { 6672.0, -1698 }, new[] { 6582.0, -1710 }, new[] { 6159.0, -1710 }
    };

    static bool InNanzhang(Position p)
    {
        return p != null && V3Data.Town(p) == "Nanzhang";
    }

    // The way points of the Jangan - Nanzhang road in walking order for this start and goal, or null for any other route.
    internal static List<Position> RoadWaypoints(Position start, Position goal)
    {
        bool forward = InJangan(start) && InNanzhang(goal), back = InNanzhang(start) && InJangan(goal);
        if (!forward && !back)
            return null;
        var points = JanganNanzhangRoad.Select(r => GatePoint(r[0], r[1])).ToList();
        if (back)
            points.Reverse();
        return points;
    }

    internal static List<TransitStep> PlanVia(Position start, Position goal, Func<Position, Position, List<TransitStep>> planner)
    {
        var road = RoadWaypoints(start, goal);
        if (road == null)
            return PlanLeg(start, goal, planner);
        var plan = new List<TransitStep>();
        var at = start;
        foreach (var p in road)
        {
            plan.AddRange(PlanLeg(at, p, planner));
            at = p;
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
            "Jangan - Nanzhang rotası, haritada işaretlenen karayolunu izler (Jangan Pass üzerinden Nanzhang’ın doğu kapısına); ışınlanma kullanılmaz." };
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

        tradeNpcs = TradeRoutes.Load(TradeRoutes.StorePath);
        RefreshCaravanUi();
        UpdateCaravanAnimalText();
    }

    static Label SectionTitle(string text)
    {
        return new Label { Text = text, Font = Ui.Heading, AutoSize = false, Width = 900, Height = 30, TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 4, 0, 2) };
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
            throw new InvalidOperationException("Seçili NPC tüccar şehirlerinden birinde değil (Hotan, Jangan, Donwhang, Samarkand, Constantinople, Nanzhang).");
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
        caravanStatus.Text = "Tüccar kayıtları temizlendi.";
        RefreshCaravanUi();
    }

    // Writes the way points of a road-only route between the two saved traders. Needs no running game.
    string ExportCaravanScript(string from, string to, bool report)
    {
        var a = RequireTradeNpc(from);
        var b = RequireTradeNpc(to);
        var start = TradeRoutes.PositionOf(a);
        var plan = TradeRoutes.PlanWalk(start, TradeRoutes.PositionOf(b));
        string text = TradeRoutes.Script(from, to, start, plan);
        Directory.CreateDirectory(TradeRoutes.ScriptFolder);
        string path = Path.Combine(TradeRoutes.ScriptFolder, TradeRoutes.ScriptName(from, to));
        File.WriteAllText(path, text, new UTF8Encoding(false));
        string note = from + " → " + to + " scripti yazıldı · " + (plan.Count + 1) + " nokta · " + path;
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

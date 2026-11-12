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
    internal static readonly string[] Towns = { "Hotan", "Jangan", "Donwhang" };
    internal static readonly string[][] Routes =
    {
        new[] { "Hotan", "Jangan" }, new[] { "Jangan", "Hotan" }, new[] { "Hotan", "Donwhang" }, new[] { "Donwhang", "Hotan" },
        new[] { "Jangan", "Donwhang" }, new[] { "Donwhang", "Jangan" }
    };

    internal static string StorePath
    {
        get { return Path.Combine(AppPaths.DataRoot, "trade-npcs.json"); }
    }

    internal static string ScriptFolder
    {
        get { return Path.Combine(AppPaths.DataRoot, "scripts"); }
    }

    // The three trade towns by their short names; null for anywhere else.
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
    internal static List<TransitStep> PlanWalk(Position start, Position goal)
    {
        RoadMesh.WalkOnly = true;
        RoadMesh.AllowFerries = true;
        try
        {
            return ExpeditionRules.Plan(start, goal);
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
    readonly Label caravanStatus = new Label();

    void BuildCaravan()
    {
        var page = new TabPage("Kervan Rotası");
        tabs.TabPages.Add(page);
        var content = PageCard(page, "Kervan rotası", 820);
        var intro = new Label { AutoSize = false, Width = 760, Height = 88, Tag = "muted", Margin = new Padding(0, 0, 0, 8), Text =
            "Ticaret malı ışınlanamaz: rota yürüyerek hesaplanır, teleport, Return Scroll ve binek kullanılmaz. Nehir geçişlerinde (örneğin Hotan - Donwhang) " +
            "en uygun feribot geçidi seçilir ve karakter feribot NPC’sine kendisi tıklayıp karşıya geçer. " +
            "Önce her şehirde tüccar NPC’ye oyunda tıklayıp “Seçili NPC’yi kaydet” düğmesine bas; konum bir kez öğrenilir." };
        content.Controls.Add(intro);

        content.Controls.Add(SectionTitle("Tüccar NPC’leri"));
        foreach (string town in TradeRoutes.Towns)
        {
            var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 0, 0, 2) };
            var name = LabelOf(town, 110);
            name.Font = Ui.BodyBold;
            var state = new Label { AutoSize = false, Width = 560, Height = 30, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 1, 0, 1) };
            tradeNpcLabels[town] = state;
            row.Controls.Add(name);
            row.Controls.Add(state);
            content.Controls.Add(row);
        }
        var actions = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 6, 0, 10) };
        actions.Controls.Add(Btn("Seçili NPC’yi kaydet", RecordTradeNpc, 190, true, ButtonKind.Primary));
        actions.Controls.Add(Btn("Kayıtları temizle", ClearTradeNpcs, 160, true, ButtonKind.Ghost));
        content.Controls.Add(actions);

        content.Controls.Add(SectionTitle("Rotalar"));
        foreach (string[] route in TradeRoutes.Routes)
        {
            string from = route[0], to = route[1];
            var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 0, 0, 2) };
            row.Controls.Add(Btn(from + " - " + to + " Sc", delegate { StartCaravan(from, to); }, 200, true, ButtonKind.Success));
            row.Controls.Add(Btn("Script dosyası", delegate { ExportCaravanScript(from, to, true); }, 150, false));
            content.Controls.Add(row);
        }
        content.Controls.Add(Btn("Script klasörünü aç", OpenScriptFolder, 190, false, ButtonKind.Ghost));
        caravanStatus.AutoSize = false;
        caravanStatus.Width = 760;
        caravanStatus.Height = 54;
        caravanStatus.Tag = "muted";
        caravanStatus.Margin = new Padding(0, 6, 0, 0);
        caravanStatus.Text = "Rota düğmesi scripti yazar ve karakteri hedef tüccara yürütür.";
        content.Controls.Add(caravanStatus);

        tradeNpcs = TradeRoutes.Load(TradeRoutes.StorePath);
        RefreshCaravanUi();
    }

    static Label SectionTitle(string text)
    {
        return new Label { Text = text, Font = Ui.Heading, AutoSize = false, Width = 760, Height = 30, TextAlign = ContentAlignment.MiddleLeft,
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
            throw new InvalidOperationException("Seçili NPC Hotan, Jangan veya Donwhang şehrinde değil.");
        tradeNpcs[town] = new TradeNpc { Town = town, X = npc.Position.X, Y = npc.Position.Y, Model = npc.Model };
        TradeRoutes.Save(TradeRoutes.StorePath, tradeNpcs);
        Log("Tüccar NPC kaydedildi · " + town + " · model " + npc.Model);
        caravanStatus.Text = town + " tüccarı kaydedildi.";
        RefreshCaravanUi();
    }

    void ClearTradeNpcs()
    {
        if (!ConfirmDialog.Ask(this, "Kayıtlar silinsin mi?", "Kaydedilen üç tüccar konumu silinir; yeniden seçmen gerekir.", "Sil", "Vazgeç", true))
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

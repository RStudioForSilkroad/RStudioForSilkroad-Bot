using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Forms;

partial class V3Checks
{
    static readonly List<string> passed = new List<string>();

    // Visible controls whose bounds leave the area of their parent. Docked controls and scrolling panels are fine.
    static IEnumerable<string> ClippedControls(Control root)
    {
        foreach (Control c in root.Controls)
        {
            if (!c.Visible)
                continue;
            var parent = c.Parent;
            var scroller = parent as ScrollableControl;
            bool scrolls = scroller != null && scroller.AutoScroll;
            if (c.Dock == DockStyle.None && !scrolls && parent.ClientSize.Width > 40 && parent.ClientSize.Height > 20 && !(parent is TabControl))
            {
                if (c.Right > parent.ClientSize.Width + 1 || c.Bottom > parent.ClientSize.Height + 1 || c.Left < -1)
                    yield return c.GetType().Name + " '" + (c.Text.Length > 30 ? c.Text.Substring(0, 30) : c.Text) + "' " + c.Bounds + " in " + parent.GetType().Name + " " +
                        parent.ClientSize;
            }
            foreach (string inner in ClippedControls(c))
                yield return inner;
        }
    }

    static void SkillChecks()
    {
        A(SkillRotation.ParseKey("1") == '1' && SkillRotation.ParseKey("a") == 'A' && SkillRotation.ParseKey("F1") == 0x70 &&
            SkillRotation.ParseKey("f12") == 0x7B, "Skill keys: digits, letters and F1-F12");
        A(SkillRotation.ParseKey("") == 0 && SkillRotation.ParseKey("F13") == 0 && SkillRotation.ParseKey("12") == 0 &&
            SkillRotation.ParseKey("ab") == 0, "Unusable skill keys are refused");
        var slots = new List<SkillSlot>
        {
            new SkillSlot { Key = "1", Kind = SkillRotation.Attack_ },
            new SkillSlot { Key = "2", Kind = SkillRotation.Attack_, Seconds = 10 },
            new SkillSlot { Key = "7", Kind = SkillRotation.Heal_, Seconds = 3, Percent = 50 },
            new SkillSlot { Key = "8", Kind = SkillRotation.Mana_, Seconds = 3, Percent = 40 },
            new SkillSlot { Key = "9", Kind = SkillRotation.Buff_, Seconds = 120 },
            new SkillSlot { On = false, Key = "0", Kind = SkillRotation.Attack_ }
        };
        A(SkillRotation.Validate(slots) == null, "A normal skill list is valid");
        slots.Add(new SkillSlot { Key = "zz" });
        A(SkillRotation.Validate(slots) != null, "A bad key is reported");
        slots.RemoveAt(slots.Count - 1);
        slots.Add(new SkillSlot { Key = "5", Kind = SkillRotation.Heal_, Percent = 0 });
        A(SkillRotation.Validate(slots) != null, "A heal percent of 0 is reported");
        slots.RemoveAt(slots.Count - 1);
        var rot = new SkillRotation();
        rot.Load(slots);
        A(rot.HasAttack && rot.Keys().Count == 5, "Disabled lines are ignored");
        // Buff first (healthy, full mana), repeats only after its interval.
        A(rot.Support(0, 100, 100, true) == '9', "A due buff is pressed without a target");
        A(rot.Support(1000, 100, 100, true) == 0 && rot.Support(119000, 100, 100, true) == 0, "A buff waits for its interval");
        A(rot.Support(120000, 100, 100, true) == '9', "A buff repeats after its interval");
        // Heal beats mana beats buff, each with its cooldown.
        A(rot.Support(240000, 30, 10, true) == '7', "Low HP presses the heal first");
        A(rot.Support(240100, 30, 10, true) == '8', "Heal on cooldown lets the mana skill go");
        A(rot.Support(240200, 30, 10, true) == '9', "Then the due buff");
        A(rot.Support(240300, 30, 10, true) == 0, "Everything on cooldown presses nothing");
        A(rot.Support(250000, 90, 90, true) == 0, "Healthy and full of mana presses no heal or mana");
        A(rot.Support(260000, 10, 10, false) == 0, "Unknown stats never trigger a heal");
        // Attack: in turn, a skill with its own cooldown is skipped until ready.
        var attacks = new SkillRotation();
        attacks.Load(slots);
        A(attacks.NextAttack(0) == '1' && attacks.NextAttack(100) == '2', "Attack skills go in turn");
        A(attacks.NextAttack(200) == '1' && attacks.NextAttack(300) == '1', "A skill on cooldown is skipped");
        A(attacks.NextAttack(10100) == '2', "The skill is used again once its cooldown is over");
        var none = new SkillRotation();
        none.Load(new[] { new SkillSlot { Key = "9", Kind = SkillRotation.Buff_ } });
        A(!none.HasAttack && none.NextAttack(0) == 0, "No attack lines, no attack key");
    }

    static void PlannerChecks()
    {
        // The batched navigation call: code for N queries, answers written to a separate data page.
        var args = new uint[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var batch = TargetSender.BuildProbeBatch(0x20000000, 0x10001000, new[] { args, args, args });
        int calls = 0;
        for (int i = 0; i + 1 < batch.Length; i++)
            if (batch[i] == 0xff && batch[i + 1] == 0xd0)
                calls++;
        A(batch[0] == 0x55 && batch[1] == 0x8b && batch[2] == 0xec && calls == 3 && batch[batch.Length - 3] == 0xc2 && batch[batch.Length - 2] == 4 && batch[batch.Length - 1] == 0 &&
            batch.Length < TargetSender.BatchCodeLimit, "Batched navigation call has one query per call instruction and a clean exit");
        bool refused = false;
        try { TargetSender.BuildProbeBatch(0x20000000, 0x10001000, new uint[17][]); }
        catch (ArgumentException) { refused = true; }
        A(refused, "A batch of more than 16 queries is refused");
        refused = false;
        try { TargetSender.BuildProbeBatch(0x20000000, 0x10001000, new[] { new uint[7] }); }
        catch (ArgumentException) { refused = true; }
        A(refused, "A query without exactly 8 arguments is refused");
        // The same search with and without batching finds the same route, and batching asks the game far less often.
        Func<Position, Position, bool> open = (a, b) => !(Math.Min(a.X, b.X) <= 2 && Math.Max(a.X, b.X) >= 0 && Math.Min(a.Y, b.Y) <= 20 && Math.Max(a.Y, b.Y) >= -40);
        var start = V2Rules.Center(-30, 0, 0f);
        var goal = V2Rules.Center(30, 0, 0f);
        int singleCalls = 0, batchCalls = 0;
        var plain = new ObstaclePath { Step = 4 };
        plain.Begin(start, goal, null, 3);
        for (int i = 0; i < 400 && plain.Route == null && !plain.Failed; i++)
            plain.Advance((a, b) => { singleCalls++; return open(a, b); }, 64);
        var batched = new ObstaclePath { Step = 4 };
        batched.Prefetch = pairs => { batchCalls++; return pairs.Select(pair => (sbyte)(open(pair[0], pair[1]) ? 1 : 0)).ToArray(); };
        batched.Begin(start, goal, null, 3);
        for (int i = 0; i < 400 && batched.Route == null && !batched.Failed; i++)
            batched.Advance((a, b) => open(a, b), 64);
        A(plain.Route != null && batched.Route != null && plain.Route.Count == batched.Route.Count &&
            plain.Route.Zip(batched.Route, (p, q) => p.Distance(q) < 0.01).All(same => same), "Batched search finds exactly the same route");
        A(batchCalls > 0 && batchCalls * 3 < singleCalls, "Batched search calls the game far less often (" + batchCalls + " instead of " + singleCalls + ")");
        // Unknown answers (-1) fall back to the single query.
        var unsure = new ObstaclePath { Step = 4 };
        unsure.Prefetch = pairs => pairs.Select(pair => (sbyte)-1).ToArray();
        unsure.Begin(start, goal, null, 3);
        for (int i = 0; i < 400 && unsure.Route == null && !unsure.Failed; i++)
            unsure.Advance((a, b) => open(a, b), 64);
        A(unsure.Route != null && unsure.Route.Count == plain.Route.Count, "Unknown batch answers fall back to the single query");
        A(MemorySelectV3Form.IdlePollMs == 250 && MemorySelectV3Form.ActivePollMs == 100, "The window looks at the game every 250 ms idle and 100 ms while a job runs");
    }

    static void StartupChecks()
    {
        var names = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceNames();
        A(Startup.MissingResources(names).Count == 0, "Every embedded file the start-up procedure requires is in the program");
        var missing = Startup.MissingResources(names.Where(n => n != "road-mesh.bin.gz"));
        A(missing.Count == 1 && missing[0] == "road-mesh.bin.gz", "A missing embedded file is reported by name");
        A(Startup.Percent(0, 5) == 0 && Startup.Percent(2, 5) == 40 && Startup.Percent(5, 5) == 100 && Startup.Percent(9, 5) == 100 &&
            Startup.Percent(1, 0) == 0, "Start-up progress is a percentage of finished steps");
        A(Startup.StepNames.Length == 5 && Startup.StepNames.All(s => s.Length > 0), "Start-up has five named steps");
        Startup.WarmData();
        ExpeditionRules.Warm();
        A(true, "Game data and the road network load in the start-up procedure");
        // Freeze and thaw hold the layout of a whole tree and release it again.
        var host = new Panel { Width = 300, Height = 200 };
        var inner = new FlowLayoutPanel { AutoSize = true };
        host.Controls.Add(inner);
        inner.Controls.Add(new Label { Text = "a" });
        int layouts = 0;
        inner.Layout += delegate { layouts++; };
        Ui.Freeze(host);
        inner.Controls.Add(new Label { Text = "b" });
        inner.Controls.Add(new Label { Text = "c" });
        int duringFreeze = layouts;
        Ui.Thaw(host);
        inner.PerformLayout();
        A(duringFreeze == 0 && layouts > 0, "Frozen layout is held back and runs again after the thaw");
    }

    static void ExplorerChecks()
    {
        // Object types read from the client: NPC = 1/2/2, monster = 1/2/1. Every NPC (traders, Explorer) must reach the snapshot.
        A(GameReader.IsNpcType(0x144) && !GameReader.IsNpcType(0xC4) && !GameReader.IsNpcType(0x4) && !GameReader.IsNpcType(0x104),
            "NPC object type is recognised, monsters and other objects are not");
        var all = ExplorerSpots.All;
        A(all.Count == 52 && all.Select(s => s.N).Distinct().Count() == 52 && all[0].N == 1 && all[51].N == 52, "52 Explorer spots, numbered 1-52");
        A(all.All(s => ExplorerSpots.Areas.Contains(s.Area)), "Every Explorer spot belongs to a named area");
        A(ExplorerSpots.Areas.All(a => all.Any(s => s.Area == a)), "Every named area has Explorer spots");
        // The map was registered onto the game world map: spot 51 lies in the sector next to Jangan.
        A(ExplorerSpots.Find(51).Point().Region == 24999, "Spot 51 is in the Jangan sector found from the client data");
        var done = new HashSet<int> { 1, 2, 3, 10 };
        var queue = ExplorerSpots.Queue(1, done, true);
        A(queue.Count == 48 && queue[0] == 4 && queue[queue.Count - 1] == 52, "Queue skips found spots");
        A(ExplorerSpots.Queue(50, done, false).SequenceEqual(new[] { 50, 51, 52 }) && ExplorerSpots.Queue(60, done, false).Count == 0,
            "Queue starts at the chosen number");
        // Walkable ground next to each marker: the map is approximate, the nearest point within 120 units is used.
        int grounded = 0;
        foreach (var s in all)
        {
            Position point;
            float height;
            if (ExplorerSpots.Ground(s, out point, out height))
                grounded++;
        }
        A(grounded >= 40, "Most Explorer spots have walkable ground (" + grounded + " / 52)");
        // Live NPC lookup: right model, not handled yet, nearest to the spot.
        var spot = all[10];
        var snap = new Snapshot();
        snap.Npcs.Add(new WorldNpc { Uid = 1, Model = 60023, Position = V2Rules.Center(spot.X + 200, spot.Y, 0) });
        snap.Npcs.Add(new WorldNpc { Uid = 2, Model = 60023, Position = V2Rules.Center(spot.X + 40, spot.Y, 0) });
        snap.Npcs.Add(new WorldNpc { Uid = 3, Model = 11111, Position = V2Rules.Center(spot.X + 5, spot.Y, 0) });
        A(ExplorerSpots.FindNpc(snap, 60023, spot.Point(), 350, new HashSet<uint>()).Uid == 2, "Nearest Explorer NPC of the right model is found");
        A(ExplorerSpots.FindNpc(snap, 60023, spot.Point(), 350, new HashSet<uint> { 2 }).Uid == 1, "A handled NPC is skipped");
        A(ExplorerSpots.FindNpc(snap, 60023, spot.Point(), 100, new HashSet<uint> { 2 }) == null, "An NPC outside the radius is ignored");
        A(new V3Settings().ExplorerModel == 60023 && new V3Settings().ExplorerSkipDone && new V3Settings().ExplorerDone.Count == 0,
            "Explorer settings start with the known model and nothing found");
    }

    static void CaravanChecks()
    {
        // Trader NPC store: save, reload, damaged file.
        string file = Path.Combine(Path.GetTempPath(), "trade-npcs-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var saved = new Dictionary<string, TradeNpc>();
            saved["Hotan"] = new TradeNpc { Town = "Hotan", X = -2100.5, Y = 1400.25, Model = 1234 };
            saved["Jangan"] = new TradeNpc { Town = "Jangan", X = 5200, Y = -300, Model = 4321 };
            TradeRoutes.Save(file, saved);
            var loaded = TradeRoutes.Load(file);
            A(loaded.Count == 2 && loaded["Hotan"].X == -2100.5 && loaded["Jangan"].Model == 4321, "Trader NPCs survive save and load");
            File.WriteAllText(file, "{ not json");
            A(TradeRoutes.Load(file).Count == 0, "A damaged trader file loads as empty");
            A(TradeRoutes.Load(file + ".missing").Count == 0, "A missing trader file loads as empty");
        }
        finally
        {
            try { File.Delete(file); } catch { }
        }
        // Nanzhang counts as a town (death recovery and trip planning wait for a town); all six city sectors are listed.
        A(V3Data.Town(V2Rules.Center(5643.3, -1885.3, 462f)) == "Nanzhang" && V3Data.Town(V2Rules.Center(5938.6, -1706.2, 462f)) == "Nanzhang" &&
            V3Data.Town(V2Rules.Center(6100.0, -1541.0, 462f)) == "Nanzhang", "Nanzhang city sectors are recognised as a town");
        A(TradeRoutes.TownKey(V2Rules.Center(5643.3, -1885.3, 462f)) == null, "Nanzhang is not a caravan town");
        A(TradeRoutes.TownKey(V2Rules.Center(148.8, 85.1, 0f)) == "Hotan" && TradeRoutes.TownKey(V2Rules.Center(6512, 1008, 0f)) == "Jangan" &&
            TradeRoutes.TownKey(V2Rules.Center(3495, 2076, 0f)) == "Donwhang", "Trader towns: Hotan (Kingdom sector), Jangan and Donwhang");
        // Routes: the four requested directions exist.
        A(TradeRoutes.Routes.Length == 6 && TradeRoutes.Routes.Any(r => r[0] == "Hotan" && r[1] == "Jangan") &&
            TradeRoutes.Routes.Any(r => r[0] == "Jangan" && r[1] == "Hotan") &&
            TradeRoutes.Routes.Any(r => r[0] == "Hotan" && r[1] == "Donwhang") &&
            TradeRoutes.Routes.Any(r => r[0] == "Donwhang" && r[1] == "Hotan") &&
            TradeRoutes.Routes.Any(r => r[0] == "Jangan" && r[1] == "Donwhang") &&
            TradeRoutes.Routes.Any(r => r[0] == "Donwhang" && r[1] == "Jangan"), "Hotan-Jangan, Hotan-Donwhang and Jangan-Donwhang routes both ways");
        // Script text: header comments, then one walk line per point, culture independent.
        var start = V2Rules.Center(-2100.4, 1400.6, 3f);
        var step = new TransitStep { To = new TransitGate { Code = "Harita yolu", X = -2000.2, Y = 1500.7, Height = 5 } };
        string[] lines = TradeRoutes.Script("Hotan", "Jangan", start, new[] { step }).Replace("\r", "").Split('\n')
            .Where(l => l.Length > 0).ToArray();
        A(lines[0].StartsWith("#") && lines[1].Contains("Hotan → Jangan") && lines[2].StartsWith("#"), "Script starts with comments");
        A(lines[3] == "walk,-2100,1401,3" && lines[4] == "walk,-2000,1501,5", "Script lists way points as walk,X,Y,Z");
        A(TradeRoutes.ScriptName("Hotan", "Jangan") == "Hotan-Jangan.txt", "Script file name");
        // Road-only planning: no teleport, ferry or flyship hop, and the flag is released afterwards.
        var from = V3Data.Dungeons.First(d => d.Mode == "Solo Trial").Entrance();
        var to = V3Data.Dungeons.Where(d => d.Mode == "Solo Trial").Skip(1).First().Entrance();
        try
        {
            var plan = TradeRoutes.PlanWalk(from, to);
            A(plan.All(s2 => s2.From == null || RoadMesh.IsFerry(s2)), "Caravan plan never uses a teleport, only ferries");
        }
        catch (InvalidOperationException)
        {
            // Not connected by road in the map data: refusing is the correct answer for trade goods.
        }
        A(!RoadMesh.WalkOnly && !RoadMesh.AllowFerries, "Road-only and ferry flags are released after planning");
        // Hotan has no land link to Donwhang or Jangan: the river is crossed by the nearest ferry, never by a teleport.
        var hotanTrader = V2Rules.Center(148.8, 85.1, 0f);
        var donwhangTrader = V2Rules.Center(3495, 2076, 0f);
        var janganTrader = V2Rules.Center(6512, 1008, 0f);
        foreach (var pair in new[] { new[] { hotanTrader, donwhangTrader }, new[] { donwhangTrader, hotanTrader },
            new[] { janganTrader, hotanTrader }, new[] { hotanTrader, janganTrader }, new[] { janganTrader, donwhangTrader },
            new[] { donwhangTrader, janganTrader } })
        {
            float groundA, groundB;
            ExpeditionRules.TryGround(pair[0], out groundA);
            ExpeditionRules.TryGround(pair[1], out groundB);
            pair[0].Height = groundA;
            pair[1].Height = groundB;
            var ferryPlan = TradeRoutes.PlanWalk(pair[0], pair[1]);
            A(ferryPlan.Count > 10 && ferryPlan.Count(RoadMesh.IsFerry) >= 1 && ferryPlan.All(s3 => s3.From == null || RoadMesh.IsFerry(s3)),
                "Caravan route " + (int)pair[0].X + "," + (int)pair[0].Y + " → " + (int)pair[1].X + "," + (int)pair[1].Y + " crosses by ferry only");
        }
        // Jangan: the caravan road crosses the wall by the south gate, from wherever the character stands inside the city.
        var southSoldier = V2Rules.Center(6434, 968, 0f);
        var eastSoldier = V2Rules.Center(6662, 1142, 0f);
        var janganNorth = V2Rules.Center(6509.6, 1135.7, 0f);
        float groundNorth;
        ExpeditionRules.TryGround(janganNorth, out groundNorth);
        janganNorth.Height = groundNorth;
        A(TradeRoutes.InJangan(janganNorth) && TradeRoutes.InJangan(janganTrader) && !TradeRoutes.InJangan(donwhangTrader) && !TradeRoutes.InJangan(hotanTrader),
            "Jangan's walls contain both the trader and the city centre");
        foreach (var origin in new[] { janganNorth, janganTrader })
        {
            var gatePlan = TradeRoutes.PlanWalk(origin, donwhangTrader);
            var gatePoints = new[] { origin }.Concat(gatePlan.Select(st => st.To.Point())).ToList();
            A(gatePoints.Min(q => q.Distance(southSoldier)) < 40 && gatePoints.Min(q => q.Distance(eastSoldier)) > 100,
                "From inside Jangan (" + (int)origin.X + "," + (int)origin.Y + ") the caravan leaves by the south gate, not the east gate");
        }
        var backPlan = TradeRoutes.PlanWalk(donwhangTrader, janganNorth);
        A(backPlan.Select(st => st.To.Point()).Min(q => q.Distance(southSoldier)) < 40, "Entering Jangan the caravan also uses the south gate");
        // The arch is crossed in a straight line: three points on one x, directly after each other.
        foreach (var archPlan in new[] { TradeRoutes.PlanWalk(janganNorth, donwhangTrader), TradeRoutes.PlanWalk(janganTrader, hotanTrader) })
        {
            int arch = archPlan.FindIndex(st => st.To.Code == "Güney kapı");
            A(arch >= 0 && arch + 2 < archPlan.Count && archPlan[arch + 1].To.Code == "Güney kapı" && archPlan[arch + 2].To.Code == "Güney kapı" &&
                Math.Abs(archPlan[arch].To.X - archPlan[arch + 2].To.X) < 1 && archPlan[arch].To.Y > archPlan[arch + 1].To.Y + 40 &&
                archPlan[arch + 1].To.Y > archPlan[arch + 2].To.Y + 40, "Leaving Jangan the route runs straight south through the arch");
        }
        var entering = TradeRoutes.PlanWalk(donwhangTrader, janganTrader);
        int enterArch = entering.FindIndex(st => st.To.Code == "Güney kapı");
        A(enterArch >= 0 && entering[enterArch].To.Y < entering[enterArch + 1].To.Y && entering[enterArch + 1].To.Y < entering[enterArch + 2].To.Y,
            "Entering Jangan the route runs straight north through the arch");
        var scriptSteps = TradeRoutes.PlanWalk(hotanTrader, donwhangTrader);
        string scriptText = TradeRoutes.Script("Hotan", "Donwhang", hotanTrader, scriptSteps);
        A(scriptText.Contains("# feribot: GATE_NPC_KT_FERRY2") && scriptText.Contains("1 feribot geçişi"), "Script marks the ferry crossing");
        SkillChecks();
        SkillEngineChecks();
        StartupChecks();
        PlannerChecks();
        ExplorerChecks();
        // Unique alert: once per monster, again after it left and came back; ordinary monsters are ignored.
        var alert = new UniqueAlert();
        var boss = new Monster { Uid = 7, Name = "Tiger Girl", Rank = 3, Alive = true };
        var plain = new Monster { Uid = 8, Name = "Wolf", Rank = 0, Alive = true };
        A(alert.Observe(new[] { plain }).Count == 0, "Ordinary monsters raise no unique alert");
        A(alert.Observe(new[] { plain, boss }).SequenceEqual(new[] { "Tiger Girl" }), "A unique is announced when it appears");
        A(alert.Observe(new[] { plain, boss }).Count == 0, "The same unique is announced only once");
        A(alert.Observe(new Monster[0]).Count == 0 && alert.Observe(new[] { boss }).Count == 1, "A unique that returns is announced again");
        boss.Alive = false;
        A(alert.Observe(new[] { boss }).Count == 0, "A dead unique raises no alert");
    }

    static void A(bool test, string name)
    {
        if (!test)
            throw new Exception("FAIL: " + name);
        passed.Add(name);
    }

    static Position P(double x, double y)
    {
        return V2Rules.Center(x, y, 0);
    }

    static Snapshot S(bool alive, Position p)
    {
        return new Snapshot { PlayerAlive = alive, Player = p, InventoryKnown = true, SpeedKnown = true };
    }

    [STAThread]
    static void Main(string[] args)
    {
        try
        {
            Run(args);
        }
        catch (Exception e)
        {
            File.WriteAllText("tests/tests-failed.txt", e.ToString());
            Environment.ExitCode = 1;
        }
    }

    static void Run(string[] args)
    {
        if (args.Length > 0 && args[0] == "--road-audit")
        {
            var rows = new List<string>();
            foreach (var d in V3Data.Dungeons.Where(d => d.Mode == "Solo Trial"))
            {
                int city = d.Area.Contains("Samarkand") ? 25 : d.Area.Contains("Constantinople") ? 20 : d.Area.Contains("Downhang") ?
                    2 : d.Area.Contains("Jangan") ? 1 : 5;
                var source = ExpeditionRules.Network.Gates.Single(gate => gate.Id == city);
                try
                {
                    var plan = ExpeditionRules.Plan(source.Point(), d.Entrance());
                    rows.Add(d.Code + " OK " + plan.Count + " steps " + string.Join(",", plan.Where(step => step.From != null)
                        .Select(step => step.From.Id + "→" + step.To.Id)));
                }
                catch (Exception error)
                {
                    rows.Add(d.Code + " FAIL " + error.Message);
                }
            }
            File.WriteAllLines("tests/road-audit.txt", rows);
            return;
        }
        foreach (var dungeon in V3Data.Dungeons.Where(d => d.Mode == "Solo Trial"))
        {
            int expected = ExpeditionRules.Expected(dungeon.Code);
            var portal = ExpeditionRules.Network.Gates.Single(transportGate => transportGate.Code == "GATE_NE_GRIM_" +
                dungeon.Code + "_S");
            A(ExpeditionRules.Network.Links.Any(l => l.From == dungeon.GateId && l.To == portal.Id), dungeon.Code +
                " Solo Trial portal linked from correct entrance");
            A(ExpeditionRules.Cleared("Dungeon cleared! Monsters " + expected + "/" + expected + ", AP +2", dungeon.Code),
                dungeon.Code + " exact count completion matches");
            A(!ExpeditionRules.Cleared("Dungeon cleared! Monsters " + (expected - 1) + "/" + expected, dungeon.Code),
                dungeon.Code + " incomplete count rejected");
            A(!ExpeditionRules.Cleared("Dungeon cleared! Monsters " + (expected == 6 ? 9 : 6) + "/" + (expected == 6 ? 9 :
                6), dungeon.Code), dungeon.Code + " other dungeon count rejected");
        }
        foreach (var dungeon in V3Data.Dungeons.Where(d => d.Mode == "Solo Trial"))
        {
            var capturedFloor = FloorNavigation.LoadCaptured(dungeon);
            var rooms = GrimRooms.Load(dungeon, capturedFloor);
            var points = new HashSet<string>();
            for (int sweep = 0; sweep < 60; sweep++)
            {
                Position point = rooms.Next(capturedFloor.Origin);
                if (point == null)
                    break;
                points.Add(point.X + "," + point.Y);
                rooms.Arrived();
            }
            A(points.Count >= 3 && points.Count <= 40, dungeon.Code +
                " initial room sweep has distinct floor waypoints (" + points.Count + ")");
        }
        var d18 = V3Data.Dungeons.Single(d => d.Code == "D18" && d.Mode == "Solo Trial");
        var roomRun = GrimRooms.Load(d18, FloorNavigation.LoadCaptured(d18));
        var pack = new[] { new Monster { Uid = 1, Alive = true, Position = P(-545, 4869) }, new Monster { Uid = 2, Alive =
            true, Position = P(-499, 4868) }, new Monster { Uid = 3, Alive = true, Position = P(-520, 4885) } };
        var nextRoom = new Monster { Uid = 4, Alive = true, Position = P(-516, 4977) };
        var optional = new Monster { Uid = 5, Alive = true, Name = "Envy", Position = P(-516, 4977) };
        roomRun.Observe(pack);
        roomRun.Observe(new[] { nextRoom, optional });
        A(!roomRun.Allows(nextRoom) && roomRun.Allows(optional),
            "Unloaded mobs do not advance room and Envy remains eligible");
        pack[0].Alive = false;
        pack[1].Health = 0;
        roomRun.Observe(pack);
        roomRun.Observe(pack);
        A(!roomRun.Allows(nextRoom), "Duplicate dead snapshots do not advance the room");
        pack[2].Alive = false;
        roomRun.Observe(pack);
        A(roomRun.Allows(nextRoom), "Three distinct confirmed deaths advance to the next room");
        A(!ExpeditionRules.ReturnIds.Overlaps(RecoveryRules.ReverseIds), "Return and Reverse item sets do not overlap");
        A(ExpeditionRules.ReturnIds.Contains(61) && ExpeditionRules.ReturnIds.Contains(63318),
            "Normal and event Return variants loaded");
        var route = ExpeditionRules.Plan(P(113.8, 15.3), P(-4407, 196));
        A(route.Count > 0 && route.Any(step => step.From != null && ExpeditionRules.Network.Links.Any(link => link.From == step.From.Id &&
            link.To == step.To.Id)), "Hotan to Roc route includes declared transit");
        foreach (var trial in V3Data.Dungeons.Where(d => d.Mode == "Solo Trial"))
        {
            int cityGate = trial.Area.Contains("Samarkand") ? 25 : trial.Area.Contains("Constantinople") ? 20 : trial.Area.Contains("Downhang") ?
                2 : trial.Area.Contains("Jangan") ? 1 : 5;
            List<TransitStep> trialRoute = null;
            try
            {
                trialRoute = ExpeditionRules.Plan(ExpeditionRules.Network.Gates.Single(gateRow => gateRow.Id == cityGate)
                    .Point(), trial.Entrance());
            }
            catch (Exception) { }
            A(trialRoute != null && trialRoute.Count > 0, trial.Code + " entrance has a road route from its city");
            if (trial.Code == "D11" || trial.Code == "D12")
                A(trialRoute != null && trialRoute.Last().To.Point().Distance(trial.Entrance()) <= 24, trial.Code +
                    " route crosses the unlinked gap and ends at the entrance");
        }
        {
            var tileScale = WorldMapView.ToImage(192, 0).X - WorldMapView.ToImage(0, 0).X;
            A(Math.Abs(tileScale - 32) < 0.001 && Math.Abs(WorldMapView.ToImage(0, 192).Y - WorldMapView.ToImage(0, 0).Y +
                32) < 0.001, "World map pixel scale matches the 192-unit region size");
            using (var worldStream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(WorldMapView.ResourceName))
                using (var worldImage = new System.Drawing.Bitmap(worldStream))
                {
                    foreach (var site in V3Data.Dungeons.Where(d => d.Mode == "Solo Trial"))
                    {
                        var pixel = WorldMapView.ToImage(site.X, site.Y);
                        int coverage = 0, samples = 0;
                        for (int dy = -6; dy <= 6; dy += 3)
                            for (int dx = -6; dx <= 6; dx += 3)
                            {
                                int qx = (int)pixel.X + dx, qy = (int)pixel.Y + dy;
                                if (qx < 0 || qy < 0 || qx >= worldImage.Width || qy >= worldImage.Height)
                                    continue;
                                samples++;
                                var tilePixel = worldImage.GetPixel(qx, qy);
                                if (!(Math.Abs(tilePixel.R - 20) <= 14 && Math.Abs(tilePixel.G - 30) <= 14 && Math.Abs(tilePixel.B - 45) <= 14))
                                    coverage++;
                            }
                        A(samples > 0 && coverage * 2 >= samples, site.Code +
                            " entrance lies on mapped terrain of the world map");
                    }
                }
        }
        var d17Route = ExpeditionRules.Plan(P(6431, 1096), V3Data.Dungeons.Single(d => d.Code == "D17" && d.Mode == "Solo Trial")
            .Entrance());
        A(d17Route.Count > 0 && d17Route.All(step => step.From == null) && d17Route.Last().To.Point().Distance(P(7484,
            1754)) <= 240, "Jangan screenshot start reaches a D17 local approach without teleport");
        A(!ExpeditionRules.PositionJumped(P(6431, 1096), P(6440, 1096)) && ExpeditionRules.PositionJumped(P(6431, 1096),
            P(6900, 1200)), "Normal movement keeps the route; a position jump requests a new route");
        var shortcutRoute = PhBotRoutes.Parse(new[] { "walk,10,0,0", "walk,20,0,0",
            "teleport,GATE_NPC_KT_FERRY2,GATE_NPC_WC_FERRY3", "walk,30,0,0" });
        A(ExpeditionRules.WalkShortcut(shortcutRoute, P(0, 0), (a, b) => true) == 1 && ExpeditionRules.WalkShortcut(shortcutRoute,
            P(0, 0), (a, b) => false) == 0, "Walking shortcut requires clear ground and preserves ferry steps");
        var nanzhangRoute = ExpeditionRules.Plan(P(5852, -1697.7), P(7484, 1754));
        A(nanzhangRoute.Any(step => step.From != null && step.From.Id == 252 && step.To.Id == 1) && nanzhangRoute.Last()
            .To.Point().Distance(P(7484, 1754)) <= 240,
            "Nanzhang screenshot route uses the Jangan gate and reaches the D17 approach");
        bool invalidTeleport = false;
        try
        {
            TargetSender.BuildPacket(0x1000, 0x2000, 0x705a, new byte[8]);
        }
        catch (ArgumentException)
        {
            invalidTeleport = true;
        }
        A(invalidTeleport, "Malformed teleport payload rejected");
        A(ExpeditionRules.ApproachReached(P(0, 0), P(3, 4)), "Travel arrival accepts exact 5-unit boundary");
        A(!ExpeditionRules.ApproachReached(P(0, 0), P(5.01, 0)), "Travel does not arrive beyond 5 units");
        A(TargetSender.BuildMountCall(0x1000, 0x2000, new uint[0]).Skip(13).Take(6).SequenceEqual(new byte[] { 0x83,
            0xf8, 1, 0x0f, 0x94, 0xc0 }), "Mount native result normalized without repeated toggles");
        A(!new V3Settings().ReturnBeforeTravel, "Automatic Return disabled by default");
        var ferryPlan = PhBotRoutes.Parse(new[] { "walk,1050.4,-50.8,0",
            "teleport,GATE_NPC_KT_FERRY2,GATE_NPC_WC_FERRY3", "walk,2594.2,-49,0" });
        A(ferryPlan != null && ferryPlan[1].From.Id == 12 && ferryPlan[1].To.Id == 14,
            "phBot ferry script maps to the known directed link");
        A(PhBotRoutes.Parse(new[] { "teleport,GATE_CH,missing" }) == null, "Unrecognized imported teleport is rejected");
        var liveFerry = new Snapshot { Player = P(1051.9, -47.2) };
        var ferryGate = ExpeditionRules.Network.Gates.Single(ng => ng.Id == 12);
        liveFerry.Npcs.Add(new WorldNpc { Model = 3826, Uid = 593, Position = P(1078.94, -59.543) });
        A(ExpeditionRules.FindGateNpc(liveFerry, ferryGate).Uid == 593,
            "Ferry NPC offset 30 units from arrival point resolves by model");
        A(!ExpeditionRules.ApproachReached(liveFerry.Player, ExpeditionRules.FindGateNpc(liveFerry, ferryGate).Position),
            "Teleport waits for real NPC 5-unit approach");
        liveFerry.Npcs.Add(new WorldNpc { Model = 3826, Uid = 594, Position = P(5000, 5000) });
        A(ExpeditionRules.FindGateNpc(liveFerry, ferryGate).Uid == 593, "Same-model distant ferry is excluded");
        NavigationChecks.Run(A);
        GrimSurveyChecks.Run(A);
        var c = P(500, -50);
        var s = S(true, c);
        s.Monsters.AddRange(new[] { new Monster { Uid = 1, Name = "A", Alive = true, Position = P(510, -50) }, new Monster { Uid =
            2, Name = "B", Alive = true, Position = P(505, -50) }, new Monster { Uid = 3, Name = "C", Alive = true,
            Position = P(501, -50) }, new Monster { Uid = 4, Name = "A", Alive = true, Position = P(651, -50) }, new Monster { Uid =
            5, Name = "A", Alive = false, Position = P(500, -50) } });
        var names = new HashSet<string> { "A", "B" };
        var bans = new Dictionary<uint, long>();
        A(MultiTarget.Choose(s, names, c, 150, bans, 0, 0).Uid == 2,
            "Multi mob: nearest allowed name; rejects unlisted/dead/outside");
        bans[2] = 60000;
        A(MultiTarget.Choose(s, names, c, 150, bans, 0, 0).Uid == 1, "Blocked target moves to other selected species");
        A(MultiTarget.Choose(s, names, c, 150, bans, 60000, 0).Uid == 2, "Ban expires at boundary");
        A(MultiTarget.Choose(s, names, c, 150, bans, 60000, 1).Uid == 1, "Live target retained while valid");
        var r = new DeathRecovery();
        r.Begin();
        var dead = S(false, c);
        A(r.Observe(dead, 0, true, true, 10, 5) == RecoveryAction.None && r.Deaths == 1, "One death counted");
        r.Observe(dead, 9000, true, true, 10, 5);
        A(r.Deaths == 1, "Repeated dead snapshots do not inflate count");
        A(r.Observe(dead, 10000, false, true, 10, 5) == RecoveryAction.None && r.Phase == RecoveryPhase.WaitingRevive,
            "No town command while unfocused");
        A(r.Observe(dead, 10001, true, true, 10, 5) == RecoveryAction.ReturnTown, "Town requested after wait");
        A(r.Observe(dead, 10002, true, true, 10, 5) == RecoveryAction.None, "Town request sent once");
        var town = S(true, P(6434, 1094));
        A(V3Data.Town(town.Player) == "Jangan", "Town arrival requires known client sector");
        town.Items.Add(new BagItem { Model = 3795, Slot = 13, Quantity = 1, Type = 0x19ed });
        A(r.Observe(town, 20000, true, true, 10, 5) == RecoveryAction.None, "Town settles before reverse");
        A(r.Observe(town, 24999, true, true, 10, 5) == RecoveryAction.None, "No early reverse");
        A(r.Observe(town, 25000, true, true, 10, 5) == RecoveryAction.Reverse, "One reverse after stable town");
        A(r.Observe(town, 26000, true, true, 10, 5) == RecoveryAction.None, "Reverse not consumed twice");
        A(r.Observe(S(true, c), 30000, true, true, 10, 5) == RecoveryAction.Resume,
            "Resume only when death point reached");
        r.Begin();
        A(r.Observe(dead, 40000, true, true, 20, 2) == RecoveryAction.Stop && r.Deaths == 2,
            "Death limit stops BEFORE town or scroll");
        r.ResetCount();
        r.Begin();
        r.Observe(dead, 0, true, true, 20, 5);
        A(r.Observe(S(true, c), 1000, true, true, 20, 5) == RecoveryAction.Resume && r.Deaths == 1,
            "Party resurrection resumes and keeps death count");
        r = new DeathRecovery();
        r.Begin();
        r.Observe(dead, 0, true, true, 0, 5);
        r.Observe(town, 1, true, true, 0, 5);
        A(r.Observe(town, 90001, true, true, 0, 5) == RecoveryAction.Stop, "Recovery timeout stops without resend");
        r = new DeathRecovery();
        r.Begin();
        r.Observe(dead, 0, true, true, 0, 5);
        var emptyTown = S(true, town.Player);
        r.Observe(emptyTown, 1, true, true, 0, 5);
        A(r.Observe(emptyTown, 5001, true, true, 0, 5) == RecoveryAction.Stop, "No reverse in bag stops");
        r = new DeathRecovery();
        r.Begin();
        r.Observe(dead, 0, true, true, 0, 5);
        A(r.Observe(S(true, c), 1, true, true, 0, 5) == RecoveryAction.Resume,
            "Late resurrection at death point after town request resumes");
        A(V3Data.Town(P(115, 13)) == "Hotan Kingdom", "Actual New Era Hotan spawn recognized");
        A(!RecoveryRules.IsReverse(new BagItem { Model = 7100, Slot = 13, Quantity = 1, Type = 0xeec }),
            "Speed item cannot be used as reverse");
        A(!RecoveryRules.IsReverse(new BagItem { Model = 3795, Slot = 12, Quantity = 1, Type = 0x19ed }),
            "Equipment slot cannot be reverse");
        A(V3Data.Dungeons.Count == 72 && V3Data.Dungeons.Select(d => d.Code).Distinct().Count() == 18,
            "72 variants / 18 actual entrances loaded");
        A(V3Data.Dungeons.GroupBy(d => d.Mode).All(group => group.Count() == 18), "Each difficulty has 18 dungeons");
        var d01 = V3Data.Dungeons.First(d => d.Code == "D01");
        A(Math.Abs(d01.X + 1649.8) <.01 && Math.Abs(d01.Y - 2632.9) <.01, "Current gate rows world XY conversion");
        A(V3Data.Dungeons.First(d => d.Code == "D12").X == 72,
            "D12 coordinate recovered from gate, not invented from missing image");
        var g = new GrimRun();
        g.Arm(d01);
        var inside = S(true, d01.InnerPosition());
        A(!g.Observe(inside) && !g.Running, "Grim region alone cannot arm unknown instance");
        inside.Grim = new GrimObservation { Known = true, Id = 168, Title = "Grim Crypt", Text = "In progress" };
        A(g.Observe(inside) && g.Running && g.Floor != null, "Entry with HUD starts geometry mapping");
        inside.Monsters.Clear();
        g.Observe(inside);
        A(g.Running && !g.Complete, "Empty nearby monster list is NOT dungeon complete");
        inside.Grim.Text = "NOT COMPLETED";
        g.Observe(inside);
        A(!g.Complete, "Negative completion phrase rejected");
        inside.Grim.Text = "COMPLETED";
        g.Observe(inside);
        A(g.Complete && !g.Running, "Positive server completion stops and waits");
        var normalGrim = new GrimRun();
        var d12 = V3Data.Dungeons.First(d => d.Code == "D12" && d.Mode == "Novice");
        var plainInside = S(true, d12.InnerPosition());
        normalGrim.Arm(d12, plainInside.Player);
        A(normalGrim.Observe(plainInside) && normalGrim.Running,
            "Regular Grim with explicit inside selection supports absent Solo HUD");
        var floor = new FloorNavigation(d12.InnerPosition(), d12.InnerRegion);
        Func<Position, Position, bool> fakeProbe = (a, b) => Math.Abs(b.X - floor.Origin.X) <= 16 && Math.Abs(b.Y - floor.Origin.Y) <= 16 &&
            !(b.X > floor.Origin.X && b.Y == floor.Origin.Y);
        while (!floor.Done)
            floor.Advance(fakeProbe, 8);
        A(floor.Cells.Count > 1 && floor.Cells.Count < 30, "Navigation explores only reachable bounded cells");
        floor.ObservePosition(floor.Origin);
        A(floor.FullyCovered, "Mapped small room coverage");
        A(floor.RouteToUncovered(floor.Origin, fakeProbe).Count == 0, "No phantom route after coverage");
        normalGrim.Floor = floor;
        A(!normalGrim.CanFinish(plainInside, 0) && !normalGrim.CanFinish(plainInside, 14999),
            "Full navigation coverage still waits fifteen seconds");
        plainInside.Monsters.Add(new Monster { Alive = true, Position = plainInside.Player.Copy() });
        A(!normalGrim.CanFinish(plainInside, 15000), "Even a banned or unselected alive mob prevents clear");
        plainInside.Monsters.Clear();
        A(!normalGrim.CanFinish(plainInside, 20000) && normalGrim.CanFinish(plainInside, 35000),
            "New mob resets clear timer; clear only at new stable boundary");
        var large = new FloorNavigation(d12.InnerPosition(), d12.InnerRegion);
        Func<Position, Position, bool> largeProbe = (a, b) => Math.Abs(b.X - large.Origin.X) <= 64 && Math.Abs(b.Y - large.Origin.Y) <= 64;
        while (!large.Done)
            large.Advance(largeProbe, 8);
        large.ObservePosition(large.Origin);
        A(!large.FullyCovered, "Room not declared clear while unvisited ground remains");
        var path = large.RouteToUncovered(large.Origin, largeProbe);
        A(path.Count > 1 && path.Zip(path.Skip(1), (a, b) => a.Distance(b)).All(distance => distance <= 8.01),
            "Exploration follows connected 8-unit navigation edges");
        byte[] call8 = TargetSender.BuildCall(0x10000000, 0x10001000, new uint[8], new byte[0]);
        A(call8.Length == 64 && call8[61] == 0, "Eight argument navigation call fits before payload boundary");
        var t = new TravelWatch();
        t.Set(P(1000, 0), P(0, 0), 0);
        A(Math.Abs(t.Step(P(0, 0)).X - 32) <.01, "Travel uses bounded 32 unit steps");
        A(t.Stuck(P(0, 0), 20000), "Blocked walking times out");
        A(TargetSender.BuildPacket(0x10000000, 0x1000e183, 0x3053, new byte[] { 1 })[64] == 1,
            "Town packet explicit option 1");
        bool reject = false;
        try
        {
            TargetSender.BuildPacket(1, 2, 0x3053, new byte[] { 2 });
        }
        catch (ArgumentException)
        {
            reject = true;
        }
        A(reject, "Unsupported respawn option rejected");
        A(V2Rules.BerserkClasses.Contains("Unique"), "Unique Berserk dropdown option");
        A(V2Rules.BerserkMatches(new Monster { Rank = 3 }, "Unique"), "Unique rank selects Berserk");
        A(V2Rules.BerserkMatches(new Monster { Rank = 3, Party = true }, "Unique"),
            "Unique remains Unique with party flag");
        A(!V2Rules.BerserkMatches(new Monster { Rank = 4 }, "Unique") && !V2Rules.BerserkMatches(new Monster { Rank = 3 },
            "Giant"), "Unique and Giant Berserk classes isolated");
        A(V2Rules.BerserkMatches(new Monster { Rank = 4, Party = true }, "Giant Party"),
            "Prior Berserk class behavior retained");
        var repair = S(true, c);
        repair.MinDurability = 4;
        A(V2Rules.NeedsRepair(repair), "Repair <=4 preserved");
        string originalAccount = Environment.GetEnvironmentVariable("SRSTUDIO_ACCOUNT");
        Environment.SetEnvironmentVariable("SRSTUDIO_ACCOUNT", "1");
        string profile1 = V3Settings.PathName;
        Environment.SetEnvironmentVariable("SRSTUDIO_ACCOUNT", "2");
        A(profile1 != V3Settings.PathName, "Account settings paths isolated");
        Environment.SetEnvironmentVariable("SRSTUDIO_ACCOUNT", originalAccount);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var form = (Form)Activator.CreateInstance(typeof(MemorySelectV3Form), true);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MemorySelectV3Form).GetField("settings", flags).SetValue(form, null);
        var poll = (Timer)typeof(MemorySelectV3Form).GetField("poll", flags).GetValue(form);
        poll.Stop();
        var tabs = (TabControl)typeof(MemorySelectV3Form).GetField("tabs", flags).GetValue(form);
        A(tabs.TabPages.Count == 9 && tabs.TabPages[7].Text == "Skiller" && tabs.TabPages[4].Text == "Çift Hesap · Deneme" && tabs.TabPages[5].Text == "Nanzhang NPC" &&
            tabs.TabPages[1].Text == "Explorer Yerleri" && tabs.TabPages[2].Text == "Grim Crypt",
            "Four tabs render including dual-account trial");
        A(tabs.TabPages[6].Text == "Kervan Rotası", "Caravan page is part of the window");
        CaravanChecks();
        var explorerGrid = (DataGridView)typeof(MemorySelectV3Form).GetField("explorerGrid", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        A(explorerGrid.Rows.Count == 52 && Convert.ToInt32(explorerGrid.Rows[51].Cells["N"].Value) == 52, "Explorer page lists the 52 spots");
        var explorerArea = (ComboBox)typeof(MemorySelectV3Form).GetField("explorerArea", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        A(explorerArea.Items.Count == 8, "Explorer page offers the whole map and seven areas");
        var backgroundWalk = (CheckBox)typeof(MemorySelectV3Form).GetField("backgroundWalk", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        A(backgroundWalk.Checked && new V3Settings().BackgroundWalk, "Walking jobs may run in the background by default");
        var notifyBox = (CheckBox)typeof(MemorySelectV3Form).GetField("notifyDone", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        A(notifyBox.Checked && new V3Settings().NotifyDone, "A finished job is announced by default");
        notifyBox.Checked = false;
        typeof(MemorySelectV3Form).GetMethod("NotifyDone", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { "Deneme", "Bildirim kapalıyken sessiz kalır." });
        A(typeof(MemorySelectV3Form).GetField("doneIcon", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form) == null,
            "With notifications off no tray icon is created");
        notifyBox.Checked = true;
        A(!(bool)typeof(MemorySelectV3Form).GetMethod("CommandsAllowed", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null),
            "Without a connected game no command is allowed");
        var dg =(DataGridView)typeof(MemorySelectV3Form).GetField("dungeonGrid", flags).GetValue(form);
        A(dg.Rows.Count == 18, "Solo Trial selected by default");
        foreach (CheckBox check in (List<CheckBox>)typeof(MemorySelectV3Form).GetField("grimModes", flags).GetValue(form))
            check.Checked = true;
        A(dg.Rows.Count == 72, "All checked modes expose 72 rows");
        var visited = (HashSet<string>)typeof(MemorySelectV3Form).GetField("visitedDungeons", flags).GetValue(form);
        visited.Clear();
        var firstDungeon = (DungeonLocation)dg.Rows[0].Tag;
        dg.Rows[0].Cells[0].Value = true;
        A(visited.Contains(firstDungeon.Code + "|" + firstDungeon.Mode), "Manual dungeon checkbox stores code and mode");
        typeof(MemorySelectV3Form).GetMethod("RefreshDungeonGrid", flags).Invoke(form, null);
        A(Convert.ToBoolean(dg.Rows[0].Cells[0].Value), "Manual visited marker survives grid refresh");
        dg.Rows[0].Cells[0].Value = false;
        A(!visited.Contains(firstDungeon.Code + "|" + firstDungeon.Mode), "Manual visited marker can be cleared");
        ((HashSet<string>)typeof(MemorySelectV3Form).GetField("selectedMobs", flags).GetValue(form)).Clear();
        ((ListBox)typeof(MemorySelectV3Form).GetField("mobList", flags).GetValue(form)).Items.Clear();
        A(Catalog.Load()[61199] == "Mystic Tiger", "Mystic Tiger model name resolved");
        var theme = (ComboBox)typeof(MemorySelectV3Form).GetField("themePicker", flags).GetValue(form);
        for (int themeIndex = 0; themeIndex < 5; themeIndex++)
        {
            theme.SelectedIndex = themeIndex;
            Application.DoEvents();
        }
        theme.SelectedIndex = 0;
        var combo = (ComboBox)typeof(MemorySelectV3Form).GetField("monsters", flags).GetValue(form);
        combo.SelectedIndex = 0;
        typeof(MemorySelectV3Form).GetMethod("AddMob", flags).Invoke(form, null);
        typeof(MemorySelectV3Form).GetMethod("AddMob", flags).Invoke(form, null);
        var mobList = (ListBox)typeof(MemorySelectV3Form).GetField("mobList", flags).GetValue(form);
        A(mobList.Items.Count == 1, "Plus prevents duplicate mob rows");
        mobList.SelectedIndex = 0;
        typeof(MemorySelectV3Form).GetMethod("RemoveMob", flags).Invoke(form, null);
        A(mobList.Items.Count == 0, "Minus removes mob row");
        var sharedKeys = (TextBox)typeof(MemorySelectV3Form).GetField("keys", flags).GetValue(form);
        sharedKeys.Text = "1 2";
        var operation = typeof(MemorySelectV3Form).GetField("operation", flags);
        operation.SetValue(form, OperationMode.Hunt);
        A((string)typeof(MemorySelectV3Form).GetProperty("RunKeys", flags).GetValue(form, null) == "1 2",
            "Normal combat reads common Av settings");
        operation.SetValue(form, OperationMode.Grim);
        A((string)typeof(MemorySelectV3Form).GetProperty("RunKeys", flags).GetValue(form, null) == "1 2",
            "Grim combat reads the same common Av settings");
        A(typeof(MemorySelectV3Form).GetField("grimKeys", flags) == null, "No duplicate Grim controls");
        sharedKeys.Text = "1 2 3 4 5 6";
        operation.SetValue(form, OperationMode.Hunt);
        {
            var syncGrid = (DataGridView)typeof(MemorySelectV3Form).GetField("dungeonGrid", flags).GetValue(form);
            var syncMap = (WorldMapView)typeof(MemorySelectV3Form).GetField("worldMap", flags).GetValue(form);
            bool followsGrid = syncGrid.Rows.Count >= 8;
            foreach (int rowIndex in new[] { 4, 7, 2, 5 })
            {
                syncGrid.CurrentCell = syncGrid.Rows[rowIndex].Cells[1];
                followsGrid = followsGrid && ((DungeonLocation)syncGrid.Rows[rowIndex].Tag).Code == syncMap.Selected;
            }
            A(followsGrid, "World map selection follows the dungeon grid without lagging one row behind");
        }
        {
            var form_ = typeof(MemorySelectV3Form);
            Func<string, DungeonLocation> trial = code => V3Data.Dungeons.First(d => d.Code == code && d.Mode == "Solo Trial");
            var toggleQueue = form_.GetMethod("ToggleQueue", flags);
            var moveQueue = form_.GetMethod("MoveQueue", flags);
            var clearQueue = form_.GetMethod("ClearQueue", flags);
            var queueOrder = form_.GetMethod("QueueOrder", flags);
            var queueBox = (ListBox)form_.GetField("queueList", flags).GetValue(form);
            Func<string> order = () => string.Join(",", (List<string>)queueOrder.Invoke(form, null));
            clearQueue.Invoke(form, null);
            foreach (string code in new[] { "D05", "D12", "D01" })
                toggleQueue.Invoke(form, new object[] { trial(code) });
            A(order() == "D05,D12,D01", "Dungeon queue keeps the order in which dungeons were picked");
            queueBox.SelectedIndex = 2;
            moveQueue.Invoke(form, new object[] { -1 });
            A(order() == "D05,D01,D12", "Dungeon queue moves an entry up");
            toggleQueue.Invoke(form, new object[] { trial("D01") });
            A(order() == "D05,D12", "Picking a queued dungeon again removes it");
            bool novice = false;
            try
            {
                toggleQueue.Invoke(form, new object[] { V3Data.Dungeons.First(d => d.Mode == "Novice") });
            }
            catch (System.Reflection.TargetInvocationException)
            {
                novice = true;
            }
            A(novice && order() == "D05,D12", "Only Solo Trial dungeons can join the queue");
            A(queueBox.Items.Count == 2 && queueBox.Items[0].ToString().Contains("D05"),
                "Queue list shows the picked dungeons in order");
            clearQueue.Invoke(form, null);
            A(order() == "", "Queue clears");
        }
        {
            var exitLinks = ExpeditionRules.Network.Links.Where(l => l.From == 254).Select(l => l.To).OrderBy(x => x).ToArray();
            A(exitLinks.SequenceEqual(new[] { 2, 252 }),
                "Dungeon Exit NPC teleporter has the Donwhang and Nanzhang destinations in the transit data");
            var exitGate = ExpeditionRules.Network.Gates.Single(gateRow => gateRow.Id == 254);
            A(exitGate.Model == 36031, "Dungeon Exit teleporter gate uses the exit NPC model the bot already tracks");
        }
        {
            var charStats = new CharacterStats();
            Func<int, ulong, uint, uint, Snapshot> charSample = (lv, xp, spExp, sp) => new Snapshot
            {
                StatsKnown = true, Level = lv, Exp = xp, SpExp = spExp, Sp = sp, Hp = 1, MaxHp = 1, Mp = 1, MaxMp = 1
            };
            A(LevelData.Required(84) == 193898546 && LevelData.Required(52) == 12913686,
                "Level table matches the game data");
            // Levels 85-89 need far more than the standard table says: 814,298,430 EXP at level 86 is 30.77 % in the game.
            A(LevelData.Required(85) == 2483101044L && LevelData.Required(86) == 2646704676L && LevelData.Required(89) == 5307077340L &&
                Math.Round(100.0 * 814298430 / LevelData.Required(86), 2) == 30.77, "Level 85-89 requirements come from the client's own level data");
            charStats.Observe(0, charSample(84, 1000000, 100, 900));
            charStats.Observe(30000, charSample(84, 1000600, 200, 900));
            charStats.Observe(60000, charSample(84, 1001200, 350, 901));
            A(charStats.ExpGained == 1200 && Math.Abs(charStats.ExpPerMinute(60000) - 1200) < 1,
                "Exp gain and per-minute rate are measured from the session");
            A(Math.Abs(charStats.SpGained - 1.625) < 0.001 && Math.Abs(charStats.SpPerMinute(60000) - 1.625) < 0.01,
                "SP gain counts the 400-point sub steps");
            charStats.Observe(70000, charSample(84, 500000, 350, 901));
            A(charStats.ExpGained == 1200, "An exp loss after a death is never a negative gain");
            charStats.Observe(80000, charSample(84, 500100, 350, 901));
            A(charStats.ExpGained == 1300, "Gains continue from the lower exp after a death");
            charStats.Reset();
            charStats.Observe(0, charSample(84, 193898000, 0, 900));
            charStats.Observe(5000, charSample(85, 100, 0, 900));
            A(charStats.ExpGained == 646 && charStats.LevelsGained == 1,
                "A level up counts the rest of the old level plus the new exp and the level gained");
            charStats.Reset();
            A(charStats.LevelsGained == 0 && charStats.ExpGained == 0, "Reset clears the session counters");
            A(StatPanel.Duration(61.5) == "1 sa 1 dk" && StatPanel.Duration(-1) == "—" && StatPanel.Duration(0.5) == "30 sn",
                "Durations are written in hours, minutes and seconds");
        }
        var checkedDungeon = V3Data.Dungeons.First(x => x.Code == "D13" && x.Mode == "Solo Trial");
        var checkedFloor = FloorNavigation.LoadCaptured(checkedDungeon);
        checkedFloor.Covered.UnionWith(checkedFloor.Cells.Keys);
        var formGrim = (GrimRun)typeof(MemorySelectV3Form).GetField("grim", flags).GetValue(form);
        formGrim.Arm(checkedDungeon, checkedFloor.Origin);
        formGrim.Floor = checkedFloor;
        typeof(MemorySelectV3Form).GetField("snapshot", flags).SetValue(form, S(true, checkedFloor.Origin));
        typeof(MemorySelectV3Form).GetField("grimHolding", flags).SetValue(form, true);
        typeof(MemorySelectV3Form).GetField("grimWaiting", flags).SetValue(form, true);
        typeof(MemorySelectV3Form).GetMethod("GrimExplore", flags).Invoke(form, new object[] { 0L });
        var idleWalker = (ObstacleWalker)typeof(MemorySelectV3Form).GetField("routeWalker", flags).GetValue(form);
        A(!idleWalker.Active, "Grim holds position only after the mapped survey is complete");
        A(!HotkeyPolicy.Allowed((int)Keys.F1, 0) && !HotkeyPolicy.Allowed((int)Keys.F8, 0) && !HotkeyPolicy.Allowed((int)Keys.F24,
            0), "Fixed function keys removed and function key assignment refused");
        A(HotkeyPolicy.Allowed((int)Keys.Insert, 0) && HotkeyPolicy.Allowed((int)Keys.K, 2),
            "Custom single key and Ctrl combination supported");
        A(HotkeyPolicy.Conflicts((int)Keys.D1, 0, new ushort[] { 49 }, true) && !HotkeyPolicy.Conflicts((int)Keys.D1, 2,
            new ushort[] { 49 }, true), "Hotkey cannot intercept its own attack inputs");
        A(HotkeyPolicy.Conflicts((int)Keys.Tab, 0, new ushort[0], true), "Berserk Tab conflict prevented");
        var cfg = new V3Settings { Keys = "1 2", Deaths = 3, HotkeyKey = (int)Keys.Insert, VisitedDungeons = new List<string> { "D06|Novice" } };
        var codec = new System.Web.Script.Serialization.JavaScriptSerializer();
        var saved = codec.Deserialize<V3Settings>(codec.Serialize(cfg));
        A(saved.Keys == "1 2" && saved.Deaths == 3 && saved.HotkeyKey == (int)Keys.Insert && saved.VisitedDungeons.SequenceEqual(new[] { "D06|Novice" }),
            "Common settings, death counter and custom key persist");
        // The release signing key is private, so the checks sign a manifest with a throwaway key.
        string testPublicKey;
        UpdateManifest manifest;
        using (var testKey = new RSACryptoServiceProvider(2048))
        {
            testPublicKey = testKey.ToXmlString(false);
            manifest = new UpdateManifest
            {
                Product = UpdateTrust.Product,
                Version = "3.0.5.0",
                Url = "https://example.test/SilkroadRStudio.exe",
                Sha256 = new string('A', 64),
                Size = 11087360
            };
            manifest.Signature = Convert.ToBase64String(testKey.SignData(UpdateTrust.Canonical(manifest), "SHA256"));
        }
        UpdateTrust.Validate(manifest, testPublicKey);
        A(true, "Publisher RSA/SHA256 signature verifies");
        using (var embedded = new RSACryptoServiceProvider())
        {
            embedded.FromXmlString(UpdateTrust.EmbeddedPublicKey());
            A(embedded.KeySize == 3072 && embedded.PublicOnly, "Embedded publisher key is a 3072-bit public key");
        }
        A(!UpdateTrust.DefaultManifestUrl.Contains("by-rikimaru") && UpdateTrust.DefaultManifestUrl.StartsWith("https://"),
            "Default update address points to this project, not the original author");
        string goodHash = manifest.Sha256;
        manifest.Sha256 = new string('0', 64);
        bool tampered = false;
        try
        {
            UpdateTrust.Validate(manifest, testPublicKey);
        }
        catch (InvalidDataException)
        {
            tampered = true;
        }
        A(tampered, "Altered signed manifest rejected");
        manifest.Sha256 = goodHash;
        manifest.Url = "http://example.test/file.exe";
        bool http = false;
        try
        {
            UpdateTrust.Validate(manifest, testPublicKey);
        }
        catch (InvalidDataException)
        {
            http = true;
        }
        A(http, "Insecure HTTP update URL rejected");
        A(!UpdateService.OwnedPath(UpdateService.Root + "-other\\job.json") && !UpdateService.OwnedPath(Path.Combine(UpdateService.Root,
            "..", "outside.json")), "Updater job paths cannot escape staging root");
        ((CheckBox)typeof(MemorySelectV3Form).GetField("autoUpdate", flags).GetValue(form)).Checked = false;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-3000, -3000);
        form.Show();
        Application.DoEvents();
        for (int i = 0; i < tabs.TabCount; i++)
        {
            tabs.SelectedIndex = i;
            Application.DoEvents();
            if (i == 2)
            {
                foreach (CheckBox check in (List<CheckBox>)typeof(MemorySelectV3Form).GetField("grimModes", flags).GetValue(form))
                    check.Checked = check.Text == "Solo Trial";
            }
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                bitmap.Save("tests/Preview-" + i + ".png");
            }
        }
        // Layout checks at the smallest and a large window size: nothing may be cut off, previews are kept for review.
        var report = new List<string>();
        foreach (var size in new[] { new Size(1120, 700), new Size(1480, 900), new Size(1920, 1080) })
        {
            form.ClientSize = size;
            Application.DoEvents();
            for (int i = 0; i < tabs.TabCount; i++)
            {
                tabs.SelectedIndex = i;
                Application.DoEvents();
                form.PerformLayout();
                foreach (string problem in ClippedControls(form))
                    report.Add(size.Width + "x" + size.Height + " · " + tabs.TabPages[i].Text + " · " + problem);
                if (i == 0 || i == 2)
                    using (var bitmap = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                        bitmap.Save("tests/Preview-" + i + "-" + size.Width + ".png");
                    }
                if (i == 2 && size.Width == 1480)
                {
                    var views = (TabControl)typeof(MemorySelectV3Form).GetField("grimViews", flags).GetValue(form);
                    for (int view = 1; view < views.TabCount; view++)
                    {
                        views.SelectedIndex = view;
                        Application.DoEvents();
                        foreach (string problem in ClippedControls(form))
                            report.Add(size.Width + "x" + size.Height + " · Grim · " + views.TabPages[view].Text + " · " + problem);
                        using (var bitmap = new Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                            bitmap.Save("tests/Preview-grim-view" + view + ".png");
                        }
                    }
                    views.SelectedIndex = 0;
                }
            }
        }
        File.WriteAllLines("tests/Layout-Report.txt", report.Count == 0 ? new[] { "No clipped controls." } : report.ToArray());
        A(report.Count == 0, "No control is cut off at 1120x700, 1480x900 and 1920x1080 (see tests/Layout-Report.txt)");
        // Closing asks first: declining keeps the window open, confirming closes it.
        bool asked = false;
        MemorySelectV3Form.CloseConfirm = (owner, running) =>
        {
            asked = true;
            return false;
        };
        form.Close();
        A(asked && form.Visible, "Declining the close question keeps the window open");
        MemorySelectV3Form.CloseConfirm = (owner, running) => true;
        form.Close();
        A(!form.Visible, "Confirming the close question closes the window");
        form.Dispose();
        if (args.Length > 0)
        {
            using (var reader = new GameReader(int.Parse(args[0]), Catalog.Load()))
            {
                var snap = reader.Capture();
                A(reader.SenderAddress != 0 && reader.MoveAddress != 0 && reader.ReverseAddress != 0,
                    "Updated DLL profile resolves live addresses");
                using (var sender = new TargetSender(reader))
                    A(true, "Live send/move/reverse entry signatures validate; no command sent");
                File.WriteAllText("tests/Live-Profile.txt", reader.Compatibility + "\r\nPlayer: " + snap.Player.X + ", " +
                    snap.Player.Y + "\r\nAlive: " + snap.PlayerAlive + "\r\nInventory: " + snap.InventoryKnown +
                    "\r\nGrim known: " + snap.Grim.Known + " id=" + snap.Grim.Id + " title=" + snap.Grim.Title +
                    " text=" + snap.Grim.Text);
            }
        }
        File.WriteAllLines("tests/Test-Sonucu.txt", passed.Select(x => "PASS · " + x).Concat(new[] { "No game commands sent by this test harness." }));
        File.WriteAllText("tests/tests-failed.txt",
            "No current test failures. Earlier development failures were corrected.");
    }
}

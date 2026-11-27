using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

sealed class WorldNpc
{
    internal uint Uid, Model, Address;
    internal Position Position;
}

sealed class TransitGate
{
    public int Id, Model, Region, World;
    public string Code;
    public double X, Y, Height;

    internal Position Point()
    {
        return V2Rules.Center(X, Y, (float)Height);
    }
}

sealed class TransitLink
{
    public int From, To, Fee, Level;
    public int[] Restrictions;
}

sealed class TransitData
{
    public List<TransitGate> Gates;
    public List<TransitLink> Links;
}

sealed class TransitStep
{
    internal TransitGate From, To;
    internal int CellId = -1;
}

static class ExpeditionRules
{
    internal static readonly TransitData Network = V3Data.Load<TransitData>("transport-network.json");
    internal static readonly HashSet<uint> ReturnIds = new HashSet<uint>(V3Data.Load<List < uint>>("return-scrolls.json"));
    internal const double ArrivalDistance = 5;

    internal static int WalkShortcut(IEnumerable<TransitStep> route, Position current, Func<Position, Position, bool> probe)
    {
        var candidates = route.Take(4).TakeWhile(step => step.From == null).Select(step => step.To.Point()).ToArray();
        for (int i = candidates.Length - 1; i > 0; i--)
        {
            if (current.Distance(candidates[i]) <= 64 && probe(current, candidates[i]))
                return i;
        }
        return 0;
    }

    internal static bool PositionJumped(Position previous, Position current)
    {
        return previous != null && current.Distance(previous) > 96;
    }

    // Where the gate's NPC stands, from the game's building table (model -> world position); null for a gate without one. The NPC is often tens of
    // units from the point where a traveller arrives (Hotan 34, Constantinople 31, Samarkand 67, the last one on a platform 70 units higher).
    sealed class GateNpcPlace
    {
        public int Model;
        public double X, Y, Height;
    }

    static readonly Dictionary<int, GateNpcPlace> gateNpcPlaces = V3Data.Load<List<GateNpcPlace>>("gate-npcs.json").GroupBy(p => p.Model)
        .ToDictionary(g => g.Key, g => g.First());

    internal static Position NpcPlace(TransitGate gate)
    {
        GateNpcPlace place;
        return gate != null && gateNpcPlaces.TryGetValue(gate.Model, out place) ? V2Rules.Center(place.X, place.Y, (float)place.Height) : null;
    }

    // The client keeps only the NPCs within about 45 units of the character (measured: seen at 43, not at 48). Selecting an NPC and taking its
    // teleport works from that far (tried live at 34 and 42 units), so the character only has to come within this range.
    internal const double NpcSight = 45;

    // A point on the line from the NPC to the character, `distance` units from the NPC (the character's own place when it is closer already).
    internal static Position StandNear(Position from, Position npcPlace, double distance)
    {
        double dx = from.X - npcPlace.X, dy = from.Y - npcPlace.Y, length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= distance || length < 0.001)
            return from;
        return V2Rules.Center(npcPlace.X + dx / length * distance, npcPlace.Y + dy / length * distance, from.Height);
    }

    internal enum GateStep
    {
        WalkToGate,
        WalkToNpc,
        Use,
        Fail
    }

    // The order of a gate hop: to the gate's own point, then towards the NPC's place until the NPC is loaded, then use it. How far from the NPC the
    // client loads it is not fixed (seen at 43 units in Samarkand, at 29 but not at 33 in Donwhang), so the stand point comes closer in steps
    // (33, 25, 17, 9 units) each time the character stands there for a moment and the NPC is still not there. Once the gate point has been reached
    // the character never goes back to it (walking back and forth between the two was a real bug); it gives up after 60 seconds.
    internal sealed class GateSearch
    {
        internal const double FirstStand = 33, StandStep = 8, LastStand = 9;
        internal bool Arrived;
        internal long Since, StandSince;
        internal double Stand = FirstStand;

        internal void Reset()
        {
            Arrived = false;
            Since = 0;
            StandSince = 0;
            Stand = FirstStand;
        }

        internal GateStep Next(bool npcLoaded, bool atGatePoint, bool placeKnown, bool atStandPoint, long now)
        {
            if (npcLoaded)
                return GateStep.Use;
            if (!Arrived)
            {
                if (!atGatePoint)
                    return GateStep.WalkToGate;
                Arrived = true;
                Since = now;
            }
            if (atStandPoint)
            {
                if (StandSince == 0)
                    StandSince = now;
                else if (now - StandSince >= 1500 && Stand > LastStand)
                {
                    Stand = Math.Max(LastStand, Stand - StandStep);
                    StandSince = 0;
                }
            }
            else
                StandSince = 0;
            return placeKnown && now - Since < 60000 ? GateStep.WalkToNpc : GateStep.Fail;
        }
    }

    // Where to stand to use a gate: the gate's own point (where the game puts a character that arrives), unless the live NPC is right there (then
    // the NPC's spot). Some gates have their NPC far off and out of reach (Hotan's stands on a platform 34 units away, up a flight of steps); the
    // client accepts the selection and the teleport from the gate's point all the same (tried live).
    internal static Position GateApproach(TransitGate gate, WorldNpc npc)
    {
        var point = gate.Point();
        return npc != null && npc.Position.Distance(point) <= 12 ? npc.Position : point;
    }

    internal static bool ApproachReached(Position current, Position goal)
    {
        return current.Distance(goal) <= ArrivalDistance;
    }

    internal static WorldNpc FindGateNpc(Snapshot snapshot, TransitGate gate)
    {
        return snapshot.Npcs.Where(n => n.Model == (uint)gate.Model && n.Position.Distance(gate.Point()) <= 160).OrderBy(n =>
            n.Position.Distance(gate.Point())).ThenBy(n => n.Uid).FirstOrDefault();
    }

    internal static int Expected(string code)
    {
        return new[] { "D01", "D06", "D10", "D11", "D15", "D16" }.Contains(code) ? 6 : 9;
    }

    internal static bool Cleared(string text, string code)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        int n = Expected(code);
        return System.Text.RegularExpressions.Regex.IsMatch(text, @"(?:^|[\r\n])Dungeon cleared!\s*Monsters\s+" + n +
            "/" + n + @"(?:\D|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    internal static string Area(Position p)
    {
        if (V3Data.IsCrypt(p))
            return "Crypt";
        double x = p.X, y = p.Y;
        if (x > 5000 && y < -1200)
            return "Nanzhang";
        if (x > 4400)
            return "Jangan";
        if (x > 1450)
            return "Donwhang";
        if (x > -2700)
            return "Hotan";
        if (x > -3200 && y > 1700)
            return "Samarkand";
        if (x > -6100 && y < 1700)
            return "Roc";
        if (x > -10000)
            return "Samarkand";
        return "Constantinople";
    }

    internal static bool Infrastructure(TransitGate g)
    {
        return g.World == 1 && g.Model > 0 && (g.Id == 1 || g.Id == 2 || g.Id == 5 || g.Id == 20 || g.Id == 25 || g.Id == 252 ||
            g.Id == 330 || g.Code.Contains("FERRY") || g.Code.Contains("FLYSHIP") || g.Code.Contains("TUNNEL"));
    }

    static readonly Lazy<RoadMesh> roads = new Lazy<RoadMesh>(() => new RoadMesh(Network));

    internal static List<TransitStep> Plan(Position start, Position goal, HashSet<int> excluded = null)
    {
        return roads.Value.Plan(start, goal, excluded);
    }

    // Loads the road network now instead of when the first route is planned.
    internal static void Warm()
    {
        if (roads.Value == null)
            throw new InvalidOperationException("Yol ağı yüklenemedi.");
    }

    internal static bool TryGround(Position p, out float height)
    {
        return roads.Value.TryGround(p, out height);
    }
}

sealed partial class MemorySelectV3Form
{
    readonly CompletionNotice completionNotice = new CompletionNotice();
    readonly CheckBox autoEnterGrim = new CheckBox(), exitAfterGrim = new CheckBox();
    readonly Queue<TransitStep> transit = new Queue<TransitStep>();
    int expeditionPhase;
    long expeditionSince;
    TransitGate pendingArrival;
    uint portalUid;
    bool selectedPortal;
    Position returnOrigin, knownExit, lastExpeditionPosition;
    bool exitHolding;
    // When the finished dungeon had no monster left to fight (for the wait for the treasure box), and how long the box is waited for.
    long completeIdleSince;
    const long BoxWaitMs = 6000;
    readonly ExpeditionRules.GateSearch gateSearch = new ExpeditionRules.GateSearch();

    void StartGrimRequested()
    {
        RequireReader();
        snapshot = reader.Capture();
        // With a queue and the character outside, the start button runs the queue: each dungeon, leave it, the next one.
        if (queue.Count > 0 && !queueRunning && !V3Data.IsCrypt(snapshot.Player))
        {
            StartQueue();
            return;
        }
        var d = SelectedDungeon();
        if (d == null)
            throw new InvalidOperationException("Dungeon seç.");
        if (V3Data.IsCrypt(snapshot.Player))
        {
            ArmDungeon();
            return;
        }
        WalkDungeon();
    }

    bool WalkingTooFar(DungeonLocation d)
    {
        try
        {
            if (V3Data.Town(snapshot.Player) != null || !snapshot.Items.Any(i => ExpeditionRules.ReturnIds.Contains(i.Model) &&
                i.Quantity > 0 && i.Type != 0 && i.Slot >= 13))
                return false;
            var p = snapshot.Player;
            double len = 0;
            foreach (var s in ExpeditionRules.Plan(p, d.Entrance()))
            {
                var q = s.To.Point();
                len += p.Distance(q);
                p = q;
            }
            bool far = len > 1500;
            if (far)
                Log("Yürüyüş yolu " + (int)len + " birim; Return Scroll ile şehre dönülüp oradan gidilecek.");
            return far;
        }
        catch
        {
            return false;
        }
    }

    void PrepareExpedition(DungeonLocation d)
    {
        bool free = d.Mode == "Serbest";
        if (!free && autoEnterGrim.Checked && (!RunSendKeys || (!skillListBox.Checked && string.IsNullOrWhiteSpace(RunKeys))))
            throw new InvalidOperationException("Otomatik dungeon için saldırı tuşlarını ve Hedef doğrulanınca tuşlara bas seçeneğini ayarla.");
        if (!free)
            completionNotice.Begin(reader.Process.Id);
        lastExpeditionPosition = snapshot.Player.Copy();
        mountPending = false;
        transit.Clear();
        expeditionPhase = 0;
        pendingArrival = null;
        portalUid = 0;
        selectedPortal = false;
        gateSearch.Reset();
        knownExit = null;
        exitHolding = false;
        queueExitPhase = 0;
        queueExitStep = 0;
        queueExitTries = 0;
        queueExitSince = 0;
        completeIdleSince = 0;
        if (!free && autoEnterGrim.Checked && !d.IsSolo)
            throw new InvalidOperationException("Otomatik giriş için Solo Trial kaydı seç.");
        excludedRoadCells.Clear();
        useImportedRoad = true;
        // First know where the character is; then choose the way: a jump to the Reverse place nearest to the dungeon, a Return Scroll, or a walk.
        // In the dungeon's own town no scroll is used at all.
        Log("Konum: " + WhereAmI(snapshot.Player));
        reverseTarget = ChooseReversePoint(d);
        if (reverseTarget != null)
            expeditionPhase = 6;
        else if (!caravanWalk && !SameTown(snapshot.Player, d.Entrance()) && ExpeditionRules.Area(snapshot.Player) != ExpeditionRules.Area(d.Entrance()) &&
            (explorerRun != null ? explorerRun.ReturnFirst : returnBeforeTravel.Checked || WalkingTooFar(d)))
            expeditionPhase = 1;
        else
            BuildRoadRoute(d);
    }

    readonly HashSet<int> excludedRoadCells = new HashSet<int>();
    bool useImportedRoad, importedRoadActive;
    // Set while a caravan route is walked: roads only, no return scroll, no riding.
    bool caravanWalk;

    void BuildRoadRoute(DungeonLocation dungeon)
    {
        List<TransitStep> imported = null;
        if (useImportedRoad && dungeon.Mode != "Serbest")
        {
            try
            {
                imported = PhBotRoutes.TryPlan(snapshot.Player, dungeon);
            }
            catch (Exception error)
            {
                Log("phBot yol kaydı okunamadı · " + error.Message);
            }
        }
        importedRoadActive = imported != null;
        var plan = imported ?? PlanRoad(snapshot.Player, dungeon.Entrance());
        // Stairs and gates are missing from the map data: append a path the user walked once by hand, when it joins this route to the entrance.
        if (imported == null)
            plan = ChooseLearned(snapshot.Player, dungeon.Entrance(), plan);
        transit.Clear();
        foreach (var step in plan)
            transit.Enqueue(step);
        expeditionPhase = 3;
        routeWalker.Reset();
        Log((importedRoadActive ? "phBot rotası" : "Harita rotası") + " · " + dungeon.Code + " · " + plan.Count +
            " adım · " + plan.Count(step => step.From != null) + " geçiş");
    }

    // Cells the walker gave up on are remembered between runs, so the same blocked spot is not tried again every time.

    List<TransitStep> PlanRoad(Position start, Position goal)
    {
        var skip = new HashSet<int>(excludedRoadCells);
        foreach (var id in settings.BlockedRoadCells)
            skip.Add(id);
        RoadMesh.WalkOnly = caravanWalk;
        RoadMesh.AllowFerries = caravanWalk;
        try
        {
            var plan = PlanLeg(start, goal, skip);
            if (!RoadMesh.LastLiveApproach || caravanWalk)
                return plan;
            Log("Harita portala bağlı değil; son kısım canlı yürünecek · son bağlı nokta X " + (int)RoadMesh.LastReach.X + " Y " +
                (int)RoadMesh.LastReach.Y + " · boşluk " + (int)RoadMesh.LastReachGap);
            // Remembered blocks can close the only way (a stair cell) without an error: the plan then ends in a dead-end live approach.
            if (settings.BlockedRoadCells.Count == 0)
                return plan;
            List<TransitStep> open = null;
            try
            {
                open = PlanLeg(start, goal, excludedRoadCells);
            }
            catch (InvalidOperationException) { }
            if (open == null || RoadMesh.LastLiveApproach)
                return plan;
            Log("Öğrenilen engeller haritalı rotayı kapattı; liste sıfırlandı.");
            settings.BlockedRoadCells.Clear();
            try
            {
                settings.Save();
            }
            catch { }
            return open;
        }
        catch (InvalidOperationException)
        {
            if (settings.BlockedRoadCells.Count == 0)
                throw;
            Log("Öğrenilen engeller rotayı kapattı; liste sıfırlandı.");
            settings.BlockedRoadCells.Clear();
            try
            {
                settings.Save();
            }
            catch { }
            return PlanLeg(start, goal, excludedRoadCells);
        }
        finally
        {
            RoadMesh.WalkOnly = false;
            RoadMesh.AllowFerries = false;
        }
    }

    // A caravan crossing Jangan's wall is led through the south gate; every other route is planned straight.
    List<TransitStep> PlanLeg(Position start, Position goal, HashSet<int> skip)
    {
        if (!caravanWalk)
            return ExpeditionRules.Plan(start, goal, skip);
        return TradeRoutes.PlanVia(start, goal, (a, b) => ExpeditionRules.Plan(a, b, skip));
    }

    void RememberBlockedCell(int id)
    {
        if (settings.BlockedRoadCells.Contains(id))
            return;
        settings.BlockedRoadCells.Add(id);
        if (settings.BlockedRoadCells.Count > 200)
            settings.BlockedRoadCells.RemoveAt(0);
        try
        {
            settings.Save();
        }
        catch { }
    }

    bool TryReplanRoad(string reason)
    {
        if (operation != OperationMode.Travel || expeditionPhase != 3 || transit.Count == 0 || grim.Location == null ||
            excludedRoadCells.Count >= 4)
            return false;
        var step = transit.Peek();
        if (importedRoadActive && step.From == null)
        {
            useImportedRoad = false;
            Log("phBot ara noktası yerel sorguda kapalı · " + reason);
            BuildRoadRoute(grim.Location);
            return true;
        }
        if (step.CellId < 0 || !excludedRoadCells.Add(step.CellId))
            return false;
        RememberBlockedCell(step.CellId);
        Log("Yol noktası çıkarıldı · " + step.To.X + " / " + step.To.Y + " · " + reason);
        BuildRoadRoute(grim.Location);
        return true;
    }

    void RecordExit()
    {
        if (operation != OperationMode.Grim && operation != OperationMode.Travel)
            return;
        var npc = snapshot.Npcs.Where(n => n.Model == 36031 || n.Model == 36032).OrderBy(n => n.Position.Distance(snapshot.Player))
            .FirstOrDefault();
        if (npc != null)
            knownExit = npc.Position.Copy();
    }

    bool CompletionExit(long now)
    {
        ReleaseKey();
        wanted = 0;
        grim.Route.Clear();
        if (exitHolding)
        {
            // The queue leaves the dungeon through QueueAfterComplete; a single run does it here when the option is on.
            if (exitAfterGrim.Checked && !queueRunning)
                return QueueExit(now, "Grim tamamlandı · ");
            status.Text = "Grim tamamlandı · Dungeon Exit önünde bekliyor. Otomatik çıkış kapalı (Grim sayfasında açılır).";
            return true;
        }
        if (knownExit == null)
        {
            status.Text = "Grim tamamlandı; Dungeon Exit NPC konumu henüz yüklenmedi. Hareket edilmeden bekleniyor.";
            return true;
        }
        if (snapshot.Player.Distance(knownExit) <= 7)
        {
            sender.Move(snapshot.Player.Copy());
            routeWalker.Reset();
            exitHolding = true;
            status.Text = "Grim tamamlandı · Dungeon Exit önünde bekliyor.";
            return true;
        }
        WalkChecked(knownExit, now, grim.IsRegion, "Tamamlandı · Dungeon Exit önüne gidiyor", false);
        return true;
    }

    void ExpeditionTick(long now)
    {
        var d = grim.Location;
        if (d == null)
        {
            Stop("Dungeon seçimi kayboldu.");
            return;
        }
        if (explorerRun != null && ExplorerTick(now))
            return;
        if (grim.IsRegion(snapshot.Player))
        {
            if (!d.IsSolo && autoEnterGrim.Checked)
            {
                Stop("Beklenmeyen dungeon modu.");
                return;
            }
            operation = OperationMode.Grim;
            grim.ContextConfirmed = true;
            grim.Observe(snapshot);
            routeWalker.Reset();
            RecordExit();
            return;
        }
        bool positionJumped = ExpeditionRules.PositionJumped(lastExpeditionPosition, snapshot.Player);
        lastExpeditionPosition = snapshot.Player.Copy();
        if (positionJumped && (expeditionPhase == 0 || expeditionPhase == 3))
        {
            portalUid = 0;
            selectedPortal = false;
            gateSearch.Reset();
            excludedRoadCells.Clear();
            useImportedRoad = true;
            Log("Karakter konumu değişti · güncel konumdan rota hesaplanıyor");
            BuildRoadRoute(d);
        }
        if (expeditionPhase == 6 || expeditionPhase == 7)
        {
            ReverseTravelTick(d, now);
            return;
        }
        if (expeditionPhase == 1)
        {
            if (!EnsureTravelMount(false, now))
                return;
            if (V3Data.Town(snapshot.Player) != null || (ExpeditionRules.Area(snapshot.Player) == "Nanzhang" && snapshot.Player.Distance(ExpeditionRules.Network.Gates.First(g =>
                g.Id == 252).Point()) < 160))
            {
                BuildRoadRoute(d);
                return;
            }
            var item = snapshot.Items.Where(i => ExpeditionRules.ReturnIds.Contains(i.Model) && i.Quantity > 0 && i.Type != 0 &&
                i.Slot >= 13).OrderBy(i => i.Slot).FirstOrDefault();
            if (item == null)
            {
                if (explorerRun != null)
                {
                    // an Explorer run goes on without the scroll: the road to the next area is walked and taken by the gates
                    Log("Return Scroll yok; sonraki bölgeye yürünerek / kapılarla gidilecek.");
                    BuildRoadRoute(d);
                    return;
                }
                Stop("Çantada kullanılabilir Return Scroll yok.");
                return;
            }
            if (!reader.ValidateItem(item))
            {
                Stop("Return Scroll tekrar doğrulanamadı.");
                return;
            }
            returnOrigin = snapshot.Player.Copy();
            if (!sender.UseReturn(item))
            {
                Stop("Return Scroll isteği reddedildi.");
                return;
            }
            Log("Return Scroll kullanımı istendi · ID " + item.Model + " slot " + item.Slot);
            expeditionSince = now;
            expeditionPhase = 2;
            status.Text = "Return Scroll kullanıldı; şehir varışı bekleniyor.";
            return;
        }
        if (expeditionPhase == 2)
        {
            if (returnOrigin == null || returnOrigin.Distance(snapshot.Player) < 96 || (V3Data.Town(snapshot.Player) == null &&
                !(ExpeditionRules.Area(snapshot.Player) == "Nanzhang" && snapshot.Player.Distance(ExpeditionRules.Network.Gates.First(g =>
                g.Id == 252).Point()) < 160)))
            {
                if (now - expeditionSince > 90000)
                    Stop("Return Scroll şehir varışı doğrulanamadı; tekrar eşya kullanılmadı.");
                return;
            }
            if (now - expeditionSince < 5000)
                return;
            BuildRoadRoute(d);
            routeWalker.Reset();
        }
        if (expeditionPhase == 5)
        {
            // The climbers' teleport lands right above or below where it was taken (about 30 units away): there the height tells the levels apart.
            bool climbGate = pendingArrival.Id >= 441 && pendingArrival.Id <= 444;
            if (snapshot.Player.Distance(pendingArrival.Point()) < 65 && (!climbGate || Math.Abs(snapshot.Player.Height - pendingArrival.Height) < 100))
            {
                Log("Teleport varışı doğrulandı · " + pendingArrival.Code);
                pendingArrival = null;
                selectedPortal = false;
                gateSearch.Reset();
                BuildRoadRoute(d);
            }
            else
            {
                if (now - expeditionSince > 60000)
                    Stop("Teleport varışı doğrulanamadı; yeni geçiş gönderilmedi.");
                return;
            }
        }
        if (expeditionPhase == 3 && !routeWalker.Active && transit.Count > 1)
        {
            int skip = ExpeditionRules.WalkShortcut(transit, snapshot.Player, sender.NavigationProbe);
            if (skip > 0)
                Log("Yürüyüş kısaltıldı · " + skip + " ara nokta atlandı");
            for (int i = 0; i < skip; i++)
                transit.Dequeue();
        }
        if (expeditionPhase == 3)
        {
            if (transit.Count == 0)
            {
                expeditionPhase = 0;
                routeWalker.Reset();
            }
            else
            {
                var step = transit.Peek();
                bool hop = step.From != null && ExpeditionRules.Network.Links.Any(l => l.From == step.From.Id && l.To == step.To.Id);
                var approach = hop ? step.From : step.To;
                var npc = hop ? ExpeditionRules.FindGateNpc(snapshot, step.From) : null;
                if (!hop)
                {
                    if (!ExpeditionRules.ApproachReached(snapshot.Player, approach.Point()))
                    {
                        WalkChecked(approach.Point(), now, null, "Ulaşım: " + approach.Code, false);
                        return;
                    }
                    transit.Dequeue();
                    routeWalker.Reset();
                    return;
                }
                // A hop: once the gate's NPC is loaded (the client keeps the NPCs within about 45 units) selecting it and taking its teleport work from
                // where the character stands (tried live at 34 and 42 units). Until then: to the gate's own point, then step towards the place the
                // building table gives for the NPC. The two steps are never mixed, so the character does not walk back and forth between them.
                var gatePlace = ExpeditionRules.NpcPlace(step.From);
                var standNow = gatePlace == null ? null : ExpeditionRules.StandNear(snapshot.Player, gatePlace, gateSearch.Stand);
                var gateStep = gateSearch.Next(npc != null, ExpeditionRules.ApproachReached(snapshot.Player, step.From.Point()), gatePlace != null,
                    standNow != null && snapshot.Player.Distance(standNow) < 3, now);
                if (gateStep == ExpeditionRules.GateStep.WalkToGate)
                {
                    WalkChecked(step.From.Point(), now, null, "Ulaşım: " + approach.Code, false);
                    return;
                }
                if (gateStep == ExpeditionRules.GateStep.WalkToNpc)
                {
                    WalkChecked(ExpeditionRules.StandNear(snapshot.Player, gatePlace, gateSearch.Stand), now, null,
                        "Ulaşım: " + approach.Code + " · kapı NPC'sine doğru (" + (int)gateSearch.Stand + ")", false);
                    return;
                }
                // A caravan never changes mount: the transport it rides stays summoned for the ferry.
                if (!caravanWalk && !EnsureTravelMount(false, now))
                    return;
                if (npc == null || !reader.ValidateNpc(npc))
                {
                    Stop("Geçiş NPC'si yakında okunamadı: " + step.From.Code);
                    return;
                }
                if (!selectedPortal)
                {
                    if (!sender.Send(npc.Uid))
                    {
                        Stop("Geçiş NPC seçimi reddedildi.");
                        return;
                    }
                    portalUid = npc.Uid;
                    selectedPortal = true;
                    expeditionSince = now;
                    return;
                }
                if (snapshot.Selected != portalUid)
                {
                    if (now - expeditionSince > 5000)
                        Stop("NPC seçimi doğrulanamadı.");
                    return;
                }
                if (now - expeditionSince < 600)
                    return;
                gateSearch.Reset();
                if (!sender.Teleport(portalUid, step.From.Id, step.To.Id))
                {
                    Stop("Geçiş isteği reddedildi.");
                    return;
                }
                var feeLink = ExpeditionRules.Network.Links.First(l => l.From == step.From.Id && l.To == step.To.Id);
                Log("Teleport istendi · " + step.From.Code + " → " + step.To.Code + " UID " + portalUid + (feeLink.Fee > 0 ? " · " + feeLink.Fee + " altın" : ""));
                transit.Dequeue();
                pendingArrival = step.To;
                expeditionSince = now;
                expeditionPhase = 5;
                status.Text = "Geçiş varışı bekleniyor: " + step.To.Code;
                return;
            }
        }
        if (expeditionPhase == 4)
        {
            if (now - expeditionSince > 60000)
            {
                Stop("Solo Trial girişi doğrulanamadı; tekrar giriş gönderilmedi.");
                return;
            }
            status.Text = "Solo Trial yüklenmesi bekleniyor.";
            return;
        }
        var portal = snapshot.Npcs.Where(n => n.Model == (uint)d.GateModel && n.Position.Distance(d.Entrance()) < 160).OrderBy(n =>
            n.Position.Distance(d.Entrance())).FirstOrDefault();
        var entrancePoint = portal == null ? d.Entrance() : portal.Position;
        if (!ExpeditionRules.ApproachReached(snapshot.Player, entrancePoint))
        {
            WalkChecked(entrancePoint, now, null, "Grim girişine yürüyor: " + d.Code, false);
            return;
        }
        if (d.Mode == "Serbest")
        {
            if (explorerRun != null)
            {
                ExplorerArrived(now);
                return;
            }
            if (d.Code == "GOREV")
                NotifyDone("Göreve varıldı", d.Name + " · karakter görev NPC’sinin yanında.");
            else
                NotifyDone(caravanWalk ? "Kervan rotası bitti" : "Yürüyüş bitti", d.Name + (caravanWalk ? " · karakter hedef tüccarın yanında." :
                    " · karakter hedefe ulaştı."));
            Stop("Hedefe ulaşıldı · " + d.Name);
            return;
        }
        if (!EnsureTravelMount(false, now))
            return;
        if (!autoEnterGrim.Checked)
        {
            Stop("Dungeon girişine ulaşıldı; otomatik giriş kapalı.");
            return;
        }
        if (!completionNotice.Ready)
        {
            if (!string.IsNullOrEmpty(completionNotice.Error))
            {
                Stop(completionNotice.Error);
                return;
            }
            status.Text = "Önceki bitiş mesajları ayrılıyor; otomatik giriş hazırlanıyor.";
            return;
        }
        if (portal == null || !reader.ValidateNpc(portal))
        {
            Stop("Grim portal NPC'si okunamadı.");
            return;
        }
        if (!selectedPortal)
        {
            if (!sender.Send(portal.Uid))
            {
                Stop("Portal seçimi reddedildi.");
                return;
            }
            portalUid = portal.Uid;
            selectedPortal = true;
            expeditionSince = now;
            return;
        }
        if (snapshot.Selected != portalUid)
        {
            if (now - expeditionSince > 5000)
                Stop("NPC seçimi doğrulanamadı.");
            return;
        }
        if (now - expeditionSince < 600)
            return;
        var target = ExpeditionRules.Network.Gates.FirstOrDefault(g => g.Code == d.SoloGateCode);
        if (target == null)
        {
            Stop("Solo Trial hedef kaydı yok.");
            return;
        }
        if (!sender.Teleport(portalUid, d.GateId, target.Id))
        {
            Stop("Solo Trial giriş isteği reddedildi.");
            return;
        }
        grim.Arm(d, snapshot.Player);
        expeditionSince = now;
        expeditionPhase = 4;
        status.Text = "Solo Trial girişi istendi; içerisi doğrulanıyor.";
    }
}

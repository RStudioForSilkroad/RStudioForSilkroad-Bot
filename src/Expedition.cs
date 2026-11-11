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

    internal static bool TryGround(Position p, out float height)
    {
        return roads.Value.TryGround(p, out height);
    }
}

sealed partial class MemorySelectV3Form
{
    readonly CompletionNotice completionNotice = new CompletionNotice();
    readonly CheckBox autoEnterGrim = new CheckBox();
    readonly Queue<TransitStep> transit = new Queue<TransitStep>();
    int expeditionPhase;
    long expeditionSince;
    TransitGate pendingArrival;
    uint portalUid;
    bool selectedPortal;
    Position returnOrigin, knownExit, lastExpeditionPosition;
    bool exitHolding;

    void StartGrimRequested()
    {
        RequireReader();
        snapshot = reader.Capture();
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
        if (!free && autoEnterGrim.Checked && (!RunSendKeys || string.IsNullOrWhiteSpace(RunKeys)))
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
        knownExit = null;
        exitHolding = false;
        if (!free && autoEnterGrim.Checked && d.Mode != "Solo Trial")
            throw new InvalidOperationException("Otomatik giriş için Solo Trial kaydı seç.");
        excludedRoadCells.Clear();
        useImportedRoad = true;
        if ((returnBeforeTravel.Checked && ExpeditionRules.Area(snapshot.Player) != ExpeditionRules.Area(d.Entrance())) ||
            WalkingTooFar(d))
            expeditionPhase = 1;
        else
            BuildRoadRoute(d);
    }

    readonly HashSet<int> excludedRoadCells = new HashSet<int>();
    bool useImportedRoad, importedRoadActive;

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
        try
        {
            return ExpeditionRules.Plan(start, goal, skip);
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
            return ExpeditionRules.Plan(start, goal, excludedRoadCells);
        }
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
            status.Text = "Grim tamamlandı · Dungeon Exit önünde bekliyor. Otomatik çıkış kapalı.";
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
        if (grim.IsRegion(snapshot.Player))
        {
            if (d.Mode != "Solo Trial" && autoEnterGrim.Checked)
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
            excludedRoadCells.Clear();
            useImportedRoad = true;
            Log("Karakter konumu değişti · güncel konumdan rota hesaplanıyor");
            BuildRoadRoute(d);
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
            if (snapshot.Player.Distance(pendingArrival.Point()) < 65)
            {
                Log("Teleport varışı doğrulandı · " + pendingArrival.Code);
                pendingArrival = null;
                selectedPortal = false;
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
                var destination = npc == null ? approach.Point() : npc.Position;
                if (!ExpeditionRules.ApproachReached(snapshot.Player, destination))
                {
                    WalkChecked(destination, now, null, "Ulaşım: " + approach.Code + (npc == null ? "" :
                        " · Canlı NPC’ye yaklaş"), false);
                    return;
                }
                if (!hop)
                {
                    transit.Dequeue();
                    routeWalker.Reset();
                    return;
                }
                if (!EnsureTravelMount(false, now))
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
                if (!sender.Teleport(portalUid, step.From.Id, step.To.Id))
                {
                    Stop("Geçiş isteği reddedildi.");
                    return;
                }
                Log("Teleport istendi · " + step.From.Code + " → " + step.To.Code + " UID " + portalUid);
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
        var target = ExpeditionRules.Network.Gates.FirstOrDefault(g => g.Code == "GATE_NE_GRIM_" + d.Code + "_S");
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

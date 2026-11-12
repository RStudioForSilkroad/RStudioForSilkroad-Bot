using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

static class V3Data
{
    internal static T Load<T>(string name)
    {
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
        {
            if (s == null)
                throw new IOException("Gömülü veri yok: " + name);
            using (var r = new StreamReader(s, Encoding.UTF8))
                return new JavaScriptSerializer().Deserialize<T>(r.ReadToEnd());
        }
    }

    internal static readonly List<DungeonLocation> Dungeons = Load<List < DungeonLocation>>("grim-locations.json");
    internal static readonly Dictionary<string, string> Towns = Load<Dictionary < string, string>>("town-regions.json");

    internal static bool IsCrypt(Position p)
    {
        return p != null && p.Outside && Dungeons.Any(d => p.Region == d.InnerRegion || p.Region == d.InnerRegion + 256);
    }

    internal static string Town(Position p)
    {
        string name;
        return p != null && p.Outside && Towns.TryGetValue(p.Region.ToString(), out name) ? name : null;
    }
}

sealed class DungeonLocation
{
    public string Code, Name, Area, Mode, Url;
    public int InstanceId, GateId, GateModel, Region, InnerRegion;
    public double X, Y, LocalX, LocalY, Height, InnerX, InnerY, InnerHeight;
    public bool RouteVerified;

    internal Position Entrance()
    {
        return new Position { Region = (ushort)Region, X = X, Y = Y, LocalX = (float)LocalX, Height = (float)Height,
            LocalY = (float)LocalY };
    }

    public override string ToString()
    {
        return Code + " · " + Name + " · " + Mode;
    }
}

sealed class GrimObservation
{
    public bool Known;
    public uint Id;
    public string Title = "", Text = "";
    // Only positive, exact server state tokens count. Empty loaded space is never completion.

    internal bool Completed
    {
        get
        {
            return Known && Regex.IsMatch(Text.Trim(),
                @"^(?:COMPLETED|CLEARED|SUCCESS|Dungeon completed|Dungeon cleared|Grim Crypt completed)[.!]?$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
    }
}

static class RecoveryRules
{
    internal static readonly HashSet<uint> ReverseIds = new HashSet<uint> { 3795, 24449, 36772, 63319 };

    internal static bool IsReverse(BagItem i)
    {
        return i != null && ReverseIds.Contains(i.Model) && i.Slot >= 13 && i.Slot < 173 && i.Quantity > 0 && i.Type != 0;
    }

    internal static BagItem PickReverse(Snapshot s)
    {
        return s.InventoryKnown ? s.Items.Where(IsReverse).OrderBy(i => i.Model == 63319 ? 0 : i.Model == 24449 ? 1 : i.Model == 36772 ?
            2 : 3).ThenBy(i => i.Slot).FirstOrDefault() : null;
    }
}

enum RecoveryPhase
{
    Ready,
    WaitingRevive,
    TownRequested,
    SettlingTown,
    ReverseRequested,
    Halted
}

enum RecoveryAction
{
    None,
    ReturnTown,
    Reverse,
    Resume,
    Stop
}

sealed class DeathRecovery
{
    internal RecoveryPhase Phase;
    internal int Deaths;
    internal Position DeathPoint;
    internal long Since;
    internal string Message = "";
    long townStable;
    Position townPoint;
    bool deadLatched;

    internal void Begin()
    {
        Phase = RecoveryPhase.Ready;
        deadLatched = false;
        townStable = 0;
        townPoint = null;
    }

    internal void Stop()
    {
        Phase = RecoveryPhase.Halted;
    }

    internal void ResetCount()
    {
        if (Phase != RecoveryPhase.Ready && Phase != RecoveryPhase.Halted)
            throw new InvalidOperationException("Önce kurtarma işlemini durdur.");
        Deaths = 0;
        deadLatched = false;
    }

    RecoveryAction Halt(string text)
    {
        Phase = RecoveryPhase.Halted;
        Message = text;
        return RecoveryAction.Stop;
    }

    internal RecoveryAction Observe(Snapshot s, long now, bool focused, bool enabled, int waitSeconds, int maxDeaths)
    {
        if (Phase == RecoveryPhase.Halted)
            return RecoveryAction.Stop;
        if (!s.PlayerAlive && !deadLatched)
        {
            deadLatched = true;
            Deaths++;
            DeathPoint = s.Player.Copy();
            Since = now;
            townStable = 0;
            townPoint = null;
            Phase = RecoveryPhase.WaitingRevive;
            if (Deaths >= maxDeaths)
                return Halt("Ölüm sınırına ulaşıldı (" + Deaths + "/" + maxDeaths + "). Bot durdu.");
            if (!enabled)
                return Halt("Karakter öldü; otomatik geri dönüş kapalı.");
        }
        if (s.PlayerAlive)
            deadLatched = false;
        if (Phase == RecoveryPhase.Ready)
            return RecoveryAction.None;
        if (Phase == RecoveryPhase.WaitingRevive)
        {
            if (s.PlayerAlive)
            {
                Phase = RecoveryPhase.Ready;
                Message = "Canlandırıldı; devam ediliyor.";
                return RecoveryAction.Resume;
            }
            long left = Math.Max(0, waitSeconds - (now - Since) / 1000);
            Message = "Canlandırılma bekleniyor · " + left + " sn · Ölüm " + Deaths + "/" + maxDeaths;
            if (left == 0 && focused)
            {
                Phase = RecoveryPhase.TownRequested;
                Since = now;
                Message = "Şehre dönüş istendi; yüklenme bekleniyor.";
                return RecoveryAction.ReturnTown;
            }
        }
        else if (Phase == RecoveryPhase.TownRequested || Phase == RecoveryPhase.SettlingTown)
        {
            if (now - Since > 90000)
                return Halt("Şehre dönüş 90 sn içinde doğrulanmadı.");
            if (s.PlayerAlive && SameSpace(s.Player, DeathPoint) && s.Player.Distance(DeathPoint) < 60)
            {
                Phase = RecoveryPhase.Ready;
                Message = "Ölüm noktasında canlandırıldı; devam ediliyor.";
                return RecoveryAction.Resume;
            }
            string town = V3Data.Town(s.Player);
            if (!s.PlayerAlive || town == null)
            {
                townStable = 0;
                Message = "Şehre dönüş ve canlı karakter doğrulaması bekleniyor.";
                return RecoveryAction.None;
            }
            if (townPoint == null || townPoint.Distance(s.Player) > 2)
            {
                townPoint = s.Player.Copy();
                townStable = now;
            }
            Phase = RecoveryPhase.SettlingTown;
            Message = town + " yüklendi; Reverse Scroll hazırlanıyor.";
            if (now - townStable >= 5000 && focused)
            {
                if (RecoveryRules.PickReverse(s) == null)
                    return Halt("Çantada kullanılabilir Reverse Return Scroll yok; bot durdu.");
                Phase = RecoveryPhase.ReverseRequested;
                Since = now;
                Message = "Reverse: ölüm noktasına dönüş doğrulanıyor.";
                return RecoveryAction.Reverse;
            }
        }
        else if (Phase == RecoveryPhase.ReverseRequested)
        {
            if (s.PlayerAlive && SameSpace(s.Player, DeathPoint) && s.Player.Distance(DeathPoint) <= 35)
            {
                Phase = RecoveryPhase.Ready;
                Message = "Ölüm noktasına dönüş doğrulandı; devam ediliyor.";
                return RecoveryAction.Resume;
            }
            if (now - Since > 90000)
                return Halt("Reverse dönüşü doğrulanmadı; tekrar scroll tüketilmeden durdu.");
        }
        return RecoveryAction.None;
    }

    internal static bool SameSpace(Position a, Position b)
    {
        return a.Outside == b.Outside && (a.Outside || a.Region == b.Region);
    }
}

static class MultiTarget
{
    internal static Monster Choose(Snapshot s, ISet<string> names, Position center, double radius, IDictionary<uint,
        long> bans, long now, uint wanted)
    {
        Func<Monster, bool> ok = m => m.Alive && names.Contains(m.Name) && TargetPolicy.InArea(m.Position, center,
            radius) && (!bans.ContainsKey(m.Uid) || bans[m.Uid] <= now);
        return s.Monsters.FirstOrDefault(m => m.Uid == wanted && ok(m)) ?? s.Monsters.Where(ok).OrderBy(m => m.Position.Distance(s.Player))
            .ThenBy(m => m.Uid).FirstOrDefault();
    }
}

sealed class TravelWatch
{
    internal Position Destination;
    internal long ProgressAt, NextMove;
    internal double Best;
    internal int Rejections;

    internal void Set(Position p, Position start, long now)
    {
        Destination = p.Copy();
        ProgressAt = now;
        NextMove = 0;
        Best = start.Distance(p);
        Rejections = 0;
    }

    internal bool Stuck(Position p, long now)
    {
        double dist = p.Distance(Destination);
        if (dist < Best - 1)
        {
            Best = dist;
            ProgressAt = now;
            Rejections = 0;
        }
        return now - ProgressAt >= 20000;
    }

    internal void Pause(long ms)
    {
        ProgressAt += Math.Max(0, ms);
    }

    internal Position Step(Position current)
    {
        double d = current.Distance(Destination);
        if (d <= 32)
            return Destination.Copy();
        double f = 32 / d;
        return V2Rules.Center(current.X + (Destination.X - current.X) * f, current.Y + (Destination.Y - current.Y) * f,
            current.Height);
    }
}

sealed class GrimRun
{
    internal DungeonLocation Location;
    internal bool Armed, Running, Complete, ContextConfirmed, SeenMob;
    internal int Waypoint, PathFailures;
    internal long EmptySince = -1;
    internal FloorNavigation Floor;
    internal GrimRooms Rooms;
    internal string Message = "Dungeon seçilmedi.";
    internal uint EntryHudId;
    internal List<Position> Route = new List<Position>();
    internal readonly HashSet<uint> Blocked = new HashSet<uint>();

    internal bool IsRegion(Position p)
    {
        return Location != null && p.Outside && (p.Region == Location.InnerRegion || p.Region == Location.InnerRegion +
            256);
    }

    internal void Arm(DungeonLocation d)
    {
        Location = d;
        Armed = true;
        Running = Complete = ContextConfirmed = SeenMob = false;
        EmptySince = -1;
        Floor = null;
        Rooms = null;
        Waypoint = PathFailures = 0;
        Route.Clear();
        Blocked.Clear();
        Message = "Giriş bekleniyor: " + d;
    }

    internal void Arm(DungeonLocation d, Position current)
    {
        Arm(d);
        ContextConfirmed = IsRegion(current) || (current.Outside && current.Distance(d.Entrance()) <= 45);
    }

    internal bool Observe(Snapshot s)
    {
        bool matches = Location != null && ((s.Grim.Known && (s.Grim.Id == (uint)Location.InstanceId || Regex.IsMatch(s.Grim.Title,
            @"\b" + Regex.Escape(Location.Code) + @"\b", RegexOptions.IgnoreCase))) || ContextConfirmed);
        if (Armed && !Running && IsRegion(s.Player) && matches)
        {
            Running = true;
            EntryHudId = s.Grim.Id;
            Floor = FloorNavigation.LoadCaptured(Location) ?? new FloorNavigation(s.Player, Location.InnerRegion);
            Rooms = GrimRooms.Load(Location, Floor);
            Message = "Grim Crypt algılandı; oyun navigasyonundan zemin okunuyor.";
            return true;
        }
        if (Armed && !Running && IsRegion(s.Player) && !matches)
            Message = "İç bölge görüldü; seçilen dungeon / mod ile HUD kimliği henüz eşleşmedi. Komut gönderilmiyor.";
        if (!Running)
            return false;
        if (!IsRegion(s.Player) || (EntryHudId != 0 && (!s.Grim.Known || s.Grim.Id != EntryHudId)))
        {
            Message = "Dungeon / oturum değişti. Durduruldu.";
            Running = Armed = false;
            return false;
        }
        if (Rooms != null)
            Rooms.Observe(s.Monsters.Where(m => IsRegion(m.Position)));
        if (s.Grim.Completed || (Location.Mode == "Solo Trial" && ExpeditionRules.Cleared(s.Grim.Text, Location.Code)))
        {
            Complete = true;
            Running = Armed = false;
            Message = "Sunucu tamamlanma durumu doğrulandı. Bekliyor.";
        }
        return false;
    }

    internal void MapStep(Snapshot s, TargetSender sender, int budget)
    {
        if (Floor == null)
            return;
        Floor.Advance(sender.NavigationProbe, budget);
        Floor.ObservePosition(s.Player, sender.NavigationProbe);
        if (s.Monsters.Any(m => m.Alive && IsRegion(m.Position)))
        {
            SeenMob = true;
            EmptySince = -1;
        }
    }

    internal bool CanFinish(Snapshot s, long now)
    {
        if (Floor == null || !Floor.FullyCovered || !IsRegion(s.Player) || s.Monsters.Any(m => m.Alive && IsRegion(m.Position)))
        {
            EmptySince = -1;
            return false;
        }
        if (EmptySince < 0)
            EmptySince = now;
        return now - EmptySince >= 15000;
    }

    internal Monster Choose(Snapshot s, Dictionary<uint, long> bans, long now, uint wanted)
    {
        Func<Monster, bool> ok = m => m.Alive && m.Health != 0 && IsRegion(m.Position) && (Rooms == null || (Complete ?
            GrimRooms.Optional(m) : (Rooms.Allows(m) || m.Position.Distance(s.Player) <= 60))) && (!bans.ContainsKey(m.Uid) ||
            bans[m.Uid] <= now);
        return s.Monsters.FirstOrDefault(m => m.Uid == wanted && ok(m)) ?? s.Monsters.Where(ok).OrderBy(m => m.Position.Distance(s.Player))
            .ThenBy(m => m.Uid).FirstOrDefault();
    }

    internal void Stop()
    {
        Armed = Running = false;
    }
}

static class DungeonExtensions
{
    internal static Position InnerPosition(this DungeonLocation d)
    {
        return V2Rules.Center(((d.InnerRegion & 255) - 135) * 192 + d.InnerX / 10, ((d.InnerRegion >> 8) - 92) * 192 + d.InnerY / 10,
            (float)d.InnerHeight);
    }
}

sealed class V3Settings
{
    public bool ReturnBeforeTravel, TravelMount, AutoEnterGrim, UniqueAlert, SkillList;
    public List<SkillSlot> Skills = new List<SkillSlot>();
    public bool BackgroundWalk = true;
    public List<int> ExplorerDone = new List<int>();
    public bool ExplorerSkipDone = true;
    public int ExplorerModel = ExplorerSpots.DefaultModel;
    public List<string> DungeonQueue = new List<string>();
    public bool QueueReturnScroll = true;
    public bool HideWhatsNew;
    public List<int> BlockedRoadCells = new List<int>();
    public string LastSeenVersion = "";
    public List<string> LearnedPaths = new List<string>();
    public List<string> IgnoredDefaults = new List<string>();
    public int Theme;
    public List<string> VisitedDungeons = new List<string>();
    public bool AutoUpdate = true;
    public string UpdateManifestUrl = UpdateTrust.DefaultManifestUrl;
    public int HotkeyKey, HotkeyModifiers;
    public List<string> Mobs = new List<string>();
    public double X, Y, Range = 150;
    public int Delay = 100, Timeout = 10, ReviveSeconds = 30, DeathLimit = 5, Deaths;
    public string Keys = "1 2 3 4 5 6", BerserkClass = "Giant";
    public bool SendKeys = true, GoHome = true, Berserk = true, Speed = true, Repair = true, Recovery = true;

    internal static string DirectoryPath
    {
        get
        {
            return Path.Combine(AppPaths.DataRoot, Environment.GetEnvironmentVariable("SRSTUDIO_ACCOUNT") == "1" ? "Account1" :
                Environment.GetEnvironmentVariable("SRSTUDIO_ACCOUNT") == "2" ? "Account2" : "");
        }
    }

    internal static string PathName
    {
        get { return Path.Combine(DirectoryPath, "settings.json"); }
    }

    internal static V3Settings Load()
    {
        if (!File.Exists(PathName))
            return new V3Settings();
        try
        {
            var loaded = new JavaScriptSerializer().Deserialize<V3Settings>(File.ReadAllText(PathName, Encoding.UTF8)) ?? new V3Settings();
            if (string.Equals(loaded.UpdateManifestUrl, UpdateTrust.LegacyManifestUrl, StringComparison.OrdinalIgnoreCase))
                loaded.UpdateManifestUrl = UpdateTrust.DefaultManifestUrl;
            return loaded;
        }
        catch
        {
            return new V3Settings();
        }
    }

    internal void Save()
    {
        Directory.CreateDirectory(DirectoryPath);
        string temp = PathName + ".tmp";
        File.WriteAllText(temp, new JavaScriptSerializer().Serialize(this), Encoding.UTF8);
        if (File.Exists(PathName))
            File.Replace(temp, PathName, PathName + ".bak");
        else
            File.Move(temp, PathName);
    }
}

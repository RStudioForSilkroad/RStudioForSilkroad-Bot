using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

sealed class BagItem
{
    public uint Model, Quantity, Address;
    public int Slot;
    public ushort Type;
}

static class V2Rules
{
    internal static readonly string[] BerserkClasses = { "General", "Champion", "Giant", "General Party",
        "Champion Party", "Giant Party", "Unique" };

    internal static string ClassName(Monster m)
    {
        if (m.Rank == 3)
            return "Unique";
        string name = m.Rank == 0 || m.Rank == 2 ? "General" : m.Rank == 1 ? "Champion" : m.Rank == 4 || m.Rank == 5 ?
            "Giant" : null;
        if (name == null)
            return m.Rank < 0 ? "Okunamadı" : "Diğer";
        return name + (m.Party ? " Party" : "");
    }

    internal static bool BerserkMatches(Monster target, string selected)
    {
        return target != null && Array.IndexOf(BerserkClasses, selected) >= 0 && ClassName(target) == selected;
    }

    internal static readonly HashSet<uint> ItemIds = new HashSet<uint> { 7100, 63361, 3784, 24458 };
    internal static readonly HashSet<uint> SpeedSkills = new HashSet<uint> { 113, 115, 1276, 1277, 1278, 1279, 1280,
        1281, 1282, 1283, 1284, 1285, 1286, 1298, 1299, 1300, 1301, 1302, 1303, 1304, 1305, 1306, 1307, 1308, 3644, 3673,
        3677, 3966, 3974, 3975, 3976, 3977, 5410, 5411, 7100, 7102, 7114, 31103, 31201, 32091, 32096, 33077, 34610,
        34611 };

    internal static uint Bits(float f)
    {
        return BitConverter.ToUInt32(BitConverter.GetBytes(f), 0);
    }

    internal static bool AllowedItem(BagItem item)
    {
        if (item == null || item.Quantity == 0 || item.Slot < 13 || item.Slot >= 173)
            return false;
        return (item.Model == 7100 || item.Model == 63361) ? item.Type == 0x0eec : (item.Model == 3784 || item.Model == 24458) &&
            (item.Type & 0xfffe) == 0x3eec;
    }

    internal static BagItem PickItem(IEnumerable<BagItem> items, bool repair)
    {
        uint[] priority = repair ? new uint[] { 24458, 3784 } : new uint[] { 63361, 7100 };
        foreach (uint id in priority)
        {
            var item = items.Where(i => i.Model == id && AllowedItem(i)).OrderBy(i => i.Slot).FirstOrDefault();
            if (item != null)
                return item;
        }
        return null;
    }

    internal static bool NeedsRepair(Snapshot s)
    {
        return s.InventoryKnown && s.MinDurability.HasValue && s.MinDurability.Value <= 4;
    }

    internal static Position Center(double x, double y, float height)
    {
        int sx = (int)Math.Floor(x / 192.0) + 135, sy = (int)Math.Floor(y / 192.0) + 92;
        if (sx < 0 || sx > 255 || sy < 0 || sy > 127)
            throw new ArgumentException("X/Y açık harita sınırları dışında.");
        return new Position { X = x, Y = y, Region = (ushort)(sx | (sy << 8)), LocalX = (float)((x - (sx - 135) * 192) * 10),
            LocalY = (float)((y - (sy - 92) * 192) * 10), Height = height };
    }

    internal static List<Monster> InRange(Snapshot s, Position center, double range)
    {
        return center == null ? new List<Monster>() : s.Monsters.Where(m => TargetPolicy.InArea(m.Position, center,
            range)).ToList();
    }
}

sealed class TargetWatch
{
    internal uint Uid;
    internal long ProgressAt;
    uint? bestHealth;
    double bestDistance;
    bool initialized;

    internal void Reset()
    {
        Uid = 0;
        initialized = false;
    }

    internal bool Stuck(Monster mob, Position player, long now, long timeout)
    {
        double d = mob.Position.Distance(player);
        if (!initialized || Uid != mob.Uid)
        {
            Uid = mob.Uid;
            ProgressAt = now;
            bestHealth = mob.Health;
            bestDistance = d;
            initialized = true;
            return false;
        }
        if ((mob.Health.HasValue && (!bestHealth.HasValue || mob.Health.Value < bestHealth.Value)) || d < bestDistance - 1)
        {
            ProgressAt = now;
            bestHealth = mob.Health;
            bestDistance = d;
        }
        return now - ProgressAt >= timeout;
    }

    internal void Pause(long elapsed)
    {
        if (initialized)
            ProgressAt += Math.Max(0, elapsed);
    }
}

sealed class ItemTransaction
{
    internal string Kind, Message = "Bakım bekliyor.";
    internal bool SpeedPaused, RepairPaused;
    internal long Started;
    internal uint Model, BeforeQuantity;

    internal bool Pending
    {
        get { return Kind != null; }
    }

    internal void Begin(string kind, BagItem item, Snapshot s, long now)
    {
        if (Pending)
            throw new InvalidOperationException("Eşya işlemi zaten bekliyor.");
        Kind = kind;
        Model = item.Model;
        BeforeQuantity = s.Items.Where(i => i.Model == Model).Aggregate(0u, (n, i) => n + i.Quantity);
        Started = now;
        Message = (kind == "repair" ? "Repair Hammer" : "Drug of Typoon") + " kullanımı doğrulanıyor...";
    }

    internal void Observe(Snapshot s, long now)
    {
        if (!Pending)
            return;
        if (Kind == "speed" && s.SpeedKnown && s.SpeedActive)
        {
            Kind = null;
            Message = "Speed etkisi doğrulandı.";
            return;
        }
        if (Kind == "repair" && s.InventoryKnown && s.MinDurability.HasValue && s.MinDurability.Value > 4)
        {
            Kind = null;
            Message = "Ekipman tamiri doğrulandı.";
            return;
        }
        if (now - Started < 10000)
            return;
        if (Kind == "speed")
            SpeedPaused = true;
        else
            RepairPaused = true;
        Message = (Kind == "speed" ? "Speed" : "Repair") +
            " sonucu doğrulanmadı; bu özellik askıya alındı. Tekrar eşya kullanılmayacak.";
        Kind = null;
    }

    internal void Rejected(string kind)
    {
        if (kind == "speed")
            SpeedPaused = true;
        else
            RepairPaused = true;
        Message = kind + " isteği reddedildi; bakım özelliği askıya alındı.";
    }

    internal void Reset()
    {
        if (Pending)
            throw new InvalidOperationException("Bekleyen eşya işlemi bitmeden sıfırlanamaz.");
        SpeedPaused = RepairPaused = false;
        Message = "Bakım hata kilitleri sıfırlandı.";
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

// Finds the character's learned-skill list in the game's memory without a fixed address.
// The client keeps it as a tree whose nodes read: pointer, pointer, skill id, the skill's level, ... (checked on the live client: 66 skills of
// a level 86 character, one level of each skill family, every level equal to the one in the game's skill table). Nodes that look like that are
// joined by their pointers into groups; the group whose levels match the skill table, and that holds the skills the character is known to have
// (active buffs, ids already in the list), is the learned-skill list. The game's whole skill table (about 5400 nodes) is too big to be taken.
static class SkillScan
{
    internal struct Rec
    {
        internal uint Addr, P0, P1, Key, Level;
    }

    const uint HeapLow = 0x10000, HeapHigh = 0xFFFF0000;
    internal const int MaxSkills = 1500;

    static uint U(byte[] b, int at)
    {
        return BitConverter.ToUInt32(b, at);
    }

    static bool Pointer(uint p)
    {
        return p >= HeapLow && p < HeapHigh && (p & 3) == 0;
    }

    // Nodes in a buffer: two pointers (or zero), then a skill id, then the dword after it (the level).
    internal static void Candidates(byte[] buffer, int length, uint baseAddress, bool[] valid, List<Rec> into)
    {
        if (valid == null || valid.Length == 0)
            return;
        for (int i = 8; i + 8 <= length; i += 4)
        {
            uint key = U(buffer, i);
            if (key == 0 || key >= valid.Length || !valid[key])
                continue;
            uint p0 = U(buffer, i - 8), p1 = U(buffer, i - 4);
            if ((p0 != 0 && !Pointer(p0)) || (p1 != 0 && !Pointer(p1)) || (p0 == 0 && p1 == 0))
                continue;
            into.Add(new Rec { Addr = baseAddress + (uint)i - 8, P0 = p0, P1 = p1, Key = key, Level = U(buffer, i + 4) });
        }
    }

    // The learned-skill group among the nodes, or null. seeds are skills the character certainly has (they only rank the groups).
    internal static List<Rec> Choose(List<Rec> recs, ICollection<uint> seeds, Func<uint, int> family, Func<uint, int> level, out string report)
    {
        var timer = Stopwatch.StartNew();
        var index = new Dictionary<uint, int>(recs.Count);
        for (int i = 0; i < recs.Count; i++)
            index[recs[i].Addr] = i;
        var parent = new int[recs.Count];
        for (int i = 0; i < parent.Length; i++)
            parent[i] = i;
        Func<int, int> find = x =>
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }
            return x;
        };
        for (int i = 0; i < recs.Count; i++)
            foreach (uint p in new[] { recs[i].P0, recs[i].P1 })
            {
                int other;
                if (p != 0 && index.TryGetValue(p, out other))
                    parent[find(other)] = find(i);
            }
        var groups = new Dictionary<int, List<Rec>>();
        for (int i = 0; i < recs.Count; i++)
        {
            int g = find(i);
            List<Rec> list;
            if (!groups.TryGetValue(g, out list))
                groups[g] = list = new List<Rec>();
            list.Add(recs[i]);
        }
        var wanted = new HashSet<uint>(seeds ?? new uint[0]);
        List<Rec> best = null;
        double bestScore = -1;
        int considered = 0;
        foreach (var g in groups.Values)
        {
            if (g.Count < 5 || g.Count > MaxSkills)
                continue;
            considered++;
            double levels = g.Count(r => level(r.Key) > 0 && r.Level == (uint)level(r.Key)) / (double)g.Count;
            if (levels < 0.8)
                continue;
            int families = g.Select(r => family(r.Key) == 0 ? -(int)r.Key : family(r.Key)).Distinct().Count();
            if ((double)families / g.Count < 0.8)
                continue;
            int hits = g.Count(r => wanted.Contains(r.Key));
            double score = hits * 10000.0 + g.Count;
            if (score > bestScore)
            {
                bestScore = score;
                best = g;
            }
        }
        report = recs.Count + " aday düğüm, " + considered + " grup" + (best == null ? ", uygun grup yok" : ", seçilen grup " + best.Count + " skill") +
            " · " + (timer.ElapsedMilliseconds / 100 / 10.0).ToString("0.0") + " sn";
        return best;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Region
    {
        internal uint Base, AllocationBase, AllocationProtect, Size, State, Protect, Type;
    }

    [DllImport("kernel32.dll")]
    static extern uint VirtualQueryEx(IntPtr process, IntPtr address, out Region info, uint length);

    // Reads the character's learned skills from the running game.
    internal static List<uint> Read(GameReader reader, ICollection<uint> seeds, out string report)
    {
        var valid = SkillCatalog.ValidTable;
        var recs = new List<Rec>();
        uint address = 0x10000;
        IntPtr handle = reader.Handle;
        var timer = Stopwatch.StartNew();
        while (address < HeapHigh && recs.Count < 3000000 && timer.ElapsedMilliseconds < 20000)
        {
            Region region;
            if (VirtualQueryEx(handle, new IntPtr(unchecked((int)address)), out region, 28) == 0)
                break;
            ulong end = (ulong)region.Base + region.Size;
            if (end <= address)
                break;
            uint protection = region.Protect & 255;
            if (region.State == 0x1000 && region.Type == 0x20000 && (region.Protect & 0x100) == 0 && (protection == 4 || protection == 8 ||
                protection == 0x40 || protection == 0x80) && region.Size <= 512u * 1024 * 1024)
            {
                for (uint part = region.Base; part < end; part += 1024 * 1024)
                {
                    int size = (int)Math.Min(1024 * 1024 + 64, end - part);
                    byte[] bytes = new byte[size];
                    UIntPtr got;
                    if (!Native.ReadProcessMemory(handle, new IntPtr(unchecked((int)part)), bytes, (UIntPtr)size, out got))
                        continue;
                    Candidates(bytes, (int)Math.Min(got.ToUInt64(), (ulong)size), part, valid, recs);
                }
            }
            if (end > HeapHigh)
                break;
            address = (uint)end;
        }
        var group = Choose(recs, seeds, SkillCatalog.FamilyOf, SkillCatalog.LevelOf, out report);
        return group == null ? new List<uint>() : group.Select(r => r.Key).Distinct().OrderBy(k => k).ToList();
    }
}

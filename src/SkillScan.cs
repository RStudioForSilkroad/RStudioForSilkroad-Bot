using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

delegate byte[] MemoryRead(uint address, int size);

// Finds the character's learned-skill list in the game's memory without a fixed address. The client keeps such lists as red-black
// trees (std::map: left, parent, right pointers, then the key). A tree whose keys are all real skill ids and that holds the skills the
// character is known to have (active buffs, ids already typed in the list) is the learned-skill tree. Read only.
static class SkillScan
{
    internal sealed class Tree
    {
        internal uint Header;
        internal bool Truncated;
        internal readonly List<uint> Keys = new List<uint>();
        internal readonly List<uint> Nodes = new List<uint>();
    }

    const uint HeapLow = 0x10000, HeapHigh = 0xFFFF0000;

    static uint U(byte[] b, int at)
    {
        return BitConverter.ToUInt32(b, at);
    }

    static bool Pointer(uint p)
    {
        return p >= HeapLow && p < HeapHigh && (p & 3) == 0;
    }

    // Addresses of nodes in the buffer that look like a tree node whose key is a skill id.
    internal static List<uint> Candidates(byte[] buffer, int length, uint baseAddress, bool[] valid)
    {
        var found = new List<uint>();
        if (valid == null || valid.Length == 0)
            return found;
        for (int i = 12; i + 4 <= length; i += 4)
        {
            uint key = BitConverter.ToUInt32(buffer, i);
            if (key == 0 || key >= valid.Length || !valid[key])
                continue;
            uint left = U(buffer, i - 12), parent = U(buffer, i - 8), right = U(buffer, i - 4);
            if (!Pointer(parent) || (left != 0 && !Pointer(left)) || (right != 0 && !Pointer(right)))
                continue;
            found.Add(baseAddress + (uint)i - 12);
        }
        return found;
    }

    // The header of the tree a node belongs to (the header's parent is the root and the root's parent is the header), or 0.
    internal static uint FindHeader(MemoryRead read, uint node)
    {
        uint x = node;
        for (int step = 0; step < 90; step++)
        {
            uint parent = U(read(x, 12), 4);
            if (!Pointer(parent))
                return 0;
            byte[] p = read(parent, 12);
            if (U(p, 4) == x)
                return parent;
            if (U(p, 0) != x && U(p, 8) != x)
                return 0;
            x = parent;
        }
        return 0;
    }

    internal static Tree Walk(MemoryRead read, uint header, int limit)
    {
        var tree = new Tree { Header = header };
        var stack = new Stack<uint>();
        var seen = new HashSet<uint>();
        stack.Push(U(read(header, 12), 4));
        while (stack.Count > 0)
        {
            uint n = stack.Pop();
            if (!Pointer(n) || n == header || !seen.Add(n))
                continue;
            if (seen.Count > limit)
            {
                tree.Truncated = true;
                break;
            }
            byte[] b = read(n, 16);
            tree.Keys.Add(U(b, 12));
            tree.Nodes.Add(n);
            stack.Push(U(b, 0));
            stack.Push(U(b, 8));
        }
        return tree;
    }

    // Among the trees the candidate nodes belong to, the learned-skill tree: every key a real skill id, the most known skills inside,
    // few levels of the same skill (a list of learned skills holds one level of each), then the larger one.
    internal static Tree Choose(MemoryRead read, IEnumerable<uint> nodes, ICollection<uint> seeds, Func<uint, bool> valid,
        Func<uint, int> family, int seconds, out string report)
    {
        var timer = Stopwatch.StartNew();
        var owner = new Dictionary<uint, uint>();
        var trees = new Dictionary<uint, Tree>();
        var rejected = new HashSet<uint>();
        int examined = 0, total = 0;
        foreach (uint node in nodes)
        {
            total++;
            if (owner.ContainsKey(node))
                continue;
            if (timer.ElapsedMilliseconds > seconds * 1000L || examined >= 60000)
                break;
            examined++;
            uint header;
            try
            {
                header = FindHeader(read, node);
            }
            catch (IOException)
            {
                continue;
            }
            if (header == 0 || rejected.Contains(header) || trees.ContainsKey(header))
                continue;
            try
            {
                var tree = Walk(read, header, 1500);
                foreach (uint n in tree.Nodes)
                    owner[n] = header;
                if (tree.Truncated || tree.Keys.Count < 3 || tree.Keys.Any(k => !valid(k)))
                    rejected.Add(header);
                else
                    trees[header] = tree;
            }
            catch (IOException)
            {
                rejected.Add(header);
            }
        }
        var wanted = new HashSet<uint>(seeds ?? new uint[0]);
        Tree best = null;
        double bestScore = -1;
        foreach (var t in trees.Values)
        {
            int hits = t.Keys.Count(k => wanted.Contains(k));
            if (wanted.Count > 0 && hits == 0)
                continue;
            int families = t.Keys.Select(k => family(k) == 0 ? -(int)k : family(k)).Distinct().Count();
            double spread = (double)families / t.Keys.Count;
            if (spread < 0.8)
                continue;
            double score = hits * 100000.0 + (hits == wanted.Count ? 50000 : 0) + t.Keys.Count;
            if (score > bestScore)
            {
                bestScore = score;
                best = t;
            }
        }
        report = total + " aday düğüm, " + trees.Count + " skill ağacı" + (best == null ? ", uygun ağaç yok" : ", seçilen ağaç " +
            best.Keys.Count + " skill") + " · " + (timer.ElapsedMilliseconds / 100 / 10.0).ToString("0.0") + " sn";
        return best;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Region
    {
        internal uint Base, AllocationBase, AllocationProtect, Size, State, Protect, Type;
    }

    [DllImport("kernel32.dll")]
    static extern uint VirtualQueryEx(IntPtr process, IntPtr address, out Region info, uint length);

    // Reads the character's learned skills from the running game. The seeds are skill ids the character certainly has.
    internal static List<uint> Read(GameReader reader, ICollection<uint> seeds, out string report)
    {
        var valid = SkillCatalog.ValidTable;
        var nodes = new List<uint>();
        uint address = 0x10000;
        IntPtr handle = reader.Handle;
        while (address < HeapHigh && nodes.Count < 400000)
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
                    nodes.AddRange(Candidates(bytes, (int)Math.Min(got.ToUInt64(), (ulong)size), part, valid));
                }
            }
            if (end > HeapHigh)
                break;
            address = (uint)end;
        }
        var tree = Choose(reader.Read, nodes, seeds, SkillCatalog.IsSkill, SkillCatalog.FamilyOf, 12, out report);
        return tree == null ? new List<uint>() : tree.Keys.Distinct().OrderBy(k => k).ToList();
    }
}

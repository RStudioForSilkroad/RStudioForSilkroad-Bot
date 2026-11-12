using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

static class Native
{
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, UIntPtr size, out UIntPtr read);
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, UIntPtr size, out UIntPtr written);
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, UIntPtr size, uint type, uint protection);
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool VirtualProtectEx(IntPtr process, IntPtr address, UIntPtr size, uint protection, out uint old);
    [DllImport("kernel32.dll")]
    internal static extern bool VirtualFreeEx(IntPtr process, IntPtr address, UIntPtr size, uint type);
    [DllImport("kernel32.dll")]
    internal static extern bool FlushInstructionCache(IntPtr process, IntPtr address, UIntPtr size);
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr CreateRemoteThread(IntPtr process, IntPtr attributes, UIntPtr stack, IntPtr start,
        IntPtr parameter, uint flags, out uint id);
    [DllImport("kernel32.dll")]
    internal static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll")]
    internal static extern bool GetExitCodeThread(IntPtr thread, out uint code);
    [DllImport("kernel32.dll")]
    internal static extern bool CloseHandle(IntPtr handle);
    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    internal static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    internal static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll")]
    internal static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")]
    internal static extern uint MapVirtualKeyEx(uint code, uint type, IntPtr layout);
    [DllImport("user32.dll")]
    internal static extern IntPtr GetKeyboardLayout(uint thread);

    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        public uint type;
        public UNION data;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct UNION
    {
        [FieldOffset(0)]
        public KEY key;
        [FieldOffset(0)]
        public MOUSE mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KEY
    {
        public ushort vk, scan;
        public uint flags, time;
        public UIntPtr extra;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSE
    {
        public int x, y;
        public uint data, flags, time;
        public UIntPtr extra;
    }

    internal static IntPtr Ptr(uint address)
    {
        return new IntPtr(unchecked((int)address));
    }

    internal static uint Addr(IntPtr address)
    {
        return unchecked((uint)address.ToInt32());
    }
}

sealed class Position
{
    public ushort Region;
    public double X, Y;
    public float LocalX, Height, LocalY;

    public bool Outside
    {
        get { return (Region & 0x8000) == 0; }
    }

    public double Distance(Position other)
    {
        return Math.Sqrt((X - other.X) * (X - other.X) + (Y - other.Y) * (Y - other.Y));
    }

    public Position Copy()
    {
        return (Position)MemberwiseClone();
    }
}

sealed class Monster
{
    public uint Uid, Model, Address;
    public string Name;
    public Position Position;
    public bool Alive;
    public uint? Health;
    public int Rank = -1;
    public bool Party;
}

sealed class Snapshot
{
    public string CharacterName;
    public bool MountKnown;
    public uint MountUid;
    public List<WorldNpc> Npcs = new List<WorldNpc>();
    public Position Player;
    public uint Selected;
    public GrimObservation Grim = new GrimObservation();
    public bool PlayerAlive;
    public bool StatsKnown;
    public uint Hp, MaxHp, Mp, MaxMp, SpExp, Sp;
    public int Level;
    public ulong Exp;
    public int Berserk = -1;
    public bool SpeedKnown, SpeedActive, InventoryKnown;
    public int? MinDurability;
    public List<BagItem> Items = new List<BagItem>();
    public List<Monster> Monsters = new List<Monster>();
}

sealed class GameReader : IDisposable
{
    const string ClientHash = "51081AC1F776A38FE0AF58E2A961BFA9E8166506AE26DC8DCFD8008736725C05";
    internal const string GuardHash = "3206CF49259ECA33CA6DF4783E1D7192ED722CEEFEAD1925F9C7E46B7F8D1D01";
    readonly IntPtr handle;
    readonly uint baseAddress;
    readonly long started;
    internal readonly Process Process;
    internal uint SenderAddress, GuardBase, MoveAddress, ReverseAddress, NavigationAddress;
    internal string Compatibility = "DLL bağlantısı yok";
    public readonly Dictionary<uint, string> Names;

    static string Hash(string path)
    {
        using (var stream = File.OpenRead(path))
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
    }

    internal GameReader(int pid, Dictionary<uint, string> names)
    {
        Names = names;
        Process = Process.GetProcessById(pid);
        started = Process.StartTime.ToUniversalTime().Ticks;
        if (!string.Equals(Process.ProcessName, "sro_client", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Hedef sro_client değil.");
        if (Hash(Process.MainModule.FileName) != ClientHash)
            throw new InvalidOperationException("İstemci sürümü değişmiş. Adresler yeniden doğrulanmalı.");
        baseAddress = Native.Addr(Process.MainModule.BaseAddress);
        handle = Native.OpenProcess(0x1010, false, pid);
        if (handle == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Bellek okuma izni alınamadı.");
        try
        {
            foreach (ProcessModule module in Process.Modules)
            {
                if (!string.Equals(module.ModuleName, "GuardyNE.dll", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (Hash(module.FileName) != GuardHash)
                {
                    Compatibility = "Desteklenmeyen GuardyNE.dll sürümü.";
                    continue;
                }
                GuardBase = Native.Addr(module.BaseAddress);
                uint pe = U32(GuardBase + 0x3c);
                if (pe > 0x10000 || U32(GuardBase + pe) != 0x4550 || U32(GuardBase + pe + 8) != 0x6aea06e7 || U32(GuardBase +
                    pe + 80) != 0x185f000)
                    throw new IOException("Yüklenen DLL disk sürümüyle eşleşmiyor; oyunu yeniden başlat.");
                SenderAddress = GuardBase + 0xE183;
                MoveAddress = GuardBase + 0x743CD0;
                ReverseAddress = GuardBase + 0x741360;
                NavigationAddress = GuardBase + 0x672e40;
                Compatibility = "04.11 DLL: disk ve yüklü sürüm doğrulandı";
            }
        }
        catch (Exception e)
        {
            SenderAddress = 0;
            MoveAddress = 0;
            ReverseAddress = 0;
            NavigationAddress = 0;
            Compatibility = e.Message;
        }
    }

    internal byte[] Read(uint address, int size)
    {
        byte[] b = new byte[size];
        UIntPtr n;
        if (!Native.ReadProcessMemory(handle, Native.Ptr(address), b, (UIntPtr)size, out n) || n.ToUInt64() != (ulong)size)
            throw new IOException("Bellek okunamadı: " + address.ToString("X8"));
        return b;
    }

    internal uint U32(uint address)
    {
        return BitConverter.ToUInt32(Read(address, 4), 0);
    }

    uint Global(uint offset)
    {
        return U32(baseAddress + offset);
    }

    Position ReadPosition(uint obj)
    {
        byte[] b = Read(obj + 0x7c, 16);
        var p = new Position();
        p.Region = BitConverter.ToUInt16(b, 0);
        p.LocalX = BitConverter.ToSingle(b, 4);
        p.Height = BitConverter.ToSingle(b, 8);
        p.LocalY = BitConverter.ToSingle(b, 12);
        if (float.IsNaN(p.LocalX) || float.IsInfinity(p.LocalX) || float.IsNaN(p.LocalY) || float.IsInfinity(p.LocalY) ||
            Math.Abs(p.LocalX) > 1000000 || Math.Abs(p.LocalY) > 1000000)
            throw new IOException("Geçersiz koordinat.");
        p.X = p.Outside ? ((p.Region & 255) - 135) * 192 + p.LocalX / 10.0 : p.LocalX / 10.0;
        p.Y = p.Outside ? ((p.Region >> 8) - 92) * 192 + p.LocalY / 10.0 : p.LocalY / 10.0;
        return p;
    }

    // Character object layout (verified on two characters): HP/MP +0x450/+0x454, max HP/MP +0x458/+0x45C,
    // level byte +0x868, exp (64-bit) +0x870, SP exp (0-399) +0x878, SP +0x880.

    void ReadStats(uint player, Snapshot s)
    {
        try
        {
            byte[] b = Read(player + 0x450, 16);
            uint hp = BitConverter.ToUInt32(b, 0), mp = BitConverter.ToUInt32(b, 4), maxHp = BitConverter.ToUInt32(b, 8),
                maxMp = BitConverter.ToUInt32(b, 12);
            int level = Read(player + 0x868, 1)[0];
            ulong exp = BitConverter.ToUInt64(Read(player + 0x870, 8), 0);
            uint spExp = U32(player + 0x878), sp = U32(player + 0x880);
            if (maxHp == 0 || maxHp > 10000000 || hp > maxHp || maxMp > 10000000 || mp > maxMp || level < 1 || level > 140 ||
                spExp >= 400 || sp > 1000000000)
                return;
            s.Hp = hp;
            s.Mp = mp;
            s.MaxHp = maxHp;
            s.MaxMp = maxMp;
            s.Level = level;
            s.Exp = exp;
            s.SpExp = spExp;
            s.Sp = sp;
            s.StatsKnown = true;
        }
        catch (IOException) { }
    }

    internal Snapshot Capture()
    {
        Process.Refresh();
        if (Process.HasExited || Process.StartTime.ToUniversalTime().Ticks != started)
            throw new IOException("Hedef işlem kapandı.");
        uint player = Global(0xAEF5EC);
        if (player < 65536)
            throw new IOException("Karakter dünyada değil.");
        var snap = new Snapshot();
        snap.Player = ReadPosition(player);
        snap.PlayerAlive = (U32(player + 0x65c) & 1) != 0;
        int gauge = Read(player + 0x886, 1)[0];
        snap.Berserk = gauge <= 5 ? gauge : -1;
        ReadStats(player, snap);
        try
        {
            snap.CharacterName = ReadWideString(player + 0x114);
        }
        catch (IOException) { }
        ReadMaintenance(player, snap);
        ReadGrim(snap);
        try
        {
            if (GuardBase != 0)
            {
                snap.MountUid = U32(GuardBase + 0xc216d8);
                snap.MountKnown = true;
            }
        }
        catch (IOException) { }
        try
        {
            uint ui = Global(0xD0F80C);
            if (ui != 0)
            {
                snap.Selected = U32(ui + 0x62c);
                if (snap.Selected == 0)
                    snap.Selected = U32(ui + 0x628);
            }
        }
        catch (IOException) { }
        uint root = Global(0xAEF5FC);
        if (root < 65536)
            throw new IOException("Nesne listesi bulunamadı.");
        var stack = new Stack<uint>();
        var seen = new HashSet<uint>();
        stack.Push(U32(root + 4));
        while (stack.Count > 0 && seen.Count < 4096)
        {
            uint node = stack.Pop();
            if (node < 65536 || node == root || !seen.Add(node))
                continue;
            try
            {
                byte[] b = Read(node, 24);
                if (b[0x15] != 0)
                    continue;
                stack.Push(BitConverter.ToUInt32(b, 0));
                stack.Push(BitConverter.ToUInt32(b, 8));
                uint uid = BitConverter.ToUInt32(b, 12), obj = BitConverter.ToUInt32(b, 16);
                if (obj < 65536 || obj == player || U32(obj + 0xf8) != uid)
                    continue;
                uint reference = U32(obj + 0x220);
                if (reference < 65536)
                    continue;
                byte[] r = Read(reference, 8);
                ushort type = BitConverter.ToUInt16(r, 0);
                uint model = BitConverter.ToUInt32(r, 4);
                // Every NPC is listed (traders, Explorer, gates): TypeID1 1, TypeID2 2, TypeID3 2 in the client's object type.
                if (IsNpcType(type) || model == 36031 || model == 36032 || ExpeditionRules.Network.Gates.Any(g => g.Model == (int)model))
                {
                    snap.Npcs.Add(new WorldNpc { Uid = uid, Model = model, Address = obj, Position = ReadPosition(obj) });
                }
                if ((type & 0x1c) != 4 || (type & 0x60) != 0x40 || (type & 0x780) != 0x80)
                    continue;
                var mob = new Monster();
                mob.Uid = uid;
                mob.Address = obj;
                mob.Model = BitConverter.ToUInt32(r, 4);
                mob.Alive = (U32(obj + 0x65c) & 1) != 0;
                mob.Position = ReadPosition(obj);
                try
                {
                    uint hp = U32(obj + 0x450), max = U32(obj + 0x458);
                    if (max > 0 && max < 1000000000 && hp <= max)
                        mob.Health = hp;
                }
                catch (IOException) { }
                try
                {
                    byte[] rank = Read(obj + 0x7ac, 2);
                    mob.Rank = rank[0];
                    mob.Party = rank[1] != 0;
                }
                catch (IOException) { }
                string name;
                mob.Name = Names.TryGetValue(mob.Model, out name) ? name : "Bilinmeyen #" + mob.Model;
                if (U32(obj + 0xf8) == uid && U32(obj + 0x220) == reference)
                    snap.Monsters.Add(mob);
            }
            catch (IOException) { }
        }
        if (stack.Count > 0)
            throw new IOException("Nesne listesi tam okunamadı.");
        if (Global(0xAEF5EC) != player || Global(0xAEF5FC) != root)
            throw new IOException("Dünya değişti; yeniden okunacak.");
        return snap;
    }

    internal static bool IsNpcType(ushort type)
    {
        return (type & 0x1c) == 4 && (type & 0x60) == 0x40 && (type & 0x780) == 0x100;
    }

    internal string ReadWideString(uint address)
    {
        byte[] b = Read(address, 24);
        uint length = BitConverter.ToUInt32(b, 16), cap = BitConverter.ToUInt32(b, 20);
        if (length == 0 || length > 32 || cap < length)
            throw new IOException("Geçersiz metin.");
        byte[] data = cap < 8 ? b.Take((int)length * 2).ToArray() : Read(BitConverter.ToUInt32(b, 0), (int)length * 2);
        string text = Encoding.Unicode.GetString(data);
        if (text.Any(ch => char.IsControl(ch)))
            throw new IOException("Geçersiz metin.");
        return text;
    }

    internal string ReadSmallString(uint address)
    {
        byte[] b = Read(address, 24);
        uint length = BitConverter.ToUInt32(b, 16), cap = BitConverter.ToUInt32(b, 20);
        if (length > 2048 || cap < length)
            throw new IOException("Geçersiz metin.");
        return Encoding.UTF8.GetString(cap <= 15 ? b.Take((int)length).ToArray() : Read(BitConverter.ToUInt32(b, 0), (int)length));
    }

    void ReadGrim(Snapshot s)
    {
        if (GuardBase == 0)
            return;
        try
        {
            uint a = GuardBase + 0xad2298;
            uint before = U32(a);
            string title = ReadSmallString(a + 8), text = ReadSmallString(a + 32);
            uint after = U32(a);
            if (before != after)
                return;
            s.Grim = new GrimObservation { Id = before, Title = title, Text = text, Known = before > 0 && before < 100000 };
        }
        catch (IOException) { }
    }

    internal bool ValidateNpc(WorldNpc npc)
    {
        try
        {
            return npc != null && U32(npc.Address + 0xf8) == npc.Uid && U32(U32(npc.Address + 0x220) + 4) == npc.Model;
        }
        catch (IOException)
        {
            return false;
        }
    }

    internal bool ValidateAnyTarget(Monster m)
    {
        try
        {
            return U32(m.Address + 0xf8) == m.Uid && (U32(m.Address + 0x65c) & 1) != 0 && U32(U32(m.Address + 0x220) + 4) == m.Model;
        }
        catch (IOException)
        {
            return false;
        }
    }

    internal bool ValidateTarget(Monster target, Position center, double radius)
    {
        try
        {
            if (U32(target.Address + 0xf8) != target.Uid || (U32(target.Address + 0x65c) & 1) == 0)
                return false;
            uint reference = U32(target.Address + 0x220);
            return U32(reference + 4) == target.Model && TargetPolicy.InArea(ReadPosition(target.Address), center,
                radius);
        }
        catch (IOException)
        {
            return false;
        }
    }

    internal uint FindTree(uint root, uint key)
    {
        uint node = U32(root + 4);
        var seen = new HashSet<uint>();
        while (node >= 65536 && node != root && seen.Count < 128 && seen.Add(node))
        {
            byte[] b = Read(node, 24);
            if (b[21] != 0)
                break;
            uint found = BitConverter.ToUInt32(b, 12);
            if (key == found)
                return BitConverter.ToUInt32(b, 16);
            node = BitConverter.ToUInt32(b, key < found ? 0 : 8);
        }
        return 0;
    }

    internal ushort ItemType(uint model)
    {
        uint root = U32(baseAddress + 0xaedf08 + 0x25c);
        uint reference = FindTree(root, model);
        if (reference == 0)
            return 0;
        byte[] b = Read(reference + 16, 8);
        return BitConverter.ToUInt32(b, 4) == model ? BitConverter.ToUInt16(b, 0) : (ushort)0;
    }

    void ReadMaintenance(uint player, Snapshot snap)
    {
        try
        {
            uint head = U32(player + 0x288);
            uint node = head == 0 ? 0 : U32(head);
            var seen = new HashSet<uint>();
            bool valid = true;
            while (node != 0 && node != head)
            {
                if (seen.Count >= 256 || !seen.Add(node))
                {
                    valid = false;
                    break;
                }
                uint buff = U32(node + 8);
                if (buff != 0)
                {
                    uint type = U32(buff + 0x54), skill = U32(buff + 0x5c);
                    if ((type == 2 || type == 3) && V2Rules.SpeedSkills.Contains(skill))
                        snap.SpeedActive = true;
                }
                node = U32(node);
            }
            snap.SpeedKnown = valid;
        }
        catch (IOException)
        {
            snap.SpeedKnown = false;
        }
        try
        {
            uint ui = Global(0xd0f80c);
            uint irm = ui + 0x36c;
            uint inv = FindTree(U32(irm + 4), 25 + U32(irm + 44));
            if (inv == 0)
                throw new IOException("Envanter yok.");
            uint bag = U32(inv + 0x7bc), begin = U32(bag + 0x3a0), end = U32(bag + 0x3a4);
            if (end < begin || (end - begin) % 4 != 0 || (end - begin) / 4 > 160)
                throw new IOException("Envanter değişti.");
            for (uint i = 0; i < (end - begin) / 4; i++)
            {
                uint obj = U32(begin + i * 4);
                if (obj < 65536)
                    continue;
                byte[] b = Read(obj, 0xa0);
                if (b[0x28] == 0)
                    continue;
                uint id = BitConverter.ToUInt32(b, 0x34);
                if (!V2Rules.ItemIds.Contains(id) && !RecoveryRules.ReverseIds.Contains(id) && !ExpeditionRules.ReturnIds.Contains(id))
                    continue;
                var item = new BagItem { Model = id, Slot = (int)i + 13, Quantity = BitConverter.ToUInt32(b, 0x9c),
                    Address = obj, Type = ItemType(id) };
                if (item.Quantity > 0)
                    snap.Items.Add(item);
            }
            uint equip = U32(inv + 0x7c0);
            if (equip < 65536)
                throw new IOException("Ekipman okunamadı.");
            for (uint slot = 0; slot < 13; slot++)
            {
                byte[] b = Read(equip + 0xf10 + slot * 0x1d0, 0xf8);
                if (b[0x28] == 0 || BitConverter.ToInt32(b, 0xf4) <= 0)
                    continue;
                int dur = BitConverter.ToInt32(b, 0x98);
                if (dur < 0)
                    dur = 0;
                snap.MinDurability = snap.MinDurability.HasValue ? Math.Min(snap.MinDurability.Value, dur) : dur;
            }
            snap.InventoryKnown = true;
        }
        catch (IOException)
        {
            snap.InventoryKnown = false;
            snap.Items.Clear();
            snap.MinDurability = null;
        }
    }

    internal bool ValidateItem(BagItem item)
    {
        try
        {
            uint ui = Global(0xd0f80c), irm = ui + 0x36c;
            uint inv = FindTree(U32(irm + 4), 25 + U32(irm + 44));
            if (inv == 0)
                return false;
            uint bag = U32(inv + 0x7bc), begin = U32(bag + 0x3a0), end = U32(bag + 0x3a4);
            int index = item.Slot - 13;
            if (index < 0 || end < begin || (ulong)index * 4 >= (ulong)(end - begin) || U32(begin + (uint)index * 4) != item.Address)
                return false;
            byte[] b = Read(item.Address, 0xa0);
            return b[0x28] != 0 && BitConverter.ToUInt32(b, 0x34) == item.Model && BitConverter.ToUInt32(b, 0x9c) > 0 &&
                ItemType(item.Model) == item.Type;
        }
        catch (IOException)
        {
            return false;
        }
    }

    // A minimised client may stop updating its world, so background work waits for the window to be restored.
    internal bool IsMinimized()
    {
        try
        {
            Process.Refresh();
            IntPtr window = Process.MainWindowHandle;
            return window != IntPtr.Zero && Native.IsIconic(window);
        }
        catch
        {
            return false;
        }
    }

    internal bool IsForeground()
    {
        uint pid;
        Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out pid);
        return pid == Process.Id;
    }

    public void Dispose()
    {
        Native.CloseHandle(handle);
        Process.Dispose();
    }
}

static class Catalog
{
    internal static Dictionary<uint, string> Load()
    {
        var names = new Dictionary<uint, string>();
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("monster-names.tsv"))
            using (var r = new StreamReader(s, Encoding.UTF8))
            {
                string line;
                while ((line = r.ReadLine()) != null)
                {
                    int tab = line.IndexOf('\t');
                    uint id;
                    if (tab > 0 && uint.TryParse(line.Substring(0, tab), out id))
                        names[id] = line.Substring(tab + 1);
                }
            }
        return names;
    }
}

static class TargetPolicy
{
    internal static bool InArea(Position p, Position center, double radius)
    {
        return p != null && center != null && p.Outside && center.Outside && p.Distance(center) <= radius;
    }

    internal static Monster Choose(Snapshot snap, string name, Position center, double radius)
    {
        if (!InArea(snap.Player, center, radius))
            return null;
        return snap.Monsters.Where(m => m.Alive && m.Name == name && InArea(m.Position, center, radius)).OrderBy(m => m.Position.Distance(snap.Player))
            .ThenBy(m => m.Uid).FirstOrDefault();
    }
}

// This optional sender calls only the verified client's existing select-message routine.
// No disk patch, security bypass, DLL replacement or arbitrary opcode is exposed.

sealed class TargetSender : IDisposable
{
    readonly IntPtr process;
    readonly uint function, moveFunction, reverseFunction, navigationFunction;
    IntPtr pendingThread, pendingBlock;

    internal TargetSender(GameReader reader)
    {
        if (reader.SenderAddress == 0)
            throw new InvalidOperationException("Hedef seçimi için DLL sürümü doğrulanamadı. " + reader.Compatibility);
        function = reader.SenderAddress;
        moveFunction = reader.MoveAddress;
        reverseFunction = reader.ReverseAddress;
        navigationFunction = reader.NavigationAddress;
        byte[] thunk = reader.Read(function, 5);
        if (thunk[0] != 0xe9)
            throw new InvalidOperationException("Seçim işlevi imzası değişmiş.");
        if (!reader.Read(moveFunction, 3).SequenceEqual(new byte[] { 0x55, 0x8b, 0xec }) || !reader.Read(reverseFunction,
            3).SequenceEqual(new byte[] { 0x55, 0x8b, 0xec }) || !reader.Read(navigationFunction, 3).SequenceEqual(new byte[] { 0x55,
            0x8b, 0xec }))
            throw new InvalidOperationException("Hareket / Reverse / navigasyon imzası değişmiş.");
        process = Native.OpenProcess(0x43a, false, reader.Process.Id);
        if (process == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Hedef seçme erişimi reddedildi.");
    }

    internal static byte[] Build(uint block, uint fn, uint uid)
    {
        return BuildPacket(block, fn, 0x7045, BitConverter.GetBytes(uid));
    }

    internal static byte[] BuildPacket(uint block, uint fn, ushort opcode, byte[] payload)
    {
        if (opcode != 0x7045 && opcode != 0x704c && opcode != 0x3053 && opcode != 0x705a)
            throw new ArgumentException("Desteklenmeyen komut.");
        if ((opcode == 0x705a && (payload.Length != 9 || payload[4] != 2)) || payload.Length > 32 || (opcode == 0x7045 &&
            payload.Length != 4) || (opcode == 0x3053 && (payload.Length != 1 || payload[0] != 1)))
            throw new ArgumentException("Geçersiz veri.");
        return BuildCall(block, fn, new uint[] { opcode, block + 64, (uint)payload.Length }, payload);
    }

    internal static byte[] BuildCall(uint block, uint fn, uint[] arguments, byte[] payload)
    {
        if (arguments.Length > 8 || payload.Length > 32)
            throw new ArgumentException("Geçersiz çağrı.");
        var bytes = new List<byte>();
        bytes.AddRange(new byte[] { 0x55, 0x8b, 0xec });
        for (int i = arguments.Length - 1; i >= 0; i--)
        {
            bytes.Add(0x68);
            bytes.AddRange(BitConverter.GetBytes(arguments[i]));
        }
        bytes.Add(0xb8);
        bytes.AddRange(BitConverter.GetBytes(fn));
        bytes.AddRange(new byte[] { 0xff, 0xd0, 0x83, 0xc4, (byte)(arguments.Length * 4), 0x0f, 0xb6, 0xc0, 0x8b, 0xe5,
            0x5d, 0xc2, 0x04, 0x00 });
        while (bytes.Count < 64)
            bytes.Add(0);
        bytes.AddRange(payload);
        return bytes.ToArray();
    }

    internal bool Send(uint uid)
    {
        return Execute(b => Build(b, function, uid));
    }

    internal bool Use(BagItem item)
    {
        if (!V2Rules.AllowedItem(item))
            throw new InvalidOperationException("Eşya kimliği veya türü doğrulanmadı.");
        byte[] payload = new byte[] { (byte)item.Slot, (byte)item.Type, (byte)(item.Type >> 8) };
        return Execute(b => BuildPacket(b, function, 0x704c, payload));
    }

    internal bool Move(Position p)
    {
        if (moveFunction == 0)
            throw new InvalidOperationException("Hareket işlevi doğrulanmadı.");
        uint move = moveFunction;
        return Execute(b => BuildCall(b, move, new uint[]
        {
            p.Region, V2Rules.Bits(p.LocalX), V2Rules.Bits(p.Height), V2Rules.Bits(p.LocalY)
        }, new byte[0]));
    }

    internal static byte[] BuildMountCall(uint block, uint fn, uint[] arguments)
    {
        var bytes = new List<byte>(BuildCall(block, fn, arguments, new byte[0]));
        bytes.InsertRange(13 + arguments.Length * 5, new byte[] { 0x83, 0xf8, 1, 0x0f, 0x94, 0xc0 });
        return bytes.ToArray();
    }

    internal bool ChangeMount(GameReader reader, bool mounted, uint uid)
    {
        if (reader.GuardBase == 0 || !reader.IsForeground())
            return false;
        uint fn = reader.GuardBase + (mounted ? 0x450110u : 0x450030u);
        byte[] signature = reader.Read(fn, 3);
        if (!signature.SequenceEqual(mounted ? new byte[] { 0x55, 0x8b, 0xec } : new byte[] { 0x83, 0xec, 8 }))
            throw new IOException("Mount işlevinin sürümü doğrulanamadı.");
        if (!mounted && uid == 0)
            return true;
        return Execute(b => BuildMountCall(b, fn, mounted ? new uint[0] : new[] { uid }));
    }

    internal bool UseReturn(BagItem item)
    {
        if (item == null || !ExpeditionRules.ReturnIds.Contains(item.Model) || item.Type == 0 || item.Quantity == 0 ||
            item.Slot < 13 || item.Slot >= 173)
            throw new InvalidOperationException("Return Scroll geçersiz.");
        return Execute(b => BuildPacket(b, function, 0x704c, new byte[] { (byte)item.Slot, (byte)item.Type, (byte)(item.Type >> 8) }));
    }

    internal bool Teleport(uint uid, int from, int to)
    {
        if (uid == 0 || !ExpeditionRules.Network.Links.Any(l => l.From == from && l.To == to))
            throw new InvalidOperationException("Teleport bağlantısı doğrulanmadı.");
        byte[] payload = new byte[9];
        Array.Copy(BitConverter.GetBytes(uid), payload, 4);
        payload[4] = 2;
        Array.Copy(BitConverter.GetBytes(to), 0, payload, 5, 4);
        return Execute(b => BuildPacket(b, function, 0x705a, payload));
    }

    internal bool ReturnTown()
    {
        return Execute(b => BuildPacket(b, function, 0x3053, new byte[] { 1 }));
    }

    internal bool ReverseToDeath(BagItem item)
    {
        if (!RecoveryRules.IsReverse(item) || reverseFunction == 0)
            throw new InvalidOperationException("Reverse Scroll doğrulanamadı.");
        return Execute(b => BuildCall(b, reverseFunction, new uint[] { (uint)(item.Slot - 13), 2 }, new byte[0]));
    }

    internal bool NavigationProbe(Position from, Position to)
    {
        if (navigationFunction == 0 || !from.Outside || !to.Outside)
            throw new InvalidOperationException("Navigasyon bağlamı doğrulanmadı.");
        return Execute(b => BuildCall(b, navigationFunction, new uint[]
        {
            from.Region, V2Rules.Bits(from.LocalX), V2Rules.Bits(from.Height), V2Rules.Bits(from.LocalY), to.Region,
                V2Rules.Bits(to.LocalX), V2Rules.Bits(to.Height), V2Rules.Bits(to.LocalY)
        }, new byte[0]));
    }

    bool Execute(Func<uint, byte[]> build)
    {
        if (pendingThread != IntPtr.Zero)
            throw new IOException("Önceki çağrı tamamlanmadı; yeniden denenmez.");
        IntPtr block = Native.VirtualAllocEx(process, IntPtr.Zero, (UIntPtr)4096, 0x3000, 4);
        if (block == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        IntPtr thread = IntPtr.Zero;
        bool free = true;
        try
        {
            byte[] code = build(Native.Addr(block));
            UIntPtr written;
            if (!Native.WriteProcessMemory(process, block, code, (UIntPtr)code.Length, out written) || written.ToUInt64() != (ulong)code.Length)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            uint old;
            if (!Native.VirtualProtectEx(process, block, (UIntPtr)4096, 0x20, out old))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!Native.FlushInstructionCache(process, block, (UIntPtr)code.Length))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            uint tid;
            thread = Native.CreateRemoteThread(process, IntPtr.Zero, UIntPtr.Zero, block, IntPtr.Zero, 0, out tid);
            if (thread == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (Native.WaitForSingleObject(thread, 1000) != 0)
            {
                pendingThread = thread;
                pendingBlock = block;
                free = false;
                thread = IntPtr.Zero;
                throw new IOException("Oyun çağrısı tamamlanmadı; durduruldu.");
            }
            uint result;
            if (!Native.GetExitCodeThread(thread, out result))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (result > 1)
                throw new IOException("Oyun çağrısı beklenmedik sonuç verdi.");
            return result == 1;
        }
        finally
        {
            if (thread != IntPtr.Zero)
                Native.CloseHandle(thread);
            if (free)
                Native.VirtualFreeEx(process, block, UIntPtr.Zero, 0x8000);
        }
    }

    public void Dispose()
    {
        if (pendingThread != IntPtr.Zero)
        {
            if (Native.WaitForSingleObject(pendingThread, 1000) == 0)
                Native.VirtualFreeEx(process, pendingBlock, UIntPtr.Zero, 0x8000);
            Native.CloseHandle(pendingThread);
        }
        Native.CloseHandle(process);
    }
}

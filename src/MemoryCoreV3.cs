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
    [DllImport("user32.dll")]
    internal static extern bool PostMessage(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);

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

// Another character in the client's object list (a party member, a passer-by): its id, name, place and whether it is alive.
sealed class WorldPlayer
{
    public uint Uid, Address;
    public string Name;
    public Position Position;
    public bool Alive;
}

sealed class Snapshot
{
    public string CharacterName;
    public bool MountKnown;
    public uint MountUid;
    // The model of the animal being ridden (read from the world object list), 0 when on foot or not found.
    public uint MountModel;
    public List<WorldNpc> Npcs = new List<WorldNpc>();
    public List<WorldPlayer> Players = new List<WorldPlayer>();
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
    // The model of the weapon in hand (equipment slot 6; 0 = none) and the weapons in the bag as (slot, model).
    public uint WeaponModel;
    public readonly List<KeyValuePair<int, uint>> BagWeapons = new List<KeyValuePair<int, uint>>();
    // Skill ids of the buffs the character carries right now; BuffsKnown is false when the list could not be read cleanly.
    public bool BuffsKnown;
    public HashSet<uint> Buffs = new HashSet<uint>();
    public int? MinDurability;
    public List<BagItem> Items = new List<BagItem>();
    public List<Monster> Monsters = new List<Monster>();
}

sealed class BagEntry
{
    public int Slot;
    public uint Model, Quantity, Address;
}

// One known build of GuardyNE.dll: the file hash, the PE time stamp and image size read back from the loaded copy, and the offsets (from the
// module base) of what the bot uses. A new game update that replaces the DLL needs one more entry here; the game's own sro_client.exe is
// checked separately.
sealed class GuardBuild
{
    internal string Label, Hash;
    internal uint Stamp, ImageSize, Sender, Move, Reverse, Navigation, MountUid, GrimText, MountDown, MountUp;

    internal static readonly GuardBuild[] Known =
    {
        // 04.11 (update rev 33 and earlier)
        new GuardBuild { Label = "04.11", Hash = "3206CF49259ECA33CA6DF4783E1D7192ED722CEEFEAD1925F9C7E46B7F8D1D01", Stamp = 0x6aea06e7, ImageSize = 0x185f000,
            Sender = 0xE183, Move = 0x743CD0, Reverse = 0x741360, Navigation = 0x672e40, MountUid = 0xc216d8, GrimText = 0xad2298,
            MountDown = 0x450110, MountUp = 0x450030 },
        // update rev 34 (2026-11-18): offsets found by matching the old functions' code in the new DLL
        new GuardBuild { Label = "rev 34", Hash = "62F212E6B775501CECB81C8AFE83292DA20BAA62645D662E65CD1E251AED660D", Stamp = 0x6afa04fb, ImageSize = 0x1a12000,
            Sender = 0xF92F, Move = 0x844A10, Reverse = 0x842000, Navigation = 0x76D8D0, MountUid = 0xd7c720, GrimText = 0xc28070,
            MountDown = 0x495B10, MountUp = 0x495A30 },
        // update rev 35 (2026-11-18, 20:50): found the same way, by matching the code of the 04.11 functions
        new GuardBuild { Label = "rev 35", Hash = "CD836FA7AFF65778667D25062BEBD6B66A78402CD5DA400415A6236E7CF28C7B", Stamp = 0x6afdb7a6, ImageSize = 0x1a18000,
            Sender = 0xF95C, Move = 0x849240, Reverse = 0x846830, Navigation = 0x7720B0, MountUid = 0xd81950, GrimText = 0xc2d070,
            MountDown = 0x499950, MountUp = 0x499870 },
        // update of 2026-11-26 (patch of the night before): found the same way, by matching the code of the 04.11 functions; the send function
        // by its first instructions (size check 0xfa0, opcode 0x7005) and its thunk
        new GuardBuild { Label = "rev 36", Hash = "D0F66E9659CEEA0E8C39326522D5A7443A2E328D66C1E6C7D8C107B780E874DB", Stamp = 0x6b06585f, ImageSize = 0x1b29000,
            Sender = 0x106BD, Move = 0x8ECF30, Reverse = 0x8EA3E0, Navigation = 0x811D70, MountUid = 0xE63470, GrimText = 0xD03A00,
            MountDown = 0x517E90, MountUp = 0x517DB0 }
    };

    internal static GuardBuild ByHash(string hash)
    {
        return Known.FirstOrDefault(b => string.Equals(b.Hash, hash, StringComparison.OrdinalIgnoreCase));
    }
}

sealed class GameReader : IDisposable
{
    const string ClientHash = "51081AC1F776A38FE0AF58E2A961BFA9E8166506AE26DC8DCFD8008736725C05";
    readonly IntPtr handle;
    readonly uint baseAddress;
    readonly long started;
    internal readonly Process Process;
    // The folder of the game's program (its "logs" folder holds client.log and crash.log).
    internal readonly string GameDirectory;
    internal uint SenderAddress, GuardBase, MoveAddress, ReverseAddress, NavigationAddress;
    internal GuardBuild Guard;
    internal string Compatibility = "DLL bağlantısı yok";
    public readonly Dictionary<uint, string> Names;

    static string Hash(string path)
    {
        using (var stream = File.OpenRead(path))
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
    }

    // The hash of the game's program with the "large address aware" flag taken out. The launcher's memory option (config/memory.ini, 4 GB) sets that one
    // bit in the program's header before the game starts and changes nothing else (checked: with the bit cleared the hash is the shipped one), so
    // the addresses the bot uses are the same in both modes.
    internal static string ClientHashIgnoringLargeAddress(byte[] image)
    {
        var copy = (byte[])image.Clone();
        if (copy.Length > 0x100 && copy[0] == 'M' && copy[1] == 'Z')
        {
            int pe = BitConverter.ToInt32(copy, 0x3c);
            if (pe > 0 && pe + 0x18 < copy.Length && copy[pe] == 'P' && copy[pe + 1] == 'E')
                copy[pe + 0x16] &= 0xDF;
        }
        using (var hash = SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(copy)).Replace("-", "");
    }

    internal GameReader(int pid, Dictionary<uint, string> names)
    {
        Names = names;
        Process = Process.GetProcessById(pid);
        started = Process.StartTime.ToUniversalTime().Ticks;
        if (!string.Equals(Process.ProcessName, "sro_client", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Hedef sro_client değil.");
        if (ClientHashIgnoringLargeAddress(File.ReadAllBytes(Process.MainModule.FileName)) != ClientHash)
            throw new InvalidOperationException("İstemci sürümü değişmiş. Adresler yeniden doğrulanmalı.");
        GameDirectory = Path.GetDirectoryName(Process.MainModule.FileName);
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
                var build = GuardBuild.ByHash(Hash(module.FileName));
                if (build == null)
                {
                    Compatibility = "Desteklenmeyen GuardyNE.dll sürümü (oyun güncellenmiş olabilir; bot yeni sürüme göre güncellenmeli).";
                    continue;
                }
                uint baseOfDll = Native.Addr(module.BaseAddress);
                uint pe = U32(baseOfDll + 0x3c);
                if (pe > 0x10000 || U32(baseOfDll + pe) != 0x4550 || U32(baseOfDll + pe + 8) != build.Stamp || U32(baseOfDll +
                    pe + 80) != build.ImageSize)
                    throw new IOException("Yüklenen DLL disk sürümüyle eşleşmiyor; oyunu yeniden başlat.");
                Guard = build;
                GuardBase = baseOfDll;
                SenderAddress = GuardBase + build.Sender;
                MoveAddress = GuardBase + build.Move;
                ReverseAddress = GuardBase + build.Reverse;
                NavigationAddress = GuardBase + build.Navigation;
                Compatibility = build.Label + " DLL: disk ve yüklü sürüm doğrulandı";
            }
        }
        catch (Exception e)
        {
            SenderAddress = 0;
            MoveAddress = 0;
            ReverseAddress = 0;
            NavigationAddress = 0;
            Guard = null;
            GuardBase = 0;
            Compatibility = e.Message;
        }
        RemoteJournal.Default.Marker(DateTime.Now, pid, "attach");
        StabilityLog.Write("Oyuna bağlanıldı · PID " + pid + " · " + Compatibility);
    }

    internal IntPtr Handle
    {
        get { return handle; }
    }

    // Every item in the bag, any item: slot (13 and up), model and quantity.
    internal List<BagEntry> ReadBag()
    {
        var list = new List<BagEntry>();
        uint ui = Global(0xd0f80c), irm = ui + 0x36c;
        uint inv = FindTree(U32(irm + 4), 25 + U32(irm + 44));
        if (inv == 0)
            throw new IOException("Envanter yok.");
        uint bag = U32(inv + 0x7bc), begin = U32(bag + 0x3a0), end = U32(bag + 0x3a4);
        if (end < begin || (end - begin) % 4 != 0 || (end - begin) / 4 > 200)
            throw new IOException("Envanter değişti.");
        for (uint i = 0; i < (end - begin) / 4; i++)
        {
            uint obj = U32(begin + i * 4);
            if (obj < 65536)
                continue;
            byte[] b = Read(obj, 0xa0);
            if (b[0x28] == 0)
                continue;
            list.Add(new BagEntry { Slot = (int)i + 13, Model = BitConverter.ToUInt32(b, 0x34), Quantity = BitConverter.ToUInt32(b, 0x9c), Address = obj });
        }
        return list;
    }

    // The gold in the bag: a 64-bit number in the bag object (found by scanning for a known amount: 40838830 sat at bag+0x378).
    internal ulong ReadGold()
    {
        uint ui = Global(0xd0f80c), irm = ui + 0x36c;
        uint inv = FindTree(U32(irm + 4), 25 + U32(irm + 44));
        if (inv == 0)
            throw new IOException("Envanter yok.");
        uint bag = U32(inv + 0x7bc);
        if (bag < 65536)
            throw new IOException("Envanter yok.");
        ulong gold = BitConverter.ToUInt64(Read(bag + 0x378, 8), 0);
        if (gold > 10000000000000UL)
            throw new IOException("Altın değeri okunamadı.");
        return gold;
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

    internal uint Global(uint offset)
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

    // How many objects of each type the last read found (for finding out what a kind of object looks like).
    internal readonly Dictionary<ushort, int> SeenTypes = new Dictionary<ushort, int>();
    internal ushort OwnType;

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
            if (GuardBase != 0 && Guard != null)
            {
                snap.MountUid = U32(GuardBase + Guard.MountUid);
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
        // Other characters have the same object type as the player's own.
        ushort playerType = 0;
        try
        {
            uint own = U32(player + 0x220);
            if (own >= 65536)
                playerType = BitConverter.ToUInt16(Read(own, 8), 0);
            OwnType = playerType;
        }
        catch (IOException) { }
        SeenTypes.Clear();
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
                if (snap.MountUid != 0 && uid == snap.MountUid)
                    snap.MountModel = model;
                int typeCount;
                SeenTypes[type] = SeenTypes.TryGetValue(type, out typeCount) ? typeCount + 1 : 1;
                if (playerType != 0 && IsPlayerType(type, playerType))
                {
                    try
                    {
                        string who = ReadWideString(obj + 0x114);
                        snap.Players.Add(new WorldPlayer { Uid = uid, Address = obj, Name = who, Position = ReadPosition(obj), Alive = (U32(obj + 0x65c) & 1) != 0 });
                    }
                    catch (IOException) { }
                    continue;
                }
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

    // A character (the player's own kind of object): the same object class as the player's own type, whichever race it is.
    internal static bool IsPlayerType(ushort type, ushort own)
    {
        return type == own || ((type & 0x1c) == 4 && (type & 0x60) == 0x20 && (own & 0x1c) == 4 && (own & 0x60) == 0x20);
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
        if (GuardBase == 0 || Guard == null)
            return;
        try
        {
            uint a = GuardBase + Guard.GrimText;
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
                    if (skill != 0 && skill < 100000000)
                        snap.Buffs.Add(skill);
                }
                node = U32(node);
            }
            snap.SpeedKnown = valid;
            snap.BuffsKnown = valid;
        }
        catch (IOException)
        {
            snap.SpeedKnown = false;
            snap.BuffsKnown = false;
            snap.Buffs.Clear();
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
                if (WeaponRules.TypeOf(id) != 0)
                    snap.BagWeapons.Add(new KeyValuePair<int, uint>((int)i + 13, id));
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
                if (slot == WeaponRules.WeaponSlot)
                    snap.WeaponModel = BitConverter.ToUInt32(b, 0x34);
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
            snap.BagWeapons.Clear();
            snap.Items.Clear();
            snap.MinDurability = null;
        }
    }

    // Every item of the character: the equipped ones (slots 0-12; the weapon is 6) and the bag (slot 13 and up) by item model. Empty bag slots
    // are listed in `emptyBagSlots`. Throws IOException when the inventory cannot be read.
    internal Dictionary<int, uint> InventoryModels(out List<int> emptyBagSlots)
    {
        var models = new Dictionary<int, uint>();
        emptyBagSlots = new List<int>();
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
            byte[] b = obj < 65536 ? null : Read(obj, 0xa0);
            if (b == null || b[0x28] == 0)
            {
                emptyBagSlots.Add((int)i + 13);
                continue;
            }
            models[(int)i + 13] = BitConverter.ToUInt32(b, 0x34);
        }
        uint equip = U32(inv + 0x7c0);
        if (equip < 65536)
            throw new IOException("Ekipman okunamadı.");
        for (int slot = 0; slot < 13; slot++)
        {
            byte[] b = Read(equip + 0xf10 + (uint)slot * 0x1d0, 0xf8);
            if (b[0x28] == 0 || BitConverter.ToInt32(b, 0xf4) <= 0)
                continue;
            models[slot] = BitConverter.ToUInt32(b, 0x34);
        }
        return models;
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

    // The character object exists (the game is in a world); one read of four bytes.
    internal bool PlayerPointerValid()
    {
        try
        {
            return Global(0xAEF5EC) >= 65536;
        }
        catch (IOException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        RemoteTrace.Flush();
        RemoteJournal.Default.Marker(DateTime.Now, Process.Id, "detach");
        StabilityLog.Write("Oyun bağlantısı bırakıldı · PID " + Process.Id);
        Native.CloseHandle(handle);
        Process.Dispose();
    }
}

// How many commands the bot sends to the game, by kind. A server may drop a connection that sends too many (or too fast); the bot writes the
// counts of the last minute to its event log while it runs, so a disconnect can be matched with what was being sent.
static class SendStats
{
    static readonly Dictionary<string, int> counts = new Dictionary<string, int>();
    static long windowStart = Environment.TickCount;

    internal static void Add(string kind)
    {
        lock (counts)
        {
            int n;
            counts.TryGetValue(kind, out n);
            counts[kind] = n + 1;
        }
    }

    // The counts since the last call as text ("seçim 410 · yürüme 22"), empty when nothing was sent; the counters start again.
    internal static string TakeSummary(out int total, out double seconds)
    {
        lock (counts)
        {
            long now = Environment.TickCount;
            seconds = Math.Max(1, (now - windowStart) / 1000.0);
            windowStart = now;
            total = counts.Values.Sum();
            string text = string.Join(" · ", counts.OrderByDescending(c => c.Value).Select(c => c.Key + " " + c.Value));
            counts.Clear();
            return text;
        }
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
    IntPtr pendingThread, pendingBlock, pendingBlock2;
    // Calls into the game that timed out or failed, one after the other: each one is followed by a short wait, and after
    // RemoteFailureLimit of them the job is stopped (see RemoteCaller).
    internal const int RemoteFailureLimit = 5;
    readonly RemoteCaller caller;

    internal bool BackingOff
    {
        get { return caller.BackingOff; }
    }

    internal bool FailureLimitReached
    {
        get { return caller.FailureLimitReached; }
    }

    internal TargetSender(GameReader reader)
    {
        int pid = reader.Process.Id;
        caller = new RemoteCaller(pid, RemoteFailureLimit, () => RemoteGate.NowMs, StabilityLog.Write, RemoteJournal.Default, () => RemoteCaller.ProcessAlive(pid));
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
        RemoteJournal.Op = opcode;
        RemoteJournal.Len = payload.Length;
        SendStats.Add(opcode == 0x7045 ? "seçim" : opcode == 0x7046 ? "konuşma" : opcode == 0x7034 ? "eşya-taşıma" :
            opcode == 0x704c ? "eşya-kullanma" : "diğer");
        // No skill packet (0x7074) is allowed: skills are cast only by the hotbar key.
        if (opcode != 0x7045 && opcode != 0x704c && opcode != 0x3053 && opcode != 0x705a && opcode != 0x7046 && opcode != 0x7034)
            throw new ArgumentException("Desteklenmeyen komut.");
        if ((opcode == 0x705a && (payload.Length != 9 || payload[4] != 2)) || payload.Length > 32 || (opcode == 0x7045 &&
            payload.Length != 4) || (opcode == 0x3053 && (payload.Length != 1 || payload[0] != 1)) ||
            (opcode == 0x7046 && (payload.Length != 5 || payload[4] < 1 || payload[4] > 9)) ||
            (opcode == 0x7034 && !ValidMove(payload)))
            throw new ArgumentException("Geçersiz veri.");
        return BuildCall(block, fn, new uint[] { opcode, block + 64, (uint)payload.Length }, payload);
    }

    // Moving an item inside the character's own inventory: 00 (inside the inventory), the slot it is taken from, the slot it goes to, the amount
    // (two bytes). Allowed: from the bag (slot 13 and up) to another bag slot or to the weapon slot (6, which puts the weapon that was there into
    // the bag slot the new one came from). Nothing else can be moved with it: no trade, storage, ground or shop transfers.
    internal static bool ValidMove(byte[] payload)
    {
        if (payload == null || payload.Length != 5 || payload[0] != 0)
            return false;
        int from = payload[1], to = payload[2], amount = BitConverter.ToUInt16(payload, 3);
        return from >= 13 && (to == 6 || to >= 13) && from != to && amount >= 1 && amount <= 1000;
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
        SendStats.Add("yürüme");
        return Execute(b => BuildCall(b, move, new uint[]
        {
            p.Region, V2Rules.Bits(p.LocalX), V2Rules.Bits(p.Height), V2Rules.Bits(p.LocalY)
        }, new byte[0]), "move");
    }

    internal static byte[] BuildMountCall(uint block, uint fn, uint[] arguments)
    {
        var bytes = new List<byte>(BuildCall(block, fn, arguments, new byte[0]));
        bytes.InsertRange(13 + arguments.Length * 5, new byte[] { 0x83, 0xf8, 1, 0x0f, 0x94, 0xc0 });
        return bytes.ToArray();
    }

    internal bool ChangeMount(GameReader reader, bool mounted, uint uid)
    {
        if (reader.GuardBase == 0 || reader.Guard == null || !reader.IsForeground())
            return false;
        uint fn = reader.GuardBase + (mounted ? reader.Guard.MountDown : reader.Guard.MountUp);
        byte[] signature = reader.Read(fn, 3);
        if (!signature.SequenceEqual(mounted ? new byte[] { 0x55, 0x8b, 0xec } : new byte[] { 0x83, 0xec, 8 }))
            throw new IOException("Mount işlevinin sürümü doğrulanamadı.");
        if (!mounted && uid == 0)
            return true;
        return Execute(b => BuildMountCall(b, fn, mounted ? new uint[0] : new[] { uid }), "mount");
    }

    internal bool UseReturn(BagItem item)
    {
        if (item == null || !ExpeditionRules.ReturnIds.Contains(item.Model) || item.Type == 0 || item.Quantity == 0 ||
            item.Slot < 13 || item.Slot >= 173)
            throw new InvalidOperationException("Return Scroll geçersiz.");
        return Execute(b => BuildPacket(b, function, 0x704c, new byte[] { (byte)item.Slot, (byte)item.Type, (byte)(item.Type >> 8) }));
    }

    // Uses a Reverse Return Scroll for the third choice of its window, "go to a place of the list": the item's slot and type, the choice 7 and the
    // number of the place (the game's own table, see ReversePoints). Nothing is sent for a number that is not in that table.
    internal bool UseReverseTo(BagItem item, int pointId)
    {
        if (!RecoveryRules.IsReverse(item) || ReverseRules.Find(pointId) == null)
            throw new InvalidOperationException("Reverse Scroll ya da nokta geçersiz.");
        byte[] payload = ReversePayload(item, pointId);
        return Execute(b => BuildPacket(b, function, 0x704c, payload));
    }

    internal static byte[] ReversePayload(BagItem item, int pointId)
    {
        var payload = new byte[8];
        payload[0] = (byte)item.Slot;
        payload[1] = (byte)item.Type;
        payload[2] = (byte)(item.Type >> 8);
        payload[3] = 7;
        Array.Copy(BitConverter.GetBytes((uint)pointId), 0, payload, 4, 4);
        return payload;
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

    // Moves an item of the bag to another bag slot or onto the weapon slot (6): "put on this weapon". The client's own send function is used.
    internal bool MoveItem(int from, int to, int amount)
    {
        byte[] payload = { 0, (byte)from, (byte)to, (byte)amount, (byte)(amount >> 8) };
        if (from < 0 || from > 255 || to < 0 || to > 255 || !ValidMove(payload))
            throw new InvalidOperationException("Eşya taşıma isteği geçersiz.");
        return Execute(b => BuildPacket(b, function, 0x7034, payload));
    }

    // Talk to a selected NPC: the NPC's id and the number of the dialog option (1 = the first option).
    internal bool Talk(uint uid, int option)
    {
        if (uid == 0 || option < 1 || option > 9)
            throw new InvalidOperationException("Konuşma seçeneği geçersiz.");
        byte[] payload = new byte[5];
        Array.Copy(BitConverter.GetBytes(uid), payload, 4);
        payload[4] = (byte)option;
        return Execute(b => BuildPacket(b, function, 0x7046, payload));
    }

    internal bool ReturnTown()
    {
        return Execute(b => BuildPacket(b, function, 0x3053, new byte[] { 1 }));
    }

    internal bool ReverseToDeath(BagItem item)
    {
        if (!RecoveryRules.IsReverse(item) || reverseFunction == 0)
            throw new InvalidOperationException("Reverse Scroll doğrulanamadı.");
        return Execute(b => BuildCall(b, reverseFunction, new uint[] { (uint)(item.Slot - 13), 2 }, new byte[0]), "reverse");
    }

    // Up to BatchMax navigation queries in one call of the game: one remote thread instead of one per query (a thread costs about 3 ms,
    // the query itself almost nothing). Each result byte is the function's raw answer (0 / 1; anything else means "no answer").
    internal const int BatchMax = 16, BatchCodeLimit = 3072;

    internal static byte[] BuildProbeBatch(uint resultBlock, uint fn, IList<uint[]> calls)
    {
        if (calls == null || calls.Count == 0 || calls.Count > BatchMax)
            throw new ArgumentException("Geçersiz toplu çağrı.");
        var bytes = new List<byte> { 0x55, 0x8b, 0xec };
        for (int i = 0; i < calls.Count; i++)
        {
            if (calls[i].Length != 8)
                throw new ArgumentException("Navigasyon çağrısı 8 bağımsız değişken ister.");
            for (int k = 7; k >= 0; k--)
            {
                bytes.Add(0x68);
                bytes.AddRange(BitConverter.GetBytes(calls[i][k]));
            }
            bytes.Add(0xb8);
            bytes.AddRange(BitConverter.GetBytes(fn));
            bytes.AddRange(new byte[] { 0xff, 0xd0, 0x83, 0xc4, 32 });
            bytes.Add(0xa2);
            bytes.AddRange(BitConverter.GetBytes(resultBlock + (uint)i));
        }
        // xor eax, eax ; mov esp, ebp ; pop ebp ; ret 4
        bytes.AddRange(new byte[] { 0x33, 0xc0, 0x8b, 0xe5, 0x5d, 0xc2, 0x04, 0x00 });
        if (bytes.Count > BatchCodeLimit)
            throw new ArgumentException("Toplu çağrı çok uzun.");
        return bytes.ToArray();
    }

    // 1 = open, 0 = blocked, -1 = the game gave no 0/1 answer for that pair (ask it again on its own).
    internal sbyte[] NavigationProbeMany(IList<Position[]> pairs)
    {
        if (navigationFunction == 0)
            throw new InvalidOperationException("Navigasyon bağlamı doğrulanmadı.");
        var calls = new List<uint[]>();
        foreach (var pair in pairs)
        {
            if (!pair[0].Outside || !pair[1].Outside)
                throw new InvalidOperationException("Navigasyon bağlamı doğrulanmadı.");
            calls.Add(new uint[]
            {
                pair[0].Region, V2Rules.Bits(pair[0].LocalX), V2Rules.Bits(pair[0].Height), V2Rules.Bits(pair[0].LocalY), pair[1].Region,
                V2Rules.Bits(pair[1].LocalX), V2Rules.Bits(pair[1].Height), V2Rules.Bits(pair[1].LocalY)
            });
        }
        byte[] raw = ExecuteBatch(resultBlock => BuildProbeBatch(resultBlock, navigationFunction, calls), calls.Count);
        var answers = new sbyte[raw.Length];
        for (int i = 0; i < raw.Length; i++)
            answers[i] = raw[i] > 1 ? (sbyte)-1 : (sbyte)raw[i];
        return answers;
    }

    byte[] ExecuteBatch(Func<uint, byte[]> build, int count)
    {
        byte[] answers = null;
        caller.Run(true, count, delegate { answers = ExecuteBatchOnce(build, count); });
        return answers;
    }

    byte[] ExecuteBatchOnce(Func<uint, byte[]> build, int count)
    {
        if (!ReleasePending())
            throw new RemoteTimeoutException("Önceki çağrı tamamlanmadı; yeniden denenmez.");
        // Two pages: the code (made executable, never writable afterwards) and a plain data page the queries write their answers into.
        IntPtr block = Native.VirtualAllocEx(process, IntPtr.Zero, (UIntPtr)4096, 0x3000, 4);
        if (block == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        IntPtr results = Native.VirtualAllocEx(process, IntPtr.Zero, (UIntPtr)4096, 0x3000, 4);
        if (results == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            Native.VirtualFreeEx(process, block, UIntPtr.Zero, 0x8000);
            throw new Win32Exception(error);
        }
        IntPtr thread = IntPtr.Zero;
        bool free = true;
        try
        {
            byte[] code = build(Native.Addr(results));
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
            if (Native.WaitForSingleObject(thread, 1000 + 20 * (uint)count) != 0)
            {
                pendingThread = thread;
                pendingBlock = block;
                pendingBlock2 = results;
                free = false;
                thread = IntPtr.Zero;
                throw new RemoteTimeoutException("Oyun çağrısı tamamlanmadı; durduruldu.");
            }
            var buffer = new byte[count];
            UIntPtr read;
            if (!Native.ReadProcessMemory(process, results, buffer, (UIntPtr)count, out read) || read.ToUInt64() != (ulong)count)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return buffer;
        }
        finally
        {
            if (thread != IntPtr.Zero)
                Native.CloseHandle(thread);
            if (free)
            {
                Native.VirtualFreeEx(process, block, UIntPtr.Zero, 0x8000);
                Native.VirtualFreeEx(process, results, UIntPtr.Zero, 0x8000);
            }
        }
    }

    internal bool NavigationProbe(Position from, Position to)
    {
        if (navigationFunction == 0 || !from.Outside || !to.Outside)
            throw new InvalidOperationException("Navigasyon bağlamı doğrulanmadı.");
        return Execute(b => BuildCall(b, navigationFunction, new uint[]
        {
            from.Region, V2Rules.Bits(from.LocalX), V2Rules.Bits(from.Height), V2Rules.Bits(from.LocalY), to.Region,
                V2Rules.Bits(to.LocalX), V2Rules.Bits(to.Height), V2Rules.Bits(to.LocalY)
        }, new byte[0]), "probe");
    }

    bool Execute(Func<uint, byte[]> build, string kind = null)
    {
        bool answer = false;
        caller.Run(false, 1, delegate { answer = ExecuteOnce(build); }, kind);
        return answer;
    }

    // A call that timed out earlier may have finished by now: its thread and memory are let go and the next call can go on. False while
    // it is still running (it is not waited for).
    bool ReleasePending()
    {
        if (pendingThread == IntPtr.Zero)
            return true;
        if (Native.WaitForSingleObject(pendingThread, 0) != 0)
            return false;
        Native.VirtualFreeEx(process, pendingBlock, UIntPtr.Zero, 0x8000);
        if (pendingBlock2 != IntPtr.Zero)
            Native.VirtualFreeEx(process, pendingBlock2, UIntPtr.Zero, 0x8000);
        Native.CloseHandle(pendingThread);
        pendingThread = pendingBlock = pendingBlock2 = IntPtr.Zero;
        return true;
    }

    bool ExecuteOnce(Func<uint, byte[]> build)
    {
        if (!ReleasePending())
            throw new RemoteTimeoutException("Önceki çağrı tamamlanmadı; yeniden denenmez.");
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
                throw new RemoteTimeoutException("Oyun çağrısı tamamlanmadı; durduruldu.");
            }
            uint result;
            if (!Native.GetExitCodeThread(thread, out result))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (result > 1)
                throw new UnexpectedResultException("Oyun çağrısı beklenmedik sonuç verdi.");
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
        RemoteTrace.Flush();
        RemoteJournal.Default.Flush();
        if (pendingThread != IntPtr.Zero)
        {
            if (Native.WaitForSingleObject(pendingThread, 1000) == 0)
            {
                Native.VirtualFreeEx(process, pendingBlock, UIntPtr.Zero, 0x8000);
                if (pendingBlock2 != IntPtr.Zero)
                    Native.VirtualFreeEx(process, pendingBlock2, UIntPtr.Zero, 0x8000);
            }
            Native.CloseHandle(pendingThread);
        }
        Native.CloseHandle(process);
    }
}

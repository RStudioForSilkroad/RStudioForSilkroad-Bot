using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

// Read-only heap observation. Old notices are baselined before entering a new run.

sealed class CompletionNotice : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    struct Region
    {
        internal uint Base, AllocationBase, AllocationProtect, Size, State, Protect, Type;
    }

    [DllImport("kernel32.dll")]
    static extern uint VirtualQueryEx(IntPtr process, IntPtr address, out Region info, uint length);
    readonly object sync = new object();
    volatile bool stopped;
    Thread worker;
    string message = "", error = "";
    bool ready;
    int generation, candidates;

    internal int Candidates
    {
        get
        {
            lock (sync)
                return candidates;
        }
    }

    internal string Message
    {
        get
        {
            lock (sync)
                return message;
        }
    }

    internal bool Ready
    {
        get
        {
            lock (sync)
                return ready;
        }
    }

    internal string Error
    {
        get
        {
            lock (sync)
                return error;
        }
    }

    static readonly byte[] ascii = Encoding.ASCII.GetBytes("Dungeon cleared! Monsters "), wide = Encoding.Unicode.GetBytes("Dungeon cleared! Monsters ");

    internal void Begin(int pid)
    {
        Dispose();
        stopped = false;
        lock (sync)
        {
            ready = false;
            message = error = "";
            generation++;
        }
        int run = generation;
        worker = new Thread(() => Observe(pid, run)) { IsBackground = true, Name = "Grim completion notice reader" };
        worker.Start();
    }

    static int Find(byte[] buffer, int length, byte[] needle, int from)
    {
        int[] skip = new int[256];
        for (int i = 0; i < 256; i++)
            skip[i] = needle.Length;
        for (int i = 0; i < needle.Length - 1; i++)
            skip[needle[i]] = needle.Length - 1 - i;
        int at = from;
        while (at <= length - needle.Length)
        {
            int j = needle.Length - 1;
            while (j >= 0 && buffer[at + j] == needle[j])
                j--;
            if (j < 0)
                return at;
            at += skip[buffer[at + needle.Length - 1]];
        }
        return -1;
    }

    void Observe(int pid, int run)
    {
        IntPtr handle = Native.OpenProcess(0x410, false, pid);
        if (handle == IntPtr.Zero)
        {
            lock (sync)
                error = "Bitiş mesajı okuma erişimi yok.";
            return;
        }
        var old = new HashSet<string>();
        bool baseline = true;
        try
        {
            while (!stopped && run == generation)
            {
                var present = new HashSet<string>();
                uint address = 65536;
                while (address < 0x7fff0000 && !stopped && run == generation)
                {
                    Region region;
                    if (VirtualQueryEx(handle, new IntPtr(unchecked((int)address)), out region, 28) == 0)
                        break;
                    ulong end = (ulong)region.Base + region.Size;
                    if (end <= address || end > 0x80000000)
                        break;
                    uint protection = region.Protect & 255;
                    if (region.State == 0x1000 && region.Type == 0x20000 && (region.Protect & 0x100) == 0 && (protection == 4 ||
                        protection == 8 || protection == 0x40 || protection == 0x80) && region.Size <= 16 * 1024 * 1024)
                    {
                        for (uint part = region.Base; part < end && !stopped; part += 1024 * 1024)
                        {
                            int size = (int)Math.Min(1024 * 1024 + 512, end - part);
                            byte[] bytes = new byte[size];
                            UIntPtr read;
                            if (!Native.ReadProcessMemory(handle, new IntPtr(unchecked((int)part)), bytes, (UIntPtr)size,
                                out read))
                                continue;
                            foreach (var pattern in new[] { ascii, wide })
                            {
                                int offset = 0;
                                while ((offset = Find(bytes, (int)read.ToUInt32(), pattern, offset)) >= 0)
                                {
                                    bool unicode = pattern == wide;
                                    int take = Math.Min(unicode ? 400 : 200, size - offset);
                                    string text = (unicode ? Encoding.Unicode : Encoding.ASCII).GetString(bytes, offset,
                                        take).Split('\0')[0];
                                    int finish = text.IndexOf("Leave dungeon.", StringComparison.Ordinal);
                                    if (finish >= 0)
                                        text = text.Substring(0, finish + 14);
                                    string key = (part + (uint)offset) + ":" + text;
                                    present.Add(key);
                                    if (!baseline && !old.Contains(key) && text.Contains("Dungeon Exit"))
                                    {
                                        lock (sync)
                                            if (run == generation)
                                                message = text;
                                    }
                                    offset += pattern.Length;
                                }
                            }
                        }
                    }
                    address = (uint)end;
                }
                if (stopped || run != generation)
                    break;
                old = present;
                baseline = false;
                lock (sync)
                    if (run == generation)
                    {
                        ready = true;
                        candidates = present.Count;
                    }
                for (int i = 0; i < 30 && !stopped && run == generation; i++)
                    Thread.Sleep(100);
            }
        }
        catch (Exception ex)
        {
            lock (sync)
                if (run == generation)
                    error = "Bitiş mesajı izleyicisi: " + ex.Message;
        }
        finally
        {
            Native.CloseHandle(handle);
        }
    }

    public void Dispose()
    {
        stopped = true;
        generation++;
        if (worker != null && worker.IsAlive)
            worker.Join(2500);
        worker = null;
    }
}

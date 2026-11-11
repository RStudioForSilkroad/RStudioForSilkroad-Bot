using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Linq;

class M
{
    [DllImport("kernel32.dll")]
    static extern IntPtr OpenProcess(int a, bool b, int pid);
    [DllImport("kernel32.dll")]
    static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out int read);
    [DllImport("kernel32.dll")]
    static extern int VirtualQueryEx(IntPtr h, IntPtr addr, out MBI mbi, int len);

    [StructLayout(LayoutKind.Sequential)]
    struct MBI
    {
        public IntPtr Base, AllocBase;
        public uint AllocProtect;
        public IntPtr Size;
        public uint State, Protect, Type;
    }

    static uint[] P = new uint[18], S0 = new uint[256], S1 = new uint[256], S2 = new uint[256], S3 = new uint[256];

    static bool Verify(uint[] p, uint[] s0, uint[] s1, uint[] s2, uint[] s3, bool be)
    {
        var txt = Encoding.ASCII.GetBytes("Joymax Pak File");
        uint l, r;
        if (be)
        {
            l = (uint)(txt[0] << 24 | txt[1] << 16 | txt[2] << 8 | txt[3]);
            r = (uint)(txt[4] << 24 | txt[5] << 16 | txt[6] << 8 | txt[7]);
        }
        else
        {
            l = BitConverter.ToUInt32(txt, 0);
            r = BitConverter.ToUInt32(txt, 4);
        }
        for (int i = 0; i < 16; i++)
        {
            l ^= p[i];
            uint x = l;
            r ^= ((s0[x >> 24] + s1[(x >> 16) & 255]) ^ s2[(x >> 8) & 255]) + s3[x & 255];
            var t = l;
            l = r;
            r = t;
        }
        var u = l;
        l = r;
        r = u;
        r ^= p[16];
        l ^= p[17];
        if (be)
            return (l >> 24) == 0xC6 && ((l >> 16) & 255) == 0xAF && ((l >> 8) & 255) == 0x93;
        return (l & 255) == 0xC6 && ((l >> 8) & 255) == 0xAF && ((l >> 16) & 255) == 0x93;
    }

    static void Main(string[] a)
    {
        var proc = Process.GetProcessesByName("sro_client").FirstOrDefault();
        IntPtr h = OpenProcess(0x0410, false, proc.Id);
        long addr = 0;
        int hits = 0;
        var sw = Stopwatch.StartNew();
        while (addr < 0x7fff0000)
        {
            MBI m;
            if (VirtualQueryEx(h, (IntPtr)addr, out m, Marshal.SizeOf(typeof(MBI))) == 0)
                break;
            long size = (long)m.Size;
            if (m.State == 0x1000 && (m.Protect & 0x66) != 0 && (m.Protect & 0x100) == 0 && size < 300000000 && size >= 4168)
            {
                var buf = new byte[size];
                int rd;
                if (ReadProcessMemory(h, m.Base, buf, (int)size, out rd) && rd >= 4168)
                {
                    var w = new uint[rd / 4];
                    Buffer.BlockCopy(buf, 0, w, 0, w.Length * 4);
                    for (int i = 0; i + 1042 <= w.Length; i++)
                    {
                        // layout A: P[18] then 4 S-boxes
                        var p = new uint[18];
                        Array.Copy(w, i, p, 0, 18);
                        var s0 = new uint[256];
                        Array.Copy(w, i + 18, s0, 0, 256);
                        var s1 = new uint[256];
                        Array.Copy(w, i + 274, s1, 0, 256);
                        var s2 = new uint[256];
                        Array.Copy(w, i + 530, s2, 0, 256);
                        var s3 = new uint[256];
                        Array.Copy(w, i + 786, s3, 0, 256);
                        foreach (bool be in new[] { false, true })
                            if (Verify(p, s0, s1, s2, s3, be))
                            {
                                Console.WriteLine("HIT layoutA be=" + be + " @0x" + ((long)m.Base + i * 4).ToString("x"));
                                hits++;
                                using (var f = File.Create(a[0] + ".ctx" + hits))
                                {
                                    var bw = new BinaryWriter(f);
                                    bw.Write(be ? 1 : 0);
                                    for (int k = 0; k < 1042; k++)
                                        bw.Write(w[i + k]);
                                }
                            }
                        // layout B: 4 S-boxes then P
                        if (i + 1042 <= w.Length)
                        {
                            Array.Copy(w, i + 1024, p, 0, 18);
                            Array.Copy(w, i, s0, 0, 256);
                            Array.Copy(w, i + 256, s1, 0, 256);
                            Array.Copy(w, i + 512, s2, 0, 256);
                            Array.Copy(w, i + 768, s3, 0, 256);
                            foreach (bool be in new[] { false, true })
                                if (Verify(p, s0, s1, s2, s3, be))
                                {
                                    Console.WriteLine("HIT layoutB be=" + be + " @0x" + ((long)m.Base + i * 4).ToString("x"));
                                    hits++;
                                    using (var f = File.Create(a[0] + ".ctxB" + hits))
                                    {
                                        var bw = new BinaryWriter(f);
                                        bw.Write(be ? 1 : 0);
                                        for (int k = 0; k < 1042; k++)
                                            bw.Write(w[i + k]);
                                    }
                                }
                        }
                    }
                }
            }
            addr = (long)m.Base + size;
        }
        Console.WriteLine("done hits=" + hits + " in " + sw.Elapsed.TotalSeconds.ToString("F0") + "s");
    }
}

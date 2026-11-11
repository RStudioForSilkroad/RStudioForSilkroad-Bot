using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;

class Pk2
{
    uint[] P = new uint[18];
    uint[][]
    S = new uint[4][]
    ;
    bool be;
    string file;
    FileStream fs;

    public Pk2(string path, string ctxFile)
    {
        file = path;
        using (var r = new BinaryReader(File.OpenRead(ctxFile)))
        {
            be = r.ReadInt32() == 1;
            for (int i = 0; i < 18; i++)
                P[i] = r.ReadUInt32();
            for (int b = 0; b < 4; b++)
            {
                S[b] = new uint[256];
                for (int i = 0; i < 256; i++)
                    S[b][i] = r.ReadUInt32();
            }
        }
        fs = File.OpenRead(path);
    }

    uint F(uint x)
    {
        return ((S[0][x >> 24] + S[1][(x >> 16) & 255]) ^ S[2][(x >> 8) & 255]) + S[3][x & 255];
    }

    void Dec(ref uint l, ref uint r)
    {
        for (int i = 17; i > 1; i--)
        {
            l ^= P[i];
            r ^= F(l);
            var t = l;
            l = r;
            r = t;
        }
        var u = l;
        l = r;
        r = u;
        r ^= P[1];
        l ^= P[0];
    }

    void DecBuf(byte[] b)
    {
        for (int o = 0; o + 8 <= b.Length; o += 8)
        {
            uint l, r;
            if (be)
            {
                l = (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);
                r = (uint)(b[o + 4] << 24 | b[o + 5] << 16 | b[o + 6] << 8 | b[o + 7]);
            }
            else
            {
                l = BitConverter.ToUInt32(b, o);
                r = BitConverter.ToUInt32(b, o + 4);
            }
            Dec(ref l, ref r);
            if (be)
            {
                b[o] = (byte)(l >> 24);
                b[o + 1] = (byte)(l >> 16);
                b[o + 2] = (byte)(l >> 8);
                b[o + 3] = (byte)l;
                b[o + 4] = (byte)(r >> 24);
                b[o + 5] = (byte)(r >> 16);
                b[o + 6] = (byte)(r >> 8);
                b[o + 7] = (byte)r;
            }
            else
            {
                Array.Copy(BitConverter.GetBytes(l), 0, b, o, 4);
                Array.Copy(BitConverter.GetBytes(r), 0, b, o + 4, 4);
            }
        }
    }

    public class Entry
    {
        public byte Type;
        public string Name;
        public long Position;
        public uint Size;
        public long Next;
        public string Path;
    }

    public List<Entry> Block(long offset)
    {
        var buf = new byte[2560];
        fs.Position = offset;
        fs.Read(buf, 0, 2560);
        DecBuf(buf);
        var list = new List<Entry>();
        for (int i = 0; i < 20; i++)
        {
            int o = i * 128;
            var e = new Entry { Type = buf[o] };
            int n = 0;
            while (n < 81 && buf[o + 1 + n] != 0)
                n++;
            e.Name = Encoding.GetEncoding(1254).GetString(buf, o + 1, n);
            e.Position = BitConverter.ToInt64(buf, o + 1 + 81 + 24);
            e.Size = BitConverter.ToUInt32(buf, o + 1 + 81 + 32);
            e.Next = BitConverter.ToInt64(buf, o + 1 + 81 + 36);
            list.Add(e);
        }
        return list;
    }

    public IEnumerable<Entry> Chain(long offset)
    {
        while (offset > 0)
        {
            var b = Block(offset);
            foreach (var e in b)
                yield return e;
            offset = b[19].Next;
            if (offset <= 0)
                yield break;
        }
    }

    public void Walk(long offset, string prefix, List<Entry> files, int depth)
    {
        foreach (var e in Chain(offset))
        {
            if (e.Type == 0 || e.Name == "." || e.Name == "..")
                continue;
            if (e.Type == 1 && depth < 24)
                Walk(e.Position, prefix + e.Name + "\\", files, depth + 1);
            else if (e.Type == 2)
            {
                e.Path = prefix + e.Name;
                files.Add(e);
            }
        }
    }

    static void Main(string[] a)
    {
        var pk = new Pk2(a[0], a[1]);
        var files = new List<Entry>();
        pk.Walk(256, "", files, 0);
        File.WriteAllLines(a[2], files.Select(f => f.Path + "\t" + f.Position + "\t" + f.Size));
        Console.WriteLine(Path.GetFileName(a[0]) + ": " + files.Count + " dosya");
    }
}

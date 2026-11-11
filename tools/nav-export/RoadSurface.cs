using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;

// Marks tiles as road when the Data.ne ground texture is dust/stone AND the world-map pixel is road brown AND the brown patch is thin (roads, not cliffs).

class RoadSurface
{
    static string Root = Environment.CurrentDirectory; // run from the repository root
    static Dictionary<string, string[]> nvmIndex;
    static FileStream dataFs;

    static byte[] ObjectCounts(int reg)
    {
        string key = "navmesh\\nv_" + reg.ToString("x4") + ".nvm";
        string[] e;
        if (!nvmIndex.TryGetValue(key, out e))
            return null;
        long pos = long.Parse(e[0]);
        int size = int.Parse(e[1]);
        var b = new byte[size];
        dataFs.Position = pos;
        dataFs.Read(b, 0, size);
        try
        {
            int p = 12;
            int n = BitConverter.ToUInt16(b, p);
            p += 2;
            for (int i = 0; i < n; i++)
            {
                int links = BitConverter.ToUInt16(b, p + 30);
                p += 32 + links * 6;
            }
            int total = BitConverter.ToInt32(b, p);
            p += 8;
            var r = new byte[total];
            for (int i = 0; i < total; i++)
            {
                int obj = b[p + 16];
                r[i] = (byte)Math.Min(255, obj);
                p += 17 + obj * 2;
            }
            return r;
        }
        catch (Exception)
        {
            return null;
        }
    }

    static void Main(string[] a)
    {
        nvmIndex = File.ReadAllLines(Path.Combine(Path.GetDirectoryName(a[1]), "index-Data.tsv")).Select(l => l.Split(new[] { (char)9 }))
            .Where(p => p[0].StartsWith("navmesh")).ToDictionary(p => p[0].ToLowerInvariant(), p => new[] { p[1], p[2] });
        dataFs = File.OpenRead(@"D:\Games\New Era Online\Data.ne");
        string tiles = a[0];
        string jsonPath = a[2]; // regions.json written by NavExport
        var texOk = new HashSet<int>();
        foreach (var ln in File.ReadAllLines(tiles))
        {
            var m = Regex.Match(ln, "^(\\d{5}) \\S+ \"(\\S+)\" \"(\\S+)\"");
            if (m.Success && (m.Groups[3].Value.Contains("dust") || m.Groups[3].Value.Contains("stone")))
                texOk.Add(int.Parse(m.Groups[1].Value));
        }
        var bmp = new Bitmap(Path.Combine(Root, @"assets\Dunya-Haritasi.jpg"));
        int W = bmp.Width, H = bmp.Height;
        var brown = new byte[W * H];
        var bd = bmp.LockBits(new Rectangle(0, 0, W, H), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        var raw = new byte[bd.Stride * H];
        System.Runtime.InteropServices.Marshal.Copy(bd.Scan0, raw, 0, raw.Length);
        bmp.UnlockBits(bd);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int o = y * bd.Stride + x * 3;
                int B = raw[o], G = raw[o + 1], R = raw[o + 2];
                if (R >= 100 && R > G * 1.12 && G > B * 1.35)
                    brown[y * W + x] = 1;
            }
        var integral = new int[(W + 1) * (H + 1)];
        for (int y = 0; y < H; y++)
        {
            int row = 0;
            for (int x = 0; x < W; x++)
            {
                row += brown[y * W + x];
                integral[(y + 1) * (W + 1) + x + 1] = integral[y * (W + 1) + x + 1] + row;
            }
        }
        Func<int, int, int, double> density = (px, py, r) =>
        {
            int x0 = Math.Max(0, px - r), x1 = Math.Min(W, px + r + 1), y0 = Math.Max(0, py - r), y1 = Math.Min(H, py +
                r + 1);
            int s = integral[y1 * (W + 1) + x1] - integral[y0 * (W + 1) + x1] - integral[y1 * (W + 1) + x0] + integral[y0 * (W +
                1) + x0];
            return s / (double)((x1 - x0) * (y1 - y0));
        };
        var regions = new List<int>();
        var offs = new List<long>();
        var re = new Regex("\"region\":(\\d+)|\"tileOffset\":(\\d+)");
        using (var sr = new StreamReader(jsonPath, Encoding.ASCII, false, 1 << 20))
        {
            var buf = new char[1 << 22];
            string tail = "";
            int n;
            while ((n = sr.Read(buf, 0, buf.Length)) > 0)
            {
                string chunk = tail + new string(buf, 0, n);
                int safe = Math.Max(0, chunk.Length - 64);
                foreach (Match m in re.Matches(chunk))
                {
                    if (m.Index >= safe)
                        break;
                    if (m.Groups[1].Success)
                        regions.Add(int.Parse(m.Groups[1].Value));
                    else
                        offs.Add(long.Parse(m.Groups[2].Value));
                }
                tail = chunk.Substring(safe);
            }
            foreach (Match m in re.Matches(tail))
            {
                if (m.Groups[1].Success)
                    regions.Add(int.Parse(m.Groups[1].Value));
                else
                    offs.Add(long.Parse(m.Groups[2].Value));
            }
        }
        var waterTiles = new Dictionary<int, bool[]>();
        var roadTiles = new Dictionary<int, bool[]>();
        long roadCount = 0;
        using (var fs = File.OpenRead(@"D:\Games\New Era Online\Data.ne"))
        {
            var b = new byte[73728];
            for (int r = 0; r < regions.Count; r++)
            {
                int reg = regions[r];
                int sx = reg & 255, sy = reg >> 8;
                if (sx < 60 || sx > 180 || sy < 69 || sy > 113)
                    continue;
                fs.Position = offs[r];
                if (fs.Read(b, 0, b.Length) != b.Length)
                    continue;
                var road = new bool[9216];
                var water = new bool[9216];
                double ox = (sx - 135) * 192, oy = (sy - 92) * 192;
                for (int t = 0; t < 9216; t++)
                {
                    int flag = BitConverter.ToUInt16(b, t * 8 + 4), tex = BitConverter.ToUInt16(b, t * 8 + 6);
                    if ((flag & 1) != 0)
                        continue;
                    {
                        int wx2 = (int)((ox + (t % 96) * 2 + 1) * 0.25 + 3408), wy2 = (int)(1020 - (oy + (t / 96) * 2 +
                            1) * 0.25);
                        if (wx2 >= 0 && wy2 >= 0 && wx2 < W && wy2 < H)
                        {
                            int o2 = wy2 * bd.Stride + wx2 * 3;
                            int B2 = raw[o2], G2 = raw[o2 + 1], R2 = raw[o2 + 2];
                            if (G2 > R2 * 1.2 && B2 > R2 * 1.1 && B2 > G2 * 0.8 && G2 >= 90)
                                water[t] = true;
                        }
                    }
                    if (!texOk.Contains(tex))
                        continue;
                    int px = (int)((ox + (t % 96) * 2 + 1) * 0.25 + 3408), py = (int)(1020 - (oy + (t / 96) * 2 + 1) * 0.25);
                    if (px < 0 || py < 0 || px >= W || py >= H || brown[py * W + px] == 0)
                        continue;
                    if (density(px, py, 5) >= 0.5)
                        continue;
                    road[t] = true;
                    roadCount++;
                }
                roadTiles[reg] = road;
                waterTiles[reg] = water;
            }
        }
        Console.WriteLine("road tiles " + roadCount + " in " + roadTiles.Count + " regions");
        // per-cell road coverage in the existing mesh order
        var cellObj = new List<byte>();
        var cellRoad = new List<byte>();
        var cellWater = new List<byte>();
        int[] hist = new int[6];
        long roadCells = 0, cells = 0;
        var sizes = new List<double>();
        using (var inp = new BinaryReader(new GZipStream(File.OpenRead(Path.Combine(Root, @"data\road-mesh.bin.gz")),
            CompressionMode.Decompress)))
        {
            inp.ReadInt32();
            int rc = inp.ReadInt32(), cc = inp.ReadInt32();
            for (int ri = 0; ri < rc; ri++)
            {
                ushort reg = inp.ReadUInt16();
                int len = inp.ReadInt32();
                bool[] road;
                roadTiles.TryGetValue(reg, out road);
                bool[] wat;
                waterTiles.TryGetValue(reg, out wat);
                var objs = ObjectCounts(reg);
                if (objs != null && objs.Length != len)
                    objs = null;
                for (int j = 0; j < len; j++)
                {
                    float ox = 0;
                    float x0 = inp.ReadSingle(), y0 = inp.ReadSingle(), x1 = inp.ReadSingle(), y1 = inp.ReadSingle();
                    inp.ReadSingle();
                    inp.ReadByte();
                    int ec = inp.ReadUInt16();
                    for (int e = 0; e < ec; e++)
                        inp.ReadInt32();
                    // stored values are world units; convert to tiles relative to region origin
                    double rx0 = x0 / 10.0, ry0 = y0 / 10.0, rx1 = x1 / 10.0, ry1 = y1 / 10.0;
                    int tx0 = Math.Max(0, (int)Math.Floor(rx0 / 2)), tx1 = Math.Min(95, (int)Math.Ceiling(rx1 / 2) - 1),
                        ty0 = Math.Max(0, (int)Math.Floor(ry0 / 2)), ty1 = Math.Min(95, (int)Math.Ceiling(ry1 / 2) - 1);
                    tx0 = Math.Min(95, tx0);
                    ty0 = Math.Min(95, ty0);
                    if (tx1 < tx0)
                        tx1 = tx0;
                    if (ty1 < ty0)
                        ty1 = ty0;
                    int hit = 0, all = 0, wet = 0;
                    if (road != null)
                        for (int ty = ty0; ty <= ty1; ty++)
                            for (int tx = tx0; tx <= tx1; tx++)
                            {
                                all++;
                                if (road[ty * 96 + tx])
                                    hit++;
                                if (wat != null && wat[ty * 96 + tx])
                                    wet++;
                            }
                    byte v = all == 0 ? (byte)0 : (byte)Math.Min(255, hit * 255 / all);
                    cellRoad.Add(v);
                    cellObj.Add(objs == null ? (byte)0 : objs[j]);
                    cellWater.Add(all == 0 ? (byte)0 : (byte)Math.Min(255, wet * 255 / all));
                    cells++;
                    if (v >= 77)
                        roadCells++;
                    sizes.Add((rx1 - rx0) * (ry1 - ry0));
                }
            }
        }
        int wc = 0, wc2 = 0;
        foreach (var q in cellWater)
        {
            if (q > 0)
                wc++;
            if (q >= 51)
                wc2++;
        }
        Console.WriteLine("water cells >0: " + wc + " >=20%: " + wc2);
        int oc = 0;
        foreach (var q in cellObj)
            if (q > 0)
                oc++;
        Console.WriteLine("cells with objects: " + oc + " of " + cellObj.Count);
        Console.WriteLine("cells " + cells + " roadCells(>=30%) " + roadCells);
        sizes.Sort();
        Console.WriteLine("cell area p10 " + sizes[sizes.Count / 10] + " median " + sizes[sizes.Count / 2] + " p90 " +
            sizes[sizes.Count * 9 / 10]);
        using (var o = new BinaryWriter(new GZipStream(File.Create(a[1]), CompressionMode.Compress)))
        {
            o.Write(0x31305352);
            o.Write(cellRoad.Count);
            o.Write(cellRoad.ToArray());
            o.Write(cellWater.ToArray());
            o.Write(cellObj.ToArray());
        }
        if (a.Length > 2)
        { // diagnostic png for area: x0 x1 y0 y1 file
            double x0 = double.Parse(a[2]), x1 = double.Parse(a[3]), y0 = double.Parse(a[4]), y1 = double.Parse(a[5]);
            double S = 0.75;
            int w = (int)((x1 - x0) * S), h = (int)((y1 - y0) * S);
            var img = new Bitmap(w, h);
            var g = Graphics.FromImage(img);
            g.DrawImage(bmp, new Rectangle(0, 0, w, h), new Rectangle((int)(x0 * 0.25 + 3408), (int)(1020 - y1 * 0.25),
                (int)((x1 - x0) * 0.25), (int)((y1 - y0) * 0.25)), GraphicsUnit.Pixel);
            var br = new SolidBrush(Color.FromArgb(200, 255, 0, 255));
            var brw = new SolidBrush(Color.FromArgb(170, 255, 255, 0));
            foreach (var kv in roadTiles)
            {
                int sx = kv.Key & 255, sy = kv.Key >> 8;
                double ox = (sx - 135) * 192, oy = (sy - 92) * 192;
                if (ox + 192 < x0 || ox > x1 || oy + 192 < y0 || oy > y1)
                    continue;
                for (int t = 0; t < 9216; t++)
                    if (waterTiles[kv.Key][t])
                    {
                        double wx0 = ox + (t % 96) * 2, wy0 = oy + (t / 96) * 2;
                        g.FillRectangle(brw, (float)((wx0 - x0) * S), (float)((y1 - wy0 - 2) * S), (float)(2 * S +.5), (float)(2 * S +
                            .5));
                    }
                for (int t = 0; t < 0; t++) { }
                for (int t = 0; t < 9216; t++)
                    if (kv.Value[t])
                    {
                        double wx = ox + (t % 96) * 2, wy = oy + (t / 96) * 2;
                        g.FillRectangle(br, (float)((wx - x0) * S), (float)((y1 - wy - 2) * S), (float)(2 * S +.5), (float)(2 * S +
                            .5));
                    }
            }
            img.Save(a[6]);
        }
    }
}

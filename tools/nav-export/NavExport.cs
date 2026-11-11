using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using NavData;

// Adds the game's object collision/walk data (walls, stairs, gates, bridges) to the terrain road mesh. Objects that span region borders exist once per region; they are merged.

class Export
{
    sealed class Terr
    {
        public ushort Region;
        public float L, B, R, T, H;
        public byte Flags;
        public List<int> Edges = new List<int>();
    }

    sealed class Extra
    {
        public ushort Region;
        public float[] X = new float[3], Z = new float[3], Y = new float[3];
        public List<int> Edges = new List<int>();
    }

    static bool Cross(double ax, double az, double bx, double bz, double cx, double cz, double dx, double dz)
    {
        double d1x = bx - ax, d1z = bz - az, d2x = dx - cx, d2z = dz - cz;
        double den = d1x * d2z - d1z * d2x;
        if (Math.Abs(den) < 1e-9)
            return false;
        double t = ((cx - ax) * d2z - (cz - az) * d2x) / den, u = ((cx - ax) * d1z - (cz - az) * d1x) / den;
        return t > 0 && t < 1 && u >= 0 && u <= 1;
    }

    static bool InTri(double px, double pz, Extra e)
    {
        double d = (e.Z[1] - e.Z[2]) * (e.X[0] - e.X[2]) + (e.X[2] - e.X[1]) * (e.Z[0] - e.Z[2]);
        if (Math.Abs(d) < 1e-9)
            return false;
        double a = ((e.Z[1] - e.Z[2]) * (px - e.X[2]) + (e.X[2] - e.X[1]) * (pz - e.Z[2])) / d, b = ((e.Z[2] - e.Z[0]) * (px - e.X[2]) +
            (e.X[0] - e.X[2]) * (pz - e.Z[2])) / d, c = 1 - a - b;
        return a >= -1e-6 && b >= -1e-6 && c >= -1e-6;
    }

    static void Main(string[] a)
    {
        string dir = a[0];
        var sw = Stopwatch.StartNew();
        var data = new Archive(@"D:\Games\New Era Online\Data.ne", dir + "\\index-Data.tsv");
        var objIdx = Loader.ObjectIndex(data.Read("navmesh\\object.ifo"));
        var cells = new List<Terr>();
        var regionStart = new Dictionary<ushort, int>();
        var regionLen = new Dictionary<ushort, int>();
        using (var r = new BinaryReader(new GZipStream(File.OpenRead(a[1]), CompressionMode.Decompress)))
        {
            r.ReadInt32();
            int rc = r.ReadInt32();
            r.ReadInt32();
            for (int i = 0; i < rc; i++)
            {
                ushort reg = r.ReadUInt16();
                int len = r.ReadInt32();
                regionStart[reg] = cells.Count;
                regionLen[reg] = len;
                for (int j = 0; j < len; j++)
                {
                    var t = new Terr { Region = reg, L = r.ReadSingle(), B = r.ReadSingle(), R = r.ReadSingle(), T = r.ReadSingle(),
                        H = r.ReadSingle(), Flags = r.ReadByte() };
                    int ec = r.ReadUInt16();
                    for (int k = 0; k < ec; k++)
                        t.Edges.Add(r.ReadInt32());
                    cells.Add(t);
                }
            }
        }
        int baseCount = cells.Count;
        var parsed = new Dictionary<int, Region>();
        foreach (var rid in regionStart.Keys)
        {
            var nb = data.Read("navmesh\\nv_" + rid.ToString("x4") + ".nvm");
            if (nb == null)
                continue;
            try
            {
                var reg = Loader.ReadRegion(nb, rid);
                if (reg.Cells.Count == regionLen[rid])
                    parsed[rid] = reg;
            }
            catch (Exception) { }
        }
        Console.WriteLine("terrain cells " + baseCount + "  parsed regions " + parsed.Count + "  " + sw.Elapsed.TotalSeconds.ToString("F0") +
            "s");
        var objCache = new Dictionary<int, NavObj>();
        Func<int, NavObj> getObj = idx =>
        {
            NavObj o;
            if (!objCache.TryGetValue(idx, out o))
            {
                string p;
                o = objIdx.TryGetValue(idx, out p) ? Loader.LoadObject(data, p, 0) : null;
                objCache[idx] = o;
            }
            return o;
        };
        // ---- pass 1: extra cells, one per triangle, owned by the region that contains the triangle centre
        var extras = new List<Extra>();
        var triNode = new Dictionary<long, int[]>();
        var worldTri = new Dictionary<long, double[][]>(); // per (region,instance): per triangle [cx,cz]
        Func<int, int, long> key = (rid, ii) => ((long)rid << 16) | (uint)ii;
        foreach (var kv in parsed)
        {
            int rid = kv.Key;
            var reg = kv.Value;
            for (int ii = 0; ii < reg.Insts.Count; ii++)
            {
                var inst = reg.Insts[ii];
                var o = getObj(inst.ObjIndex);
                if (o == null)
                    continue;
                bool linked = inst.Links.Any(l => l[0] >= 0);
                if (!(o.Enterable || linked))
                    continue;
                double cs = Math.Cos(inst.Yaw), sn = Math.Sin(inst.Yaw);
                var nodes = new int[o.T0.Length];
                var cent = new double[o.T0.Length][];
                for (int t = 0; t < o.T0.Length; t++)
                {
                    var e = new Extra { Region = (ushort)rid };
                    int[] vs = { o.T0[t], o.T1[t], o.T2[t] };
                    double mx = 0, mz = 0;
                    for (int k = 0; k < 3; k++)
                    {
                        double vx = o.VX[vs[k]], vz = o.VZ[vs[k]];
                        e.X[k] = (float)(vx * cs - vz * sn + inst.X);
                        e.Z[k] = (float)(vx * sn + vz * cs + inst.Z);
                        e.Y[k] = o.VY[vs[k]] + inst.Y;
                        mx += e.X[k] / 3;
                        mz += e.Z[k] / 3;
                    }
                    cent[t] = new[] { mx, mz };
                    if (mx >= 0 && mx < 1920 && mz >= 0 && mz < 1920)
                    {
                        nodes[t] = baseCount + extras.Count;
                        extras.Add(e);
                    }
                    else
                        nodes[t] = -1;
                }
                triNode[key(rid, ii)] = nodes;
                worldTri[key(rid, ii)] = cent;
            }
        }
        Console.WriteLine("extra cells " + extras.Count + "  " + sw.Elapsed.TotalSeconds.ToString("F0") + "s");
        // node of triangle t of instance ii in region rid (own or the twin copy in the neighbouring region)
        Func<int, int, int, int> nodeOf = (rid, ii, t) =>
        {
            int[] nodes;
            if (!triNode.TryGetValue(key(rid, ii), out nodes) || t < 0 || t >= nodes.Length)
                return -1;
            if (nodes[t] >= 0)
                return nodes[t];
            var c = worldTri[key(rid, ii)][t];
            int sx = rid & 255, sy = rid >> 8;
            int rx = sx + (c[0] < 0 ? -1 : c[0] >= 1920 ? 1 : 0), ry = sy + (c[1] < 0 ? -1 : c[1] >= 1920 ? 1 : 0);
            int rid2 = rx | (ry << 8);
            Region reg2;
            if (!parsed.TryGetValue(rid2, out reg2))
                return -1;
            var inst = parsed[rid].Insts[ii];
            int twin = reg2.Insts.FindIndex(x => x.ObjIndex == inst.ObjIndex && x.Id == inst.Id);
            if (twin < 0)
                return -1;
            int[] n2;
            if (!triNode.TryGetValue(key(rid2, twin), out n2) || t >= n2.Length)
                return -1;
            return n2[t];
        };
        // terrain cell (global index) at a region-local point, crossing region borders
        Func<int, double, double, int> terrainAt = (rid, x, z) =>
        {
            int sx = rid & 255, sy = rid >> 8;
            while (x < 0)
            {
                x += 1920;
                sx--;
            }
            while (x >= 1920)
            {
                x -= 1920;
                sx++;
            }
            while (z < 0)
            {
                z += 1920;
                sy--;
            }
            while (z >= 1920)
            {
                z -= 1920;
                sy++;
            }
            int r2 = sx | (sy << 8);
            Region reg;
            if (!parsed.TryGetValue(r2, out reg))
                return -1;
            for (int c = 0; c < reg.Cells.Count; c++)
            {
                var rc = reg.Cells[c];
                if (x >= rc.X0 && x <= rc.X1 && z >= rc.Z0 && z <= rc.Z1)
                    return regionStart[(ushort)r2] + c;
            }
            return -1;
        };
        long linksObj = 0, linksTerrain = 0, linksInst = 0;
        var addEdges = new List<long>();
        var softSet = new HashSet<long>();
        int skipBlocked = 0, skipHeight = 0, skipNoCell = 0;
        foreach (var kv in parsed)
        {
            int rid = kv.Key;
            var reg = kv.Value;
            int sx = rid & 255, sy = rid >> 8;
            int start = regionStart[(ushort)rid];
            var segs = new Dictionary<int, List < double[]>>();
            for (int ii = 0; ii < reg.Insts.Count; ii++)
            {
                var inst = reg.Insts[ii];
                var o = getObj(inst.ObjIndex);
                if (o == null)
                    continue;
                double cs = Math.Cos(inst.Yaw), sn = Math.Sin(inst.Yaw);
                Func<int, double[]> W = v => new[]
                {
                    o.VX[v] * cs - o.VZ[v] * sn + inst.X, o.VX[v] * sn + o.VZ[v] * cs + inst.Z, o.VY[v] + inst.Y
                };
                var list = new List<double[]>();
                for (int e = 0; e < o.GV0.Length; e++)
                {
                    if ((o.GFlag[e] & 16) != 0)
                        continue;
                    var p = W(o.GV0[e]);
                    var q = W(o.GV1[e]);
                    list.Add(new[] { p[0], p[1], q[0], q[1] });
                }
                segs[ii] = list;
                if (!triNode.ContainsKey(key(rid, ii)))
                    continue;
                // internal links
                for (int e = 0; e < o.IV0.Length; e++)
                {
                    int s0 = o.ISrc[e], d0 = o.IDst[e];
                    if (s0 < 0 || d0 < 0 || s0 >= o.T0.Length || d0 >= o.T0.Length)
                        continue;
                    int f = o.IFlag[e];
                    int A = nodeOf(rid, ii, s0), B = nodeOf(rid, ii, d0);
                    if (A < 0 || B < 0)
                        continue;
                    if ((f & 2) == 0 && !extras[A - baseCount].Edges.Contains(B))
                    {
                        extras[A - baseCount].Edges.Add(B);
                        linksObj++;
                    }
                    if ((f & 1) == 0 && !extras[B - baseCount].Edges.Contains(A))
                    {
                        extras[B - baseCount].Edges.Add(A);
                        linksObj++;
                    }
                }
                // open outline edges to the ground
                for (int e = 0; e < o.GV0.Length; e++)
                {
                    int s0 = o.GSrc[e];
                    if (s0 < 0 || s0 >= o.T0.Length)
                        continue;
                    int f = o.GFlag[e];
                    if ((f & 16) != 0)
                        continue;
                    if ((f & 3) == 3)
                    {
                        skipBlocked++;
                        continue;
                    }
                    int A = nodeOf(rid, ii, s0);
                    if (A < 0)
                        continue;
                    var EA = extras[A - baseCount];
                    double ax = o.VX[o.GV0[e]] * cs - o.VZ[o.GV0[e]] * sn + inst.X, az = o.VX[o.GV0[e]] * sn + o.VZ[o.GV0[e]] * cs +
                        inst.Z, bx = o.VX[o.GV1[e]] * cs - o.VZ[o.GV1[e]] * sn + inst.X, bz = o.VX[o.GV1[e]] * sn + o.VZ[o.GV1[e]] * cs +
                        inst.Z;
                    double mx = (ax + bx) / 2, mz = (az + bz) / 2, nx = bz - az, nz = ax - bx, nl = Math.Sqrt(nx * nx +
                        nz * nz);
                    if (nl < 1e-6)
                        continue;
                    nx /= nl;
                    nz /= nl;
                    if (InTri(mx + nx * 0.5, mz + nz * 0.5, EA))
                    {
                        nx = -nx;
                        nz = -nz;
                    }
                    double edgeY = (o.VY[o.GV0[e]] + o.VY[o.GV1[e]]) / 2 + inst.Y;
                    int tc = -1;
                    bool sawCell = false;
                    foreach (double off in new[] { 6.0, 30.0, 60.0, 120.0 })
                    {
                        int cand = terrainAt(rid, mx + nx * off, mz + nz * off);
                        if (cand < 0 || (cells[cand].Flags & 1) != 0)
                            continue;
                        sawCell = true;
                        if (Math.Abs(cells[cand].H - edgeY) <= 25)
                        {
                            tc = cand;
                            break;
                        }
                    }
                    if (tc < 0)
                    {
                        if (sawCell)
                            skipHeight++;
                        else
                            skipNoCell++;
                        continue;
                    }
                    if ((f & 2) == 0 && !EA.Edges.Contains(tc))
                    {
                        EA.Edges.Add(tc);
                        linksTerrain++;
                    }
                    if ((f & 1) == 0)
                        addEdges.Add(((long)tc << 32) | (uint)A);
                }
                foreach (var lk in inst.Links)
                {
                    if (lk[0] < 0)
                        continue;
                    int other = lk[0];
                    if (other < 0 || other >= reg.Insts.Count)
                        continue;
                    var o2 = getObj(reg.Insts[other].ObjIndex);
                    if (o2 == null)
                        continue;
                    int myEdge = lk[2], hisEdge = lk[1];
                    if (myEdge < 0 || myEdge >= o.GSrc.Length || hisEdge < 0 || hisEdge >= o2.GSrc.Length)
                        continue;
                    int A = nodeOf(rid, ii, o.GSrc[myEdge]), B = nodeOf(rid, other, o2.GSrc[hisEdge]);
                    if (A < 0 || B < 0)
                        continue;
                    if (!extras[A - baseCount].Edges.Contains(B))
                    {
                        extras[A - baseCount].Edges.Add(B);
                        linksInst++;
                    }
                    if (!extras[B - baseCount].Edges.Contains(A))
                    {
                        extras[B - baseCount].Edges.Add(A);
                        linksInst++;
                    }
                }
            }
            // terrain edges crossing an outline: wall; kept but expensive
            for (int c = 0; c < regionLen[(ushort)rid]; c++)
            {
                var rc = reg.Cells[c];
                if (rc.Insts.Length == 0)
                    continue;
                var tc = cells[start + c];
                var mine = new List<double[]>();
                foreach (int ii in rc.Insts)
                    if (ii >= 0 && ii < reg.Insts.Count)
                    {
                        List<double[]> l;
                        if (segs.TryGetValue(ii, out l))
                            mine.AddRange(l);
                    }
                if (mine.Count == 0)
                    continue;
                double cx = (tc.L + tc.R) / 2, cz = (tc.B + tc.T) / 2;
                foreach (int d in tc.Edges)
                {
                    if (d >= baseCount)
                        continue;
                    var td = cells[d];
                    double dx = (td.L + td.R) / 2 + ((td.Region & 255) - sx) * 1920.0, dz = (td.B + td.T) / 2 + ((td.Region >> 8) - sy) * 1920.0;
                    foreach (var sg in mine)
                    {
                        if (Cross(cx, cz, dx, dz, sg[0], sg[1], sg[2], sg[3]))
                        {
                            softSet.Add(((long)(start + c) << 32) | (uint)d);
                            softSet.Add(((long)d << 32) | (uint)(start + c));
                            break;
                        }
                    }
                }
            }
        }
        foreach (long k in addEdges)
        {
            int from = (int)(k >> 32), to = (int)(k & 0xffffffff);
            if (!cells[from].Edges.Contains(to))
                cells[from].Edges.Add(to);
        }
        var soft = softSet.OrderBy(k => k).ToList();
        foreach (int f in new HashSet<int>(soft.Select(k => (int)(k >> 32))))
            cells[f].Flags |= 16;
        Console.WriteLine("object links " + linksObj + "  terrain links " + (linksTerrain + addEdges.Count) +
            "  instance links " + linksInst + "  wall edges " + soft.Count + "  skipped: blocked " + skipBlocked +
            " noCell " + skipNoCell + " height " + skipHeight + "  " + sw.Elapsed.TotalSeconds.ToString("F0") + "s");
        using (var o = new BinaryWriter(new GZipStream(File.Create(a[2]), CompressionMode.Compress)))
        {
            o.Write(0x32304452);
            o.Write(regionStart.Count);
            o.Write(baseCount);
            o.Write(extras.Count);
            foreach (var kv in regionStart.OrderBy(k => k.Value))
            {
                o.Write(kv.Key);
                o.Write(regionLen[kv.Key]);
                for (int j = 0; j < regionLen[kv.Key]; j++)
                {
                    var t = cells[kv.Value + j];
                    o.Write(t.L);
                    o.Write(t.B);
                    o.Write(t.R);
                    o.Write(t.T);
                    o.Write(t.H);
                    o.Write(t.Flags);
                    o.Write((ushort)t.Edges.Count);
                    foreach (int d in t.Edges)
                        o.Write(d);
                }
            }
            o.Write(soft.Count);
            foreach (long k in soft)
            {
                o.Write((int)(k >> 32));
                o.Write((int)(k & 0xffffffff));
            }
            foreach (var e in extras)
            {
                o.Write(e.Region);
                for (int k = 0; k < 3; k++)
                {
                    o.Write(e.X[k]);
                    o.Write(e.Z[k]);
                    o.Write(e.Y[k]);
                }
                o.Write((ushort)e.Edges.Count);
                foreach (int d in e.Edges)
                    o.Write(d);
            }
        }
        Console.WriteLine("written " + new FileInfo(a[2]).Length + " bytes  total " + sw.Elapsed.TotalSeconds.ToString("F0") +
            "s");
    }
}

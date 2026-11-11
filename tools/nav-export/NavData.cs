using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;

namespace NavData
{
    public class Archive
    {
        Dictionary<string, long[]> index = new Dictionary<string, long[]>();
        FileStream fs;

        public Archive(string file, string tsv)
        {
            fs = File.OpenRead(file);
            foreach (var l in File.ReadAllLines(tsv))
            {
                var p = l.Split((char)9);
                if (p.Length < 3)
                    continue;
                index[p[0].ToLowerInvariant()] = new[] { long.Parse(p[1]), long.Parse(p[2]) };
            }
        }

        public bool Has(string path)
        {
            return index.ContainsKey(path.ToLowerInvariant());
        }

        public byte[] Read(string path)
        {
            long[] e;
            if (!index.TryGetValue(path.ToLowerInvariant(), out e))
                return null;
            var b = new byte[e[1]];
            fs.Position = e[0];
            fs.Read(b, 0, b.Length);
            return b;
        }

        public IEnumerable<string> Paths
        {
            get { return index.Keys; }
        }
    }

    public class NavObj
    {
        public string Name;
        public float[] VX, VY, VZ;
        public int[] T0, T1, T2;
        public short[] CellFlag;
        public int[] GV0, GV1, GSrc, GDst, GFlag;
        public int[] IV0, IV1, ISrc, IDst, IFlag;
        public bool Enterable;
    }

    public class Inst
    {
        public int ObjIndex;
        public float X, Y, Z, Yaw;
        public int Id, Region;
        public bool Big, Struct;
        public List<int[]> Links = new List<int[]>();
    } // link: linkedObjId, linkedEdge, edge

    public class Cell
    {
        public float X0, Z0, X1, Z1;
        public int[] Insts;
    }

    public class GEdge
    {
        public float AX, AZ, BX, BZ;
        public int Flag, SrcCell, DstCell, SrcRegion, DstRegion;
    }

    public class IEdge
    {
        public float AX, AZ, BX, BZ;
        public int Flag, SrcCell, DstCell;
    }

    public class Region
    {
        public int Id;
        public List<Inst> Insts = new List<Inst>();
        public List<Cell> Cells = new List<Cell>();
        public int Open;
        public List<GEdge> Global = new List<GEdge>();
        public List<IEdge> Internal = new List<IEdge>();
    }

    public static class Loader
    {
        static string Str(BinaryReader r)
        {
            int n = r.ReadInt32();
            if (n < 0 || n > 8192)
                throw new Exception("string");
            var b = r.ReadBytes(n);
            int z = Array.IndexOf(b, (byte)0);
            return Encoding.ASCII.GetString(b, 0, z < 0 ? n : z);
        }

        public static Region ReadRegion(byte[] data, int id)
        {
            var r = new BinaryReader(new MemoryStream(data));
            var reg = new Region { Id = id };
            string sig = Encoding.ASCII.GetString(r.ReadBytes(12));
            if (!sig.StartsWith("JMXVNVM"))
                throw new Exception("nvm sig");
            int n = r.ReadUInt16();
            for (int i = 0; i < n; i++)
            {
                var s = new Inst();
                s.ObjIndex = r.ReadInt32();
                s.X = r.ReadSingle();
                s.Y = r.ReadSingle();
                s.Z = r.ReadSingle();
                r.ReadInt16();
                s.Yaw = r.ReadSingle();
                s.Id = r.ReadUInt16();
                r.ReadUInt16();
                s.Big = r.ReadBoolean();
                s.Struct = r.ReadBoolean();
                s.Region = r.ReadUInt16();
                int lc = r.ReadInt16();
                for (int k = 0; k < lc; k++)
                    s.Links.Add(new int[] { r.ReadInt16(), r.ReadInt16(), r.ReadInt16() });
                reg.Insts.Add(s);
            }
            int total = r.ReadInt32();
            reg.Open = r.ReadInt32();
            for (int i = 0; i < total; i++)
            {
                var c = new Cell { X0 = r.ReadSingle(), Z0 = r.ReadSingle(), X1 = r.ReadSingle(), Z1 = r.ReadSingle() };
                int ic = r.ReadByte();
                c.Insts = new int[ic];
                for (int k = 0; k < ic; k++)
                    c.Insts[k] = r.ReadInt16();
                reg.Cells.Add(c);
            }
            int ge = r.ReadInt32();
            for (int i = 0; i < ge; i++)
            {
                var e = new GEdge { AX = r.ReadSingle(), AZ = r.ReadSingle(), BX = r.ReadSingle(), BZ = r.ReadSingle() };
                e.Flag = r.ReadByte();
                r.ReadByte();
                r.ReadByte();
                e.SrcCell = r.ReadInt16();
                e.DstCell = r.ReadInt16();
                e.SrcRegion = r.ReadInt16();
                e.DstRegion = r.ReadInt16();
                reg.Global.Add(e);
            }
            int ie = r.ReadInt32();
            for (int i = 0; i < ie; i++)
            {
                var e = new IEdge { AX = r.ReadSingle(), AZ = r.ReadSingle(), BX = r.ReadSingle(), BZ = r.ReadSingle() };
                e.Flag = r.ReadByte();
                r.ReadByte();
                r.ReadByte();
                e.SrcCell = r.ReadInt16();
                e.DstCell = r.ReadInt16();
                reg.Internal.Add(e);
            }
            return reg;
        }

        public static NavObj ReadBms(byte[] data)
        {
            var r = new BinaryReader(new MemoryStream(data));
            string sig = Encoding.ASCII.GetString(r.ReadBytes(12));
            if (sig != "JMXVBMS 0110")
                return null;
            int[] h = new int[12];
            for (int i = 0; i < 12; i++)
                h[i] = r.ReadInt32();
            int bboxOff = h[5], navOff = h[7], option = h[11];
            if (navOff == 0)
                return null;
            r.ReadInt32();
            r.ReadInt32();
            r.ReadInt32();
            var o = new NavObj();
            o.Name = Str(r);
            r.BaseStream.Position = navOff;
            int vc = r.ReadInt32();
            o.VX = new float[vc];
            o.VY = new float[vc];
            o.VZ = new float[vc];
            for (int i = 0; i < vc; i++)
            {
                o.VX[i] = r.ReadSingle();
                o.VY[i] = r.ReadSingle();
                o.VZ[i] = r.ReadSingle();
                r.ReadByte();
            }
            int cc = r.ReadInt32();
            o.T0 = new int[cc];
            o.T1 = new int[cc];
            o.T2 = new int[cc];
            o.CellFlag = new short[cc];
            for (int i = 0; i < cc; i++)
            {
                o.T0[i] = r.ReadInt16();
                o.T1[i] = r.ReadInt16();
                o.T2[i] = r.ReadInt16();
                o.CellFlag[i] = r.ReadInt16();
                if ((option & 2) != 0)
                    r.ReadByte();
            }
            int ge = r.ReadInt32();
            o.GV0 = new int[ge];
            o.GV1 = new int[ge];
            o.GSrc = new int[ge];
            o.GDst = new int[ge];
            o.GFlag = new int[ge];
            for (int i = 0; i < ge; i++)
            {
                o.GV0[i] = r.ReadInt16();
                o.GV1[i] = r.ReadInt16();
                o.GSrc[i] = r.ReadInt16();
                o.GDst[i] = r.ReadInt16();
                o.GFlag[i] = r.ReadByte();
                if ((option & 1) != 0)
                    r.ReadByte();
            }
            int ie = r.ReadInt32();
            o.IV0 = new int[ie];
            o.IV1 = new int[ie];
            o.ISrc = new int[ie];
            o.IDst = new int[ie];
            o.IFlag = new int[ie];
            for (int i = 0; i < ie; i++)
            {
                o.IV0[i] = r.ReadInt16();
                o.IV1[i] = r.ReadInt16();
                o.ISrc[i] = r.ReadInt16();
                o.IDst[i] = r.ReadInt16();
                o.IFlag[i] = r.ReadByte();
                if ((option & 1) != 0)
                    r.ReadByte();
            }
            for (int i = 0; i < ge; i++)
                if ((o.GFlag[i] & 3) != 3)
                    o.Enterable = true;
            return o;
        }

        static string ReadNavPath(byte[] data, string expectSig, int offsetIndex)
        {
            var r = new BinaryReader(new MemoryStream(data));
            string sig = Encoding.ASCII.GetString(r.ReadBytes(12));
            if (sig != expectSig)
                return null;
            int[] h = new int[offsetIndex + 1];
            for (int i = 0; i <= offsetIndex; i++)
                h[i] = r.ReadInt32();
            if (h[offsetIndex] == 0)
                return null;
            r.BaseStream.Position = h[offsetIndex];
            return Str(r);
        }

        public static NavObj LoadObject(Archive data, string path, int depth)
        {
            if (depth > 3 || string.IsNullOrEmpty(path))
                return null;
            string lower = path.ToLowerInvariant();
            byte[] b = data.Read(lower);
            if (b == null)
                return null;
            if (lower.EndsWith(".bms"))
                return ReadBms(b);
            if (lower.EndsWith(".bsr"))
            {
                var nav = ReadNavPath(b, "JMXVRES 0109", 7);
                return nav == null ? null : LoadObject(data, nav, depth + 1);
            }
            if (lower.EndsWith(".cpd"))
            {
                var nav = ReadNavPath(b, "JMXVCPD 0101", 0);
                return nav == null ? null : LoadObject(data, nav, depth + 1);
            }
            return null;
        }

        public static Dictionary<int, string> ObjectIndex(byte[] ifo)
        {
            var map = new Dictionary<int, string>();
            foreach (var line in Encoding.GetEncoding(1254).GetString(ifo).Split('\n'))
            {
                var m = System.Text.RegularExpressions.Regex.Match(line.Trim(), "^(\\d{5}) 0x[0-9a-fA-F]{8} \"(.+?)\"");
                if (m.Success)
                    map[int.Parse(m.Groups[1].Value)] = m.Groups[2].Value;
            }
            return map;
        }
    }
}

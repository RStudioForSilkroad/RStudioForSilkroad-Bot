using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
// Extract files from a PK2-style archive using the index written by Pk2Dump (path, position, size). Plain data, read only.
static class Pk2Extract
{
    static void Main(string[] a)
    {
        string archive = a[0], index = a[1], pattern = a[2], outDir = a[3];
        var rx = new Regex(pattern, RegexOptions.IgnoreCase);
        int count = 0;
        using (var fs = File.OpenRead(archive))
            foreach (var line in File.ReadAllLines(index))
            {
                var p = line.Split('\t');
                if (p.Length < 3 || !rx.IsMatch(p[0]))
                    continue;
                long pos = long.Parse(p[1]); int size = int.Parse(p[2]);
                var buf = new byte[size];
                fs.Position = pos;
                int got = 0;
                while (got < size) { int r = fs.Read(buf, got, size - got); if (r <= 0) break; got += r; }
                string target = Path.Combine(outDir, p[0]);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.WriteAllBytes(target, buf);
                count++;
            }
        Console.WriteLine(count + " dosya cikarildi");
    }
}

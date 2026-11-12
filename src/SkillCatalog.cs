using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

// One skill of the game's own skill table.
sealed class SkillInfo
{
    internal uint Id;
    internal string Name = "";
    // Family = every level of one skill shares it; Level is the skill's own level; ReqLevel the character level it asks for;
    // Time is the table's time column in milliseconds (a buff's duration, an attack's cool-down).
    internal int Family, Level, ReqLevel, Time;

    internal string Display
    {
        get { return Name + (Level > 0 ? " · Sv " + Level : ""); }
    }
}

// The game folder's data\skillname.txt (id TAB name) and data\skillmeta.txt (one skill per line, tab separated). Read once; used to name
// the character's skills and to tell which ids are real skill ids.
static class SkillCatalog
{
    static Dictionary<uint, SkillInfo> byId = new Dictionary<uint, SkillInfo>();
    static bool[] valid = new bool[0];

    internal static bool Loaded
    {
        get { return byId.Count > 0; }
    }

    internal static int Count
    {
        get { return byId.Count; }
    }

    internal static void Clear()
    {
        byId = new Dictionary<uint, SkillInfo>();
        valid = new bool[0];
    }

    static string[] Cells(string line)
    {
        return line.TrimEnd('\r', '\n').Split('\t');
    }

    internal static void Parse(IEnumerable<string> names, IEnumerable<string> meta)
    {
        var map = new Dictionary<uint, SkillInfo>();
        foreach (string line in names)
        {
            var c = Cells(line);
            uint id;
            if (c.Length >= 2 && uint.TryParse(c[0], out id) && id > 0 && c[1].Trim().Length > 0)
                map[id] = new SkillInfo { Id = id, Name = c[1].Trim() };
        }
        foreach (string line in meta)
        {
            var c = Cells(line);
            uint id;
            SkillInfo info;
            if (c.Length < 25 || !uint.TryParse(c[0], out id) || !map.TryGetValue(id, out info))
                continue;
            info.Time = Num(c[5]);
            info.ReqLevel = Num(c[11]);
            info.Family = Num(c[23]);
            info.Level = Num(c[24]);
        }
        uint max = map.Count == 0 ? 0 : map.Keys.Max();
        var flags = new bool[max + 1];
        foreach (uint id in map.Keys)
            flags[id] = true;
        byId = map;
        valid = flags;
    }

    static int Num(string text)
    {
        int n;
        return int.TryParse(text, out n) ? n : 0;
    }

    // data\skillname.txt next to the game; true when both tables were read.
    internal static bool LoadFrom(string gameFolder)
    {
        try
        {
            string dir = Path.Combine(gameFolder, "data");
            string names = Path.Combine(dir, "skillname.txt"), meta = Path.Combine(dir, "skillmeta.txt");
            if (!File.Exists(names))
                return false;
            Parse(File.ReadAllLines(names, Encoding.UTF8), File.Exists(meta) ? File.ReadAllLines(meta, Encoding.UTF8) : new string[0]);
            return Loaded;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal static bool IsSkill(uint id)
    {
        return id > 0 && id < valid.Length && valid[id];
    }

    internal static bool[] ValidTable
    {
        get { return valid; }
    }

    internal static SkillInfo Find(uint id)
    {
        SkillInfo info;
        return byId.TryGetValue(id, out info) ? info : null;
    }

    internal static string NameOf(uint id)
    {
        var info = Find(id);
        return info == null ? "" : info.Name;
    }

    internal static int FamilyOf(uint id)
    {
        var info = Find(id);
        return info == null ? 0 : info.Family;
    }

    // Skills whose name contains the text (case-insensitive), the highest level of each family first.
    internal static List<SkillInfo> Search(string text, int limit)
    {
        text = (text ?? "").Trim();
        if (text.Length < 2)
            return new List<SkillInfo>();
        return byId.Values.Where(s => s.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(s => s.Name)
            .ThenByDescending(s => s.Level).ThenByDescending(s => s.Id).Take(limit).ToList();
    }

    // A first guess for the kind of a line from the table: long times are buffs. Only a suggestion; the user can change it.
    internal static string GuessKind(SkillInfo info)
    {
        return info != null && info.Time >= 30000 ? SkillRotation.Buff_ : SkillRotation.Attack_;
    }
}

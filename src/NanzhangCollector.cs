using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

// Nanzhang data: the NPCs and quests that come with the bot (read from the game's own text tables, see tools/build-nanzhang-world.py) and the
// NPCs the bot has seen on the Nanzhang map while the player walked (kept in nanzhang-collected.json).
public sealed class CollectedNpc
{
    public uint Model;
    public string Name = "";
    public double X, Y;
    public int Region;
    public string Seen = "";
}

public sealed class NanzhangStore
{
    public List<CollectedNpc> Npcs = new List<CollectedNpc>();
}

// An NPC spawn from the game's tables.
public sealed class NzNpc
{
    public int id, region;
    public string code = "", name = "";
    public double x, y, height;

    // The tables name some NPCs without a position (the server places them); the bot learns it when it sees them.
    public bool Located
    {
        get { return x != 0 || y != 0; }
    }
}

// A quest of a Nanzhang NPC: what has to be done, the reward and the conversation, as the game's tables give them.
public sealed class NzQuest
{
    public string code = "", title = "", npc = "", mission = "", rewards = "";
    public int level, npcId;
    public List<string> dialog = new List<string>();

    // The quests that only talk (no reward) are the story lines between the real quests.
    public bool HasReward
    {
        get { return rewards.Length > 0 && rewards != "(none)"; }
    }
}

public sealed class NanzhangWorld
{
    public List<int> regions = new List<int>();
    public List<NzNpc> npcs = new List<NzNpc>();
    public List<NzQuest> quests = new List<NzQuest>();
}

static class NanzhangData
{
    static NanzhangWorld world;

    internal static NanzhangWorld World
    {
        get
        {
            if (world == null)
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("nanzhang-world.json"))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                    world = new JavaScriptSerializer { MaxJsonLength = 16000000 }.Deserialize<NanzhangWorld>(reader.ReadToEnd());
            return world;
        }
    }

    internal static string StorePath
    {
        get { return Path.Combine(AppPaths.DataRoot, "nanzhang-collected.json"); }
    }

    internal static NanzhangStore Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var store = new JavaScriptSerializer { MaxJsonLength = 16000000 }.Deserialize<NanzhangStore>(File.ReadAllText(path, Encoding.UTF8));
                if (store != null)
                {
                    store.Npcs = store.Npcs ?? new List<CollectedNpc>();
                    return store;
                }
            }
        }
        catch (Exception) { }
        return new NanzhangStore();
    }

    internal static void Save(string path, NanzhangStore store)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, new JavaScriptSerializer { MaxJsonLength = 16000000 }.Serialize(store), new UTF8Encoding(false));
    }

    static HashSet<int> mapRegions;

    // The Nanzhang map as the game's world map shows it: the city, its outskirts, Mossgate, Dugu Manor, Blightwood, Venom Fen, Gloom Swamps,
    // Forest of Whispers, Hollow Hive Ridge with Kuhe, Old Mining Camp and the rest, by the regions of those zones.
    internal static bool InMap(Position p)
    {
        if (p == null || !p.Outside)
            return false;
        if (mapRegions == null)
            mapRegions = new HashSet<int>(World.regions);
        return V3Data.Town(p) == "Nanzhang" || mapRegions.Contains((int)p.Region);
    }

    // True when an NPC of this model is already known within 14 units (an NPC that walks about is not recorded twice).
    internal static bool Known(IEnumerable<CollectedNpc> known, uint model, double x, double y)
    {
        return known.Any(n => n.Model == model && Math.Abs(n.X - x) < 14 && Math.Abs(n.Y - y) < 14);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

sealed class CaravanScript
{
    public string From, To, Source;
    public List<string> Steps;
}

sealed class CaravanScriptFile
{
    public Dictionary<string, string> Names;
    public List<CaravanScript> Scripts;
}

// Ready-made caravan roads taken from phBot caravan scripts (data/caravan-scripts.json, built by tools/game-data/import_phbot_caravan.py): the way
// points walked by hand with the ferries between them. The routes that have one follow it instead of a road planned from the map; every other route
// is still planned. A script holds only walk / wait / teleport lines: chat, trade and mount commands of the original files are never kept.
static class CaravanScripts
{
    static readonly CaravanScriptFile file = V3Data.Load<CaravanScriptFile>("caravan-scripts.json");

    // How close a character has to be to a way point to continue a road that was begun (stopped half way, or after a restart).
    internal const double Reach = 60;

    // Set by the checks to look at the planned road on its own.
    internal static bool Disabled;

    internal static IEnumerable<CaravanScript> All
    {
        get { return file.Scripts; }
    }

    internal static CaravanScript Find(string from, string to)
    {
        return file.Scripts.FirstOrDefault(s => s.From == from && s.To == to);
    }

    // The script's commands with each ferry NPC's name replaced by the gate code the transport network knows it by.
    internal static List<string> Commands(CaravanScript script)
    {
        var commands = new List<string>();
        foreach (string line in script.Steps)
        {
            var parts = line.Split(',');
            if (parts[0] == "teleport" && parts.Length == 3)
            {
                string from, to;
                commands.Add("teleport," + (file.Names.TryGetValue(parts[1], out from) ? from : parts[1]) + "," +
                    (file.Names.TryGetValue(parts[2], out to) ? to : parts[2]));
            }
            else
                commands.Add(line);
        }
        return commands;
    }

    // The walk and ferry steps of a script, or null when one of its lines is not understood.
    internal static List<TransitStep> Steps(CaravanScript script)
    {
        return PhBotRoutes.Parse(Commands(script));
    }

    internal static int WalkPoints(CaravanScript script)
    {
        return script.Steps.Count(s => s.StartsWith("walk,", StringComparison.Ordinal));
    }

    internal static int Ferries(CaravanScript script)
    {
        return script.Steps.Count(s => s.StartsWith("teleport,", StringComparison.Ordinal));
    }

    // The steps of the script still ahead for this start and goal, in walking order; null when no script leads to the goal's town.
    // A character in the script's first town begins at the first point (`atStart`); one standing on the road continues from the nearest point.
    internal static List<TransitStep> Ahead(Position start, Position goal, out bool atStart)
    {
        atStart = false;
        string toTown = TradeRoutes.TownKey(goal);
        if (Disabled || start == null || toTown == null)
            return null;
        string here = TradeRoutes.TownKey(start);
        List<TransitStep> onRoad = null;
        int nearest = -1;
        double best = Reach;
        foreach (var script in file.Scripts.Where(s => s.To == toTown))
        {
            var steps = Steps(script);
            if (steps == null || steps.Count == 0)
                continue;
            if (script.From == here)
            {
                atStart = true;
                return steps;
            }
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i].From != null)
                    continue;
                double d = TradeRoutes.RoadDistance(start, steps[i].To.Point());
                if (d < best)
                {
                    best = d;
                    nearest = i;
                    onRoad = steps;
                }
            }
        }
        if (onRoad == null)
            return null;
        var ahead = onRoad.Skip(nearest).ToList();
        // the nearest point is skipped when it is right under the character
        if (ahead.Count > 1 && ahead[0].From == null && TradeRoutes.RoadDistance(start, ahead[0].To.Point()) < 6)
            ahead.RemoveAt(0);
        return ahead;
    }

    // The script as text for a phBot-style bot: walk, wait and teleport lines under a short header.
    internal static string Text(CaravanScript script)
    {
        var text = new StringBuilder();
        text.AppendLine("# Silkroad R Studio · kervan rotası");
        text.AppendLine("# " + script.From + " → " + script.To + " · " + WalkPoints(script) + " nokta · " + Ferries(script) + " feribot geçişi · kaynak: phBot kervan scripti " +
            script.Source);
        text.AppendLine("# Biçim: walk,X,Y,Z · teleport,NPC,NPC · wait,ms");
        foreach (string line in script.Steps)
            text.AppendLine(line);
        return text.ToString();
    }
}

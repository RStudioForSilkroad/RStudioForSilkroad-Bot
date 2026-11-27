using System;
using System.Collections.Generic;
using System.Linq;

// One destination of the Reverse Return Scroll: the game's own table (refoptionalteleport) has about fifty of them, from the big towns to
// hunting grounds. The scroll's third choice teleports to the one that is picked from this list.
sealed class ReversePoint
{
    public int Id;
    public string Name, Turkish;
    public int Region;
    public double X, Y, Height;

    internal Position Point()
    {
        return V2Rules.Center(X, Y, (float)Height);
    }
}

static class ReverseRules
{
    // The length of the Reverse scroll's lock: after one use the next one is not tried for this long (milliseconds).
    internal const long LockMs = 70000;

    internal static readonly List<ReversePoint> All = V3Data.Load<List<ReversePoint>>("reverse-points.json");

    // Points that are not places of the open world: the floors of the Donwhang cave and of the Qin-Shi tomb (their regions are instances) and the two
    // "flag position only" notes of the table (29, 31). They are not offered as a place to start walking from. Everything else, the mountain places
    // included (Roc Mountain, Heart Peak, Lost Town ... 9 to 13), is overland.
    static readonly HashSet<int> NotOverland = new HashSet<int> { 29, 30, 31, 32, 33, 38 };

    internal static IEnumerable<ReversePoint> Overland
    {
        get { return All.Where(p => !NotOverland.Contains(p.Id)); }
    }

    internal static ReversePoint Find(int id)
    {
        return All.FirstOrDefault(p => p.Id == id);
    }

    // The point that leaves the shortest way to the goal: the straight distance when no way length is known, else the way length that
    // the caller measures (for example along the roads). Returns null when no point is closer than the character already is.
    internal static ReversePoint Nearest(Position from, Position goal, Func<Position, Position, double> wayLength, double margin)
    {
        double current = wayLength(from, goal);
        ReversePoint best = null;
        double bestLength = current - margin;
        // only the few places closest to the goal as the crow flies are measured along the roads
        foreach (var point in Overland.OrderBy(p => p.Point().Distance(goal)).Take(6))
        {
            double length = wayLength(point.Point(), goal);
            if (length < bestLength)
            {
                best = point;
                bestLength = length;
            }
        }
        return best;
    }
}

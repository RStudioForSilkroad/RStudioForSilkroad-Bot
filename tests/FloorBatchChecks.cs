using System;
using System.Collections.Generic;
using System.Linq;

// The Grim floor code asks the game for walkability in batches: same map, same coverage, same route as the single-query path, far fewer calls.
static class FloorBatchChecks
{
    // A walled room with a door and two pillars (region 30085: X -384..-192, Y 4800..5184).
    static bool Free(double x, double y)
    {
        if (Math.Abs(x + 290) > 80 || Math.Abs(y - 4900) > 90)
            return false;
        if (x > -293 && x < -287 && y > 4850 && y < 4930 && !(y > 4895 && y < 4905))
            return false;
        if (Math.Abs(x + 250) < 6 && Math.Abs(y - 4930) < 6)
            return false;
        return !(Math.Abs(x + 330) < 6 && Math.Abs(y - 4870) < 6);
    }

    static bool Open(Position a, Position b)
    {
        for (int i = 0; i <= 40; i++)
        {
            double t = i / 40.0;
            if (!Free(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t))
                return false;
        }
        return true;
    }

    static bool SameGraph(FloorNavigation a, FloorNavigation b)
    {
        return a.Cells.Count == b.Cells.Count && a.Queries == b.Queries && a.Done == b.Done && a.Cells.Keys.All(k => b.Cells.ContainsKey(k) &&
            a.Edges[k].SetEquals(b.Edges[k]));
    }

    internal static void Run(Action<bool, string> check)
    {
        int maxChunk = 0;
        var origin = V2Rules.Center(-290, 4900, 0);
        // live scan: the same cells, links and query count after every call of every budget, with answers of -1 mixed in
        foreach (int budget in new[] { 48, 7, 1 })
        {
            var single = new FloorNavigation(origin, 30085);
            var batched = new FloorNavigation(origin, 30085);
            int singleCalls = 0, batchCalls = 0, unknown = 0;
            Func<Position, Position, bool> one = (a, b) =>
            {
                singleCalls++;
                return Open(a, b);
            };
            Func<Position, Position, bool> fallback = (a, b) => Open(a, b);
            Func<IList<Position[]>, sbyte[]> many = pairs =>
            {
                batchCalls++;
                maxChunk = Math.Max(maxChunk, pairs.Count);
                return pairs.Select(p => (unknown++ % 5 == 4) ? (sbyte)-1 : (sbyte)(Open(p[0], p[1]) ? 1 : 0)).ToArray();
            };
            bool same = true;
            for (int i = 0; i < 6000 && (!single.Done || !batched.Done); i++)
            {
                single.Advance(one, budget);
                batched.Advance(fallback, budget, many);
                same &= SameGraph(single, batched);
            }
            check(single.Done && batched.Done && single.Cells.Count > 300 && same, "Floor scan (budget " + budget + "): batched queries give the same cells, links and query count after every call");
            if (budget == 48)
                check(batchCalls * 8 <= singleCalls, "Floor scan: " + singleCalls + " single calls become " + batchCalls + " batch calls");
        }
        // looking at a position and routing, over every captured Solo Trial floor, from places between the cells
        foreach (var trial in V3Data.Dungeons.Where(x => x.Mode == "Solo Trial"))
        {
            var single = FloorNavigation.LoadCaptured(trial);
            var batched = FloorNavigation.LoadCaptured(trial);
            int singleCalls = 0, batchCalls = 0;
            Func<Position, Position, bool> pure = (a, b) => a.Distance(b) <= 7 && ((int)(a.X * 7 + b.Y * 13) % 5 != 0);
            Func<Position, Position, bool> one = (a, b) =>
            {
                singleCalls++;
                return pure(a, b);
            };
            Func<IList<Position[]>, sbyte[]> many = pairs =>
            {
                batchCalls++;
                maxChunk = Math.Max(maxChunk, pairs.Count);
                return pairs.Select(p => (sbyte)(pure(p[0], p[1]) ? 1 : 0)).ToArray();
            };
            Position pos = single.Origin.Copy();
            bool same = true;
            int steps = 0;
            while (!single.FullyCovered && steps++ < 250)
            {
                Position off = V2Rules.Center(pos.X + 1.3, pos.Y + 1.1, pos.Height);
                single.ObservePosition(off, one);
                batched.ObservePosition(off, pure, many);
                var a = single.RouteToUncovered(pos, one);
                var b = batched.RouteToUncovered(pos, pure, many);
                same &= single.Covered.SetEquals(batched.Covered) && a.Count == b.Count && a.Zip(b, (p, q) => p.Distance(q) < .001).All(t => t);
                if (a.Count < 2)
                    break;
                foreach (Position p in a)
                {
                    pos = p.Copy();
                    single.ObservePosition(pos);
                    batched.ObservePosition(pos);
                }
            }
            check(same && single.Covered.SetEquals(batched.Covered) && single.FullyCovered == batched.FullyCovered && steps > 1,
                trial.Code + " Solo Trial: batched look-ups cover the same cells and plan the same routes");
            check(batchCalls <= singleCalls, trial.Code + " Solo Trial: batched look-ups need no more calls (" + singleCalls + " -> " + batchCalls + ")");
        }
        // a character standing still is looked at once, not on every tick
        var floor = FloorNavigation.LoadCaptured(V3Data.Dungeons.First(x => x.Code == "D13" && x.Mode == "Solo Trial"));
        var still = V2Rules.Center(floor.Origin.X + 1.3, floor.Origin.Y + 1.1, floor.Origin.Height);
        int stillCalls = 0;
        Func<IList<Position[]>, sbyte[]> every = pairs =>
        {
            stillCalls++;
            return pairs.Select(p => (sbyte)1).ToArray();
        };
        for (int i = 0; i < 20; i++)
            floor.ObservePosition(still, (a, b) => true, every);
        check(stillCalls == 1 && floor.Covered.Count > 0, "A character standing still costs one batch call, not one per tick");
        floor.ObservePosition(V2Rules.Center(still.X + 2, still.Y, still.Height), (a, b) => true, every);
        check(stillCalls == 2, "A moved character is looked at again");
        // far candidates: more than 16 queries go out in chunks of at most 16
        Func<IList<Position[]>, sbyte[]> chunks = pairs =>
        {
            maxChunk = Math.Max(maxChunk, pairs.Count);
            return pairs.Select(p => (sbyte)0).ToArray();
        };
        var wide = new FloorNavigation(origin, 30085);
        wide.Advance((a, b) => false, 48, chunks);
        wide.Advance((a, b) => true, 48, chunks);
        check(maxChunk > 1 && maxChunk <= TargetSender.BatchMax, "No batch is larger than the game's batch limit of " + TargetSender.BatchMax);
        // answers of -1 for everything: each pair is asked again singly, with the same outcome as before
        var unknownAll = new FloorNavigation(origin, 30085);
        var reference = new FloorNavigation(origin, 30085);
        for (int i = 0; i < 3000 && (!unknownAll.Done || !reference.Done); i++)
        {
            unknownAll.Advance(Open, 48, pairs => pairs.Select(p => (sbyte)-1).ToArray());
            reference.Advance(Open, 48);
        }
        check(SameGraph(unknownAll, reference), "Without any batch answer the floor scan falls back to single queries with the same result");
    }
}

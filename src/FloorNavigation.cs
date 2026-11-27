using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;

sealed class FloorCell
{
    public int X, Y;

    internal string Key
    {
        get { return X + "," + Y; }
    }
}

sealed class FloorNavigation
{
    internal readonly Position Origin;
    internal readonly int InnerRegion;
    internal bool Done;
    internal int Queries;
    internal const int CellSize = 8;
    internal int Step = CellSize;
    internal string Source = "Canlı tarama";
    internal readonly Dictionary<string, FloorCell> Cells = new Dictionary<string, FloorCell>();
    internal readonly Dictionary<string, HashSet < string>> Edges = new Dictionary<string, HashSet < string>>();
    internal readonly HashSet<string> Covered = new HashSet<string>();
    readonly Queue<FloorCell> queue = new Queue<FloorCell>();
    readonly HashSet<string> tested = new HashSet<string>();
    FloorCell working;
    int direction;
    readonly int[] dx = { 1, -1, 0, 0 }, dy = { 0, 0, 1, -1 };

    internal FloorNavigation(Position origin, int innerRegion)
    {
        Origin = origin.Copy();
        InnerRegion = innerRegion;
        var first = new FloorCell();
        Cells.Add(first.Key, first);
        Edges.Add(first.Key, new HashSet<string>());
        queue.Enqueue(first);
    }

    internal Position PositionOf(FloorCell c)
    {
        return V2Rules.Center(Origin.X + c.X * Step, Origin.Y + c.Y * Step, Origin.Height);
    }

    sealed class Capture
    {
        public string Code, Mode;
        public int Region, Step;
        public double OriginX, OriginY;
        public float Height;
        public bool Complete, PositionStable;
        public List<FloorCell> Cells;
        public List<string> Edges;
    }

    internal static FloorNavigation LoadCaptured(DungeonLocation location)
    {
        if (location == null || !location.IsSolo)
            return null;
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("GrimFloor." + location.Code +
            ".SoloTrial.json"))
        {
            if (stream == null)
                return null;
            Capture data;
            using (var reader = new StreamReader(stream))
                data = new JavaScriptSerializer { MaxJsonLength = 20000000 }.Deserialize<Capture>(reader.ReadToEnd());
            if (data == null || data.Code != location.Code || data.Mode != "Solo Trial" || data.Region != location.InnerRegion ||
                !data.Complete || !data.PositionStable || (data.Step != 4 && data.Step != 8) || data.Cells == null ||
                data.Cells.Count < 2 || data.Cells.Count > 20000 || data.Edges == null)
                throw new InvalidDataException("Kayıtlı dungeon haritası doğrulanamadı.");
            var floor = new FloorNavigation(V2Rules.Center(data.OriginX, data.OriginY, data.Height), data.Region);
            floor.Step = data.Step;
            floor.Cells.Clear();
            floor.Edges.Clear();
            floor.queue.Clear();
            double minX = ((data.Region & 255) - 135) * 192, minY = ((data.Region >> 8) - 92) * 192;
            foreach (var cell in data.Cells)
            {
                Position p = floor.PositionOf(cell);
                if (double.IsNaN(p.X) || double.IsNaN(p.Y) || p.X < minX || p.X > minX + 192 || p.Y < minY || p.Y > minY +
                    384 || floor.Cells.ContainsKey(cell.Key))
                    throw new InvalidDataException("Dungeon zemin noktası geçersiz.");
                floor.Cells.Add(cell.Key, cell);
                floor.Edges.Add(cell.Key, new HashSet<string>());
            }
            foreach (string edge in data.Edges)
            {
                var ends = edge.Split(':');
                if (ends.Length != 2 || !floor.Cells.ContainsKey(ends[0]) || !floor.Cells.ContainsKey(ends[1]))
                    throw new InvalidDataException("Dungeon geçiş bağlantısı geçersiz.");
                if (floor.PositionOf(floor.Cells[ends[0]]).Distance(floor.PositionOf(floor.Cells[ends[1]])) > data.Step +
                    .01)
                    throw new InvalidDataException("Dungeon geçişi komşu noktalara bağlı değil.");
                floor.Edges[ends[0]].Add(ends[1]);
                floor.Edges[ends[1]].Add(ends[0]);
            }
            var reachable = new HashSet<string>();
            var pending = new Queue<string>();
            pending.Enqueue(floor.Cells.Keys.First());
            while (pending.Count > 0)
            {
                string k = pending.Dequeue();
                if (!reachable.Add(k))
                    continue;
                foreach (string n in floor.Edges[k])
                    if (!reachable.Contains(n))
                        pending.Enqueue(n);
            }
            if (reachable.Count != floor.Cells.Count)
                throw new InvalidDataException("Dungeon haritası bağlantılı değil.");
            floor.Done = true;
            floor.Source = "Kayıtlı " + location.Code + " Solo Trial · canlı hareket kontrolü";
            return floor;
        }
    }

    // Several walkability queries in one call of the game (answers 1 open, 0 blocked, -1 no answer: that pair is asked again on its own).
    // The floor code gives them only for queries that do not depend on each other's answer; with none given every query is asked singly as before.
    internal const int BatchSize = TargetSender.BatchMax;

    static sbyte[] Ask(Func<IList<Position[]>, sbyte[]> many, List<Position[]> pairs)
    {
        var all = new sbyte[pairs.Count];
        for (int i = 0; i < pairs.Count; i += BatchSize)
        {
            var chunk = pairs.GetRange(i, Math.Min(BatchSize, pairs.Count - i));
            sbyte[] answers = many(chunk);
            for (int k = 0; k < chunk.Count; k++)
                all[i + k] = answers != null && answers.Length == chunk.Count ? answers[k] : (sbyte)-1;
        }
        return all;
    }

    static string EdgeKey(string a, string b)
    {
        return string.CompareOrdinal(a, b) < 0 ? a + ":" + b : b + ":" + a;
    }

    // The queries Advance is going to make next, in the order it makes them (the first one is given), as far as the budget goes: the rest of
    // the working cell's directions and then the cells already waiting in the queue. Cells found by these queries come after the queue, so none
    // of the answers can change which queries these are.
    Dictionary<string, sbyte> Prefetch(Func<IList<Position[]>, sbyte[]> many, string firstEdge, Position from, Position to, int limit,
        double minX, double minY)
    {
        limit = Math.Min(limit, 1 + 4500 - Queries);
        var keys = new List<string> { firstEdge };
        var pairs = new List<Position[]> { new[] { from, to } };
        var seen = new HashSet<string> { firstEdge };
        Action<FloorCell, int> scan = (cell, first) =>
        {
            for (int d = first; d < 4 && pairs.Count < limit; d++)
            {
                var next = new FloorCell { X = cell.X + dx[d], Y = cell.Y + dy[d] };
                Position p = PositionOf(next);
                if (p.X < minX + 3 || p.X > minX + 189 || p.Y < minY + 3 || p.Y > minY + 381)
                    continue;
                string edge = EdgeKey(cell.Key, next.Key);
                if (tested.Contains(edge) || !seen.Add(edge))
                    continue;
                keys.Add(edge);
                pairs.Add(new[] { PositionOf(cell), p });
            }
        };
        scan(working, direction);
        foreach (var cell in queue)
        {
            if (pairs.Count >= limit)
                break;
            scan(cell, 0);
        }
        sbyte[] answers = Ask(many, pairs);
        var ready = new Dictionary<string, sbyte>();
        for (int i = 0; i < keys.Count; i++)
            ready[keys[i]] = answers[i];
        return ready;
    }

    internal void Advance(Func<Position, Position, bool> probe, int budget, Func<IList<Position[]>, sbyte[]> many = null)
    {
        double minX = ((InnerRegion & 255) - 135) * 192, minY = ((InnerRegion >> 8) - 92) * 192;
        Dictionary<string, sbyte> ready = null;
        while (!Done && budget > 0)
        {
            if (working == null)
            {
                if (queue.Count == 0)
                {
                    Done = true;
                    break;
                }
                working = queue.Dequeue();
                direction = 0;
            }
            if (direction >= 4)
            {
                working = null;
                continue;
            }
            var next = new FloorCell { X = working.X + dx[direction], Y = working.Y + dy[direction] };
            direction++;
            Position p = PositionOf(next);
            if (p.X < minX + 3 || p.X > minX + 189 || p.Y < minY + 3 || p.Y > minY + 381)
                continue;
            string a = working.Key, b = next.Key, edge = string.CompareOrdinal(a, b) < 0 ? a + ":" + b : b + ":" + a;
            if (!tested.Add(edge))
                continue;
            if (Queries >= 4500)
                throw new InvalidOperationException("Dungeon zemin taraması sınırı aşıldı; rota doğrulanamadı.");
            Queries++;
            budget--;
            Position origin = PositionOf(working);
            sbyte answer = -1;
            if (many != null)
            {
                if (ready == null || !ready.ContainsKey(edge))
                    ready = Prefetch(many, edge, origin, p, budget + 1, minX, minY);
                answer = ready[edge];
            }
            if (answer < 0 ? !probe(origin, p) : answer == 0)
                continue;
            if (!Cells.ContainsKey(next.Key))
            {
                Cells.Add(next.Key, next);
                Edges.Add(next.Key, new HashSet<string>());
                queue.Enqueue(next);
            }
            Edges[a].Add(b);
            Edges[b].Add(a);
        }
    }

    // The first of the candidates (in the order given) that the character stands on or can walk to; with several queries to ask they go to the
    // game together (they do not depend on each other: only the first open one counts).
    FloorCell FirstOpen(Position p, IEnumerable<FloorCell> candidates, double range, Func<Position, Position, bool> probe,
        Func<IList<Position[]>, sbyte[]> many)
    {
        var list = new List<FloorCell>();
        var pairs = new List<Position[]>();
        foreach (var c in candidates)
        {
            double d = PositionOf(c).Distance(p);
            if (d < .1)
            {
                list.Add(c);
                break;
            }
            if (d <= range)
            {
                list.Add(c);
                pairs.Add(new[] { p, PositionOf(c) });
            }
        }
        sbyte[] answers = pairs.Count == 0 ? new sbyte[0] : Ask(many, pairs);
        int k = 0;
        foreach (var c in list)
        {
            if (PositionOf(c).Distance(p) < .1)
                return c;
            sbyte answer = answers[k++];
            if (answer < 0 ? probe(p, PositionOf(c)) : answer == 1)
                return c;
        }
        return null;
    }

    // What the last look at a position (with batched queries) found: the same position over the same cells and links marks the same cells.
    Position observed;
    int observedGraph = -1;

    int GraphSize()
    {
        int n = Cells.Count;
        foreach (var e in Edges.Values)
            n += e.Count;
        return n;
    }

    internal void ObservePosition(Position p, Func<Position, Position, bool> probe = null, Func<IList<Position[]>, sbyte[]> many = null)
    {
        bool batched = probe != null && many != null;
        if (batched && observed != null && observedGraph == GraphSize() && observed.Region == p.Region && observed.X == p.X &&
            observed.Y == p.Y && observed.Height == p.Height)
            return;
        // Coverage follows graph distance, so a room across a wall is not marked
        // visited merely because it is close in straight-line coordinates.
        var nearest = Cells.Values.OrderBy(c => PositionOf(c).Distance(p)).Take(12);
        var start = batched ? FirstOpen(p, nearest, Step * 1.5, probe, many) : nearest.FirstOrDefault(c => PositionOf(c).Distance(p) <.1 ||
            (PositionOf(c).Distance(p) <= Step * 1.5 && (probe == null || probe(p, PositionOf(c)))));
        observed = null;
        if (start == null)
            return;
        if (batched)
        {
            observed = p.Copy();
            observedGraph = GraphSize();
        }
        var q = new Queue<string>();
        var distances = new Dictionary<string, int>();
        q.Enqueue(start.Key);
        distances[start.Key] = 0;
        while (q.Count > 0)
        {
            string k = q.Dequeue();
            Covered.Add(k);
            if (distances[k] + Step > 32)
                continue;
            foreach (string n in Edges[k])
                if (!distances.ContainsKey(n))
                {
                    distances[n] = distances[k] + Step;
                    q.Enqueue(n);
                }
        }
    }

    internal Position NearestCell(Position p)
    {
        var cell = Cells.Values.OrderBy(c => PositionOf(c).Distance(p)).FirstOrDefault();
        return cell == null ? null : PositionOf(cell);
    }

    // A goal the walker could not reach is not tried again at once: the cells around it count as visited.
    internal void MarkCovered(Position p)
    {
        foreach (var c in Cells.Values)
            if (PositionOf(c).Distance(p) <= Step * 1.5)
                Covered.Add(c.Key);
    }

    // The way between two points along the checked floor cells (every step is an edge the game itself confirmed walkable).
    // Empty when either end is not near the floor or the two lie in separate parts.
    internal List<Position> PathBetween(Position from, Position to)
    {
        var none = new List<Position>();
        if (!Done || Cells.Count < 2)
            return none;
        var a = Cells.Values.OrderBy(c => PositionOf(c).Distance(from)).First();
        var b = Cells.Values.OrderBy(c => PositionOf(c).Distance(to)).First();
        if (PositionOf(a).Distance(from) > Step * 4 || PositionOf(b).Distance(to) > Step * 4)
            return none;
        var parent = new Dictionary<string, string>();
        var q = new Queue<string>();
        parent[a.Key] = null;
        q.Enqueue(a.Key);
        while (q.Count > 0 && !parent.ContainsKey(b.Key))
        {
            string key = q.Dequeue();
            foreach (string next in Edges[key])
                if (!parent.ContainsKey(next))
                {
                    parent[next] = key;
                    q.Enqueue(next);
                }
        }
        if (!parent.ContainsKey(b.Key))
            return none;
        var path = new List<Position>();
        for (string k = b.Key; k != null; k = parent[k])
            path.Add(PositionOf(Cells[k]));
        path.Reverse();
        return path;
    }

    internal bool FullyCovered
    {
        get { return Done && Cells.Count > 1 && Covered.Count == Cells.Count; }
    }

    internal List<Position> RouteToUncovered(Position current, Func<Position, Position, bool> probe, Func<IList<Position[]>, sbyte[]> many = null)
    {
        if (!Done)
            return new List<Position>();
        var near = Cells.Values.OrderBy(c => PositionOf(c).Distance(current)).Take(12).ToList();
        FloorCell start = many != null ? FirstOpen(current, near, double.MaxValue, probe, many) :
            near.FirstOrDefault(c => PositionOf(c).Distance(current) <.1 || probe(current, PositionOf(c)));
        if (start == null)
            throw new InvalidOperationException("Mevcut konumdan doğrulanmış dungeon ağına yol bulunamadı.");
        var q = new Queue<string>();
        var parent = new Dictionary<string, string>();
        parent[start.Key] = null;
        q.Enqueue(start.Key);
        string goal = null;
        while (q.Count > 0)
        {
            string key = q.Dequeue();
            if (!Covered.Contains(key))
            {
                goal = key;
                break;
            }
            foreach (string next in Edges[key])
                if (!parent.ContainsKey(next))
                {
                    parent.Add(next, key);
                    q.Enqueue(next);
                }
        }
        if (goal == null)
            return new List<Position>();
        var path = new List<Position>();
        while (goal != null)
        {
            path.Add(PositionOf(Cells[goal]));
            goal = parent[goal];
        }
        path.Reverse();
        return path;
    }
}

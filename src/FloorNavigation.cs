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
        if (location == null || location.Mode != "Solo Trial")
            return null;
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("GrimFloor." + location.Code +
            ".SoloTrial.json"))
        {
            if (stream == null)
                return null;
            Capture data;
            using (var reader = new StreamReader(stream))
                data = new JavaScriptSerializer { MaxJsonLength = 20000000 }.Deserialize<Capture>(reader.ReadToEnd());
            if (data == null || data.Code != location.Code || data.Mode != location.Mode || data.Region != location.InnerRegion ||
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

    internal void Advance(Func<Position, Position, bool> probe, int budget)
    {
        double minX = ((InnerRegion & 255) - 135) * 192, minY = ((InnerRegion >> 8) - 92) * 192;
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
            if (!probe(PositionOf(working), p))
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

    internal void ObservePosition(Position p, Func<Position, Position, bool> probe = null)
    {
        // Coverage follows graph distance, so a room across a wall is not marked
        // visited merely because it is close in straight-line coordinates.
        var start = Cells.Values.OrderBy(c => PositionOf(c).Distance(p)).Take(12).FirstOrDefault(c => PositionOf(c).Distance(p) <.1 ||
            (PositionOf(c).Distance(p) <= Step * 1.5 && (probe == null || probe(p, PositionOf(c)))));
        if (start == null)
            return;
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

    internal List<Position> RouteToUncovered(Position current, Func<Position, Position, bool> probe)
    {
        if (!Done)
            return new List<Position>();
        var near = Cells.Values.OrderBy(c => PositionOf(c).Distance(current)).Take(12).ToList();
        FloorCell start = near.FirstOrDefault(c => PositionOf(c).Distance(current) <.1 || probe(current, PositionOf(c)));
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

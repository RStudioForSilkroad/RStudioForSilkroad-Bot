using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;

sealed class RoadMesh
{
    struct Cell
    {
        internal float Left, Bottom, Right, Top, Height;
        internal ushort Region;
        internal byte Flags;
        internal int FirstEdge, EdgeCount;

        internal float X
        {
            get { return (Left + Right) / 2; }
        }

        internal float Y
        {
            get { return (Bottom + Top) / 2; }
        }

        internal Position Center()
        {
            return V2Rules.Center(X, Y, Height);
        }
    }

    struct Entry
    {
        internal int Node;
        internal double Cost;
    }

    readonly Cell[] cells;
    readonly int[] edges;
    readonly byte[] surface, water, objects;
    readonly Dictionary<ushort, int[]> regions = new Dictionary<ushort, int[]>();
    // Walkable surfaces of objects (stairs, bridges, gates, building floors): triangles after the terrain cells, found by region.
    readonly Dictionary<ushort, List < int>> extraRegions = new Dictionary<ushort, List < int>>();
    float[] extraTri;
    // Terrain edges whose straight line crosses a wall or building outline: kept (towns stay connected) but expensive.
    readonly HashSet<long> softWalls = new HashSet<long>(new MixedLongComparer());

    // Neighbouring cell numbers differ by little, so the default long hash (high ^ low) collides heavily; mix the bits instead.

    sealed class MixedLongComparer : IEqualityComparer<long>
    {
        public bool Equals(long a, long b)
        {
            return a == b;
        }

        public int GetHashCode(long v)
        {
            unchecked
            {
                ulong x = (ulong)v * 0x9E3779B97F4A7C15UL;
                return (int)(x >> 32);
            }
        }
    }

    int terrainCount;
    readonly Dictionary<int, List < TransitStep>> transfers = new Dictionary<int, List < TransitStep>>();
    readonly Dictionary<int, int> gateCells = new Dictionary<int, int>();

    internal RoadMesh(TransitData network)
    {
        using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("road-mesh.bin.gz"))
            using (var input = new BinaryReader(new GZipStream(resource, CompressionMode.Decompress)))
            {
                int magic = input.ReadInt32();
                if (magic != 0x31304452 && magic != 0x32304452)
                    throw new InvalidDataException("Yol verisi biçimi geçersiz.");
                int regionCount = input.ReadInt32(), cellCount = input.ReadInt32();
                int extraCount = magic == 0x32304452 ? input.ReadInt32() : 0;
                if (regionCount < 1 || regionCount > 10000 || cellCount < 1 || cellCount > 3000000 || extraCount < 0 ||
                    extraCount > 3000000)
                    throw new InvalidDataException("Yol verisi boyutu geçersiz.");
                terrainCount = cellCount;
                cells = new Cell[cellCount + extraCount];
                var links = new List<int>();
                int index = 0;
                for (int regionIndex = 0; regionIndex < regionCount; regionIndex++)
                {
                    ushort region = input.ReadUInt16();
                    int length = input.ReadInt32();
                    if (length < 0 || length > terrainCount - index)
                        throw new InvalidDataException("Yol bölgesi geçersiz.");
                    regions.Add(region, new[] { index, length });
                    float originX = ((region & 255) - 135) * 192;
                    float originY = ((region >> 8) - 92) * 192;
                    for (int j = 0; j < length; j++, index++)
                    {
                        cells[index] = new Cell { Region = region, Left = originX + input.ReadSingle() / 10, Bottom =
                            originY + input.ReadSingle() / 10, Right = originX + input.ReadSingle() / 10, Top = originY +
                            input.ReadSingle() / 10, Height = input.ReadSingle(), Flags = input.ReadByte(), FirstEdge =
                            links.Count, EdgeCount = input.ReadUInt16() };
                        for (int e = 0; e < cells[index].EdgeCount; e++)
                        {
                            int destination = input.ReadInt32();
                            if (destination < 0 || destination >= cells.Length)
                                throw new InvalidDataException("Yol bağlantısı geçersiz.");
                            links.Add(destination);
                        }
                    }
                }
                if (index != terrainCount)
                    throw new InvalidDataException("Yol hücreleri eksik.");
                if (magic == 0x32304452)
                {
                    int softCount = input.ReadInt32();
                    for (int s = 0; s < softCount; s++)
                    {
                        long from = input.ReadInt32();
                        long to = (uint)input.ReadInt32();
                        softWalls.Add((from << 32) | to);
                    }
                }
                extraTri = new float[extraCount * 9];
                for (int x = 0; x < extraCount; x++, index++)
                {
                    ushort region = input.ReadUInt16();
                    float originX = ((region & 255) - 135) * 192, originY = ((region >> 8) - 92) * 192;
                    float cx = 0, cy = 0, ch = 0;
                    for (int k = 0; k < 3; k++)
                    {
                        float vx = originX + input.ReadSingle() / 10, vz = originY + input.ReadSingle() / 10, vy = input.ReadSingle();
                        extraTri[x * 9 + k * 3] = vx;
                        extraTri[x * 9 + k * 3 + 1] = vz;
                        extraTri[x * 9 + k * 3 + 2] = vy;
                        cx += vx / 3;
                        cy += vz / 3;
                        ch += vy / 3;
                    }
                    cells[index] = new Cell { Region = region, Left = cx, Right = cx, Bottom = cy, Top = cy, Height = ch,
                        Flags = 8, FirstEdge = links.Count, EdgeCount = input.ReadUInt16() };
                    for (int e = 0; e < cells[index].EdgeCount; e++)
                    {
                        int destination = input.ReadInt32();
                        if (destination < 0 || destination >= cells.Length)
                            throw new InvalidDataException("Yol bağlantısı geçersiz.");
                        links.Add(destination);
                    }
                    List<int> list;
                    if (!extraRegions.TryGetValue(region, out list))
                        extraRegions.Add(region, list = new List<int>());
                    list.Add(index);
                }
                edges = links.ToArray();
            }
        var layers = LoadSurface(terrainCount);
        surface = Grow(layers == null ? null : layers[0], cells.Length);
        water = Grow(layers == null ? null : layers[1], cells.Length);
        objects = Grow(layers == null ? null : layers[2], cells.Length);

        var gates = network.Gates.Where(ExpeditionRules.Infrastructure).ToDictionary(g => g.Id);
        foreach (var gate in gates.Values)
            gateCells[gate.Id] = Locate(gate.Point(), null, 12);
        foreach (var link in network.Links)
        {
            int from, to;
            if (!gateCells.TryGetValue(link.From, out from) || !gateCells.TryGetValue(link.To, out to) || from < 0 || to < 0 ||
                from == to)
                continue;
            List<TransitStep> options;
            if (!transfers.TryGetValue(from, out options))
                transfers.Add(from, options = new List<TransitStep>());
            options.Add(new TransitStep { From = gates[link.From], To = gates[link.To] });
        }
    }

    static byte[] Grow(byte[] layer, int length)
    {
        if (layer == null || layer.Length == length)
            return layer;
        var grown = new byte[length];
        Array.Copy(layer, grown, layer.Length);
        return grown;
    }

    // Optional per-cell share of dirt-road tiles (0-255) exported from the game ground textures; missing data means no road preference.
    // Layers: [0] dirt-road share, [1] water share (lakes and fords drawn on the game map but listed walkable in the mesh).

    static byte[][]
    LoadSurface(int count)
    {
        try
        {
            using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("road-surface.bin.gz"))
            {
                if (resource == null)
                    return null;
                using (var input = new BinaryReader(new GZipStream(resource, CompressionMode.Decompress)))
                {
                    if (input.ReadInt32() != 0x31305352 || input.ReadInt32() != count)
                        return null;
                    var road = input.ReadBytes(count);
                    var wet = input.ReadBytes(count);
                    var solid = input.ReadBytes(count);
                    return road.Length == count && wet.Length == count ? new[] { road, wet, solid.Length == count ?
                        solid : null } : null;
                }
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Ground height of the walkable cell nearest to a clicked map point; false when no walkable cell is close.

    internal bool TryGround(Position point, out float height)
    {
        int cell = Locate(point, null, 24);
        height = cell < 0 ? 0 : cells[cell].Height;
        return cell >= 0;
    }

    // Closest walkable cell to a point. Terrain cells match by rectangle; object surfaces (stairs, floors) by triangle, and when
    // both lie under the point the one whose height is closer to the point's height wins (gates and clicks have no height: terrain wins).

    int Locate(Position point, HashSet<int> excluded, double limit)
    {
        int best = -1, bestExtra = -1;
        double nearest = double.PositiveInfinity, nearestExtra = double.PositiveInfinity;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                ushort region = (ushort)(point.Region + dx + 256 * dy);
                int[] range;
                if (regions.TryGetValue(region, out range))
                {
                    for (int i = range[0]; i < range[0] + range[1]; i++)
                    {
                        var cell = cells[i];
                        if ((cell.Flags & 1) != 0 || (excluded != null && excluded.Contains(i)))
                            continue;
                        double x = Math.Max(cell.Left - point.X, Math.Max(0, point.X - cell.Right));
                        double y = Math.Max(cell.Bottom - point.Y, Math.Max(0, point.Y - cell.Top));
                        double distance = x * x + y * y;
                        if (distance <= limit * limit && distance < nearest)
                        {
                            best = i;
                            nearest = distance;
                        }
                    }
                }
                List<int> extra;
                if (extraRegions.TryGetValue(region, out extra))
                {
                    foreach (int i in extra)
                    {
                        if (excluded != null && excluded.Contains(i))
                            continue;
                        double distance, height;
                        TriangleDistance(i - terrainCount, point.X, point.Y, out distance, out height);
                        if (distance <= limit * limit && distance < nearestExtra)
                        {
                            bestExtra = i;
                            nearestExtra = distance;
                        }
                    }
                }
            }
        if (bestExtra < 0)
            return best;
        if (best < 0)
            return bestExtra;
        // Gates and clicked points carry no height: always the ground, an object surface only when no ground is near.
        if (point.Height == 0)
            return best;
        if (nearestExtra > 0.25)
            return nearest <= nearestExtra ? best : bestExtra;
        if (nearest > 0.25)
            return nearest <= nearestExtra ? best : bestExtra;
        double extraHeight, ignored;
        TriangleDistance(bestExtra - terrainCount, point.X, point.Y, out ignored, out extraHeight);
        // Terrain under a building floor is not reachable by itself (an entrance inside a palace stands on the floor), so a floor wins ties.
        return Math.Abs(extraHeight - point.Height) <= Math.Abs(cells[best].Height - point.Height) + 12 ? bestExtra :
            best;
    }

    // Squared XY distance from a point to an object triangle (0 inside) and the surface height there.

    void TriangleDistance(int index, double px, double pz, out double distance, out double height)
    {
        int o = index * 9;
        double ax = extraTri[o], az = extraTri[o + 1], ay = extraTri[o + 2], bx = extraTri[o + 3], bz = extraTri[o + 4],
            by = extraTri[o + 5], cx = extraTri[o + 6], cz = extraTri[o + 7], cy = extraTri[o + 8];
        double d = (bz - cz) * (ax - cx) + (cx - bx) * (az - cz);
        if (Math.Abs(d) > 1e-9)
        {
            double u = ((bz - cz) * (px - cx) + (cx - bx) * (pz - cz)) / d, v = ((cz - az) * (px - cx) + (ax - cx) * (pz - cz)) / d,
                w = 1 - u - v;
            if (u >= -1e-6 && v >= -1e-6 && w >= -1e-6)
            {
                distance = 0;
                height = u * ay + v * by + w * cy;
                return;
            }
        }
        double mx = (ax + bx + cx) / 3, mz = (az + bz + cz) / 3;
        distance = Math.Min(Math.Min((px - ax) * (px - ax) + (pz - az) * (pz - az), (px - bx) * (px - bx) + (pz - bz) * (pz - bz)),
            Math.Min((px - cx) * (px - cx) + (pz - cz) * (pz - cz), (px - mx) * (px - mx) + (pz - mz) * (pz - mz)));
        height = (ay + by + cy) / 3;
    }

    static void Push(List<Entry> heap, Entry entry)
    {
        int child = heap.Count;
        heap.Add(entry);
        while (child > 0)
        {
            int parent = (child - 1) / 2;
            if (heap[parent].Cost <= entry.Cost)
                break;
            heap[child] = heap[parent];
            child = parent;
        }
        heap[child] = entry;
    }

    static Entry Pop(List<Entry> heap)
    {
        Entry result = heap[0], tail = heap[heap.Count - 1];
        heap.RemoveAt(heap.Count - 1);
        if (heap.Count == 0)
            return result;
        int parent = 0;
        while (parent * 2 + 1 < heap.Count)
        {
            int child = parent * 2 + 1;
            if (child + 1 < heap.Count && heap[child + 1].Cost < heap[child].Cost)
                child++;
            if (heap[child].Cost >= tail.Cost)
                break;
            heap[parent] = heap[child];
            parent = child;
        }
        heap[parent] = tail;
        return result;
    }

    // Entrances can sit on a small walkable patch that the exported mesh does not link to the road network.
    // Find the closest pair of cells between the reachable network and that patch so the live walker only has to cross the short gap.

    bool FindBridge(double[] distance, int destination, out int from, out int to, out double gap)
    {
        from = to = -1;
        gap = double.PositiveInfinity;
        var island = new HashSet<int> { destination };
        var pending = new Queue<int>();
        pending.Enqueue(destination);
        while (pending.Count > 0 && island.Count < 20000)
        {
            var c = cells[pending.Dequeue()];
            for (int i = c.FirstEdge; i < c.FirstEdge + c.EdgeCount; i++)
                if (island.Add(edges[i]))
                    pending.Enqueue(edges[i]);
        }
        double best = double.PositiveInfinity;
        foreach (int b in island)
        {
            var cb = cells[b];
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int[] range;
                    if (!regions.TryGetValue((ushort)(cb.Region + dx + 256 * dy), out range))
                        continue;
                    for (int a = range[0]; a < range[0] + range[1]; a++)
                    {
                        if (double.IsPositiveInfinity(distance[a]) || (cells[a].Flags & 1) != 0)
                            continue;
                        double ddx = cells[a].X - cb.X, ddy = cells[a].Y - cb.Y, d = ddx * ddx + ddy * ddy;
                        if (d < best)
                        {
                            best = d;
                            from = a;
                            to = b;
                        }
                    }
                }
        }
        if (from < 0)
            return false;
        gap = Math.Sqrt(best);
        return true;
    }

    const float WaterHeight = -30f;
    const double WaterPenalty = 10;
    // A teleport or ferry costs loading time (and gold): about half a minute of walking, so short walks are preferred over a detour via a town.
    internal static double HopCost = 600;
    // Trade goods cannot be teleported or shipped: while set, planning uses roads only and never a teleport, ferry or flyship hop.
    internal static bool WalkOnly;
    internal static double WallPenalty = 40, ObjectPenalty = 1.5;
    // The last stretch to an entrance that the map cannot reach is walked live, which is less reliable: every unit of it counts several times.
    const double LiveGapWeight = 6, ShortcutGap = 240, ShortcutMargin = 600;
    const double RoadDiscount = 0.7, RoadShare = 0.12 * 255, WetPenalty = 80, WetShare = 0.15 * 255;

    internal List<TransitStep> Plan(Position start, Position goal, HashSet<int> excluded = null)
    {
        int origin = Locate(start, null, 12), destination = Locate(goal, excluded, 12);
        if (origin < 0 || destination < 0)
            throw new InvalidOperationException("Başlangıç veya portal yakınında yürünebilir harita hücresi bulunamadı.");
        var distance = new double[cells.Length];
        var parent = new int[cells.Length];
        var transfersUsed = new Dictionary<int, TransitStep>();
        var queue = new List<Entry>();
        int bridgeFrom = -1, bridgeTo = -1;
        double bridgeCost = 0;
        int nearestNode = origin, shortNode = -1;
        double nearestGap = start.Distance(goal), shortScore = double.PositiveInfinity, shortGap = 0;
        Action<int, int, double, TransitStep> visit = (from, to, cost, hop) =>
        {
            if (excluded != null && excluded.Contains(to))
                return;
            double candidate = distance[from] + cost;
            if (candidate >= distance[to])
                return;
            distance[to] = candidate;
            parent[to] = from;
            if (hop == null)
                transfersUsed.Remove(to);
            else
                transfersUsed[to] = hop;
            Push(queue, new Entry { Node = to, Cost = candidate });
        };
        for (int attempt = 0; attempt < 2; attempt++)
        {
            for (int i = 0; i < distance.Length; i++)
            {
                distance[i] = double.PositiveInfinity;
                parent[i] = -1;
            }
            transfersUsed.Clear();
            queue.Clear();
            nearestNode = origin;
            nearestGap = start.Distance(goal);
            shortNode = -1;
            shortScore = double.PositiveInfinity;
            shortGap = 0;
            distance[origin] = 0;
            Push(queue, new Entry { Node = origin });
            while (queue.Count > 0)
            {
                var current = Pop(queue);
                int node = current.Node;
                if (current.Cost != distance[node])
                    continue;
                if (node == destination)
                    break;
                var cell = cells[node];
                double gapX = cell.X - goal.X, gapY = cell.Y - goal.Y;
                double gap = Math.Sqrt(gapX * gapX + gapY * gapY);
                if (gap < nearestGap)
                {
                    nearestGap = gap;
                    nearestNode = node;
                }
                if (gap <= ShortcutGap && current.Cost + gap * LiveGapWeight < shortScore)
                {
                    shortScore = current.Cost + gap * LiveGapWeight;
                    shortNode = node;
                    shortGap = gap;
                }
                for (int i = cell.FirstEdge; i < cell.FirstEdge + cell.EdgeCount; i++)
                {
                    int next = edges[i];
                    var target = cells[next];
                    double dx = cell.X - target.X, dy = cell.Y - target.Y;
                    double cost = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
                    // Lake and swamp beds are listed as walkable but the client will not walk into deep water; prefer shores and roads.
                    if ((cell.Flags & 16) != 0 && softWalls.Contains(((long)node << 32) | (uint)next))
                        cost *= WallPenalty;
                    // Water comes from the colours of the game map. A low height alone is not water (the Taklamakan basin lies below zero).
                    if (water != null)
                    {
                        if (water[next] > 0)
                            cost *= 1 + WetPenalty * Math.Min(1, water[next] / WetShare);
                    }
                    else if (target.Height < WaterHeight)
                        cost *= WaterPenalty;
                    // Walls and buildings are separate objects that the terrain cells do not know about; cells they overlap are costly to cross.
                    if (objects != null && objects[next] > 0 && (surface == null || surface[next] == 0))
                        cost *= ObjectPenalty;
                    // Dirt roads are the maintained, collision-free ground: prefer them over cutting across open terrain.
                    else if (surface != null && surface[next] > 0)
                        cost *= 1 - RoadDiscount * Math.Min(1, surface[next] / RoadShare);
                    visit(node, next, cost, null);
                }
                if (node == bridgeFrom)
                    visit(node, bridgeTo, bridgeCost, null);
                List<TransitStep> options;
                if (!WalkOnly && transfers.TryGetValue(node, out options))
                    foreach (var hop in options)
                    {
                        // Never route through a teleport into Nanzhang unless the destination itself is there.
                        if (hop.To.Id == 252 && ExpeditionRules.Area(goal) != "Nanzhang")
                            continue;
                        visit(node, gateCells[hop.To.Id], HopCost, hop);
                    }
            }
            if (!double.IsPositiveInfinity(distance[destination]) || attempt == 1)
                break;
            // Permit a bounded live approach when an entrance is on a separate local mesh.
            var near = nearestNode == origin ? start : cells[nearestNode].Center();
            double limit = ExpeditionRules.Area(near) == ExpeditionRules.Area(goal) ? 192 : 64;
            if (nearestGap <= limit)
                break;
            double gapToIsland;
            if (!FindBridge(distance, destination, out bridgeFrom, out bridgeTo, out gapToIsland) || gapToIsland > limit)
                break;
            bridgeCost = Math.Max(1, gapToIsland);
        }
        bool needsLiveApproach = double.IsPositiveInfinity(distance[destination]);
        // Stairs, ramps and building interiors are not in the exported mesh, so a connected mesh path can be a huge detour
        // (around a lake) while the road ends a short walk from the entrance. Prefer that short live approach when it saves a lot.
        // When the entrance is not linked at all, the cell closest to it may sit on a long detour; compare the real cost of each live approach.
        double rival = needsLiveApproach ? distance[nearestNode] + nearestGap * LiveGapWeight : distance[destination];
        bool shortcut = needsLiveApproach && shortNode >= 0 && shortScore + ShortcutMargin < rival;
        if (shortcut)
        {
            needsLiveApproach = true;
            nearestNode = shortNode;
            nearestGap = shortGap;
        }
        if (needsLiveApproach)
        {
            // The live walker validates the final short leg around portal objects.
            var approach = nearestNode == origin ? start : cells[nearestNode].Center();
            double approachLimit = ExpeditionRules.Area(approach) == ExpeditionRules.Area(goal) ? 192 : 64;
            if (!shortcut && nearestGap > approachLimit)
                throw new InvalidOperationException("Harita verisinde portala bağlı yol bulunamadı.");
            destination = nearestNode;
        }
        var path = new List<int>();
        for (int node = destination; node != origin; node = parent[node])
            path.Add(node);
        path.Reverse();
        var result = new List<TransitStep>();
        Position previous = start;
        foreach (int node in path)
        {
            TransitStep hop;
            if (transfersUsed.TryGetValue(node, out hop))
            {
                result.Add(hop);
                previous = hop.To.Point();
                continue;
            }
            Position point = cells[node].Center();
            bool bridgeStep = node == bridgeTo || node == bridgeFrom;
            double minStep = (cells[node].Flags & 8) != 0 ? 12 : 32;
            if (!bridgeStep && previous.Distance(point) < minStep && Math.Abs(previous.Height - point.Height) < 8 && !(needsLiveApproach &&
                node == destination))
                continue;
            result.Add(new TransitStep { CellId = node, To = new TransitGate { Code = "Harita yolu", X = point.X, Y =
                point.Y, Height = point.Height } });
            previous = point;
        }
        // The mesh may end a short way from the portal (stairs, platforms): finish the route on the entrance itself so it ends within a few units.
        if (previous.Distance(goal) > 4 && previous.Distance(goal) < 250)
            result.Add(new TransitStep { CellId = -1, To = new TransitGate { Code = "Giriş", X = goal.X, Y = goal.Y,
                Height = goal.Height } });
        return result;
    }
}

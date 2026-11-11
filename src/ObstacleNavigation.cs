using System;
using System.Collections.Generic;
using System.Linq;

// Incremental A*: only edges accepted by the client's navigation query are used.
// Bounds and query limits deliberately fail closed when terrain is not available.

sealed class ObstaclePath
{
    sealed class Node
    {
        internal int X, Y;
        internal double G;
        internal Node Parent;

        internal string Key
        {
            get { return X + "," + Y; }
        }
    }

    readonly Dictionary<string, Node> open = new Dictionary<string, Node>();
    readonly HashSet<string> closed = new HashSet<string>();
    readonly HashSet<string> blocked = new HashSet<string>();
    readonly List<Position> trouble = new List<Position>();
    readonly Dictionary<string, bool> tested = new Dictionary<string, bool>();
    Node working, closest;
    int direction;
    Position origin, goal;
    double minX, maxX, minY, maxY, tolerance;
    Func<Position, bool> allowed;
    internal List<Position> Route;
    internal bool Failed;
    internal int Queries;
    internal double ClosestDistance = double.MaxValue;
    internal int Step = 8;

    static string PointKey(Position p)
    {
        return Math.Round(p.X * 2) + "," + Math.Round(p.Y * 2);
    }

    internal static string EdgeKey(Position a, Position b)
    {
        string x = PointKey(a), y = PointKey(b);
        return string.CompareOrdinal(x, y) < 0 ? x + ":" + y : y + ":" + x;
    }

    internal void Block(Position a, Position b)
    {
        blocked.Add(EdgeKey(a, b));
        trouble.Add(V2Rules.Center((a.X + b.X) / 2, (a.Y + b.Y) / 2, a.Height));
    }

    internal void Clear()
    {
        open.Clear();
        closed.Clear();
        tested.Clear();
        blocked.Clear();
        trouble.Clear();
        Route = null;
        Failed = false;
        working = null;
    }

    internal void Begin(Position start, Position end, Func<Position, bool> constraint, double arrivalTolerance = 3)
    {
        open.Clear();
        closed.Clear();
        tested.Clear();
        Route = null;
        Failed = false;
        Queries = 0;
        ClosestDistance = double.MaxValue;
        working = closest = null;
        origin = start.Copy();
        goal = end.Copy();
        allowed = constraint;
        tolerance = arrivalTolerance;
        climb = end.Height - start.Height;
        slopeX = end.X - start.X;
        slopeY = end.Y - start.Y;
        slopeLength = slopeX * slopeX + slopeY * slopeY;
        slope = Math.Abs(climb) > 8 && Math.Abs(climb) < 600 && end.Height != 0 && slopeLength > 1 && slopeLength <= 400 * 400;
        minX = Math.Min(start.X, end.X) - 384;
        maxX = Math.Max(start.X, end.X) + 384;
        minY = Math.Min(start.Y, end.Y) - 384;
        maxY = Math.Max(start.Y, end.Y) + 384;
        var first = new Node();
        open.Add(first.Key, first);
    }

    // A short final approach that climbs (stairs, ramps) must be queried at the slope's height, not at the start's: the client rejects points at the wrong height.
    bool slope;
    double climb, slopeX, slopeY, slopeLength;

    Position At(Node n)
    {
        double x = origin.X + n.X * Step, y = origin.Y + n.Y * Step, z = origin.Height;
        if (slope)
        {
            double t = Math.Max(0, Math.Min(1, ((x - origin.X) * slopeX + (y - origin.Y) * slopeY) / slopeLength));
            z = origin.Height + climb * t;
        }
        return V2Rules.Center(x, y, (float)z);
    }

    internal bool Pass(Position a, Position b, Func<Position, Position, bool> probe)
    {
        string key = EdgeKey(a, b);
        if (blocked.Contains(key))
            return false;
        foreach (Position p in trouble)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, length = dx * dx + dy * dy;
            double t = length <.001 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / length));
            if (Math.Sqrt(Math.Pow(a.X + t * dx - p.X, 2) + Math.Pow(a.Y + t * dy - p.Y, 2)) < 2)
                return false;
        }
        bool result;
        if (tested.TryGetValue(key, out result))
            return result;
        Queries++;
        result = probe(a, b);
        tested[key] = result;
        return result;
    }

    internal void Advance(Func<Position, Position, bool> probe, int budget)
    {
        int used = 0, iterations = 0;
        while (Route == null && !Failed && used < budget && iterations++ < 128)
        {
            if (Queries >= 1500 || closed.Count >= 800)
            {
                Failed = true;
                return;
            }
            if (working == null)
            {
                if (open.Count == 0)
                {
                    Failed = true;
                    return;
                }
                working = open.Values.OrderBy(n => n.G + At(n).Distance(goal)).ThenBy(n => At(n).Distance(goal)).First();
                open.Remove(working.Key);
                closed.Add(working.Key);
                direction = -1;
            }
            Position here = At(working);
            if (here.Distance(goal) < ClosestDistance)
            {
                ClosestDistance = here.Distance(goal);
                closest = working;
            }
            if (direction == -1)
            {
                direction = 0;
                if (here.Distance(goal) <= (working.Parent == null ? (slope ? 300 : 48) : 12) && (allowed == null ||
                    allowed(goal)))
                {
                    bool near = here.Distance(goal) <= tolerance;
                    if (!near)
                        used++;
                    if (near || Pass(here, goal, probe))
                    {
                        var path = new List<Position>();
                        if (!near)
                            path.Add(goal.Copy());
                        for (Node n = working; n != null; n = n.Parent)
                            path.Add(At(n));
                        path.Reverse();
                        Route = path;
                        return;
                    }
                }
            }
            if (direction >= 8)
            {
                working = null;
                continue;
            }
            int d = direction++;
            int nx = d == 0 || d == 4 || d == 6 ? 1 : d == 1 || d == 5 || d == 7 ? -1 : 0, ny = d == 2 || d == 4 || d == 5 ?
                1 : d == 3 || d == 6 || d == 7 ? -1 : 0;
            var next = new Node { X = working.X + nx, Y = working.Y + ny, G = working.G + Step * (d < 4 ? 1 : Math.Sqrt(2)),
                Parent = working };
            if (closed.Contains(next.Key))
                continue;
            Node old;
            if (open.TryGetValue(next.Key, out old) && old.G <= next.G)
                continue;
            Position dest = At(next);
            if (dest.X < minX || dest.X > maxX || dest.Y < minY || dest.Y > maxY || (allowed != null && !allowed(dest)))
                continue;
            used++;
            if (Pass(here, dest, probe))
                open[next.Key] = next;
        }
    }

    internal bool UseVerifiedLeg()
    {
        // Long routes can extend beyond the client's loaded terrain. Only walk a
        // connected, already verified part, then query again after terrain loads.
        if ((!Failed && Queries < 256) || closest == null || origin.Distance(goal) <= 192 || origin.Distance(goal) - ClosestDistance < 96)
            return false;
        var path = new List<Position>();
        for (Node n = closest; n != null; n = n.Parent)
            path.Add(At(n));
        path.Reverse();
        if (path.Count < 2)
            return false;
        Route = path;
        Failed = false;
        return true;
    }
}

enum RouteResult
{
    Working,
    Arrived,
    Failed
}

sealed class ObstacleWalker
{
    readonly ObstaclePath planner = new ObstaclePath();
    Position goal, lastGood, segmentStart, command, rollback;
    int index, replans, rejections;
    long nextSend, progressAt;
    double best, tolerance = 3;
    bool searching, backtracking, segmentSent;
    internal int GridStep = 8;
    internal string Message = "";

    internal bool Active
    {
        get { return goal != null; }
    }

    internal bool Backtracking
    {
        get { return backtracking; }
    }

    internal void Reset()
    {
        planner.Clear();
        goal = lastGood = segmentStart = command = rollback = null;
        index = replans = rejections = 0;
        searching = backtracking = false;
        nextSend = 0;
    }

    internal void Pause(long elapsed)
    {
        progressAt += Math.Max(0, elapsed);
        nextSend += Math.Max(0, elapsed);
    }

    internal void Recover(Position current, Position destination, Position previous, long now, Func<Position, bool> allowed)
    {
        Reset();
        goal = destination.Copy();
        lastGood = previous == null ? current.Copy() : previous.Copy();
        double distance = current.Distance(destination), fraction = distance <.01 ? 0 : Math.Min(8, distance) / distance;
        planner.Block(current, V2Rules.Center(current.X + (destination.X - current.X) * fraction, current.Y + (destination.Y - current.Y) * fraction,
            current.Height));
        replans = 1;
        if (previous != null && current.Distance(previous) > 1 && current.Distance(previous) <= 32)
        {
            rollback = previous.Copy();
            backtracking = true;
            progressAt = now;
            nextSend = now;
        }
        else
            Search(current, allowed);
    }

    void Search(Position current, Func<Position, bool> allowed)
    {
        planner.Step = GridStep;
        planner.Begin(current, goal, allowed, tolerance);
        searching = true;
        index = 0;
        command = null;
    }

    void BeginSegment(Position current, Position dest, long now)
    {
        segmentStart = current.Copy();
        command = dest.Copy();
        best = current.Distance(dest);
        progressAt = now;
        segmentSent = false;
        nextSend = now;
    }

    internal RouteResult Tick(Position current, Position destination, long now, Func<Position, Position, bool> probe,
        Func<Position, bool> move, Func<Position, bool> allowed, double arrivalTolerance = 3)
    {
        tolerance = arrivalTolerance;
        if (goal == null || goal.Distance(destination) > 8)
        {
            Reset();
            goal = destination.Copy();
            lastGood = current.Copy();
            Search(current, allowed);
        }
        if (current.Distance(goal) <= tolerance)
        {
            Reset();
            Message = "Hedef konuma ulaşıldı.";
            return RouteResult.Arrived;
        }
        if (backtracking)
        {
            Message = "Engel: son doğrulanmış konuma geri dönüyor; farklı rota hazırlanacak.";
            if (current.Distance(rollback) <= 2)
            {
                backtracking = false;
                lastGood = current.Copy();
                Search(current, allowed);
            }
            else
            {
                if (now - progressAt >= 6000)
                {
                    Message = "Geri dönüş yolu ilerlemiyor; güvenli olarak durdu.";
                    return RouteResult.Failed;
                }
                if (now >= nextSend)
                {
                    nextSend = now + 1000;
                    if (!probe(current, rollback) || !move(rollback))
                    {
                        Message = "Önceki konuma dönüş doğrulanamadı.";
                        return RouteResult.Failed;
                    }
                }
                return RouteResult.Working;
            }
        }
        if (searching)
        {
            planner.Advance(probe, 64);
            Message = "Engelsiz rota hesaplanıyor · " + planner.Queries + " geçiş kontrolü";
            if (planner.Route == null)
                planner.UseVerifiedLeg();
            if (planner.Failed)
            {
                Message = "Yüklenmiş zeminde sınırlar içinde yürünebilir rota bulunamadı.";
                return RouteResult.Failed;
            }
            if (planner.Route == null)
                return RouteResult.Working;
            searching = false;
            index = 1;
        }
        while (index < planner.Route.Count && current.Distance(planner.Route[index]) <= 2)
        {
            lastGood = current.Copy();
            index++;
            command = null;
        }
        if (index >= planner.Route.Count)
        {
            if (current.Distance(goal) <= tolerance)
            {
                Reset();
                return RouteResult.Arrived;
            }
            Search(current, allowed);
            return RouteResult.Working;
        }
        if (command == null)
        {
            // Look ahead only along a collision-checked straight segment. This avoids
            // pausing at every eight-unit search cell while retaining corner checks.
            for (int farther = Math.Min(index + (GridStep == 4 ? 7 : 3), planner.Route.Count - 1); farther > index; farther--)
            {
                Position p = planner.Route[farther];
                if (current.Distance(p) <= 28 && (allowed == null || allowed(p)) && planner.Pass(current, p, probe))
                {
                    index = farther;
                    break;
                }
            }
        }
        Position next = planner.Route[index];
        if (command == null)
            BeginSegment(current, next, now);
        double distance = current.Distance(next);
        if (distance < best -.5)
        {
            best = distance;
            progressAt = now;
        }
        if (now - progressAt >= 4000)
        {
            // Keep the failed edge excluded even when the native query accepted it.
            planner.Block(segmentStart, next);
            if (index > 0)
                planner.Block(planner.Route[index - 1], next);
            if (++replans > 6)
            {
                Message = "Altı farklı rota denemesinde ilerleme yok; durdu.";
                return RouteResult.Failed;
            }
            Position previous = lastGood ?? segmentStart;
            command = null;
            if (current.Distance(previous) > 1)
            {
                rollback = previous.Copy();
                backtracking = true;
                progressAt = now;
                nextSend = now;
            }
            else
                Search(current, allowed);
            Message = "Geçiş engellendi; önceki konum ve alternatif rota deneniyor.";
            return RouteResult.Working;
        }
        Message = "Doğrulanmış rotada yürüyor · " + (index + 1) + " / " + planner.Route.Count;
        // A successful move keeps running in the client. Reissuing it while the
        // character is progressing can reset its animation/path on some clients.
        if (segmentSent && now - progressAt < 900)
            return RouteResult.Working;
        if (now < nextSend)
            return RouteResult.Working;
        nextSend = now + 400;
        if (allowed != null && !allowed(next))
        {
            Message = "Rota av bölgesi dışına çıkıyor; durdu.";
            return RouteResult.Failed;
        }
        if (!probe(current, next))
        {
            planner.Block(segmentStart, next);
            if (++replans > 6)
            {
                Message = "Rota tekrar tekrar kapandı.";
                return RouteResult.Failed;
            }
            Search(current, allowed);
            return RouteResult.Working;
        }
        if (move(next))
            segmentSent = true;
        else if (++rejections >= 3)
        {
            Message = "Hareket isteği kabul edilmedi.";
            return RouteResult.Failed;
        }
        return RouteResult.Working;
    }
}

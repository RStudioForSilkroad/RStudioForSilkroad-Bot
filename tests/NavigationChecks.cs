using System;
using System.Linq;

static class NavigationChecks
{
    static Position P(double x, double y)
    {
        return V2Rules.Center(x, y, 0);
    }

    static bool Wall(Position a, Position b)
    {
        for (int i = 0; i <= 100; i++)
        {
            double t = i / 100.0, x = a.X + (b.X - a.X) * t, y = a.Y + (b.Y - a.Y) * t;
            if (x >= 24 && x <= 40 && y >= -16 && y <= 16)
                return false;
        }
        return true;
    }

    internal static void Run(Action<bool, string> check)
    {
        var narrow = new ObstacleWalker { GridStep = 4 };
        Position fine = P(0, 0);
        RouteResult fineResult = RouteResult.Working;
        for (long now = 0; now < 120000 && fineResult == RouteResult.Working; now += 250)
            fineResult = narrow.Tick(fine, P(40, 0), now, (a, b) => Math.Abs(a.Y) <= 2 && Math.Abs(b.Y) <= 2 && a.Distance(b) <= 28,
                p =>
            {
                fine = p.Copy();
                return true;
            }, null, 5);
        check(fineResult == RouteResult.Arrived && fine.Distance(P(40, 0)) <= 5,
            "Fine travel grid follows verified narrow passage and 5-unit arrival");
        var path = new ObstaclePath();
        path.Begin(P(0, 0), P(64, 0), null);
        for (int i = 0; i < 4000 && path.Route == null && !path.Failed; i++)
            path.Advance(Wall, 8);
        check(path.Route != null && path.Route.Any(p => Math.Abs(p.Y) >= 24),
            "Navigation detours around a wall instead of direct XY movement");
        check(path.Route.Zip(path.Route.Skip(1), (a, b) => Wall(a, b)).All(x => x),
            "Every planned edge passes independent collision geometry");
        path = new ObstaclePath();
        path.Begin(P(0, 0), P(64, 0), p => Math.Abs(p.Y) <= 8);
        for (int i = 0; i < 4000 && path.Route == null && !path.Failed; i++)
            path.Advance(Wall, 8);
        check(path.Failed && path.Route == null, "Range constraint rejects an otherwise possible out-of-area detour");
        path = new ObstaclePath();
        path.Begin(P(0, 0), P(64, 0), null, 12);
        Func<Position, Position, bool> portal = (a, b) =>
        {
            for (int i = 0; i <= 100; i++)
            {
                double t = i / 100.0;
                if (P(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t).Distance(P(64, 0)) < 10)
                    return false;
            }
            return true;
        };
        for (int i = 0; i < 4000 && path.Route == null && !path.Failed; i++)
            path.Advance(portal, 32);
        check(path.Route != null && path.Route.Last().Distance(P(64, 0)) <= 12 && path.Route.Last().Distance(P(64, 0)) >= 10,
            "Portal approach stops beside occupied gate instead of demanding its exact center");
        path = new ObstaclePath();
        path.Begin(P(0, 0), P(64, 0), null);
        for (int i = 0; i < 4 && !path.Failed; i++)
            path.Advance((a, b) => false, 8);
        check(path.Failed, "Unreachable terrain fails closed");
        var walker = new ObstacleWalker();
        Position current = P(0, 0);
        int calls = 0;
        double longest = 0;
        RouteResult result = RouteResult.Working;
        for (long now = 0; now < 120000 && result == RouteResult.Working; now += 250)
        {
            result = walker.Tick(current, P(64, 0), now, Wall, p =>
            {
                check(Wall(current, p), "Smoothing never cuts through wall");
                longest = Math.Max(longest, current.Distance(p));
                current = p.Copy();
                calls++;
                return true;
            }, null);
        }
        check(result == RouteResult.Arrived && current.Distance(P(64, 0)) <= 3,
            "Shared walker reaches destination through verified detour");
        check(longest > 8 && longest <= 28.01, "Lookahead combines grid cells without blind long movement");
        walker = new ObstacleWalker();
        current = P(4, 0);
        walker.Recover(current, P(64, 0), P(0, 0), 0, null);
        bool returned = false;
        result = walker.Tick(current, P(64, 0), 0, (a, b) => true, p =>
        {
            returned = p.Distance(P(0, 0)) <.1;
            current = p.Copy();
            return true;
        }, null);
        check(returned && walker.Backtracking, "Blocked attack approach first returns to previous position");
        for (long now = 250; now < 60000 && result == RouteResult.Working; now += 250)
            result = walker.Tick(current, P(64, 0), now, (a, b) => true, p =>
            {
                current = p.Copy();
                return true;
            }, null);
        check(result == RouteResult.Arrived, "Failed edge stays excluded while alternative route succeeds");
        walker = new ObstacleWalker();
        current = P(0, 0);
        bool stalled = false;
        for (long now = 0; now < 180000; now += 250)
        {
            result = walker.Tick(current, P(64, 0), now, (a, b) => true, p => true, null);
            if (walker.Message.Contains("Geçiş engellendi"))
                stalled = true;
            if (result == RouteResult.Failed)
                break;
        }
        check(stalled && result == RouteResult.Failed,
            "Accepted commands without actual progress trigger replans then bounded stop");
        walker = new ObstacleWalker();
        current = P(0, 0);
        walker.Tick(current, P(64, 0), 0, (a, b) => true, p => true, null);
        walker.Pause(60000);
        result = walker.Tick(current, P(64, 0), 60250, (a, b) => true, p => true, null);
        check(result != RouteResult.Failed && !walker.Backtracking,
            "Unfocused time does not trigger a false obstacle recovery");
        walker = new ObstacleWalker();
        current = P(0, 0);
        Position far = P(600, 0);
        int legs = 0;
        result = RouteResult.Working;
        Func<Position, Position, bool> loaded = (a, b) => a.Distance(current) <= 180 && b.Distance(current) <= 180;
        for (long now = 0; now < 300000 && result == RouteResult.Working; now += 250)
            result = walker.Tick(current, far, now, loaded, p =>
            {
                if (!loaded(current, p))
                    throw new Exception("Unloaded terrain move");
                current = p.Copy();
                legs++;
                return true;
            }, null);
        check(result == RouteResult.Arrived && current.Distance(far) <= 3 && legs > 5,
            "Long route advances through verified loaded legs and replans after each leg");
        path = new ObstaclePath();
        path.Begin(P(0, 0), P(600, 0), null);
        for (int i = 0; i < 10 && !path.Failed; i++)
            path.Advance((a, b) => false, 32);
        check(!path.UseVerifiedLeg(), "A partial route cannot invent progress when no connected terrain exists");
        // Jupiter Temple (D10): from the Garden of Gods Reverse point the stairs to the gate are on the map; closing one cell of them
        // (the forecourt, cell 308059) leaves no error but a dead-end live approach, which the caller has to be told about.
        var temple = V3Data.Dungeons.First(x => x.Code == "D10" && x.Mode == "Solo Trial").Entrance();
        var garden = ReverseRules.Find(3).Point();
        var mapped = ExpeditionRules.Plan(garden, temple);
        check(!RoadMesh.LastLiveApproach && mapped.Last().CellId >= 0 && mapped.Last().To.Point().Distance(temple) < 12,
            "D10 gate is reached over mapped stairs from the Garden of Gods Reverse point");
        var closedForecourt = ExpeditionRules.Plan(garden, temple, new System.Collections.Generic.HashSet<int> { 308059 });
        check(RoadMesh.LastLiveApproach && closedForecourt.Last().CellId < 0 && closedForecourt.Count < mapped.Count,
            "A closed stair cell turns the D10 route into a flagged live approach");
        ExpeditionRules.Plan(garden, temple);
        check(!RoadMesh.LastLiveApproach, "The live-approach flag is reset by the next plan");
    }
}

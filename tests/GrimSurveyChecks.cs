using System;
using System.Collections.Generic;
using System.Linq;

static class GrimSurveyChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var trials = V3Data.Dungeons.Where(x => x.Mode == "Solo Trial").ToList();
        check(trials.Count == 18, "All eighteen Solo Trial catalog entries exist");
        foreach (var trial in trials)
        {
            var mapped = FloorNavigation.LoadCaptured(trial);
            check(mapped != null && mapped.Done && mapped.Step == 4 && mapped.Cells.Count >= 800 && mapped.Source.Contains(trial.Code),
                trial.Code + " Solo Trial loads its own embedded capture");
            Position pos = mapped.Origin.Copy();
            mapped.ObservePosition(pos);
            int count = 0;
            bool edgesValid = true;
            while (!mapped.FullyCovered && count++ < 250)
            {
                var plan = mapped.RouteToUncovered(pos, (a, b) => false);
                if (plan.Count < 2)
                    break;
                for (int i = 1; i < plan.Count; i++)
                    if (plan[i].Distance(plan[i - 1]) > 4.01)
                        edgesValid = false;
                foreach (Position p in plan)
                {
                    pos = p.Copy();
                    mapped.ObservePosition(pos);
                }
            }
            check(edgesValid && mapped.FullyCovered && count > 1, trial.Code +
                " survey reaches every captured area through connected corridors");
        }
        check(V3Data.Dungeons.Where(x => x.Mode != "Solo Trial").All(x => FloorNavigation.LoadCaptured(x) == null),
            "Other modes do not silently inherit Trial-only captures");
        var d = V3Data.Dungeons.First(x => x.Code == "D13" && x.Mode == "Solo Trial");
        var floor = FloorNavigation.LoadCaptured(d);
        check(floor != null && floor.Done && floor.Step == 4 && floor.Cells.Count == 1082,
            "D13 Solo Trial captured map is embedded and validated");
        check(FloorNavigation.LoadCaptured(V3Data.Dungeons.First(x => x.Code == "D13" && x.Mode == "Novice")) == null,
            "Captured Solo Trial map is not silently reused for another mode");
        Position current = floor.Origin.Copy();
        floor.ObservePosition(current);
        check(!floor.FullyCovered, "An empty entrance room does not complete the whole dungeon");
        int segments = 0;
        bool connected = true;
        while (!floor.FullyCovered && segments < 200)
        {
            var route = floor.RouteToUncovered(current, (a, b) => false);
            if (route.Count < 2)
                break;
            for (int i = 1; i < route.Count; i++)
                if (route[i].Distance(route[i - 1]) > floor.Step +.01)
                    connected = false;
            foreach (var p in route)
            {
                current = p.Copy();
                floor.ObservePosition(current);
            }
            segments++;
        }
        check(connected && segments > 1 && floor.FullyCovered,
            "Survey crosses connected corridors and covers all D13 rooms before waiting");
        check(floor.RouteToUncovered(current, (a, b) => false).Count == 0,
            "No further wandering route after all mapped areas are checked");
        var run = new GrimRun();
        run.Arm(d, current);
        run.Floor = floor;
        var snapshot = new Snapshot { Player = current, PlayerAlive = true };
        check(!run.CanFinish(snapshot, 0) && !run.CanFinish(snapshot, 14999) && run.CanFinish(snapshot, 15000),
            "All checked rooms still require a stable fifteen-second no-mob observation");
        snapshot.Monsters.Add(new Monster { Uid = 1, Alive = true, Position = current.Copy(), Name = "Late wave" });
        check(!run.CanFinish(snapshot, 16000), "A late mob wave prevents local clear even after the survey");
        var banned = new Dictionary<uint, long>();
        check(run.Choose(snapshot, banned, 16000, 0) != null, "New mobs resume combat from the final waiting position");
        banned[1] = 99999;
        check(run.Choose(snapshot, banned, 16000, 0) == null && !run.CanFinish(snapshot, 17000),
            "A temporarily blocked living mob is never counted as a completed clear");
        check(!run.Complete, "Local surveyed-area clear does not forge a server completion flag");
        // Two nearby arms of a U corridor: straight distance is small, but their
        // connecting route is long. The far arm must remain unvisited.
        var small = new FloorNavigation(V2Rules.Center(0, 0, 0), 23687);
        small.Cells.Clear();
        small.Edges.Clear();
        small.Done = true;
        var cells = new List<FloorCell>();
        for (int y = 0; y <= 8; y++)
            cells.Add(new FloorCell { X = 0, Y = y });
        cells.Add(new FloorCell { X = 1, Y = 8 });
        for (int y = 8; y >= 0; y--)
            cells.Add(new FloorCell { X = 2, Y = y });
        foreach (var c in cells)
        {
            small.Cells.Add(c.Key, c);
            small.Edges.Add(c.Key, new HashSet<string>());
        }
        for (int i = 1; i < cells.Count; i++)
        {
            small.Edges[cells[i - 1].Key].Add(cells[i].Key);
            small.Edges[cells[i].Key].Add(cells[i - 1].Key);
        }
        small.ObservePosition(small.Origin);
        check(!small.Covered.Contains("2,0"),
            "Coverage follows corridor distance instead of marking rooms through walls");
    }
}

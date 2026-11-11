using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

static class PhBotRoutes
{
    sealed class RouteFile
    {
        public List<RecordedRoute> Routes;
    }

    sealed class RecordedRoute
    {
        public string Code;
        public double StartX, StartY, GoalX, GoalY;
        public List<string> Steps;
    }

    static string FileName
    {
        get
        {
            return Path.Combine(AppPaths.DataRoot, "phbot-routes.json");
        }
    }

    internal static List<TransitStep> TryPlan(Position start, DungeonLocation dungeon)
    {
        var file = new FileInfo(FileName);
        if (!file.Exists || file.Length > 4000000)
            return null;
        var data = new JavaScriptSerializer { MaxJsonLength = 4000000 }.Deserialize<RouteFile>(File.ReadAllText(file.FullName));
        if (data == null || data.Routes == null)
            return null;
        List<TransitStep> remaining = null;
        double nearest = 20;
        foreach (var route in data.Routes.Where(r => r != null && r.Code == dungeon.Code && r.Steps != null && r.Steps.Count > 0 &&
            Math.Abs(r.GoalX - dungeon.X) < 1 && Math.Abs(r.GoalY - dungeon.Y) < 1))
        {
            var steps = Parse(route.Steps);
            if (steps == null || steps.Count == 0 || steps.Last().To.Point().Distance(dungeon.Entrance()) > 64)
                continue;
            double dx = start.X - route.StartX, dy = start.Y - route.StartY;
            if (dx * dx + dy * dy <= 15 * 15)
                return steps;

            // A restart along a recorded route may use its remaining suffix.
            for (int i = 0; i < steps.Count; i++)
            {
                var point = steps[i].From == null ? steps[i].To.Point() : steps[i].From.Point();
                double distance = start.Distance(point);
                if (point.Region != start.Region || distance >= nearest)
                    continue;
                nearest = distance;
                remaining = steps.Skip(i).ToList();
            }
        }
        return remaining;
    }

    internal static List<TransitStep> Parse(IEnumerable<string> commands)
    {
        var steps = new List<TransitStep>();
        foreach (string command in commands)
        {
            if (string.IsNullOrWhiteSpace(command))
                return null;
            var parts = command.Split(',');
            if (parts[0] == "wait" && parts.Length == 2)
                continue;
            if (parts[0] == "walk" && (parts.Length == 4 || parts.Length == 6))
            {
                var values = new double[parts.Length - 1];
                for (int i = 0; i < values.Length; i++)
                {
                    if (!double.TryParse(parts[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) ||
                        double.IsInfinity(values[i]) || double.IsNaN(values[i]))
                        return null;
                }
                double x = values[0], y = values[1];
                if (parts.Length == 6)
                {
                    x = (values[0] - 135) * 192 + values[2] / 10;
                    y = (values[1] - 92) * 192 + values[3] / 10;
                }
                if (x < -25920 || x >= 23232 || y < -17664 || y >= 6912)
                    return null;
                steps.Add(new TransitStep { To = new TransitGate { Code = "phBot yolu", X = x, Y = y, Height = values[values.Length - 1] } });
                continue;
            }
            if (parts[0] == "teleport" && parts.Length == 3)
            {
                var from = ExpeditionRules.Network.Gates.FirstOrDefault(g => g.Code == parts[1] && ExpeditionRules.Infrastructure(g));
                var to = ExpeditionRules.Network.Gates.FirstOrDefault(g => g.Code == parts[2] && ExpeditionRules.Infrastructure(g));
                if (from == null || to == null || !ExpeditionRules.Network.Links.Any(l => l.From == from.Id && l.To == to.Id))
                    return null;
                steps.Add(new TransitStep { From = from, To = to });
                continue;
            }
            return null;
        }
        return steps;
    }
}

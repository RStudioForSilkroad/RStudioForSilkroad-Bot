using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

sealed class GrimRooms
{
    // Approximate centers from the annotated Solo Trial maps, ordered from the entrance.
    static readonly int[][]
    []
    Marks = { new[] { new[] { -886, 4868, -844, 4869, -864, 4894 }, new[] { -901, 4988, -836, 4991, -864, 5026 } }, // D01
    new[] { new[] { -700, 4862, -654, 4865, -678, 4884 }, new[] { -707, 4976, -637, 4978, -674, 5006 }, new[] { -695,
        5087, -657, 5086, -674, 5110 } }, // D02
    new[] { new[] { -543, 4860, -502, 4860, -523, 4880 }, new[] { -516, 4974, -452, 4973, -481, 5005 }, new[] { -463,
        5088, -423, 5090, -443, 5107 } }, // D03
    new[] { new[] { -313, 4871, -272, 4869, -290, 4890 }, new[] { -316, 4986, -256, 4987, -292, 5019 } }, // D04
    new[] { new[] { -120, 4864, -79, 4867, -98, 4886 }, new[] { -130, 4964, -65, 4965, -98, 4997 }, new[] { -123, 5067,
        -78, 5068, -98, 5092 } }, // D05
    new[] { new[] { -888, 4870, -846, 4873, -867, 4890 }, new[] { -898, 4989, -837, 4990, -864, 5025 } }, // D06
    new[] { new[] { -697, 4862, -652, 4865, -674, 4883 }, new[] { -707, 4976, -642, 4971, -678, 5011 }, new[] { -691,
        5087, -657, 5090, -672, 5110 } }, // D07
    new[] { new[] { -544, 4867, -504, 4866, -522, 4887 }, new[] { -514, 4977, -451, 4978, -479, 5005 }, new[] { -464,
        5095, -426, 5092, -447, 5111 } }, // D08
    new[] { new[] { -118, 4861, -77, 4863, -101, 4880 }, new[] { -128, 4966, -65, 4964, -95, 4994 }, new[] { -118, 5070,
        -80, 5071, -97, 5091 } }, // D09
    new[] { new[] { -312, 4869, -269, 4872, -293, 4890 }, new[] { -322, 4982, -262, 4981, -292, 5021 } }, // D10
    new[] { new[] { -888, 4873, -841, 4873, -861, 4892 }, new[] { -903, 4988, -831, 4991, -862, 5029 } }, // D11
    new[] { new[] { -119, 4865, -76, 4863, -96, 4881 }, new[] { -130, 4968, -63, 4968, -99, 4996 }, new[] { -115, 5068,
        -77, 5072, -96, 5092 } }, // D12
    new[] { new[] { -699, 4858, -652, 4863, -676, 4879 }, new[] { -715, 4974, -648, 4973, -673, 5012 }, new[] { -696,
        5088, -654, 5089, -675, 5114 } }, // D13
    new[] { new[] { -546, 4861, -500, 4862, -524, 4881 }, new[] { -521, 4972, -450, 4971, -484, 5004 }, new[] { -465,
        5088, -416, 5089, -437, 5108 } }, // D14
    new[] { new[] { -313, 4869, -270, 4871, -292, 4891 }, new[] { -324, 4982, -261, 4986, -290, 5022 } }, // D15
    new[] { new[] { -888, 4874, -843, 4872, -865, 4893 }, new[] { -896, 4992, -835, 4989, -866, 5026 } }, // D16
    new[] { new[] { -694, 4864, -651, 4863, -674, 4880 }, new[] { -707, 4971, -646, 4972, -677, 5010 }, new[] { -693,
        5089, -656, 5087, -674, 5108 } }, // D17
    new[] { new[] { -545, 4869, -499, 4868, -520, 4885 }, new[] { -516, 4977, -447, 4977, -482, 5012 }, new[] { -463,
        5095, -424, 5089, -444, 5115 } }, // D18
    };

    readonly Position[][]
    areas, waypoints;
    readonly Dictionary<uint, int> seen = new Dictionary<uint, int>();
    readonly HashSet<uint> dead = new HashSet<uint>();
    readonly HashSet<Position> visited = new HashSet<Position>();
    readonly int[] kills;
    int room;
    Position destination;

    GrimRooms(int[][] marks, FloorNavigation floor)
    {
        Position[] cells = floor.Cells.Values.Select(floor.PositionOf).ToArray();
        areas = marks.Select(row => Enumerable.Range(0, 3).Select(i => V2Rules.Center(row[i * 2], row[i * 2 + 1], floor.Origin.Height))
            .ToArray()).ToArray();
        var centers = areas.Select(points => V2Rules.Center(points.Average(p => p.X), points.Average(p => p.Y), points[0].Height))
            .ToArray();
        waypoints = areas.Select((points, roomIndex) =>
        {
            var list = points.Select(point =>
            {
                Position nearest = cells.OrderBy(cell => cell.Distance(point)).First();
                if (nearest.Distance(point) > 8)
                    throw new InvalidDataException("Grim waypoint is outside the captured floor.");
                return nearest;
            }).ToList();
            // Sweep the room square by square: floor points every ~22 units, so a mob is never walked past unseen.
            foreach (Position cell in cells.OrderBy(c => c.Y).ThenBy(c => c.X))
            {
                if (cell.Distance(centers[roomIndex]) > 60)
                    continue;
                int nearestRoom = 0;
                for (int k = 1; k < centers.Length; k++)
                    if (cell.Distance(centers[k]) < cell.Distance(centers[nearestRoom]))
                        nearestRoom = k;
                if (nearestRoom != roomIndex || list.Any(p => p.Distance(cell) < 22))
                    continue;
                list.Add(cell);
            }
            return list.ToArray();
        }).ToArray();
        kills = new int[areas.Length];
    }

    internal static GrimRooms Load(DungeonLocation location, FloorNavigation floor)
    {
        int index;
        if (location.Mode != "Solo Trial" || !floor.Done || !int.TryParse(location.Code.Substring(1), out index) ||
            index < 1 || index > Marks.Length)
            return null;
        return new GrimRooms(Marks[index - 1], floor);
    }

    internal static bool Optional(Monster mob)
    {
        return mob.Rank == 3 || string.Equals(mob.Name, "Envy", StringComparison.OrdinalIgnoreCase) || string.Equals(mob.Name,
            "Grim Treasure Box", StringComparison.OrdinalIgnoreCase);
    }

    int RoomOf(Monster mob)
    {
        double best = 90;
        int result = -1;
        for (int i = 0; i < areas.Length; i++)
        {
            double distance = areas[i].Min(point => point.Distance(mob.Position));
            if (distance < best)
            {
                best = distance;
                result = i;
            }
        }
        return result;
    }

    internal void Observe(IEnumerable<Monster> monsters)
    {
        foreach (Monster mob in monsters)
        {
            int index;
            if (seen.TryGetValue(mob.Uid, out index))
            {
                if ((!mob.Alive || mob.Health == 0) && dead.Add(mob.Uid))
                    kills[index]++;
            }
            else if (mob.Alive && mob.Health != 0 && !Optional(mob))
            {
                index = RoomOf(mob);
                if (index >= 0)
                    seen.Add(mob.Uid, index);
            }
        }
        while (room < kills.Length && kills[room] >= 3)
        {
            room++;
            destination = null;
        }
    }

    internal bool Allows(Monster mob)
    {
        if (Optional(mob) || room == areas.Length)
            return true;
        int index;
        return (seen.TryGetValue(mob.Uid, out index) ? index : RoomOf(mob)) == room;
    }

    internal Position Next(Position player)
    {
        if (room == waypoints.Length)
            return null;
        if (destination == null)
        {
            destination = waypoints[room].Where(point => !visited.Contains(point)).OrderBy(point => point.Distance(player))
                .FirstOrDefault();
            // Whole room swept and still fewer than 3 kills: sweep it again.
            if (destination == null)
            {
                foreach (Position point in waypoints[room])
                    visited.Remove(point);
                destination = waypoints[room].OrderBy(point => point.Distance(player)).FirstOrDefault();
            }
        }
        return destination;
    }

    internal void Arrived()
    {
        if (destination != null)
            visited.Add(destination);
        destination = null;
    }

    internal string Status
    {
        get
        {
            if (room == areas.Length)
                return "İşaretli odalar temizlendi · sunucu bitişi bekleniyor";
            return "Oda " + (room + 1) + "/" + areas.Length + " · " + kills[room] + "/3 doğrulanmış ölüm";
        }
    }
}

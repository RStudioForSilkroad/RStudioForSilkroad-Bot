using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

// Getting near a dungeon (or any travel target) with the Reverse Return Scroll: the scroll's third choice jumps to the place of the game's
// list that is closest to the target, and the walk starts from there. The scroll has a lock of one minute after every use, so it is only
// used when nothing can go wrong and it pays off, never twice in a row, and the next step waits for the arrival.
sealed partial class MemorySelectV3Form
{
    // A Reverse place is only worth a scroll when it saves at least this much walking (map units).
    const double ReverseMinimumSaving = 1200;

    readonly CheckBox useReverseTravel = new CheckBox();
    ReversePoint reverseTarget;
    long reverseLockUntil;

    // Where the character is, in words: the town when it stands in one, else the region of the map.
    static string WhereAmI(Position p)
    {
        if (V3Data.IsCrypt(p))
            return "dungeon içinde";
        string town = V3Data.Town(p);
        return town != null ? town + " (şehir)" : ExpeditionRules.Area(p) + " bölgesi (şehir dışı)";
    }

    // Both in the same town: the dungeon is right there, no scroll of any kind is needed.
    static bool SameTown(Position a, Position b)
    {
        string town = V3Data.Town(a);
        return town != null && town == V3Data.Town(b);
    }

    // The walking length of the planned road between two points; a hop through a gate costs nothing. A point that cannot be planned is
    // counted as twice the straight distance, so it is never preferred over a point that can.
    static double RoadLength(Position from, Position to)
    {
        try
        {
            double length = 0;
            Position at = from;
            foreach (var step in ExpeditionRules.Plan(from, to))
            {
                if (step.From != null)
                {
                    length += at.Distance(step.From.Point());
                    at = step.To.Point();
                }
                else
                {
                    var next = step.To.Point();
                    length += at.Distance(next);
                    at = next;
                }
            }
            return length + at.Distance(to);
        }
        catch
        {
            return from.Distance(to) * 2;
        }
    }

    // The place to jump to before walking to the dungeon, or null when the scroll is not used (setting off, none in the bag, the dungeon is in
    // this very town, or no place saves enough walking).
    ReversePoint ChooseReversePoint(DungeonLocation d)
    {
        if (!useReverseTravel.Checked || caravanWalk || explorerRun != null || RecoveryRules.PickReverse(snapshot) == null)
            return null;
        var player = snapshot.Player;
        var goal = d.Entrance();
        if (V3Data.IsCrypt(player))
            return null;
        if (SameTown(player, goal))
        {
            Log("Dungeon bu şehirde; Reverse / Return kullanılmayacak.");
            return null;
        }
        var point = ReverseRules.Nearest(player, goal, RoadLength, ReverseMinimumSaving);
        if (point == null)
            Log("Reverse yeri yürümekten kazançlı değil; yürünecek.");
        else
            Log("Dungeon'a en yakın Reverse yeri: " + point.Name + " (" + point.Id + ") · yürüyüş " + (int)RoadLength(player, goal) + " → " +
                (int)RoadLength(point.Point(), goal) + " birim");
        return point;
    }

    // Phase 6: use the scroll once. Phase 7: wait until the character stands at the place, then walk on from there.
    void ReverseTravelTick(DungeonLocation d, long now)
    {
        var point = reverseTarget;
        if (point == null)
        {
            BuildRoadRoute(d);
            return;
        }
        if (expeditionPhase == 6)
        {
            var item = RecoveryRules.PickReverse(snapshot);
            if (item == null)
            {
                Log("Reverse Scroll kalmadı; yürünerek gidilecek.");
                reverseTarget = null;
                BuildRoadRoute(d);
                return;
            }
            // The lock of the last use: wait for it instead of spending a scroll that would do nothing.
            if (now < reverseLockUntil)
            {
                status.Text = "Reverse bekleme süresi: " + ((reverseLockUntil - now + 999) / 1000) + " sn";
                return;
            }
            if (!snapshot.PlayerAlive)
            {
                Stop("Karakter ölü; Reverse kullanılmadı.");
                return;
            }
            if (!EnsureTravelMount(false, now))
                return;
            if (!reader.ValidateItem(item))
            {
                Stop("Reverse Scroll tekrar doğrulanamadı; kullanılmadı.");
                return;
            }
            if (!sender.UseReverseTo(item, point.Id))
            {
                Stop("Reverse isteği reddedildi; tekrar scroll kullanılmadı.");
                return;
            }
            reverseLockUntil = now + ReverseRules.LockMs;
            Log("Reverse kullanıldı · " + point.Name + " (" + point.Id + ") · eşya " + item.Model + " slot " + item.Slot);
            expeditionSince = now;
            expeditionPhase = 7;
            status.Text = "Reverse kullanıldı; " + point.Name + " varışı bekleniyor.";
            return;
        }
        // The loading screen reads as region 0; the place itself is where the scroll puts the character, to the unit.
        if (snapshot.Player.Region != 0 && snapshot.Player.Distance(point.Point()) <= 80)
        {
            if (now - expeditionSince < 3000)
                return;
            Log("Reverse varışı doğrulandı · " + point.Name);
            reverseTarget = null;
            BuildRoadRoute(d);
            routeWalker.Reset();
            return;
        }
        if (now - expeditionSince > 90000)
        {
            Stop("Reverse varışı doğrulanamadı; tekrar scroll kullanılmadı.");
            return;
        }
        status.Text = "Reverse varışı bekleniyor: " + point.Name;
    }
}

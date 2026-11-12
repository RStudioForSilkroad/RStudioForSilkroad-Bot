using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows.Forms;

// Checks of the skill system: buff upkeep by the character's own buff list, emergency lines, target-class attack lists, background key
// messages, settings, and the Grim walk that no longer stops the run.
partial class V3Checks
{
    static SkillContext Ctx(int hp = 100, int mp = 100, int attackers = 0, bool combat = false, uint[] buffs = null)
    {
        return new SkillContext { Hp = hp, Mp = mp, Attackers = attackers, InCombat = combat, StatsKnown = true, Buffs = new HashSet<uint>(buffs ?? new uint[0]) };
    }

    static void SkillEngineChecks()
    {
        // ---- buffs follow the character's real buff list ----
        var buff = new SkillRotation();
        buff.Load(new[] { new SkillSlot { Key = "5", Kind = SkillRotation.Buff_, SkillId = 4001 } });
        A(buff.Support(0, Ctx()) == '5', "A buff that is missing from the buff list is pressed");
        A(buff.Support(1000, Ctx()) == 0, "The same buff is not pressed again within 3 seconds");
        A(buff.Support(3500, Ctx(buffs: new uint[] { 4001 })) == 0 && buff.Support(60000, Ctx(buffs: new uint[] { 4001 })) == 0, "A buff that is active is left alone");
        A(buff.Support(61000, Ctx(buffs: new uint[] { 7 })) == '5', "When the buff is gone the key is pressed again, however long it lasted");
        // five failed presses in a row (no mana, skill not learned) back off for 30 seconds
        var stubborn = new SkillRotation();
        stubborn.Load(new[] { new SkillSlot { Key = "5", Kind = SkillRotation.Buff_, SkillId = 4001 } });
        long t = 0;
        int pressed = 0;
        for (int i = 0; i < 5; i++, t += 3100)
            if (stubborn.Support(t, Ctx()) == '5')
                pressed++;
        A(pressed == 5 && stubborn.Support(t, Ctx()) == 0 && stubborn.Support(t + 20000, Ctx()) == 0 && stubborn.Support(t + 31000, Ctx()) == '5',
            "A buff that never appears is retried five times, then left alone for 30 seconds");
        // unknown buff list: the timer decides
        var timed = new SkillRotation();
        timed.Load(new[] { new SkillSlot { Key = "6", Kind = SkillRotation.Buff_, SkillId = 4002, Seconds = 60 } });
        var blind = new SkillContext { Buffs = null };
        A(timed.Support(0, blind) == '6' && timed.Support(30000, blind) == 0 && timed.Support(60000, blind) == '6',
            "Without a readable buff list the buff repeats by its interval");
        // buffs do not interrupt a fight when asked not to
        var calm = new SkillRotation();
        calm.Load(new[] { new SkillSlot { Key = "5", Kind = SkillRotation.Buff_, SkillId = 4001 } }, new SkillOptions { BuffWhileAttacking = false });
        A(calm.Support(0, Ctx(combat: true)) == 0 && calm.Support(0, Ctx(combat: false)) == '5', "Buffs wait for the end of a fight when asked to");
        // a revival or a new area makes every buff due again
        var once = new SkillRotation();
        once.Load(new[] { new SkillSlot { Key = "9", Kind = SkillRotation.Buff_ } });
        A(once.Support(0, blind) == '9' && once.Support(500000, blind) == 0, "A buff with interval 0 is pressed once per run");
        once.Rearm();
        A(once.Support(500100, blind) == '9', "Rearm makes it due again (after a revival or a change of area)");

        // ---- emergency buffs ----
        var emergency = new SkillRotation();
        emergency.Load(new[]
        {
            new SkillSlot { Key = "7", Kind = SkillRotation.Emergency_, Percent = 35, Seconds = 20, SkillId = 5001 },
            new SkillSlot { Key = "8", Kind = SkillRotation.Emergency_, Percent = 0, MinMobs = 4, Seconds = 20 }
        });
        A(emergency.Support(0, Ctx(hp: 80)) == 0, "No emergency buff while healthy and not crowded");
        A(emergency.Support(0, Ctx(hp: 30)) == '7', "Low HP presses the emergency buff");
        A(emergency.Support(1000, Ctx(hp: 30)) == 0, "The emergency buff has its cooldown");
        A(emergency.Support(30000, Ctx(hp: 30, buffs: new uint[] { 5001 })) == 0, "An emergency buff that is already active is not pressed");
        A(emergency.Support(30000, Ctx(attackers: 3)) == 0 && emergency.Support(30000, Ctx(attackers: 4)) == '8',
            "Enough monsters around the character press the crowd emergency buff");
        var order = new SkillRotation();
        order.Load(new[]
        {
            new SkillSlot { Key = "9", Kind = SkillRotation.Buff_, SkillId = 1 },
            new SkillSlot { Key = "8", Kind = SkillRotation.Heal_, Percent = 50, Seconds = 3 },
            new SkillSlot { Key = "7", Kind = SkillRotation.Emergency_, Percent = 30, Seconds = 3 }
        });
        A(order.Support(0, Ctx(hp: 20)) == '7' && order.Support(10, Ctx(hp: 20)) == '8' && order.Support(20, Ctx(hp: 20)) == '9',
            "Emergency comes before heal, heal before buff");

        // ---- attack lists by target class and mob count ----
        var attack = new SkillRotation();
        attack.Load(new[]
        {
            new SkillSlot { Key = "1", Kind = SkillRotation.Attack_ },
            new SkillSlot { Key = "2", Kind = SkillRotation.Attack_, Target = "Giant", Seconds = 10 },
            new SkillSlot { Key = "3", Kind = SkillRotation.Attack_, MinMobs = 3, Seconds = 5 }
        });
        A(attack.NextAttack(0, "General", 1) == '1' && attack.NextAttack(10, "General", 1) == '1',
            "A mob-count line stays unused while few monsters are around");
        A(attack.NextAttack(20, "General", 5) == '3', "...and is used once enough monsters are close");
        attack.NewTarget();
        A(attack.NextAttack(30, "Giant", 1) == '2', "A line made for the Giant class is chosen first for a Giant");
        A(attack.NextAttack(40, "Giant", 1) == '1', "With the fallback option the general list is used while the Giant line recovers");
        var strict = new SkillRotation();
        strict.Load(new[]
        {
            new SkillSlot { Key = "1", Kind = SkillRotation.Attack_ },
            new SkillSlot { Key = "2", Kind = SkillRotation.Attack_, Target = "Giant", Seconds = 10 }
        }, new SkillOptions { Fallback = false });
        A(strict.NextAttack(0, "Giant", 1) == '2' && strict.NextAttack(10, "Giant", 1) == 0 && strict.NextAttack(20, "General", 1) == '1',
            "Without the fallback a Giant waits for its own line");
        var inOrder = new SkillRotation();
        inOrder.Load(new[] { new SkillSlot { Key = "1" }, new SkillSlot { Key = "2" } });
        inOrder.NextAttack(0, "General", 1);
        inOrder.NewTarget();
        A(inOrder.NextAttack(1, "General", 1) == '1', "A new target starts the list from its first line");

        // ---- validation ----
        A(SkillRotation.Validate(new[] { new SkillSlot { Key = "1", SkillId = -3 } }) != null, "A negative Skill ID is refused");
        A(SkillRotation.Validate(new[] { new SkillSlot { Key = "1", MinMobs = 99 } }) != null, "A mob count above 50 is refused");
        A(SkillRotation.Validate(new[] { new SkillSlot { Key = "1", Target = "Dragon" } }) != null, "An unknown target class is refused");
        A(SkillRotation.Validate(new[] { new SkillSlot { Key = "1", Kind = SkillRotation.Emergency_, Percent = 0, MinMobs = 3 } }) == null,
            "An emergency line may rely on the crowd condition alone");
        A(SkillRotation.Validate(new[] { new SkillSlot { Key = "1", Kind = SkillRotation.Emergency_, Percent = 0 } }) != null,
            "An emergency line needs a percent or a mob count");
        foreach (string name in SkillRotation.TemplateNames)
        {
            var template = SkillRotation.Template(name);
            foreach (var s in template)
                s.On = true;
            A(template.Count > 0 && SkillRotation.Validate(template) == null && template.All(s => s.On), "Template “" + name + "” is valid once switched on");
            A(SkillRotation.Template(name).All(s => !s.On), "Template “" + name + "” arrives switched off");
        }

        // ---- keyboard messages for a window in the background ----
        A(KeyMessage.LParam(2, false) == 0x00020001u && KeyMessage.LParam(2, true) == 0xC0020001u,
            "WM_KEYDOWN / WM_KEYUP lParam carry repeat count, scan code and transition flags");
        A(KeyMessage.Param(0x1e, true).ToInt32() == unchecked((int)0xC01E0001u), "The lParam fits a 32-bit pointer-sized value");
        A(!KeyMessage.Post(IntPtr.Zero, 0x31, 2, false), "No key is posted to a missing window");

        // ---- settings: old files keep working, new fields round-trip ----
        var old = new JavaScriptSerializer().Deserialize<V3Settings>("{\"SkillList\":true,\"Skills\":[{\"On\":true,\"Key\":\"4\",\"Kind\":\"Buff\",\"Seconds\":90,\"Percent\":50}]}");
        A(old.SkillList && old.Skills.Count == 1 && old.Skills[0].Target == "Hepsi" && old.Skills[0].SkillId == 0 && old.SkillOptions.Rearm &&
            !old.BackgroundKeys && old.SkillProfiles.Count == 0, "A settings file from before the skill page still loads with sensible defaults");
        var fresh = new V3Settings { BackgroundKeys = true };
        fresh.SkillOptions.BuffOnly = true;
        fresh.Skills.Add(new SkillSlot { Key = "3", Kind = SkillRotation.Emergency_, SkillId = 777, Target = "Giant", MinMobs = 4, Name = "kalkan" });
        fresh.SkillProfiles.Add(new SkillProfile { Name = "Okçu", Slots = new List<SkillSlot> { new SkillSlot { Key = "6", SkillId = 12 } } });
        var json = new JavaScriptSerializer().Serialize(fresh);
        var back = new JavaScriptSerializer().Deserialize<V3Settings>(json);
        A(back.BackgroundKeys && back.SkillOptions.BuffOnly && back.Skills[0].SkillId == 777 && back.Skills[0].Name == "kalkan" &&
            back.Skills[0].MinMobs == 4 && back.SkillProfiles.Count == 1 && back.SkillProfiles[0].Name == "Okçu" && back.SkillProfiles[0].Slots[0].SkillId == 12,
            "Skill settings and profiles survive a save and load");

        // ---- the page in the window ----
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(MemorySelectV3Form);
        using (var form = (Form)Activator.CreateInstance(type, true))
        {
            type.GetField("settings", flags).SetValue(form, null);
            ((Timer)type.GetField("poll", flags).GetValue(form)).Stop();
            var grid = (DataGridView)type.GetField("skillGrid", flags).GetValue(form);
            A(grid.Columns.Count == 9 && grid.Columns["SkillId"] != null && grid.Columns["Target"] != null && grid.Columns["Mobs"] != null,
                "The skill grid has name, key, kind, Skill ID, target class, mob count, seconds and percent columns");
            var restore = new V3Settings { SkillList = true, BackgroundKeys = true };
            restore.SkillOptions.BuffWhileAttacking = false;
            restore.Skills.Add(new SkillSlot { Key = "5", Kind = SkillRotation.Buff_, SkillId = 321, Name = "zırh", Seconds = 0 });
            restore.Skills.Add(new SkillSlot { Key = "2", Kind = SkillRotation.Attack_, Target = "Unique", MinMobs = 2, Seconds = 7 });
            type.GetMethod("RestoreSkillSettings", flags).Invoke(form, new object[] { restore });
            var read = (List<SkillSlot>)type.GetMethod("SkillSlotsFromGrid", flags).Invoke(form, null);
            A(read.Count == 2 && read[0].SkillId == 321 && read[0].Name == "zırh" && read[1].Target == "Unique" && read[1].MinMobs == 2 && read[1].Seconds == 7,
                "The grid shows and returns every skill field");
            var saved = new V3Settings();
            type.GetMethod("SaveSkillSettings", flags).Invoke(form, new object[] { saved });
            A(saved.BackgroundKeys && saved.SkillList && !saved.SkillOptions.BuffWhileAttacking && saved.Skills.Count == 2,
                "Saving the page writes options and lines back");
            var method = type.GetMethod("CommandsAllowed", flags);
            A(!(bool)method.Invoke(form, null), "Without a connected game no command is allowed, in the background or not");

            // ---- Grim: a walk without a route no longer stops the run ----
            var dungeon = V3Data.Dungeons.First(x => x.Code == "D13" && x.Mode == "Solo Trial");
            var floor = FloorNavigation.LoadCaptured(dungeon);
            var run = (GrimRun)type.GetField("grim", flags).GetValue(form);
            run.Arm(dungeon, floor.Origin);
            run.Floor = floor;
            type.GetField("snapshot", flags).SetValue(form, S(true, floor.Origin));
            Position far = floor.Cells.Values.Select(floor.PositionOf).OrderByDescending(p => p.Distance(floor.Origin)).First();
            var path = floor.PathBetween(floor.Origin, far);
            A(path.Count > 2 && path.Zip(path.Skip(1), (a, b) => a.Distance(b)).All(d => d <= floor.Step + .01) && path[0].Distance(floor.Origin) < 1,
                "The checked floor gives a path of single steps between two far cells");
            var walkFailed = type.GetMethod("GrimWalkFailed", flags);
            Func<string, Position, long, bool> fail = (why, goal, now) => (bool)walkFailed.Invoke(form, new object[] { why, goal, now });
            bool handled = fail("Yüklenmiş zeminde rota bulunamadı", far, 1000);
            var graph = (List<Position>)type.GetField("graphPath", flags).GetValue(form);
            A(handled && graph != null && graph.Count == path.Count, "A Grim walk with no route first falls back to the checked floor cells");
            handled = fail("yine olmadı", far, 2000);
            long retry = (long)type.GetField("grimRetryAt", flags).GetValue(form);
            A(handled && type.GetField("graphPath", flags).GetValue(form) == null && retry > 2000,
                "If that fails too the point is dropped and the run waits a moment instead of stopping");
            handled = fail("uzak hedef", P(1000, 1000), 3000);
            A(handled && type.GetField("graphPath", flags).GetValue(form) == null, "A goal off the floor is skipped, not walked into");
            floor.Covered.Clear();
            floor.MarkCovered(far);
            A(floor.Covered.Count > 0 && floor.Covered.Count < floor.Cells.Count, "A skipped goal marks only the cells around it as visited");
            A(floor.NearestCell(far) != null && floor.NearestCell(far).Distance(far) < 1, "The nearest checked floor point is found");
        }
    }
}

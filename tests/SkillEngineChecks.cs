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
        LearnedSkillChecks();
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

    sealed class FakeMemory
    {
        internal readonly uint Base;
        internal readonly byte[] Data;

        internal FakeMemory(uint start, int size)
        {
            Base = start;
            Data = new byte[size];
        }

        internal void Put(uint address, uint value)
        {
            BitConverter.GetBytes(value).CopyTo(Data, (int)(address - Base));
        }

        internal byte[] Read(uint address, int size)
        {
            if (address < Base || address + size > Base + Data.Length)
                throw new System.IO.IOException("outside");
            var b = new byte[size];
            Array.Copy(Data, (int)(address - Base), b, 0, size);
            return b;
        }
    }

    // A red-black-tree-shaped map in fake memory: header (left = first, parent = root, right = last), nodes of 32 bytes, key at +12.
    static void BuildTree(FakeMemory m, uint header, uint[] keys)
    {
        var sorted = keys.OrderBy(k => k).ToArray();
        uint next = header + 32;
        Func<int, int, uint, uint> make = null;
        make = (lo, hi, parent) =>
        {
            if (lo > hi)
                return header;
            int mid = (lo + hi) / 2;
            uint node = next;
            next += 32;
            m.Put(node + 4, parent);
            m.Put(node + 12, sorted[mid]);
            m.Put(node, make(lo, mid - 1, node));
            m.Put(node + 8, make(mid + 1, hi, node));
            return node;
        };
        uint root = make(0, sorted.Length - 1, header);
        m.Put(header + 4, root);
        uint first = root, last = root;
        while (BitConverter.ToUInt32(m.Read(first, 4), 0) != header)
            first = BitConverter.ToUInt32(m.Read(first, 4), 0);
        while (BitConverter.ToUInt32(m.Read(last + 8, 4), 0) != header)
            last = BitConverter.ToUInt32(m.Read(last + 8, 4), 0);
        m.Put(header, first);
        m.Put(header + 8, last);
    }

    static void LearnedSkillChecks()
    {
        // ---- the game's skill table ----
        var names = new List<string>();
        var meta = new List<string>();
        for (uint id = 100; id < 160; id++)
        {
            names.Add(id + "\tSkill " + id + "\r");
            var cells = new string[28];
            for (int i = 0; i < cells.Length; i++)
                cells[i] = "0";
            cells[0] = id.ToString();
            cells[5] = id % 2 == 0 ? "60000" : "2000";
            cells[11] = "20";
            cells[23] = (id + 1000).ToString();
            cells[24] = "3";
            meta.Add(string.Join("\t", cells));
        }
        names.Add("9\tStrike Smash\r");
        SkillCatalog.Parse(names, meta);
        A(SkillCatalog.Loaded && SkillCatalog.Count == 61 && SkillCatalog.IsSkill(120) && !SkillCatalog.IsSkill(99) && SkillCatalog.NameOf(120) == "Skill 120",
            "The skill table is read: ids, names, and which ids are skills");
        A(SkillCatalog.Find(120).Family == 1120 && SkillCatalog.Find(120).Level == 3 && SkillCatalog.Find(120).ReqLevel == 20 && SkillCatalog.Find(120).Time == 60000,
            "Family, level, required level and time come from the table");
        A(SkillCatalog.Search("strike", 10).Count == 1 && SkillCatalog.Search("s", 10).Count == 0 && SkillCatalog.Search("Skill 12", 100).Count == 10,
            "The skill table is searched by name (at least two letters)");
        A(SkillCatalog.GuessKind(SkillCatalog.Find(120)) == SkillRotation.Buff_ && SkillCatalog.GuessKind(SkillCatalog.Find(121)) == SkillRotation.Attack_,
            "A long time column suggests a buff");

        // ---- the learned-skill tree is found by shape, with no fixed address ----
        var mem = new FakeMemory(0x02000000, 0x8000);
        var real = new uint[] { 101, 103, 105, 110, 112, 118, 125, 131, 140, 150 };
        BuildTree(mem, 0x02000000, real);
        BuildTree(mem, 0x02002000, new uint[] { 102, 104, 106, 107, 108, 109 });
        BuildTree(mem, 0x02004000, new uint[] { 101, 120 });
        // a decoy that is not a tree: a few skill ids in a row
        for (uint i = 0; i < 8; i++)
            mem.Put(0x02006000 + i * 4, 120 + i);
        var candidates = SkillScan.Candidates(mem.Data, mem.Data.Length, mem.Base, SkillCatalog.ValidTable);
        A(candidates.Count >= real.Length + 6, "Nodes that look like skill-id tree nodes are found in a memory buffer");
        string report;
        var seeds = new uint[] { 105, 112, 150 };
        var chosen = SkillScan.Choose(mem.Read, candidates, seeds, SkillCatalog.IsSkill, SkillCatalog.FamilyOf, 5, out report);
        A(chosen != null && chosen.Header == 0x02000000 && chosen.Keys.OrderBy(k => k).SequenceEqual(real),
            "The tree that holds the character's known skills is chosen and read completely");
        var none = SkillScan.Choose(mem.Read, candidates, new uint[] { 159 }, SkillCatalog.IsSkill, SkillCatalog.FamilyOf, 5, out report);
        A(none == null, "No tree is chosen when none of them holds a known skill");
        A(SkillScan.FindHeader(mem.Read, 0x02000000 + 32) == 0x02000000, "From any node the header of its tree is found");
        A(SkillScan.FindHeader(mem.Read, 0x02006000) == 0, "Skill ids in a row are not mistaken for a tree");
        SkillCatalog.Clear();
        A(!SkillCatalog.Loaded && !SkillCatalog.IsSkill(120), "The table can be cleared");

        // ---- using a skill by id ----
        byte[] cast = TargetSender.CastPayload(10258, 0x1234);
        A(cast.Length == 11 && cast[0] == 1 && cast[1] == 4 && BitConverter.ToUInt32(cast, 2) == 10258 && cast[6] == 1 && BitConverter.ToUInt32(cast, 7) == 0x1234 &&
            TargetSender.ValidCast(cast), "A cast on a target is 01 04, skill id, 01, target id");
        byte[] self = TargetSender.CastPayload(11738, 0);
        A(self.Length == 7 && self[6] == 0 && TargetSender.ValidCast(self), "A cast on oneself has no target");
        A(!TargetSender.ValidCast(TargetSender.CastPayload(0, 5)) && !TargetSender.ValidCast(new byte[] { 1, 5, 1, 0, 0, 0, 0 }) &&
            !TargetSender.ValidCast(new byte[] { 1, 4, 1, 0, 0, 0, 1 }) && !TargetSender.ValidCast(null), "Anything else is not a valid cast");
        A(TargetSender.BuildPacket(0x10000000, 0x2000, 0x7074, cast)[64] == 1, "The cast packet is built through the allowed list");
        bool refused = false;
        try
        {
            TargetSender.BuildPacket(0x10000000, 0x2000, 0x7074, new byte[] { 1, 4, 0, 0, 0, 0, 0 });
        }
        catch (ArgumentException)
        {
            refused = true;
        }
        A(refused, "A cast without a skill id is refused");

        // ---- talking to an NPC ----
        byte[] talk = new byte[] { 0x78, 0x56, 0x34, 0x12, 1 };
        A(TargetSender.BuildPacket(0x10000000, 0x2000, 0x7046, talk)[68] == 1, "The NPC talk packet (NPC id, option 1) is built");
        foreach (byte option in new byte[] { 0, 10 })
        {
            refused = false;
            try
            {
                TargetSender.BuildPacket(0x10000000, 0x2000, 0x7046, new byte[] { 1, 2, 3, 4, option });
            }
            catch (ArgumentException)
            {
                refused = true;
            }
            A(refused, "NPC talk option " + option + " is refused");
        }
        A(new V3Settings().ExplorerTalk && new V3Settings().ExplorerTalkOption == 1, "Explorer talks to the NPC by default, option 1");

        // ---- lines with an id and no key ----
        var ids = new SkillRotation();
        ids.Load(new[]
        {
            new SkillSlot { Key = "", Kind = SkillRotation.Buff_, SkillId = 11738, Name = "Healing Cycle" },
            new SkillSlot { Key = "", Kind = SkillRotation.Attack_, SkillId = 10258 },
            new SkillSlot { Key = "2", Kind = SkillRotation.Attack_, SkillId = 10129 }
        });
        A(ids.HasIdOnly && ids.Keys().SequenceEqual(new ushort[] { '2' }), "A line with an id and no key is used by id and adds no key to the key list");
        ushort first = ids.Support(0, Ctx());
        A(first == SkillRotation.IdOnly && ids.LastSkillId == 11738 && ids.LastKind == SkillRotation.Buff_, "A buff line without a key reports its skill id");
        A(ids.Support(1000, Ctx(buffs: new uint[] { 11738 })) == 0, "An id-only buff that is already on is left alone");
        ushort a1 = ids.NextAttack(0), a2 = ids.NextAttack(1);
        A(a1 == SkillRotation.IdOnly && ids.LastSkillId != 0 && a2 == '2' && ids.LastSkillId == 10129, "Attack lines alternate between an id line and a key line");
        A(SkillRotation.Validate(new[] { new SkillSlot { Key = "", SkillId = 10258 } }) == null, "A line with an id and no key is valid");
        A(SkillRotation.Validate(new[] { new SkillSlot { Key = "", SkillId = 0 } }) != null, "A line with neither a key nor an id is refused");
        A(SkillRotation.Validate(new[] { new SkillSlot { Key = "??", SkillId = 5 } }) != null, "A wrong key is still refused when an id is given");
    }
}

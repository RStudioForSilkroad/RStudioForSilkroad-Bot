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
        ProtocolAndRouteChecks();
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
            A(grid.Columns.Count == 10 && grid.Columns["SkillId"] != null && grid.Columns["Target"] != null && grid.Columns["Mobs"] != null && grid.Columns["Members"] != null,
                "The skill grid has name, key, kind, Skill ID, target class, mob count, seconds, percent and party member columns");
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

    static void ProtocolAndRouteChecks()
    {
        // ---- no skill is ever sent as a packet ----
        bool refused = false;
        foreach (byte[] payload in new[] { new byte[] { 1, 4, 0x12, 0x28, 0, 0, 0 }, new byte[] { 1, 4, 0x12, 0x28, 0, 0, 1, 0x34, 0x12, 0, 0 }, new byte[0] })
        {
            refused = false;
            try
            {
                TargetSender.BuildPacket(0x10000000, 0x2000, 0x7074, payload);
            }
            catch (ArgumentException)
            {
                refused = true;
            }
            A(refused, "A skill packet (0x7074, " + payload.Length + " bytes) is refused: the bot cannot send a cast");
        }
        A(TargetSender.BuildPacket(0x10000000, 0x2000, 0x7045, new byte[] { 1, 2, 3, 4 }).Length > 0 &&
            TargetSender.BuildPacket(0x10000000, 0x2000, 0x704c, new byte[] { 14, 0xec, 0x0e }).Length > 0 &&
            TargetSender.BuildPacket(0x10000000, 0x2000, 0x7034, new byte[] { 0, 20, 6, 1, 0 }).Length > 0 &&
            TargetSender.BuildPacket(0x10000000, 0x2000, 0x3053, new byte[] { 1 }).Length > 0 &&
            TargetSender.BuildPacket(0x10000000, 0x2000, 0x7046, new byte[] { 1, 2, 3, 4, 1 }).Length > 0 &&
            TargetSender.BuildPacket(0x10000000, 0x2000, 0x705a, new byte[] { 1, 2, 3, 4, 2, 5, 0, 0, 0 }).Length > 0,
            "Select, item use, item move, return, talk and teleport packets are still built");
        A(typeof(TargetSender).GetMethod("Cast", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) == null &&
            typeof(TargetSender).GetMethod("ValidCast", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public) == null,
            "The cast sender and its validator no longer exist");

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

        // ---- known GuardyNE.dll builds ----
        var oldGuard = GuardBuild.ByHash("3206cf49259eca33ca6df4783e1d7192ed722ceefead1925f9c7e46b7f8d1d01");
        var newGuard = GuardBuild.ByHash("62F212E6B775501CECB81C8AFE83292DA20BAA62645D662E65CD1E251AED660D");
        A(oldGuard != null && oldGuard.Sender == 0xE183 && oldGuard.Move == 0x743CD0 && oldGuard.Stamp == 0x6aea06e7,
            "The DLL build before the rev 34 update is still known, hash compared without regard to case");
        A(newGuard != null && newGuard.Sender == 0xF92F && newGuard.Move == 0x844A10 && newGuard.Reverse == 0x842000 && newGuard.Navigation == 0x76D8D0 &&
            newGuard.MountUid == 0xd7c720 && newGuard.GrimText == 0xc28070 && newGuard.MountDown == 0x495B10 && newGuard.MountUp == 0x495A30 && newGuard.Stamp == 0x6afa04fb,
            "The rev 34 DLL build has its own offsets");
        A(GuardBuild.ByHash("00") == null && GuardBuild.Known.Select(b => b.Hash).Distinct().Count() == GuardBuild.Known.Length,
            "An unknown DLL is refused and every build has its own hash");

        // ---- caravan animals are not the stable's mount ----
        A(new V3Settings().CaravanAnimals.SequenceEqual(new[] { 23909 }), "The Buffalo is a caravan animal by default");
        A(MemorySelectV3Form.IsCaravanAnimal(23909, new V3Settings().CaravanAnimals) && !MemorySelectV3Form.IsCaravanAnimal(1912, new V3Settings().CaravanAnimals) &&
            !MemorySelectV3Form.IsCaravanAnimal(0, new V3Settings().CaravanAnimals) && !MemorySelectV3Form.IsCaravanAnimal(23909, null),
            "A ridden animal is told from the stable's mount by its model");

        // ---- the new caravan routes ----
        var routeNames = TradeRoutes.Routes.Select(r => r[0] + ">" + r[1]).ToList();
        A(new[] { "Samarkand>Constantinople", "Constantinople>Samarkand", "Jangan>Nanzhang", "Nanzhang>Jangan" }.All(routeNames.Contains) &&
            new[] { "Samarkand", "Constantinople", "Nanzhang" }.All(TradeRoutes.Towns.Contains),
            "Samarkand - Constantinople (both ways) and Jangan - Nanzhang are caravan routes with their own trader towns");
        Func<double, double, Position> at = (x, y) => { var p = V2Rules.Center(x, y, 0); float h; if (ExpeditionRules.TryGround(p, out h)) p.Height = h; return p; };
        var samarkandPoint = at(-5280, 2784);
        var constantinoplePoint = at(-10700, 2600);
        var there = TradeRoutes.PlanWalk(samarkandPoint, constantinoplePoint);
        var backAgain = TradeRoutes.PlanWalk(constantinoplePoint, samarkandPoint);
        A(there.Count > 20 && backAgain.Count > 20 && there.Last().To.Point().Distance(constantinoplePoint) < 80 && backAgain.Last().To.Point().Distance(samarkandPoint) < 80,
            "Samarkand - Constantinople is planned in both directions over roads");
        var janganPoint = at(6512, 1008);
        var nanzhangPoint = at(5955.6, -1757.4);
        var toNanzhang = TradeRoutes.PlanWalk(janganPoint, nanzhangPoint);
        var fromNanzhang = TradeRoutes.PlanWalk(nanzhangPoint, janganPoint);
        A(toNanzhang.Count > 100 && toNanzhang.Last().To.Point().Distance(nanzhangPoint) < 80 && fromNanzhang.Count > 100 && fromNanzhang.Last().To.Point().Distance(janganPoint) < 80,
            "Jangan - Nanzhang is planned in both directions");
        A(TradeRoutes.RoadWaypoints(janganPoint, nanzhangPoint).Count == TradeRoutes.RoadNorth.Length + 1 + 1 + TradeRoutes.RoadSouth.Length &&
            TradeRoutes.RoadWaypoints(nanzhangPoint, janganPoint).First().Distance(at(6159, -1710)) < 5 && TradeRoutes.RoadWaypoints(samarkandPoint, constantinoplePoint) == null,
            "The marked road is followed from Jangan, reversed from Nanzhang, and used for no other route");
        // The road is cut by the Tiger Rim cliff: one climbers' teleport joins the two parts (Warden down, Ropemaster up).
        Func<IEnumerable<TransitStep>, List<TransitStep>> hops = plan => plan.Where(s => s.From != null && ExpeditionRules.Network.Links.Any(l => l.From == s.From.Id && l.To == s.To.Id)).ToList();
        var downHops = hops(toNanzhang);
        var upHops = hops(fromNanzhang);
        A(downHops.Count == 1 && downHops[0].From.Id == 442 && downHops[0].From.Model == 60161 && downHops[0].To.Id == 441 &&
            upHops.Count == 1 && upHops[0].From.Id == 441 && upHops[0].From.Model == 60160 && upHops[0].To.Id == 442,
            "Jangan - Nanzhang crosses the cliff by the Cliff Warden's teleport, Nanzhang - Jangan by the Ropemaster's");
        int hopAt = toNanzhang.IndexOf(downHops[0]);
        A(toNanzhang.Take(hopAt).All(s => s.To.Point().Y > -800 || s.From != null) && toNanzhang.Skip(hopAt + 1).All(s => s.To.Point().Y < -740) &&
            toNanzhang.Take(hopAt).Last().To.Point().Distance(V2Rules.Center(6944, -787.5, 735)) < 25,
            "The walk to the Warden ends on the cliff top and everything after the teleport is south of the camp");
        var campSpot = V2Rules.Center(6944, -760, 483);
        var topSpot = V2Rules.Center(6930, -785, 735);
        A(hops(TradeRoutes.PlanWalk(campSpot, nanzhangPoint)).Count == 0 && hops(TradeRoutes.PlanWalk(topSpot, nanzhangPoint)).Count == 1 &&
            hops(TradeRoutes.PlanWalk(campSpot, janganPoint)).Count == 1 && hops(TradeRoutes.PlanWalk(topSpot, janganPoint)).Count == 0,
            "A route restarted below the cliff goes on south, one restarted on top takes the teleport, and the reverse way mirrors it");
        A(!toNanzhang.Concat(fromNanzhang).Any(s => s.From != null && ExpeditionRules.Network.Links.Any(l => l.From == s.From.Id && l.To == s.To.Id && (s.To.Id == 252 || s.From.Id == 252))),
            "The Nanzhang road never uses the gate teleport");

        // ---- Nanzhang collection ----
        var world = NanzhangData.World;
        var gaoShan = world.npcs.FirstOrDefault(n => n.id == 60160);
        A(world.npcs.Count >= 90 && world.quests.Count >= 350 && gaoShan != null && gaoShan.name == "Ropemaster Gao Shan" && Math.Abs(gaoShan.x - 6940.5) < 2 && Math.Abs(gaoShan.y + 760.5) < 2,
            "The game's tables give the Nanzhang NPCs (outside the city too) and their quests");
        var matteo = world.quests.FirstOrDefault(q => q.code == "QNO_NZ_NL_MATTEO_1");
        A(matteo != null && matteo.npc == "Adventurer Matteo" && matteo.level == 90 && matteo.rewards == "Experience 4370000 / SP 749 / Gold 155000" &&
            matteo.mission.Contains("Dragon Turtle Scales") && matteo.HasReward && matteo.dialog.Count > 0 && matteo.npcId == 60418,
            "A quest carries its giver, mission, reward and conversation");
        A(world.quests.Count(q => q.HasReward) > 40 && world.quests.Any(q => !q.HasReward) && world.quests.All(q => q.npc.Length > 0 && q.title.Length > 0 && !q.rewards.Contains("<") && !q.rewards.Contains("&gt;")),
            "Quests with and without a reward are told apart and the texts are clean");
        A(world.npcs.Any(n => n.name == "Elder Gu Wen" && n.Located && Math.Abs(n.x - 3620) < 2) && world.npcs.Any(n => n.name == "Lord Dugu Jing" && n.Located) &&
            world.npcs.Any(n => n.name == "Cliff Warden Lu Feng" && !n.Located) && world.npcs.Any(n => n.name == "Treasurer Yan Qing" && !n.Located),
            "The land around the city is covered: Kuhe, Dugu Manor, the cliff and the Old Mining Camp (without a position where the tables give none)");
        var merged = MemorySelectV3Form.MergeTableNpcs(new List<MemorySelectV3Form.NpcMapRecord>());
        A(merged.Count == world.npcs.Count && merged.All(n => n.fromTable), "The table NPCs join the list unless the survey already has them");
        var seenNpcs = new List<CollectedNpc> { new CollectedNpc { Model = 5, X = 5900, Y = -1800 } };
        A(NanzhangData.Known(seenNpcs, 5, 5905, -1795) && !NanzhangData.Known(seenNpcs, 5, 5950, -1800) && !NanzhangData.Known(seenNpcs, 6, 5900, -1800),
            "An NPC is not collected twice, a different one nearby is");
        A(NanzhangData.InMap(V2Rules.Center(5900, -1800, 0)) && NanzhangData.InMap(V2Rules.Center(6300, -1300, 0)) && !NanzhangData.InMap(V2Rules.Center(6431, 1092, 0)) &&
            NanzhangData.InMap(V2Rules.Center(3620, -1650, 0)) && NanzhangData.InMap(V2Rules.Center(4521, -1033, 0)) && NanzhangData.InMap(V2Rules.Center(3300, -2900, 0)) &&
            NanzhangData.InMap(V2Rules.Center(6940, -760, 0)) && NanzhangData.InMap(V2Rules.Center(7500, -2000, 0)) && !NanzhangData.InMap(V2Rules.Center(1500, 1000, 0)),
            "The Nanzhang map covers the city, Hive Ridge with Kuhe, Dugu Manor, the Old Mining Camp, the cliff road and Eastern Crossing, not Jangan or Donwhang");

        // ---- Solo 85 trials (18 November update) ----
        var solo85 = V3Data.Dungeons.Where(d => d.Mode == DungeonLocation.Solo85).ToList();
        A(solo85.Count == 18 && solo85.Select(d => d.Code).Distinct().Count() == 18 && solo85.All(d => d.IsSolo && d.InstanceId >= 251 && d.InstanceId <= 268),
            "The 18 Solo 85 trials are in the catalog, one per dungeon");
        A(solo85.All(d => ExpeditionRules.Network.Gates.Any(g => g.Code == d.SoloGateCode && g.World == d.InstanceId) &&
            ExpeditionRules.Network.Links.Any(l => l.From == d.GateId && ExpeditionRules.Network.Gates.Any(g => g.Id == l.To && g.Code == d.SoloGateCode) && l.Level == 85)),
            "Every Solo 85 trial has its gate and a level 85 link from the dungeon's entrance gate");
        var soloD05 = V3Data.Dungeons.First(d => d.Code == "D05" && d.Mode == "Solo Trial");
        var solo85D05 = solo85.First(d => d.Code == "D05");
        A(soloD05.QueueKey == "D05" && solo85D05.QueueKey == "D05@85" && soloD05.SoloGateCode == "GATE_NE_GRIM_D05_S" && solo85D05.SoloGateCode == "GATE_NE_GRIM_D05_S85" &&
            FloorNavigation.LoadCaptured(solo85D05) != null && !V3Data.Dungeons.First(d => d.Code == "D05" && d.Mode == "Novice").IsSolo,
            "Solo 85 has its own run-order name and gate and uses the dungeon's captured floor");

        // ---- grim rooms: nothing is walked past ----
        var d05Trial = V3Data.Dungeons.First(d => d.Code == "D05" && d.Mode == "Solo Trial");
        var d05Floor = FloorNavigation.LoadCaptured(d05Trial);
        Func<uint, double, double, Monster> grimMob = (uid, x, y) => new Monster { Uid = uid, Name = "Grim Bunwang", Position = V2Rules.Center(x, y, 1000), Alive = true, Health = 57660u, Rank = 0 };
        var rooms1 = GrimRooms.Load(d05Trial, d05Floor);
        var offMarks = grimMob(11, 20, 4915);
        var farRoom = grimMob(12, 20, 5068);
        A(rooms1.Allows(offMarks) && !rooms1.Allows(farRoom),
            "A monster away from the marked spots still belongs to the nearest room and is fought, the room after next waits");
        var roomsB = GrimRooms.Load(d05Trial, d05Floor);
        var firstRoom = new[] { grimMob(21, -100, 4870), grimMob(22, -85, 4868), grimMob(23, -98, 4888) };
        roomsB.Observe(firstRoom);
        foreach (var dying in firstRoom)
            dying.Alive = false;
        roomsB.Observe(firstRoom);
        A(roomsB.Room == 1, "Three verified kills clear the room");
        var roomsC = GrimRooms.Load(d05Trial, d05Floor);
        var sweepFrom = d05Floor.Origin;
        int steps = 0;
        while (roomsC.Room == 0 && steps++ < 600)
        {
            if (roomsC.Next(sweepFrom) == null)
                break;
            roomsC.Arrived();
        }
        A(roomsC.Room == 1 && steps < 600, "An empty room is swept once and left, it is not swept again and again");

        // ---- automatic potions ----
        A(V2Rules.PotionOf(0x08ec) == V2Rules.Potion.Hp && V2Rules.PotionOf(0x08ed) == V2Rules.Potion.Hp && V2Rules.PotionOf(0x10ec) == V2Rules.Potion.Mp &&
            V2Rules.PotionOf(0x10ed) == V2Rules.Potion.Mp && V2Rules.PotionOf(0x18ec) == V2Rules.Potion.None && V2Rules.PotionOf(0x0eec) == V2Rules.Potion.None,
            "HP and MP potions are told apart by the item type code");
        var potionBag = new[]
        {
            new BagItem { Model = 9, Slot = 14, Quantity = 5, Type = 0x08ec }, new BagItem { Model = 8, Slot = 13, Quantity = 3, Type = 0x08ec },
            new BagItem { Model = 16, Slot = 15, Quantity = 2, Type = 0x10ec }, new BagItem { Model = 7100, Slot = 16, Quantity = 1, Type = 0x0eec },
            new BagItem { Model = 8, Slot = 17, Quantity = 0, Type = 0x08ec }
        };
        A(V2Rules.PickPotion(potionBag, V2Rules.Potion.Hp).Slot == 13 && V2Rules.PickPotion(potionBag, V2Rules.Potion.Mp).Slot == 15 &&
            V2Rules.PickPotion(new BagItem[0], V2Rules.Potion.Hp) == null && V2Rules.PickPotion(potionBag.Where(i => i.Slot > 14).ToArray(), V2Rules.Potion.Hp) == null,
            "The potion is the first one of its kind in the bag; an empty slot is never picked");
        A(V2Rules.AllowedItem(potionBag[0]) && !V2Rules.AllowedItem(potionBag[4]) && !V2Rules.AllowedItem(new BagItem { Model = 5, Slot = 20, Quantity = 1, Type = 0x2000 }),
            "Potions may be used, an empty slot and an unknown item may not");
        A(!new V3Settings().AutoHpPotion && !new V3Settings().AutoMpPotion && new V3Settings().HpPotionPercent == 50 && new V3Settings().MpPotionPercent == 30,
            "Automatic potions are off by default (the game's own macro may do it)");
        A(ItemCatalog.NameOf(5) == "Eşya #5", "An item without a name shows its id");

        // ---- the forced update check ----
        A(MemorySelectV3Form.UpdateCheckSeconds == 15 && MemorySelectV3Form.UpdateGraceSeconds >= 1 && MemorySelectV3Form.UpdateGraceSeconds <= 10,
            "A newer version is looked for every 15 seconds and installed within seconds of being found");

        // ---- rows are executed only through their key ----
        var keyed = new SkillRotation();
        keyed.Load(new[]
        {
            new SkillSlot { Key = "", Kind = SkillRotation.Buff_, SkillId = 11738, Name = "Healing Cycle" },
            new SkillSlot { Key = "", Kind = SkillRotation.Attack_, SkillId = 10258 },
            new SkillSlot { Key = "2", Kind = SkillRotation.Attack_, SkillId = 10129 },
            new SkillSlot { Key = "F5", Kind = SkillRotation.Buff_, SkillId = 4001 },
            new SkillSlot { Key = "??", Kind = SkillRotation.Attack_ },
            new SkillSlot { On = false, Key = "", Kind = SkillRotation.Attack_ }
        });
        A(keyed.KeylessLines == 3 && keyed.Keys().OrderBy(k => k).SequenceEqual(new ushort[] { '2', 0x74 }),
            "Enabled rows without a usable key are counted and never become keys; a switched-off row is not counted");
        A(keyed.NextAttack(0) == '2' && keyed.NextAttack(1) == '2' && keyed.NextAttack(2) == '2', "An attack row with a key is pressed by its key; keyless attack rows are skipped every time");
        A(keyed.Support(0, Ctx()) == 0x74 && keyed.Support(10, Ctx(buffs: new uint[] { 4001 })) == 0, "A buff row with a key is pressed when its buff is missing (the Buff ID only reads the buff list)");
        A(keyed.Support(100000, Ctx(buffs: new uint[] { 99 })) == 0x74, "...and again when the buff is gone; the keyless Healing Cycle row never comes up");
        var onlyKeyless = new SkillRotation();
        onlyKeyless.Load(new[] { new SkillSlot { Key = " ", Kind = SkillRotation.Attack_, SkillId = 10258 }, new SkillSlot { Key = "", Kind = SkillRotation.Heal_, Percent = 50 } });
        A(!onlyKeyless.HasAttack && onlyKeyless.KeylessLines == 2 && onlyKeyless.NextAttack(0) == 0 && onlyKeyless.Support(0, Ctx(hp: 10)) == 0 && onlyKeyless.Keys().Count == 0,
            "A list of keyless rows does nothing");
        A(SkillRotation.Validate(new[] { new SkillSlot { Key = "", SkillId = 10258 } }) == null && SkillRotation.Validate(new[] { new SkillSlot { Key = "", SkillId = 0 } }) == null,
            "A keyless row does not block a run (it is reported and skipped)");
        A(SkillRotation.Validate(new[] { new SkillSlot { Key = "??", SkillId = 5 } }) != null, "A wrong key is still refused");
        A(SkillRotation.NameOfKey(Keys.D1) == "1" && SkillRotation.NameOfKey(Keys.NumPad7) == "7" && SkillRotation.NameOfKey(Keys.Q) == "Q" && SkillRotation.NameOfKey(Keys.F10) == "F10" &&
            SkillRotation.NameOfKey(Keys.Back) == null && SkillRotation.NameOfKey(Keys.Tab) == null && SkillRotation.NameOfKey(Keys.ShiftKey) == null &&
            Enumerable.Range(0, 10).Select(i => (Keys)((int)Keys.D0 + i)).All(k => SkillRotation.ParseKey(SkillRotation.NameOfKey(k)) != 0),
            "A pressed key becomes a hotbar key name the rotation accepts (number row, number pad, letters, F keys); other keys are not captured");
        A(SkillRotation.NextFreeKey(new[] { "1", "2", "4", "" }) == "3" && SkillRotation.NextFreeKey(new string[0]) == "1" &&
            SkillRotation.NextFreeKey(new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" }) == "F1" && SkillRotation.NextFreeKey(new[] { "f1", "2" }) == "1",
            "A new row starts with the first hotbar key that is not used yet");

        // ---- old party buff rows are kept and never used ----
        var party = new SkillRotation();
        party.Load(new[]
        {
            new SkillSlot { Kind = SkillRotation.PartyBuff_, Key = "", SkillId = 9001, Seconds = 60, Members = "alice; Bob" },
            new SkillSlot { Kind = SkillRotation.PartyBuff_, Key = "9", SkillId = 9002, Members = "Carol" }
        });
        A(party.InertPartyLines == 2 && party.KeylessLines == 0 && party.Keys().Count == 0 && party.Support(0, Ctx()) == 0 && party.NextAttack(0) == 0,
            "Party buff rows are inert: no key is pressed for them, even when they carry one");
        A(SkillRotation.Validate(new[] { new SkillSlot { Kind = SkillRotation.PartyBuff_, Key = "", SkillId = 9001, Members = "Alice" } }) == null && !SkillRotation.Kinds.Contains(SkillRotation.PartyBuff_),
            "An old party buff row does not block a run, and the kind can no longer be chosen");
        A(typeof(SkillRotation).GetMethod("NextPartyBuff", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) == null &&
            typeof(SkillRotation).GetField("IdOnly", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public) == null, "The party buff and by-id runtime code is gone");

        // ---- old settings with skill ids still load, ids survive a save ----
        var legacy = new JavaScriptSerializer().Deserialize<V3Settings>("{\"SkillList\":true,\"SkillOptions\":{\"WeaponSwap\":true,\"CastPacePercent\":55},\"Skills\":[" +
            "{\"On\":true,\"Key\":\"\",\"Kind\":\"Saldırı\",\"SkillId\":10258,\"Name\":\"Flaming\"}," +
            "{\"On\":true,\"Key\":\"4\",\"Kind\":\"Buff\",\"SkillId\":11738,\"Seconds\":90}," +
            "{\"On\":true,\"Key\":\"\",\"Kind\":\"Parti buff\",\"SkillId\":9001,\"Members\":\"Alice;Bob\"}]}");
        A(legacy.Skills.Count == 3 && legacy.Skills[0].SkillId == 10258 && legacy.Skills[0].Key == "" && legacy.Skills[2].Kind == "Parti buff" && legacy.Skills[2].Members == "Alice;Bob" &&
            legacy.SkillOptions.CastPacePercent == 55 && legacy.SkillOptions.WeaponSwap, "A settings file with skill ids, keyless rows and party rows still loads");
        var again = new JavaScriptSerializer().Deserialize<V3Settings>(new JavaScriptSerializer().Serialize(legacy));
        A(again.Skills[0].SkillId == 10258 && again.Skills[1].SkillId == 11738 && again.Skills[2].Members == "Alice;Bob", "The ids and party names are kept when the settings are saved again");
        var legacyRotation = new SkillRotation();
        legacyRotation.Load(legacy.Skills, legacy.SkillOptions);
        A(legacyRotation.KeylessLines == 1 && legacyRotation.InertPartyLines == 1 && legacyRotation.Keys().SequenceEqual(new ushort[] { '4' }) && legacyRotation.NextAttack(0) == 0 && legacyRotation.Support(0, Ctx()) == '4',
            "Of the old rows only the keyed buff runs; the id-only attack is reported and the party buff is inert");

        // ---- the Skill page has no skill-id controls ----
        using (var page = (Form)Activator.CreateInstance(typeof(MemorySelectV3Form), true))
        {
            var texts = new List<string>();
            Action<Control> walk = null;
            walk = c =>
            {
                texts.Add(c.Text ?? "");
                var tab = c as TabControl;
                if (tab != null)
                    foreach (TabPage p in tab.TabPages)
                        texts.Add(p.Text);
                foreach (Control child in c.Controls)
                    walk(child);
            };
            walk(page);
            string all = string.Join("\n", texts);
            A(!texts.Contains("Karakterin skilleri") && !all.Contains("Kimlikle sına") && !all.Contains("Skilleri oku") && !all.Contains("Listeye ekle") && !all.Contains("Tüm skiller") &&
                !all.Contains("tuşsuz") && !all.Contains("kimlikle kullan"), "The Skill page has no character-skills tab, id test, skill scan or by-id texts");
            A(texts.Contains("Skill ve Buff") && texts.Contains("Seçenekler") && texts.Contains("Profil / şablon") && texts.Contains("Gelişmiş tablo") && texts.Contains("Nasıl çalışır"),
                "The other Skill page tabs are still there");
            var pageType = typeof(MemorySelectV3Form);
            var pageFlags = BindingFlags.Instance | BindingFlags.NonPublic;
            pageType.GetField("settings", pageFlags).SetValue(page, null);
            ((Timer)pageType.GetField("poll", pageFlags).GetValue(page)).Stop();
            var rowsSettings = new V3Settings { SkillList = true };
            rowsSettings.Skills.Add(new SkillSlot { Key = "", Kind = SkillRotation.Attack_, SkillId = 10258, Name = "kimlikli" });
            rowsSettings.Skills.Add(new SkillSlot { Key = "3", Kind = SkillRotation.PartyBuff_, SkillId = 9001, Members = "Alice" });
            pageType.GetMethod("RestoreSkillSettings", pageFlags).Invoke(page, new object[] { rowsSettings });
            var rows = (List<SkillSlot>)pageType.GetMethod("SkillSlotsFromGrid", pageFlags).Invoke(page, null);
            A(rows.Count == 2 && rows[0].Key == "" && rows[0].SkillId == 10258 && rows[1].Kind == SkillRotation.PartyBuff_ && rows[1].Members == "Alice" && rows[1].SkillId == 9001,
                "The page keeps keyless and party rows (and their ids) exactly as saved");
            var entry = pageType.GetMethod("EntryOf", pageFlags);
            var keyless = entry.Invoke(page, new object[] { 0 });
            var legacyEntry = entry.Invoke(page, new object[] { 1 });
            Func<object, string, object> read = (o, name) => o.GetType().GetField(name, pageFlags | BindingFlags.Public).GetValue(o);
            A((bool)read(keyless, "Warn") && ((string)read(keyless, "Detail")).Contains("TUŞ YOK") && (bool)read(legacyEntry, "Warn") && ((string)read(legacyEntry, "Detail")).Contains("kullanılmaz"),
                "A keyless row and an old party row are marked in the list (warning colour and text)");
        }
    }
}

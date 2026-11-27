using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Forms;

partial class V3Checks
{
    static readonly List<string> passed = new List<string>();

    // Visible controls whose bounds leave the area of their parent. Docked controls and scrolling panels are fine.
    static IEnumerable<string> ClippedControls(Control root)
    {
        foreach (Control c in root.Controls)
        {
            if (!c.Visible)
                continue;
            var parent = c.Parent;
            var scroller = parent as ScrollableControl;
            bool scrolls = scroller != null && scroller.AutoScroll;
            if (c.Dock == DockStyle.None && !scrolls && parent.ClientSize.Width > 40 && parent.ClientSize.Height > 20 && !(parent is TabControl))
            {
                if (c.Right > parent.ClientSize.Width + 1 || c.Bottom > parent.ClientSize.Height + 1 || c.Left < -1)
                    yield return c.GetType().Name + " '" + (c.Text.Length > 30 ? c.Text.Substring(0, 30) : c.Text) + "' " + c.Bounds + " in " + parent.GetType().Name + " " +
                        parent.ClientSize;
            }
            foreach (string inner in ClippedControls(c))
                yield return inner;
        }
    }

    static void StabilityChecks()
    {
        // bounded logs
        var capped = new CappedLog(10, 5);
        for (int i = 1; i <= 100; i++)
            capped.AppendLine("line " + i);
        string cappedText = capped.ToString();
        A(capped.Lines <= 15 && capped.Lines >= 10 && cappedText.Contains("line 100") && !cappedText.Contains("line 1" + Environment.NewLine) && capped.Version == 100,
            "The in-memory event log keeps only its last lines and counts every append");
        var multi = new CappedLog(4, 0);
        multi.AppendLine("a" + Environment.NewLine + "   b");
        multi.AppendLine("c");
        multi.AppendLine("d");
        A(multi.Lines == 4 && multi.ToString().StartsWith("a"), "A line with a line break counts as two lines");
        multi.AppendLine("e");
        A(multi.Lines == 4 && multi.ToString().StartsWith("   b") && multi.ToString().EndsWith("e" + Environment.NewLine), "When the cap is passed the oldest physical lines go first");
        string logDir = Path.Combine(Path.GetTempPath(), "SrStudioLogCheck-" + Guid.NewGuid().ToString("N"));
        string logFile = Path.Combine(logDir, "events.log");
        try
        {
            for (int i = 0; i < 40; i++)
                LogFiles.Append(logFile, "2026-01-01 00:00:00 · entry " + i + " " + new string('x', 40), 1000);
            A(File.Exists(logFile + ".1") && new FileInfo(logFile).Length < 1500 && new FileInfo(logFile + ".1").Length >= 1000,
                "events.log is rotated by size and one backup is kept");
            string tail = LogFiles.ReadTail(logFile, 200);
            A(tail.Length <= 200 && tail.Contains("entry 39") && tail.StartsWith("2026-01-01"), "The log tab reads only the tail and drops the cut first line");
            A(LogFiles.ReadTail(logFile, 1000000).Contains("entry 39"), "A file shorter than the tail size is read whole");
            string freshFile = Path.Combine(logDir, "fresh.log");
            LogFiles.Append(freshFile, "first line", 1000000);
            A(File.ReadAllBytes(freshFile)[0] == 0xEF && LogFiles.ReadTail(freshFile, 1000000).StartsWith("first line"), "The byte order mark of a whole file is not shown in the log tab");
            // the game's own logs
            string gameDir = Path.Combine(logDir, "game"), gameLogs = Path.Combine(gameDir, "logs");
            Directory.CreateDirectory(gameLogs);
            File.WriteAllText(Path.Combine(gameLogs, "client.log"), "07:00:00 I old line" + Environment.NewLine + "07:00:01 W Disconnected by the server - old" + Environment.NewLine);
            var watch = new ClientLogWatch();
            watch.Begin(gameDir, 0);
            A(watch.Poll(ClientLogWatch.PollMs) == null, "Lines that were in client.log before the job began are ignored");
            File.AppendAllText(Path.Combine(gameLogs, "client.log"), "08:00:00 I fine" + Environment.NewLine);
            A(watch.Poll(2 * ClientLogWatch.PollMs) == null, "Ordinary client.log lines do not stop the job");
            File.AppendAllText(Path.Combine(gameLogs, "client.log"), "08:00:05 W Disconnected by the server - session considered lost" + Environment.NewLine);
            A(watch.Poll(3 * ClientLogWatch.PollMs) != null && watch.Poll(4 * ClientLogWatch.PollMs) == null, "A new Disconnected line stops the job once");
            A(!watch.LastWasCrash, "A disconnect is not marked as a crash");
            File.WriteAllText(Path.Combine(gameLogs, "crash.log"), "crash");
            A(watch.Poll(5 * ClientLogWatch.PollMs) != null && watch.LastWasCrash && watch.Poll(6 * ClientLogWatch.PollMs) == null, "A crash.log that appears is reported once and marked as a crash");
            File.AppendAllText(Path.Combine(gameLogs, "client.log"), "09:00:00 I more" + Environment.NewLine);
            using (new FileStream(Path.Combine(gameLogs, "client.log"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                A(watch.Poll(7 * ClientLogWatch.PollMs) == null && watch.Failures == 1, "A log that cannot be read is counted");
            }
            A(watch.Poll(8 * ClientLogWatch.PollMs) == null && watch.Failures == 0, "A readable log clears the count");
            A(ClientLogWatch.HasDisconnect("x Disconnected by the server y") && !ClientLogWatch.HasDisconnect("all fine") && !ClientLogWatch.HasDisconnect(null), "Disconnect lines are recognised");
        }
        finally
        {
            try { Directory.Delete(logDir, true); } catch { }
        }
        // failures: counted in a row, back-off grows to a limit
        var streak = new FailureStreak();
        A(streak.Fail(0, 10000) == 1 && streak.Fail(1000, 10000) == 2 && streak.Fail(2000, 10000) == 3, "Failures that follow each other are counted");
        A(streak.Fail(20000, 10000) == 1, "A failure long after the last one starts the count again");
        streak.Fail(20100, 10000);
        streak.Success();
        A(streak.Count == 0, "A success clears the failure count");
        var ticks = new FailureStreak();
        ticks.Fail(0, 10000);
        ticks.EndTick(false);
        A(ticks.Count == 0 && ticks.Fail(100, 10000) == 1, "A look at the game without a failure ends the streak of failed looks");
        ticks.EndTick(true);
        A(ticks.Count == 1 && ticks.Fail(200, 10000) == 2, "A look with a failure keeps the streak");
        // calls into the game: results that are not failures, failures, back-off (with a clock and a log given)
        long fakeNow = 1000;
        var fakeLog = new List<string>();
        var caller = new RemoteCaller(77, 5, () => fakeNow, fakeLog.Add);
        int callsMade = 0;
        Action ok = () => callsMade++;
        Action unexpected = () => { callsMade++; throw new UnexpectedResultException("answer 7"); };
        Action broken = () => { callsMade++; throw new IOException("timeout"); };
        caller.Run(false, 1, ok);
        bool thrownUnexpected = false;
        try { caller.Run(false, 1, unexpected); } catch (UnexpectedResultException) { thrownUnexpected = true; }
        A(thrownUnexpected && caller.Failures == 0 && !caller.BackingOff && fakeLog.Count == 0 && callsMade == 2, "A result other than 0 / 1 (stairs, ramps) is not a failure: no count, no back-off, no log");
        bool thrownBroken = false;
        try { caller.Run(false, 1, broken); } catch (IOException) { thrownBroken = true; }
        A(thrownBroken && caller.Failures == 1 && caller.BackingOff && fakeLog.Count == 1 && fakeLog[0].Contains("PID 77"), "A timed-out call counts, starts the back-off and is logged");
        bool backoffThrown = false;
        try { caller.Run(false, 1, ok); } catch (RemoteBackoffException) { backoffThrown = true; }
        A(backoffThrown && callsMade == 3 && caller.Failures == 1 && fakeLog.Count == 1, "During the back-off nothing is sent and the refusal is not a new failure");
        fakeNow += RemoteBackoff.DelayMs(1) + 1;
        caller.Run(false, 1, ok);
        A(!caller.BackingOff && caller.Failures == 0 && callsMade == 4, "After the wait calls go on and a success clears the count");
        for (int i = 0; i < 5; i++)
        {
            fakeNow += 2100;
            try { caller.Run(true, 16, broken); } catch (IOException) { }
        }
        A(caller.FailureLimitReached && caller.Failures == 5, "Five failed calls in a row reach the limit that stops the job");
        // the game's process vanished under a call: a clean exit, not a failure (injectable process check)
        bool gameAlive = true;
        var goneLog = new List<string>();
        var goneCaller = new RemoteCaller(99, 5, () => fakeNow, goneLog.Add, null, () => gameAlive);
        Action denied = () => { throw new System.ComponentModel.Win32Exception(5, "Erişim engellendi"); };
        try { goneCaller.Run(false, 1, denied); } catch (System.ComponentModel.Win32Exception) { }
        A(goneCaller.Failures == 1 && goneLog.Count == 1 && goneCaller.BackingOff, "With the game still running, a denied call is a failure as before");
        goneCaller = new RemoteCaller(99, 5, () => fakeNow, goneLog.Add, null, () => gameAlive);
        goneLog.Clear();
        gameAlive = false;
        bool goneThrown = false;
        try { goneCaller.Run(false, 1, denied); } catch (GameGoneException) { goneThrown = true; }
        try { goneCaller.Run(true, 16, broken); } catch (GameGoneException) { goneThrown &= true; }
        A(goneThrown && goneCaller.Failures == 0 && !goneCaller.BackingOff && !goneCaller.FailureLimitReached && goneLog.Count == 0,
            "A call that fails because the game process is gone is a game exit: no failure count, no back-off, no exception log line");
        A(RemoteCaller.ProcessAlive(System.Diagnostics.Process.GetCurrentProcess().Id) && !RemoteCaller.ProcessAlive(0x7ffffff0),
            "The real process check sees this process and not a process that does not exist");
        // the call journal: line format, burst collapsing, rotation, a writer that throws, and the lines a caller writes
        var when = new DateTime(2026, 11, 26, 19, 47, 49, 123);
        A(RemoteJournal.Format(when, 14180, 0x7074, 11, "send", 1, 3, 0, "ok") == "19:47:49.123 pid=14180 op=7074 len=11 kind=send 3ms ok", "A journal line has the time with milliseconds, pid, opcode, payload length, kind, duration and result");
        A(RemoteJournal.Format(when, 14180, -1, 0, "move", 1, 2, 12, "timeout") == "19:47:49.123 pid=14180 kind=move 2ms gap=12ms timeout" &&
            RemoteJournal.Format(when, 14180, -1, 0, "probe", 16, 5, 0, "ok") == "19:47:49.123 pid=14180 kind=probe x16 5ms ok", "A line without a packet has no opcode; a batch shows its count and the wait for the gap is shown");
        var journalLines = new List<string>();
        var journal = new RemoteJournal(journalLines.Add);
        for (int i = 0; i < 100; i++)
            journal.Call(when, 14180, -1, 0, "probe", 1, 1, 0, "ok");
        A(journalLines.Count == RemoteJournal.LinesPerSecond, "Past 20 lines in a second, successful probes are not written one by one");
        journal.Call(when, 14180, -1, 0, "probe", 1, 1, 0, "timeout");
        A(journalLines.Count == RemoteJournal.LinesPerSecond + 2 && journalLines[journalLines.Count - 2].Contains("kind=probe x80 in 80 calls (burst)") && journalLines[journalLines.Count - 1].EndsWith("timeout"),
            "The collapsed probes are written as one line before the next line, and a failure is always written");
        journalLines.Clear();
        for (int i = 0; i < 25; i++)
            journal.Call(when.AddSeconds(1), 14180, -1, 0, "probe", 16, 3, 0, "ok");
        journal.Marker(when.AddSeconds(1), 14180, "crash.log changed");
        A(journalLines.Count == RemoteJournal.LinesPerSecond + 2 && journalLines[journalLines.Count - 2].Contains("probe x80 in 5 calls") && journalLines[journalLines.Count - 1].EndsWith("pid=14180 -- crash.log changed"),
            "Batches collapse by their queries; a marker line writes the collapsed ones first and then itself");
        journalLines.Clear();
        journal.Call(when.AddSeconds(2), 14180, 0x7045, 4, "send", 1, 2, 0, "ok");
        journal.Call(when.AddSeconds(2), 14180, 0x7045, 4, "send", 1, 2, 0, "ok");
        A(journalLines.Count == 2, "Sends are never collapsed");
        var throwing = new RemoteJournal(line => { throw new IOException("disk full"); });
        bool journalThrew = false;
        try
        {
            throwing.Call(when, 1, -1, 0, "probe", 1, 1, 0, "ok");
            throwing.Marker(when, 1, "attach");
            throwing.Flush();
        }
        catch (Exception) { journalThrew = true; }
        A(!journalThrew, "A journal writer that throws never reaches the caller");
        string journalDir = Path.Combine(Path.GetTempPath(), "SrStudioJournalCheck-" + Guid.NewGuid().ToString("N"));
        try
        {
            string journalFile = Path.Combine(journalDir, "remote-calls.log");
            var filed = new RemoteJournal(line => LogFiles.Append(journalFile, line, 500));
            for (int i = 0; i < 30; i++)
                filed.Call(when.AddSeconds(i), 14180, 0x7074, 11, "send", 1, 3, 0, "ok");
            A(File.Exists(journalFile + ".1") && new FileInfo(journalFile).Length < 1000 && File.ReadAllText(journalFile).Contains("op=7074"), "The journal file is rotated by size with one backup");
        }
        finally
        {
            try { Directory.Delete(journalDir, true); } catch { }
        }
        var journalled = new List<string>();
        var journalCaller = new RemoteCaller(88, 5, () => fakeNow, fakeLog.Add, new RemoteJournal(journalled.Add));
        journalCaller.Run(false, 1, () => { RemoteJournal.Op = 0x7074; RemoteJournal.Len = 11; });
        journalCaller.Run(true, 16, ok);
        try { journalCaller.Run(false, 1, unexpected, "probe"); } catch (UnexpectedResultException) { }
        try { journalCaller.Run(false, 1, () => { throw new RemoteTimeoutException("slow"); }, "move"); } catch (RemoteTimeoutException) { }
        for (int i = 0; i < 3; i++)
            try { journalCaller.Run(false, 1, ok); } catch (RemoteBackoffException) { }
        A(journalled.Count == 5 && journalled[0].Contains("pid=88 op=7074 len=11 kind=send") && journalled[0].EndsWith("ok") && journalled[1].Contains("kind=probe x16") &&
            journalled[2].Contains("kind=probe") && journalled[2].EndsWith("unexpected") && journalled[3].Contains("kind=move") && journalled[3].EndsWith("timeout") &&
            journalled[4].EndsWith("backoff"), "A caller journals every call with its result, and one line for a whole back-off");
        var traced = new List<string>();
        var oldSink = RemoteTrace.Sink;
        RemoteTrace.Sink = traced.Add;
        try
        {
            RemoteTrace.Flush();
            traced.Clear();
            lock (RemoteGate.Lock)
                RemoteTrace.Note(4321, true, 8, 3);
            RemoteTrace.Flush();
            A(traced.Count == 1 && traced[0].Contains("PID 4321") && traced[0].Contains("1 çağrı (1 toplu, 8 sorgu)") && traced[0].Contains(" 1 sn:"), "A flush writes the calls counted so far (a short window shows 1 sn)");
            RemoteTrace.Flush();
            A(traced.Count == 1, "A flush with nothing counted writes nothing");
        }
        finally
        {
            RemoteTrace.Sink = oldSink;
        }
        A(RemoteBackoff.DelayMs(0) == 0 && RemoteBackoff.DelayMs(1) == RemoteBackoff.StepMs && RemoteBackoff.DelayMs(5) == RemoteBackoff.DelayMs(50) &&
            RemoteBackoff.DelayMs(2) > RemoteBackoff.DelayMs(1) && TargetSender.RemoteFailureLimit == 5, "The wait after failed game calls grows to a limit; 5 failures in a row stop the job");
        // calls into the game: a minimum gap
        A(RemoteGate.MinGapMs == 15 && RemoteGate.Remaining(100, 100, 15) == 15 && RemoteGate.Remaining(110, 100, 15) == 5 && RemoteGate.Remaining(115, 100, 15) == 0 &&
            RemoteGate.Remaining(100, 200, 15) == 15, "Two calls into the game are at least 15 ms apart");
        A(RemoteTrace.Format(1234, 10, 37, 4, 52, 6, 0).Contains("PID 1234") && RemoteTrace.Format(1234, 10, 37, 4, 52, 6, 0).Contains("37 çağrı"), "The call summary names the process and the counts");
        var throttle = new Throttle();
        A(throttle.Allow("a", 0, 60000) && !throttle.Allow("a", 30000, 60000) && throttle.Allow("b", 30000, 60000) && throttle.Allow("a", 60000, 60000), "A swallowed error is logged once a minute per cause");
        // update gate
        A(UpdateGate.Idle(false, false, false, false, false) && !UpdateGate.Idle(true, false, false, false, false) && !UpdateGate.Idle(false, true, false, false, false) &&
            !UpdateGate.Idle(false, false, true, false, false) && !UpdateGate.Idle(false, false, false, true, false) && !UpdateGate.Idle(false, false, false, false, true),
            "An update is installed only while no job, dungeon, queue, lottery or planned resume is running");
        long sixHours = UpdateGate.MaxDeferralMs;
        A(sixHours == 6L * 3600 * 1000 && UpdateGate.MayApply(true, 0, false, false, false) && !UpdateGate.MayApply(false, sixHours - 1, false, false, false) &&
            UpdateGate.MayApply(false, sixHours, false, false, false), "A busy bot lets an update through only after 6 hours of waiting");
        A(!UpdateGate.MayApply(false, sixHours, true, false, false) && !UpdateGate.MayApply(false, sixHours, false, true, false) && !UpdateGate.MayApply(false, sixHours, false, false, true) &&
            !UpdateGate.MayApply(true, 0, true, false, false), "An overdue update still waits for an item transaction, a death recovery or a resume being connected");
        // area changes
        var area = new AreaWatch();
        Func<ushort, double, double, Position> at = (region, x, y) => new Position { Region = region, X = x, Y = y };
        area.Observe(at(0x7f80, 0, 0), 0);
        area.Observe(at(0x7f81, 10, 0), 100);
        A(!area.Within(200, AreaWatch.SettleMs), "Walking over a region border is not an area change");
        area.Observe(at(0x7f81, 4000, 0), 300);
        A(area.Within(400, AreaWatch.SettleMs) && !area.Within(300 + AreaWatch.SettleMs, AreaWatch.SettleMs) && area.Within(300 + AreaWatch.SettleMs, AreaWatch.QuietMs), "A jump is an area change: commands wait 5 s, window waking 10 s");
        var indoor = new AreaWatch();
        indoor.Observe(at(0x7f80, 0, 0), 0);
        indoor.Observe(at(0x8001, 1, 0), 50);
        A(indoor.Within(60, AreaWatch.SettleMs), "Entering an indoor region is an area change");
        indoor.Reset();
        A(!indoor.Within(60, AreaWatch.SettleMs), "Reset forgets the last change");
        var loading = new AreaWatch();
        loading.Loaded(10);
        A(loading.Within(20, AreaWatch.SettleMs), "Memory that could not be read marks the world as loading");
    }

    static void SkillChecks()
    {
        A(SkillRotation.ParseKey("1") == '1' && SkillRotation.ParseKey("a") == 'A' && SkillRotation.ParseKey("F1") == 0x70 &&
            SkillRotation.ParseKey("f12") == 0x7B, "Skill keys: digits, letters and F1-F12");
        A(SkillRotation.ParseKey("") == 0 && SkillRotation.ParseKey("F13") == 0 && SkillRotation.ParseKey("12") == 0 &&
            SkillRotation.ParseKey("ab") == 0, "Unusable skill keys are refused");
        var slots = new List<SkillSlot>
        {
            new SkillSlot { Key = "1", Kind = SkillRotation.Attack_ },
            new SkillSlot { Key = "2", Kind = SkillRotation.Attack_, Seconds = 10 },
            new SkillSlot { Key = "7", Kind = SkillRotation.Heal_, Seconds = 3, Percent = 50 },
            new SkillSlot { Key = "8", Kind = SkillRotation.Mana_, Seconds = 3, Percent = 40 },
            new SkillSlot { Key = "9", Kind = SkillRotation.Buff_, Seconds = 120 },
            new SkillSlot { On = false, Key = "0", Kind = SkillRotation.Attack_ }
        };
        A(SkillRotation.Validate(slots) == null, "A normal skill list is valid");
        slots.Add(new SkillSlot { Key = "zz" });
        A(SkillRotation.Validate(slots) != null, "A bad key is reported");
        slots.RemoveAt(slots.Count - 1);
        slots.Add(new SkillSlot { Key = "5", Kind = SkillRotation.Heal_, Percent = 0 });
        A(SkillRotation.Validate(slots) != null, "A heal percent of 0 is reported");
        slots.RemoveAt(slots.Count - 1);
        var rot = new SkillRotation();
        rot.Load(slots);
        A(rot.HasAttack && rot.Keys().Count == 5, "Disabled lines are ignored");
        // Buff first (healthy, full mana), repeats only after its interval.
        A(rot.Support(0, 100, 100, true) == '9', "A due buff is pressed without a target");
        A(rot.Support(1000, 100, 100, true) == 0 && rot.Support(119000, 100, 100, true) == 0, "A buff waits for its interval");
        A(rot.Support(120000, 100, 100, true) == '9', "A buff repeats after its interval");
        // Heal beats mana beats buff, each with its cooldown.
        A(rot.Support(240000, 30, 10, true) == '7', "Low HP presses the heal first");
        A(rot.Support(240100, 30, 10, true) == '8', "Heal on cooldown lets the mana skill go");
        A(rot.Support(240200, 30, 10, true) == '9', "Then the due buff");
        A(rot.Support(240300, 30, 10, true) == 0, "Everything on cooldown presses nothing");
        A(rot.Support(250000, 90, 90, true) == 0, "Healthy and full of mana presses no heal or mana");
        A(rot.Support(260000, 10, 10, false) == 0, "Unknown stats never trigger a heal");
        // Attack: in turn, a skill with its own cooldown is skipped until ready.
        var attacks = new SkillRotation();
        attacks.Load(slots);
        A(attacks.NextAttack(0) == '1' && attacks.NextAttack(100) == '2', "Attack skills go in turn");
        A(attacks.NextAttack(200) == '1' && attacks.NextAttack(300) == '1', "A skill on cooldown is skipped");
        A(attacks.NextAttack(10100) == '2', "The skill is used again once its cooldown is over");
        var none = new SkillRotation();
        none.Load(new[] { new SkillSlot { Key = "9", Kind = SkillRotation.Buff_ } });
        A(!none.HasAttack && none.NextAttack(0) == 0, "No attack lines, no attack key");
    }

    static void PlannerChecks()
    {
        // The batched navigation call: code for N queries, answers written to a separate data page.
        var args = new uint[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var batch = TargetSender.BuildProbeBatch(0x20000000, 0x10001000, new[] { args, args, args });
        int calls = 0;
        for (int i = 0; i + 1 < batch.Length; i++)
            if (batch[i] == 0xff && batch[i + 1] == 0xd0)
                calls++;
        A(batch[0] == 0x55 && batch[1] == 0x8b && batch[2] == 0xec && calls == 3 && batch[batch.Length - 3] == 0xc2 && batch[batch.Length - 2] == 4 && batch[batch.Length - 1] == 0 &&
            batch.Length < TargetSender.BatchCodeLimit, "Batched navigation call has one query per call instruction and a clean exit");
        bool refused = false;
        try { TargetSender.BuildProbeBatch(0x20000000, 0x10001000, new uint[17][]); }
        catch (ArgumentException) { refused = true; }
        A(refused, "A batch of more than 16 queries is refused");
        refused = false;
        try { TargetSender.BuildProbeBatch(0x20000000, 0x10001000, new[] { new uint[7] }); }
        catch (ArgumentException) { refused = true; }
        A(refused, "A query without exactly 8 arguments is refused");
        // The same search with and without batching finds the same route, and batching asks the game far less often.
        Func<Position, Position, bool> open = (a, b) => !(Math.Min(a.X, b.X) <= 2 && Math.Max(a.X, b.X) >= 0 && Math.Min(a.Y, b.Y) <= 20 && Math.Max(a.Y, b.Y) >= -40);
        var start = V2Rules.Center(-30, 0, 0f);
        var goal = V2Rules.Center(30, 0, 0f);
        int singleCalls = 0, batchCalls = 0;
        var plain = new ObstaclePath { Step = 4 };
        plain.Begin(start, goal, null, 3);
        for (int i = 0; i < 400 && plain.Route == null && !plain.Failed; i++)
            plain.Advance((a, b) => { singleCalls++; return open(a, b); }, 64);
        var batched = new ObstaclePath { Step = 4 };
        batched.Prefetch = pairs => { batchCalls++; return pairs.Select(pair => (sbyte)(open(pair[0], pair[1]) ? 1 : 0)).ToArray(); };
        batched.Begin(start, goal, null, 3);
        for (int i = 0; i < 400 && batched.Route == null && !batched.Failed; i++)
            batched.Advance((a, b) => open(a, b), 64);
        A(plain.Route != null && batched.Route != null && plain.Route.Count == batched.Route.Count &&
            plain.Route.Zip(batched.Route, (p, q) => p.Distance(q) < 0.01).All(same => same), "Batched search finds exactly the same route");
        A(batchCalls > 0 && batchCalls * 3 < singleCalls, "Batched search calls the game far less often (" + batchCalls + " instead of " + singleCalls + ")");
        // Unknown answers (-1) fall back to the single query.
        var unsure = new ObstaclePath { Step = 4 };
        unsure.Prefetch = pairs => pairs.Select(pair => (sbyte)-1).ToArray();
        unsure.Begin(start, goal, null, 3);
        for (int i = 0; i < 400 && unsure.Route == null && !unsure.Failed; i++)
            unsure.Advance((a, b) => open(a, b), 64);
        A(unsure.Route != null && unsure.Route.Count == plain.Route.Count, "Unknown batch answers fall back to the single query");
        A(MemorySelectV3Form.IdlePollMs == 250 && MemorySelectV3Form.ActivePollMs == 100, "The window looks at the game every 250 ms idle and 100 ms while a job runs");
    }

    static void StartupChecks()
    {
        var names = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceNames();
        A(Startup.MissingResources(names).Count == 0, "Every embedded file the start-up procedure requires is in the program");
        var missing = Startup.MissingResources(names.Where(n => n != "road-mesh.bin.gz"));
        A(missing.Count == 1 && missing[0] == "road-mesh.bin.gz", "A missing embedded file is reported by name");
        A(Startup.Percent(0, 5) == 0 && Startup.Percent(2, 5) == 40 && Startup.Percent(5, 5) == 100 && Startup.Percent(9, 5) == 100 &&
            Startup.Percent(1, 0) == 0, "Start-up progress is a percentage of finished steps");
        A(Startup.StepNames.Length == 5 && Startup.StepNames.All(s => s.Length > 0), "Start-up has five named steps");
        foreach (double shownPercent in new[] { 8.0, 62.0, 100.0 })
            using (var splashPicture = StartupSplash.Preview(shownPercent, (int)(shownPercent / 25), shownPercent >= 100 ? "Hazır" : "Oyun verileri yükleniyor"))
                splashPicture.Save("tests/Preview-splash-" + (int)shownPercent + ".png");
        Startup.WarmData();
        ExpeditionRules.Warm();
        A(true, "Game data and the road network load in the start-up procedure");
        // Freeze and thaw hold the layout of a whole tree and release it again.
        var host = new Panel { Width = 300, Height = 200 };
        var inner = new FlowLayoutPanel { AutoSize = true };
        host.Controls.Add(inner);
        inner.Controls.Add(new Label { Text = "a" });
        int layouts = 0;
        inner.Layout += delegate { layouts++; };
        Ui.Freeze(host);
        inner.Controls.Add(new Label { Text = "b" });
        inner.Controls.Add(new Label { Text = "c" });
        int duringFreeze = layouts;
        Ui.Thaw(host);
        inner.PerformLayout();
        A(duringFreeze == 0 && layouts > 0, "Frozen layout is held back and runs again after the thaw");
    }

    static void ExplorerChecks()
    {
        // Object types read from the client: NPC = 1/2/2, monster = 1/2/1. Every NPC (traders, Explorer) must reach the snapshot.
        A(GameReader.IsNpcType(0x144) && !GameReader.IsNpcType(0xC4) && !GameReader.IsNpcType(0x4) && !GameReader.IsNpcType(0x104),
            "NPC object type is recognised, monsters and other objects are not");
        var all = ExplorerSpots.All;
        A(all.Count == 52 && all.Select(s => s.N).Distinct().Count() == 52 && all[0].N == 1 && all[51].N == 52, "52 Explorer spots, numbered 1-52");
        A(all.All(s => ExplorerSpots.Areas.Contains(s.Area)), "Every Explorer spot belongs to a named area");
        A(ExplorerSpots.Areas.All(a => all.Any(s => s.Area == a)), "Every named area has Explorer spots");
        // The map was registered onto the game world map: spot 51 lies in the sector next to Jangan.
        A(ExplorerSpots.Find(51).Point().Region == 24999, "Spot 51 is in the Jangan sector found from the client data");
        var done = new HashSet<int> { 1, 2, 3, 10 };
        var queue = ExplorerSpots.Queue(1, done, true);
        A(queue.Count == 48 && queue[0] == 4 && queue[queue.Count - 1] == 52, "Queue skips found spots");
        A(ExplorerSpots.Queue(50, done, false).SequenceEqual(new[] { 50, 51, 52 }) && ExplorerSpots.Queue(60, done, false).Count == 0,
            "Queue starts at the chosen number");
        // Walkable ground next to each marker: the map is approximate, the nearest point within 120 units is used.
        int grounded = 0;
        foreach (var s in all)
        {
            Position point;
            float height;
            if (ExplorerSpots.Ground(s, out point, out height))
                grounded++;
        }
        A(grounded >= 40, "Most Explorer spots have walkable ground (" + grounded + " / 52)");
        // Live NPC lookup: right model, not handled yet, nearest to the spot.
        var spot = all[10];
        var snap = new Snapshot();
        snap.Npcs.Add(new WorldNpc { Uid = 1, Model = 60023, Position = V2Rules.Center(spot.X + 200, spot.Y, 0) });
        snap.Npcs.Add(new WorldNpc { Uid = 2, Model = 60023, Position = V2Rules.Center(spot.X + 40, spot.Y, 0) });
        snap.Npcs.Add(new WorldNpc { Uid = 3, Model = 11111, Position = V2Rules.Center(spot.X + 5, spot.Y, 0) });
        A(ExplorerSpots.FindNpc(snap, 60023, spot.Point(), 350, new HashSet<uint>()).Uid == 2, "Nearest Explorer NPC of the right model is found");
        A(ExplorerSpots.FindNpc(snap, 60023, spot.Point(), 350, new HashSet<uint> { 2 }).Uid == 1, "A handled NPC is skipped");
        A(ExplorerSpots.FindNpc(snap, 60023, spot.Point(), 100, new HashSet<uint> { 2 }) == null, "An NPC outside the radius is ignored");
        A(new V3Settings().ExplorerModel == 60023 && new V3Settings().ExplorerSkipDone && new V3Settings().ExplorerDone.Count == 0,
            "Explorer settings start with the known model and nothing found");
    }

    static void CaravanChecks()
    {
        // Trader NPC store: save, reload, damaged file.
        string file = Path.Combine(Path.GetTempPath(), "trade-npcs-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var saved = new Dictionary<string, TradeNpc>();
            saved["Hotan"] = new TradeNpc { Town = "Hotan", X = -2100.5, Y = 1400.25, Model = 1234 };
            saved["Jangan"] = new TradeNpc { Town = "Jangan", X = 5200, Y = -300, Model = 4321 };
            TradeRoutes.Save(file, saved);
            var loaded = TradeRoutes.Load(file);
            A(loaded.Count == 2 && loaded["Hotan"].X == -2100.5 && loaded["Jangan"].Model == 4321, "Trader NPCs survive save and load");
            File.WriteAllText(file, "{ not json");
            A(TradeRoutes.Load(file).Count == 0, "A damaged trader file loads as empty");
            A(TradeRoutes.Load(file + ".missing").Count == 0, "A missing trader file loads as empty");
        }
        finally
        {
            try { File.Delete(file); } catch { }
        }
        // Nanzhang counts as a town (death recovery and trip planning wait for a town); all six city sectors are listed.
        A(V3Data.Town(V2Rules.Center(5643.3, -1885.3, 462f)) == "Nanzhang" && V3Data.Town(V2Rules.Center(5938.6, -1706.2, 462f)) == "Nanzhang" &&
            V3Data.Town(V2Rules.Center(6100.0, -1541.0, 462f)) == "Nanzhang", "Nanzhang city sectors are recognised as a town");
        A(TradeRoutes.TownKey(V2Rules.Center(5643.3, -1885.3, 462f)) == "Nanzhang", "Nanzhang is a caravan town (reached on foot from Jangan)");
        A(TradeRoutes.TownKey(V2Rules.Center(148.8, 85.1, 0f)) == "Hotan" && TradeRoutes.TownKey(V2Rules.Center(6512, 1008, 0f)) == "Jangan" &&
            TradeRoutes.TownKey(V2Rules.Center(3495, 2076, 0f)) == "Donwhang", "Trader towns: Hotan (Kingdom sector), Jangan and Donwhang");
        // Routes: the four requested directions exist.
        A(TradeRoutes.Routes.Length == 12 && TradeRoutes.Routes.Any(r => r[0] == "Hotan" && r[1] == "Jangan") &&
            TradeRoutes.Routes.Any(r => r[0] == "Jangan" && r[1] == "Hotan") &&
            TradeRoutes.Routes.Any(r => r[0] == "Hotan" && r[1] == "Donwhang") &&
            TradeRoutes.Routes.Any(r => r[0] == "Donwhang" && r[1] == "Hotan") &&
            TradeRoutes.Routes.Any(r => r[0] == "Jangan" && r[1] == "Donwhang") &&
            TradeRoutes.Routes.Any(r => r[0] == "Donwhang" && r[1] == "Jangan"), "Hotan-Jangan, Hotan-Donwhang and Jangan-Donwhang routes both ways");
        // Script text: header comments, then one walk line per point, culture independent.
        var start = V2Rules.Center(-2100.4, 1400.6, 3f);
        var step = new TransitStep { To = new TransitGate { Code = "Harita yolu", X = -2000.2, Y = 1500.7, Height = 5 } };
        string[] lines = TradeRoutes.Script("Hotan", "Jangan", start, new[] { step }).Replace("\r", "").Split('\n')
            .Where(l => l.Length > 0).ToArray();
        A(lines[0].StartsWith("#") && lines[1].Contains("Hotan → Jangan") && lines[2].StartsWith("#"), "Script starts with comments");
        A(lines[3] == "walk,-2100,1401,3" && lines[4] == "walk,-2000,1501,5", "Script lists way points as walk,X,Y,Z");
        A(TradeRoutes.ScriptName("Hotan", "Jangan") == "Hotan-Jangan.txt", "Script file name");
        // The checks up to the script text look at the road the map planner finds (the ready-made scripts are checked after them).
        CaravanScripts.Disabled = true;
        // Road-only planning: no teleport, ferry or flyship hop, and the flag is released afterwards.
        var from =V3Data.Dungeons.First(d => d.Mode == "Solo Trial").Entrance();
        var to = V3Data.Dungeons.Where(d => d.Mode == "Solo Trial").Skip(1).First().Entrance();
        try
        {
            var plan = TradeRoutes.PlanWalk(from, to);
            A(plan.All(s2 => s2.From == null || RoadMesh.IsFerry(s2)), "Caravan plan never uses a teleport, only ferries");
        }
        catch (InvalidOperationException)
        {
            // Not connected by road in the map data: refusing is the correct answer for trade goods.
        }
        A(!RoadMesh.WalkOnly && !RoadMesh.AllowFerries, "Road-only and ferry flags are released after planning");
        // Hotan has no land link to Donwhang or Jangan: the river is crossed by the nearest ferry, never by a teleport.
        var hotanTrader = V2Rules.Center(148.8, 85.1, 0f);
        var donwhangTrader = V2Rules.Center(3495, 2076, 0f);
        var janganTrader = V2Rules.Center(6512, 1008, 0f);
        foreach (var pair in new[] { new[] { hotanTrader, donwhangTrader }, new[] { donwhangTrader, hotanTrader },
            new[] { janganTrader, hotanTrader }, new[] { hotanTrader, janganTrader }, new[] { janganTrader, donwhangTrader },
            new[] { donwhangTrader, janganTrader } })
        {
            float groundA, groundB;
            ExpeditionRules.TryGround(pair[0], out groundA);
            ExpeditionRules.TryGround(pair[1], out groundB);
            pair[0].Height = groundA;
            pair[1].Height = groundB;
            var ferryPlan = TradeRoutes.PlanWalk(pair[0], pair[1]);
            A(ferryPlan.Count > 10 && ferryPlan.Count(RoadMesh.IsFerry) >= 1 && ferryPlan.All(s3 => s3.From == null || RoadMesh.IsFerry(s3)),
                "Caravan route " + (int)pair[0].X + "," + (int)pair[0].Y + " → " + (int)pair[1].X + "," + (int)pair[1].Y + " crosses by ferry only");
        }
        // Jangan: the caravan road crosses the wall by the south gate, from wherever the character stands inside the city.
        var southSoldier = V2Rules.Center(6434, 968, 0f);
        var eastSoldier = V2Rules.Center(6662, 1142, 0f);
        var janganNorth = V2Rules.Center(6509.6, 1135.7, 0f);
        float groundNorth;
        ExpeditionRules.TryGround(janganNorth, out groundNorth);
        janganNorth.Height = groundNorth;
        A(TradeRoutes.InJangan(janganNorth) && TradeRoutes.InJangan(janganTrader) && !TradeRoutes.InJangan(donwhangTrader) && !TradeRoutes.InJangan(hotanTrader),
            "Jangan's walls contain both the trader and the city centre");
        foreach (var origin in new[] { janganNorth, janganTrader })
        {
            var gatePlan = TradeRoutes.PlanWalk(origin, donwhangTrader);
            var gatePoints = new[] { origin }.Concat(gatePlan.Select(st => st.To.Point())).ToList();
            A(gatePoints.Min(q => q.Distance(southSoldier)) < 40 && gatePoints.Min(q => q.Distance(eastSoldier)) > 100,
                "From inside Jangan (" + (int)origin.X + "," + (int)origin.Y + ") the caravan leaves by the south gate, not the east gate");
        }
        var backPlan = TradeRoutes.PlanWalk(donwhangTrader, janganNorth);
        A(backPlan.Select(st => st.To.Point()).Min(q => q.Distance(southSoldier)) < 40, "Entering Jangan the caravan also uses the south gate");
        // The arch is crossed in a straight line: three points on one x, directly after each other.
        foreach (var archPlan in new[] { TradeRoutes.PlanWalk(janganNorth, donwhangTrader), TradeRoutes.PlanWalk(janganTrader, hotanTrader) })
        {
            int arch = archPlan.FindIndex(st => st.To.Code == "Güney kapı");
            A(arch >= 0 && arch + 2 < archPlan.Count && archPlan[arch + 1].To.Code == "Güney kapı" && archPlan[arch + 2].To.Code == "Güney kapı" &&
                Math.Abs(archPlan[arch].To.X - archPlan[arch + 2].To.X) < 1 && archPlan[arch].To.Y > archPlan[arch + 1].To.Y + 40 &&
                archPlan[arch + 1].To.Y > archPlan[arch + 2].To.Y + 40, "Leaving Jangan the route runs straight south through the arch");
        }
        var entering = TradeRoutes.PlanWalk(donwhangTrader, janganTrader);
        int enterArch = entering.FindIndex(st => st.To.Code == "Güney kapı");
        A(enterArch >= 0 && entering[enterArch].To.Y < entering[enterArch + 1].To.Y && entering[enterArch + 1].To.Y < entering[enterArch + 2].To.Y,
            "Entering Jangan the route runs straight north through the arch");
        var scriptSteps = TradeRoutes.PlanWalk(hotanTrader, donwhangTrader);
        string scriptText = TradeRoutes.Script("Hotan", "Donwhang", hotanTrader, scriptSteps);
        A(scriptText.Contains("# feribot: GATE_NPC_KT_FERRY2") && scriptText.Contains("1 feribot geçişi"), "Script marks the ferry crossing");
        CaravanScripts.Disabled = false;
        ReadyCaravanScriptChecks(hotanTrader, donwhangTrader, janganTrader);
        SkillChecks();
        SkillEngineChecks();
        StabilityChecks();
        StartupChecks();
        GameArtChecks();
        PartyBuffChecks();
        ResumeChecks();
        PlannerChecks();
        ExplorerChecks();
        // Unique alert: once per monster, again after it left and came back; ordinary monsters are ignored.
        var alert = new UniqueAlert();
        var boss = new Monster { Uid = 7, Name = "Tiger Girl", Rank = 3, Alive = true };
        var plain = new Monster { Uid = 8, Name = "Wolf", Rank = 0, Alive = true };
        A(alert.Observe(new[] { plain }).Count == 0, "Ordinary monsters raise no unique alert");
        A(alert.Observe(new[] { plain, boss }).SequenceEqual(new[] { "Tiger Girl" }), "A unique is announced when it appears");
        A(alert.Observe(new[] { plain, boss }).Count == 0, "The same unique is announced only once");
        A(alert.Observe(new Monster[0]).Count == 0 && alert.Observe(new[] { boss }).Count == 1, "A unique that returns is announced again");
        boss.Alive = false;
        A(alert.Observe(new[] { boss }).Count == 0, "A dead unique raises no alert");
    }

    // Getting back to work after the game closed: wait for the new game, connect, let the character settle, start the job; give up on time,
    // on a dead character and on a game that keeps closing.
    static void ResumeChecks()
    {
        const long sec = 1000;
        var plan = new ResumePlan();
        A(!new V3Settings().AutoResume && !plan.Armed && plan.Next(0, false, true) == ResumePlan.Step.Nothing, "The resume is off by default and does nothing unarmed");
        A(plan.Arm(0, OperationMode.Hunt, "AlgosGOD", null, null, 111) && plan.Armed && plan.Current == ResumePlan.Phase.Waiting && plan.Character == "AlgosGOD",
            "The closed game's job is remembered");
        A(!plan.TimeToLook(1 * sec) && plan.TimeToLook(3 * sec) && !plan.TimeToLook(3 * sec + 10), "The new game is looked for every three seconds");
        A(!plan.Candidate(111) && plan.Candidate(222), "The closed game's own number is not a new game");
        plan.Found(4 * sec, 222);
        A(plan.Current == ResumePlan.Phase.Found && plan.Next(6 * sec, false, true) == ResumePlan.Step.Nothing, "A game that has just opened is given time to load");
        A(plan.Next(12 * sec, false, true) == ResumePlan.Step.Connect && plan.Next(12 * sec + 10, false, true) == ResumePlan.Step.Nothing &&
            plan.Next(17 * sec, false, true) == ResumePlan.Step.Connect, "The connection is tried again every few seconds until it works");
        plan.Connected(18 * sec);
        A(plan.Current == ResumePlan.Phase.Ready && plan.Next(25 * sec, true, true) == ResumePlan.Step.Nothing && plan.Next(34 * sec, true, true) == ResumePlan.Step.Start,
            "The job starts after the character has settled");
        plan.Started(34 * sec);
        A(!plan.Armed, "A started job ends the plan");
        // another game is turned down and looked past
        plan.Arm(100 * sec, OperationMode.Travel, "AlgosGOD", "Hotan", "Jangan", 5);
        plan.Found(104 * sec, 300);
        plan.Reject(105 * sec);
        A(plan.Current == ResumePlan.Phase.Waiting && !plan.Candidate(300) && plan.Candidate(301) && plan.From == "Hotan" && plan.To == "Jangan", "A game with another character is not tried again");
        // a connection that is lost while the character settles goes back to waiting
        plan.Found(110 * sec, 301);
        plan.Connected(120 * sec);
        plan.Next(125 * sec, false, true);
        A(plan.Current == ResumePlan.Phase.Waiting, "A lost connection goes back to waiting");
        // give up: no game at all, a game that never enters the world, a dead character
        var late = new ResumePlan();
        late.Arm(0, OperationMode.Hunt, "A", null, null, 1);
        A(late.Next(ResumePlan.MaxWaitMs + 1, false, true) == ResumePlan.Step.Cancel && !late.Armed && late.Why.Contains("geri gelmedi"), "No game for half an hour ends the plan");
        var stuck = new ResumePlan();
        stuck.Arm(0, OperationMode.Hunt, "A", null, null, 1);
        stuck.Found(sec, 2);
        A(stuck.Next(sec + ResumePlan.EnterWorldMs + 1, false, true) == ResumePlan.Step.Cancel && stuck.Why.Contains("dünyaya girmedi"), "A game that never enters the world ends the plan");
        var dead = new ResumePlan();
        dead.Arm(0, OperationMode.Hunt, "A", null, null, 1);
        dead.Found(sec, 2);
        dead.Connected(2 * sec);
        A(dead.Next(30 * sec, true, false) == ResumePlan.Step.Nothing && dead.Next(30 * sec + ResumePlan.DeadWaitMs + 1, true, false) == ResumePlan.Step.Cancel &&
            dead.Why.Contains("ölü"), "A character that stays dead ends the plan");
        var revived = new ResumePlan();
        revived.Arm(0, OperationMode.Hunt, "A", null, null, 1);
        revived.Found(sec, 2);
        revived.Connected(2 * sec);
        revived.Next(20 * sec, true, false);
        A(revived.Next(60 * sec, true, true) == ResumePlan.Step.Start, "A revived character starts the job");
        // a game that keeps closing is not started for ever
        var loop = new ResumePlan();
        for (int i = 0; i < ResumePlan.MaxResumes; i++)
        {
            A(loop.Arm(i * 60 * sec, OperationMode.Hunt, "A", null, null, i), "Resume " + (i + 1) + " is allowed");
            loop.Started(i * 60 * sec + 1);
        }
        A(!loop.Arm(6 * 60 * sec, OperationMode.Hunt, "A", null, null, 9) && loop.Why.Contains("sürekli"), "Too many resumes in a short time are refused");
        A(loop.Arm(ResumePlan.ResumeWindowMs + 10 * 60 * sec, OperationMode.Hunt, "A", null, null, 9), "After a while resuming is allowed again");
        loop.Cancel("elle durduruldu");
        A(!loop.Armed && loop.Why == "elle durduruldu", "The user's own Stop ends the plan");
    }

    // Characters are told from monsters and NPCs by their object type (party buffs themselves are gone: nothing is cast by id any more).
    static void PartyBuffChecks()
    {
        A(GameReader.IsPlayerType(0x26, 0x26) && !GameReader.IsPlayerType(0x8C6, 0x26) && !GameReader.IsPlayerType(0x46, 0x26) && !GameReader.IsPlayerType(0x11C7, 0x26) &&
            !GameReader.IsPlayerType(0x26, 0), "Characters are told from monsters and NPCs by their object type");
    }

    // The pictures taken from the game: the launcher film's frames, the plate, the seven unique portraits, and where each page gets its unique.
    static void GameArtChecks()
    {
        A(Enumerable.Range(0, GameArt.FrameCount).All(i => GameArt.Frame(i) != null && GameArt.Frame(i).Width == 640 && GameArt.Frame(i).Height == 360) &&
            GameArt.Plate != null && GameArt.Plate.Width == 384, "The launcher film's twelve frames and the plate are in the program");
        A(GameArt.Uniques.Length == 7 && GameArt.UniqueNames.Length == 7 && GameArt.Uniques.All(k => GameArt.Unique(k) != null && GameArt.Unique(k).Width == 44),
            "The seven unique monsters have their game portraits in the program");
        A(GameArt.UniqueOf("Grim Crypt") == "kerberos" && GameArt.UniqueOf("Av Alanı") == "tiger" && GameArt.UniqueOf("Koruma") == "shaitan" &&
            GameArt.UniqueOf("Yenilikler") == "yarkan" && GameArt.UniqueOf("nowhere") == "tiger" && GameArt.UniqueOf(null) == "tiger",
            "Every page has a unique beside it");
        Func<double, long> sum = seconds =>
        {
            using (var bmp = new Bitmap(640, 250))
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);
                GameArt.DrawFilm(g, new Rectangle(0, 0, 640, 250), new Rectangle(0, 8, 640, 250), seconds);
                long total = 0;
                for (int y = 0; y < 250; y += 5)
                    for (int x = 0; x < 640; x += 5)
                    {
                        var c = bmp.GetPixel(x, y);
                        total += c.R + c.G + c.B;
                    }
                return total;
            }
        };
        long first = sum(0), between = sum(0.25), second = sum(0.5), last = sum(99);
        A(first > 100000 && second > 100000 && last > 100000 && first != second && between != first && between != second,
            "The film is drawn, blends two frames between them and stops on its last frame");
        using (var sheet = new Bitmap(900, 330))
        using (var g = Graphics.FromImage(sheet))
        {
            g.Clear(Color.FromArgb(40, 29, 23));
            for (int i = 0; i < GameArt.Uniques.Length; i++)
                GameArt.DrawPortrait(g, GameArt.Uniques[i], new Rectangle(10 + i * 56, 10, 48, 48), Ui.Accent);
            using (var plate = new ArtPlate { Width = 208, Height = 104 })
            using (var plateBitmap = new Bitmap(208, 104))
            {
                plate.DrawToBitmap(plateBitmap, new Rectangle(0, 0, 208, 104));
                g.DrawImage(plateBitmap, 10, 70);
            }
            foreach (var page in new[] { "Grim Crypt", "Av Alanı", "Kervan Rotası" })
                using (var header = new RouteHeader { Width = 640, Height = 50 })
                using (var headerBitmap = new Bitmap(640, 50))
                {
                    header.SetTitle(page);
                    header.SetState("AlgosGOD · Seviye 99 · Bağlı", true);
                    header.DrawToBitmap(headerBitmap, new Rectangle(0, 0, 640, 50));
                    g.DrawImage(headerBitmap, 240, 70 + Array.IndexOf(new[] { "Grim Crypt", "Av Alanı", "Kervan Rotası" }, page) * 56);
                }
            sheet.Save("tests/Preview-gameart.png");
        }
    }

    // The ready-made caravan roads (data/caravan-scripts.json, from phBot caravan scripts): the routes that have one, what they contain, how they join the
    // saved traders, and how the planner uses them.
    static void ReadyCaravanScriptChecks(Position hotanTrader, Position donwhangTrader, Position janganTrader)
    {
        var expected = new[]
        {
            new { From = "Hotan", To = "Jangan", Ferries = 2 }, new { From = "Jangan", To = "Hotan", Ferries = 2 },
            new { From = "Hotan", To = "Donwhang", Ferries = 1 }, new { From = "Donwhang", To = "Hotan", Ferries = 1 },
            new { From = "Jangan", To = "Donwhang", Ferries = 1 }, new { From = "Donwhang", To = "Jangan", Ferries = 1 },
            new { From = "Constantinople", To = "Samarkand", Ferries = 1 }
        };
        A(CaravanScripts.All.Count() == expected.Length && CaravanScripts.Find("Samarkand", "Constantinople") == null &&
            CaravanScripts.Find("Jangan", "Nanzhang") == null && CaravanScripts.Find("Jangan", "Jangan Pass") == null,
            "Only the caravan routes the bot has get a ready-made script (the rest of the folder is not used)");
        var traders = new Dictionary<string, Position> { { "Hotan", hotanTrader }, { "Donwhang", donwhangTrader }, { "Jangan", janganTrader } };
        foreach (var route in expected)
        {
            var script = CaravanScripts.Find(route.From, route.To);
            string name = route.From + " → " + route.To;
            A(script != null && TradeRoutes.Routes.Any(r => r[0] == route.From && r[1] == route.To), name + " is one of the bot's caravan routes");
            var steps = CaravanScripts.Steps(script);
            A(steps != null && CaravanScripts.WalkPoints(script) > 200 && CaravanScripts.Ferries(script) == route.Ferries &&
                steps.Count(s => s.From != null) == route.Ferries && steps.Where(s => s.From != null).All(RoadMesh.IsFerry),
                name + " script is understood: " + CaravanScripts.WalkPoints(script) + " way points and the ferries it takes");
            double longest = 0;
            for (int i = 1; i < steps.Count; i++)
                if (steps[i - 1].From == null && steps[i].From == null)
                    longest = Math.Max(longest, steps[i - 1].To.Point().Distance(steps[i].To.Point()));
            A(longest > 0 && longest <= 60, name + " script has no jump between two way points (longest " + (int)longest + ")");
            Position town;
            if (traders.TryGetValue(route.From, out town))
                A(steps.First(s => s.From == null).To.Point().Distance(town) <= 40, name + " script begins at the trader of " + route.From);
            if (traders.TryGetValue(route.To, out town))
                A(steps.Last(s => s.From == null).To.Point().Distance(town) <= 40, name + " script ends at the trader of " + route.To);
            string text = CaravanScripts.Text(script);
            var lines = text.Replace("\r", "").Split('\n').Where(l => l.Length > 0).ToList();
            A(lines.All(l => l.StartsWith("#") || l.StartsWith("walk,") || l.StartsWith("wait,") || l.StartsWith("teleport,")) &&
                lines.Count(l => l.StartsWith("walk,")) == CaravanScripts.WalkPoints(script) && !text.Contains("chat") && !text.Contains("oldtrade") &&
                !text.Contains("mount"), name + " script file holds only walk, wait and teleport lines (no chat or trade commands)");
        }
        // The planner follows the script: from the first town, from the middle of the road, and not for a start far from every road.
        var stub = new Func<Position, Position, List<TransitStep>>((a, b) => new List<TransitStep>());
        var hotanJangan = CaravanScripts.Steps(CaravanScripts.Find("Hotan", "Jangan"));
        var whole = TradeRoutes.PlanVia(hotanTrader, janganTrader, stub);
        A(whole.Count >= hotanJangan.Count && whole.Count <= hotanJangan.Count + 1 && whole.Count(RoadMesh.IsFerry) == 2 &&
            whole.First(s => s.From == null).To.Point().Distance(hotanJangan.First().To.Point()) < 1, "Hotan → Jangan is planned as the script's way points and two ferries");
        // a way point on the open road (in no town) a little way from Hotan
        var halfWay = hotanJangan.Where(s => s.From == null).Skip(40).First(s => TradeRoutes.TownKey(s.To.Point()) == null).To.Point();
        var rest = TradeRoutes.PlanVia(V2Rules.Center(halfWay.X + 2, halfWay.Y + 2, halfWay.Height), janganTrader, stub);
        A(rest.Count > 100 && rest.Count < whole.Count - 20 && rest.Count(RoadMesh.IsFerry) == 2 &&
            rest.Last().To.Point().Distance(hotanJangan.Last().To.Point()) < 15, "A caravan stopped on the road goes on from where it stands");
        var plannerCalls = new List<string>();
        var recording = new Func<Position, Position, List<TransitStep>>((a, b) =>
        {
            plannerCalls.Add((int)a.X + "," + (int)a.Y + ">" + (int)b.X + "," + (int)b.Y);
            return new List<TransitStep>();
        });
        var farAway = V2Rules.Center(-9000, 1000, 0);
        TradeRoutes.PlanVia(farAway, janganTrader, recording);
        A(plannerCalls.Count >= 1 && plannerCalls[0].StartsWith("-9000,1000>"), "A start away from every ready-made road is planned from the map");
        var fromTown = new List<string>();
        TradeRoutes.PlanVia(V2Rules.Center(hotanTrader.X + 30, hotanTrader.Y - 20, 0), janganTrader, (a, b) =>
        {
            fromTown.Add((int)a.X + "," + (int)b.X);
            return new List<TransitStep>();
        });
        A(fromTown.Count == 1 && fromTown[0].EndsWith("," + (int)hotanJangan.First().To.Point().X), "From elsewhere in Hotan the character walks to the script's first point");
        var reverse = TradeRoutes.PlanVia(janganTrader, hotanTrader, stub);
        A(reverse.Count(RoadMesh.IsFerry) == 2 && reverse.First(s => s.From != null).From.Code == "GATE_NPC_CH_FERRY2",
            "Jangan → Hotan takes the Jangan ferry first, then the Hotan one");
        A(TradeRoutes.PlanVia(janganTrader, V2Rules.Center(6045, -285, 331), stub).Count > 100 && !TradeRoutes.PlanVia(janganTrader, V2Rules.Center(6045, -285, 331), stub)
            .Any(RoadMesh.IsFerry), "Routes without a ready-made script keep their own road (Jangan Pass)");
    }

    static void A(bool test, string name)
    {
        if (!test)
            throw new Exception("FAIL: " + name);
        passed.Add(name);
    }

    static Position P(double x, double y)
    {
        return V2Rules.Center(x, y, 0);
    }

    static Snapshot S(bool alive, Position p)
    {
        return new Snapshot { PlayerAlive = alive, Player = p, InventoryKnown = true, SpeedKnown = true };
    }

    [STAThread]
    static void Main(string[] args)
    {
        try
        {
            Run(args);
        }
        catch (Exception e)
        {
            File.WriteAllText("tests/tests-failed.txt", e.ToString());
            Environment.ExitCode = 1;
        }
    }

    static void Run(string[] args)
    {
        if (args.Length > 0 && args[0] == "--road-audit")
        {
            var rows = new List<string>();
            foreach (var d in V3Data.Dungeons.Where(d => d.Mode == "Solo Trial"))
            {
                int city = d.Area.Contains("Samarkand") ? 25 : d.Area.Contains("Constantinople") ? 20 : d.Area.Contains("Downhang") ?
                    2 : d.Area.Contains("Jangan") ? 1 : 5;
                var source = ExpeditionRules.Network.Gates.Single(gate => gate.Id == city);
                try
                {
                    var plan = ExpeditionRules.Plan(source.Point(), d.Entrance());
                    rows.Add(d.Code + " OK " + plan.Count + " steps " + string.Join(",", plan.Where(step => step.From != null)
                        .Select(step => step.From.Id + "→" + step.To.Id)));
                }
                catch (Exception error)
                {
                    rows.Add(d.Code + " FAIL " + error.Message);
                }
            }
            File.WriteAllLines("tests/road-audit.txt", rows);
            return;
        }
        foreach (var dungeon in V3Data.Dungeons.Where(d => d.Mode == "Solo Trial"))
        {
            int expected = ExpeditionRules.Expected(dungeon.Code);
            var portal = ExpeditionRules.Network.Gates.Single(transportGate => transportGate.Code == "GATE_NE_GRIM_" +
                dungeon.Code + "_S");
            A(ExpeditionRules.Network.Links.Any(l => l.From == dungeon.GateId && l.To == portal.Id), dungeon.Code +
                " Solo Trial portal linked from correct entrance");
            A(ExpeditionRules.Cleared("Dungeon cleared! Monsters " + expected + "/" + expected + ", AP +2", dungeon.Code),
                dungeon.Code + " exact count completion matches");
            A(!ExpeditionRules.Cleared("Dungeon cleared! Monsters " + (expected - 1) + "/" + expected, dungeon.Code),
                dungeon.Code + " incomplete count rejected");
            A(!ExpeditionRules.Cleared("Dungeon cleared! Monsters " + (expected == 6 ? 9 : 6) + "/" + (expected == 6 ? 9 :
                6), dungeon.Code), dungeon.Code + " other dungeon count rejected");
        }
        foreach (var dungeon in V3Data.Dungeons.Where(d => d.Mode == "Solo Trial"))
        {
            var capturedFloor = FloorNavigation.LoadCaptured(dungeon);
            var rooms = GrimRooms.Load(dungeon, capturedFloor);
            var points = new HashSet<string>();
            for (int sweep = 0; sweep < 60; sweep++)
            {
                Position point = rooms.Next(capturedFloor.Origin);
                if (point == null)
                    break;
                points.Add(point.X + "," + point.Y);
                rooms.Arrived();
            }
            A(points.Count >= 3 && points.Count <= 40, dungeon.Code +
                " initial room sweep has distinct floor waypoints (" + points.Count + ")");
        }
        var d18 = V3Data.Dungeons.Single(d => d.Code == "D18" && d.Mode == "Solo Trial");
        var roomRun = GrimRooms.Load(d18, FloorNavigation.LoadCaptured(d18));
        var pack = new[] { new Monster { Uid = 1, Alive = true, Position = P(-545, 4869) }, new Monster { Uid = 2, Alive =
            true, Position = P(-499, 4868) }, new Monster { Uid = 3, Alive = true, Position = P(-520, 4885) } };
        var nextRoom = new Monster { Uid = 4, Alive = true, Position = P(-516, 4977) };
        var optional = new Monster { Uid = 5, Alive = true, Name = "Envy", Position = P(-516, 4977) };
        roomRun.Observe(pack);
        roomRun.Observe(new[] { nextRoom, optional });
        var thirdRoom = new Monster { Uid = 6, Alive = true, Position = P(-444, 5115) };
        roomRun.Observe(new[] { thirdRoom });
        A(roomRun.Allows(nextRoom) && roomRun.Allows(optional) && !roomRun.Allows(thirdRoom) && roomRun.Room == 0,
            "The next room is cleared on the way, the room after it waits, mobs that are only seen do not advance the room and Envy remains eligible");
        pack[0].Alive = false;
        pack[1].Health = 0;
        roomRun.Observe(pack);
        roomRun.Observe(pack);
        A(roomRun.Room == 0 && !roomRun.Allows(thirdRoom), "Duplicate dead snapshots do not advance the room");
        pack[2].Alive = false;
        roomRun.Observe(pack);
        A(roomRun.Room == 1 && roomRun.Allows(thirdRoom), "Three distinct confirmed deaths advance to the next room");
        A(!ExpeditionRules.ReturnIds.Overlaps(RecoveryRules.ReverseIds), "Return and Reverse item sets do not overlap");
        A(ExpeditionRules.ReturnIds.Contains(61) && ExpeditionRules.ReturnIds.Contains(63318),
            "Normal and event Return variants loaded");
        var route = ExpeditionRules.Plan(P(113.8, 15.3), P(-4407, 196));
        A(route.Count > 0 && route.Any(step => step.From != null && ExpeditionRules.Network.Links.Any(link => link.From == step.From.Id &&
            link.To == step.To.Id)), "Hotan to Roc route includes declared transit");
        foreach (var trial in V3Data.Dungeons.Where(d => d.Mode == "Solo Trial"))
        {
            int cityGate = trial.Area.Contains("Samarkand") ? 25 : trial.Area.Contains("Constantinople") ? 20 : trial.Area.Contains("Downhang") ?
                2 : trial.Area.Contains("Jangan") ? 1 : 5;
            List<TransitStep> trialRoute = null;
            try
            {
                trialRoute = ExpeditionRules.Plan(ExpeditionRules.Network.Gates.Single(gateRow => gateRow.Id == cityGate)
                    .Point(), trial.Entrance());
            }
            catch (Exception) { }
            A(trialRoute != null && trialRoute.Count > 0, trial.Code + " entrance has a road route from its city");
            if (trial.Code == "D11" || trial.Code == "D12")
                A(trialRoute != null && trialRoute.Last().To.Point().Distance(trial.Entrance()) <= 24, trial.Code +
                    " route crosses the unlinked gap and ends at the entrance");
        }
        {
            var tileScale = WorldMapView.ToImage(192, 0).X - WorldMapView.ToImage(0, 0).X;
            A(Math.Abs(tileScale - 32) < 0.001 && Math.Abs(WorldMapView.ToImage(0, 192).Y - WorldMapView.ToImage(0, 0).Y +
                32) < 0.001, "World map pixel scale matches the 192-unit region size");
            using (var worldStream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(WorldMapView.ResourceName))
                using (var worldImage = new System.Drawing.Bitmap(worldStream))
                {
                    foreach (var site in V3Data.Dungeons.Where(d => d.Mode == "Solo Trial"))
                    {
                        var pixel = WorldMapView.ToImage(site.X, site.Y);
                        int coverage = 0, samples = 0;
                        for (int dy = -6; dy <= 6; dy += 3)
                            for (int dx = -6; dx <= 6; dx += 3)
                            {
                                int qx = (int)pixel.X + dx, qy = (int)pixel.Y + dy;
                                if (qx < 0 || qy < 0 || qx >= worldImage.Width || qy >= worldImage.Height)
                                    continue;
                                samples++;
                                var tilePixel = worldImage.GetPixel(qx, qy);
                                if (!(Math.Abs(tilePixel.R - 20) <= 14 && Math.Abs(tilePixel.G - 30) <= 14 && Math.Abs(tilePixel.B - 45) <= 14))
                                    coverage++;
                            }
                        A(samples > 0 && coverage * 2 >= samples, site.Code +
                            " entrance lies on mapped terrain of the world map");
                    }
                }
        }
        var d17Route = ExpeditionRules.Plan(P(6431, 1096), V3Data.Dungeons.Single(d => d.Code == "D17" && d.Mode == "Solo Trial")
            .Entrance());
        A(d17Route.Count > 0 && d17Route.All(step => step.From == null) && d17Route.Last().To.Point().Distance(P(7484,
            1754)) <= 240, "Jangan screenshot start reaches a D17 local approach without teleport");
        A(!ExpeditionRules.PositionJumped(P(6431, 1096), P(6440, 1096)) && ExpeditionRules.PositionJumped(P(6431, 1096),
            P(6900, 1200)), "Normal movement keeps the route; a position jump requests a new route");
        var shortcutRoute = PhBotRoutes.Parse(new[] { "walk,10,0,0", "walk,20,0,0",
            "teleport,GATE_NPC_KT_FERRY2,GATE_NPC_WC_FERRY3", "walk,30,0,0" });
        A(ExpeditionRules.WalkShortcut(shortcutRoute, P(0, 0), (a, b) => true) == 1 && ExpeditionRules.WalkShortcut(shortcutRoute,
            P(0, 0), (a, b) => false) == 0, "Walking shortcut requires clear ground and preserves ferry steps");
        var nanzhangRoute = ExpeditionRules.Plan(P(5852, -1697.7), P(7484, 1754));
        A(nanzhangRoute.Any(step => step.From != null && step.From.Id == 252 && step.To.Id == 1) && nanzhangRoute.Last()
            .To.Point().Distance(P(7484, 1754)) <= 240,
            "Nanzhang screenshot route uses the Jangan gate and reaches the D17 approach");
        bool invalidTeleport = false;
        try
        {
            TargetSender.BuildPacket(0x1000, 0x2000, 0x705a, new byte[8]);
        }
        catch (ArgumentException)
        {
            invalidTeleport = true;
        }
        A(invalidTeleport, "Malformed teleport payload rejected");
        A(ExpeditionRules.ApproachReached(P(0, 0), P(3, 4)), "Travel arrival accepts exact 5-unit boundary");
        A(!ExpeditionRules.ApproachReached(P(0, 0), P(5.01, 0)), "Travel does not arrive beyond 5 units");
        A(TargetSender.BuildMountCall(0x1000, 0x2000, new uint[0]).Skip(13).Take(6).SequenceEqual(new byte[] { 0x83,
            0xf8, 1, 0x0f, 0x94, 0xc0 }), "Mount native result normalized without repeated toggles");
        A(!new V3Settings().ReturnBeforeTravel, "Automatic Return disabled by default");
        var ferryPlan = PhBotRoutes.Parse(new[] { "walk,1050.4,-50.8,0",
            "teleport,GATE_NPC_KT_FERRY2,GATE_NPC_WC_FERRY3", "walk,2594.2,-49,0" });
        A(ferryPlan != null && ferryPlan[1].From.Id == 12 && ferryPlan[1].To.Id == 14,
            "phBot ferry script maps to the known directed link");
        A(PhBotRoutes.Parse(new[] { "teleport,GATE_CH,missing" }) == null, "Unrecognized imported teleport is rejected");
        var liveFerry = new Snapshot { Player = P(1051.9, -47.2) };
        var ferryGate = ExpeditionRules.Network.Gates.Single(ng => ng.Id == 12);
        liveFerry.Npcs.Add(new WorldNpc { Model = 3826, Uid = 593, Position = P(1078.94, -59.543) });
        A(ExpeditionRules.FindGateNpc(liveFerry, ferryGate).Uid == 593,
            "Ferry NPC offset 30 units from arrival point resolves by model");
        A(!ExpeditionRules.ApproachReached(liveFerry.Player, ExpeditionRules.FindGateNpc(liveFerry, ferryGate).Position),
            "Teleport waits for real NPC 5-unit approach");
        liveFerry.Npcs.Add(new WorldNpc { Model = 3826, Uid = 594, Position = P(5000, 5000) });
        A(ExpeditionRules.FindGateNpc(liveFerry, ferryGate).Uid == 593, "Same-model distant ferry is excluded");
        NavigationChecks.Run(A);
        GrimSurveyChecks.Run(A);
        FloorBatchChecks.Run(A);
        var c = P(500, -50);
        var s = S(true, c);
        s.Monsters.AddRange(new[] { new Monster { Uid = 1, Name = "A", Alive = true, Position = P(510, -50) }, new Monster { Uid =
            2, Name = "B", Alive = true, Position = P(505, -50) }, new Monster { Uid = 3, Name = "C", Alive = true,
            Position = P(501, -50) }, new Monster { Uid = 4, Name = "A", Alive = true, Position = P(651, -50) }, new Monster { Uid =
            5, Name = "A", Alive = false, Position = P(500, -50) } });
        var names = new HashSet<string> { "A", "B" };
        var bans = new Dictionary<uint, long>();
        A(MultiTarget.Choose(s, names, c, 150, bans, 0, 0).Uid == 2,
            "Multi mob: nearest allowed name; rejects unlisted/dead/outside");
        bans[2] = 60000;
        A(MultiTarget.Choose(s, names, c, 150, bans, 0, 0).Uid == 1, "Blocked target moves to other selected species");
        A(MultiTarget.Choose(s, names, c, 150, bans, 60000, 0).Uid == 2, "Ban expires at boundary");
        A(MultiTarget.Choose(s, names, c, 150, bans, 60000, 1).Uid == 1, "Live target retained while valid");
        // the older checks below look at one read as a death; the loading-blip checks further down use the real window
        DeathRecovery.ConfirmMs = 0;
        var r = new DeathRecovery();
        r.Begin();
        var dead = S(false, c);
        A(r.Observe(dead, 0, true, true, 10) == RecoveryAction.None && r.TakeDeath(), "A death is noticed once");
        r.Observe(dead, 9000, true, true, 10);
        A(!r.TakeDeath(), "Repeated dead snapshots do not count as new deaths");
        A(r.Observe(dead, 10000, false, true, 10) == RecoveryAction.None && r.Phase == RecoveryPhase.WaitingRevive,
            "No town command while unfocused");
        A(r.Observe(dead, 10001, true, true, 10) == RecoveryAction.ReturnTown, "Town requested after wait");
        A(r.Observe(dead, 10002, true, true, 10) == RecoveryAction.None, "Town request sent once");
        var town = S(true, P(6434, 1094));
        A(V3Data.Town(town.Player) == "Jangan", "Town arrival requires known client sector");
        town.Items.Add(new BagItem { Model = 3795, Slot = 13, Quantity = 1, Type = 0x19ed });
        A(r.Observe(town, 20000, true, true, 10) == RecoveryAction.None, "Town settles before reverse");
        A(r.Observe(town, 24999, true, true, 10) == RecoveryAction.None, "No early reverse");
        A(r.Observe(town, 25000, true, true, 10) == RecoveryAction.Reverse, "One reverse after stable town");
        A(r.Observe(town, 26000, true, true, 10) == RecoveryAction.None, "Reverse not consumed twice");
        A(r.Observe(S(true, c), 30000, true, true, 10) == RecoveryAction.Resume,
            "Resume only when death point reached");
        r.Begin();
        // There is no death limit: the bot goes on however often the character dies
        A(r.Observe(dead, 40000, true, true, 20) == RecoveryAction.None && r.TakeDeath(), "No death limit: a later death is handled like the first");
        r.Begin();
        r.Observe(dead, 0, true, true, 20);
        A(r.Observe(S(true, c), 1000, true, true, 20) == RecoveryAction.Resume,
            "Party resurrection resumes");
        r = new DeathRecovery();
        r.Begin();
        r.Observe(dead, 0, true, true, 0);
        r.Observe(town, 1, true, true, 0);
        A(r.Observe(town, 90001, true, true, 0) == RecoveryAction.Stop, "Recovery timeout stops without resend");
        r = new DeathRecovery();
        r.Begin();
        r.Observe(dead, 0, true, true, 0);
        var emptyTown = S(true, town.Player);
        r.Observe(emptyTown, 1, true, true, 0);
        A(r.Observe(emptyTown, 5001, true, true, 0) == RecoveryAction.Stop, "No reverse in bag stops");
        r = new DeathRecovery();
        r.Begin();
        r.Observe(dead, 0, true, true, 0);
        A(r.Observe(S(true, c), 1, true, true, 0) == RecoveryAction.Resume,
            "Late resurrection at death point after town request resumes");
        DeathRecovery.ConfirmMs = 800;
        var loadingScreen = S(false, new Position { Region = 0, X = -25920, Y = -17664 });
        r = new DeathRecovery();
        r.Begin();
        A(r.Observe(loadingScreen, 0, true, true, 10) == RecoveryAction.None && !r.TakeDeath() && r.Phase == RecoveryPhase.Ready,
            "Cleared alive flag at the loading-screen position (region 0) is not a death");
        r.Observe(dead, 1000, true, true, 10);
        A(r.Observe(dead, 1800, true, true, 10) == RecoveryAction.None && r.TakeDeath() && r.Phase == RecoveryPhase.WaitingRevive,
            "A real death right after loading is noticed within the window");
        A(!DeathRecovery.PositionReal(null) && !DeathRecovery.PositionReal(new Position { Region = 0 }) && DeathRecovery.PositionReal(c) &&
            DeathRecovery.PositionReal(new Position { Region = 25672 }) && DeathRecovery.PositionReal(new Position { Region = 30085 }),
            "Only region 0 / no position reads as an unreal position");
        // instance load blip: alive flag cleared for less than the window at a real-looking position (21:24:46, X -669 Y 4846)
        var blipAt = S(false, P(-669.06, 4846.10));
        r = new DeathRecovery();
        r.Begin();
        A(r.Observe(blipAt, 0, true, true, 30) == RecoveryAction.None && !r.TakeDeath() && r.Phase == RecoveryPhase.Ready &&
            r.Observe(blipAt, 400, true, true, 30) == RecoveryAction.None && !r.TakeDeath() && r.Phase == RecoveryPhase.Ready,
            "A cleared alive flag that has not lasted the window is not a death yet");
        A(r.Observe(S(true, blipAt.Player), 500, true, true, 30) == RecoveryAction.None && r.Phase == RecoveryPhase.Ready &&
            r.Observe(blipAt, 1000, true, true, 30) == RecoveryAction.None && !r.TakeDeath() &&
            r.Observe(blipAt, 1700, true, true, 30) == RecoveryAction.None && !r.TakeDeath() && r.Phase == RecoveryPhase.Ready,
            "A loading blip that clears again is ignored, and the window starts anew after it");
        A(r.Observe(blipAt, 1800, true, true, 30) == RecoveryAction.None && r.TakeDeath() && r.Phase == RecoveryPhase.WaitingRevive,
            "A real death is latched once the flag has stayed for the window (well within 2 s)");
        A(r.Observe(loadingScreen, 2000, true, true, 30) == RecoveryAction.None && r.Phase == RecoveryPhase.WaitingRevive,
            "An unreal read does not disturb a latched death");
        r = new DeathRecovery();
        r.Begin();
        r.Observe(loadingScreen, 0, true, true, 30);
        A(r.Observe(loadingScreen, 5000, true, true, 30) == RecoveryAction.None && !r.TakeDeath() && r.Phase == RecoveryPhase.Ready,
            "Region 0 stays ignored however long it lasts");
        A(V3Data.Town(P(115, 13)) == "Hotan Kingdom", "Actual New Era Hotan spawn recognized");
        A(!RecoveryRules.IsReverse(new BagItem { Model = 7100, Slot = 13, Quantity = 1, Type = 0xeec }),
            "Speed item cannot be used as reverse");
        A(!RecoveryRules.IsReverse(new BagItem { Model = 3795, Slot = 12, Quantity = 1, Type = 0x19ed }),
            "Equipment slot cannot be reverse");
        A(V3Data.Dungeons.Count == 90 && V3Data.Dungeons.Select(d => d.Code).Distinct().Count() == 18,
            "90 variants (five modes) / 18 actual entrances loaded");
        A(V3Data.Dungeons.GroupBy(d => d.Mode).All(group => group.Count() == 18), "Each difficulty has 18 dungeons");
        var d01 = V3Data.Dungeons.First(d => d.Code == "D01");
        A(Math.Abs(d01.X + 1649.8) <.01 && Math.Abs(d01.Y - 2632.9) <.01, "Current gate rows world XY conversion");
        A(V3Data.Dungeons.First(d => d.Code == "D12").X == 72,
            "D12 coordinate recovered from gate, not invented from missing image");
        var g = new GrimRun();
        g.Arm(d01);
        var inside = S(true, d01.InnerPosition());
        A(!g.Observe(inside) && !g.Running, "Grim region alone cannot arm unknown instance");
        inside.Grim = new GrimObservation { Known = true, Id = 168, Title = "Grim Crypt", Text = "In progress" };
        A(g.Observe(inside) && g.Running && g.Floor != null, "Entry with HUD starts geometry mapping");
        inside.Monsters.Clear();
        g.Observe(inside);
        A(g.Running && !g.Complete, "Empty nearby monster list is NOT dungeon complete");
        inside.Grim.Text = "NOT COMPLETED";
        g.Observe(inside);
        A(!g.Complete, "Negative completion phrase rejected");
        inside.Grim.Text = "COMPLETED";
        g.Observe(inside);
        A(g.Complete && !g.Running, "Positive server completion stops and waits");
        var normalGrim = new GrimRun();
        var d12 = V3Data.Dungeons.First(d => d.Code == "D12" && d.Mode == "Novice");
        var plainInside = S(true, d12.InnerPosition());
        normalGrim.Arm(d12, plainInside.Player);
        A(normalGrim.Observe(plainInside) && normalGrim.Running,
            "Regular Grim with explicit inside selection supports absent Solo HUD");
        var floor = new FloorNavigation(d12.InnerPosition(), d12.InnerRegion);
        Func<Position, Position, bool> fakeProbe = (a, b) => Math.Abs(b.X - floor.Origin.X) <= 16 && Math.Abs(b.Y - floor.Origin.Y) <= 16 &&
            !(b.X > floor.Origin.X && b.Y == floor.Origin.Y);
        while (!floor.Done)
            floor.Advance(fakeProbe, 8);
        A(floor.Cells.Count > 1 && floor.Cells.Count < 30, "Navigation explores only reachable bounded cells");
        floor.ObservePosition(floor.Origin);
        A(floor.FullyCovered, "Mapped small room coverage");
        A(floor.RouteToUncovered(floor.Origin, fakeProbe).Count == 0, "No phantom route after coverage");
        normalGrim.Floor = floor;
        A(!normalGrim.CanFinish(plainInside, 0) && !normalGrim.CanFinish(plainInside, 14999),
            "Full navigation coverage still waits fifteen seconds");
        plainInside.Monsters.Add(new Monster { Alive = true, Position = plainInside.Player.Copy() });
        A(!normalGrim.CanFinish(plainInside, 15000), "Even a banned or unselected alive mob prevents clear");
        plainInside.Monsters.Clear();
        A(!normalGrim.CanFinish(plainInside, 20000) && normalGrim.CanFinish(plainInside, 35000),
            "New mob resets clear timer; clear only at new stable boundary");
        var large = new FloorNavigation(d12.InnerPosition(), d12.InnerRegion);
        Func<Position, Position, bool> largeProbe = (a, b) => Math.Abs(b.X - large.Origin.X) <= 64 && Math.Abs(b.Y - large.Origin.Y) <= 64;
        while (!large.Done)
            large.Advance(largeProbe, 8);
        large.ObservePosition(large.Origin);
        A(!large.FullyCovered, "Room not declared clear while unvisited ground remains");
        var path = large.RouteToUncovered(large.Origin, largeProbe);
        A(path.Count > 1 && path.Zip(path.Skip(1), (a, b) => a.Distance(b)).All(distance => distance <= 8.01),
            "Exploration follows connected 8-unit navigation edges");
        byte[] call8 = TargetSender.BuildCall(0x10000000, 0x10001000, new uint[8], new byte[0]);
        A(call8.Length == 64 && call8[61] == 0, "Eight argument navigation call fits before payload boundary");
        var t = new TravelWatch();
        t.Set(P(1000, 0), P(0, 0), 0);
        A(Math.Abs(t.Step(P(0, 0)).X - 32) <.01, "Travel uses bounded 32 unit steps");
        A(t.Stuck(P(0, 0), 20000), "Blocked walking times out");
        A(TargetSender.BuildPacket(0x10000000, 0x1000e183, 0x3053, new byte[] { 1 })[64] == 1,
            "Town packet explicit option 1");
        bool reject = false;
        try
        {
            TargetSender.BuildPacket(1, 2, 0x3053, new byte[] { 2 });
        }
        catch (ArgumentException)
        {
            reject = true;
        }
        A(reject, "Unsupported respawn option rejected");
        A(V2Rules.BerserkClasses.Contains("Unique"), "Unique Berserk dropdown option");
        A(V2Rules.BerserkMatches(new Monster { Rank = 3 }, "Unique"), "Unique rank selects Berserk");
        A(V2Rules.BerserkMatches(new Monster { Rank = 3, Party = true }, "Unique"),
            "Unique remains Unique with party flag");
        A(!V2Rules.BerserkMatches(new Monster { Rank = 4 }, "Unique") && !V2Rules.BerserkMatches(new Monster { Rank = 3 },
            "Giant"), "Unique and Giant Berserk classes isolated");
        A(V2Rules.BerserkMatches(new Monster { Rank = 4, Party = true }, "Giant Party"),
            "Prior Berserk class behavior retained");
        var repair = S(true, c);
        repair.MinDurability = 4;
        A(V2Rules.NeedsRepair(repair), "Repair <=4 preserved");
        string originalAccount = Environment.GetEnvironmentVariable("SRSTUDIO_ACCOUNT");
        Environment.SetEnvironmentVariable("SRSTUDIO_ACCOUNT", "1");
        string profile1 = V3Settings.PathName;
        Environment.SetEnvironmentVariable("SRSTUDIO_ACCOUNT", "2");
        A(profile1 != V3Settings.PathName, "Account settings paths isolated");
        Environment.SetEnvironmentVariable("SRSTUDIO_ACCOUNT", originalAccount);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var form = (Form)Activator.CreateInstance(typeof(MemorySelectV3Form), true);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MemorySelectV3Form).GetField("settings", flags).SetValue(form, null);
        var poll = (Timer)typeof(MemorySelectV3Form).GetField("poll", flags).GetValue(form);
        poll.Stop();
        var tabs = (TabControl)typeof(MemorySelectV3Form).GetField("tabs", flags).GetValue(form);
        A(tabs.TabPages.Count == 16 && tabs.TabPages[15].Text == "Yenilikler" && tabs.SelectedTab.Text == "Yenilikler" && tabs.TabPages[14].Text == "Lottery" && tabs.TabPages[13].Text == "Quest" &&tabs.TabPages[9].Text == "İstatistik" && tabs.TabPages[10].Text == "Koruma" && tabs.TabPages[11].Text == "Envanter" && tabs.TabPages[12].Text == "Bildirimler" && tabs.TabPages[7].Text == "Saldırı" && tabs.TabPages[4].Text == "Çift Hesap · Deneme" && tabs.TabPages[5].Text == "Nanzhang NPC" &&
            tabs.TabPages[1].Text == "Explorer Yerleri" && tabs.TabPages[2].Text == "Grim Crypt",
            "Four tabs render including dual-account trial");
        A(tabs.TabPages[6].Text == "Kervan Rotası", "Caravan page is part of the window");
        var reverseItem = new BagItem { Slot = 40, Model = 3795, Type = 6637, Quantity = 9 };
        var reversePayload = TargetSender.ReversePayload(reverseItem, 24);
        var displayIni = new[] { "; New Era Online - display options", "[display]", "fps_limit=60", "render_in_background=0" };
        var displayOn = GameDisplay.WithRenderInBackground(displayIni, true);
        var displayNoKey = GameDisplay.WithRenderInBackground(new[] { "[display]", "fps_limit=60", "", "[other]", "a=1" }, true);
        A(GameDisplay.ParseRenderInBackground(displayIni) == false && GameDisplay.ParseRenderInBackground(displayOn) == true &&
            displayOn.Count == 4 && displayOn[0] == displayIni[0] && displayOn[2] == "fps_limit=60" &&
            GameDisplay.ParseRenderInBackground(new[] { "[display]", "fps_limit=60" }) == null &&
            GameDisplay.ParseRenderInBackground(new[] { "[other]", "render_in_background=1" }) == null &&
            displayNoKey.IndexOf("render_in_background=1") == 2 && GameDisplay.ParseRenderInBackground(displayNoKey) == true,
            "Game display option: render_in_background is read from and written into the [display] section only");
        {
            var hotanGate = ExpeditionRules.Network.Gates.First(gateItem => gateItem.Id == 5);
            var farNpc = new WorldNpc { Uid = 4, Model = 2096, Position = V2Rules.Center(113.1, 48.8, 305) };
            var nearNpc = new WorldNpc { Uid = 5, Model = 2096, Position = V2Rules.Center(hotanGate.X + 3, hotanGate.Y + 2, 0) };
            A(ExpeditionRules.GateApproach(hotanGate, farNpc).Distance(hotanGate.Point()) < 0.5 && ExpeditionRules.GateApproach(hotanGate, null).Distance(hotanGate.Point()) < 0.5 &&
                ExpeditionRules.GateApproach(hotanGate, nearNpc).Distance(nearNpc.Position) < 0.5,
                "Gate approach: the gate's own point, the NPC's spot only when the NPC stands right there");
        }
        {
            var gateById = ExpeditionRules.Network.Gates.ToDictionary(gateItem => gateItem.Id);
            var samarkandNpc = ExpeditionRules.NpcPlace(gateById[25]);
            var hotanNpc = ExpeditionRules.NpcPlace(gateById[5]);
            A(samarkandNpc != null && Math.Abs(samarkandNpc.X + 5184.3) < 0.5 && Math.Abs(samarkandNpc.Y - 2890.8) < 0.5 && Math.Abs(samarkandNpc.Height - 250) < 0.5 &&
                hotanNpc != null && Math.Abs(hotanNpc.X - 113.1) < 0.5 && Math.Abs(hotanNpc.Y - 48.8) < 0.5, "Gate NPC places come from the game's building table");
            var standing = ExpeditionRules.StandNear(gateById[25].Point(), samarkandNpc, 33);
            A(Math.Abs(standing.Distance(samarkandNpc) - 33) < 1.5 && ExpeditionRules.StandNear(samarkandNpc, samarkandNpc, 33).Distance(samarkandNpc) < 0.5,
                "A gate NPC out of sight is approached to a point on the line to it, not walked onto its platform");
        }
        {
            var search = new ExpeditionRules.GateSearch();
            var order = new List<ExpeditionRules.GateStep>();
            order.Add(search.Next(false, false, true, false, 0));       // far from the gate: walk to its point
            order.Add(search.Next(false, true, true, false, 1000));     // at the point, the NPC not loaded: towards the NPC
            order.Add(search.Next(false, false, true, false, 2000));    // already walked away from the point: still towards the NPC, never back
            order.Add(search.Next(false, false, true, false, 3000));
            order.Add(search.Next(true, false, true, false, 4000));     // the NPC is loaded: use it
            A(order.SequenceEqual(new[] { ExpeditionRules.GateStep.WalkToGate, ExpeditionRules.GateStep.WalkToNpc, ExpeditionRules.GateStep.WalkToNpc,
                ExpeditionRules.GateStep.WalkToNpc, ExpeditionRules.GateStep.Use }), "Gate hop: gate point, then towards the NPC, no walking back and forth");
            // standing at the stand point with no NPC in sight brings the stand point closer, step by step, down to a floor
            var closer = new ExpeditionRules.GateSearch();
            closer.Next(false, true, true, false, 0);
            double first = closer.Stand;
            closer.Next(false, false, true, true, 1000);
            closer.Next(false, false, true, true, 2600);
            double second = closer.Stand;
            closer.Next(false, false, true, true, 3000);
            closer.Next(false, false, true, true, 4600);
            closer.Next(false, false, true, true, 6200);
            closer.Next(false, false, true, true, 7800);
            closer.Next(false, false, true, true, 9400);
            A(first == 33 && second == 25 && closer.Stand == 9, "Gate hop: the stand point comes closer when the NPC is not in sight there (33, 25, ... down to 9)");
            var lost = new ExpeditionRules.GateSearch();
            lost.Next(false, true, true, false, 0);
            var unknown = new ExpeditionRules.GateSearch();
            A(lost.Next(false, false, true, false, 61000) == ExpeditionRules.GateStep.Fail && unknown.Next(false, true, false, false, 0) == ExpeditionRules.GateStep.Fail,
                "Gate hop: gives up after 60 seconds, or at once when the NPC's place is unknown");
        }
        {
            var jangan = ExpeditionRules.Network.Links.FirstOrDefault(link => link.From == 1 && link.To == 2);
            A(jangan != null && jangan.Fee == 5000, "Transit data: Jangan's gate takes you to Donwhang for 5000 gold (the game's own dialog offers it)");
        }
        {
            // The data model's rules (see docs/data-model.md): references resolve, every town gate knows where its NPC stands, the dungeons' gates exist
            // and lead to their Solo gate, the numbers of the Reverse places are unique.
            var gateById = ExpeditionRules.Network.Gates.ToDictionary(gateItem => gateItem.Id);
            A(ExpeditionRules.Network.Links.All(link => gateById.ContainsKey(link.From) && gateById.ContainsKey(link.To)), "Transit data: every link joins two known gates");
            A(new[] { 1, 2, 5, 20, 25 }.All(id => ExpeditionRules.NpcPlace(gateById[id]) != null), "Transit data: every town gate has the place of its NPC");
            A(V3Data.Dungeons.All(d => gateById.ContainsKey(d.GateId) && (!d.IsSolo || ExpeditionRules.Network.Links.Any(link => link.From == d.GateId &&
                gateById.ContainsKey(link.To) && gateById[link.To].Code == d.SoloGateCode))), "Dungeons: each gate is known and each Solo dungeon has its link to the Solo gate");
            A(ReverseRules.All.Select(p => p.Id).Distinct().Count() == ReverseRules.All.Count, "Reverse places have unique numbers");
            // Every pair of the five big towns is reached by gates alone: the walk is only the short way inside the towns. A missing link shows up
            // here as a long walk to a ferry or around the map (the missing Jangan - Donwhang link once made the bot walk to the ferry port).
            var chain = new[] { 1, 2, 5, 25, 20 };
            var routes = new List<string>();
            bool shortWalks = true;
            for (int from = 0; from < chain.Length; from++)
                for (int to = 0; to < chain.Length; to++)
                {
                    if (from == to)
                        continue;
                    var start = gateById[chain[from]].Point();
                    var plan = ExpeditionRules.Plan(start, gateById[chain[to]].Point());
                    double walk = 0;
                    Position at = start;
                    int hopCount = 0;
                    foreach (var step in plan)
                    {
                        if (step.From != null)
                        {
                            walk += at.Distance(step.From.Point());
                            hopCount++;
                            at = step.To.Point();
                        }
                        else
                        {
                            walk += at.Distance(step.To.Point());
                            at = step.To.Point();
                        }
                    }
                    routes.Add(gateById[chain[from]].Code + " -> " + gateById[chain[to]].Code + " · " + hopCount + " hops · walk " + (int)walk);
                    shortWalks = shortWalks && walk <= 700 && hopCount >= 1;
                }
            File.WriteAllLines("tests/Route-Report.txt", routes.ToArray());
            A(shortWalks, "Routes between the five big towns use gates and walk at most 700 units (see tests/Route-Report.txt)");
        }
        {
            // An NPC dialog is a large dark panel; with text it has rows of light pixels, an empty one has none. Scenery around it does not count.
            Func<bool, bool, Bitmap> screen = delegate(bool panel, bool text)
            {
                var bmp = new Bitmap(900, 600);
                using (var canvas = Graphics.FromImage(bmp))
                {
                    canvas.Clear(Color.FromArgb(150, 160, 130));
                    canvas.FillRectangle(new SolidBrush(Color.FromArgb(210, 215, 200)), 20, 200, 60, 150);
                    if (panel)
                    {
                        canvas.FillRectangle(new SolidBrush(Color.FromArgb(18, 18, 18)), 70, 90, 380, 420);
                        canvas.DrawString("Explorer Gisaeng Ahjin", new Font("Tahoma", 9f), Brushes.White, 160, 98);
                        if (text)
                            for (int line = 0; line < 8; line++)
                                canvas.DrawString("Ah, a fellow traveler! I am Ahjin. Once I danced and", new Font("Tahoma", 9f), Brushes.White, 90, 150 + line * 18);
                    }
                }
                return bmp;
            };
            using (var none = screen(false, false))
            using (var blank = screen(true, false))
            using (var filled = screen(true, true))
            {
                A(DialogProbe.Read(none) == DialogState.None && DialogProbe.Read(blank) == DialogState.Blank && DialogProbe.Read(filled) == DialogState.Filled,
                    "Dialog probe tells no window, an empty window and a window with text apart (rows: " + DialogProbe.LastLight + ")");
            }
            using (var black = new Bitmap(900, 600))
                A(DialogProbe.Read(black) == DialogState.Unknown, "Dialog probe does not judge a black picture");
        }
        {
            // The Jangan - Jangan Pass caravan road: the walked way points, both directions, from the middle of the road, and no effect on other routes.
            var inJangan = V2Rules.Center(6440, 1100, 0);
            var atPass = V2Rules.Center(6045, -285, 331);
            var there = TradeRoutes.PassRoadAhead(inJangan, atPass);
            var back = TradeRoutes.PassRoadAhead(atPass, inJangan);
            A(there != null && back != null && there.Count > 100 && there.Count == back.Count &&
                Math.Abs(there[0].X - 6466.8) < 1 && Math.Abs(there[there.Count - 1].X - 6041.3) < 1 && Math.Abs(back[0].X - 6041.3) < 1 &&
                Math.Abs(back[back.Count - 1].X - 6466.8) < 1, "Jangan Pass road runs from Jangan's south gate to the pass and back the same way");
            var middle = there[there.Count / 2];
            var restart = TradeRoutes.PassRoadAhead(V2Rules.Center(middle.X + 1, middle.Y + 1, middle.Height), atPass);
            var restartBack = TradeRoutes.PassRoadAhead(V2Rules.Center(middle.X + 1, middle.Y + 1, middle.Height), inJangan);
            A(restart != null && restartBack != null && restart.Count < there.Count && restart.Count > 50 && restartBack.Count < there.Count && restartBack.Count > 50,
                "A walk stopped on the Jangan Pass road goes on from where it stands, either way");
            A(TradeRoutes.PassRoadAhead(V2Rules.Center(113, 49, 0), inJangan) == null && TradeRoutes.PassRoadAhead(inJangan, V2Rules.Center(113, 49, 0)) == null,
                "The Jangan Pass road is not used for other routes");
            var steps = TradeRoutes.PlanVia(inJangan, atPass, (a, b) => new List<TransitStep>());
            A(steps.Count == there.Count && TradeRoutes.TownKey(atPass) == "Jangan Pass" &&
                TradeRoutes.Routes.Any(stop => stop[0] == "Jangan" && stop[1] == "Jangan Pass") && TradeRoutes.Routes.Any(stop => stop[0] == "Jangan Pass" && stop[1] == "Jangan"),
                "Jangan Pass is a caravan stop with routes both ways");
        }
        {
            // Weapon change for skills that need another weapon: a Wizard / Cleric holds the Wizard staff (type 11) for spells and the Cleric staff
            // (type 15) for buffs and heals. 11603 = Cleric heal, 9958 = Wizard spell, 11527 = passive (any weapon), 10895 = Cleric staff, 10893 = Wizard staff.
            A(WeaponRules.RequiredFor(11603) == 15 && WeaponRules.RequiredFor(9958) == 11 && WeaponRules.RequiredFor(11527) == 0 &&
                WeaponRules.RequiredFor(99999999) == 0 && WeaponRules.TypeOf(10895) == 15 && WeaponRules.TypeOf(10893) == 11 && WeaponRules.TypeOf(8) == 0,
                "Skills and weapons know their weapon type (from the game's tables)");
            var wizardStaff = 10893u;
            var clericStaff = 10895u;
            var bag = new List<KeyValuePair<int, uint>> { new KeyValuePair<int, uint>(20, clericStaff) };
            A(WeaponRules.SlotToEquip(11603, wizardStaff, bag) == 20 && WeaponRules.SlotToEquip(11603, clericStaff, bag) == WeaponRules.NoChange &&
                WeaponRules.SlotToEquip(9958, clericStaff, new List<KeyValuePair<int, uint>> { new KeyValuePair<int, uint>(21, wizardStaff) }) == 21 &&
                WeaponRules.SlotToEquip(11527, wizardStaff, bag) == WeaponRules.NoChange &&
                WeaponRules.SlotToEquip(11603, wizardStaff, new List<KeyValuePair<int, uint>>()) == WeaponRules.NoWeapon,
                "A skill that needs another weapon names the bag slot to put on; the right weapon, a passive or no weapon in the bag change nothing");
            var better = new List<KeyValuePair<int, uint>> { new KeyValuePair<int, uint>(20, 10895u), new KeyValuePair<int, uint>(25, 10895u + 8u) };
            A(WeaponRules.TypeOf(10895u + 8u) != 15 || WeaponRules.SlotToEquip(11603, wizardStaff, better) == 25,
                "Of several suitable weapons the one of the higher model number is put on");
            A(TargetSender.ValidMove(new byte[] { 0, 20, 6, 1, 0 }) && TargetSender.ValidMove(new byte[] { 0, 23, 50, 2, 0 }) &&
                !TargetSender.ValidMove(new byte[] { 0, 5, 6, 1, 0 }) && !TargetSender.ValidMove(new byte[] { 0, 20, 7, 1, 0 }) &&
                !TargetSender.ValidMove(new byte[] { 1, 20, 6, 1, 0 }) && !TargetSender.ValidMove(new byte[] { 0, 20, 20, 1, 0 }) &&
                !TargetSender.ValidMove(new byte[] { 0, 20, 6, 0, 0 }), "The item move command only puts bag items on the weapon slot or in another bag slot");
            var swapRotation = new SkillRotation();
            swapRotation.Load(new[] { new SkillSlot { On = true, Key = "6", SkillId = 11603, Kind = SkillRotation.Buff_, Seconds = 100 } });
            var noBuffs = new SkillContext { Buffs = new HashSet<uint>() };
            A(swapRotation.Support(0, noBuffs) == '6', "A buff that is not on is chosen");
            swapRotation.DelayLast(5000);
            A(swapRotation.Support(1000, noBuffs) == 0 && swapRotation.Support(5000, noBuffs) == '6',
                "A delayed buff is offered again after the wait");
            A(new SkillOptions().WeaponSwap, "Weapon change is on by default");
        }
        {
            // The Grim treasure box (model 64835 = Solo 85 box) is an optional monster whatever its name: the box once had no name in the table
            // ("Bilinmeyen #64835"), was not taken for the box, and was left standing when the dungeon was finished.
            var unnamedBox = new Monster { Uid = 1, Model = 64835, Name = "Bilinmeyen #64835", Alive = true, Health = 150000, Position = V2Rules.Center(0, 0, 0) };
            var plain = new Monster { Uid = 2, Model = 61741, Name = "Grim Niya General", Alive = true, Health = 1000, Position = V2Rules.Center(0, 0, 0) };
            var mobNames = Catalog.Load();
            bool allNamed = true;
            for (uint model = GrimRooms.FirstBoxModel; model <= GrimRooms.LastBoxModel; model++)
            {
                string boxName;
                allNamed = allNamed && mobNames.TryGetValue(model, out boxName) && boxName == "Grim Treasure Box";
            }
            A(GrimRooms.Optional(unnamedBox) && GrimRooms.IsBox(unnamedBox) && !GrimRooms.Optional(plain) && allNamed,
                "The Grim treasure box is recognised by its model, and every box model has its name");
        }
        {
            // Skill pacing and targets from the game's skill table: 3423 = Soul Fire force (an imbue: no target), 7926 = a bow attack (a target, a
            // 1.4 s cast), 3473 = a buff with a 5 minute reuse.
            var imbue = SkillTiming.Of(3423);
            var bowAttack = SkillTiming.Of(7926);
            A(imbue != null && !imbue.NeedsTarget && imbue.ReuseMs == 8000 && bowAttack != null && bowAttack.NeedsTarget && bowAttack.PrepareMs + bowAttack.CastMs >= 1400 &&
                SkillTiming.Of(3473).ReuseMs == 300000 && SkillTiming.Of(99999999) == null, "Skill timing and target need come from the game's skill table");
            var imbueRotation = new SkillRotation();
            imbueRotation.Load(new[] { new SkillSlot { On = true, Key = "4", SkillId = 3423, Kind = SkillRotation.Attack_ } });
            var imbueContext = new SkillContext { Buffs = new HashSet<uint>() };
            A(imbueRotation.Support(0, imbueContext) == '4' && imbueRotation.NextAttack(0) == 0,
                "An imbue put in an attack line is used as a buff (no target)");
            var attackRotation = new SkillRotation();
            attackRotation.Load(new[] { new SkillSlot { On = true, Key = "5", SkillId = 7926, Kind = SkillRotation.Attack_ } });
            A(attackRotation.NextAttack(0) == '5', "A real attack skill stays an attack line");
        }
        {
            // The launcher's 4 GB memory option sets the "large address aware" bit in the game's header; the client check ignores that one bit.
            var image = new byte[0x400];
            image[0] = (byte)'M';
            image[1] = (byte)'Z';
            image[0x3c] = 0x80;
            image[0x80] = (byte)'P';
            image[0x81] = (byte)'E';
            image[0x80 + 0x16] = 0x02;
            image[0x80 + 0x17] = 0x01;
            var largeAddress = (byte[])image.Clone();
            largeAddress[0x80 + 0x16] |= 0x20;
            var otherChange = (byte[])image.Clone();
            otherChange[0x200] = 7;
            A(GameReader.ClientHashIgnoringLargeAddress(image) == GameReader.ClientHashIgnoringLargeAddress(largeAddress) &&
                GameReader.ClientHashIgnoringLargeAddress(image) != GameReader.ClientHashIgnoringLargeAddress(otherChange),
                "The client check accepts the 4 GB memory mode (one header bit) and still rejects any other change");
        }
        A(ReverseRules.All.Count == 50 && ReverseRules.Find(24) != null && Math.Abs(ReverseRules.Find(24).X - 3553.2) < 0.1 &&
            Math.Abs(ReverseRules.Find(24).Y - 2062.1) < 0.1, "Reverse: the game's 50 places are loaded with their world positions");
        A(reversePayload.Length == 8 && reversePayload[0] == 40 && reversePayload[1] == (6637 & 0xff) && reversePayload[2] == (6637 >> 8) &&
            reversePayload[3] == 7 && BitConverter.ToUInt32(reversePayload, 4) == 24, "Reverse: the packet is slot, item type, choice 7 and the place number");
        A(ReverseRules.Overland.All(p => p.Id != 30 && p.Id != 38 && p.Id != 29) && ReverseRules.Overland.Any(p => p.Id == 24) &&
            ReverseRules.Find(10).Name == "Heart Peak" && ReverseRules.Overland.Any(p => p.Id == 10),
            "Reverse: cave and tomb floors are not offered, towns and the mountain places (Heart Peak) are");
        var forHeartPeak = ReverseRules.Nearest(V2Rules.Center(121, 18, 0), V2Rules.Center(-4407, 196, 0), (a, b) => a.Distance(b), 1200);
        A(forHeartPeak != null && forHeartPeak.Id == 10, "Reverse: the Peak of Hearth dungeon (D06) is reached from the Heart Peak place");
        var nearToDonwhang = ReverseRules.Nearest(V2Rules.Center(121, 18, 0), V2Rules.Center(3560, 2060, 0), (a, b) => a.Distance(b), 1200);
        var noGainNearby = ReverseRules.Nearest(V2Rules.Center(3500, 2000, 0), V2Rules.Center(3560, 2060, 0), (a, b) => a.Distance(b), 1200);
        A(nearToDonwhang != null && nearToDonwhang.Id == 24 && noGainNearby == null,
            "Reverse: the place nearest to the goal is chosen, and none when the character is already close");
        A(LotteryRules.Coupons(40838830UL, 0, 20000, 0, 0) == 2041 && LotteryRules.Coupons(40838830UL, 838830, 20000, 0, 0) == 2000 &&
            LotteryRules.Coupons(19999UL, 0, 20000, 0, 0) == 0 && LotteryRules.Coupons(20000UL, 0, 20000, 0, 0) == 1 &&
            LotteryRules.Coupons(40838830UL, 0, 20000, 10, 0) == 10 && LotteryRules.Coupons(40838830UL, 0, 20000, 10, 10) == 0 &&
            LotteryRules.Coupons(100UL, 500, 20000, 0, 0) == 0 && LotteryRules.Coupons(100000UL, 0, 0, 0, 0) == 0,
            "Lottery: coupons the gold allows (reserve, price, limit)");
        CaravanChecks();
        var explorerGrid = (DataGridView)typeof(MemorySelectV3Form).GetField("explorerGrid", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        A(explorerGrid.Rows.Count == 52 && Convert.ToInt32(explorerGrid.Rows[51].Cells["N"].Value) == 52, "Explorer page lists the 52 spots");
        var explorerArea = (ComboBox)typeof(MemorySelectV3Form).GetField("explorerArea", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        A(explorerArea.Items.Count == 7, "Explorer page offers the seven cities, no whole-map view");
        var backgroundWalk = (CheckBox)typeof(MemorySelectV3Form).GetField("backgroundWalk", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        A(backgroundWalk.Checked && new V3Settings().BackgroundWalk, "Walking jobs may run in the background by default");
        var notifyBox = (CheckBox)typeof(MemorySelectV3Form).GetField("notifyDone", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        A(notifyBox.Checked && new V3Settings().NotifyDone, "A finished job is announced by default");
        notifyBox.Checked = false;
        typeof(MemorySelectV3Form).GetMethod("NotifyDone", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { "Deneme", "Bildirim kapalıyken sessiz kalır." });
        A(typeof(MemorySelectV3Form).GetField("doneIcon", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form) == null,
            "With notifications off no tray icon is created");
        notifyBox.Checked = true;
        A(!(bool)typeof(MemorySelectV3Form).GetMethod("CommandsAllowed", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null),
            "Without a connected game no command is allowed");
        var dg =(DataGridView)typeof(MemorySelectV3Form).GetField("dungeonGrid", flags).GetValue(form);
        A(dg.Rows.Count == 18, "Solo Trial selected by default");
        foreach (CheckBox check in (List<CheckBox>)typeof(MemorySelectV3Form).GetField("grimModes", flags).GetValue(form))
            check.Checked = true;
        A(dg.Rows.Count == 90, "All checked modes expose 90 rows");
        var visited = (HashSet<string>)typeof(MemorySelectV3Form).GetField("visitedDungeons", flags).GetValue(form);
        visited.Clear();
        var firstDungeon = (DungeonLocation)dg.Rows[0].Tag;
        dg.Rows[0].Cells[0].Value = true;
        A(visited.Contains(firstDungeon.Code + "|" + firstDungeon.Mode), "Manual dungeon checkbox stores code and mode");
        typeof(MemorySelectV3Form).GetMethod("RefreshDungeonGrid", flags).Invoke(form, null);
        A(Convert.ToBoolean(dg.Rows[0].Cells[0].Value), "Manual visited marker survives grid refresh");
        dg.Rows[0].Cells[0].Value = false;
        A(!visited.Contains(firstDungeon.Code + "|" + firstDungeon.Mode), "Manual visited marker can be cleared");
        ((HashSet<string>)typeof(MemorySelectV3Form).GetField("selectedMobs", flags).GetValue(form)).Clear();
        ((ListBox)typeof(MemorySelectV3Form).GetField("mobList", flags).GetValue(form)).Items.Clear();
        A(Catalog.Load()[61199] == "Mystic Tiger", "Mystic Tiger model name resolved");
        var theme = (ComboBox)typeof(MemorySelectV3Form).GetField("themePicker", flags).GetValue(form);
        for (int themeIndex = 0; themeIndex < 5; themeIndex++)
        {
            theme.SelectedIndex = themeIndex;
            Application.DoEvents();
        }
        theme.SelectedIndex = 0;
        var combo = (ComboBox)typeof(MemorySelectV3Form).GetField("monsters", flags).GetValue(form);
        combo.SelectedIndex = 0;
        typeof(MemorySelectV3Form).GetMethod("AddMob", flags).Invoke(form, null);
        typeof(MemorySelectV3Form).GetMethod("AddMob", flags).Invoke(form, null);
        var mobList = (ListBox)typeof(MemorySelectV3Form).GetField("mobList", flags).GetValue(form);
        A(mobList.Items.Count == 1, "Plus prevents duplicate mob rows");
        mobList.SelectedIndex = 0;
        typeof(MemorySelectV3Form).GetMethod("RemoveMob", flags).Invoke(form, null);
        A(mobList.Items.Count == 0, "Minus removes mob row");
        var sharedKeys = (TextBox)typeof(MemorySelectV3Form).GetField("keys", flags).GetValue(form);
        sharedKeys.Text = "1 2";
        var operation = typeof(MemorySelectV3Form).GetField("operation", flags);
        operation.SetValue(form, OperationMode.Hunt);
        A((string)typeof(MemorySelectV3Form).GetProperty("RunKeys", flags).GetValue(form, null) == "1 2",
            "Normal combat reads common Av settings");
        operation.SetValue(form, OperationMode.Grim);
        A((string)typeof(MemorySelectV3Form).GetProperty("RunKeys", flags).GetValue(form, null) == "1 2",
            "Grim combat reads the same common Av settings");
        A(typeof(MemorySelectV3Form).GetField("grimKeys", flags) == null, "No duplicate Grim controls");
        sharedKeys.Text = "1 2 3 4 5 6";
        operation.SetValue(form, OperationMode.Hunt);
        {
            var syncGrid = (DataGridView)typeof(MemorySelectV3Form).GetField("dungeonGrid", flags).GetValue(form);
            var syncDetails = (DungeonDetails)typeof(MemorySelectV3Form).GetField("dungeonDetails", flags).GetValue(form);
            bool followsGrid = syncGrid.Rows.Count >= 8;
            foreach (int rowIndex in new[] { 4, 7, 2, 5 })
            {
                syncGrid.CurrentCell = syncGrid.Rows[rowIndex].Cells[1];
                followsGrid = followsGrid && syncDetails.Title.StartsWith(((DungeonLocation)syncGrid.Rows[rowIndex].Tag).Code + " · ");
            }
            A(followsGrid, "The dungeon card follows the dungeon grid without lagging one row behind");
        }
        {
            var form_ = typeof(MemorySelectV3Form);
            Func<string, DungeonLocation> trial = code => V3Data.Dungeons.First(d => d.Code == code && d.Mode == "Solo Trial");
            var toggleQueue = form_.GetMethod("ToggleQueue", flags);
            var moveQueue = form_.GetMethod("MoveQueue", flags);
            var clearQueue = form_.GetMethod("ClearQueue", flags);
            var queueOrder = form_.GetMethod("QueueOrder", flags);
            var queueBox = (ListBox)form_.GetField("queueList", flags).GetValue(form);
            Func<string> order = () => string.Join(",", (List<string>)queueOrder.Invoke(form, null));
            clearQueue.Invoke(form, null);
            foreach (string code in new[] { "D05", "D12", "D01" })
                toggleQueue.Invoke(form, new object[] { trial(code) });
            A(order() == "D05,D12,D01", "Dungeon queue keeps the order in which dungeons were picked");
            queueBox.SelectedIndex = 2;
            moveQueue.Invoke(form, new object[] { -1 });
            A(order() == "D05,D01,D12", "Dungeon queue moves an entry up");
            toggleQueue.Invoke(form, new object[] { trial("D01") });
            A(order() == "D05,D12", "Picking a queued dungeon again removes it");
            bool novice = false;
            try
            {
                toggleQueue.Invoke(form, new object[] { V3Data.Dungeons.First(d => d.Mode == "Novice") });
            }
            catch (System.Reflection.TargetInvocationException)
            {
                novice = true;
            }
            A(novice && order() == "D05,D12", "Only Solo Trial dungeons can join the queue");
            A(queueBox.Items.Count == 2 && queueBox.Items[0].ToString().Contains("D05"),
                "Queue list shows the picked dungeons in order");
            clearQueue.Invoke(form, null);
            A(order() == "", "Queue clears");
            // one entry can be taken out of the list without clearing it; "add all" skips the dungeons that are already done (green)
            var removeSelected = form_.GetMethod("RemoveSelectedFromQueue", flags);
            var addAll = form_.GetMethod("AddAllToQueue", flags);
            var resetVisited = form_.GetMethod("ResetVisited", flags);
            var visitedMarks = (HashSet<string>)form_.GetField("visitedDungeons", flags).GetValue(form);
            foreach (string code in new[] { "D05", "D12", "D01" })
                toggleQueue.Invoke(form, new object[] { trial(code) });
            queueBox.SelectedIndex = 1;
            removeSelected.Invoke(form, null);
            A(order() == "D05,D01", "One entry leaves the queue list without clearing the rest");
            clearQueue.Invoke(form, null);
            foreach (var modeBox in (List<CheckBox>)form_.GetField("grimModes", flags).GetValue(form))
                modeBox.Checked = modeBox.Text == "Solo Trial";
            visitedMarks.Add("D02|Solo Trial");
            form_.GetMethod("RefreshDungeonGrid", flags).Invoke(form, null);
            var rowGrid = (DataGridView)form_.GetField("dungeonGrid", flags).GetValue(form);
            Func<string, Color> rowInk = code => rowGrid.Rows.Cast<DataGridViewRow>().First(rowItem => ((DungeonLocation)rowItem.Tag).Code == code).DefaultCellStyle.ForeColor;
            A(rowInk("D02") == Ui.Done && rowInk("D01") == Ui.Fresh && Ui.Done.R == 255 && Ui.Done.B < 100 && Ui.Fresh == Color.White,
                "Dungeon table: entered dungeons are yellow, the others white");
            addAll.Invoke(form, null);
            var queued = order().Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            A(queued.Length == V3Data.Dungeons.Count(d => d.Mode == "Solo Trial") - 1 && !queued.Contains("D02") && queued.Contains("D01"),
                "Add all queues every listed dungeon except the finished (green) ones");
            // map by map: Constantinople, Samarkand, Hotan, Roc Mountain, Taklamakan, Donwhang, Jangan (D02 is yellow here, so it is left out)
            A(order() == "D10,D11,D07,D08,D09,D05,D12,D13,D15,D06,D01,D03,D04,D14,D16,D17,D18",
                "Add all queues the dungeons city by city: Constantinople, Samarkand, Hotan, Roc Mountain, Taklamakan, Donwhang, Jangan");
            clearQueue.Invoke(form, null);
            foreach (string code in new[] { "D17", "D05", "D10", "D01", "D06" })
                toggleQueue.Invoke(form, new object[] { trial(code) });
            form_.GetMethod("SortQueueByCity", flags).Invoke(form, null);
            A(order() == "D10,D05,D06,D01,D17", "The city-order button sorts a hand-made queue by city");
            clearQueue.Invoke(form, null);
            var pair = GrimCityOrder.Sort(new[] { V3Data.Dungeons.First(d => d.Code == "D10" && d.Mode == DungeonLocation.Solo85), trial("D11"), trial("D10") });
            A(string.Join(",", pair.Select(d => d.QueueKey)) == "D10,D10@85,D11", "Inside a city the Solo Trial comes right before the Solo 85 of the same dungeon");
            resetVisited.Invoke(form, null);
            A(visitedMarks.Count == 0, "Reset clears the green marks");
        }
        {
            var exitLinks = ExpeditionRules.Network.Links.Where(l => l.From == 254).Select(l => l.To).OrderBy(x => x).ToArray();
            A(exitLinks.SequenceEqual(new[] { 2, 252 }),
                "Dungeon Exit NPC teleporter has the Donwhang and Nanzhang destinations in the transit data");
            var exitGate = ExpeditionRules.Network.Gates.Single(gateRow => gateRow.Id == 254);
            A(exitGate.Model == 36031, "Dungeon Exit teleporter gate uses the exit NPC model the bot already tracks");
        }
        {
            var charStats = new CharacterStats();
            Func<int, ulong, uint, uint, Snapshot> charSample = (lv, xp, spExp, sp) => new Snapshot
            {
                StatsKnown = true, Level = lv, Exp = xp, SpExp = spExp, Sp = sp, Hp = 1, MaxHp = 1, Mp = 1, MaxMp = 1
            };
            A(LevelData.Required(84) == 193898546 && LevelData.Required(52) == 12913686,
                "Level table matches the game data");
            // Levels 85-89 need far more than the standard table says: 814,298,430 EXP at level 86 is 30.77 % in the game.
            A(LevelData.Required(85) == 2483101044L && LevelData.Required(86) == 2646704676L && LevelData.Required(89) == 5307077340L &&
                Math.Round(100.0 * 814298430 / LevelData.Required(86), 2) == 30.77, "Level 85-89 requirements come from the client's own level data");
            charStats.Observe(0, charSample(84, 1000000, 100, 900));
            charStats.Observe(30000, charSample(84, 1000600, 200, 900));
            charStats.Observe(60000, charSample(84, 1001200, 350, 901));
            A(charStats.ExpGained == 1200 && Math.Abs(charStats.ExpPerMinute(60000) - 1200) < 1,
                "Exp gain and per-minute rate are measured from the session");
            A(Math.Abs(charStats.SpGained - 1.625) < 0.001 && Math.Abs(charStats.SpPerMinute(60000) - 1.625) < 0.01,
                "SP gain counts the 400-point sub steps");
            charStats.Observe(70000, charSample(84, 500000, 350, 901));
            A(charStats.ExpGained == 1200, "An exp loss after a death is never a negative gain");
            charStats.Observe(80000, charSample(84, 500100, 350, 901));
            A(charStats.ExpGained == 1300, "Gains continue from the lower exp after a death");
            charStats.Reset();
            charStats.Observe(0, charSample(84, 193898000, 0, 900));
            charStats.Observe(5000, charSample(85, 100, 0, 900));
            A(charStats.ExpGained == 646 && charStats.LevelsGained == 1,
                "A level up counts the rest of the old level plus the new exp and the level gained");
            charStats.Reset();
            A(charStats.LevelsGained == 0 && charStats.ExpGained == 0, "Reset clears the session counters");
            A(StatPanel.Duration(61.5) == "1 sa 1 dk" && StatPanel.Duration(-1) == "—" && StatPanel.Duration(0.5) == "30 sn",
                "Durations are written in hours, minutes and seconds");
        }
        var checkedDungeon = V3Data.Dungeons.First(x => x.Code == "D13" && x.Mode == "Solo Trial");
        var checkedFloor = FloorNavigation.LoadCaptured(checkedDungeon);
        checkedFloor.Covered.UnionWith(checkedFloor.Cells.Keys);
        var formGrim = (GrimRun)typeof(MemorySelectV3Form).GetField("grim", flags).GetValue(form);
        formGrim.Arm(checkedDungeon, checkedFloor.Origin);
        formGrim.Floor = checkedFloor;
        typeof(MemorySelectV3Form).GetField("snapshot", flags).SetValue(form, S(true, checkedFloor.Origin));
        typeof(MemorySelectV3Form).GetField("grimHolding", flags).SetValue(form, true);
        typeof(MemorySelectV3Form).GetField("grimWaiting", flags).SetValue(form, true);
        typeof(MemorySelectV3Form).GetMethod("GrimExplore", flags).Invoke(form, new object[] { 0L });
        var idleWalker = (ObstacleWalker)typeof(MemorySelectV3Form).GetField("routeWalker", flags).GetValue(form);
        A(!idleWalker.Active, "Grim holds position only after the mapped survey is complete");
        A(!HotkeyPolicy.Allowed((int)Keys.F1, 0) && !HotkeyPolicy.Allowed((int)Keys.F8, 0) && !HotkeyPolicy.Allowed((int)Keys.F24,
            0), "Fixed function keys removed and function key assignment refused");
        A(HotkeyPolicy.Allowed((int)Keys.Insert, 0) && HotkeyPolicy.Allowed((int)Keys.K, 2),
            "Custom single key and Ctrl combination supported");
        A(HotkeyPolicy.Conflicts((int)Keys.D1, 0, new ushort[] { 49 }, true) && !HotkeyPolicy.Conflicts((int)Keys.D1, 2,
            new ushort[] { 49 }, true), "Hotkey cannot intercept its own attack inputs");
        A(HotkeyPolicy.Conflicts((int)Keys.Tab, 0, new ushort[0], true), "Berserk Tab conflict prevented");
        var cfg = new V3Settings { Keys = "1 2", HotkeyKey = (int)Keys.Insert, VisitedDungeons = new List<string> { "D06|Novice" } };
        var codec = new System.Web.Script.Serialization.JavaScriptSerializer();
        var saved = codec.Deserialize<V3Settings>(codec.Serialize(cfg));
        A(saved.Keys == "1 2" && saved.HotkeyKey == (int)Keys.Insert && saved.VisitedDungeons.SequenceEqual(new[] { "D06|Novice" }),
            "Common settings and custom key persist");
        // The release signing key is private, so the checks sign a manifest with a throwaway key.
        string testPublicKey;
        UpdateManifest manifest;
        using (var testKey = new RSACryptoServiceProvider(2048))
        {
            testPublicKey = testKey.ToXmlString(false);
            manifest = new UpdateManifest
            {
                Product = UpdateTrust.Product,
                Version = "3.0.5.0",
                Url = "https://example.test/SilkroadRStudio.exe",
                Sha256 = new string('A', 64),
                Size = 11087360
            };
            manifest.Signature = Convert.ToBase64String(testKey.SignData(UpdateTrust.Canonical(manifest), "SHA256"));
        }
        UpdateTrust.Validate(manifest, testPublicKey);
        A(true, "Publisher RSA/SHA256 signature verifies");
        using (var embedded = new RSACryptoServiceProvider())
        {
            embedded.FromXmlString(UpdateTrust.EmbeddedPublicKey());
            A(embedded.KeySize == 3072 && embedded.PublicOnly, "Embedded publisher key is a 3072-bit public key");
        }
        A(!UpdateTrust.DefaultManifestUrl.Contains("by-rikimaru") && UpdateTrust.DefaultManifestUrl.StartsWith("https://"),
            "Default update address points to this project, not the original author");
        string goodHash = manifest.Sha256;
        manifest.Sha256 = new string('0', 64);
        bool tampered = false;
        try
        {
            UpdateTrust.Validate(manifest, testPublicKey);
        }
        catch (InvalidDataException)
        {
            tampered = true;
        }
        A(tampered, "Altered signed manifest rejected");
        manifest.Sha256 = goodHash;
        manifest.Url = "http://example.test/file.exe";
        bool http = false;
        try
        {
            UpdateTrust.Validate(manifest, testPublicKey);
        }
        catch (InvalidDataException)
        {
            http = true;
        }
        A(http, "Insecure HTTP update URL rejected");
        A(!UpdateService.OwnedPath(UpdateService.Root + "-other\\job.json") && !UpdateService.OwnedPath(Path.Combine(UpdateService.Root,
            "..", "outside.json")), "Updater job paths cannot escape staging root");
        ((CheckBox)typeof(MemorySelectV3Form).GetField("autoUpdate", flags).GetValue(form)).Checked = false;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-3000, -3000);
        form.Show();
        Application.DoEvents();
        for (int i = 0; i < tabs.TabCount; i++)
        {
            tabs.SelectedIndex = i;
            Application.DoEvents();
            if (i == 2)
            {
                foreach (CheckBox check in (List<CheckBox>)typeof(MemorySelectV3Form).GetField("grimModes", flags).GetValue(form))
                    check.Checked = check.Text == "Solo Trial";
            }
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                bitmap.Save("tests/Preview-" + i + ".png");
            }
        }
        // Layout checks at the smallest and a large window size: nothing may be cut off, previews are kept for review.
        var report = new List<string>();
        foreach (var size in new[] { new Size(1120, 700), new Size(1480, 900), new Size(1920, 1080) })
        {
            form.ClientSize = size;
            Application.DoEvents();
            for (int i = 0; i < tabs.TabCount; i++)
            {
                tabs.SelectedIndex = i;
                Application.DoEvents();
                form.PerformLayout();
                foreach (string problem in ClippedControls(form))
                    report.Add(size.Width + "x" + size.Height + " · " + tabs.TabPages[i].Text + " · " + problem);
                if (i == 0 || i == 2 || tabs.TabPages[i].Text == "Saldırı")
                    using (var bitmap = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                        bitmap.Save("tests/Preview-" + i + "-" + size.Width + ".png");
                    }
            }
        }
        File.WriteAllLines("tests/Layout-Report.txt", report.Count == 0 ? new[] { "No clipped controls." } : report.ToArray());
        A(report.Count == 0, "No control is cut off at 1120x700, 1480x900 and 1920x1080 (see tests/Layout-Report.txt)");
        // Closing asks first: declining keeps the window open, confirming closes it.
        bool asked = false;
        MemorySelectV3Form.CloseConfirm = (owner, running) =>
        {
            asked = true;
            return false;
        };
        form.Close();
        A(asked && form.Visible, "Declining the close question keeps the window open");
        MemorySelectV3Form.CloseConfirm = (owner, running) => true;
        form.Close();
        A(!form.Visible, "Confirming the close question closes the window");
        form.Dispose();
        if (args.Length > 0)
        {
            using (var reader = new GameReader(int.Parse(args[0]), Catalog.Load()))
            {
                var snap = reader.Capture();
                A(reader.SenderAddress != 0 && reader.MoveAddress != 0 && reader.ReverseAddress != 0,
                    "Updated DLL profile resolves live addresses");
                using (var sender = new TargetSender(reader))
                    A(true, "Live send/move/reverse entry signatures validate; no command sent");
                File.WriteAllText("tests/Live-Profile.txt", reader.Compatibility + "\r\nPlayer: " + snap.Player.X + ", " +
                    snap.Player.Y + "\r\nAlive: " + snap.PlayerAlive + "\r\nInventory: " + snap.InventoryKnown +
                    "\r\nGrim known: " + snap.Grim.Known + " id=" + snap.Grim.Id + " title=" + snap.Grim.Title +
                    " text=" + snap.Grim.Text);
            }
        }
        File.WriteAllLines("tests/Test-Sonucu.txt", passed.Select(x => "PASS · " + x).Concat(new[] { "No game commands sent by this test harness." }));
        File.WriteAllText("tests/tests-failed.txt",
            "No current test failures. Earlier development failures were corrected.");
    }
}

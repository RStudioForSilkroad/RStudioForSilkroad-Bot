using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// Stability helpers: bounded logs, exception hooks, serialised and rate-limited calls into the game, failure counting with back-off, area-load
// and client-log watching, and the update idle gate. Everything here is plain logic that does not touch the game, so the unit checks cover it.

// events.log is kept small: rotated by size (one backup, ".1"), appended under one lock, read back only from its tail.
static class LogFiles
{
    internal const long MaxFileBytes = 2L * 1024 * 1024;
    // The in-memory event log keeps the last MemoryLines lines (it is cut back once it is MemorySlack lines over).
    internal const int MemoryLines = 2000, MemorySlack = 200, TailBytes = 200000;
    static readonly object sync = new object();

    internal static string EventLogPath
    {
        get { return Path.Combine(V3Settings.DirectoryPath, "events.log"); }
    }

    // Appends one line; a file of maxBytes or more first becomes "<path>.1" (an older ".1" is replaced).
    internal static void Append(string path, string line, long maxBytes)
    {
        lock (sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            Rotate(path, maxBytes);
            File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
        }
    }

    internal static bool Rotate(string path, long maxBytes)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < maxBytes)
                return false;
            string backup = path + ".1";
            if (File.Exists(backup))
                File.Delete(backup);
            File.Move(path, backup);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    // The last maxBytes of a file as text, without the (probably cut) first line when the file was longer.
    internal static string ReadTail(string path, int maxBytes)
    {
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            long start = Math.Max(0, stream.Length - maxBytes);
            stream.Seek(start, SeekOrigin.Begin);
            var buffer = new byte[stream.Length - start];
            int read = 0, n;
            while (read < buffer.Length && (n = stream.Read(buffer, read, buffer.Length - read)) > 0)
                read += n;
            string text = Encoding.UTF8.GetString(buffer, 0, read).TrimStart('\uFEFF');
            if (start > 0)
            {
                int cut = text.IndexOf('\n');
                if (cut >= 0)
                    text = text.Substring(cut + 1);
            }
            return text;
        }
    }
}

// The event log kept in memory: the last Keep lines (older ones are dropped once Slack more have been added). Safe to use from any thread.
sealed class CappedLog
{
    readonly StringBuilder text = new StringBuilder();
    readonly int keep, slack;
    int lines, version;

    internal CappedLog() : this(LogFiles.MemoryLines, LogFiles.MemorySlack)
    {
    }

    internal CappedLog(int keep, int slack)
    {
        this.keep = keep;
        this.slack = slack;
    }

    // Changes with every appended line (the pages that show the log compare it instead of splitting the text again).
    internal int Version
    {
        get
        {
            lock (text)
                return version;
        }
    }

    internal int Lines
    {
        get
        {
            lock (text)
                return lines;
        }
    }

    internal void AppendLine(string line)
    {
        lock (text)
        {
            text.AppendLine(line);
            version++;
            for (int i = 0; i < line.Length; i++)
                if (line[i] == '\n')
                    lines++;
            lines++;
            if (lines <= keep + slack)
                return;
            string all = text.ToString();
            int cut = 0, dropped = 0;
            while (dropped < lines - keep)
            {
                int newline = all.IndexOf('\n', cut);
                if (newline < 0)
                {
                    cut = all.Length;
                    dropped = lines;
                    break;
                }
                cut = newline + 1;
                dropped++;
            }
            text.Remove(0, cut);
            lines -= dropped;
        }
    }

    public override string ToString()
    {
        lock (text)
            return text.ToString();
    }
}

// Counts failures that follow each other: a failure more than windowMs after the previous one starts the count again.
sealed class FailureStreak
{
    long last;

    internal int Count { get; private set; }

    internal int Fail(long now, long windowMs)
    {
        Count = Count == 0 || now - last > windowMs ? 1 : Count + 1;
        last = now;
        return Count;
    }

    internal void Success()
    {
        Count = 0;
    }

    // At the end of a look at the game: a look that raised no failure ends the streak.
    internal void EndTick(bool failed)
    {
        if (!failed)
            Count = 0;
    }
}

// A call into the game that failed in a way that may pass (a timeout, a result nobody expected) is tried again after a short wait;
// after RemoteFailureLimit failures in a row the job is stopped.
static class RemoteBackoff
{
    internal const int StepMs = 400, MaxSteps = 5;

    internal static int DelayMs(int failures)
    {
        return failures <= 0 ? 0 : Math.Min(failures, MaxSteps) * StepMs;
    }
}

// Thrown instead of calling the game while the back-off after a failed call lasts; it is not a new failure.
sealed class RemoteBackoffException : IOException
{
    internal RemoteBackoffException(string message) : base(message)
    {
    }
}

// The game's process is gone (it exited or crashed) while a call was being made: a clean game exit, not a failure of the connection.
sealed class GameGoneException : Exception
{
    internal GameGoneException(int pid) : base("Oyun kapandı (uzak çağrı sırasında süreç kayboldu) · PID " + pid)
    {
    }
}

// A call into the game that did not finish in time (or whose predecessor had not finished).
sealed class RemoteTimeoutException : IOException
{
    internal RemoteTimeoutException(string message) : base(message)
    {
    }
}

// The game answered, but with something other than 0 / 1. A walkability query does that for stairs and ramps (the callers handle it), so it is a
// finished call, not a failure of the connection.
sealed class UnexpectedResultException : IOException
{
    internal UnexpectedResultException(string message) : base(message)
    {
    }
}

// One sender's calls into the game: one at a time (process-wide lock), at least RemoteGate.MinGapMs after the one before, counted. A call that
// fails (timeout, no memory, thread not started) starts a back-off during which calls throw RemoteBackoffException (not a new failure); a
// success ends it. The clock and the log are given, so the rules can be checked without a game.
sealed class RemoteCaller
{
    const long FailureWindowMs = 10000;
    readonly FailureStreak failures = new FailureStreak();
    readonly int pid, limit;
    readonly Func<long> clock;
    readonly Action<string> log;
    readonly RemoteJournal journal;
    readonly Func<bool> alive;
    long backoffUntil;
    bool backoffNoted;

    // alive tells whether the game's process still exists (asked only after a call failed); null: always taken as alive.
    internal RemoteCaller(int pid, int limit, Func<long> clock, Action<string> log, RemoteJournal journal = null, Func<bool> alive = null)
    {
        this.alive = alive;
        this.pid = pid;
        this.limit = limit;
        this.clock = clock;
        this.log = log;
        this.journal = journal;
    }

    internal static bool ProcessAlive(int pid)
    {
        try
        {
            using (var process = Process.GetProcessById(pid))
                return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Win32Exception)
        {
            return true;
        }
    }

    bool Gone()
    {
        try
        {
            return alive != null && !alive();
        }
        catch (Exception)
        {
            return false;
        }
    }

    internal int Failures
    {
        get { return failures.Count; }
    }

    internal bool BackingOff
    {
        get { return failures.Count > 0 && clock() < backoffUntil; }
    }

    internal bool FailureLimitReached
    {
        get { return failures.Count >= limit; }
    }

    // kind names the call in the journal (null: "send" for a packet, "call" otherwise; a batch is "probe").
    internal void Run(bool batch, int calls, Action call, string kind = null)
    {
        lock (RemoteGate.Lock)
        {
            if (BackingOff)
            {
                // one journal line for a whole back-off, not one per refused call
                if (!backoffNoted)
                    Journal(kind, batch, calls, 0, 0, "backoff");
                backoffNoted = true;
                throw new RemoteBackoffException("Oyun çağrıları art arda başarısız oldu; kısa süre beklenip yeniden denenecek (" +
                    failures.Count + "/" + limit + ").");
            }
            backoffNoted = false;
            int gap = RemoteGate.WaitGap();
            RemoteJournal.Op = -1;
            RemoteJournal.Len = 0;
            long started = clock();
            string result = "ok";
            try
            {
                call();
            }
            catch (UnexpectedResultException)
            {
                // the call was made and answered
                result = "unexpected";
                failures.Success();
                RemoteTrace.Note(pid, batch, calls, clock() - started);
                throw;
            }
            catch (RemoteTimeoutException e)
            {
                result = "timeout";
                Failed(e);
                throw;
            }
            catch (IOException e)
            {
                result = "error";
                if (Gone())
                {
                    result = "gone";
                    throw new GameGoneException(pid);
                }
                Failed(e);
                throw;
            }
            catch (Win32Exception e)
            {
                result = "error";
                if (Gone())
                {
                    result = "gone";
                    throw new GameGoneException(pid);
                }
                Failed(e);
                throw;
            }
            catch (Exception)
            {
                result = "invalid";
                throw;
            }
            finally
            {
                RemoteGate.Leave();
                Journal(kind, batch, calls, clock() - started, gap, result);
            }
            failures.Success();
            RemoteTrace.Note(pid, batch, calls, clock() - started);
        }
    }

    void Journal(string kind, bool batch, int calls, long ms, int gap, string result)
    {
        if (journal == null)
            return;
        try
        {
            int op = RemoteJournal.Op;
            journal.Call(DateTime.Now, pid, op, RemoteJournal.Len, kind ?? (batch ? "probe" : op >= 0 ? "send" : "call"), calls, ms, gap, result);
        }
        catch { }
    }

    void Failed(Exception e)
    {
        int count = failures.Fail(clock(), FailureWindowMs);
        backoffUntil = clock() + RemoteBackoff.DelayMs(count);
        RemoteTrace.Failed();
        log("Uzak çağrı başarısız (" + count + "/" + limit + ") · PID " + pid + " · " + e.Message);
    }
}

// Every call into the game (select, move, use, navigation probes) goes through one process-wide lock, and two calls are at least MinGapMs
// apart: the game never sees two injected calls at once or back to back. What is sent, and in which order, is not changed.
static class RemoteGate
{
    internal const int MinGapMs = 15;
    internal static readonly object Lock = new object();
    static readonly Stopwatch clock = Stopwatch.StartNew();
    static long lastEnd = -1000000;

    internal static long NowMs
    {
        get { return clock.ElapsedMilliseconds; }
    }

    // How long to wait before the next call may start (0 when the gap has passed).
    internal static int Remaining(long now, long lastEnd, int gapMs)
    {
        long wait = gapMs - (now - lastEnd);
        return wait <= 0 ? 0 : wait > gapMs ? gapMs : (int)wait;
    }

    // Call with the lock held. Returns how long it waited.
    internal static int WaitGap()
    {
        int wait = Remaining(NowMs, lastEnd, MinGapMs);
        if (wait > 0)
            Thread.Sleep(wait);
        return wait;
    }

    internal static void Leave()
    {
        lastEnd = NowMs;
    }
}

// A journal of every call into the game, one line each with millisecond time, written at once to remote-calls.log (next to events.log;
// rotated at 2 MB, one ".1" backup), so the last call before a crash of the game is on disk. A line holds the process id, the packet opcode
// and payload LENGTH when the call carries a packet (never the payload or any account data), the kind, the duration, the wait for the
// minimum gap and the result (ok / timeout / unexpected / error / invalid / backoff). Past LinesPerSecond lines in one second, further
// successful probes are collapsed into one "probe xN" line, written when the burst ends (or every 250 ms of it). A writer that throws is ignored.
sealed class RemoteJournal
{
    internal const int LinesPerSecond = 20;
    const long CollapseFlushTicks = 250 * TimeSpan.TicksPerMillisecond;
    internal static readonly RemoteJournal Default = new RemoteJournal(WriteFile);
    // What the call in progress reports about itself (set under RemoteGate.Lock by the call and by BuildPacket).
    internal static int Op = -1, Len;
    readonly Action<string> sink;
    long second = -1, collapseStart;
    int lines, collapsedQueries, collapsedCalls, collapsedPid;
    long collapsedMs;
    DateTime collapsedLast;

    internal RemoteJournal(Action<string> sink)
    {
        this.sink = sink;
    }

    internal static string FilePath
    {
        get { return Path.Combine(V3Settings.DirectoryPath, "remote-calls.log"); }
    }

    static void WriteFile(string line)
    {
        LogFiles.Append(FilePath, line, LogFiles.MaxFileBytes);
    }

    internal static string Format(DateTime time, int pid, int op, int length, string kind, int count, long ms, long gapMs, string result)
    {
        var line = new StringBuilder(time.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append(" pid=").Append(pid);
        if (op >= 0)
            line.Append(" op=").Append(op.ToString("X4")).Append(" len=").Append(length);
        line.Append(" kind=").Append(kind);
        if (count > 1)
            line.Append(" x").Append(count);
        line.Append(' ').Append(ms).Append("ms");
        if (gapMs > 0)
            line.Append(" gap=").Append(gapMs).Append("ms");
        return line.Append(' ').Append(result).ToString();
    }

    internal void Call(DateTime time, int pid, int op, int length, string kind, int count, long ms, long gapMs, string result)
    {
        lock (this)
        {
            long now = time.Ticks / TimeSpan.TicksPerSecond;
            if (now != second)
            {
                FlushCollapsed();
                second = now;
                lines = 0;
            }
            if (kind == "probe" && result == "ok" && lines >= LinesPerSecond)
            {
                if (collapsedCalls == 0)
                    collapseStart = time.Ticks;
                collapsedCalls++;
                collapsedQueries += count;
                collapsedMs += ms;
                collapsedPid = pid;
                collapsedLast = time;
                if (time.Ticks - collapseStart >= CollapseFlushTicks)
                    FlushCollapsed();
                return;
            }
            FlushCollapsed();
            lines++;
            Emit(Format(time, pid, op, length, kind, count, ms, gapMs, result));
        }
    }

    // A line that is not a call (attach, detach, the game exited, crash.log changed).
    internal void Marker(DateTime time, int pid, string text)
    {
        lock (this)
        {
            FlushCollapsed();
            Emit(time.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " pid=" + pid + " -- " + text);
        }
    }

    internal void Flush()
    {
        lock (this)
            FlushCollapsed();
    }

    void FlushCollapsed()
    {
        if (collapsedCalls == 0)
            return;
        Emit(collapsedLast.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " pid=" + collapsedPid + " kind=probe x" + collapsedQueries + " in " + collapsedCalls +
            " calls (burst) " + collapsedMs + "ms total ok");
        collapsedCalls = collapsedQueries = 0;
        collapsedMs = 0;
    }

    void Emit(string line)
    {
        try
        {
            sink(line);
        }
        catch { }
    }
}

// The calls made into the game, counted: one line in events.log about every TraceEveryMs (not on screen), so a crash of the game can be matched
// with what the bot was doing. Failures are written at once.
static class RemoteTrace
{
    internal const long TraceEveryMs = 10000;
    // Where a summary goes (events.log); the unit checks replace it.
    internal static Action<string> Sink = StabilityLog.Write;
    static int calls, batches, probes, failed, lastPid;
    static long worst, windowStart = -1, lastCall;

    // Called with RemoteGate.Lock held. A summary is written when the window is TraceEveryMs old or the calls paused for that long; its
    // length is the time from the first to the last call (not the idle time after it).
    internal static void Note(int pid, bool batch, int count, long elapsedMs)
    {
        long now = RemoteGate.NowMs;
        lastPid = pid;
        if (windowStart >= 0 && now - lastCall > TraceEveryMs)
            Write();
        if (windowStart < 0)
            windowStart = now;
        lastCall = now;
        calls++;
        if (batch)
        {
            batches++;
            probes += count;
        }
        worst = Math.Max(worst, elapsedMs);
        if (now - windowStart >= TraceEveryMs)
            Write();
    }

    internal static void Failed()
    {
        long now = RemoteGate.NowMs;
        if (windowStart < 0)
            windowStart = now;
        lastCall = now;
        failed++;
    }

    // Writes what has been counted so far (when the game is let go, so the last calls before a detach are in the log).
    internal static void Flush()
    {
        lock (RemoteGate.Lock)
            Write();
    }

    static void Write()
    {
        if (windowStart >= 0 && (calls > 0 || failed > 0))
            Sink(Format(lastPid, Math.Max(1, (lastCall - windowStart) / 1000), calls, batches, probes, worst, failed));
        calls = batches = probes = failed = 0;
        worst = 0;
        windowStart = -1;
    }

    internal static string Format(int pid, long seconds, int calls, int batches, int probes, long worst, int failed)
    {
        return "Uzak çağrı özeti · PID " + pid + " · " + seconds + " sn: " + calls + " çağrı (" + batches + " toplu, " + probes +
            " sorgu) · en uzun " + worst + " ms · hata " + failed;
    }
}

// Lets a cause through once per period (the same swallowed error is logged once a minute, not on every tick).
sealed class Throttle
{
    readonly Dictionary<string, long> last = new Dictionary<string, long>();

    internal bool Allow(string cause, long now, long periodMs)
    {
        lock (last)
        {
            long before;
            if (last.TryGetValue(cause, out before) && now - before < periodMs)
                return false;
            if (last.Count > 64)
                last.Clear();
            last[cause] = now;
            return true;
        }
    }
}

// Writes straight to events.log (the same line format as the window's own log), for code that has no window: attach / detach, the call
// summaries, errors nobody caught.
static class StabilityLog
{
    static readonly Throttle repeated = new Throttle();
    static readonly Stopwatch clock = Stopwatch.StartNew();

    internal static void Write(string text)
    {
        try
        {
            LogFiles.Append(LogFiles.EventLogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " · " + text, LogFiles.MaxFileBytes);
        }
        catch { }
    }

    // The whole exception, written before anything else happens. The job is not stopped and no dialog is shown.
    internal static void Report(string source, Exception e)
    {
        try
        {
            if (e == null || !repeated.Allow(e.GetType().FullName + ": " + e.Message, clock.ElapsedMilliseconds, 5000))
                return;
            Write("İşlenmeyen hata (" + source + ") · " + e);
        }
        catch { }
    }

    internal static void InstallExceptionHooks()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += delegate(object s, ThreadExceptionEventArgs t) { Report("arayüz iş parçacığı", t.Exception); };
        AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs u)
        {
            Report("uygulama alanı" + (u.IsTerminating ? " · program kapanıyor" : ""), u.ExceptionObject as Exception ??
                new InvalidOperationException(Convert.ToString(u.ExceptionObject)));
        };
    }
}

// Tells a change of area (a teleport, a gate, a dungeon, a new world) from walking: the character is somewhere else at once (more than
// JumpDistance away), or an indoor region changed, or the game's memory could not be read for a while (the world was loading).
sealed class AreaWatch
{
    // Commands wait SettleMs after a change; the window-waking code stays quiet for QuietMs.
    internal const long SettleMs = 5000, QuietMs = 10000;
    internal const double JumpDistance = 96;
    bool known, outside;
    ushort region;
    double x, y;
    long changedAt = -1;

    internal void Observe(Position p, long now)
    {
        if (p == null)
            return;
        if (known)
        {
            double jump = Math.Sqrt((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y));
            if (jump > JumpDistance || (p.Region != region && (!p.Outside || !outside)))
                changedAt = now;
        }
        known = true;
        region = p.Region;
        outside = p.Outside;
        x = p.X;
        y = p.Y;
    }

    internal void Loaded(long now)
    {
        changedAt = now;
    }

    internal bool Within(long now, long ms)
    {
        return changedAt >= 0 && now - changedAt < ms;
    }

    internal void Reset()
    {
        known = false;
        changedAt = -1;
    }
}

// A new version is installed only when the bot has nothing to do (no hunt, no armed or running dungeon, no queue, no lottery).
static class UpdateGate
{
    // An update that has waited this long may go in even though the bot is busy (a job that runs round the clock must not hold it for ever).
    internal const long MaxDeferralMs = 6L * 60 * 60 * 1000;

    internal static bool Idle(bool active, bool grimArmed, bool queueRunning, bool lotteryRunning, bool resumeArmed)
    {
        return !active && !grimArmed && !queueRunning && !lotteryRunning && !resumeArmed;
    }

    // Idle, or overdue and at a safe moment (no item transaction, no death recovery, no resume being connected or started).
    internal static bool MayApply(bool idle, long deferredMs, bool transactionPending, bool recoveryBusy, bool resumeBusy)
    {
        if (transactionPending || recoveryBusy || resumeBusy)
            return false;
        return idle || deferredMs >= MaxDeferralMs;
    }
}

// Watches the game's own logs while a job runs: a "Disconnected by the server" line added to client.log, or a crash.log that grows or appears.
sealed class ClientLogWatch
{
    internal const long PollMs = 5000;
    const int ReadLimit = 65536;
    string clientLog, crashLog;
    long clientOffset, crashLength, nextPoll;
    DateTime crashWrite;

    // The last reason Poll gave was crash.log (it may be another client's: the caller checks this client before stopping).
    internal bool LastWasCrash { get; private set; }
    // Polls in a row that could not read the logs.
    internal int Failures { get; private set; }

    internal void Begin(string gameDirectory, long now)
    {
        clientLog = crashLog = null;
        if (string.IsNullOrEmpty(gameDirectory))
            return;
        try
        {
            string client = Path.Combine(gameDirectory, "logs", "client.log"), crash = Path.Combine(gameDirectory, "logs", "crash.log");
            var c = new FileInfo(client);
            var k = new FileInfo(crash);
            clientOffset = c.Exists ? c.Length : 0;
            crashLength = k.Exists ? k.Length : -1;
            crashWrite = k.Exists ? k.LastWriteTimeUtc : DateTime.MinValue;
            clientLog = client;
            crashLog = crash;
            nextPoll = now + PollMs;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal static bool HasDisconnect(string text)
    {
        return text != null && text.IndexOf("Disconnected by the server", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // The reason to stop, or null.
    internal string Poll(long now)
    {
        if (clientLog == null || now < nextPoll)
            return null;
        nextPoll = now + PollMs;
        LastWasCrash = false;
        try
        {
            string reason = Check();
            Failures = 0;
            return reason;
        }
        catch (IOException) { Failures++; }
        catch (UnauthorizedAccessException) { Failures++; }
        return null;
    }

    string Check()
    {
        var k = new FileInfo(crashLog);
        long length = k.Exists ? k.Length : -1;
        DateTime write = k.Exists ? k.LastWriteTimeUtc : DateTime.MinValue;
        if (length != crashLength || write != crashWrite)
        {
            crashLength = length;
            crashWrite = write;
            LastWasCrash = true;
            return "Oyunun crash.log dosyasına yeni kayıt eklendi (istemci çöktü).";
        }
        var c = new FileInfo(clientLog);
        if (!c.Exists)
            return null;
        if (c.Length < clientOffset)
            clientOffset = 0;
        if (c.Length == clientOffset)
            return null;
        long from = Math.Max(clientOffset, c.Length - ReadLimit);
        string added;
        using (var stream = new FileStream(clientLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            stream.Seek(from, SeekOrigin.Begin);
            var buffer = new byte[Math.Max(0, stream.Length - from)];
            int read = 0, n;
            while (read < buffer.Length && (n = stream.Read(buffer, read, buffer.Length - read)) > 0)
                read += n;
            added = Encoding.UTF8.GetString(buffer, 0, read);
            clientOffset = from + read;
        }
        if (HasDisconnect(added))
            return "Oyun sunucudan koptu (client.log: Disconnected by the server).";
    
        return null;
    }
}

sealed partial class MemorySelectV3Form
{
    readonly Throttle logThrottle = new Throttle();
    readonly AreaWatch areaWatch = new AreaWatch();
    readonly ClientLogWatch clientLogWatch = new ClientLogWatch();
    readonly FailureStreak tickFailures = new FailureStreak();
    // An IOException in a look at the game (a call that timed out, a settings file held by a virus scanner) is tried again; this many in a
    // row (each within TickFailureWindowMs of the one before) stop the job.
    internal const int TickFailureLimit = 5;
    const long TickFailureWindowMs = 10000;

    // The same cause is logged once a minute (for errors that are swallowed on purpose).
    void LogOnce(string cause, string text)
    {
        if (logThrottle.Allow(cause, clock.ElapsedMilliseconds, 60000))
            Log(text);
    }

    // True when the look at the game may go on after this exception: a transient IOException is logged and tried again on the next tick.
    bool RetryAfterIoError(Exception e, long now)
    {
        ReleaseKey();
        if (e is RemoteBackoffException)
        {
            LogOnce("remote-backoff", e.Message);
            return true;
        }
        int failures = tickFailures.Fail(now, TickFailureWindowMs);
        Log("Geçici G/Ç hatası (" + failures + "/" + TickFailureLimit + "): " + e.Message);
        return failures < TickFailureLimit;
    }

    // A reason to stop from the game's own logs, or null. A crash.log entry counts only when this client is gone or its window hangs (the
    // file is shared with every client of the same game folder); otherwise it is only logged.
    string ClientLogProblem(long now)
    {
        string problem = clientLogWatch.Poll(now);
        if (problem != null)
            RemoteJournal.Default.Marker(DateTime.Now, reader.Process.Id, clientLogWatch.LastWasCrash ? "crash.log changed" : "client.log: disconnected");
        if (clientLogWatch.Failures >= 3)
            LogOnce("clientlog-poll", "Oyunun client.log / crash.log dosyaları okunamıyor; kopma ve çökme bu yolla izlenemiyor.");
        if (problem == null || !clientLogWatch.LastWasCrash)
            return problem;
        try
        {
            reader.Process.Refresh();
            if (reader.Process.HasExited || hungSince != 0 || IsClientHung())
                return problem;
        }
        catch (Exception)
        {
            return problem;
        }
        LogOnce("crash-log", problem + " Bu istemci çalışıyor ve yanıt veriyor; iş sürüyor.");
        return null;
    }

    // The game's state allows commands: its area is not loading, its window answers, calls into it are not backing off, the character exists.
    bool GameStateReady()
    {
        if (reader == null)
            return false;
        if (areaWatch.Within(clock.ElapsedMilliseconds, AreaWatch.SettleMs))
            return false;
        if (sender != null && sender.BackingOff)
            return false;
        if (active ? hungSince != 0 : IsClientHung())
            return false;
        return reader.PlayerPointerValid();
    }

    bool IsClientHung()
    {
        var window = ClientWindow();
        return window != IntPtr.Zero && IsHungAppWindow(window);
    }
}

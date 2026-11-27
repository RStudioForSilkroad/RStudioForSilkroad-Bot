using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

// Getting back to work after the game closed. The launcher's own AutoLogin starts the game again and enters the character; this plan waits for
// that new game, connects to it when it holds the same character, and starts the job that was running (hunting in its area, or a caravan road,
// which goes on from the nearest point). No password is ever handled here.
sealed class ResumePlan
{
    internal enum Phase { Off, Waiting, Found, Ready }
    internal enum Step { Nothing, Connect, Start, Cancel }

    // How long to wait for the game to come back, how long a game that has opened may take to enter the world, how long to let the character settle
    // before the job starts, and how long a dead character is waited for.
    internal const long MaxWaitMs = 30 * 60 * 1000, EnterWorldMs = 4 * 60 * 1000, SettleMs = 15000, DeadWaitMs = 2 * 60 * 1000;
    internal const long LookEveryMs = 3000, TryEveryMs = 4000, FirstTryMs = 8000;
    // At most this many resumes inside the window: a game that keeps closing is not restarted for ever.
    internal const int MaxResumes = 5;
    internal const long ResumeWindowMs = 30 * 60 * 1000;

    internal Phase Current { get; private set; }
    internal OperationMode Mode { get; private set; }
    internal string Character { get; private set; }
    internal string From { get; private set; }
    internal string To { get; private set; }
    internal int OldPid { get; private set; }
    internal int FoundPid { get; private set; }
    // The last reason the plan ended or refused to start, for the log.
    internal string Why { get; private set; }

    readonly HashSet<int> rejected = new HashSet<int>();
    readonly List<long> resumes = new List<long>();
    long armedAt, nextLook, foundAt, nextTry, readyAt, deadSince;

    internal bool Armed
    {
        get { return Current != Phase.Off; }
    }

    // The game closed while a job was running. A hunt needs its character; a caravan road its two towns. False when the plan is not made
    // (too many resumes lately).
    internal bool Arm(long now, OperationMode mode, string character, string from, string to, int oldPid)
    {
        resumes.RemoveAll(t => now - t > ResumeWindowMs);
        if (resumes.Count >= MaxResumes)
        {
            Why = "son " + ResumeWindowMs / 60000 + " dakikada " + MaxResumes + " kez devam edildi; oyun sürekli kapanıyor";
            Current = Phase.Off;
            return false;
        }
        Mode = mode;
        Character = character ?? "";
        From = from;
        To = to;
        OldPid = oldPid;
        FoundPid = 0;
        rejected.Clear();
        armedAt = now;
        nextLook = now + LookEveryMs;
        Current = Phase.Waiting;
        Why = null;
        return true;
    }

    internal void Cancel(string why)
    {
        Current = Phase.Off;
        Why = why;
    }

    internal bool TimeToLook(long now)
    {
        if (Current != Phase.Waiting || now < nextLook)
            return false;
        nextLook = now + LookEveryMs;
        return true;
    }

    // A game process that is not the closed one and was not turned down.
    internal bool Candidate(int pid)
    {
        return pid != OldPid && !rejected.Contains(pid);
    }

    internal void Found(long now, int pid)
    {
        FoundPid = pid;
        foundAt = now;
        nextTry = now + FirstTryMs;
        Current = Phase.Found;
    }

    // The game that was tried is not ours (another character, or another bot window has it): look further.
    internal void Reject(long now)
    {
        if (FoundPid != 0)
            rejected.Add(FoundPid);
        FoundPid = 0;
        nextLook = now + LookEveryMs;
        Current = Phase.Waiting;
    }

    // Called every look at the game while a plan is armed. connected: the bot holds the found game and reads it; alive: the character is alive.
    internal Step Next(long now, bool connected, bool alive)
    {
        if (Current == Phase.Off)
            return Step.Nothing;
        if (now - armedAt > MaxWaitMs)
        {
            Cancel("oyun " + MaxWaitMs / 60000 + " dakikada geri gelmedi");
            return Step.Cancel;
        }
        if (Current == Phase.Waiting)
            return Step.Nothing;
        if (Current == Phase.Found)
        {
            if (now - foundAt > EnterWorldMs)
            {
                Cancel("oyun açıldı ama karakter " + EnterWorldMs / 60000 + " dakikada dünyaya girmedi");
                return Step.Cancel;
            }
            if (now < nextTry)
                return Step.Nothing;
            nextTry = now + TryEveryMs;
            return Step.Connect;
        }
        // Ready: the character is in the world and read; let it settle, wait for a dead one to be revived
        if (!connected)
        {
            Current = Phase.Waiting;
            return Step.Nothing;
        }
        if (!alive)
        {
            if (deadSince == 0)
                deadSince = now;
            if (now - deadSince > DeadWaitMs)
            {
                Cancel("karakter ölü kaldı");
                return Step.Cancel;
            }
            return Step.Nothing;
        }
        deadSince = 0;
        if (now - readyAt < SettleMs)
            return Step.Nothing;
        return Step.Start;
    }

    // The result of a try to connect to the found game: false = not ready yet (the try is repeated).
    internal void Connected(long now)
    {
        Current = Phase.Ready;
        readyAt = now;
        deadSince = 0;
    }

    // The job was started: the plan is done.
    internal void Started(long now)
    {
        resumes.Add(now);
        Current = Phase.Off;
        Why = null;
    }
}

sealed partial class MemorySelectV3Form
{
    readonly ResumePlan resume = new ResumePlan();
    readonly CheckBox autoResume = new CheckBox();
    // True while the plan itself connects or starts: those calls must not cancel the plan like the user's own Stop or Connect does.
    bool resumeBusy;
    string caravanFrom, caravanTo;
    long nextResumeStatus;

    // The game closed: if a job that can be taken up again was running, the plan is armed. Called before the job is stopped.
    ResumeJob JobBeforeExit()
    {
        if (!active)
            return null;
        string character = snapshot == null ? null : snapshot.CharacterName;
        if (operation == OperationMode.Hunt && center != null && selectedMobs.Count > 0)
            return new ResumeJob { Mode = OperationMode.Hunt, Character = character, Text = "av" };
        if (operation == OperationMode.Travel && caravanFrom != null && caravanTo != null)
            return new ResumeJob { Mode = OperationMode.Travel, Character = character, From = caravanFrom, To = caravanTo, Text = "kervan " + caravanFrom + " → " + caravanTo };
        return new ResumeJob { Text = null };
    }

    sealed class ResumeJob
    {
        internal OperationMode Mode;
        internal string Character, From, To, Text;
    }

    void ArmResume(ResumeJob job, int oldPid)
    {
        if (job == null || !autoResume.Checked)
            return;
        if (job.Text == null)
        {
            Log("Otomatik devam bu iş için yok (yalnız av ve kervan yolu devam eder).");
            return;
        }
        if (string.IsNullOrEmpty(job.Character))
        {
            Log("Otomatik devam: karakter adı okunamadığı için devam edilemez.");
            return;
        }
        if (!resume.Arm(clock.ElapsedMilliseconds, job.Mode, job.Character, job.From, job.To, oldPid))
        {
            Log("Otomatik devam yapılmadı · " + resume.Why + ".");
            return;
        }
        nextResumeStatus = 0;
        Log("Otomatik devam: yeni oyun bekleniyor · " + job.Text + " · karakter " + job.Character + ".");
    }

    // The game processes that are not the closed one, in the order they started.
    List<int> NewGameProcesses()
    {
        var ids = new List<KeyValuePair<DateTime, int>>();
        foreach (var p in Process.GetProcessesByName("sro_client"))
        {
            try
            {
                if (resume.Candidate(p.Id))
                    ids.Add(new KeyValuePair<DateTime, int>(p.StartTime, p.Id));
            }
            catch (Exception)
            {
                // a process that cannot be read now is looked at again at the next look
            }
            finally
            {
                p.Dispose();
            }
        }
        return ids.OrderBy(k => k.Key).Select(k => k.Value).ToList();
    }

    // Called at the start of every look at the game, with or without a reader.
    void ResumeTick(long now)
    {
        if (!resume.Armed)
            return;
        if (!autoResume.Checked)
        {
            resume.Cancel("kutu kapatıldı");
            Log("Otomatik devam kapatıldı.");
            return;
        }
        if (resume.Current == ResumePlan.Phase.Waiting)
        {
            if (now >= nextResumeStatus)
            {
                nextResumeStatus = now + 5000;
                status.Text = "Otomatik devam: yeni oyun bekleniyor (" + resume.Character + ").";
            }
            if (!resume.TimeToLook(now))
                return;
            var found = NewGameProcesses();
            if (found.Count == 0)
                return;
            resume.Found(now, found[0]);
            Log("Otomatik devam: yeni oyun bulundu (PID " + found[0] + "), karakterin girmesi bekleniyor.");
            return;
        }
        bool alive = snapshot != null && snapshot.PlayerAlive;
        var step = resume.Next(now, reader != null, alive);
        switch (step)
        {
            case ResumePlan.Step.Cancel:
                Log("Otomatik devam bırakıldı · " + resume.Why + ".");
                status.Text = "Otomatik devam bırakıldı · " + resume.Why + ".";
                break;
            case ResumePlan.Step.Connect:
                ResumeConnect(now);
                break;
            case ResumePlan.Step.Start:
                ResumeStart(now);
                break;
        }
    }

    // Connects to the found game when it has entered the world and holds the same character; any other game is turned down.
    void ResumeConnect(long now)
    {
        var item = processes.Items.Cast<ProcessItem>().FirstOrDefault(i => i.Id == resume.FoundPid);
        if (item == null)
        {
            RefreshProcesses();
            item = processes.Items.Cast<ProcessItem>().FirstOrDefault(i => i.Id == resume.FoundPid);
        }
        if (item == null)
        {
            resume.Reject(now);
            return;
        }
        resumeBusy = true;
        try
        {
            processes.SelectedItem = item;
            Connect(true);
        }
        catch (InvalidOperationException e)
        {
            DropResumeReader();
            if (e.Message.IndexOf("başka bir bot", StringComparison.Ordinal) >= 0)
            {
                // another bot window holds this game
                Log("Otomatik devam: PID " + resume.FoundPid + " kullanılamadı · " + e.Message);
                resume.Reject(now);
            }
            return;
        }
        catch (Exception)
        {
            // the character is not in the world yet: tried again in a moment
            DropResumeReader();
            return;
        }
        finally
        {
            resumeBusy = false;
        }
        string name = snapshot == null ? null : snapshot.CharacterName;
        if (string.IsNullOrEmpty(name))
        {
            DropResumeReader();
            return;
        }
        if (!string.Equals(name, resume.Character, StringComparison.OrdinalIgnoreCase))
        {
            Log("Otomatik devam: PID " + resume.FoundPid + " başka karakter (" + name + "), geçildi.");
            DropResumeReader();
            resume.Reject(now);
            return;
        }
        resume.Connected(now);
        Log("Otomatik devam: " + name + " bağlandı, iş " + ResumePlan.SettleMs / 1000 + " sn sonra başlayacak.");
        status.Text = "Otomatik devam: bağlandı, iş birazdan başlayacak.";
    }

    void DropResumeReader()
    {
        if (reader != null)
        {
            reader.Dispose();
            reader = null;
        }
    }

    void ResumeStart(long now)
    {
        resumeBusy = true;
        try
        {
            if (resume.Mode == OperationMode.Hunt)
                Start();
            else
                StartCaravan(resume.From, resume.To);
            resume.Started(now);
            Log("Otomatik devam: iş yeniden başladı.");
        }
        catch (Exception e)
        {
            resume.Cancel("iş başlatılamadı: " + e.Message);
            Log("Otomatik devam bırakıldı · " + resume.Why);
            status.Text = "Otomatik devam bırakıldı · " + e.Message;
        }
        finally
        {
            resumeBusy = false;
        }
    }
}

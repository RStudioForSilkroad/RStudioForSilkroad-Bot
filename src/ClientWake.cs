using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

// The game window can be left with its drawing stopped while the game itself goes on (the character fights and walks, the picture does not
// change; Alt+Tab gives one frame). It happens after the window was hidden and shown again. Minimizing and restoring the window wakes
// it for good (it then draws as long as it is in front), so that is what "Client'ı göster" does now, and what the watch below does
// when the picture stands still while the character moves.
static class ClientPicture
{
    [DllImport("user32.dll")]
    static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);

    [DllImport("user32.dll")]
    static extern bool GetClientRect(IntPtr window, out Rect rect);

    [StructLayout(LayoutKind.Sequential)]
    struct Rect
    {
        internal int Left, Top, Right, Bottom;
    }

    // A picture of the window's client area (small enough to compare every few seconds). Null when the window has no area.
    internal static Bitmap Grab(IntPtr window)
    {
        Rect r;
        if (!GetClientRect(window, out r) || r.Right - r.Left < 16 || r.Bottom - r.Top < 16)
            return null;
        var bitmap = new Bitmap(r.Right - r.Left, r.Bottom - r.Top);
        using (var g = Graphics.FromImage(bitmap))
        {
            IntPtr dc = g.GetHdc();
            try
            {
                PrintWindow(window, dc, 3);
            }
            finally
            {
                g.ReleaseHdc(dc);
            }
        }
        return bitmap;
    }

    // Mean colour difference of sampled pixels; 0 means the same picture.
    internal static double Diff(Bitmap a, Bitmap b)
    {
        if (a == null || b == null || a.Width != b.Width || a.Height != b.Height)
            return double.MaxValue;
        long sum = 0;
        int n = 0;
        for (int y = 0; y < a.Height; y += 7)
            for (int x = 0; x < a.Width; x += 7)
            {
                Color p = a.GetPixel(x, y), q = b.GetPixel(x, y);
                sum += Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B);
                n++;
            }
        return n == 0 ? 0 : (double)sum / n;
    }
}

sealed partial class MemorySelectV3Form
{
    [DllImport("user32.dll")]
    static extern bool AttachThreadInput(uint attach, uint to, bool on);
    [DllImport("user32.dll")]
    static extern bool BringWindowToTop(IntPtr window);
    [DllImport("kernel32.dll")]
    static extern uint GetCurrentThreadId();

    readonly CheckBox autoWake = new CheckBox();
    long nextFreezeCheck, wakeCooldown;
    Bitmap lastShot;
    Position lastShotPosition;
    uint lastShotHp;
    int stillSamples;
    bool grabbing;

    void PostUi(Action action)
    {
        try
        {
            if (!IsDisposed && IsHandleCreated)
                BeginInvoke(action);
        }
        catch (InvalidOperationException) { }
    }

    // Brings a window to the front even when this program is not in front (the input queues are joined for the call).
    static void ForceForeground(IntPtr window)
    {
        uint pid;
        uint front = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out pid), me = GetCurrentThreadId();
        bool joined = front != 0 && front != me && AttachThreadInput(me, front, true);
        try
        {
            BringWindowToTop(window);
            SetForegroundWindow(window);
        }
        finally
        {
            if (joined)
                AttachThreadInput(me, front, false);
        }
    }

    // Minimize (a hidden window is shown minimized), restore, bring to the front: the window handles its resize and activation again.
    void WakeClient(IntPtr window)
    {
        ShowWindowAsync(window, IsWindowVisible(window) ? 6 : 7);
        var timer = new System.Windows.Forms.Timer { Interval = 700 };
        int step = 0;
        timer.Tick += delegate
        {
            step++;
            if (step == 1)
            {
                ShowWindowAsync(window, 9);
                timer.Interval = 600;
                return;
            }
            timer.Stop();
            timer.Dispose();
            ForceForeground(window);
        };
        timer.Start();
    }

    // While the game is in front and a job runs: when the character moves (or its HP changes) but the picture does not, drawing has stopped.
    void FreezeWatch(long now)
    {
        if (!autoWake.Checked || reader == null || snapshot == null || snapshot.Player == null)
            return;
        if (!reader.IsForeground() || reader.IsMinimized())
        {
            stillSamples = 0;
            return;
        }
        // No picture grabbing or waking right after an area load, or while the game does not answer Windows.
        if (areaWatch.Within(now, AreaWatch.QuietMs) || hungSince != 0)
        {
            stillSamples = 0;
            return;
        }
        if (grabbing || now < nextFreezeCheck)
            return;
        nextFreezeCheck = now + 3000;
        IntPtr window = ClientWindow();
        if (window == IntPtr.Zero)
            return;
        Position position = snapshot.Player.Copy();
        uint hp = snapshot.Hp;
        grabbing = true;
        ThreadPool.QueueUserWorkItem(delegate
        {
            Bitmap shot = null;
            try
            {
                shot = ClientPicture.Grab(window);
            }
            catch (Exception) { }
            PostUi(delegate
            {
                grabbing = false;
                if (shot == null)
                    return;
                if (lastShot != null)
                {
                    bool moved = position.Distance(lastShotPosition) > 2 || hp != lastShotHp;
                    bool same = ClientPicture.Diff(lastShot, shot) < 0.2;
                    stillSamples = same && moved ? stillSamples + 1 : 0;
                    long clockNow = clock.ElapsedMilliseconds;
                    if (stillSamples >= 2 && clockNow >= wakeCooldown && !areaWatch.Within(clockNow, AreaWatch.QuietMs) && hungSince == 0)
                    {
                        stillSamples = 0;
                        wakeCooldown = clockNow + 30000;
                        Log("Oyunun çizimi durmuştu (karakter hareket ediyor, görüntü aynı) · pencere küçültülüp açılarak canlandırılıyor");
                        WakeClient(window);
                    }
                    lastShot.Dispose();
                }
                lastShot = shot;
                lastShotPosition = position;
                lastShotHp = hp;
            });
        });
    }
}

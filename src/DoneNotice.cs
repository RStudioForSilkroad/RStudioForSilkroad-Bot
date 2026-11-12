using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Tells the user that a job has finished while the window is behind others: a tray balloon (a toast on Windows 10/11), the system
// sound, and the taskbar button flashing until the window is brought back. Clicking the balloon restores the window.
sealed partial class MemorySelectV3Form
{
    readonly CheckBox notifyDone = new CheckBox();
    NotifyIcon doneIcon;
    Timer doneHide;

    [StructLayout(LayoutKind.Sequential)]
    struct FlashInfo
    {
        internal uint Size;
        internal IntPtr Window;
        internal uint Flags, Count, Timeout;
    }

    [DllImport("user32.dll")]
    static extern bool FlashWindowEx(ref FlashInfo info);

    void NotifyDone(string title, string text)
    {
        Log("Bildirim · " + title + " · " + text);
        if (!notifyDone.Checked || !IsHandleCreated)
            return;
        try
        {
            if (doneIcon == null)
            {
                doneIcon = new NotifyIcon { Icon = Icon ?? SystemIcons.Information, Text = AppPaths.AppName };
                doneIcon.BalloonTipClicked += delegate { ShowAgain(); };
                doneIcon.Click += delegate { ShowAgain(); };
                doneHide = new Timer { Interval = 20000 };
                doneHide.Tick += delegate
                {
                    doneHide.Stop();
                    if (doneIcon != null)
                        doneIcon.Visible = false;
                };
            }
            doneIcon.Visible = true;
            doneIcon.ShowBalloonTip(10000, title, text, ToolTipIcon.Info);
            doneHide.Stop();
            doneHide.Start();
            System.Media.SystemSounds.Asterisk.Play();
            if (GetForegroundWindow() != Handle)
            {
                // FLASHW_ALL | FLASHW_TIMERNOFG: caption and taskbar flash until the window is in front.
                var info = new FlashInfo { Size = (uint)Marshal.SizeOf(typeof(FlashInfo)), Window = Handle, Flags = 3 | 12, Count = uint.MaxValue };
                FlashWindowEx(ref info);
            }
        }
        catch { }
    }

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    void ShowAgain()
    {
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        Activate();
    }

    void DisposeDoneNotice()
    {
        if (doneHide != null)
            doneHide.Dispose();
        if (doneIcon != null)
        {
            doneIcon.Visible = false;
            doneIcon.Dispose();
            doneIcon = null;
        }
    }
}

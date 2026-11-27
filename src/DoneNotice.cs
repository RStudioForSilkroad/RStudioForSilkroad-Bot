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
            EnsureTrayIcon();
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
        RestoreFromTray();
    }

    // The one tray icon of the program: it carries the "job done" balloons and holds the window while it is minimized.
    void EnsureTrayIcon()
    {
        if (doneIcon != null)
            return;
        doneIcon = new NotifyIcon { Icon = Icon ?? SystemIcons.Information, Text = AppPaths.AppName };
        doneIcon.BalloonTipClicked += delegate { ShowAgain(); };
        doneIcon.MouseClick += delegate(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                ShowAgain();
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Göster", null, delegate { ShowAgain(); });
        menu.Items.Add("Kapat", null, delegate
        {
            ShowAgain();
            Close();
        });
        doneIcon.ContextMenuStrip = menu;
        doneHide = new Timer { Interval = 20000 };
        doneHide.Tick += delegate
        {
            doneHide.Stop();
            // The icon stays while the window is in the tray: it is the only way back to it.
            if (doneIcon != null && Visible)
                doneIcon.Visible = false;
        };
    }

    // Minimizing sends the window to the tray (the hidden icons): no taskbar button is left.
    void InitTray()
    {
        Resize += delegate
        {
            if (WindowState == FormWindowState.Minimized && IsHandleCreated && Visible)
            {
                EnsureTrayIcon();
                doneIcon.Visible = true;
                Hide();
            }
        };
    }

    void RestoreFromTray()
    {
        if (!Visible)
            Show();
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        Activate();
        if (doneIcon != null && doneHide != null && !doneHide.Enabled)
            doneIcon.Visible = false;
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

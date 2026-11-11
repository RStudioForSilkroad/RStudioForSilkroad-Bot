using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

sealed partial class MemorySelectV3Form
{
    readonly System.Windows.Forms.Timer dualTimer = new System.Windows.Forms.Timer();
    readonly ComboBox firstAccount = new ComboBox(), secondAccount = new ComboBox();
    readonly Label dualStatus = new Label();
    readonly NumericUpDown dualSeconds = new NumericUpDown();
    int dualFirst, dualSecond;
    long dualSwitched;
    Mutex clientClaim;
    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]
    static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")]
    static extern bool ShowWindowAsync(IntPtr window, int command);

    void ReleaseClientClaim()
    {
        if (clientClaim != null)
        {
            clientClaim.Dispose();
            clientClaim = null;
        }
    }

    void ClaimClient(int id)
    {
        ReleaseClientClaim();
        bool created;
        var claim = new Mutex(false, "Local\\SilkroadRStudioClient_" + id, out created);
        if (!created)
        {
            claim.Dispose();
            throw new InvalidOperationException("Bu oyun başka bir bot penceresine bağlı.");
        }
        clientClaim = claim;
    }

    void BuildDualAccount()
    {
        string account = Environment.GetEnvironmentVariable("SRSTUDIO_ACCOUNT");
        if (account == "1" || account == "2")
        {
            Text += " · Hesap " + account;
            int id;
            if (int.TryParse(Environment.GetEnvironmentVariable("SRSTUDIO_GAME_PID"), out id))
                foreach (ProcessItem item in processes.Items)
                    if (item.Id == id)
                    {
                        processes.SelectedItem = item;
                        Safe(Connect);
                        break;
                    }
            return;
        }
        var page = new TabPage("Çift Hesap · Deneme");
        tabs.TabPages.Add(page);
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents =
            false, Padding = new Padding(20), AutoScroll = true };
        page.Controls.Add(layout);
        layout.Controls.Add(new Label { Text = "İki ayrı hesap profili · Sırayla oyun penceresini öne alır", AutoSize =
            true, Font = new Font(Font, FontStyle.Bold) });
        foreach (var box in new[] { firstAccount, secondAccount })
        {
            box.Width = 650;
            box.DropDownStyle = ComboBoxStyle.DropDownList;
            layout.Controls.Add(box);
        }
        var refresh = new Button { Text = "Oyunları yenile", Width = 200 };
        refresh.Click += delegate
        {
            firstAccount.Items.Clear();
            secondAccount.Items.Clear();
            foreach (var p in Process.GetProcessesByName("sro_client"))
            {
                using (p)
                {
                    var item = new ProcessItem { Id = p.Id, Name = "PID " + p.Id + " · " + p.MainWindowTitle };
                    firstAccount.Items.Add(item);
                    secondAccount.Items.Add(item);
                }
            }
            if (firstAccount.Items.Count > 0)
                firstAccount.SelectedIndex = 0;
            if (secondAccount.Items.Count > 1)
                secondAccount.SelectedIndex = 1;
        };
        layout.Controls.Add(refresh);
        refresh.PerformClick();
        layout.Controls.Add(new Label { Text = "Her oyun için önde kalma süresi (saniye)", AutoSize = true });
        dualSeconds.Minimum = 3;
        dualSeconds.Maximum = 60;
        dualSeconds.Value = 3;
        layout.Controls.Add(dualSeconds);
        var open = new Button { Text = "İki hesap panelini aç", Width = 260 };
        open.Click += delegate
        {
            Safe(delegate
            {
                var a = firstAccount.SelectedItem as ProcessItem;
                var b = secondAccount.SelectedItem as ProcessItem;
                if (a == null || b == null || a.Id == b.Id)
                    throw new InvalidOperationException("İki farklı oyun seç.");
                LaunchAccount(1, a.Id);
                LaunchAccount(2, b.Id);
                dualStatus.Text = "Her hesap penceresinde av ayarlarını yapıp başlat. Sonra geçişi aç.";
            });
        };
        layout.Controls.Add(open);
        var start = new Button { Text = "Pencere geçişini başlat", Width = 260 };
        start.Click += delegate
        {
            Safe(delegate
            {
                if (active || grim.Armed)
                    throw new InvalidOperationException("Ana penceredeki otomasyonu önce durdur.");
                var a = firstAccount.SelectedItem as ProcessItem;
                var b = secondAccount.SelectedItem as ProcessItem;
                if (a == null || b == null || a.Id == b.Id)
                    throw new InvalidOperationException("İki farklı oyun seç.");
                dualFirst = a.Id;
                dualSecond = b.Id;
                dualSwitched = clock.ElapsedMilliseconds;
                dualTimer.Start();
                dualStatus.Text = "Geçiş hazır. Oyunlardan birini öne getir.";
            });
        };
        layout.Controls.Add(start);
        var stop = new Button { Text = "Pencere geçişini durdur", Width = 260 };
        stop.Click += delegate
        {
            dualTimer.Stop();
            dualStatus.Text = "Geçiş kapalı. Hesap botlarını kendi Durdur düğmelerinden durdur.";
        };
        layout.Controls.Add(stop);
        dualStatus.Width = 750;
        dualStatus.Height = 85;
        layout.Controls.Add(dualStatus);
        layout.Controls.Add(new Label { Text =
            "Başka uygulama öndeyken geçiş bekler. Botları durdurmak için her hesap panelindeki Durdur düğmesini kullan. Ayrı kısayollar ata. Bu deneme saldırı sürekliliğini garanti etmez.",
            Width = 750, Height = 85 });
        dualTimer.Interval = 250;
        dualTimer.Tick += delegate
        {
            try
            {
                uint current;
                Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out current);
                if (current != dualFirst && current != dualSecond)
                {
                    dualSwitched = clock.ElapsedMilliseconds;
                    dualStatus.Text = "Başka uygulama önde · geçiş bekliyor.";
                    return;
                }
                if (clock.ElapsedMilliseconds - dualSwitched < (long)dualSeconds.Value * 1000)
                    return;
                using (var next = Process.GetProcessById(current == dualFirst ? dualSecond : dualFirst))
                {
                    next.Refresh();
                    if (next.ProcessName != "sro_client" || next.MainWindowHandle == IntPtr.Zero)
                        throw new IOException("Seçili oyun kapandı.");
                    if (IsIconic(next.MainWindowHandle))
                        ShowWindowAsync(next.MainWindowHandle, 9);
                    bool ok = SetForegroundWindow(next.MainWindowHandle);
                    dualSwitched = clock.ElapsedMilliseconds;
                    dualStatus.Text = ok ? "Öne alınan oyun PID " + next.Id :
                        "Windows odak geçişini kabul etmedi; oyunu elle öne getir.";
                }
            }
            catch (Exception e)
            {
                dualTimer.Stop();
                dualStatus.Text = e.Message;
            }
        };
    }

    void LaunchAccount(int slot, int id)
    {
        var info = new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = false, WorkingDirectory =
            AppDomain.CurrentDomain.BaseDirectory };
        info.EnvironmentVariables["SRSTUDIO_ACCOUNT"] = slot.ToString();
        info.EnvironmentVariables["SRSTUDIO_GAME_PID"] = id.ToString();
        Process.Start(info);
    }
}

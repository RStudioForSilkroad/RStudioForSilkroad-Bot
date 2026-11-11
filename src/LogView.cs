using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;

sealed partial class MemorySelectV3Form
{
    readonly TextBox logView = new TextBox();
    string loggedStatus = "";
    long nextStatusLog;

    string EventLogPath
    {
        get { return Path.Combine(V3Settings.DirectoryPath, "events.log"); }
    }

    void RecordStatusLog()
    {
        if (active && status.Text != loggedStatus && clock.ElapsedMilliseconds >= nextStatusLog)
        {
            loggedStatus = status.Text;
            nextStatusLog = clock.ElapsedMilliseconds + 1000;
            Log("Durum · " + loggedStatus);
        }
    }

    void BuildLogTab()
    {
        var page = new TabPage("Log");
        tabs.TabPages.Add(page);
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 45 };
        logView.Multiline = true;
        logView.ReadOnly = true;
        logView.ScrollBars = ScrollBars.Both;
        logView.WordWrap = false;
        logView.Dock = DockStyle.Fill;
        page.Controls.Add(logView);
        page.Controls.Add(tools);
        Action refresh = delegate
        {
            try
            {
                string value = File.Exists(EventLogPath) ? File.ReadAllText(EventLogPath, Encoding.UTF8) :
                    "Henüz kayıt yok.";
                logView.Text = value.Length > 200000 ? value.Substring(value.Length - 200000) : value;
                logView.SelectionStart = logView.TextLength;
                logView.ScrollToCaret();
            }
            catch (Exception e)
            {
                logView.Text = e.Message;
            }
        };
        var read = new Button { Text = "Yenile", Width = 110 };
        read.Click += delegate { refresh(); };
        tools.Controls.Add(read);
        var export = new Button { Text = "Log dosyasını kaydet", Width = 210 };
        export.Click += delegate
        {
            Safe(delegate
            {
                using (var dialog = new SaveFileDialog { Filter = "Metin dosyası|*.txt", FileName = "SilkroadRStudio-Log-" +
                    DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt" })
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                        File.WriteAllText(dialog.FileName, File.Exists(EventLogPath) ? File.ReadAllText(EventLogPath,
                            Encoding.UTF8) : eventLog.ToString(), Encoding.UTF8);
                }
            });
        };
        tools.Controls.Add(export);
        var folder = new Button { Text = "Kayıt klasörü", Width = 150 };
        folder.Click += delegate
        {
            Safe(delegate
            {
                Directory.CreateDirectory(V3Settings.DirectoryPath);
                Process.Start(new ProcessStartInfo(V3Settings.DirectoryPath) { UseShellExecute = true });
            });
        };
        tools.Controls.Add(folder);
        tabs.SelectedIndexChanged += delegate
        {
            if (tabs.SelectedTab == page)
                refresh();
        };
    }
}

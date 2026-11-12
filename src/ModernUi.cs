using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows.Forms;

// Application shell: colour application, header, log card and the Nanzhang NPC page. Tokens live in Ui.cs.
sealed partial class MemorySelectV3Form
{
    readonly ComboBox themePicker = new ComboBox();
    readonly Label themeLabel = new Label { Text = "Vurgu", AutoSize = true };
    Button whatsNewButton;
    Panel headerPanel, footerPanel, sidebarHost;

    void BuildModernTabs()
    {
        var page = new TabPage("Nanzhang NPC");
        tabs.TabPages.Add(page);
        var split = new SplitContainer { Size = new Size(1200, 650), Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1,
            SplitterDistance = 340, SplitterWidth = 1 };
        page.Controls.Add(split);
        split.Panel1.Padding = new Padding(Ui.Pad);
        var searchLabel = new Label { Text = "İsim veya kimlik ara", Dock = DockStyle.Top, Height = 24, Tag = "muted" };
        var search = new TextBox { Dock = DockStyle.Top, BorderStyle = BorderStyle.FixedSingle };
        var list = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false };
        var viewer = new MapViewer { Dock = DockStyle.Fill };
        var hint = new Label { Dock = DockStyle.Bottom, Height = 72, Padding = new Padding(0, 10, 0, 0), Tag = "muted", Text =
            "Fare tekerleği: yakınlaştır · Sol tuş: sürükle\n134 canlı kayıt; şehir krokisi yaklaşık yapı izdüşümüdür." };
        split.Panel1.Controls.Add(list);
        split.Panel1.Controls.Add(hint);
        split.Panel1.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 8 });
        split.Panel1.Controls.Add(search);
        split.Panel1.Controls.Add(searchLabel);
        split.Panel2.Padding = new Padding(Ui.Pad);
        var nanzhangFrame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), Tag = "border" };
        nanzhangFrame.Controls.Add(viewer);
        split.Panel2.Controls.Add(nanzhangFrame);
        viewer.LoadResource("nanzhang-map.jpg");
        List<NpcMapRecord> records;
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("nanzhang-data.json"))
            using (var r = new StreamReader(stream))
            {
                records = new JavaScriptSerializer { MaxJsonLength = 16000000 }.Deserialize<NpcMapData>(r.ReadToEnd()).NPCs;
            }
        Action filter = delegate
        {
            list.Items.Clear();
            foreach (var n in records.Where(n => (n.name + " " + n.id).IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) >= 0))
                list.Items.Add(n);
        };
        search.TextChanged += delegate { filter(); };
        filter();
        list.SelectedIndexChanged += delegate
        {
            var n = list.SelectedItem as NpcMapRecord;
            if (n != null)
            {
                hint.Text = n.name + " · Kimlik " + n.id + "\nX " + n.x.ToString("F1") + " / Y " + n.y.ToString("F1") +
                    " · Harita no " + n.number;
                viewer.FocusNpc(n.x, n.y);
            }
        };
        themePicker.Width = 120;
        themePicker.DropDownStyle = ComboBoxStyle.DropDownList;
        themePicker.Items.AddRange(Ui.AccentNames);
        headerPanel.Controls.Add(themeLabel);
        headerPanel.Controls.Add(themePicker);
        PlaceHeaderRight();
        headerPanel.Resize += delegate { PlaceHeaderRight(); };
        themePicker.SelectedIndexChanged += delegate
        {
            ApplyTheme();
            if (settings != null)
            {
                settings.Theme = themePicker.SelectedIndex;
                SaveSettings();
            }
        };
        themePicker.SelectedIndex = Math.Max(0, Math.Min(themePicker.Items.Count - 1, settings.Theme));
    }

    // Colours come from Ui; the accent colour is the only thing the user can change.
    void ApplyTheme()
    {
        Ui.SetAccent(Math.Max(0, themePicker.SelectedIndex));
        Action<Control, Color> paint = null;
        paint = (c, bg) =>
        {
            if (c is MapViewer || c is ExplorerMapView || c is DungeonRadar || c is StatPanel || c is TabStrip)
            {
                c.Invalidate();
                return;
            }
            if (c == sidebar)
            {
                sidebar.BackColor = Ui.Surface;
                sidebar.Invalidate();
                return;
            }
            if (c == headerPanel || c == footerPanel || c == sidebarHost)
                bg = Ui.Surface;
            if (c is CardBox)
                bg = Ui.Surface;
            if (c is WorldMapView)
            {
                foreach (Control child in c.Controls)
                    paint(child, Ui.Surface);
                c.Invalidate();
                return;
            }
            var page = c as TabPage;
            if (page != null)
                page.UseVisualStyleBackColor = false;
            if (c is TextBox || c is ComboBox || c is NumericUpDown || c is ListBox)
            {
                c.BackColor = Ui.Surface;
                c.ForeColor = Ui.Text;
            }
            else if (c is SplitContainer)
                c.BackColor = Ui.Divider;
            else if (c is Button)
                Fancy.Style((Button)c);
            else if ("border".Equals(c.Tag))
            {
                c.BackColor = Ui.Border;
                bg = Ui.Surface;
            }
            else
            {
                c.BackColor = bg;
                c.ForeColor = c is Label && "muted".Equals(c.Tag) ? Ui.TextMuted : c is Label && "warn".Equals(c.Tag) ? Ui.Warning : Ui.Text;
            }
            var g = c as DataGridView;
            if (g != null)
                Ui.StyleGrid(g);
            foreach (Control child in c.Controls)
                paint(child, bg);
        };
        BackColor = Ui.Canvas;
        paint(this, Ui.Canvas);
        statPanel.Invalidate();
        if (headerPanel != null)
            headerPanel.Invalidate(true);
        dungeonRadar.Invalidate();
        Invalidate(true);
    }

    // Window title starts with the character name, so two running copies can be told apart in the taskbar.
    void UpdateTitle()
    {
        string name = snapshot == null ? null : snapshot.CharacterName;
        string title = (string.IsNullOrEmpty(name) ? "" : name + " - ") + AppPaths.AppName + " " + Application.ProductVersion;
        if (Text != title)
            Text = title;
    }

    // Right side of the header: release notes button and the accent colour choice.
    void PlaceHeaderRight()
    {
        if (headerPanel == null || whatsNewButton == null)
            return;
        int right = headerPanel.ClientSize.Width - 20;
        themePicker.Location = new Point(Math.Max(10, right - themePicker.Width), 49);
        themeLabel.Location = new Point(themePicker.Left - themeLabel.PreferredWidth - 8, 53);
        whatsNewButton.Location = new Point(themeLabel.Left - whatsNewButton.Width - 16, 46);
    }

    // Header: white bar with the application name on the left and the connection controls next to it.
    void PaintHeader(object sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        var r = headerPanel.ClientRectangle;
        if (r.Width < 2 || r.Height < 2)
            return;
        g.Clear(Ui.Surface);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var mark = new Rectangle(20, 12, 32, 32);
        using (var path = Ui.RoundRect(mark, 8))
            using (var fill = new SolidBrush(Ui.Accent))
                g.FillPath(fill, path);
        TextRenderer.DrawText(g, "R", Ui.Title, mark, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, AppPaths.AppName, Ui.Heading, new Point(60, 10), Ui.Text);
        TextRenderer.DrawText(g, "Sürüm " + Application.ProductVersion, Ui.Small, new Point(61, 31), Ui.TextMuted);
        using (var line = new Pen(Ui.Border))
            g.DrawLine(line, 0, r.Height - 1, r.Width, r.Height - 1);
        // Connection indicator in front of the status line
        Color dot = reader == null ? Ui.BorderStrong : Ui.Success;
        using (var brush = new SolidBrush(dot))
            g.FillEllipse(brush, 22, 62, 10, 10);
    }

    void OpenManual()
    {
        string path = Path.Combine(V3Settings.DirectoryPath, "Kullanim-Kilavuzu.pdf");
        Directory.CreateDirectory(V3Settings.DirectoryPath);
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("manual.pdf"))
            using (var file = File.Create(path))
                stream.CopyTo(file);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    sealed class NpcMapData
    {
        public List<NpcMapRecord> NPCs;
    }

    sealed class NpcMapRecord
    {
        public string name;
        public int id, number;
        public double x, y;

        public override string ToString()
        {
            return number.ToString("000") + " · " + name;
        }
    }
}

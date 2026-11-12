using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

// Explorer Gisaeng Ahjin: 52 NPCs spread over the map. Talking to one is a single click; the bot walks to each spot in turn.
// The spots come from the game's own Explorer map (data/explorer-spots.json): the marker pixels were located on that map, the map was
// registered onto the game world map (same scale, shifted by 598 / -46 pixels) and converted with the world map's exact formula.
// The coordinates are therefore approximate; the NPC itself is found live in the client's object list once it is in sight.

public sealed class ExplorerSpot
{
    public int N;
    public double X, Y;

    internal Position Point()
    {
        return V2Rules.Center(X, Y, 0);
    }

    internal string Area
    {
        get { return ExpeditionRules.Area(Point()); }
    }
}

static class ExplorerSpots
{
    internal const int DefaultModel = 60023;
    internal const string AllAreas = "Tüm harita";
    // Order of the area picker, west to east. Names are the ones ExpeditionRules.Area gives.
    internal static readonly string[] Areas = { "Constantinople", "Samarkand", "Roc", "Hotan", "Donwhang", "Jangan", "Nanzhang" };
    internal static readonly List<ExplorerSpot> All = V3Data.Load<List<ExplorerSpot>>("explorer-spots.json").OrderBy(s => s.N).ToList();

    internal static ExplorerSpot Find(int n)
    {
        return All.FirstOrDefault(s => s.N == n);
    }

    // The numbers still to do, in order, starting at "from".
    internal static List<int> Queue(int from, ICollection<int> done, bool skipDone)
    {
        return All.Where(s => s.N >= from && !(skipDone && done.Contains(s.N))).Select(s => s.N).ToList();
    }

    // The map is approximate, so the marker can sit off the road: the nearest walkable point within 120 units is used.
    internal static bool Ground(ExplorerSpot spot, out Position point, out float height)
    {
        for (int ring = 0; ring <= 10; ring++)
        {
            int count = ring == 0 ? 1 : 8;
            for (int k = 0; k < count; k++)
            {
                double a = Math.PI * 2 * k / count, r = ring * 12.0;
                var p = V2Rules.Center(spot.X + Math.Cos(a) * r, spot.Y + Math.Sin(a) * r, 0);
                float h;
                if (ExpeditionRules.TryGround(p, out h))
                {
                    p.Height = h;
                    point = p;
                    height = h;
                    return true;
                }
            }
        }
        point = null;
        height = 0;
        return false;
    }

    // Walkable points on two rings around the spot, to look for the NPC when it is not in sight at arrival.
    internal static List<Position> Ring(ExplorerSpot spot)
    {
        var list = new List<Position>();
        foreach (double radius in new[] { 45.0, 90.0 })
            for (int k = 0; k < 8; k++)
            {
                double a = Math.PI * 2 * k / 8;
                Position p;
                try
                {
                    p = V2Rules.Center(spot.X + Math.Cos(a) * radius, spot.Y + Math.Sin(a) * radius, 0);
                }
                catch (ArgumentException)
                {
                    continue;
                }
                float h;
                if (ExpeditionRules.TryGround(p, out h))
                {
                    p.Height = h;
                    list.Add(p);
                }
            }
        return list;
    }

    internal static WorldNpc FindNpc(Snapshot snapshot, uint model, Position near, double radius, ICollection<uint> skip)
    {
        return snapshot.Npcs.Where(n => n.Model == model && n.Position != null && !skip.Contains(n.Uid) && n.Position.Distance(near) <= radius)
            .OrderBy(n => n.Position.Distance(near)).ThenBy(n => n.Uid).FirstOrDefault();
    }

    // Image position of a world coordinate on the game world map the Explorer view is drawn on.
    internal static PointF Image(double x, double y)
    {
        return WorldMapView.ToImage(x, y);
    }
}

// The game's world map with the 52 Explorer spots on it: fit to one area, drag and wheel like the other maps.
sealed class ExplorerMapView : Control
{
    static readonly Font numberFont = new Font("Segoe UI Semibold", 8f);
    Bitmap baseMap;
    double zoom = 0.2, cx = 2000, cy = 700, dragCx, dragCy;
    bool autoFit = true, dragging, moved;
    Point dragStart;
    List<ExplorerSpot> area;

    internal List<ExplorerSpot> Spots = new List<ExplorerSpot>();
    // 0 pending, 1 found, 2 not found
    internal Func<int, int> StateOf;
    internal int Selected, Active;
    internal Position Player;
    internal event Action<int> SpotPicked;

    internal ExplorerMapView()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        VisibleChanged += delegate
        {
            if (Visible)
                LoadBase();
        };
    }

    internal bool Ready
    {
        get { return baseMap != null; }
    }

    internal void LoadBase()
    {
        if (baseMap == null)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(WorldMapView.ResourceName))
            {
                if (stream == null)
                    return;
                using (var image = Image.FromStream(stream))
                    baseMap = new Bitmap(image);
            }
        }
        Refit();
    }

    // Shows these spots (null = the whole map).
    internal void ShowArea(IEnumerable<ExplorerSpot> spots)
    {
        area = spots == null ? null : spots.ToList();
        autoFit = true;
        Refit();
    }

    internal void Refit()
    {
        if (Width < 60 || Height < 60)
            return;
        double w = baseMap != null ? baseMap.Width : 4224, h = baseMap != null ? baseMap.Height : 1408;
        if (area == null || area.Count == 0)
        {
            zoom = Math.Min((Width - 16) / w, (Height - 16) / h);
            cx = w / 2;
            cy = h / 2;
        }
        else
        {
            var pts = area.Select(s => ExplorerSpots.Image(s.X, s.Y)).ToList();
            double x0 = pts.Min(p => p.X), x1 = pts.Max(p => p.X), y0 = pts.Min(p => p.Y), y1 = pts.Max(p => p.Y);
            double bw = Math.Max(300, x1 - x0 + 200), bh = Math.Max(180, y1 - y0 + 200);
            zoom = Math.Max(0.1, Math.Min(2.5, Math.Min(Width / bw, Height / bh)));
            cx = (x0 + x1) / 2;
            cy = (y0 + y1) / 2;
        }
        Invalidate();
    }

    PointF ToScreen(double x, double y)
    {
        var q = ExplorerSpots.Image(x, y);
        return new PointF((float)(Width / 2.0 + (q.X - cx) * zoom), (float)(Height / 2.0 + (q.Y - cy) * zoom));
    }

    float Radius
    {
        get { return zoom < 0.3 ? 5f : zoom < 0.6 ? 9f : 12f; }
    }

    int Hit(Point at)
    {
        int best = 0;
        double bestDistance = Radius + 6;
        foreach (var s in Spots)
        {
            var q = ToScreen(s.X, s.Y);
            double d = Math.Sqrt((q.X - at.X) * (q.X - at.X) + (q.Y - at.Y) * (q.Y - at.Y));
            if (d <= bestDistance)
            {
                bestDistance = d;
                best = s.N;
            }
        }
        return best;
    }

    // The scaled map is the costly part of a repaint (about 100 ms); it is redrawn only when the view changes, so the spots and the
    // character mark on top can be repainted as often as the character moves.
    Bitmap layer;
    double layerZoom, layerCx, layerCy;
    Size layerSize;

    void DrawBase(Graphics g)
    {
        if (Width < 2 || Height < 2)
            return;
        if (layer == null || layerSize != ClientSize || layerZoom != zoom || layerCx != cx || layerCy != cy)
        {
            if (layer != null)
                layer.Dispose();
            layer = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            layerSize = ClientSize;
            layerZoom = zoom;
            layerCx = cx;
            layerCy = cy;
            using (var lg = Graphics.FromImage(layer))
            {
                lg.Clear(Color.FromArgb(232, 236, 243));
                lg.InterpolationMode = zoom < 1 ? InterpolationMode.HighQualityBicubic : InterpolationMode.Bilinear;
                lg.PixelOffsetMode = PixelOffsetMode.Half;
                lg.DrawImage(baseMap, new RectangleF((float)(Width / 2.0 - cx * zoom), (float)(Height / 2.0 - cy * zoom),
                    (float)(baseMap.Width * zoom), (float)(baseMap.Height * zoom)));
            }
        }
        g.DrawImageUnscaled(layer, 0, 0);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Color.FromArgb(232, 236, 243));
        if (baseMap == null)
        {
            using (var brush = new SolidBrush(Ui.TextMuted))
                g.DrawString("Harita yükleniyor…", Ui.Body, brush, 12, 12);
            return;
        }
        DrawBase(g);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float r = Radius;
        var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        foreach (var s in Spots.OrderBy(s => s.N == Selected || s.N == Active ? 1 : 0))
        {
            var q = ToScreen(s.X, s.Y);
            if (q.X < -30 || q.Y < -30 || q.X > Width + 30 || q.Y > Height + 30)
                continue;
            int state = StateOf == null ? 0 : StateOf(s.N);
            Color fill = state == 1 ? Ui.Success : state == 2 ? Ui.Warning : Ui.Accent;
            bool chosen = s.N == Selected || s.N == Active;
            if (chosen)
                using (var halo = new SolidBrush(Color.FromArgb(90, s.N == Active ? Ui.Warning : Ui.Accent)))
                    g.FillEllipse(halo, q.X - r - 7, q.Y - r - 7, (r + 7) * 2, (r + 7) * 2);
            using (var brush = new SolidBrush(fill))
                g.FillEllipse(brush, q.X - r, q.Y - r, r * 2, r * 2);
            using (var pen = new Pen(Color.White, chosen ? 2.5f : 1.5f))
                g.DrawEllipse(pen, q.X - r, q.Y - r, r * 2, r * 2);
            if (r >= 9)
                g.DrawString(s.N.ToString(), numberFont, Brushes.White, new RectangleF(q.X - r - 4, q.Y - r - 1, r * 2 + 8, r * 2 + 2), format);
        }
        if (Player != null && Player.Outside)
        {
            var p = ToScreen(Player.X, Player.Y);
            using (var brush = new SolidBrush(Ui.Danger))
                g.FillEllipse(brush, p.X - 6, p.Y - 6, 12, 12);
            using (var pen = new Pen(Color.White, 2f))
                g.DrawEllipse(pen, p.X - 6, p.Y - 6, 12, 12);
        }
        format.Dispose();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (autoFit)
            Refit();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        autoFit = false;
        double old = zoom;
        double ix = cx + (e.X - Width / 2.0) / old, iy = cy + (e.Y - Height / 2.0) / old;
        zoom = Math.Max(0.08, Math.Min(4, zoom * (e.Delta > 0 ? 1.25 : 0.8)));
        cx = ix - (e.X - Width / 2.0) / zoom;
        cy = iy - (e.Y - Height / 2.0) / zoom;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button != MouseButtons.Left)
            return;
        dragging = true;
        moved = false;
        dragStart = e.Location;
        dragCx = cx;
        dragCy = cy;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragging)
        {
            if (Math.Abs(e.X - dragStart.X) + Math.Abs(e.Y - dragStart.Y) > 4)
                moved = true;
            if (moved)
            {
                autoFit = false;
                cx = dragCx - (e.X - dragStart.X) / zoom;
                cy = dragCy - (e.Y - dragStart.Y) / zoom;
                Invalidate();
            }
            return;
        }
        Cursor = Hit(e.Location) != 0 ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!dragging)
            return;
        dragging = false;
        if (!moved && SpotPicked != null)
        {
            int n = Hit(e.Location);
            if (n != 0)
                SpotPicked(n);
        }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        autoFit = true;
        Refit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && layer != null)
        {
            layer.Dispose();
            layer = null;
        }
        if (disposing && baseMap != null)
        {
            baseMap.Dispose();
            baseMap = null;
        }
        base.Dispose(disposing);
    }
}

sealed class ExplorerRun
{
    internal List<int> Pending = new List<int>();
    internal ExplorerSpot Spot;
    // 0 walking to the spot, 1 walking to the NPC, 2 NPC clicked / waiting for the selection, 3 looking around the spot,
    // 4 the dialog option was sent / waiting before the next spot
    internal int Phase, Tries, RingIndex, Failures, Total;
    internal long Since, PointSince;
    internal uint Uid;
    internal List<Position> Ring = new List<Position>();
    internal readonly HashSet<uint> Handled = new HashSet<uint>();
}

sealed partial class MemorySelectV3Form
{
    readonly ExplorerMapView explorerMap = new ExplorerMapView();
    readonly DataGridView explorerGrid = new DataGridView();
    readonly ComboBox explorerArea = new ComboBox();
    readonly Label explorerStatus = new Label();
    readonly CheckBox explorerSkipDone = new CheckBox();
    readonly NumericUpDown explorerModel = new NumericUpDown();
    readonly NumericUpDown explorerOption = new NumericUpDown();
    readonly CheckBox explorerTalk = new CheckBox();
    readonly HashSet<int> explorerMissing = new HashSet<int>();
    ExplorerRun explorerRun;
    bool explorerAdvancing, explorerSyncing;
    Button explorerOriginalButton;

    int ExplorerState(int n)
    {
        if (settings != null && settings.ExplorerDone != null && settings.ExplorerDone.Contains(n))
            return 1;
        return explorerMissing.Contains(n) ? 2 : 0;
    }

    // The Explorer page: the spots on the left, the map on the right. Right of the list a map per area, left a list per number.
    void BuildExplorer(TabPage page, Control mapHost)
    {
        var root = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Ui.Pad) };
        page.Controls.Add(root);

        // Right: toolbar and map.
        var right = new Panel { Dock = DockStyle.Fill };
        var mapFrame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), Tag = "border" };
        explorerMap.Dock = DockStyle.Fill;
        explorerMap.Spots = ExplorerSpots.All;
        explorerMap.StateOf = ExplorerState;
        explorerMap.SpotPicked += SelectExplorerSpot;
        mapFrame.Controls.Add(explorerMap);
        mapFrame.Controls.Add(mapHost);
        mapHost.Visible = false;
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, WrapContents = false, Padding = new Padding(0, 0, 0, 6) };
        explorerArea.DropDownStyle = ComboBoxStyle.DropDownList;
        explorerArea.Width = 176;
        explorerArea.Margin = new Padding(0, 4, 8, 0);
        explorerArea.Items.Add(ExplorerSpots.AllAreas);
        foreach (string name in ExplorerSpots.Areas)
            explorerArea.Items.Add(name + " · " + ExplorerSpots.All.Count(s => s.Area == name) + " nokta");
        explorerArea.SelectedIndexChanged += delegate
        {
            if (!explorerSyncing)
                ShowExplorerArea();
        };
        toolbar.Controls.Add(explorerArea);
        toolbar.Controls.Add(Btn("◀", delegate { StepExplorerArea(-1); }, 40, false));
        toolbar.Controls.Add(Btn("▶", delegate { StepExplorerArea(1); }, 40, false));
        explorerOriginalButton = Btn("Orijinal", delegate { ToggleExplorerOriginal(mapHost); }, 104, false, ButtonKind.Ghost);
        toolbar.Controls.Add(explorerOriginalButton);
        right.Controls.Add(mapFrame);
        right.Controls.Add(toolbar);

        // Left: list and actions.
        var leftFrame = new Panel { Dock = DockStyle.Left, Width = 444, Padding = new Padding(1), Tag = "border" };
        var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Ui.Pad) };
        leftFrame.Controls.Add(left);

        explorerGrid.Dock = DockStyle.Fill;
        explorerGrid.ReadOnly = true;
        explorerGrid.AllowUserToAddRows = false;
        explorerGrid.AllowUserToResizeRows = false;
        explorerGrid.RowHeadersVisible = false;
        explorerGrid.MultiSelect = false;
        explorerGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        explorerGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        explorerGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "N", HeaderText = "No", Width = 48 });
        explorerGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "State", HeaderText = "Durum", Width = 96 });
        explorerGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Area", HeaderText = "Bölge", AutoSizeMode =
            DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 80 });
        explorerGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "X", HeaderText = "X", Width = 70 });
        explorerGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Y", HeaderText = "Y", Width = 62 });
        foreach (var s in ExplorerSpots.All)
        {
            int row = explorerGrid.Rows.Add(s.N, "", s.Area, Math.Round(s.X).ToString(), Math.Round(s.Y).ToString());
            explorerGrid.Rows[row].Tag = s;
        }
        explorerGrid.SelectionChanged += delegate
        {
            var spot = TryExplorerSpot();
            if (spot != null && !explorerSyncing)
                SelectExplorerSpot(spot.N);
        };
        explorerGrid.CellDoubleClick += delegate(object o, DataGridViewCellEventArgs ev)
        {
            if (ev.RowIndex >= 0)
                Safe(delegate { StartExplorer(SelectedExplorerSpot().N, true); });
        };

        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true, Padding = new Padding(0, 8, 0, 0) };
        actions.Controls.Add(Btn("1 → 52 otomatik", delegate { StartExplorer(1, false); }, 188, true, ButtonKind.Primary));
        actions.Controls.Add(Btn("Seçiliden başla", delegate { StartExplorer(SelectedExplorerSpot().N, false); }, 156));
        actions.Controls.Add(Btn("Yalnız seçili noktaya git", delegate { StartExplorer(SelectedExplorerSpot().N, true); }, 188));
        actions.Controls.Add(Btn("Durdur", delegate { Stop("Explorer durduruldu."); }, 108, false, ButtonKind.Danger));
        ConfigureCheck(explorerSkipDone, "Bulunanları atla", true, 0);
        explorerSkipDone.CheckedChanged += delegate
        {
            if (!explorerSyncing)
                SaveSettingsQuietly();
        };
        actions.Controls.Add(explorerSkipDone);
        var modelRow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0) };
        var modelLabel = new Label { Text = "NPC model", AutoSize = false, Width = 82, Height = 30, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 3, 0, 0) };
        Num(explorerModel, 1, 999999, ExplorerSpots.DefaultModel, 0, 100);
        explorerModel.ValueChanged += delegate
        {
            if (!explorerSyncing)
                SaveSettingsQuietly();
        };
        modelRow.Controls.Add(modelLabel);
        modelRow.Controls.Add(explorerModel);
        modelRow.Controls.Add(Btn("Seçili NPC’yi kullan", UseSelectedExplorerNpc, 160, true, ButtonKind.Ghost));
        actions.Controls.Add(modelRow);
        ConfigureCheck(explorerTalk, "NPC’ye tıkla ve konuşma seçeneğini seç", true, 0);
        explorerTalk.CheckedChanged += delegate
        {
            if (!explorerSyncing)
                SaveSettingsQuietly();
        };
        actions.Controls.Add(explorerTalk);
        var optionRow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0) };
        optionRow.Controls.Add(new Label { Text = "Seçenek no", AutoSize = false, Width = 82, Height = 30, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 3, 0, 0) });
        Num(explorerOption, 1, 9, 1, 0, 100);
        explorerOption.ValueChanged += delegate
        {
            if (!explorerSyncing)
                SaveSettingsQuietly();
        };
        optionRow.Controls.Add(explorerOption);
        actions.Controls.Add(optionRow);
        actions.Controls.Add(Btn("Bulunanları sıfırla", ResetExplorerProgress, 170, true, ButtonKind.Ghost));
        explorerStatus.AutoSize = false;
        explorerStatus.Height = 64;
        explorerStatus.Width = 380;
        explorerStatus.Tag = "muted";
        explorerStatus.Margin = new Padding(0, 4, 0, 0);
        actions.Controls.Add(explorerStatus);

        left.Controls.Add(explorerGrid);
        left.Controls.Add(actions);

        root.Controls.Add(right);
        root.Controls.Add(new Panel { Dock = DockStyle.Left, Width = Ui.Pad });
        root.Controls.Add(leftFrame);

        explorerArea.SelectedIndex = 0;
        RefreshExplorerUi();
        ShowExplorerArea();
    }

    ExplorerSpot TryExplorerSpot()
    {
        var row = explorerGrid.SelectedRows.Count > 0 ? explorerGrid.SelectedRows[0] : explorerGrid.CurrentRow;
        return row == null ? null : row.Tag as ExplorerSpot;
    }

    ExplorerSpot SelectedExplorerSpot()
    {
        var spot = TryExplorerSpot();
        if (spot == null)
            throw new InvalidOperationException("Listeden bir Explorer noktası seç.");
        return spot;
    }

    void ToggleExplorerOriginal(Control mapHost)
    {
        mapHost.Visible = !mapHost.Visible;
        explorerMap.Visible = !mapHost.Visible;
        explorerOriginalButton.Text = mapHost.Visible ? "Bölgeler" : "Orijinal";
        if (mapHost.Visible)
            explorerView.Fit();
    }

    void ShowExplorerArea()
    {
        int index = explorerArea.SelectedIndex;
        explorerMap.ShowArea(index <= 0 ? null : ExplorerSpots.All.Where(s => s.Area == ExplorerSpots.Areas[index - 1]));
    }

    void StepExplorerArea(int direction)
    {
        int count = explorerArea.Items.Count;
        explorerArea.SelectedIndex = (explorerArea.SelectedIndex + direction + count) % count;
    }

    // A spot was chosen in the list or on the map: highlight it in both and bring its area into view.
    void SelectExplorerSpot(int n)
    {
        var spot = ExplorerSpots.Find(n);
        if (spot == null)
            return;
        explorerSyncing = true;
        try
        {
            foreach (DataGridViewRow row in explorerGrid.Rows)
                if (row.Tag == spot)
                {
                    if (!row.Selected)
                    {
                        explorerGrid.ClearSelection();
                        row.Selected = true;
                    }
                    explorerGrid.CurrentCell = row.Cells[0];
                    break;
                }
            explorerMap.Selected = n;
            int areaIndex = Array.IndexOf(ExplorerSpots.Areas, spot.Area) + 1;
            if (explorerArea.SelectedIndex > 0 && explorerArea.SelectedIndex != areaIndex)
                explorerArea.SelectedIndex = areaIndex;
            if (explorerArea.SelectedIndex > 0)
                ShowExplorerArea();
            explorerMap.Invalidate();
        }
        finally
        {
            explorerSyncing = false;
        }
    }

    void SaveSettingsQuietly()
    {
        try
        {
            SaveSettings();
        }
        catch { }
    }

    void RefreshExplorerUi()
    {
        var run = explorerRun;
        int found = 0, missing = 0;
        foreach (DataGridViewRow row in explorerGrid.Rows)
        {
            var spot = row.Tag as ExplorerSpot;
            if (spot == null)
                continue;
            int state = ExplorerState(spot.N);
            bool current = run != null && run.Spot != null && run.Spot.N == spot.N;
            if (state == 1)
                found++;
            else if (state == 2)
                missing++;
            row.Cells["State"].Value = current ? "▶ Şimdi" : state == 1 ? "✓ Bulundu" : state == 2 ? "Bulunamadı" : run != null &&
                run.Pending.Contains(spot.N) ? "Sırada" : "—";
            row.DefaultCellStyle.BackColor = current ? Ui.AccentTint : state == 1 ? Ui.SuccessTint : state == 2 ? Ui.WarningTint :
                explorerGrid.DefaultCellStyle.BackColor;
        }
        explorerMap.Active = run != null && run.Spot != null ? run.Spot.N : 0;
        explorerMap.Invalidate();
        string text = "Bulunan " + found + " / " + ExplorerSpots.All.Count + (missing > 0 ? " · bulunamayan " + missing : "");
        if (run != null && run.Spot != null)
            text += "\r\nÇalışıyor: " + run.Spot.N + ". nokta · kalan " + run.Pending.Count + " · " + ExplorerPhaseText(run);
        else
            text += "\r\nHaritadaki konumlar yaklaşıktır; NPC yakına gelince canlı bulunur.";
        explorerStatus.Text = text;
    }

    static string ExplorerPhaseText(ExplorerRun run)
    {
        return run.Phase == 0 ? "noktaya yürüyor" : run.Phase == 1 ? "NPC’ye yaklaşıyor" : run.Phase == 2 ? "tıklandı, onay bekleniyor" : run.Phase == 4 ? "konuşma seçeneği seçildi" :
            "çevrede NPC arıyor";
    }

    void ResetExplorerProgress()
    {
        if (!ConfirmDialog.Ask(this, "Bulunanlar sıfırlansın mı?", "Bulundu olarak işaretli tüm Explorer noktaları temizlenir; 1 → 52 baştan çalışır.",
            "Sıfırla", "Vazgeç", true))
            return;
        settings.ExplorerDone.Clear();
        explorerMissing.Clear();
        SaveSettingsQuietly();
        RefreshExplorerUi();
    }

    // The NPC the user clicked in the client gives the model number to look for.
    void UseSelectedExplorerNpc()
    {
        RequireReader();
        snapshot = reader.Capture();
        if (snapshot.Selected == 0)
            throw new InvalidOperationException("Önce oyunda Explorer NPC’ye tıkla; sonra tekrar dene.");
        var npc = snapshot.Npcs.FirstOrDefault(n => n.Uid == snapshot.Selected);
        if (npc == null)
            throw new InvalidOperationException("Seçili hedef bir NPC olarak okunamadı.");
        SetValue(explorerModel, npc.Model);
        SaveSettingsQuietly();
        Log("Explorer NPC modeli: " + npc.Model);
        status.Text = "Explorer NPC modeli " + npc.Model + " olarak ayarlandı.";
    }

    void StartExplorer(int from, bool single)
    {
        RequireReader();
        if (active)
            throw new InvalidOperationException("Önce çalışan işlemi durdur.");
        var done = new HashSet<int>(settings.ExplorerDone);
        var list = single ? new List<int> { from } : ExplorerSpots.Queue(from, done, explorerSkipDone.Checked);
        if (list.Count == 0)
            throw new InvalidOperationException("Sırada Explorer noktası yok; hepsi bulunmuş görünüyor. “Bulunanları sıfırla” ile baştan başlayabilirsin.");
        foreach (int n in list)
            explorerMissing.Remove(n);
        var run = new ExplorerRun { Pending = list, Total = list.Count };
        explorerRun = run;
        explorerAdvancing = true;
        try
        {
            if (!ExplorerWalkNext(true))
                throw new InvalidOperationException("Explorer noktasına rota bulunamadı.");
        }
        catch (Exception)
        {
            explorerRun = null;
            RefreshExplorerUi();
            throw;
        }
        finally
        {
            explorerAdvancing = false;
        }
        Log("Explorer başladı: " + list.Count + " nokta · " + list[0] + " …" + list[list.Count - 1]);
    }

    // Starts the walk to the next pending spot. A spot without a route is marked "not found" and skipped; on the first one the
    // problem is reported instead (nothing can be walked at all).
    bool ExplorerWalkNext(bool first)
    {
        var run = explorerRun;
        string lastError = null;
        while (run.Pending.Count > 0)
        {
            var spot = ExplorerSpots.Find(run.Pending[0]);
            run.Pending.RemoveAt(0);
            if (spot == null)
                continue;
            try
            {
                Position point;
                float height;
                if (!ExplorerSpots.Ground(spot, out point, out height))
                    throw new InvalidOperationException("yürünebilir zemin yok");
                var d = new DungeonLocation { Code = "EXP" + spot.N, Name = "Explorer " + spot.N, Area = ExpeditionRules.Area(point), Mode = "Serbest",
                    Region = point.Region, X = point.X, Y = point.Y, LocalX = point.LocalX, LocalY = point.LocalY, Height = height };
                run.Spot = spot;
                run.Phase = 0;
                run.Tries = 0;
                run.Uid = 0;
                run.Since = 0;
                WalkDungeon(d);
                run.Failures = 0;
                RefreshExplorerUi();
                return true;
            }
            catch (InvalidOperationException e)
            {
                if (first && run.Total == 1)
                    throw;
                lastError = e.Message;
                explorerMissing.Add(spot.N);
                run.Failures++;
                Log("Explorer " + spot.N + " atlandı · " + e.Message);
                if (run.Failures >= 3)
                {
                    run.Spot = null;
                    throw new InvalidOperationException("Explorer rotası kurulamıyor: " + lastError);
                }
            }
        }
        run.Spot = null;
        return false;
    }

    void ExplorerFinish()
    {
        var run = explorerRun;
        int found = settings.ExplorerDone.Count, missing = explorerMissing.Count;
        explorerRun = null;
        NotifyDone("Explorer bitti", "Bulunan " + found + " / " + ExplorerSpots.All.Count + (missing > 0 ? " · bulunamayan " + missing : ""));
        Stop("Explorer tamamlandı · bulunan " + found + " / " + ExplorerSpots.All.Count + (missing > 0 ? " · bulunamayan " + missing : ""));
        RefreshExplorerUi();
    }

    void ExplorerNext()
    {
        explorerAdvancing = true;
        try
        {
            if (!ExplorerWalkNext(false))
                ExplorerFinish();
        }
        catch (Exception e)
        {
            explorerRun = null;
            Stop(e.Message);
        }
        finally
        {
            explorerAdvancing = false;
        }
        RefreshExplorerUi();
    }

    void ExplorerMissingNow(string reason)
    {
        var run = explorerRun;
        explorerMissing.Add(run.Spot.N);
        Log("Explorer " + run.Spot.N + " bulunamadı · " + reason);
        ExplorerNext();
    }

    void ExplorerDoneNow()
    {
        var run = explorerRun;
        if (!settings.ExplorerDone.Contains(run.Spot.N))
            settings.ExplorerDone.Add(run.Spot.N);
        explorerMissing.Remove(run.Spot.N);
        SaveSettingsQuietly();
        Log("Explorer " + run.Spot.N + " bulundu · onaylandı");
        ExplorerNext();
    }

    string NearbyNpcModels()
    {
        var list = snapshot.Npcs.Where(n => n.Position != null && n.Position.Distance(snapshot.Player) <= 300).GroupBy(n => n.Model)
            .OrderByDescending(g => g.Count()).Take(8).Select(g => g.Key + "×" + g.Count()).ToList();
        return list.Count == 0 ? "yakında NPC yok" : "yakındaki NPC modelleri: " + string.Join(", ", list);
    }

    // The character reached the walkable point next to the spot: look around for the NPC.
    void ExplorerArrived(long now)
    {
        var run = explorerRun;
        run.Ring = ExplorerSpots.Ring(run.Spot);
        run.RingIndex = 0;
        run.PointSince = now;
        run.Phase = 3;
        routeWalker.Reset();
        Log("Explorer " + run.Spot.N + " · noktaya varıldı, NPC aranıyor (" + run.Ring.Count + " nokta)");
        status.Text = "Explorer " + run.Spot.N + " · NPC aranıyor.";
        RefreshExplorerUi();
    }

    // Called every tick of a travel while an Explorer run is on. True when it handled the tick.
    bool ExplorerTick(long now)
    {
        var run = explorerRun;
        if (run == null || run.Spot == null)
            return false;
        var player = snapshot.Player;
        uint model = (uint)explorerModel.Value;
        if (run.Phase == 0 || run.Phase == 3)
        {
            var seen = ExplorerSpots.FindNpc(snapshot, model, run.Spot.Point(), 350, run.Handled);
            if (seen != null)
            {
                run.Phase = 1;
                run.Uid = seen.Uid;
                run.Since = now;
                routeWalker.Reset();
                Log("Explorer " + run.Spot.N + " · NPC görüldü · UID " + seen.Uid + " · uzaklık " + (int)player.Distance(seen.Position));
                RefreshExplorerUi();
            }
        }
        if (run.Phase == 0)
            return false;
        if (run.Phase == 1)
        {
            var npc = snapshot.Npcs.FirstOrDefault(n => n.Uid == run.Uid);
            if (npc == null)
            {
                run.Handled.Add(run.Uid);
                run.Phase = 3;
                run.PointSince = now;
                return true;
            }
            if (now - run.Since > 60000)
            {
                run.Handled.Add(run.Uid);
                ExplorerMissingNow("NPC’ye yaklaşılamadı");
                return true;
            }
            if (player.Distance(npc.Position) > 6)
            {
                WalkChecked(npc.Position, now, null, "Explorer " + run.Spot.N + " · NPC’ye yaklaşıyor", false);
                return true;
            }
            routeWalker.Reset();
            if (!reader.ValidateNpc(npc))
                return true;
            if (!sender.Send(npc.Uid))
            {
                run.Handled.Add(run.Uid);
                ExplorerMissingNow("NPC seçimi reddedildi");
                return true;
            }
            run.Phase = 2;
            run.Tries = 1;
            run.Since = now;
            Log("Explorer " + run.Spot.N + " · NPC’ye tıklandı · UID " + run.Uid);
            RefreshExplorerUi();
            return true;
        }
        if (run.Phase == 2)
        {
            if (snapshot.Selected == run.Uid)
            {
                if (now - run.Since < 800)
                    return true;
                run.Handled.Add(run.Uid);
                if (explorerTalk.Checked)
                {
                    int option = (int)explorerOption.Value;
                    bool sent = false;
                    try
                    {
                        sent = sender.Talk(run.Uid, option);
                    }
                    catch (System.IO.IOException e)
                    {
                        Log("Explorer " + run.Spot.N + " · konuşma gönderilemedi · " + e.Message);
                    }
                    if (sent)
                    {
                        run.Phase = 4;
                        run.Since = now;
                        Log("Explorer " + run.Spot.N + " · NPC ile konuşuldu · seçenek " + option);
                        RefreshExplorerUi();
                        return true;
                    }
                    ExplorerMissingNow("konuşma isteği reddedildi");
                    return true;
                }
                ExplorerDoneNow();
                return true;
            }
            if (now - run.Since > 4000)
            {
                var npc = snapshot.Npcs.FirstOrDefault(n => n.Uid == run.Uid);
                if (run.Tries >= 3 || npc == null || !reader.ValidateNpc(npc))
                {
                    run.Handled.Add(run.Uid);
                    ExplorerMissingNow("NPC seçimi doğrulanamadı");
                    return true;
                }
                if (!sender.Send(npc.Uid))
                {
                    run.Handled.Add(run.Uid);
                    ExplorerMissingNow("NPC seçimi reddedildi");
                    return true;
                }
                run.Tries++;
                run.Since = now;
            }
            return true;
        }
        if (run.Phase == 4)
        {
            // Give the server a moment to answer the dialog choice before moving on.
            if (now - run.Since >= 1500)
                ExplorerDoneNow();
            return true;
        }
        // Phase 3: walk the ring points, one after the other, until the NPC shows up.
        if (run.RingIndex >= run.Ring.Count)
        {
            ExplorerMissingNow("çevrede NPC yok · " + NearbyNpcModels());
            return true;
        }
        var point = run.Ring[run.RingIndex];
        bool clear = false;
        try
        {
            clear = sender.NavigationProbe(player, point);
        }
        catch (System.IO.IOException)
        {
            clear = false;
        }
        if (!clear || now - run.PointSince > 25000 || player.Distance(point) <= 5)
        {
            run.RingIndex++;
            run.PointSince = now;
            routeWalker.Reset();
            return true;
        }
        WalkChecked(point, now, null, "Explorer " + run.Spot.N + " · NPC aranıyor", false);
        return true;
    }
}

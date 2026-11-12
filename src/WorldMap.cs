using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

sealed class MapLeg
{
    internal List<PointF> Points = new List<PointF>();
    internal string Label = "";
    internal string Code = "";
    internal bool Estimated;
}

// World map: the game's own terrain map with the dungeon entrances placed at their world coordinates.

sealed class WorldMapView : Control
{
    // The map is stitched from the game's own minimap tiles (zoom 6, 4x4 regions per tile), so world
    // coordinates map to pixels exactly: pixel = coordinate / 4 + offset, with Y flipped (north is up).
    internal const double AX = 1.0 / 6, AY = 0.0, AC = 2848.0, BX = 0.0, BY = -1.0 / 6, BC = 704.0;
    internal const string ResourceName = "world-map-game.jpg";
    static readonly KeyValuePair<int, string>[] Towns = { new KeyValuePair<int, string>(20, "Constantinople"), new KeyValuePair<int,
        string>(25, "Samarkand"), new KeyValuePair<int, string>(5, "Hotan"), new KeyValuePair<int, string>(2, "Donwhang"),
        new KeyValuePair<int, string>(1, "Jangan"), new KeyValuePair<int, string>(252, "Nanzhang") };

    Bitmap baseMap;
    bool userMoved, dragging, moved;
    // View state in artwork pixels: zoom = screen pixels per artwork pixel, (cx, cy) = artwork pixel at the view centre.
    double zoom = 0.4, cx = 1833, cy = 667, dragCx, dragCy;
    Point dragStart;
    List<DungeonLocation> sites = new List<DungeonLocation>();

    internal Position Player;
    internal string Selected;
    internal Func<string, bool> Visited;
    internal event Action<string> DungeonPicked;
    internal event Action<string> DungeonToggled;
    internal event Action<double, double> GoRequested;
    internal PointF? Target;
    // Planned road route of every queued (or selected) dungeon, one leg per hop: you -> 1, exit of 1 -> 2, ...
    internal List<MapLeg> Legs;
    // Route to every dungeon from its best town, each in the dungeon's own colour (checkbox in the corner).
    internal List<MapLeg> AllRoutes;
    internal bool ShowAll;
    internal event Action AllRoutesWanted;
    readonly CheckBox allBox = new CheckBox();
    readonly CheckBox learnBox = new CheckBox();
    internal event Action<bool> LearnChanged;
    internal List<List < PointF>> Learned;
    PointF hover;

    static Color Hue(int index, int count)
    {
        double h = 360.0 * index / Math.Max(1, count), s = 0.85, v = 1.0;
        double c = v * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = v - c, r = 0, g = 0, b = 0;
        if (h < 60)
        {
            r = c;
            g = x;
        }
        else if (h < 120)
        {
            r = x;
            g = c;
        }
        else if (h < 180)
        {
            g = c;
            b = x;
        }
        else if (h < 240)
        {
            g = x;
            b = c;
        }
        else if (h < 300)
        {
            r = x;
            b = c;
        }
        else
        {
            r = c;
            b = x;
        }
        return Color.FromArgb((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
    }

    // Same colour a dungeon has on the exported route picture: its position in the code-sorted list.

    internal Color ColorOf(string code)
    {
        int index = sites.FindIndex(s => s.Code == code);
        return index < 0 ? Color.White : Hue(index, sites.Count);
    }

    bool hasHover;
    internal Func<IList < string>> Queue;

    internal bool Ready
    {
        get { return baseMap != null; }
    }

    // Inverse of ToImage for the axis-aligned map: screen point to game X/Y.

    PointF ToWorld(Point screen)
    {
        double ix = cx + (screen.X - Width / 2.0) / zoom, iy = cy + (screen.Y - Height / 2.0) / zoom;
        return new PointF((float)((ix - AC) / AX), (float)((iy - BC) / BY));
    }

    internal static PointF ToImage(double x, double y)
    {
        return new PointF((float)(AX * x + AY * y + AC), (float)(BX * x + BY * y + BC));
    }

    internal IEnumerable<DungeonLocation> Sites
    {
        set
        {
            sites = value.GroupBy(d => d.Code).Select(g => g.First()).OrderBy(d => d.Code).ToList();
            Invalidate();
        }
    }

    internal WorldMapView()
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
        allBox.Text = "Tüm dungeon rotalarını renkli göster";
        allBox.AutoSize = true;
        allBox.BackColor = Ui.Surface;
        allBox.ForeColor = Ui.Text;
        allBox.Location = new Point(8, 8);
        allBox.CheckedChanged += delegate
        {
            ShowAll = allBox.Checked;
            if (ShowAll && AllRoutes == null && AllRoutesWanted != null)
                AllRoutesWanted();
            Invalidate();
        };
        Controls.Add(allBox);
        learnBox.Text = "Yol öğret: işaretle, karakteri elle yürüt, işareti kaldır";
        learnBox.AutoSize = true;
        learnBox.BackColor = Ui.Surface;
        learnBox.ForeColor = Ui.Text;
        learnBox.Location = new Point(8, 34);
        learnBox.CheckedChanged += delegate
        {
            if (LearnChanged != null)
                LearnChanged(learnBox.Checked);
        };
        Controls.Add(learnBox);
        // View picker: the whole map or one city, as in the game's own world map window; "Merkezle" jumps to the character.
        viewBox.DropDownStyle = ComboBoxStyle.DropDownList;
        viewBox.Items.Add("Tüm harita");
        foreach (var town in Towns)
            viewBox.Items.Add(town.Value);
        viewBox.SelectedIndex = 0;
        viewBox.Location = new Point(8, 62);
        viewBox.Width = 170;
        viewBox.SelectedIndexChanged += delegate { ShowArea(viewBox.SelectedIndex); };
        Controls.Add(viewBox);
        centerButton.Text = "Merkezle";
        centerButton.Location = new Point(184, 61);
        centerButton.Size = new Size(84, 25);
        Fancy.Kind(centerButton, ButtonKind.Primary);
        centerButton.Click += delegate { CenterOnPlayer(); };
        Controls.Add(centerButton);
        foreach (var pair in new[] { Tuple.Create(exportButton, "Yollarımı kopyala", 274), Tuple.Create(importButton,
            "Yolları yapıştır", 422) })
        {
            var button = pair.Item1;
            button.Text = pair.Item2;
            button.Location = new Point(pair.Item3, 61);
            button.Size = new Size(ReferenceEquals(button, exportButton) ? 140 : 130, 25);
            Fancy.Kind(button, ButtonKind.Secondary);
            Controls.Add(button);
        }
        exportButton.Click += delegate
        {
            if (ExportRequested != null)
                ExportRequested();
        };
        importButton.Click += delegate
        {
            if (ImportRequested != null)
                ImportRequested();
        };
    }

    readonly ComboBox viewBox = new ComboBox();
    readonly Button centerButton = new Button();

    void ShowArea(int index)
    {
        if (index <= 0)
        {
            userMoved = false;
            Fit();
            Invalidate();
            return;
        }
        var town = Towns[index - 1];
        var gate = ExpeditionRules.Network.Gates.FirstOrDefault(x => x.Id == town.Key);
        if (gate == null)
            return;
        var p = ToImage(gate.X, gate.Y);
        cx = p.X;
        cy = p.Y;
        zoom = 2.6;
        userMoved = true;
        Invalidate();
    }

    internal void CenterOnPlayer()
    {
        if (Player == null || !Player.Outside)
            return;
        var p = ToImage(Player.X, Player.Y);
        cx = p.X;
        cy = p.Y;
        zoom = Math.Max(zoom, 2.2);
        userMoved = true;
        Invalidate();
    }

    internal void LoadBase()
    {
        if (baseMap != null)
            return;
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
        {
            if (stream == null)
                return;
            using (var image = Image.FromStream(stream))
                baseMap = new Bitmap(image);
        }
        if (!userMoved)
            Fit();
        Invalidate();
    }

    void Fit()
    {
        if (Width < 50 || Height < 50)
            return;
        double w = baseMap != null ? baseMap.Width : 3667, h = baseMap != null ? baseMap.Height : 1334;
        zoom = Math.Min((Width - 20) / w, (Height - 20) / h);
        cx = w / 2;
        cy = h / 2;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (!userMoved)
            Fit();
    }

    PointF S(double x, double y)
    {
        var p = ToImage(x, y);
        return new PointF((float)((p.X - cx) * zoom + Width / 2.0), (float)((p.Y - cy) * zoom + Height / 2.0));
    }

    internal void CenterOn(DungeonLocation d)
    {
        var p = ToImage(d.X, d.Y);
        cx = p.X;
        cy = p.Y;
        userMoved = true;
        Invalidate();
    }

    // Scrolls to the entrance only when it is not already comfortably on screen.

    internal void Reveal(DungeonLocation d)
    {
        var p = S(d.X, d.Y);
        if (p.X < 40 || p.Y < 40 || p.X > Width - 40 || p.Y > Height - 40)
            CenterOn(d);
    }

    DungeonLocation HitTest(Point point)
    {
        DungeonLocation best = null;
        double nearest = 15 * 15;
        foreach (var d in sites)
        {
            var p = S(d.X, d.Y);
            double distance = (p.X - point.X) * (p.X - point.X) + (p.Y - point.Y) * (p.Y - point.Y);
            if (distance <= nearest)
            {
                nearest = distance;
                best = d;
            }
        }
        return best;
    }

    // White text with a dark outline stays readable on both the artwork and the dark background.

    static void Label(Graphics g, string text, Font font, Brush brush, float x, float y)
    {
        foreach (var o in new[] { new PointF(-1, 0), new PointF(1, 0), new PointF(0, -1), new PointF(0, 1) })
            g.DrawString(text, font, Brushes.Black, x + o.X, y + o.Y);
        g.DrawString(text, font, brush, x, y);
    }

    // The scaled map is the costly part of a repaint (about 80 ms); it is redrawn only when the view changes, so the marks and routes on
    // top (repainted every time the character moves) are cheap.
    Bitmap layer;
    double layerZoom, layerCx, layerCy;
    Size layerSize;
    bool layerFast;

    void DrawBase(Graphics g)
    {
        if (Width < 2 || Height < 2)
            return;
        if (layer == null || layerSize != ClientSize || layerZoom != zoom || layerCx != cx || layerCy != cy || layerFast != dragging)
        {
            if (layer != null)
                layer.Dispose();
            layer = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            layerSize = ClientSize;
            layerZoom = zoom;
            layerCx = cx;
            layerCy = cy;
            layerFast = dragging;
            using (var lg = Graphics.FromImage(layer))
            {
                lg.Clear(BackColor);
                lg.InterpolationMode = dragging ? InterpolationMode.Bilinear : InterpolationMode.HighQualityBilinear;
                lg.PixelOffsetMode = PixelOffsetMode.Half;
                float left = (float)((0 - cx) * zoom + Width / 2.0), top = (float)((0 - cy) * zoom + Height / 2.0);
                lg.DrawImage(baseMap, new RectangleF(left, top, (float)(baseMap.Width * zoom), (float)(baseMap.Height * zoom)));
            }
        }
        g.DrawImageUnscaled(layer, 0, 0);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var small = new Font("Segoe UI", 9f, FontStyle.Bold))
            using (var big = new Font("Segoe UI", 10.5f, FontStyle.Bold))
            {
                if (baseMap != null)
                    DrawBase(g);
                else
                {
                    Label(g, "Harita resmi bulunamadı.", big, Brushes.White, 20, 20);
                }
                var queued = Queue == null ? null : Queue();
                var learned = Learned;
                if (learned != null)
                    foreach (var path in learned)
                    {
                        var pts = path.Select(q => S(q.X, q.Y)).ToArray();
                        if (pts.Length < 2)
                            continue;
                        using (var line = new Pen(Color.White, 2.5f) { DashStyle = DashStyle.Dot })
                            g.DrawLines(line, pts);
                    }
                var every = AllRoutes;
                if (ShowAll && every != null)
                {
                    foreach (var route in every)
                    {
                        var pts = route.Points.Select(q => S(q.X, q.Y)).ToArray();
                        if (pts.Length < 2)
                            continue;
                        using (var outline = new Pen(Color.FromArgb(190, 0, 0, 0), 5f) { LineJoin = LineJoin.Round })
                            using (var line = new Pen(ColorOf(route.Code), 2.6f) { LineJoin = LineJoin.Round })
                            {
                                g.DrawLines(outline, pts);
                                g.DrawLines(line, pts);
                            }
                    }
                }
                var legs = Legs;
                if (legs != null)
                {
                    var colors = new[] { Color.Gold, Color.Cyan, Color.Orange, Color.Magenta, Color.LimeGreen, Color.DeepSkyBlue };
                    for (int i = 0; i < legs.Count; i++)
                    {
                        var pts = legs[i].Points.Select(q => S(q.X, q.Y)).ToArray();
                        if (pts.Length < 2)
                            continue;
                        var style = legs[i].Estimated ? DashStyle.Dash : DashStyle.Solid;
                        using (var outline = new Pen(Color.FromArgb(200, 0, 0, 0), 5.5f) { LineJoin = LineJoin.Round,
                            DashStyle = style })
                            using (var line = new Pen(colors[i % colors.Length], 3f) { LineJoin = LineJoin.Round,
                                DashStyle = style })
                            {
                                g.DrawLines(outline, pts);
                                g.DrawLines(line, pts);
                            }
                        var mid = pts[pts.Length / 2];
                        Label(g, legs[i].Label, small, Brushes.White, mid.X + 4, mid.Y - 16);
                    }
                }
                foreach (var town in Towns)
                {
                    var gate = ExpeditionRules.Network.Gates.FirstOrDefault(x => x.Id == town.Key);
                    if (gate == null)
                        continue;
                    var p = S(gate.X, gate.Y);
                    var diamond = new[] { new PointF(p.X, p.Y - 8), new PointF(p.X + 8, p.Y), new PointF(p.X, p.Y + 8),
                        new PointF(p.X - 8, p.Y) };
                    g.FillPolygon(Brushes.DodgerBlue, diamond);
                    g.DrawPolygon(Pens.White, diamond);
                    Label(g, town.Value, small, Brushes.White, p.X + 10, p.Y - 8);
                }
                foreach (var d in sites)
                {
                    var p = S(d.X, d.Y);
                    bool selected = d.Code == Selected, visited = Visited != null && Visited(d.Code);
                    if (selected)
                        using (var ring = new Pen(Color.Gold, 3))
                            g.DrawEllipse(ring, p.X - 13, p.Y - 13, 26, 26);
                    if (ShowAll)
                        using (var tint = new SolidBrush(ColorOf(d.Code)))
                            g.FillEllipse(tint, p.X - 7, p.Y - 7, 14, 14);
                    else
                        g.FillEllipse(visited ? Brushes.SeaGreen : Brushes.OrangeRed, p.X - 7, p.Y - 7, 14, 14);
                    g.DrawEllipse(Pens.White, p.X - 7, p.Y - 7, 14, 14);
                    Label(g, d.Code, selected ? big : small, selected ? Brushes.Gold : Brushes.White, p.X + 10, p.Y - (selected ?
                        22 : 18));
                    if (selected || zoom > 0.55)
                        Label(g, d.Name, small, Brushes.White, p.X + 10, p.Y + 4);
                }
                if (queued != null)
                {
                    using (var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment =
                        StringAlignment.Center })
                    {
                        for (int i = 0; i < queued.Count; i++)
                        {
                            var stop = sites.FirstOrDefault(s => s.Code == queued[i]);
                            if (stop == null)
                                continue;
                            var p = S(stop.X, stop.Y);
                            var badge = new RectangleF(p.X - 24, p.Y - 24, 17, 17);
                            g.FillEllipse(Brushes.RoyalBlue, badge);
                            g.DrawEllipse(Pens.White, badge.X, badge.Y, badge.Width, badge.Height);
                            g.DrawString((i + 1).ToString(), small, Brushes.White, badge, center);
                        }
                    }
                }
                if (Player != null && Player.Outside)
                {
                    var p = S(Player.X, Player.Y);
                    g.FillEllipse(Brushes.Cyan, p.X - 6, p.Y - 6, 12, 12);
                    g.DrawEllipse(Pens.Black, p.X - 6, p.Y - 6, 12, 12);
                    Label(g, "Sen", small, Brushes.Cyan, p.X + 8, p.Y - 8);
                }
                if (Target.HasValue)
                {
                    var tp = S(Target.Value.X, Target.Value.Y);
                    using (var pen = new Pen(Color.Red, 3))
                    {
                        g.DrawLine(pen, tp.X - 9, tp.Y - 9, tp.X + 9, tp.Y + 9);
                        g.DrawLine(pen, tp.X - 9, tp.Y + 9, tp.X + 9, tp.Y - 9);
                    }
                    Label(g, "Hedef", small, Brushes.Red, tp.X + 10, tp.Y - 8);
                }
                if (hasHover)
                {
                    string readout = "X " + Math.Round(hover.X) + "   Y " + Math.Round(hover.Y) +
                        "   ·   sağ tık: buraya git";
                    var rs = g.MeasureString(readout, small);
                    using (var back = new SolidBrush(Color.FromArgb(170, 0, 0, 0)))
                        g.FillRectangle(back, Width - rs.Width - 14, 6, rs.Width + 8, rs.Height + 6);
                    g.DrawString(readout, small, Brushes.White, Width - rs.Width - 10, 9);
                }
                const string legend =
                    "Kırmızı: giriş · Yeşil: girildi · Altın halka: seçili · Mavi: şehir · Turkuaz: karakter\nFare tekerleği: yakınlaştır · Sürükle: kaydır · Çift tık: sığdır · Noktaya tıkla: dungeon seç · Shift+tık: sıraya ekle/çıkar · Boş yere sağ tık: oraya git";
                var legendSize = g.MeasureString(legend, small, Math.Max(200, Width - 24));
                float legendTop = Height - legendSize.Height - 14;
                using (var back = new SolidBrush(Color.FromArgb(170, 0, 0, 0)))
                    g.FillRectangle(back, 6, legendTop - 4, legendSize.Width + 8, legendSize.Height + 8);
                g.DrawString(legend, small, Brushes.White, new RectangleF(10, legendTop, Math.Max(200, Width - 24),
                    legendSize.Height + 4));
            }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        double wx = cx + (e.X - Width / 2.0) / zoom, wy = cy + (e.Y - Height / 2.0) / zoom;
        zoom = Math.Max(0.1, Math.Min(4.0, zoom * (e.Delta > 0 ? 1.25 : 0.8)));
        cx = wx - (e.X - Width / 2.0) / zoom;
        cy = wy - (e.Y - Height / 2.0) / zoom;
        userMoved = true;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
            return;
        Focus();
        dragging = true;
        moved = false;
        dragStart = e.Location;
        dragCx = cx;
        dragCy = cy;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!dragging)
        {
            Cursor = HitTest(e.Location) != null ? Cursors.Hand : Cursors.Default;
            hover = ToWorld(e.Location);
            hasHover = true;
            Invalidate();
            return;
        }
        int dx = e.X - dragStart.X, dy = e.Y - dragStart.Y;
        if (Math.Abs(dx) > 3 || Math.Abs(dy) > 3)
            moved = true;
        if (!moved)
            return;
        cx = dragCx - dx / zoom;
        cy = dragCy - dy / zoom;
        userMoved = true;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Right)
        {
            ShowGoMenu(e.Location);
            return;
        }
        if (e.Button != MouseButtons.Left)
            return;
        bool click = dragging && !moved;
        dragging = false;
        if (!click)
        {
            Invalidate();
            return;
        }
        var hit = HitTest(e.Location);
        if (hit == null)
            return;
        if ((ModifierKeys & Keys.Shift) != 0)
        {
            if (DungeonToggled != null)
                DungeonToggled(hit.Code);
        }
        else if (DungeonPicked != null)
            DungeonPicked(hit.Code);
    }

    internal event Action<int> LearnedDeleteRequested;
    internal event Action ExportRequested, ImportRequested;
    internal List<bool> LearnedBuiltin;
    readonly Button exportButton = new Button(), importButton = new Button();

    static double SegmentDistance(Point p, PointF a, PointF b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, length = dx * dx + dy * dy;
        double t = length < 0.001 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / length));
        double ex = a.X + t * dx - p.X, ey = a.Y + t * dy - p.Y;
        return Math.Sqrt(ex * ex + ey * ey);
    }

    void ShowGoMenu(Point at)
    {
        var world = ToWorld(at);
        string label = "X " + Math.Round(world.X) + "  Y " + Math.Round(world.Y);
        var menu = new ContextMenuStrip();
        if (Target.HasValue)
        {
            menu.Items.Add("Hedefi sil", null, delegate
            {
                Target = null;
                Invalidate();
            });
            menu.Items.Add(new ToolStripSeparator());
        }
        var site = HitTest(at);
        if (site != null)
        {
            string code = site.Code;
            menu.Items.Add("Sıraya ekle / çıkar (" + code + ")", null, delegate
            {
                if (DungeonToggled != null)
                    DungeonToggled(code);
            });
            menu.Items.Add(new ToolStripSeparator());
        }
        var learned = Learned;
        if (learned != null)
        {
            int nearest = -1;
            double best = 10;
            for (int i = 0; i < learned.Count; i++)
            {
                var pts = learned[i].Select(q => S(q.X, q.Y)).ToArray();
                for (int k = 1; k < pts.Length; k++)
                {
                    double d = SegmentDistance(at, pts[k - 1], pts[k]);
                    if (d < best)
                    {
                        best = d;
                        nearest = i;
                    }
                }
            }
            if (nearest >= 0)
            {
                int index = nearest, count = learned[nearest].Count;
                menu.Items.Add("Öğrenilen yolu sil (" + (index + 1) + ". yol · " + count + " nokta" + (LearnedBuiltin != null &&
                    index < LearnedBuiltin.Count && LearnedBuiltin[index] ? " · hazır yol" : "") + ")", null, delegate
                {
                    if (MessageBox.Show(this, (index + 1) + ". öğrenilen yol silinsin mi?", "Öğrenilen yol",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes && LearnedDeleteRequested != null)
                        LearnedDeleteRequested(index);
                });
                menu.Items.Add(new ToolStripSeparator());
            }
        }
        menu.Items.Add("Buraya git (" + label + ")", null, delegate
        {
            if (GoRequested != null)
                GoRequested(world.X, world.Y);
        });
        menu.Items.Add("Koordinatı kopyala", null, delegate
        {
            try
            {
                Clipboard.SetText(label);
            }
            catch { }
        });
        menu.Show(this, at);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        userMoved = false;
        Fit();
        Invalidate();
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

sealed partial class MemorySelectV3Form
{
    readonly WorldMapView worldMap = new WorldMapView();
    TabPage worldTab;
    bool pickingFromMap;

    void BuildWorldMap()
    {
        worldTab = new TabPage("Dünya haritası");
        worldMap.Dock = DockStyle.Fill;
        worldTab.Controls.Add(worldMap);
        grimViews.TabPages.Add(worldTab);
        worldMap.Sites = V3Data.Dungeons;
        worldMap.Visited = code => visitedDungeons.Any(id => id.StartsWith(code + "|", StringComparison.Ordinal));
        worldMap.DungeonPicked += PickDungeonFromMap;
        worldMap.GoRequested += delegate(double x, double y)
        {
            Safe(delegate { GoToPoint(x, y); });
        };
        worldMap.Queue = delegate { return QueueOrder(); };
        worldMap.AllRoutesWanted += ComputeAllRoutes;
        worldMap.LearnChanged += SetRecording;
        worldMap.ExportRequested += ExportLearned;
        worldMap.ImportRequested += ImportLearned;
        worldMap.LearnedDeleteRequested += delegate(int index)
        {
            var all = AllLearned();
            if (index < 0 || index >= all.Count)
                return;
            if (all[index].Builtin)
            {
                if (!settings.IgnoredDefaults.Contains(LearnedKey(all[index].Text)))
                    settings.IgnoredDefaults.Add(LearnedKey(all[index].Text));
            }
            else
                settings.LearnedPaths.Remove(all[index].Text);
            try
            {
                settings.Save();
            }
            catch (Exception) { }
            Log("Öğrenilen yol silindi" + (all[index].Builtin ? " (hazır yol bu bilgisayarda gizlendi)" : "") +
                " · kalan " + (all.Count - 1));
            ShowLearned();
        };
        ShowLearned();
        worldMap.DungeonToggled += delegate(string code)
        {
            var trial = V3Data.Dungeons.FirstOrDefault(x => x.Code == code && x.Mode == "Solo Trial");
            if (trial != null)
                Safe(delegate { ToggleQueue(trial); });
        };
        grimViews.SelectedIndexChanged += delegate
        {
            if (grimViews.SelectedTab == worldTab)
                SyncWorldMap();
        };
    }

    // Right click on the map: plan a road route to that point and walk it, like phBot's map.

    void GoToPoint(double x, double y)
    {
        RequireReader();
        if (queueRunning)
            throw new InvalidOperationException("Dungeon sırası çalışırken haritadan gidilemez; önce sırayı durdur.");
        snapshot = reader.Capture();
        if (!snapshot.PlayerAlive)
            throw new InvalidOperationException("Karakter ölü.");
        if (V3Data.IsCrypt(snapshot.Player))
            throw new InvalidOperationException("Dungeon içindeyken haritadan gidilemez.");
        var p = V2Rules.Center(x, y, 0);
        float height;
        if (!ExpeditionRules.TryGround(p, out height))
            throw new InvalidOperationException("Bu noktada yürünebilir zemin yok; yola daha yakın bir yere sağ tıkla.");
        var d = new DungeonLocation { Code = "GİT", Name = "X " + Math.Round(x) + " Y " + Math.Round(y), Area =
            ExpeditionRules.Area(p), Mode = "Serbest", Region = p.Region, X = x, Y = y, LocalX = p.LocalX, LocalY = p.LocalY,
            Height = height };
        worldMap.Target = new PointF((float)x, (float)y);
        worldMap.Invalidate();
        WalkDungeon(d);
    }

    void SyncWorldMap()
    {
        if (worldTab == null)
            return;
        var d = SelectedDungeon();
        worldMap.Selected = d == null ? null : d.Code;
        if (d != null && !pickingFromMap)
            worldMap.Reveal(d);
        worldMap.Invalidate();
        ShowLearned();
        RefreshRoutes();
    }

    // The client's walkability query gives no 0/1 answer for points at different heights (stairs, ramps); the walker then lets the
    // client climb and relies on its progress check, instead of stopping on the unexpected result.
    // The client gives no 0/1 answer when the two points differ in height (stairs, ramps). While travelling, a short steep step is trusted
    // (the map now models stairs and walls, and the walker still checks progress); a longer or flat step with that answer still stops the walk.

    static Func<Position, Position, bool> ClimbSafeProbe(TargetSender sender, bool allowed)
    {
        return delegate(Position a, Position b)
        {
            try
            {
                return sender.NavigationProbe(a, b);
            }
            catch (IOException)
            {
                if (allowed && Math.Abs(a.Height - b.Height) > 8 && a.Distance(b) <= 40)
                    return true;
                throw;
            }
        };
    }

    // ---- Learned passages: stairs, gates and building interiors are not in the map data, so the user walks them once by hand. ----
    bool recording;
    readonly List<Position> recorded = new List<Position>();

    static List<Position> ParseLearned(string text)
    {
        var points = new List<Position>();
        foreach (string item in (text ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var part = item.Split(',');
            double x, y, h;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (part.Length == 3 && double.TryParse(part[0], System.Globalization.NumberStyles.Float, inv, out x) &&
                double.TryParse(part[1], System.Globalization.NumberStyles.Float, inv, out y) && double.TryParse(part[2],
                System.Globalization.NumberStyles.Float, inv, out h))
                points.Add(V2Rules.Center(x, y, (float)h));
        }
        return points;
    }

    void ShowLearned()
    {
        if (settings == null || settings.LearnedPaths == null)
            return;
        var items = AllLearned();
        worldMap.Learned = items.Select(item => ParseLearned(item.Text).Select(p => new PointF((float)p.X, (float)p.Y)).ToList())
            .ToList();
        worldMap.LearnedBuiltin = items.Select(item => item.Builtin).ToList();
        worldMap.Invalidate();
    }

    void SetRecording(bool on)
    {
        if (on)
        {
            recorded.Clear();
            recording = true;
            Log("Yol öğrenme başladı: karakteri elle yürüt, bitince işareti kaldır.");
            return;
        }
        recording = false;
        if (recorded.Count < 2)
        {
            Log("Öğrenilen yol çok kısa; kaydedilmedi.");
            return;
        }
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        settings.LearnedPaths.Add(string.Join(";", recorded.Select(p => p.X.ToString("F1", inv) + "," + p.Y.ToString("F1",
            inv) + "," + p.Height.ToString("F1", inv))));
        while (settings.LearnedPaths.Count > 60)
            settings.LearnedPaths.RemoveAt(0);
        try
        {
            settings.Save();
        }
        catch (Exception) { }
        Log("Yol öğrenildi · " + recorded.Count + " nokta · X " + Math.Round(recorded[0].X) + " Y " + Math.Round(recorded[0].Y) +
            " → X " + Math.Round(recorded[recorded.Count - 1].X) + " Y " + Math.Round(recorded[recorded.Count - 1].Y));
        ShowLearned();
    }

    void RecordPath()
    {
        if (!recording || snapshot == null || snapshot.Player == null || !snapshot.Player.Outside)
            return;
        if (recorded.Count == 0 || recorded[recorded.Count - 1].Distance(snapshot.Player) >= 6)
            recorded.Add(snapshot.Player.Copy());
    }

    sealed class LearnedItem
    {
        internal string Text;
        internal bool Builtin;
    }

    static string LearnedKey(string text)
    {
        return text.Substring(0, Math.Min(40, text.Length)) + "|" + text.Length;
    }

    // Paths shipped with the bot (walked once by hand) that every user starts with; each user may hide them and add their own.

    static List<string> DefaultLearned()
    {
        var list = new List<string>();
        try
        {
            using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("learned-defaults.txt"))
                using (var reader = new StreamReader(stream))
                    foreach (string line in reader.ReadToEnd().Split(new[] { (char)10, (char)13 }, StringSplitOptions.RemoveEmptyEntries))
                        if (line.Contains(","))
                            list.Add(line.Trim());
        }
        catch (Exception) { }
        return list;
    }

    List<LearnedItem> AllLearned()
    {
        var items = new List<LearnedItem>();
        if (settings == null || settings.LearnedPaths == null)
            return items;
        foreach (string text in settings.LearnedPaths)
            items.Add(new LearnedItem { Text = text });
        var ignored = settings.IgnoredDefaults ?? new List<string>();
        foreach (string text in DefaultLearned())
            if (!ignored.Contains(LearnedKey(text)))
                items.Add(new LearnedItem { Text = text, Builtin = true });
        return items;
    }

    // Friends can swap their own paths through the clipboard (one path per line).

    void ExportLearned()
    {
        if (settings.LearnedPaths.Count == 0)
        {
            Log("Dışa aktarılacak kendi öğrettiğin yol yok.");
            return;
        }
        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, settings.LearnedPaths));
            Log("Öğrenilen yollar panoya kopyalandı · " + settings.LearnedPaths.Count + " yol");
        }
        catch (Exception)
        {
            Log("Pano kullanılamadı.");
        }
    }

    void ImportLearned()
    {
        string text;
        try
        {
            text = Clipboard.GetText();
        }
        catch (Exception)
        {
            text = null;
        }
        int added = 0;
        foreach (string line in (text ?? "").Split(new[] { (char)10, (char)13 }, StringSplitOptions.RemoveEmptyEntries))
        {
            string path = line.Trim();
            if (ParseLearned(path).Count < 2 || settings.LearnedPaths.Contains(path) || DefaultLearned().Contains(path))
                continue;
            settings.LearnedPaths.Add(path);
            added++;
        }
        if (added > 0)
        {
            try
            {
                settings.Save();
            }
            catch (Exception) { }
            ShowLearned();
        }
        Log(added > 0 ? "Panodan " + added + " yol eklendi." : "Panoda eklenecek yeni yol bulunamadı.");
    }

    sealed class LearnedChain
    {
        internal List<Position> Points = new List<Position>();
        internal double Length;

        internal Position Start
        {
            get { return Points[0]; }
        }

        internal Position End
        {
            get { return Points[Points.Count - 1]; }
        }
    }

    // One point about every 24 units (plus the points where the ground height changes quickly, i.e. stairs).

    static List<Position> Thin(List<Position> points)
    {
        var kept = new List<Position> { points[0] };
        for (int i = 1; i < points.Count - 1; i++)
            if (kept[kept.Count - 1].Distance(points[i]) >= 24 || Math.Abs(points[i].Height - kept[kept.Count - 1].Height) >= 40)
                kept.Add(points[i]);
        kept.Add(points[points.Count - 1]);
        return kept;
    }

    static double TrackLength(List<Position> points)
    {
        double length = 0;
        for (int i = 1; i < points.Count; i++)
            length += points[i - 1].Distance(points[i]);
        return length;
    }

    // Walking cost of a planned route; a teleport counts as 180 units.

    static double PlanLength(Position from, List<TransitStep> plan)
    {
        double length = 0;
        var at = from;
        foreach (var step in plan)
        {
            if (step.From != null)
            {
                var gate = step.From.Point();
                length += at.Distance(gate) + RoadMesh.HopCost;
                at = step.To.Point();
                continue;
            }
            var to = step.To.Point();
            length += at.Distance(to);
            at = to;
        }
        return length;
    }

    static Position PlanEnd(Position from, List<TransitStep> plan)
    {
        return plan.Count == 0 ? from : plan[plan.Count - 1].To.Point();
    }

    // Compares the map route with the routes the user walked by hand (alone or two in a row, either direction) and keeps the shortest.

    List<TransitStep> ChooseLearned(Position from, Position goal, List<TransitStep> meshPlan)
    {
        var paths = new List<List < Position>>();
        foreach (var item in AllLearned())
        {
            var points = ParseLearned(item.Text);
            if (points.Count >= 2)
                paths.Add(Thin(points));
        }
        if (paths.Count == 0)
            return meshPlan;
        var chains = new List<LearnedChain>();
        for (int a = 0; a < paths.Count; a++)
            for (int da = 0; da < 2; da++)
            {
                var first = da == 0 ? paths[a] : Enumerable.Reverse(paths[a]).ToList();
                chains.Add(new LearnedChain { Points = first, Length = TrackLength(first) });
                for (int b = 0; b < paths.Count; b++)
                    if (b != a)
                        for (int db = 0; db < 2; db++)
                        {
                            var second = db == 0 ? paths[b] : Enumerable.Reverse(paths[b]).ToList();
                            if (first[first.Count - 1].Distance(second[0]) > 90)
                                continue;
                            var joined = new List<Position>(first);
                            joined.AddRange(second);
                            chains.Add(new LearnedChain { Points = joined, Length = TrackLength(joined) });
                        }
            }
        double meshCost = PlanLength(from, meshPlan) + 3 * PlanEnd(from, meshPlan).Distance(goal);
        double bestCost = meshCost;
        LearnedChain best = null;
        List<TransitStep> bestLead = null, bestTail = null;
        var cache = new Dictionary<string, List < TransitStep>>();
        Func<Position, Position, List < TransitStep>> plan = (s, g) =>
        {
            string key = (int)s.X + "," + (int)s.Y + ">" + (int)g.X + "," + (int)g.Y;
            List<TransitStep> found;
            if (cache.TryGetValue(key, out found))
                return found;
            try
            {
                found = PlanRoad(s, g);
            }
            catch (Exception)
            {
                found = null;
            }
            cache[key] = found;
            return found;
        };
        foreach (var chain in chains)
        {
            double leadGap = from.Distance(chain.Start), tailGap = chain.End.Distance(goal);
            bool leadFixed = leadGap <= 140, tailFixed = tailGap <= 140;
            if (!leadFixed && !tailFixed)
                continue;
            double cost = 0.85 * chain.Length;
            if (cost + leadGap + tailGap >= bestCost)
                continue;
            List<TransitStep> lead = null, tail = null;
            if (leadFixed)
                cost += leadGap;
            else
            {
                lead = plan(from, chain.Start);
                if (lead == null)
                    continue;
                cost += PlanLength(from, lead) + 3 * PlanEnd(from, lead).Distance(chain.Start);
            }
            if (tailFixed)
                cost += tailGap;
            else
            {
                tail = plan(chain.End, goal);
                if (tail == null)
                    continue;
                cost += PlanLength(chain.End, tail) + 3 * PlanEnd(chain.End, tail).Distance(goal);
            }
            if (cost < bestCost)
            {
                bestCost = cost;
                best = chain;
                bestLead = lead;
                bestTail = tail;
            }
        }
        if (best == null)
            return meshPlan;
        var result = new List<TransitStep>();
        if (bestLead != null)
            result.AddRange(bestLead);
        var last = bestLead == null ? from : PlanEnd(from, bestLead);
        foreach (var p in best.Points)
        {
            if (last.Distance(p) < 6)
                continue;
            result.Add(new TransitStep { CellId = -1, To = new TransitGate { Code = "Öğrenilmiş yol", X = p.X, Y = p.Y,
                Height = p.Height } });
            last = p;
        }
        if (bestTail != null)
            result.AddRange(bestTail);
        Log("Öğrenilmiş yol seçildi · yaklaşık " + (int)bestCost + " birim (harita rotası " + (int)meshCost + ")");
        return result;
    }

    bool allBusy;
    static readonly int[] TownGates = { 20, 25, 5, 2, 1, 252 };

    // Plans the road route from the closest town (teleport hops included) to every dungeon entrance, in the background.

    void ComputeAllRoutes()
    {
        if (allBusy)
            return;
        allBusy = true;
        var targets = V3Data.Dungeons.GroupBy(d => d.Code).Select(g => g.First()).OrderBy(d => d.Code).ToList();
        System.Threading.ThreadPool.QueueUserWorkItem(delegate
        {
            var routes = new List<MapLeg>();
            foreach (var d in targets)
            {
                try
                {
                    var goal = d.Entrance();
                    float ground;
                    if (ExpeditionRules.TryGround(goal, out ground))
                        goal.Height = ground;
                    MapLeg best = null;
                    double bestCost = double.MaxValue;
                    foreach (int id in TownGates)
                    {
                        var gate = ExpeditionRules.Network.Gates.FirstOrDefault(x => x.Id == id);
                        if (gate == null)
                            continue;
                        try
                        {
                            var start = gate.Point();
                            var leg = new MapLeg { Code = d.Code, Label = d.Code };
                            leg.Points.Add(new PointF((float)start.X, (float)start.Y));
                            double cost = 0, px = start.X, py = start.Y;
                            foreach (var step in ExpeditionRules.Plan(start, goal))
                            {
                                if (step.From != null)
                                {
                                    var f = step.From.Point();
                                    cost += Math.Sqrt((f.X - px) * (f.X - px) + (f.Y - py) * (f.Y - py)) + RoadMesh.HopCost;
                                    px = f.X;
                                    py = f.Y;
                                    leg.Points.Add(new PointF((float)f.X, (float)f.Y));
                                }
                                var q = step.To.Point();
                                if (step.From == null)
                                    cost += Math.Sqrt((q.X - px) * (q.X - px) + (q.Y - py) * (q.Y - py));
                                px = q.X;
                                py = q.Y;
                                leg.Points.Add(new PointF((float)q.X, (float)q.Y));
                            }
                            leg.Points.Add(new PointF((float)goal.X, (float)goal.Y));
                            if (cost < bestCost)
                            {
                                bestCost = cost;
                                best = leg;
                            }
                        }
                        catch (Exception) { }
                    }
                    if (best != null)
                        routes.Add(best);
                }
                catch (Exception) { }
            }
            try
            {
                BeginInvoke(new Action(delegate
                {
                    allBusy = false;
                    worldMap.AllRoutes = routes;
                    worldMap.Invalidate();
                }));
            }
            catch (Exception) { }
        });
    }

    string routeKey = "";
    bool routeBusy;

    // Plans every hop of the queue in the background (you -> 1, exit of 1 -> 2, ...) and hands the finished lines to the map.

    internal void RefreshRoutes()
    {
        if (worldTab == null || routeBusy)
            return;
        var order = QueueOrder();
        if (order.Count == 0)
        {
            var sel = SelectedDungeon();
            if (sel != null)
                order.Add(sel.Code);
        }
        Position from = null;
        var player = snapshot == null ? null : snapshot.Player;
        if (player != null && player.Outside)
            from = player.Copy();
        string key = string.Join(",", order) + (from == null ? "" : "@" + (int)(from.X / 300) + "," + (int)(from.Y / 300));
        if (key == routeKey)
            return;
        routeKey = key;
        if (order.Count == 0)
        {
            worldMap.Legs = null;
            worldMap.Invalidate();
            return;
        }
        var stops = new List<DungeonLocation>();
        foreach (string code in order)
        {
            var site = V3Data.Dungeons.FirstOrDefault(x => x.Code == code);
            if (site != null)
                stops.Add(site);
        }
        routeBusy = true;
        var start = from;
        System.Threading.ThreadPool.QueueUserWorkItem(delegate
        {
            var legs = new List<MapLeg>();
            try
            {
                Position previous = start;
                string previousName = "Sen";
                foreach (var stop in stops)
                {
                    var goal = stop.Entrance();
                    float ground;
                    if (ExpeditionRules.TryGround(goal, out ground))
                        goal.Height = ground;
                    if (previous != null)
                    {
                        var leg = new MapLeg { Label = previousName + " → " + stop.Code };
                        leg.Points.Add(new PointF((float)previous.X, (float)previous.Y));
                        try
                        {
                            foreach (var step in ExpeditionRules.Plan(previous, goal))
                            {
                                if (step.From != null)
                                {
                                    var hopFrom = step.From.Point();
                                    leg.Points.Add(new PointF((float)hopFrom.X, (float)hopFrom.Y));
                                }
                                var to = step.To.Point();
                                leg.Points.Add(new PointF((float)to.X, (float)to.Y));
                            }
                        }
                        catch (Exception)
                        {
                            leg.Estimated = true;
                        }
                        leg.Points.Add(new PointF((float)goal.X, (float)goal.Y));
                        legs.Add(leg);
                    }
                    previous = goal;
                    previousName = stop.Code;
                }
            }
            catch (Exception) { }
            try
            {
                BeginInvoke(new Action(delegate
                {
                    routeBusy = false;
                    worldMap.Legs = legs;
                    worldMap.Invalidate();
                    RefreshRoutes();
                }));
            }
            catch (Exception) { }
        });
    }

    // Clicking a marker selects the grid row without moving the map under the cursor.

    void PickDungeonFromMap(string code)
    {
        foreach (DataGridViewRow row in dungeonGrid.Rows)
        {
            var location = row.Tag as DungeonLocation;
            if (location == null || location.Code != code)
                continue;
            pickingFromMap = true;
            try
            {
                dungeonGrid.CurrentCell = row.Cells[1];
            }
            finally
            {
                pickingFromMap = false;
            }
            return;
        }
    }
}

using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

sealed class DungeonRadar : Control
{
    DungeonLocation preview;
    FloorNavigation previewFloor;

    internal DungeonLocation Preview
    {
        get { return preview; }
        set
        {
            preview = value;
            previewFloor = value == null ? null : FloorNavigation.LoadCaptured(value);
        }
    }

    internal Snapshot Snapshot;
    internal GrimRun Run;

    internal DungeonRadar()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(21, 34, 52);
        ForeColor = Color.White;
        Dock = DockStyle.Fill;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.DrawString("CANLI İÇ HARİTA · oyun navigasyonu · kayıtlı / canlı zemin", Font, Brushes.White, 12, 10);
        if (Preview != null && (Snapshot == null || Run == null || Run.Location != Preview || !Run.IsRegion(Snapshot.Player)))
        {
            g.DrawString(Preview.Code + " · " + Preview.Name + " · " + Preview.Mode, Font, Brushes.Gold, 12, 38);
            if (previewFloor == null)
            {
                g.DrawString("Bu modun kayıtlı iç haritası yok; içeri girince canlı görünüm açılır.", Font, Brushes.LightSteelBlue,
                    12, 65);
                return;
            }
            var points = previewFloor.Cells.Values.Select(c => previewFloor.PositionOf(c)).ToList();
            double previewminX = points.Min(p => p.X), previewmaxX = points.Max(p => p.X), previewminY = points.Min(p =>
                p.Y), previewmaxY = points.Max(p => p.Y);
            double previewscale = Math.Min((Width - 50) / Math.Max(1, previewmaxX - previewminX + 16), (Height - 120) / Math.Max(1,
                previewmaxY - previewminY + 16));
            foreach (var p in points)
            {
                float size = (float)(previewFloor.Step * previewscale);
                g.FillRectangle(Brushes.SlateGray, 25 + (float)((p.X - previewminX) * previewscale), 85 + (float)((previewmaxY - p.Y) * previewscale),
                    size, size);
            }
            g.DrawString("Kayıtlı Solo Trial zemini · canlı mob bilgisi içeride gösterilir", Font, Brushes.LightSteelBlue,
                12, 60);
            return;
        }
        if (Snapshot == null || Run == null || (Preview ?? Run.Location) == null)
        {
            g.DrawString("Dungeon seçip bağlandıktan sonra konumlar burada görünür.", Font, Brushes.LightSteelBlue, 12,
                42);
            return;
        }
        var selected = Preview ?? Run.Location;
        if (!V3Data.IsCrypt(Snapshot.Player) || !Run.IsRegion(Snapshot.Player) || Run.Location != selected)
        {
            g.DrawString("Karakter seçilen dungeon iç bölgesinde değil.", Font, Brushes.LightSteelBlue, 12, 42);
            return;
        }
        var d = selected;
        double minX = ((d.InnerRegion & 255) - 135) * 192, minY = ((d.InnerRegion >> 8) - 92) * 192;
        double scale = Math.Max(.1, Math.Min((Width - 100) / 192.0, (Height - 95) / 384.0));
        float left = (float)((Width - 192 * scale) / 2), top = 48;
        Func<Position, PointF> pixel = p => new PointF(left + (float)((p.X - minX) * scale), top + (float)((minY + 384 - p.Y) * scale));
        using (var pen = new Pen(Color.FromArgb(52, 75, 103)))
        {
            for (int i = 0; i <= 6; i++)
                g.DrawLine(pen, left + (float)(i * 32 * scale), top, left + (float)(i * 32 * scale), top + (float)(384 * scale));
            for (int i = 0; i <= 12; i++)
                g.DrawLine(pen, left, top + (float)(i * 32 * scale), left + (float)(192 * scale), top + (float)(i * 32 * scale));
        }
        if (Run.Floor != null)
        {
            var floor = Run.Floor;
            foreach (var pair in floor.Cells)
            {
                var q = pixel(floor.PositionOf(pair.Value));
                float size = (float)(floor.Step * scale);
                g.FillRectangle(floor.Covered.Contains(pair.Key) ? Brushes.DarkSlateGray : Brushes.SlateGray, q.X - size / 2,
                    q.Y - size / 2, size, size);
            }
            g.DrawString((floor.Done ? "Zemin çıkarıldı" : "Zemin okunuyor") + " · " + floor.Cells.Count +
                " nokta · Taranan: " + floor.Covered.Count, Font, Brushes.LightSteelBlue, 12, 32);
        }
        foreach (var p in Run.Route.Skip(Run.Waypoint))
        {
            var q = pixel(p);
            g.FillEllipse(Brushes.LightGreen, q.X - 2, q.Y - 2, 4, 4);
        }
        foreach (var mob in Snapshot.Monsters.Where(m => m.Alive && Run.IsRegion(m.Position)))
        {
            var q = pixel(mob.Position);
            g.FillEllipse(mob.Uid == Snapshot.Selected ? Brushes.Gold : Brushes.OrangeRed, q.X - 4, q.Y - 4, 8, 8);
        }
        var player = pixel(Snapshot.Player);
        g.FillEllipse(Brushes.Cyan, player.X - 5, player.Y - 5, 10, 10);
        g.DrawString("Sen · " + Snapshot.Player.X.ToString("F1") + " / " + Snapshot.Player.Y.ToString("F1"), Font,
            Brushes.Cyan, player.X + 8, player.Y - 8);
    }
}

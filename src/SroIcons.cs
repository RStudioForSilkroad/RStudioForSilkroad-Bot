using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

// The page icons, drawn in code on a 16 x 16 grid in the spirit of the Silk Road: a camel for the caravan, a cash coin for the lottery,
// a paper lantern for notifications, a gate for the dungeons, a straw-hat traveller for the NPCs. No font and no image files are needed,
// so they stay sharp at any size and take the colour they are given.
static class SroIcons
{
    static readonly Dictionary<string, Action<Graphics, Pen, Brush>> shapes = new Dictionary<string, Action<Graphics, Pen, Brush>>();

    static SroIcons()
    {
        // what is new: a four-pointed star
        shapes["Yenilikler"] = (g, pen, ink) =>
        {
            g.FillPolygon(ink, new[] { new PointF(8, 0.8f), new PointF(9.7f, 6.3f), new PointF(15.2f, 8), new PointF(9.7f, 9.7f), new PointF(8, 15.2f),
                new PointF(6.3f, 9.7f), new PointF(0.8f, 8), new PointF(6.3f, 6.3f) });
        };
        // statistics: three bars
        shapes["İstatistik"] = (g, pen, ink) =>
        {
            g.FillRectangle(ink, 2f, 9f, 3f, 5f);
            g.FillRectangle(ink, 6.5f, 5f, 3f, 9f);
            g.FillRectangle(ink, 11f, 2f, 3f, 12f);
        };
        // protection: a shield
        shapes["Koruma"] = (g, pen, ink) =>
        {
            g.DrawPolygon(pen, new[] { new PointF(8, 1.5f), new PointF(14, 3.5f), new PointF(13, 9f), new PointF(8, 14.5f), new PointF(3, 9f), new PointF(2, 3.5f) });
            g.FillPolygon(ink, new[] { new PointF(8, 4f), new PointF(11.5f, 5.2f), new PointF(10.8f, 8.6f), new PointF(8, 11.6f) });
        };
        // inventory: a drawstring pouch
        shapes["Envanter"] = (g, pen, ink) =>
        {
            g.DrawEllipse(pen, 3f, 5.2f, 10f, 9.3f);
            g.FillRectangle(ink, 5.5f, 1.8f, 5f, 2.6f);
            g.DrawLine(pen, 5f, 5.4f, 11f, 5.4f);
        };
        // notifications: a paper lantern
        shapes["Bildirimler"] = (g, pen, ink) =>
        {
            g.DrawEllipse(pen, 3f, 4f, 10f, 8f);
            g.DrawLine(pen, 8f, 4f, 8f, 12f);
            g.FillRectangle(ink, 6f, 2f, 4f, 2f);
            g.FillRectangle(ink, 6f, 12f, 4f, 1.6f);
            g.DrawLine(pen, 8f, 13.6f, 8f, 15.5f);
        };
        // hunting ground: a crosshair
        shapes["Av Alanı"] = (g, pen, ink) =>
        {
            g.DrawEllipse(pen, 3f, 3f, 10f, 10f);
            g.DrawLine(pen, 8f, 0.5f, 8f, 5f);
            g.DrawLine(pen, 8f, 11f, 8f, 15.5f);
            g.DrawLine(pen, 0.5f, 8f, 5f, 8f);
            g.DrawLine(pen, 11f, 8f, 15.5f, 8f);
            g.FillEllipse(ink, 7f, 7f, 2f, 2f);
        };
        // attack: a straight sword
        shapes["Saldırı"] = (g, pen, ink) =>
        {
            using (var blade = new Pen(pen.Color, 1.9f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(blade, 13.2f, 2.8f, 6f, 10f);
            g.DrawLine(pen, 4.5f, 7.5f, 8.5f, 11.5f);
            g.DrawLine(pen, 5.5f, 10.5f, 3.2f, 12.8f);
            g.FillEllipse(ink, 1.8f, 11.8f, 2.4f, 2.4f);
        };
        // Grim Crypt: a gate with two cross beams
        shapes["Grim Crypt"] = (g, pen, ink) =>
        {
            g.FillRectangle(ink, 1.2f, 2.6f, 13.6f, 1.9f);
            g.FillRectangle(ink, 3f, 6f, 10f, 1.3f);
            g.FillRectangle(ink, 3.8f, 6f, 1.7f, 9f);
            g.FillRectangle(ink, 10.5f, 6f, 1.7f, 9f);
        };
        // Explorer: a compass
        shapes["Explorer Yerleri"] = (g, pen, ink) =>
        {
            g.DrawEllipse(pen, 1.6f, 1.6f, 12.8f, 12.8f);
            g.FillPolygon(ink, new[] { new PointF(8, 3.4f), new PointF(10.2f, 8f), new PointF(5.8f, 8f) });
            g.DrawPolygon(pen, new[] { new PointF(8, 12.6f), new PointF(10.2f, 8f), new PointF(5.8f, 8f) });
        };
        // caravan: a two-humped camel
        shapes["Kervan Rotası"] = (g, pen, ink) =>
        {
            g.FillEllipse(ink, 2.4f, 6.2f, 8.8f, 4.4f);
            g.FillEllipse(ink, 3.8f, 4.2f, 3.2f, 3.6f);
            g.FillEllipse(ink, 7.2f, 4.2f, 3.2f, 3.6f);
            using (var neck = new Pen(pen.Color, 2.1f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(neck, 10.4f, 7.4f, 12.8f, 3.8f);
            g.FillEllipse(ink, 11.6f, 2f, 3.6f, 2.3f);
            using (var leg = new Pen(pen.Color, 1.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(leg, 4f, 9.6f, 4f, 14.2f);
                g.DrawLine(leg, 6f, 9.8f, 6f, 14.2f);
                g.DrawLine(leg, 8.6f, 9.8f, 8.6f, 14.2f);
                g.DrawLine(leg, 10.4f, 9.6f, 10.4f, 14.2f);
            }
        };
        // NPC: a traveller in a conical straw hat
        shapes["Nanzhang NPC"] = (g, pen, ink) =>
        {
            g.FillPolygon(ink, new[] { new PointF(8, 0.8f), new PointF(13.4f, 5.2f), new PointF(2.6f, 5.2f) });
            g.FillEllipse(ink, 5.8f, 6f, 4.4f, 4.2f);
            g.DrawArc(pen, 2.6f, 11f, 10.8f, 9f, 180, 180);
        };
        // quest: a scroll
        shapes["Quest"] = (g, pen, ink) =>
        {
            g.DrawRectangle(pen, 4f, 3f, 8f, 10f);
            g.FillEllipse(ink, 2.4f, 1.4f, 11.2f, 3.2f);
            g.FillEllipse(ink, 2.4f, 11.4f, 11.2f, 3.2f);
            g.DrawLine(pen, 6.2f, 6.6f, 9.8f, 6.6f);
            g.DrawLine(pen, 6.2f, 9f, 9.8f, 9f);
        };
        // lottery: a cash coin with a square hole
        shapes["Lottery"] = (g, pen, ink) =>
        {
            g.DrawEllipse(pen, 1.6f, 1.6f, 12.8f, 12.8f);
            g.DrawRectangle(pen, 5.6f, 5.6f, 4.8f, 4.8f);
        };
        // two accounts: two travellers side by side
        shapes["Çift Hesap · Deneme"] = (g, pen, ink) =>
        {
            g.FillEllipse(ink, 2.6f, 2.4f, 4f, 4f);
            g.FillEllipse(ink, 9.4f, 2.4f, 4f, 4f);
            g.DrawArc(pen, 0.8f, 8f, 7.6f, 8f, 180, 180);
            g.DrawArc(pen, 7.6f, 8f, 7.6f, 8f, 180, 180);
        };
        // update / manual: an arrow into a tray
        shapes["Güncelleme/Kılavuz"] = (g, pen, ink) =>
        {
            g.DrawLine(pen, 8f, 1.8f, 8f, 10f);
            g.DrawLine(pen, 4.6f, 6.8f, 8f, 10.2f);
            g.DrawLine(pen, 11.4f, 6.8f, 8f, 10.2f);
            g.DrawLine(pen, 2.4f, 11f, 2.4f, 14f);
            g.DrawLine(pen, 2.4f, 14f, 13.6f, 14f);
            g.DrawLine(pen, 13.6f, 14f, 13.6f, 11f);
        };
        // log: a ruled page
        shapes["Log"] = (g, pen, ink) =>
        {
            g.DrawRectangle(pen, 3f, 1.6f, 10f, 12.8f);
            g.DrawLine(pen, 5.6f, 5f, 10.4f, 5f);
            g.DrawLine(pen, 5.6f, 8f, 10.4f, 8f);
            g.DrawLine(pen, 5.6f, 11f, 9f, 11f);
        };
        // the kinds of a skill line (Skill page): a sword, a shield, a cross, a drop of mana, a bolt, two travellers
        shapes["skill.attack"] = shapes["Saldırı"];
        shapes["skill.buff"] = shapes["Koruma"];
        shapes["skill.heal"] = (g, pen, ink) =>
        {
            g.FillRectangle(ink, 6.4f, 2.4f, 3.2f, 11.2f);
            g.FillRectangle(ink, 2.4f, 6.4f, 11.2f, 3.2f);
        };
        shapes["skill.mana"] = (g, pen, ink) =>
        {
            g.FillPolygon(ink, new[] { new PointF(8, 1.4f), new PointF(12.4f, 8f), new PointF(3.6f, 8f) });
            g.FillEllipse(ink, 3f, 6.2f, 10f, 8.4f);
        };
        shapes["skill.emergency"] = (g, pen, ink) =>
        {
            g.FillPolygon(ink, new[] { new PointF(9.6f, 1.2f), new PointF(3.4f, 9f), new PointF(7.4f, 9f), new PointF(6f, 14.8f), new PointF(12.8f, 6.6f), new PointF(8.6f, 6.6f) });
        };
        shapes["skill.party"] = shapes["Çift Hesap · Deneme"];
        shapes["camel"] = shapes["Kervan Rotası"];
    }

    internal static bool Has(string key)
    {
        return key != null && shapes.ContainsKey(key);
    }

    // Draws the icon of a page (by its name) into r in the colour ink; an unknown name draws nothing.
    internal static void Draw(Graphics g, string key, Rectangle r, Color ink)
    {
        Action<Graphics, Pen, Brush> shape;
        if (key == null || !shapes.TryGetValue(key, out shape))
            return;
        var state = g.Save();
        try
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(r.X, r.Y);
            g.ScaleTransform(r.Width / 16f, r.Height / 16f);
            using (var pen = new Pen(ink, 1.3f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var brush = new SolidBrush(ink))
                shape(g, pen, brush);
        }
        finally
        {
            g.Restore(state);
        }
    }
}

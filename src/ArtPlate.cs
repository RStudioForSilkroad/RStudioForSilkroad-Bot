using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

// The small picture under the page list: the golden dragon's fiery breath over the gate of Nanzhang from the game's launcher, framed like the page list above it.
sealed class ArtPlate : Control
{
    internal ArtPlate()
    {
        Height = 104;
        TabStop = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ui.Stone);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var frame = new Rectangle(0, 8, Width - 1, Height - 9);
        using (var fill = new SolidBrush(Ui.Card))
            g.FillRectangle(fill, frame);
        using (var border = new Pen(Ui.Bronze))
            g.DrawRectangle(border, frame);
        var inside = Rectangle.Inflate(frame, -3, -3);
        var picture = GameArt.Plate;
        if (picture != null)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(picture, inside, 0, 0, picture.Width, picture.Height, GraphicsUnit.Pixel);
        }
    }
}

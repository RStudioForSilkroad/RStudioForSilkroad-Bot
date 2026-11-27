using System;
using System.Drawing;
using System.Drawing.Imaging;

// What the game window shows of an NPC dialog. The client sometimes opens the dialog window empty (the first request of a talk right after the
// selection); a second request fills it. The bot cannot read that from the game's memory, so it looks at the picture: a dialog is a large, very
// dark panel; with text in it there are light pixels, without text there are none. Nothing else on the screen is that dark and that large.
enum DialogState
{
    Unknown,    // no picture to look at
    None,       // no dialog window on the screen
    Blank,      // a dialog window without text
    Filled      // a dialog window with text
}

static class DialogProbe
{
    const int Cell = 4;                 // pixels per sampled cell
    const int WindowCells = 56;         // the dark panel searched for is 224 x 224 pixels at least (a dialog's text area is about 330 x 380)
    const double DarkShare = 0.88;      // share of dark cells in that panel
    const int DarkMax = 52;             // a pixel this dark (highest colour channel) belongs to the panel
    const int LightMin = 170;           // a pixel this light (lowest colour channel) belongs to text
    const int LitPixelsPerRow = 10;     // a pixel row with this many light pixels is a row of text
    const int TextRows = 24;            // text rows that make a panel "with text": a dialog has dozens of them; an empty one only the title (about 12)

    // The text rows counted in the best panel of the last reading (for the log).
    internal static int LastLight;
    internal static Rectangle LastWindow;

    internal static DialogState Read(Bitmap picture)
    {
        LastLight = 0;
        if (picture == null || picture.Width < 400 || picture.Height < 300)
            return DialogState.Unknown;
        int width = picture.Width, height = picture.Height;
        var data = picture.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[Math.Abs(data.Stride) * height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            int stride = data.Stride;
            int cols = width / Cell, rows = height / Cell;
            if (cols < WindowCells || rows < WindowCells)
                return DialogState.Unknown;
            // integral image of the dark cells (a cell is dark when its middle pixel is)
            var sum = new int[(rows + 1) * (cols + 1)];
            var dark = new bool[rows * cols];
            for (int r = 0; r < rows; r++)
            {
                int line = 0;
                for (int c = 0; c < cols; c++)
                {
                    int i = (r * Cell + Cell / 2) * stride + (c * Cell + Cell / 2) * 4;
                    if (Math.Max(bytes[i], Math.Max(bytes[i + 1], bytes[i + 2])) <= DarkMax)
                    {
                        line++;
                        dark[r * cols + c] = true;
                    }
                    sum[(r + 1) * (cols + 1) + c + 1] = sum[r * (cols + 1) + c + 1] + line;
                }
            }
            // a light pixel is text only when it lies in the dark panel (most of the cells around it are dark): a bright patch of scenery at the
            // panel's edge, or the panel's own light frame, is not
            var inPanel = new bool[rows * cols];
            for (int r = 1; r < rows - 1; r++)
                for (int c = 1; c < cols - 1; c++)
                {
                    int around = 0;
                    for (int dr = -1; dr <= 1; dr++)
                        for (int dc = -1; dc <= 1; dc++)
                            if (dark[(r + dr) * cols + c + dc])
                                around++;
                    inPanel[r * cols + c] = around >= 6;
                }
            // a picture that is black nearly everywhere is a hidden or sleeping window, not a screen without a dialog
            if (sum[rows * (cols + 1) + cols] > rows * cols * 0.97)
                return DialogState.Unknown;
            bool found = false;
            int best = 0;
            int need = (int)(WindowCells * WindowCells * DarkShare);
            for (int r = 0; r + WindowCells <= rows; r += 2)
                for (int c = 0; c + WindowCells <= cols; c += 2)
                {
                    int darkCells = sum[(r + WindowCells) * (cols + 1) + c + WindowCells] - sum[r * (cols + 1) + c + WindowCells] -
                        sum[(r + WindowCells) * (cols + 1) + c] + sum[r * (cols + 1) + c];
                    if (darkCells < need)
                        continue;
                    found = true;
                    int litRows = 0;
                    for (int y = r * Cell; y < (r + WindowCells) * Cell; y++)
                    {
                        int light = 0;
                        for (int x = c * Cell; x < (c + WindowCells) * Cell; x++)
                        {
                            int i = y * stride + x * 4;
                            if (Math.Min(bytes[i], Math.Min(bytes[i + 1], bytes[i + 2])) >= LightMin && inPanel[(y / Cell) * cols + x / Cell])
                                light++;
                        }
                        if (light >= LitPixelsPerRow)
                            litRows++;
                    }
                    if (litRows > best)
                        LastWindow = new Rectangle(c * Cell, r * Cell, WindowCells * Cell, WindowCells * Cell);
                    best = Math.Max(best, litRows);
                }
            LastLight = best;
            return !found ? DialogState.None : best >= TextRows ? DialogState.Filled : DialogState.Blank;
        }
        finally
        {
            picture.UnlockBits(data);
        }
    }

    // The state of the dialog on the game window right now.
    internal static DialogState Read(IntPtr window)
    {
        if (window == IntPtr.Zero)
            return DialogState.Unknown;
        using (var picture = ClientPicture.Grab(window))
            return Read(picture);
    }
}

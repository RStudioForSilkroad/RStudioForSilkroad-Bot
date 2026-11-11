using System;
using System.Drawing;
using System.Windows.Forms;

// A small question dialog in the application's own style: title, message and two buttons.
// The safe answer ("no") is the default button and what Esc picks.
sealed class ConfirmDialog : Form
{
    ConfirmDialog(string title, string message, string yes, string no, bool dangerous)
    {
        Text = AppPaths.AppName;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(440, 190);
        BackColor = Ui.Surface;
        ForeColor = Ui.Text;
        Font = Ui.Body;
        var heading = new Label { Text = title, Font = Ui.Heading, Left = 24, Top = 22, Width = 392, Height = 28 };
        var body = new Label { Text = message, Left = 24, Top = 56, Width = 392, Height = 60, ForeColor = Ui.TextSecondary };
        var bar = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Ui.SurfaceAlt };
        var confirm = new Button { Text = yes, Size = new Size(130, 36), DialogResult = DialogResult.Yes };
        var decline = new Button { Text = no, Size = new Size(130, 36), DialogResult = DialogResult.No };
        confirm.Location = new Point(ClientSize.Width - 24 - confirm.Width, 14);
        decline.Location = new Point(confirm.Left - 10 - decline.Width, 14);
        Fancy.Kind(confirm, dangerous ? ButtonKind.Danger : ButtonKind.Primary);
        Fancy.Kind(decline, ButtonKind.Secondary);
        bar.Controls.Add(confirm);
        bar.Controls.Add(decline);
        bar.Paint += delegate(object sender, PaintEventArgs e)
        {
            using (var line = new Pen(Ui.Border))
                e.Graphics.DrawLine(line, 0, 0, bar.Width, 0);
        };
        Controls.Add(heading);
        Controls.Add(body);
        Controls.Add(bar);
        AcceptButton = decline;
        CancelButton = decline;
    }

    // True when the user chose the first button.
    internal static bool Ask(IWin32Window owner, string title, string message, string yes, string no, bool dangerous)
    {
        using (var dialog = new ConfirmDialog(title, message, yes, no, dangerous))
            return dialog.ShowDialog(owner) == DialogResult.Yes;
    }
}

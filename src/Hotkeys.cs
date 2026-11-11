using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

static class HotkeyPolicy
{
    internal static bool Allowed(int key, int mods)
    {
        if ((mods & ~7) != 0)
            return false;
        var k = (Keys)key;
        if (k >= Keys.F1 && k <= Keys.F24)
            return false;
        return (k >= Keys.A && k <= Keys.Z) || (k >= Keys.D0 && k <= Keys.D9) || (k >= Keys.NumPad0 && k <= Keys.NumPad9) ||
            new[] { Keys.Insert, Keys.Delete, Keys.Home, Keys.End, Keys.PageUp, Keys.PageDown, Keys.Pause, Keys.Scroll,
            Keys.Space, Keys.Tab }.Contains(k);
    }

    internal static string Name(int key, int mods)
    {
        if (key == 0)
            return "Atanmadı";
        Keys k = (Keys)key;
        if ((mods & 1) != 0)
            k |= Keys.Alt;
        if ((mods & 2) != 0)
            k |= Keys.Control;
        if ((mods & 4) != 0)
            k |= Keys.Shift;
        return new KeysConverter().ConvertToString(k);
    }

    internal static bool Conflicts(int key, int mods, IEnumerable<ushort> attacks, bool berserk)
    {
        return key != 0 && mods == 0 && (attacks.Any(k => k == key) || (berserk && key == (int)Keys.Tab));
    }
}

sealed class HotkeyDialog : Form
{
    internal int SelectedKey, SelectedMods;
    readonly Label value = new Label(), hint = new Label();

    internal HotkeyDialog(int key, int mods)
    {
        SelectedKey = key;
        SelectedMods = mods;
        Text = "Başlat / durdur tuşu";
        ClientSize = new Size(450, 180);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;
        Font = new Font("Segoe UI", 10);
        value.SetBounds(18, 16, 414, 45);
        value.Text = HotkeyPolicy.Name(key, mods);
        value.Font = new Font("Segoe UI", 18, FontStyle.Bold);
        hint.SetBounds(18, 65, 414, 42);
        hint.Text = "İstediğin tuşa veya Ctrl / Alt / Shift ile birleşimine bas.";
        Controls.AddRange(new Control[] { value, hint });
        var ok = new Button { Text = "Ata", DialogResult = DialogResult.OK, Bounds = new Rectangle(18, 124, 125, 34) };
        var clear = new Button { Text = "Kaldır", Bounds = new Rectangle(158, 124, 125, 34) };
        clear.Click += delegate
        {
            SelectedKey = SelectedMods = 0;
            value.Text = "Atanmadı";
        };
        var cancel = new Button { Text = "İptal", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(298, 124,
            125, 34) };
        Controls.AddRange(new Control[] { ok, clear, cancel });
        CancelButton = cancel;
        KeyDown += CaptureKey;
    }

    void CaptureKey(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
            return;
        e.SuppressKeyPress = true;
        e.Handled = true;
        int mods = (e.Alt ? 1 : 0) | (e.Control ? 2 : 0) | (e.Shift ? 4 : 0);
        if (!HotkeyPolicy.Allowed((int)e.KeyCode, mods))
        {
            hint.Text = "Harf, sayı, Insert / Home gibi bir tuş seç. F tuşları kullanılmaz.";
            return;
        }
        SelectedKey = (int)e.KeyCode;
        SelectedMods = mods;
        value.Text = HotkeyPolicy.Name(SelectedKey, SelectedMods);
        hint.Text = "Kaydetmek için Ata düğmesine tıkla.";
    }
}

sealed partial class MemorySelectV3Form : Form
{
    readonly Label shortcutLabel = new Label();
    int shortcutKey, shortcutMods;
    bool capturingHotkey;
    void RestoreHotkey()
    {
        shortcutKey = settings.HotkeyKey;
        shortcutMods = settings.HotkeyModifiers;
        if (shortcutKey != 0 && !HotkeyPolicy.Allowed(shortcutKey, shortcutMods))
            shortcutKey = shortcutMods = 0;
        shortcutLabel.Text = HotkeyPolicy.Name(shortcutKey, shortcutMods);
    }

    void ChooseHotkey()
    {
        Native.UnregisterHotKey(Handle, 1);
        capturingHotkey = true;
        try
        {
            using (var dialog = new HotkeyDialog(shortcutKey, shortcutMods))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    if (dialog.SelectedKey != 0 && !HotkeyPolicy.Allowed(dialog.SelectedKey, dialog.SelectedMods))
                        throw new InvalidOperationException("Bu kısayol desteklenmiyor.");
                    var keysNow = keys.Text.ToUpperInvariant().Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                        .Where(t => t.Length == 1).Select(t => (ushort)t[0]);
                    if (HotkeyPolicy.Conflicts(dialog.SelectedKey, dialog.SelectedMods, keysNow, autoZerk.Checked))
                        throw new InvalidOperationException("Kısayol saldırı veya Berserk tuşuyla aynı olamaz.");
                    if (dialog.SelectedKey != 0 && !Native.RegisterHotKey(Handle, 1, (uint)(0x4000 | dialog.SelectedMods),
                        (uint)dialog.SelectedKey))
                        throw new InvalidOperationException("Bu kısayol başka bir program tarafından kullanılıyor.");
                    shortcutKey = dialog.SelectedKey;
                    shortcutMods = dialog.SelectedMods;
                    shortcutLabel.Text = HotkeyPolicy.Name(shortcutKey, shortcutMods);
                    SaveSettings();
                    status.Text = "Başlat / durdur kısayolu: " + shortcutLabel.Text;
                }
            }
        }
        finally
        {
            capturingHotkey = false;
            Native.UnregisterHotKey(Handle, 1);
            RegisterShortcut();
        }
    }

    void RegisterShortcut()
    {
        if (shortcutKey != 0 && !Native.RegisterHotKey(Handle, 1, (uint)(0x4000 | shortcutMods), (uint)shortcutKey))
            status.Text = "Seçtiğin kısayol kullanılamıyor; düğmeler kullanılabilir.";
    }

    void HandleShortcut()
    {
        if (capturingHotkey)
            return;
        if (active || grim.Armed)
        {
            Stop("Kısayol ile durduruldu.");
            return;
        }
        if (ContainsFocus && (ActiveControl is TextBoxBase || ActiveControl is NumericUpDown || ActiveControl is ComboBox))
            return;
        StartRequested();
    }
}

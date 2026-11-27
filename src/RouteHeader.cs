using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

// The heading above every page, in the manner of the MSRO BOT panel: the page's name large in cream-gold, one muted line under it that says what
// the page is for, a thin bronze rule beneath, and at the right end the character and connection state. Every page inherits it from the shell.
sealed class RouteHeader : Control
{
    string title = "", info = "";
    bool connected;

    // What each page is for, one line (the text under the heading).
    static readonly Dictionary<string, string> descriptions = new Dictionary<string, string>
    {
        { "Yenilikler", "Bu sürümde neler değişti; önceki sürümlerin notları da burada." },
        { "İstatistik", "Karakterin ve yakındaki yaratıkların canlı durumu." },
        { "Koruma", "İksir, bakım, ölüm ve yolculuk güvenlik ayarları." },
        { "Av Alanı", "Av bölgesini kaydet, saldırılacak yaratıkları ve tuşları belirle." },
        { "Saldırı", "Seçili skiller numara sırasıyla kullanılır. Sırayı oklarla değiştir." },
        { "Grim Crypt", "Dungeon seç, girişe yürü; Solo Trial'ı otomatik avla." },
        { "Explorer Yerleri", "Haritadaki yerleri sırayla gez, görülenleri işaretle." },
        { "Kervan Rotası", "Şehirler arası kervan yolu ve ticaret rotalarını çalıştır." },
        { "Envanter", "Çantadaki eşyaları sat, depola ve düzenle." },
        { "Bildirimler", "İş bitince ve önemli olaylarda nasıl haber alacağını seç." },
        { "Nanzhang NPC", "Nanzhang'daki NPC'leri haritada ara ve konumlarını gör." },
        { "Quest", "Görevleri, verenleri ve ödüllerini listele." },
        { "Lottery", "Lottery biletlerini sırayla kullan ve sonuçları izle." },
        { "Çift Hesap · Deneme", "İkinci hesapla eşli çalışma denemesi." },
        { "Güncelleme/Kılavuz", "Güncellemeleri denetle, kılavuzu aç." },
        { "Log", "Botun olay kayıtları; en yenisi altta." }
    };

    internal static string Describe(string page)
    {
        string text;
        return page != null && descriptions.TryGetValue(page, out text) ? text : "";
    }

    internal RouteHeader()
    {
        Dock = DockStyle.Top;
        Height = 66;
        TabStop = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    internal void SetTitle(string text)
    {
        if (text == title)
            return;
        title = text ?? "";
        Invalidate();
    }

    // info is the line at the right end (character, connection); connected only changes the colour of its dot.
    internal void SetState(string text, bool isConnected)
    {
        if (text == info && isConnected == connected)
            return;
        info = text ?? "";
        connected = isConnected;
        Invalidate();
    }

    const TextFormatFlags Flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix |
        TextFormatFlags.SingleLine;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Ui.PaintStone(g, new Rectangle(0, 0, Width, Height), Ui.Stone);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int right = Width - 12, infoWidth = 0;
        if (info.Length > 0)
        {
            infoWidth = Math.Min(Math.Max(120, Width / 2), TextRenderer.MeasureText(g, info, Ui.Small, new Size(2000, 40), Flags).Width + 22);
            using (var dot = new SolidBrush(connected ? Ui.Jade : Ui.TextDisabled))
                g.FillEllipse(dot, right - infoWidth, 14, 7, 7);
            TextRenderer.DrawText(g, info, Ui.Small, new Rectangle(right - infoWidth + 14, 8, infoWidth - 14, 20), Ui.TextSecondary, Flags | TextFormatFlags.EndEllipsis);
        }
        int textWidth = Math.Max(60, Width - 24 - infoWidth);
        TextRenderer.DrawText(g, title, Ui.PageTitle, new Rectangle(4, 4, textWidth, 30), Ui.Gold, Flags | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, Describe(title), Ui.Body, new Rectangle(4, 35, Width - 12, 20), Ui.InkMuted, Flags | TextFormatFlags.EndEllipsis);
        using (var rule = new Pen(Ui.Bronze))
            g.DrawLine(rule, 0, Height - 3, Width, Height - 3);
    }
}

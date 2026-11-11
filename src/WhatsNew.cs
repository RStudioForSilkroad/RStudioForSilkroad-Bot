using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

// "Yenilikler" window shown once after the program was updated. The notes of every version are kept in the program itself.
// Rule: every change that reaches users is added to the newest entry below (and a new entry is started for a new version number).

static class ReleaseNotes
{
    internal sealed class Entry
    {
        internal string Version;
        internal string Title;
        internal string[] Items;
    }

    // Newest first.
    internal static readonly Entry[] All = { new Entry { Version = "3.7.0.0", Title = "Kervan rotası ve sadeleşen Grim Crypt",
        Items = new[] { "Yeni Kervan Rotası sayfası: Hotan - Jangan, Jangan - Hotan, Hotan - Donwhang ve Donwhang - Hotan düğmeleri. Düğme scripti yazar ve karakteri hedef tüccar NPC’ye yürütür.",
        "Kervan rotası yalnızca yoldan hesaplanır: teleport, gemi, Return Scroll ve binek kullanılmaz (ticaret malı ışınlanamaz). Script dosyaları Script klasörüne walk,X,Y,Z satırlarıyla yazılır.",
        "Tüccar NPC konumları programla gelmez: her şehirde NPC’ye oyunda tıkla, Seçili NPC’yi kaydet de; konum bir kez öğrenilir.",
        "Grim Crypt sayfası sadeleşti: modlar rozet düğmeleri, tablo daha az sütunlu ve başlıklı, yinelenen Durdur düğmesi kalktı.",
        "Etkinlik günlüğü kartı kaldırıldı (kayıt events.log dosyasında tutulmaya devam eder).",
        "Pencereyi kapatırken “Kapatmak istiyor musunuz?” sorusu çıkar; güncelleme sırasında sorulmaz.",
        "Yeni: yakında unique canavar çıkınca sesli uyarı (Av / Ayarlar → Otomatik bakım)." } },
        new Entry { Version = "3.6.0.0", Title = "Yeni beyaz arayüz",
        Items = new[] { "Tamamen yenilenmiş beyaz tasarım: açık gri zemin, beyaz kartlar, okunaklı yazı tipleri ve her yerde aynı düğme, tablo ve giriş kutusu stili.",
        "Soldaki menü simgeli ve iki gruba ayrıldı (Çalışma / Araçlar); üst çubukta bağlantı durumu noktası, altta renkli Başlat / Durdur düğmeleri.",
        "Av / Ayarlar sayfası geniş ekranda iki sütun, dar pencerede tek sütun olur; hiçbir kontrol kesilmez. Canlı durum tablosu tam genişliktedir.",
        "Karakter durumu paneli yeniden çizildi: HP / MP / EXP / SP çubukları, seviye rozeti ve oturum hızları açık zeminde daha okunaklı.",
        "Vurgu rengi seçilebilir (Mavi, Zümrüt, Mor, Turuncu, Gül, Turkuaz, Grafit); tüm renkler beyaz yazıyla en az 4,5:1 kontrast taşır.",
        "Klavyeyle gezinme: odaklı düğmede halka, sekme şeridinde ok tuşları. Küçük pencerelerde etkinlik günlüğü kendiliğinden katlanır.",
        "Tasarım kuralları docs/design-system.md içinde; testler üç pencere boyutunda kesilen kontrol arar." } },
        new Entry { Version = "3.5.0.0", Title = "Silkroad R Studio",
        Items = new[] { "Program adı Silkroad R Studio oldu; pencere başlığı, üst çubuk ve dosya adı (SilkroadRStudio.exe) buna göre değişti.",
        "Güncellemeler artık bu projenin kendi GitHub sürümlerinden gelir ve yeni bir yayıncı anahtarıyla imzalanır. Eski New Era Memory Select sürümleri bu zincire güncellenemez; bir kez elle indirmek gerekir.",
        "Ayarlar %LOCALAPPDATA%\\SilkroadRStudio klasörüne taşındı; eski klasördeki ayarlar ilk açılışta otomatik kopyalanır." } },
        new Entry { Version = "3.4.6.0", Title = "Pencere başlığında karakter adı",
        Items = new[] { "Pencere başlığı karakter adıyla başlar (örn. Rikimaru - New Era · Memory Select ...); iki hesap açıkken görev çubuğunda hangisinin hangisi olduğu görünür." } },
        new Entry { Version = "3.4.5.0", Title = "Tüm dünya için yol haritası ve oda oda tarama", Items = new[] { "Tüm dünya yol ağı: duvar, çit ve yapı sınırları ile merdiven/köprü yüzeyleri haritaya işlendi; rota duvara çarpmadan en kısa yoldan hesaplanır.",
        "18 dungeonun hepsi için rota girişin kendisinde biter (son boşluk 5 birimin altında); Hotan sarayı (D12) şehir içinden, D01–D05 Hotan'dan doğrudan kuzeye yürür.",
        "Yürüyüş yolu çok uzunsa ve çantada Return Scroll varsa bot önce şehre döner, oradan ışınlanma/yürüyüşle devam eder. Planlamada Nanzhang'a ışınlanma yok.",
        "Dünya haritası: oyunun kendi haritaları, şehir listesi + Merkezle, tüm dungeon rotalarını renkli gösterme ve sıradaki dungeonların rotaları.",
        "Öğrettiğin yollar (Yolları kopyala/yapıştır ile paylaşılır) bot rotasıyla karşılaştırılır; hangisi kısaysa o seçilir. Hedef ve öğrenilen yol sağ tıkla silinir.",
        "\"Hedef doğrulanınca tuşlara bas\" seçeneği artık her açılışta işaretli.",
        "Grim odaları oda oda taranır: her oda ~22 birimlik karelerle gezilir, 3 yaratık ölünce sonraki odaya geçilir; oda bitmediyse baştan taranır.",
        "Karakterin 60 birim yakınındaki canlı yaratığa hangi odada olursa olsun hemen saldırılır; odaya atanamayan yaratıklar artık yok sayılmaz.",
        "Otomatik çıkış yalnızca dungeon sırası çalışırken yapılır; tek tek girdiğinde karakter Dungeon Exit önünde bekler.",
        "Yeni arayüz: yuvarlak köşeli, renk geçişli düğmeler (üzerine gelince ve basınca yumuşak geçiş), renk geçişli üst çubuk, başlıklarda kalın yazı ve üst çubuğun altında sürekli kayan ışık şeridi; seçtiğin temanın rengini izler.",
        "Sayfaların etrafındaki kalın beyaz çerçeve kaldırıldı; etkinlik günlüğü başlıklı, yuvarlak köşeli bir kartta, daha okunur yazıyla gösterilir.",
        "Tema seçimi sağ üst köşeye (Görünüm sekmesi kaldırıldı); karakter durumu panelinde karakter adı gösterilir.",
        "Üst çubukta \"Yenilikler\" düğmesi tam görünür (pencere genişliği düzeltildi).",
        "Yenilikler penceresi artık tüm sürümlerin değişikliklerini listeler; üstteki Yenilikler düğmesiyle istediğin zaman açılır." } },
        new Entry { Version = "3.4.4.0", Title = "Dungeon sırası ve karakter paneli", Items = new[] { "Dungeon çalıştırma sırası: dungeonları seçtiğin sırayla otomatik çalıştırır (Ekle/Çıkar, ▲▼, haritada Shift+tık).",
        "Her dungeon bitince Dungeon Exit NPC'si ile otomatik çıkış; sıra bitince istersen Return Scroll ile şehre dönüş.",
        "Dünya haritası: oyunun kendi harita karolarıyla birebir koordinat, yakınlaştırma ve sıra çizgisi.",
        "Karakter durumu paneli: HP (kırmızı), MP (mavi), EXP (yeşil), SP (sarı) canlı çubukları.",
        "Büyük seviye göstergesi, oturumda atlanan seviye sayısı, dakikalık ve saatlik EXP/SP kazancı, seviyeye kalan süre.",
        "Bu \"Yenilikler\" penceresi: güncellemeden sonra değişiklikleri bir kez gösterir." } }, new Entry { Version =
        "3.4.3.0", Title = "Dünya haritası", Items = new[] { "Grim sekmesine Dünya haritası paneli eklendi: 18 dungeon girişi, şehirler ve karakterin konumu.",
        "Gerçek harita resmi ve doğru koordinat eşlemesi; oyunun kendi minimap karolarından kesin koordinatlı harita.",
        "Haritada dungeon seçerken oluşan gecikme giderildi." } }, new Entry { Version = "3.4.2.0", Title =
        "Açık kaynak sürümü", Items = new[] { "Kaynak kodu GitHub'da yayınlandı (MIT lisansı).",
        "Klasör yapısı düzenlendi (src, data, assets, tests, tools, phbot); README birleştirildi." } }, new Entry { Version =
        "3.4.1.0", Title = "Küçük düzeltmeler", Items = new[] { "Kararlılık ve küçük düzeltmeler (bu sürüm için ayrıntılı kayıt tutulmadı)." } },
        new Entry { Version = "3.4.0.0", Title = "Temel sürüm", Items = new[] { "Grim Crypt otomasyonu: dungeon girişine yol ağıyla yürüme, Solo Trial girişi, mob avı ve bitişte çıkış.",
        "Karakter belleğini okuyan ana panel, Explorer yerleri, Nanzhang NPC, çift hesap ve phBot rota eklentisi sekmeleri.",
        "İmzalı otomatik güncelleme: yeni sürüm indirilir, imzası doğrulanır ve program kendini günceller." } }, };

    internal static string CurrentVersion
    {
        get { return Assembly.GetExecutingAssembly().GetName().Version.ToString(); }
    }

    internal static Entry Find(string version)
    {
        foreach (var e in All)
            if (e.Version == version)
                return e;
        return null;
    }
}

sealed class WhatsNewForm : Form
{
    internal bool DontShowAgain
    {
        get { return hide.Checked; }
    }

    readonly CheckBox hide = new CheckBox();

    internal WhatsNewForm(ReleaseNotes.Entry entry, string version, Icon icon, Color back, Color fore)
    {
        Text = "Yenilikler · " + version;
        if (icon != null)
            Icon = icon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(640, 600);
        BackColor = Ui.Surface;
        ForeColor = Ui.Text;
        Font = Ui.Body;

        var head = new Label { Text = "Güncellendi: sürüm " + version, Font = Ui.Title,
            ForeColor = Ui.Accent, Left = 18, Top = 14, Width = 600, Height = 32 };
        var notes = new RichTextBox { Left = 20, Top = 54, Width = 600, Height = 480, ReadOnly = true, BorderStyle =
            BorderStyle.None, BackColor = Ui.Surface, ForeColor = Ui.Text, DetectUrls = false, TabStop = false };
        var bold = Ui.Heading;
        var normal = Ui.Body;
        foreach (var e in ReleaseNotes.All)
        {
            notes.SelectionFont = bold;
            notes.SelectionColor = e.Version == version ? Ui.Accent : Ui.Text;
            notes.AppendText(e.Version + " · " + e.Title + (e.Version == version ? "  (bu sürüm)" : "") + "\n");
            notes.SelectionFont = normal;
            notes.SelectionColor = Ui.Text;
            notes.SelectionIndent = 14;
            notes.SelectionHangingIndent = 14;
            foreach (var item in e.Items)
                notes.AppendText("• " + item + "\n");
            notes.SelectionIndent = 0;
            notes.SelectionHangingIndent = 0;
            notes.AppendText("\n");
        }
        notes.Select(0, 0);
        hide.Text = "Bir daha gösterme";
        hide.AutoSize = true;
        hide.Left = 20;
        hide.Top = ClientSize.Height - 40;
        var ok = new Button { Text = "Tamam", DialogResult = DialogResult.OK, Width = 110, Height = 30, Left = 510, Top =
            ClientSize.Height - 46 };
        Fancy.Kind(ok, ButtonKind.Primary);
        AcceptButton = ok;
        Controls.AddRange(new Control[] { head, notes, hide, ok });
    }
}

sealed partial class MemorySelectV3Form
{
    // Opened on demand from the "Yenilikler" button, whatever was seen before.

    void ShowWhatsNewNow()
    {
        string version = ReleaseNotes.CurrentVersion;
        var entry = ReleaseNotes.Find(version) ?? ReleaseNotes.All[0];
        using (var form = new WhatsNewForm(entry, version, Icon, BackColor, ForeColor))
            form.ShowDialog(this);
    }

    // Shown once per new version, unless the user ticked "Bir daha gösterme".

    void ShowWhatsNew()
    {
        try
        {
            string version = ReleaseNotes.CurrentVersion;
            if (settings.HideWhatsNew || settings.LastSeenVersion == version)
                return;
            var entry = ReleaseNotes.Find(version);
            settings.LastSeenVersion = version;
            if (entry != null)
            {
                using (var form = new WhatsNewForm(entry, version, Icon, BackColor, ForeColor))
                {
                    form.ShowDialog(this);
                    if (form.DontShowAgain)
                        settings.HideWhatsNew = true;
                }
            }
            settings.Save();
        }
        catch { }
    }
}

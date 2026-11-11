# Silkroad R Studio · Tasarım sistemi

Bu belge arayüzün nasıl tasarlandığını ve yeni bir ekran eklerken hangi kurallara uyulacağını anlatır. Tüm değerler kodda tek yerdedir: [src/Ui.cs](../src/Ui.cs) (renk, yazı tipi, aralık) ve [src/UiControls.cs](../src/UiControls.cs) (düğme, kart, sekme şeridi).

## İlkeler

1. **Beyaz yüzey, açık gri zemin.** Sayfalar `Canvas` rengindedir, içerik beyaz kartlardadır. Gölge ve gradyan yok; ayrım ince bir çerçeveyle yapılır.
2. **Tek vurgu rengi.** Birincil işlem (Bağlan, Ekle, Bölgeyi set et) ve seçili öğe vurgu rengini kullanır. Yeşil yalnızca *Başlat* ve olumlu durum, kırmızı yalnızca *Durdur* ve hata içindir.
3. **Yoğun ama nefes alan.** Bir bot paneli çok ayar taşır; kartlar bu ayarları konuya göre gruplar, satırlar 8 px aralıkla dizilir.
4. **Hiçbir şey kesilmez.** Genişlik sabit piksel yerine kartın iç genişliğine göre hesaplanır; dar pencerede tek sütuna düşülür.
5. **Klavyeyle de kullanılır.** Odaklı düğmede halka, sekme şeridinde ok tuşları, listede yön tuşları çalışır.

## Renkler

| Rol | Değer | Kullanım |
|---|---|---|
| Canvas | `#F3F5F9` | Sayfa zemini |
| Surface | `#FFFFFF` | Kart, başlık, kenar çubuğu, tablo |
| Border / Divider | `#D9DFE8` / `#E6EAF1` | Kart çerçevesi / ince ayraç |
| Text | `#0F172A` | Gövde metni (beyazda 17:1) |
| TextSecondary | `#475569` | İkincil metin (7,6:1) |
| TextMuted | `#596579` | İpucu, açıklama (5,6:1) |
| Accent | seçilebilir, varsayılan `#2563EB` | Birincil düğme, seçili menü, bağlantı |
| Success / Danger | `#15803D` / `#B91C1C` | Başlat / Durdur, durum (beyaz yazıyla ≥ 5:1) |
| Warning | `#B45309` | Uyarı notu |

Vurgu renkleri: Mavi, Zümrüt, Mor, Turuncu, Gül, Turkuaz, Grafit. Hepsi beyaz yazıyla en az 4,5:1 kontrast verir. Yeni bir renk eklerken bunu doğrulayın.

## Yazı tipleri

Segoe UI 10 pt gövde, Segoe UI Semibold başlık ve düğme, Consolas 9,5 pt günlük. Simgeler Segoe MDL2 Assets ile çizilir; yazı tipi yoksa simgeler atlanır, metin kalır. Emoji simge olarak kullanılmaz.

## Bileşenler

- **Düğme** (`Fancy.Kind(button, ButtonKind.X)`): `Primary` (vurgu dolgu), `Secondary` (beyaz, çerçeveli), `Success`, `Danger`, `Ghost` (metin düğmesi). Üzerine gelince renk 150 ms içinde geçer, basılıyken koyulaşır, pasifken soluk griye döner, odaklıyken 2 px halka alır. Yükseklik 32 px (Başlat/Durdur 40 px).
- **Kart** (`CardBox`): başlık, ince ayraç, 16 px iç boşluk. Av sayfasında `Card(title, column)` kullanılır; `0` sol, `1` sağ, `2` tam genişlik.
- **Sekme şeridi** (`TabStrip`): metin sekmeleri, seçili olanda vurgu çizgisi. Bir `FlatTabs` denetimini sürer.
- **Kenar çubuğu**: simge + etiket, iki başlık (Çalışma, Araçlar), seçili sayfa vurgu zeminli hap içinde.
- **Tablo** (`Ui.StyleGrid`): açık başlık, yalnızca yatay çizgi, seçili satır vurgu tonu, ziyaret edilen satır yeşil ton.
- **Giriş kutuları**: yerel Windows denetimleri, beyaz zemin. Etiket her zaman görünür; yer tutucu etiket yerine geçmez.
- **Etiketler**: `Tag = "muted"` ipucu rengi, `Tag = "warn"` uyarı rengi verir.

## Düzen

- Üst çubuk 84 px: sol üstte marka, yanında bağlantı denetimleri; ikinci satırda bağlantı noktası, durum satırı, `Yenilikler` ve vurgu rengi.
- Alt çubuk 68 px: Başlat / Durdur ve tek satırlık durum.
- Etkinlik günlüğü kartı yoktur; kayıtlar events.log dosyasına yazılır ve Log sayfasında okunur.
- Pencereyi kapatma `ConfirmDialog` ile sorulur (güvenli yanıt varsayılan düğmedir, Esc de onu seçer). Rozet düğmeleri `ChipBox` ile çizilir.
- Av / Ayarlar: genişlik ≥ 1060 px ise iki sütun, değilse tek sütun. Kart genişliğine göre uzayan denetimler `Stretch(control)` ile işaretlenir.
- Diğer sayfalar tek kart için `PageCard(page, title, width)` kullanır.

## Yeni ekran eklerken

1. Renk, yazı tipi ve aralığı `Ui` içinden al; kodda yeni `Color.FromArgb` yazma.
2. Düğmeleri `Btn(...)` ya da `Fancy.Kind(...)` ile oluştur; eylemin anlamına göre türünü seç.
3. Sabit genişlikli satır yerine `Line(card)` ile sarılan satır kullan; uzayan denetimi `Stretch` ile işaretle.
4. Sayfayı `tabs` içine ekle, `navIcons` sözlüğüne simge ekle, `docs` altına gerekiyorsa not düş.
5. `tools\run-tests.ps1` çalıştır. Test üç pencere boyutunda (1120×700, 1480×900, 1920×1080) kesilen kontrol arar ve `tests\Layout-Report.txt` dosyasına yazar; `tests\Preview-*.png` ekran görüntülerine bakarak gözle de doğrula.

## Araştırma notu

Beyaz masaüstü aracı yönü için arayüz tasarım veri tabanından (stil: *Minimalism & Swiss*, yoğun gösterge paneli ayarı) ve phBot, sBot, mBot, rsBot'un bilinen düzen alışkanlıklarından yararlanıldı: tek pencerede soldan gezinme, üstte bağlantı ve karakter bilgisi, altta Başlat / Durdur, bölge ve hedef ayarlarının kartlarda toplanması. Bu botların ekran görüntülerine internetten ulaşılamadı; benzerlikler belge ve deneyime dayanır, birebir kopya değildir.

# Silkroad R Studio · Tasarım sistemi (taş panel, bronz-altın teması)

Bu belge arayüzün nasıl tasarlandığını ve yeni bir ekran eklerken hangi kurallara uyulacağını anlatır. Pencere içi tasarım MSRO BOT panelinin tarzındadır; ayrıntılı şartname
[design-msro-style.md](design-msro-style.md) dosyasındadır. Tüm değerler kodda tek yerdedir: [src/Ui.cs](../src/Ui.cs) (renk, yazı tipi, aralık, doku) ve
[src/UiControls.cs](../src/UiControls.cs) (düğme, onay kutusu, grup kutusu, sekme şeridi).

**Açılış ekranı bu sistemin dışındadır.** `Startup.cs` ve onu çağıran kod değişmez; açılış ekranının okuduğu eski renk üyeleri (`Ui.Canvas`, `Surface`, `Border`, `Text`, `TextMuted`,
`Title`) eski değerleriyle kalır. Pencerenin içinde bu adlar kullanılmaz; yerlerine yeni adlar vardır (aşağıda).

## İlkeler

1. **Taş panel.** Zemin koyu taş grisi (hafif gradyan ve gürültü dokusu yalnız dış çerçevede), grup kutuları bir kademe açık, çerçeve ince bronz. Gölge yok; ayrım 1 px çerçeveyle yapılır.
2. **Tek vurgu: altın.** Birincil işlem, sayaç rozeti, onay tiki ve seçili öğe altın/bronz tonundadır (vurgu rengi seçilebilir, varsayılan altın). Vurgu dolgusu üstündeki yazı koyu lakedir (`OnAccent`).
   Yeşim yeşili yalnız *Başlat* ve olumlu durum, kızıl yalnız *Durdur* ve hata içindir.
3. **Başlık + tek satır açıklama.** Her sayfanın üstünde büyük altın-krem başlık ve altında soluk tek satır açıklama bulunur (`RouteHeader`, açıklamalar `RouteHeader.Describe`); altında bronz ayraç.
4. **Soldan numaralı gezinme.** `01 … 16` numaralı, bordürlü koyu düğmeler; seçili olan bronz-altın dolgulu. Gruplar küçük soluk büyük harfli etiketle ayrılır.
5. **Çerçeveye gömülü başlık.** Grup kutusunun başlığı üst çerçeve çizgisinin içindedir.
6. **Hiçbir şey kesilmez, klavyeyle de kullanılır.** Genişlik kartın iç genişliğine göre hesaplanır; odaklı düğmede halka, sekme şeridinde ok tuşları, skill listesinde ↑ ↓ / Ctrl+↑↓ / Boşluk çalışır.

## Renkler

| Rol | Üye | Değer | Kullanım |
|---|---|---|---|
| Dış çerçeve | `Ui.Back` | `#1B1C1C` | Sol gezinme, günlük, alt çubuk |
| Sayfa | `Ui.Stone` | `#242524` | Sayfa zemini |
| Grup kutusu | `Ui.Card` | `#2D2E2A` | Kart dolgusu |
| Yükseltilmiş | `Ui.SurfaceAlt` | `#383930` | Düğme, hover satır, tablo başlığı |
| Giriş | `Ui.Field` | `#101114` | Giriş kutusu, liste, tablo |
| Çerçeve | `Ui.Bronze` / `Ui.BorderStrong` / `Ui.Divider` | `#6E5E38` / `#968048` / `#403E34` | Kart / düğme-onay kutusu / ince ayraç |
| Satır / etkin menü | `Ui.RowFill`+`RowEdge` / `Ui.NavOn`+`NavOnEdge` | `#4A4128`+`#8C7640` / `#685A30`+`#BEA460` | Skill satırı, onaylı kutucuk / seçili menü |
| Metin | `Ui.Ink` / `Ui.TextSecondary` / `Ui.InkMuted` | `#E8E2D0` / `#CEC8B6` / `#A09A86` | Gövde (≥ 11:1) / ikincil (≥ 8:1) / ipucu (≥ 4,8:1) |
| Başlık | `Ui.Gold` | `#F4E2AA` | Sayfa ve kutu başlığı |
| Vurgu | `Ui.Accent` | seçilebilir, varsayılan `#DAA648` | Birincil düğme, rozet, tik |
| Durum | `Success` / `Danger` / `Jade` / `Warning` | aynı | Başlat / Durdur / bağlantı noktası / uyarı |

Vurgu renkleri: Altın, Yeşim, Lapis, Bakır, Kızıl, Turkuaz, Gümüş (hepsi koyu yazıyla ve kart üstünde ≥ 4,7:1). Yeni bir renk eklerken bunu doğrulayın.

## Yazı tipleri

Segoe UI ailesi: `PageTitle` 15 pt Semibold (sayfa başlığı), `Heading` 9 pt Semibold (grup başlığı, bölüm başlığı; `Tag = "gold"` etiketler altın-krem olur), `Body` 9 pt, `Small` 8,25 pt (açıklama, ipucu),
`GroupLabel` 7,5 pt (menü grup etiketi ve numara), `Mono` Consolas (günlük). Serif (`Ui.Title`) yalnız açılış ekranında ve karakter adında kalır. Simgeler kodla çizilir.

## Simgeler

`SroIcons` (src/SroIcons.cs) 16 × 16 ızgarada, Silkroad'a uygun simgeler çizer (deve, çin parası, fener, kapı, pusula, hasır şapkalı yolcu, tomar…) ve skill türleri için
`skill.attack / buff / heal / mana / emergency / party` simgelerini. Simge sayfa adı ya da tür anahtarıyla anılır: `SroIcons.Draw(g, "Quest", rect, renk)`. Oyunun kendi görselleri yalnız
`GameArt` ile (açılış filmi kareleri, sayfa listesinin altındaki plaka) kullanılır; MSRO'dan hiçbir görsel alınmamıştır.

## Bileşenler

- **Düğme** (`Fancy.Kind(button, ButtonKind.X)`): `Primary` (vurgu dolgu), `Secondary` (koyu zeytin plaka + bronz çerçeve, üzerine gelince altın çerçeve), `Success`, `Danger`, `Ghost`. 2 px köşe,
  150 ms geçiş, basılıyken koyulaşır, pasifken soluk, odaklıyken halka. Yükseklik 26–32 px.
- **Onay kutusu** (`Fancy.StyleCheck`, ApplyTheme tüm CheckBox'lara uygular): 14 px kare, koyu dolgu, bronz çerçeve; işaretliyken altın çerçeve + altın tik. `ChipBox`: aynı dilde geniş kutucuk.
- **Grup kutusu** (`CardBox`): 1 px bronz çerçeve, başlık çizginin içinde, isteğe bağlı sayı rozeti (`Badge`). Av sayfasında `Card(title, column)`; tek kart için `PageCard`.
- **Sekme şeridi** (`TabStrip`): metin sekmeler, seçili olanda altın çizgi ve altın-krem yazı; bir `FlatTabs` denetimini sürer.
- **Gezinme** (`PhShell.BuildNavigation`): numaralı bordürlü düğmeler, simge, ad; dört + bir grup (Başlangıç, Karakter, Av, Yolculuk, Araçlar).
- **Alt çubuk**: bağlantı/durum metni, “Kaydedildi hh:mm” notu (her `SaveSettings` sonrası) ve önceki/sonraki sayfa okları.
- **Skill listesi** (`SkillListView`): numaralı, simgeli, oklu bronz satırlar; sayfa-yan sayaçlar. Veri `skillGrid` içinde kalır (bkz. design-msro-style.md).
- **Tablo** (`Ui.StyleGrid`): koyu gövde, olive başlık (altın yazı), yalnız yatay çizgi, seçili satır `RowFill`.
- **Giriş kutuları**: yerel Windows denetimleri, `Field` zemin; kaydırma çubukları ve açılır listeler koyu varyantla (`Ui.DarkNative`). Etiket her zaman görünür.
- **Etiketler**: `Tag = "muted"` ipucu, `"warn"` uyarı, `"gold"` altın-krem başlık rengi verir.
- **Doku**: `Ui.PaintStone` (çok hafif gradyan + ±3 seviyelik gürültü kutucuğu) başlık şeridinde ve `StonePanel` ile dış çerçevede (sol sütun, günlük, alt çubuk) kullanılır.

## Düzen

- Sol sütun 216 px: oyun seçici, numaralı sayfa listesi, altta ejderha plakası (`ArtPlate`). Altta günlük + altı düğme, onun altında 28 px alt çubuk.
- Etkinlik günlüğü kartı yoktur; kayıtlar events.log dosyasına yazılır ve Log sayfasında okunur.
- Pencereyi kapatma `ConfirmDialog` ile sorulur (güvenli yanıt varsayılan düğmedir, Esc de onu seçer).
- Av / Ayarlar: genişlik ≥ 1060 px ise iki sütun, değilse tek sütun. Kart genişliğine göre uzayan denetimler `Stretch(control)` ile işaretlenir.
- Diğer sayfalar tek kart için `PageCard(page, title, width)` kullanır; sayfa başlığı ve açıklaması ayrıca kabuktan (`RouteHeader`) gelir, sayfa içinde yinelenmez.
- Saldırı sayfası: Skill ve Buff · Seçenekler · Profil / şablon · Gelişmiş tablo · Nasıl çalışır (`SubTabs`).

## Yeni ekran eklerken

1. Renk, yazı tipi ve aralığı `Ui` içinden al (yeni adlar: `Stone`, `Card`, `Bronze`, `Ink`, `InkMuted`, `Gold`…); eski `Canvas/Surface/Border/Text/TextMuted` adlarını kullanma (yalnız açılış ekranı içindir).
2. Düğmeleri `Btn(...)` ya da `Fancy.Kind(...)` ile oluştur; onay kutusu için düz `CheckBox` yeter (ApplyTheme biçimler).
3. Sabit genişlikli satır yerine `Line(card)` ile sarılan satır kullan; uzayan denetimi `Stretch` ile işaretle. Kartın tam genişliğini isteyen denetimi doğrudan kartın akışına ekle (Line'ın `MaximumSize` sınırı vardır).
4. Sayfayı `tabs` içine ekle, `navIcons`/`SroIcons.shapes` içine simge, `RouteHeader.descriptions` içine tek satır açıklama ekle.
5. `tools\run-tests.ps1` çalıştır. Test üç pencere boyutunda (1120×700, 1480×900, 1920×1080) kesilen kontrol arar ve `tests\Layout-Report.txt` dosyasına yazar; `tests\Preview-*.png` ekran görüntülerine bakarak gözle de doğrula.

## Araştırma notu

Yön, MSRO BOT panelinin ekran görüntülerinden (taş panel, numaralı sol menü, çerçeveye gömülü başlıklı grup kutuları, altın çerçeveli onay kutuları, numaralı skill listesi) ve
phBot, sBot, mBot, rsBot'un bilinen düzen alışkanlıklarından (tek pencerede soldan gezinme, üstte bağlantı bilgisi, altta Başlat / Durdur) türetildi. Yalnız stil aktarıldı; hiçbir görsel kopyalanmadı.

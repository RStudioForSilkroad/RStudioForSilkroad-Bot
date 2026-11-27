# Tasarım şartnamesi · MSRO BOT paneli tarzı iç tasarım

Bu belge, botun **pencere içi** görünümünün MSRO BOT panelinin (bkz. `MSRO_analysis/panel_screens/t01-t10.png`, `RAPOR.md` bölüm 6) tarzında yeniden tasarlanmasını anlatır.
Yalnızca **stil** aktarılır; hiçbir MSRO görseli kopyalanmaz, her şey GDI+ ile kodla çizilir ya da projedeki mevcut varlıklar kullanılır
(`SroIcons.cs`, `GameArt.cs`, `ArtPlate.cs`). Kodda tek kaynak: `src/Ui.cs` (renk, yazı tipi), `src/UiControls.cs` (düğme, grup kutusu, onay kutusu),
`src/PhShell.cs` (gezinme, alt çubuk), `src/RouteHeader.cs` (sayfa başlığı).

## Değişmeyenler (dokunulmaz)

- **Açılış ekranı** (`src/Startup.cs`: `Startup`, `StartupSplash`; çağıran `V3App.cs` `Main`): hiçbir satırı değişmedi. Açılış ekranı `Ui.Canvas`, `Ui.Surface`, `Ui.Border`,
  `Ui.Text`, `Ui.TextMuted`, `Ui.Accent`, `Ui.OnAccent`, `Ui.Title` ve `Ui.Mix/RoundRect/SetAccent` okur. Bu üyeler **eski değerleriyle** kalır
  ("açılış paleti"); pencerenin içi yeni üyeleri kullanır. Böylece iki taraf birbirini bozmaz.
- Otomasyon ve oyun etkileşimi: `MemoryCoreV3.cs`, `V3App.cs` `Tick()`, `TargetSender`, opcode/paketler, guard, AutoResume. Sayfa sayısı (16), sırası ve indeksleri,
  `V3Settings` alanları (yalnız ekleme serbest), kayıtlı profil/şablonlar.

## Palet

| Rol | Üye | Değer | Kullanım |
|---|---|---|---|
| Dış çerçeve | `Ui.Back` | `#1B1C1C` | Sol gezinme, alt çubuklar, günlük |
| Sayfa taşı | `Ui.Stone` | `#242524` | Sayfa zemini (taş grisi, üstüne ince gradyan + gürültü dokusu) |
| Grup kutusu | `Ui.Card` | `#2D2E2A` | Grup kutusu / kart dolgusu |
| Yükseltilmiş | `Ui.SurfaceAlt` | `#383930` | Düğme zemini, üzerine gelinen satır, tablo başlığı |
| Giriş alanı | `Ui.Field` | `#101114` | TextBox, ComboBox, liste, tablo (neredeyse siyah) |
| Bronz çerçeve | `Ui.Bronze` | `#6E5E38` | Grup kutusu ve gezinme düğmesi çerçevesi (1 px) |
| Güçlü çerçeve | `Ui.BorderStrong` | `#968048` | Düğme, onay kutusu, odak |
| Ayraç | `Ui.Divider` | `#403E34` | İnce çizgi |
| Satır dolgusu | `Ui.RowFill` / `Ui.RowEdge` | `#4A4128` / `#8C7640` | Skill satırı, onaylı kutucuk, seçili tablo satırı |
| Etkin gezinme | `Ui.NavOn` / `Ui.NavOnEdge` | `#685A30` / `#BEA460` | Seçili menü girişi |
| Metin | `Ui.Ink` | `#E8E2D0` | Gövde (Card üstünde ≥ 11:1) |
| İkincil / soluk | `Ui.TextSecondary` / `Ui.InkMuted` | `#CEC8B6` / `#A09A86` | Açıklama, ipucu (≥ 4,8:1) |
| Başlık | `Ui.Gold` | `#F4E2AA` | Sayfa başlığı, kutu başlığı (altın-krem) |
| Vurgu | `Ui.Accent` | seçilebilir, varsayılan `#DAA648` | Birincil düğme, sayı rozeti, tik |
| Vurgu üstü yazı | `Ui.OnAccent` | `#241A15` | Vurgu dolgusu üstündeki yazı |

Durum renkleri aynı kalır (`Success`, `Danger`, `Jade`, `Warning`). Eski adlar (`Canvas`, `Surface`, `Border`, `Text`, `TextMuted`) yalnız açılış ekranı içindir.

## Yazı tipleri

Segoe UI ailesi: `Ui.PageTitle` 15 pt Semibold (sayfa başlığı), `Ui.Heading` 9 pt Semibold (kutu başlığı), `Ui.Body` 9 pt, `Ui.Small` 8,25 pt
(açıklama, numara, ipucu), `Ui.Mono` Consolas (günlük). Serif (`Ui.Title`) yalnız açılış ekranında kalır.

## Bileşenler

- **Sayfa başlığı** (`RouteHeader`, her sayfanın üstünde, 16 sayfa da devralır): 15 pt altın-krem başlık, altında tek satır soluk açıklama (`RouteHeader.Describe`),
  sağ uçta karakter/bağlantı satırı (nokta + metin), altında 1 px bronz ayraç. Eski dotted caravan yolu ve eşsiz portresi kalktı.
- **Gezinme** (`PhShell.BuildNavigation`): sol sütun 216 px; her giriş bordürlü koyu düğme (yükseklik 22, aralık 4), solda küçük numara `01`…`16`, simge, ad. Seçili giriş bronz-altın
  dolgu (`NavOn`) + açık kenar + krem yazı; üzerine gelince kenar bronz→altın. Gruplar (Başlangıç, Karakter, Av, Yolculuk, Araçlar) küçük soluk büyük-harf etiket + ince çizgi.
- **Grup kutusu** (`CardBox`): 1 px bronz çerçeve, başlık **çerçeve çizgisinin içine gömülü** (çizgi başlığın arkasında kesilir), altın-krem 9 pt Semibold; isteğe bağlı sayı rozeti
  (`Badge`, vurgu renginde: `Saldırı 6`). İç boşluk 10 px, üst 20 px.
- **Giriş**: yerel TextBox/ComboBox/NumericUpDown, `Field` zemin, koyu native çizim; etiket her zaman görünür.
- **Onay kutusu** (`Fancy.StyleCheck`, ApplyTheme tüm CheckBox'lara uygular): 14 px kare, `Field` dolgu, `BorderStrong` çerçeve, işaretliyken altın çerçeve + altın tik. `ChipBox` aynı dilde
  geniş kutucuk (işaretliyken `RowFill` dolgu + `RowEdge` çerçeve + tik).
- **Düğme** (`Fancy`): 2 px köşe, ikincil = koyu zeytin plaka (`SurfaceAlt`) + bronz çerçeve, hover'da çerçeve altına döner; birincil = vurgu dolgusu; Success / Danger / Ghost aynen.
- **Liste satırı** (`SkillListView`): `RowFill` dolgu + `RowEdge` çerçeve, solda 30 px çerçeveli simge, kalın krem `N. Ad`, altında soluk ayrıntı, sağda ince ▲/▼ okları; seçili satır açık çerçeve.
- **Alt çubuk** (`statusStrip`, 26 px): solda bağlantı/durum metni, ortada küçük ◆, sağda küçük “Kaydedildi hh:mm” ve sayfalar arası ▲ ▼ okları.
- **Doku**: dış çerçeve yüzeyleri (`Ui.Stone`/`Back` dolgusu) çok hafif dikey gradyan ve ±3 seviyelik gürültü kutucuğu (`Ui.PaintStone`) ile çizilir; grup kutuları düz.

## Sayfa eşlemesi

Tüm 16 sayfa başlık, gezinme, grup kutusu, düğme, giriş ve onay kutusunu merkezden devralır; sayfa içeriği yerinde kalır.

| Sayfa | Not |
|---|---|
| Av Alanı, Koruma, İstatistik | Grup kutuları (Card) yeni çerçeve/başlıkla; ayarlar aynı |
| **Saldırı (Skill)** | Aşağıda |
| Grim Crypt, Explorer, Kervan, Envanter, Bildirimler, Nanzhang, Quest, Lottery, Çift Hesap, Güncelleme, Log, Yenilikler | Başlık + açıklama, tablo/giriş/düğme stili |

### Saldırı sayfası (MSRO t02 düzeni)

Alt sekmeler: **Skill ve Buff** · Seçenekler · Profil / şablon · Gelişmiş tablo · Nasıl çalışır. (“Karakterin skilleri” sekmesi kaldırıldı: skill ID ile kullanım yok.)

1. *Skill ve Buff*: üstte **Imbue** kutusu (açılır liste: `Kullanma` + adında “Imbue” geçen satırlar; “Savaş sırasında etkisi bitince yenilenir” notu), altta yan yana
   **Saldırı N** ve **Buff N** kutuları: numaralı (`1. Tuş 1 skilli`) sıralı liste, satırda tür simgesi, açık/kapalı kutusu, ayrıntı satırı (tuş · Buff ID · hedef · mob · süre), sağda satır okları;
   kutu altında seçili satırı taşıyan ▲ ▼. **Tuşu olmayan satır** turuncu çerçeve ve turuncu “⚠ TUŞ YOK · atılmaz” ayrıntısıyla gösterilir (`Ui.Warning`); eski Parti buff satırı “Parti buff · kullanılmaz” der.
   Altında **Seçili skill** düzenleyicisi: ilk alan **Hotbar tuşu** (kutuya tıklayıp tuşa basılır: 1-9, 0, A-Z, F1-F12; elle yazılabilir), sonra ad, tür, hedef türü, mob ≥, süre, HP/MP %, en altta
   **Buff ID (yalnız buff’ın açık olup olmadığını okumak için)**; Ekle (ilk boş hotbar tuşuyla yeni satır) / Sil / Buff ID öğren.
   Alt not: “Sıra yukarıdan aşağıya uygulanır · öncelik: acil → iyileştirme → mana → buff → saldırı · skill’ler yalnız hotbar tuşuna basılarak kullanılır, oyun önde olmalı”.
2. Veri modeli aynıdır: `skillGrid` (DataGridView) modeli tutmaya devam eder (Gelişmiş tablo sekmesinde görünür), `SkillSlotsFromGrid`, profil, şablon, `V3Settings.Skills` değişmedi. Yeni liste görünümü
   modelin bir yansımasıdır; düzenleyici hücrelere yazar.
3. Buff kutusu: Buff, Acil, İyileştirme, Mana türleri (eski Parti buff satırları da burada, kullanılmaz); Saldırı kutusu: Saldırı türü. Sıra, kategori içinde yer değiştirir (çalışma önceliği zaten türe göredir).

## Davranış değişiklikleri

Skill ID ile kullanım kaldırıldı (paketle gönderilen skill oyunu çökertiyordu): satırlar yalnız hotbar tuşuna basılarak çalışır; tuşsuz satır atlanır ve çalıştırınca sayısı kayda yazılır.
Parti buff satırları kullanılmaz. Imbue seçici adında Imbue geçen satırı açıp kapatır (yeni satır eklemez). Ayarlar dosyası aynı: `SkillId` (artık yalnız Buff ID), `Members`, `WeaponSwap`, `CastPacePercent` alanları eski dosyalar için durur.

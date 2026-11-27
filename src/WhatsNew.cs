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
    internal static readonly Entry[] All = { new Entry { Version = "3.14.0.0", Title = "Skill'ler yalnızca tuşla atılıyor; paketle atma kaldırıldı",
        Items = new[] { "Kaldırıldı: Skill'leri kimlikle (paketle) atma. Bu yöntem oyunun gönderme yolunu botun kendi iş parçacığından çağırdığı için oyunun çökmesine yol açıyordu; hotbar tuşuyla atarken sorun görülmedi. Artık skill'ler yalnızca hotbar tuşuna basılarak atılır, oyun penceresi önde olmalıdır ve arka planda skill atılmaz. Bot oyuna skill paketi göndermez (cast opcode'u izin listesinden çıkarıldı).",
        "Kaldırıldı: “Karakterin skilleri” sekmesi (bellek taraması, tüm skiller tablosu, Listeye ekle, Kimlikle sına) ve Parti buff satır türü. Eski Parti buff satırları listede kalır ama atılmaz.",
        "Yeni: Skill satırının ilk alanı “Hotbar tuşu”: alana tıklayıp tuşa bas (1-9, 0, A-Z, F1-F12). Tuşu olmayan satır turuncu çerçeve ve “TUŞ YOK · atılmaz” ile işaretlenir; çalıştırırken kaç satırın atılmayacağı günlüğe yazılır. “+ Skill ekle” ilk boş tuşla yeni satır açar.",
        "Değişti: Buff ID yalnızca buff'ın açık olup olmadığını okumak için kullanılır (tuşa ne zaman basılacağını belirler); paket göndermez. “Skill aralığı %” ve silah değişimi seçenekleri gösterilmiyor; Wizard / Cleric otomatik silah değişimi artık çalışmıyor. Eski ayar dosyaları olduğu gibi yüklenir." } },
        new Entry { Version = "3.13.11.0", Title = "Toplu zemin yoklaması, temiz oyun çıkışı, kararlı ölüm algısı",
        Items = new[] { "Yeni: Grim dungeon zemin taraması oyuna yoklamaları 16'lık gruplar hâlinde gönderiyor (eskiden her yoklama için ayrı bir uzak iş parçacığı). Canlı bir zemin taramasında çağrı sayısı yaklaşık 1000'den 70'e iniyor; sonuçlar eski yöntemle aynı. Duran karakter artık her tick'te yoklama yapmıyor.",
        "Düzeltme: Oyun çöktüğünde ya da kapandığında bot artık “Erişim engellendi” hata yığını yazmıyor; tek satır “Oyun kapandı (uzak çağrı sırasında süreç kayboldu)” yazıp otomatik devam akışını hazırlıyor.",
        "Düzeltme: Dungeon yüklenirken yaşam bayrağının bir an düşmesi artık ölüm sayılmıyor; ölüm en az 800 ms sürerse işleniyor. Gerçek ölüme tepki yaklaşık 1 saniye içinde." } },
        new Entry { Version = "3.13.10.0", Title = "Yanlış ölüm alarmı düzeltildi, harita rotası teşhisi",
        Items = new[] { "Düzeltme: Reverse sonrası alan yüklenirken okunan geçersiz konum (Bölge 0, X -25920 Y -17664) artık ölüm sayılmıyor; “Karakter öldü” yanlış alarmı ve gereksiz canlandırma beklemesi kalktı. Gerçek ölüm hâlâ ilk gerçek konum okumasında algılanıyor.",
        "Yeni: Harita rotası hedefe bağlanamayıp yalnızca canlı yaklaşmaya düştüğünde günlüğe “Harita portala bağlı değil…” yazılıyor (son bağlı nokta ve boşluk ile). Kalıcı engel kayıtları bu yüzden rotayı bozuyorsa, onlar olmadan tam rota varsa engeller temizlenip o rota kullanılıyor.",
        "Bilinen: Constantinople Jupiter Temple (D10) girişi için harita ağında tapınak avlusuna açılan gerçek geçit eksik; rota (-12495, 900) civarında takılabilir. Kalıcı çözüm için Grim Crypt sayfasında “Yol öğret” ile Garden of Gods Reverse noktasından portala (X -12389, Y 753) giden yolu bir kez yürüyüp kaydet; “Yolları kopyala” ile paylaşılırsa hazır yollara eklenir." } },
        new Entry { Version = "3.13.9.0", Title = "Çağrı günlüğü: çökme ve kopmaları bot kaydıyla eşleştirmek için",
        Items = new[] { "Yeni: Oyuna giden her çağrı için milisaniye damgalı bir günlük tutuluyor (events.log'un yanındaki remote-calls.log): saat, PID, opcode, çağrı türü, süre, bekleme ve sonuç. Paket içeriği ve hesap bilgisi yazılmıyor. Oyun çökerse son çağrının çökmeye ne kadar yakın olduğu buradan okunur.",
        "Yeni: Günlüğe bağlanma, ayrılma, oyun kapandı, crash.log değişti ve client.log'ta bağlantı kopması işaretleri de yazılıyor; çökme iki çağrı satırı arasına yerleştirilebiliyor.",
        "Değişti: Günlük 2 MB olunca yedeğe (.1) alınıyor; saniyede 20 satırı aşan yoklama patlamaları tek satırda toplanıyor." } },
        new Entry { Version = "3.13.8.0", Title = "Skill taraması sınırlandı: hızlı, bellek dostu",
        Items = new[] { "Düzeltme: “Skilleri oku” artık dakikalarca işlemciyi yemiyor ve belleği tüketmiyor. Okuma en çok 20 saniye, gruplama en çok 10 saniye sürüyor; bellek yetmezse “Bellek yetmedi; tekrar dene” diyor. Önceki sürümde tarama 3 dakikadan uzun sürüp OutOfMemory ile düşebiliyordu.",
        "Düzeltme: Skill taraması sırasında “Taranıyor… N sn” sayacı görünüyor; tarama sürerken düğmeye tekrar basmak hata yığını yerine bilgi satırı gösteriyor.",
        "Yeni: Tarama yalnızca seviyesi skill tablosuyla eşleşen adayları tutuyor ve en çok 400.000 aday saklıyor; gerçek skill listesi bulma kuralları aynı." } },
        new Entry { Version = "3.13.7.0", Title = "Kararlılık paketi: güncelleme bekler, günlük sınırlı, çağrılar sıralı",
        Items = new[] { "Düzeltme: Yakalanmayan bir hata artık kimsenin kapatmadığı hata penceresi açmıyor; hata ayrıntılı olarak events.log dosyasına yazılıyor ve bot durmuyor.",
        "Düzeltme: Güncelleme bulununca çalışan av ya da kervan kapanmıyor; güncelleme bot boşta olunca (bekleyen otomatik devam da dahil) uygulanıyor. Meşgul bir bot 6 saat sonra, eşya işlemi ve ölüm kurtarma yokken güncellenir. Güncelleme kontrolü bulunduktan sonra 5 dakikada bir yapılıyor.",
        "Düzeltme: Ayar dosyası geçici olarak kilitliyse (antivirüs, senkron programı) bot durmuyor; art arda 5 hatadan sonra duruyor ve her hata kayda yazılıyor.",
        "Yeni: Günlük sınırlandı. Bellekteki günlük yaklaşık 2000 satırda tutuluyor, events.log 2 MB olunca yedeğe (.1) alınıyor, Log sekmesi yalnızca dosyanın sonunu okuyor.",
        "Yeni: Oyuna giden çağrılar tek sıraya alındı ve aralarına en az 15 ms bırakılıyor. Alan yüklenirken (ışınlanma, kapı, dungeon girişi sonrası ilk 5 saniye), oyun penceresi yanıt vermezken ve oyuncu bilgisi geçersizken komut gönderilmiyor.",
        "Yeni: Art arda 5 çağrı hatasında iş duruyor; oyun kapanır, \"Disconnected\" satırı yazılır ya da crash.log değişirse bot durup otomatik devam akışını hazırlıyor. Aynı oyun klasörünü paylaşan iki hesapta diğer istemcinin kopması ya da çökmesi de bu hesabı durdurabilir.",
        "Yeni: Bağlanma ve ayrılma (PID ile) ile oyuna giden çağrıların özeti (yaklaşık 10 saniyede bir) events.log dosyasına yazılıyor; çökme ve kopmaları bot kaydıyla eşleştirmek için.",
        "Düzeltme: Alan değişiminden sonraki 10 saniye ve oyun yanıt vermezken pencere uyandırma ve ekran görüntüsü alma çalışmıyor.",
        "Değişti: Derleme artık satır numaralı hata izi için bir .pdb dosyası üretiyor." } },
        new Entry { Version = "3.13.6.0", Title = "Yeni iç tasarım: taş gri panel, numaralı Skill ve Buff listesi",
        Items = new[] { "Yeni: Pencerenin içi MSRO BOT panelinin tarzında yenilendi: koyu taş gri zemin, ince bronz çerçeveli grup kutuları (başlık çerçeve çizgisine gömülü), altın çerçeveli onay kutuları, numaralı (01, 02 …) sol menü, her sayfanın üstünde büyük başlık ve altında tek satır açıklama, altta küçük \"Kaydedildi\" notu ve sayfalar arası oklar. Açılış ekranı aynen kaldı.",
        "Yeni: Saldırı sayfası yeniden düzenlendi. \"Skill ve Buff\" sekmesinde Saldırı ve Buff listeleri numaralı ve sayaçlı (Saldırı 6, Buff 0), her satırda skill simgesi, açma/kapama kutusu ve yukarı / aşağı okları var; seçili skill'in tuşu, türü, Skill ID'si, hedef türü, mob sayısı, süresi ve yüzdesi altındaki bölümden ayarlanır. Imbue seçicisi Imbue skillini açıp kapatır. Eski tablo \"Gelişmiş tablo\" sekmesinde durur; profiller, şablonlar ve kayıtlı ayarlar aynen çalışır, Skill ID'li satırlar tuşsuz kullanılmaya devam eder." } },
        new Entry { Version = "3.13.5.0", Title = "26 Kasım oyun güncellemesine uyum, Grim dungeonları şehir şehir",
        Items = new[] { "Düzeltme: Oyunun 26 Kasım güncellemesi koruma modülünü (GuardyNE.dll) değiştirmişti; bot yeni sürümü tanımıyor, bağlanamıyordu. Yeni modülün adresleri (gönderme, hareket, Reverse, navigasyon, binek, Grim yazısı) eski modülün kodu eşlenerek bulundu ve doğrulandı; bilinmeyen sürümler eskisi gibi reddedilmeye devam eder.",
        "Yeni: Grim çalıştırma sırası şehir şehir çalışır: önce Constantinople, sonra Samarkand, Hotan, Roc Mountain, Taklamakan, Donwhang ve Jangan. Tümünü ekle dungeonları bu sırayla ekler (girilmiş sarı olanlar atlanır); şehir içinde dungeon koduna göre gider ve bir dungeonun Solo Trial'ı Solo 85'inden hemen önce gelir. Yeni \"Şehir sırası\" düğmesi elle kurulan sırayı aynı şehir sırasına dizer (çalışan dungeon yerinde kalır). Girdileri sıfırla düğmesinin adı Girdi sıfırla oldu." } },
        new Entry { Version = "3.13.4.0", Title = "Nanzhang ejderhalı açılış, hazır kervan scriptleri, 4 GB bellek modu",
        Items = new[] { "Yeni: Oyun kapanınca otomatik devam (Av / Ayarlar > Oyun penceresi, kutu varsayılan kapalı). Oyun kapandığında bir av ya da kervan yolu çalışıyorsa bot launcher'ın AutoLogin'inin açtığı yeni oyunu bekler, aynı karakter dünyaya girince bağlanır, 15 sn bekler ve işi yeniden başlatır: av aynı bölgede, kervan yolun kaldığı yerinden. Başka karakterli oyunlar atlanır; oyun 30 dakikada gelmezse, karakter 2 dakikadan uzun ölü kalırsa ya da 30 dakikada 5 kez devam edildiyse bırakılır. Elle Durdur ya da Bağlan planı iptal eder. Şifre hiçbir yerde girilmez. Grim ve Explorer için devam yok.",
        "Düzeltme: Oyun kapanınca \"Oyun kapandı.\" satırı her saniye yazılıyordu; artık bir kez yazılıyor ve bot kapanan oyunu bırakıyor.",
        "Yeni: Parti buff. Skill listesine yeni tür: \"Parti buff\". Skill ID'sini (başkasına atılan buff) ve \"Parti üyeleri\" sütununa kişi adlarını yaz (virgül ya da noktalı virgülle ayır); Süre sn = yenileme aralığı (0 = koşu başına bir kez). Bot, adı listede olan, yakında (22 birim) duran ve ölü olmayan her kişiye skill'i ID ile, kişinin üstüne atar. Başkasının buff'ları okunamadığı için süre sayacı kullanılır. Gerekirse buff için gereken silahı takar.",
        "Yeni: Açılış ekranı oyunun kendi launcher görüntüsünü kullanıyor: Nanzhang kapısının üstünde dönen altın ejderha ateş püskürtüyor (launcher arka plan filminden on iki kare, birbirine karışarak oynar). Yolculuk çizgisi artık Nanzhang'dan başlıyor. Sayfa listesinin altındaki küçük resim aynı ejderhadan, her sayfanın başlığının sağında da oyunun yedi eşsiz canavarından biri (Tiger Girl, Cerberus, Captain Ivy, Uruchi, Isyutaru, Lord Yarkan, Demon Shaitan) oyunun kendi küçük resmiyle görünüyor.",
        "Yeni: Hotan - Jangan, Hotan - Donwhang, Jangan - Donwhang (iki yönde) ve Constantinople → Samarkand rotaları artık phBot kervan scriptlerindeki hazır yolu nokta nokta izliyor; feribot geçişleri (hangi NPC ile hangi yakaya geçileceği) scriptten gelir. Scriptlerde yalnızca yürüme, bekleme ve ışınlanma satırları alınır; sohbet, ticaret ve binek komutları hiçbir zaman alınmaz. Yolun ortasında durdurulan yürüyüş bulunduğu yerden devam eder. Samarkand → Constantinople, Jangan - Nanzhang ve Jangan - Jangan Pass eskisi gibi haritadan / kayıtlı yoldan gider. Rotanın \"Script\" düğmesi bu scripti scripts klasörüne yazar.",
        "Düzeltme: Oyun penceresi donunca (Windows \"yanıt vermiyor\" dediğinde) bot artık komut göndermiyor ve bekliyor. Eskiden donmuş oyuna saniyede seçim / yürüme komutu yağdırıyor, her \"yanıt yok\" cevabı bir canavarı 60 sn banlıyor ve yanıt gelince av alanındaki hemen hemen tüm canavarlar banlı kalıyordu. Oyun yeniden yanıt verince bantlar temizlenir.",
        "Düzeltme: Oyunun 4 GB bellek modu açıkken (launcher OPTIONS > MEMORY, config/memory.ini) bot \"İstemci sürümü değişmiş\" diyip bağlanmıyordu. Launcher bu modda oyunun başlığında yalnızca bir biti (büyük adres desteği) değiştiriyor; bot artık yalnızca o biti yok sayıyor, başka her değişikliği reddetmeye devam ediyor. Nanzhang'da oyunun belleği 2 GB sınırında doluyor ve oyun çöküyordu; 4 GB modu bunu önler." } },
        new Entry { Version = "3.13.3.0", Title = "Wizard / Cleric silah değişimi, Jangan Pass, Grim kutusu",
        Items = new[] { "Yeni: Kervan rotası Jangan - Jangan Pass (iki yönde). Rota, elle yürünüp kaydedilen yolu izler: Jangan'ın güney kapısından Pass'e, 145 nokta, yaklaşık 1700 birim. Yarıda durursa bulunduğu yerden devam eder. Jangan Pass tüccarı olarak yolun sonundaki NPC (model 60850) hazır gelir; başka bir NPC'yi \"Seçili NPC'yi kaydet\" ile değiştirebilirsin.",
        "Yeni: Wizard / Cleric gibi iki silahlı karakterler için otomatik silah değişimi. Wizard büyüleri Wizard asasını (tip 11), Cleric buff ve iyileştirmeleri Cleric asasını (tip 15) ister; bot skill'in istediği silah takılı değilse çantadaki uygun silahı kendisi takar, sonra yine gerekeni geri takar. Skill'ler oyunun tablolarından tanınır. Skill listesindeki satırların Skill ID'si dolu olmalı. Saldırı > Seçenekler > \"Skill başka silah istiyorsa çantadaki uygun silahı tak\" ile kapatılabilir.",
        "Düzeltme: Grim dungeonun en son çıkan Treasure Box'ı kırılmıyordu. Solo 85 kutusunun (model 64835) adı isim tablosunda yoktu (\"Bilinmeyen #64835\"), bot onu kutu diye tanımıyor ve dungeon bitince kırmadan çıkışa gidiyordu. Kutular artık model numarasıyla tanınıyor ve tüm kutu adları tabloya eklendi. Ayrıca dungeon tamamlanınca bot çıkışa gitmeden önce 6 sn kutunun çıkmasını bekliyor; çıkış NPC'sinin önündeyken kutu çıkarsa önce kutuyu kırıyor.",
        "Kaldırıldı: ölüm sayacı ve ölüm sınırı. Karakter kaç kez ölürse ölsün bot durmuyor; ölümde şehre dönüş ve Reverse ile geri gelme aynen çalışır. Koruma sayfasındaki \"Ölüm sınırı\" kutusu ve \"Ölüm sayacını sıfırla\" düğmesi kalktı.",
        "Yeni: Tek bir Grim dungeonu (sıra dışında) bitince de karakter Dungeon Exit ile kendisi çıkıyor. Grim sayfasında \"Bitince Dungeon Exit ile çık\" kutusu var (açık gelir); kapatırsan eskisi gibi çıkış NPC'sinin önünde bekler. Çıkınca çalışma biter.",
        "Düzeltme: Skill'leri kimlikle kullanırken bot, son skill bitmeden ve skill'in kendi bekleme süresi dolmadan saniyede 4-9 komut gönderiyordu; sunucu her birine hata cevabı veriyordu (oyun çökmeden önceki son paketler 110 ms aralıklı hata çiftleriydi). Artık oyunun skill tablosundaki hazırlık / cast süresi ve bekleme süresine uyuluyor (Saldırı > Seçenekler > \"Skill aralığı %\" ile ayarlanır; düşük değer = skill'ler daha sık, arada normal vuruş azalır). Hedef istemeyen skill'ler (imbue, kendine buff) \"Saldırı\" satırına konsa bile hedefsiz ve buff gibi kullanılıyor (\"Invalid target\" hatası bitti)." } },
        new Entry { Version = "3.13.2.0", Title = "Program güncelleme notlarıyla açılır",
        Items = new[] { "Yeni: Program artık Yenilikler sayfasında açılır. Bağlantı kurulmadan önceki boş alan, güncellemeyle gelen değişiklikleri ve önceki sürümlerin notlarını gösterir; sol menünün en üstündeki Yenilikler sayfasından her zaman açılabilir. Güncelleme sonrası açılan ayrı Yenilikler penceresi kalktı.",
        "Düzeltme: Oyunun görüntüsü donması (Grim dahil). Asıl neden bulundu: oyunun kendi ayarı render_in_background=0 (config/display.ini) olduğunda oyun, öndeki pencere olmayınca çizimi tamamen durduruyor (çizim iş parçacığı hiç çalışmıyor, mantık yaklaşık 6 kat yavaşlıyor); karakter ilerlemeye devam ediyor, görüntü donuyor. Koruma > Oyun penceresi'ne \"Arka planda çizimi aç\" düğmesi ve ayarın durumu eklendi; bağlanınca ayar kapalıysa günlüğe uyarı yazılır. Ayarı açtıktan sonra oyunu bir kez yeniden başlat.",
        "Yeni: Grim Crypt sayfası yenilendi. Dünya haritası ve canlı iç görünüm kalktı; işlevleri tabloya geçti: girilen dungeonlar sarı, girilmeyenler beyaz görünür, tabloda her dungeon için en iyi şehirden rota özeti (\"Rota\" sütunu) vardır, tablonun altında sabit boyutlu dungeon görseli ve seçili dungeonun kartı (mod, şehir, giriş, rota, sıradaki yeri) durur. Yol öğretme (işaretle, elle yürüt), yolları kopyala / yapıştır ve son yolu sil düğmeleri tablonun üstüne taşındı.",
        "Yeni: Çalıştırma sırası yeniden düzenlendi: tabloda birden çok dungeon seçilip Ekle ile eklenir, Tümünü ekle girilmemiş olanları ekler, Çıkar listeden tek bir dungeonu siler (çalışan hariç), Girdileri sıfırla sarı işaretleri kaldırır. Biten dungeon listeden silinir ve tabloda sarı olur; liste her zaman henüz çalışmamış olanları gösterir.",
        "Düzeltme: Jangan'dan Donwhang'a ışınlanma bilinmiyordu. Oyunun kapı verisi (teleportlink) botunkinden yeniydi: Jangan'ın Dimensional Gate NPC'si Donwhang'a 5000 altına ışınlıyor, ama bot verisinde bu bağlantı yoktu ve Jangan'dan batıya giderken feribota yürüyordu (rota 8000 birim yürüyüş). Bağlantı eklendi; Jangan → Constantinople rotası artık dört kapıyla yaklaşık 3300 birim yürüyüştür. Oyunun hizmetteki 391 bağlantısının hepsi artık bot verisinde.",
        "Düzeltme: Explorer ve diğer yolculuklarda şehir kapısına yaklaşma. Hotan'ın ışınlanma NPC'si meydanın 34 birim ötesinde, merdivenle çıkılan platformda duruyor; bot oraya yürümeye çalışıp \"rota bulunamadı\" diyordu. Kapının kendi noktasında durmak yetiyor (Hotan meydanından seçim ve ışınlanma canlıda denendi, kabul edildi); NPC kapının hemen yanındaysa eskisi gibi ona yaklaşılır. Oyun kapı NPC'lerini yalnızca yakınındayken yüklüyor (Samarkand'da 43, Donwhang'da 29 birimde göründü, 33'te görünmedi) ve NPC'ler kapı noktasından 31–67 birim ötede, platformlarda duruyor; bot \"NPC okunamadı\" diyordu. Artık NPC görünmüyorsa oyunun bina tablosundaki yerine doğru, görünene kadar kademeli yaklaşır (33, 25, 17, 9 birim); NPC'nin üstündeki platforma çıkmaya çalışmaz. Hotan, Samarkand ve Donwhang kapılarında uzaktan seçim ve ışınlanma canlıda denendi (28–42 birim).",
        "Yeni: Explorer artık bölge değiştirirken (örneğin Constantinople noktaları bitince Samarkand'a) önce Return Scroll ile şehre döner, oradan kapılarla yeni bölgeye gider. Açma/kapama: \"Bölge değişince Return Scroll ile şehre dön\". Scroll yoksa yürüyerek / kapılarla devam eder. Haritadaki \"Tüm harita\" seçeneği kalktı; yalnızca şehirlerin haritaları kaldı.",
        "Düzeltme: Explorer NPC'sine konuşurken bazen boş pencere açılıyor ve bot yine de noktayı \"bulundu\" sayıyordu. Konuşma seçeneği 2 olarak kalmıştı; canlı denemede seçenek 1 NPC'nin penceresini açıyor, 2 hiçbir şey açmıyor. Seçenek 1'e alındı. Bot artık oyun penceresine bakıyor: yazılı pencere görürse bulundu sayar; boş ya da açılmamışsa NPC'ye en çok 3 kez yeniden tıklar, olmazsa noktayı bulunamadı bırakır. \"Seçili NPC ile konuş\" düğmesi de botun yaptığı gibi önce seçer, sonra konuşur ve sonucu durum satırında yazar.",
        "Düzeltme: Grim sırası dungeonu bitirince çıkmıyordu (eski çıkış paketi Dungeon Exit NPC'sinde hiç çalışmıyordu). Artık dungeon bitince Dungeon Exit NPC'si seçilir, konuşma açılır ve penceredeki \"1. Exit Dungeon\" satırına tıklanır (canlıda denendi, karakter dungeondan çıktı). Çıkınca sıradaki dungeon otomatik başlar; aynı bölgedeyse şehre dönülmez. \"Grim Crypt Başlat\" sıra doluyken sırayı çalıştırır. Çıkış sırasında fareye dokunma, oyun öne alınır.",
        "Yeni: Uzak bir dungeon'a (ya da herhangi bir hedefe) giderken oyunun 50 Reverse noktasından hedefe en yakın olanına Reverse Scroll ile ışınlanılır, yürüyüş oradan başlar. Yalnızca yürümekten en az 1200 birim kazandırıyorsa kullanılır; kullanımdan sonra 70 sn bekleme uygulanır, varış doğrulanmadan ikinci scroll kullanılmaz. Koruma sayfasından kapatılabilir.",
        "Düzeltme: Return Scroll yalnızca hedef başka bir bölgedeyse kullanılır; karakter dungeon'un bulunduğu şehirdeyse ya da aynı bölgedeyse hiç scroll kullanılmaz, bulunduğu yerden yürünür. Gidişten önce konum günlüğe yazılır.",
        "Düzeltme: Oyunun 18 Kasım 20:50 güncellemesi (rev 35) GuardyNE.dll'i yenilemişti ve bot “DLL sürümü doğrulanamadı” hatası veriyordu. Gönderim, yürüme, navigasyon, mount ve Grim okuma adresleri yeni DLL'e göre bulundu; rev 35, rev 34 ve 04.11 DLL'leri tanınır. Canlı oyunda karakter, konum ve gönderici doğrulandı.",
        "Düzeltme: Açılışta ilk sayfanın içeriği boş çizilebiliyordu; pencere gösterilirken ilk sayfa artık bir kez baştan yerleştirilir." } },
        new Entry { Version = "3.13.1.0", Title = "Grim'de hiçbir canavar atlanmıyor, Quest'te Göreve Git",
        Items = new[] { "Düzeltme: Grim dungeon'da yanından geçilen canavarlar artık atlanmaz. Bir canavar, işaretli noktalardan uzak da olsa en yakın odaya sayılır ve dövüşülür; eskiden hiçbir odaya sayılmayan canavar ne dövüşülüyor ne de ölümü sayılıyordu, oda ardı ardına süpürülüyordu.",
        "Bot bulunduğu odanın ve sıradaki odanın canavarlarını temizler, geride canlı kalanları da alır; ancak ondan sonraki odalar bekler. Canavar kalmayan oda bir kez süpürülüp bırakılır, tekrar tekrar gezilmez.",
        "Yanıt vermeyen canavar Grim'de 60 sn değil 8 sn atlanır ve yanında canavar varken bot yürüyüp uzaklaşmaz; kısa süre bekleyip yeniden dener.",
        "Yeni: Silkroad temalı arayüz. Deri-kahve zemin, parşömen yazı ve eski altın vurgu; sayfalar Karakter / Av / Yolculuk / Araçlar diye gruplanır, her sayfanın üstünde adı, simgesi ve bağlantı durumu bir kervan yolu üzerinde görünür. Sayfa simgeleri deve, çin parası, fener, kapı, pusula gibi Silkroad’a uygun çizimlerdir. Açılış ekranında kervan Jangan’dan Constantinople’a yürür; vurgu rengi Güncelleme sayfasından değiştirilir (Altın, Yeşim, Lapis, Bakır, Kızıl, Turkuaz, Gümüş).",
        "Yeni: Lottery sayfası. Menü > New Era Lottery penceresindeki kuponu botun kendisi alır: “Fill with quick picks”, “Buy”, altın bitene kadar tekrar. Düğmelerin yerini bir kez kaydedersin (4 sn içinde fareyi düğmenin üstüne götür); her alım envanterdeki altının azalmasıyla doğrulanır, azalmazsa durur. Dokunulmayacak altın ve en çok kupon ayarlanabilir; başlamadan önce kaç kupon / ne kadar altın harcanacağı sorulur.",
        "Düzeltme: Oyunun görüntüsü donuyordu (karakter ilerliyor, ekran aynı kalıyor, Alt+Tab yalnızca bir kare veriyordu). Neden: oyun pencereyi gizleyip tekrar göstermekle çizimi durmuş kalıyordu; pencereyi küçültüp geri açmak uyandırıyor. “Client'ı göster” artık bunu yapar (küçült, geri aç, öne al); Koruma sayfasına “Oyunu canlandır” düğmesi ve “Çizim donarsa otomatik canlandır” seçeneği eklendi: oyun öndeyken karakter hareket ediyor ama görüntü 6 sn aynıysa bot pencereyi kendisi canlandırır.",
        "Yeni: Quest sayfasında bir göreve sağ tıklayınca “Göreve Git” çıkar (çift tık ve “Göreve git” düğmesi de aynı işi yapar): karakter görevi veren NPC'ye yürür. Yalnızca yürür, Return Scroll ve binek kullanmaz; NPC'nin konumu bilinmiyorsa bunu söyler." } },
        new Entry { Version = "3.13.0.0", Title = "phBot düzeninde yeni arayüz",
        Items = new[] { "Arayüz baştan phBot'un düzenine göre yapıldı: solda ikonlu sayfa listesi, ortada üst alt-sekmeler, altta canlı günlük ve sağda 2×3 düğme ızgarası (Bağlan / Oku, Başlat, Yenile, Durdur, Client'ı gizle / göster), en altta bağlantı ve durum şeridi. Renkler, köşeler ve yazı boyutu klasik masaüstü aracı gibi sıkı ve sade.",
        "Sayfalar phBot'un yapısına göre bölündü: İstatistik (Karakter, Çevre), Koruma (İksirler, Berserk, Bakım, Dönüş, Uyarı / Yolculuk), Av Alanı (Hedefler, Alan, Saldırı tuşları, Kısayol), Saldırı (liste, karakterin skilleri, seçenekler, profil, yardım), Envanter, Bildirimler; Grim Crypt, Explorer, Kervan, Nanzhang NPC, Çift Hesap, Güncelleme ve Log yerinde.",
        "Yeni: Koruma > İksirler. HP ya da MP seçilen yüzdenin altına inince çantadaki ilk HP / MP iksiri kullanılır (varsayılan kapalı). Canlı oyunda doğrulandı: kullanılan iksir çantadan düştü.",
        "Yeni: Envanter sayfası çantanın tamamını eşya adı, adet ve kimlikle gösterir (adlar oyunun kendi data klasöründen). Yeni: Bildirimler sayfası botun olaylarını listeler.",
        "Yeni: Grim Crypt'e 18 Solo 85 dungeon'u eklendi (18 Kasım güncellemesi; giriş için seviye 85). Mod düğmesinden seçilir, çalıştırma sırasına Solo Trial'la karışmadan eklenir (D05@85 gibi); iç harita Solo Trial ile aynı zemini kullanır. Canlı denenmedi.",
        "Pencere küçüldü ve kutu düzenine geçti: İstatistik'te Karakter ve Çevre tek sayfada, Koruma ve Av Alanı tek sayfada yan yana kutular. Simge durumuna küçültünce program görev çubuğundan kalkıp bildirim alanındaki gizli simgelere gider; simgeye tıklayınca geri gelir.",
        "Düzeltme: “Client'ı göster” sonrası oyun donup Alt+Tab bekliyordu; oyun penceresi artık gösterilince öne alınır ve çizmeye devam eder. Nanzhang NPC sayfasında şehir dışındaki NPC seçilince oyunun kendi harita görseli ve işaret gösterilir.",
        "Düzeltme: Jangan - Nanzhang kervan rotası Tiger Rim uçurumunda kopuyordu. Rota artık uçurum üstündeki Cliff Warden Lu Feng ile Climbers Camp'e ışınlanır (Nanzhang - Jangan yönünde Ropemaster Gao Shan ile Cliff Top'a); 30000 altın, seviye 75. Işınlanmadan sonra yürüyüş kaldığı yerden devam eder; varış, kat farkı yüzünden yükseklikle doğrulanır.",
        "Yeni: Nanzhang NPC sayfası artık yalnızca şehri değil Nanzhang haritasındaki tüm NPC'leri listeler: oyunun kendi NPC tablosundan gelenler (harita Nanzhang, Mossgate, Dugu Manor, Blightwood, Venom Fen, Hollow Hive Ridge / Kuhe, Old Mining Camp, Forest of Whispers, Gloom Swamps dahil tüm bölgeleri kapsar; konumu tabloda olmayanlar “konum yok” diye listelenir) ve gezerken görülen yeniler (★, kapatılabilir). NPC seçince görev sayısı da görünür.",
        "Yeni: Quest sayfası. Nanzhang NPC'lerinin görevleri oyunun kendi görev tablolarından okunur: görev adı, seviye, yapılacak iş, ödül (tecrübe, SP, altın, eşya) ve NPC'nin konuşma metni. Arama ve “yalnız ödüllü” süzgeci vardır, liste metin dosyasına aktarılabilir. Oyunda gezmek gerekmez.",
        "Pencere başlığı phBot gibi: sürüm, karakter ve bağlantı durumu. Av / Ayarlar sayfasındaki gecikme ve pencere boyutu değişimindeki yavaşlık giderildi." } },
        new Entry { Version = "3.12.1.0", Title = "Oyunun 18 Kasım güncellemesine uyum",
        Items = new[] { "Oyun güncellemesiyle GuardyNE.dll yenilendi (rev 34); gönderim, yürüme, navigasyon, mount ve Grim okuma adresleri yeni DLL'e göre bulundu. Eski DLL sürümü de tanınmaya devam eder.",
        "Canlı oyunda doğrulandı: karakter okuma, yürüme, yol sorgusu, skill listesi okuma, mount durumu. Bilinmeyen bir DLL sürümü gelirse bot bunu açıkça bildirir.",
        "Not: Grim Crypt içi ve mount'a binme / inme çağrısı yeni DLL'de henüz canlı denenmedi (imzalar ve veri düzeni doğrulandı).",
        "Kervan rotası: Samarkand - Constantinople (iki yön) ve Jangan - Nanzhang (iki yön) eklendi. Jangan - Nanzhang, haritada işaretlenen karayolunu izler (Jangan Pass üzerinden Nanzhang'ın doğu kapısına), ışınlanma kullanılmaz. Yeni şehirlerin tüccarları için oyunda NPC'ye tıklayıp “Seçili NPC'yi kaydet”e bas. Yol planı hesaplandı, gerçek yürüyüşte henüz denenmedi.",
        "Kervan hayvanı (Buffalo, model 23909) normal mount'tan ayrıldı: binili iken bot mount / inme komutu göndermez, kervan rotasında ona binili yürür. Kervan sayfasından binili hayvan kervan hayvanı olarak eklenebilir. Hayvanı çağırıp bindirme yapılmadı.",
        "Av / Ayarlar sayfası pencere boyutu değişirken artık çok daha hızlı yerleşiyor (adım başına yaklaşık 3 sn yerine 0,15 sn).",
        "Daha yumuşak tasarım: açık kenarlıklar, slate metin rengi, daha yuvarlak köşeler ve kartlarda çok hafif gölge." } },
        new Entry { Version = "3.12.0.0", Title = "Skill listesi okunuyor, zorunlu güncelleme ve yeni Kervan sayfası",
        Items = new[] { "“Skilleri oku” artık karakterin öğrendiği skill’leri gerçekten buluyor (canlı oyunda doğrulandı: 66 skill, adı ve seviyesiyle). İpucu gerekmez; yaklaşık 6 saniye sürer.",
        "Skill’i kimliğiyle kullanma canlı oyunda doğrulandı: tuşsuz satır skill’i istemciye kimlikle bildirir, buff gelir, mana düşer; oyun arkada ya da gizliyken de çalışır.",
        "Arka plan tuşu: ölçüldü, bu istemci klavye mesajını yalnız oyun öndeyken işliyor. Arka planda çalışmak için skill’leri listeden seçip tuşsuz ekle. “Arka plan tuşunu sına” tuşlu satırı kendisi seçer, tuşsuz satırda “Kimlikle sına”ya yönlendirir.",
        "Explorer: “Seçili NPC ile konuş” düğmesi eklendi; seçenek numarasını oyunda elle denemek içindir.",
        "Güncelleme: yeni sürüm 15 saniyede bir aranır ve bulunursa kapatılamaz biçimde kurulur. Ekranın üstünde kırmızı KRİTİK GÜNCELLEME uyarısı çıkar, geri sayımdan sonra uygulama kendini günceller.",
        "Kervan sayfası yeniden düzenlendi: boşluk kalktı, kart pencereye göre genişler, rotalar üçerli sıralanır. Tüm kart sayfaları pencereyle birlikte genişler." } },
        new Entry { Version = "3.11.0.0", Title = "Skill seçimi, Explorer konuşması ve yeni Dungeon düzeni",
        Items = new[] { "Skiller sayfasına “Karakterin skilleri” bölümü eklendi: “Skilleri oku” karakterin öğrendiği skill’leri oyun belleğinden bulur; adı, seviyesi ve Skill ID’siyle listelenir. Bulunamazsa “Tüm skiller” ile oyunun skill tablosunda ada göre aranır.",
        "Seçilen skill “Listeye ekle” ile tuşsuz eklenir. Tuşu boş, Skill ID’si dolu satırı bot skill’i kimliğiyle kullanır; hotbar tuşu gerekmez ve oyunun önde olması gerekmez. Tuşlu satırlar eskisi gibi tuşa basar. “Kimlikle sına” düğmesi bir skill’i bir kez gönderip mana ve buff değişimine bakar.",
        "Explorer: NPC seçilip onaylanınca bot artık NPC ile konuşur ve konuşma seçeneğini seçer (varsayılan 1. seçenek; “NPC’ye tıkla ve konuşma seçeneğini seç” ve “Seçenek no” ayarları). Nokta, konuşma isteği gönderildikten sonra bulundu sayılır.",
        "Grim Crypt sayfası iki ayrı bölüme ayrıldı: üstte dungeon listesi ile çalıştırma sırası yan yana, altta tam genişlikte Dungeon görseli, Canlı iç görünüm ve Dünya haritası.",
        "Not: skill listesinin bellekte bulunması ve skill / NPC konuşma isteklerinin oyunda kabul edilmesi gerçek oyunla henüz doğrulanmadı." } },
        new Entry { Version = "3.10.0.0", Title = "Skiller sayfası, buff takibi ve Grim'de durmayan yürüyüş",
        Items = new[] { "Yeni Skiller sayfası (phBot'un Attack / Buffs sekmelerindeki özelliklere göre): her satırın adı, tuşu, türü, Skill ID'si, hedef türü, en az mob sayısı, süresi ve HP/MP yüzdesi vardır.",
        "Buff takibi: Skill ID'si olan buff'ın açık olup olmadığı karakterin gerçek buff listesinden okunur; buff bitince tuş basılır, açıkken basılmaz. “Buff ID’sini öğren” düğmesi ID'yi kendisi bulur: satırı seç, düğmeye bas, oyunda buff'ı kullan.",
        "Acil buff: can yüzdesi sınırın altına inince ya da etraftaki canavar sayısı sınıra ulaşınca basılır. İyileştirme ve mana önceliği: acil, iyileştirme, mana, buff, saldırı.",
        "Saldırı satırları hedef türüne (General, Champion, Giant, Unique ...) ve yakındaki mob sayısına göre seçilir; türe özel skill hazır değilse genel liste kullanılır. Yeni hedefte sıra baştan başlar.",
        "Dirilince ve yeni bölgeye girince buff'lar yeniden basılır. “Saldırırken de buff yenile”, “Yalnız buff / iyileştirme yap” (Cleric, Bard) seçenekleri eklendi.",
        "Profiller: skill listesi adla kaydedilir, yüklenir; üç hazır şablon eklendi (savunma buff seti, alan hasarı, yalnız buff).",
        "Arka plan tuşları: “Oyun önde değilken de tuşları gönder” açılırsa tuşlar oyun penceresine Windows mesajıyla gider; av, Grim Crypt, buff ve iyileştirme oyun arkadayken de çalışır. “Arka plan tuşunu sına” düğmesi istemcinin bunu kabul edip etmediğini mana ve buff değişimine bakarak doğrular; kabul etmiyorsa seçeneği kendisi kapatır.",
        "Grim Crypt: ilk odadaki canavarlar bitince “yol bulunamadı” diye duran bot artık durmaz. Rota bulunamazsa önce doğrulanmış zemin hücreleri üzerinden gider, olmazsa o noktayı atlayıp sıradakine geçer, kısa bekleyip yeniden dener." } },
        new Entry { Version = "3.9.0.0", Title = "Kervan feribotu, arka planda yürüyüş ve hız",
        Items = new[] { "Kervan rotaları: Hotan, Jangan ve Donwhang arasında altı rota. Nehir geçişlerinde en uygun feribot geçidi seçilir ve karakter feribot NPC’sine kendisi tıklayıp karşıya geçer; teleport kullanılmaz.",
        "Jangan'a giren ya da çıkan rotalar surun güney kapısındaki kemerden düz geçer; karakter şehrin neresinde olursa olsun doğu kapıdan dolanmaz.",
        "Tüm NPC'ler artık okunur: “Seçili NPC’yi kaydet” tüccarları kaydeder, Explorer NPC’si bulunur. Hotan şehri (Hotan Kingdom sektörü) tüccar kaydında tanınır.",
        "Yürüyüş işleri (kervan, Explorer, dungeon yolu) oyun arka plandayken de çalışır (Ayarlar > Yolculuk). Av için oyunun önde olması gerekir, çünkü saldırı tuşları klavye ile gönderilir.",
        "İş bitince bildirim: kervan ya da Explorer bitince ses, balon ve görev çubuğu uyarısı.",
        "EXP çubuğu düzeltildi: seviye 85-89 için gereken EXP oyunun kendi tablosundan alınır; çubuk oyundaki yüzdeyle aynıdır.",
        "Hız: yol arama oyuna sorguları toplu gönderir (turdaki en uzun bekleme 111-162 ms yerine 35-52 ms, oyunda 8 kat az iş parçacığı); Explorer ve Dünya haritaları tur başına 101 / 81 ms yerine 12 / 17 ms çizilir; iş çalışırken bot oyunu 100 ms'de bir okur." } },
        new Entry { Version = "3.8.1.0", Title = "Açılış ekranı ve hızlı sekme geçişi",
        Items = new[] { "Yeni açılış ekranı: dosyalar denetlenir, ayarlar okunur, oyun verileri ve yol ağı yüklenir, pencere kurulur; ilerleme çubuğu adım adım gösterilir. Eksik dosya varsa adıyla bildirilir.",
        "Av / Ayarlar sayfasına dönerken yaşanan donma giderildi: sayfa açılırken yüzlerce ayrı yerleşim hesabı yapılıyordu, artık düzen bir kez hesaplanır (3-5 sn yerine saniyenin onda biri).",
        "Pencerenin ilk açılışı da aynı şekilde hızlandı." } },
        new Entry { Version = "3.8.0.0", Title = "Explorer 1 → 52, skill listesi ve Nanzhang şehir tanıma",
        Items = new[] { "Explorer Yerleri sayfası yenilendi: 52 nokta listesi, bölge bölge yakınlaştırılan harita (Constantinople, Samarkand, Roc, Hotan, Donwhang, Jangan, Nanzhang) ve karakterin konumu.",
        "1 → 52 otomatik: bot sırayla her noktaya yürür, Explorer NPC’yi (Explorer Gisaeng Ahjin) oyunun nesne listesinde canlı bulur, bir kez tıklar ve seçim doğrulanınca sıradakine geçer. Bulunanlar kaydedilir, “Bulunanları atla” ile kaldığı yerden devam edilir.",
        "Haritadaki konumlar yaklaşıktır: NPC yakına gelince canlı bulunur; görünmezse noktanın çevresinde iki halka dolaşılır, yine yoksa nokta “Bulunamadı” olur ve sıra devam eder. NPC modeli sayfadan değiştirilebilir.",
        "Av / Ayarlar sayfasında yeni Skill listesi: her satır bir tuş ve türü (Saldırı, Buff, İyileştirme, Mana). Saldırılar sırayla basılır, buff süresi dolunca basılır, iyileştirme ve mana HP / MP yüzdesi sınırın altına inince basılır. F1-F12 tuşları da kullanılabilir.",
        "Skill listesi kapalıysa eski Tuşlar kutusu aynen çalışır.",
        "Nanzhang'ın altı bölgesi artık şehir olarak tanınır: Nanzhang'da ölünce şehre dönüş doğrulaması takılmaz, yolculuk planı şehir içinden başlar.",
        "Oyun arşivlerinden (.ne) NPC konumları, teleport ve canavar tanımları çıkarıldı; çıkarma aracı tools/nav-export içindedir." } },
        new Entry { Version = "3.7.0.0", Title = "Kervan rotası ve sadeleşen Grim Crypt",
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
        BackColor = Ui.Card;
        ForeColor = Ui.Ink;
        Font = Ui.Body;

        var head = new Label { Text = "Güncellendi: sürüm " + version, Font = Ui.Title,
            ForeColor = Ui.Accent, Left = 18, Top = 14, Width = 600, Height = 32 };
        var notes = new RichTextBox { Left = 20, Top = 54, Width = 600, Height = 480, ReadOnly = true, BorderStyle =
            BorderStyle.None, BackColor = Ui.Card, ForeColor = Ui.Ink, DetectUrls = false, TabStop = false };
        var bold = Ui.Heading;
        var normal = Ui.Body;
        foreach (var e in ReleaseNotes.All)
        {
            notes.SelectionFont = bold;
            notes.SelectionColor = e.Version == version ? Ui.Accent : Ui.Ink;
            notes.AppendText(e.Version + " · " + e.Title + (e.Version == version ? "  (bu sürüm)" : "") + "\n");
            notes.SelectionFont = normal;
            notes.SelectionColor = Ui.Ink;
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
    // The "Yenilikler" button on the update page goes to the page the window opens on.

    void ShowWhatsNewNow()
    {
        ((FlatTabs)tabs).Choose(homeTab);
    }

    // The page itself is the first thing shown after an update; only the version that was seen is remembered.

    void ShowWhatsNew()
    {
        try
        {
            string version = ReleaseNotes.CurrentVersion;
            if (settings.LastSeenVersion != version)
            {
                settings.LastSeenVersion = version;
                settings.Save();
            }
        }
        catch { }
    }
}

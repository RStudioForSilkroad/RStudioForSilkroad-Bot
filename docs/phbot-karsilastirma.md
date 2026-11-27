# phBot ile karşılaştırma (3.13.0.0)

Kaynak: [phBot Guide](https://guide.phbot.org/phbot) (31 sayfa okundu). phBot sunucuya paket enjekte eden, istemcisiz çalışabilen bir bottur; Silkroad R Studio oyunun belleğini okur ve istemcinin kendi işlevlerini çağırır. Bu yüzden paket tabanlı sayfalar (parti, lonca, takas, simya) alınmadı; belleğin gösterebildiği ve istemci işleviyle yapılabilen her şey phBot düzenine taşındı.

## Arayüz düzeni

| phBot | Silkroad R Studio |
|---|---|
| Solda ikonlu dikey sayfa listesi | Solda ikonlu sayfa listesi (üstünde oyun seçici) |
| Sayfa içinde üst alt-sekmeler (Attack / Buffs / Party ...) | Aynı: Saldırı, Koruma, Av Alanı, İstatistik, Envanter, Bildirimler alt-sekmelidir |
| Altta canlı günlük, sağda Launch / Start Bot / Go Clientless / Stop Bot / Hide Client / Return Scroll | Altta canlı günlük, sağda 2×3 düğme: Bağlan / Oku, Başlat, Yenile, Durdur, Client'ı gizle, Client'ı göster |
| Pencere başlığında sürüm, karakter, bağlantı | Aynı biçim |
| Gri klasik masaüstü görünümü, ince çerçeveli gruplar | Gri tuval, beyaz çalışma alanı, ince gri çerçeveli grup kutuları, 9 pt yazı, 3 px köşe |

## Sayfa karşılıkları

| phBot sayfası | Bizde | Durum |
|---|---|---|
| Statistics | İstatistik > Karakter, Çevre | Var: can / mana / EXP çubukları, sayaçlar, yakındaki canavarlar |
| Auto Configure | Saldırı > Profil / şablon | Kısmen: skill profilleri ve üç şablon |
| Notifications | Bildirimler | Var: bot olayları listesi; bildirim seçenekleri Koruma > Uyarı / Yolculuk |
| Protection > Potions | Koruma > İksirler | Yeni: HP / MP eşiği, çantadaki ilk iksir, 1,5 sn bekleme |
| Protection > Return | Koruma > Dönüş | Kısmen: ölümde şehre dön + Reverse, ölüm sınırı. Eşya bitince / çanta dolunca dönme yok |
| Protection > Berserk | Koruma > Berserk | Var |
| Protection > Monster Preferences | Av Alanı > Hedefler | Kısmen: hedef listesi; saldırı skill'leri hedef türüne göre seçilir |
| Town | — | Yok: şehir döngüsü (alım, depo) paket tabanlı |
| Training Area | Av Alanı > Alan | Var: merkez, yarıçap, takılma süresi; yol bulma Grim / Explorer / Kervan'da |
| Script | Kervan Rotası > Script dosyası | Kısmen: walk,X,Y,Z betikleri yazılır |
| Attack | Saldırı > Saldırı listesi | Var: sırayla vur, hedef türüne göre liste, en az mob sayısı, alt skill kullanımı |
| Attack > Buffs | Saldırı listesi (Buff, Acil, İyileştirme, Mana) | Var: buff listesi gerçek buff listesinden izlenir, acil buff, saldırırken buff |
| Attack > Party buffs, Resurrect, Healing (parti) | — | Yok: parti paketleri gerekir. Kendi iyileştirme satırı var |
| Pet, Party, Union Party, Guild, Academy, Players, Chat, Quest, Stall, Trade, Alchemy, Mastery | — | Yok: paket tabanlı |
| Pick Filter | — | Yok: yerden alma istemcinin kendi makrosundadır |
| Inventory | Envanter | Yeni: çantanın tamamı, eşya adı ve adet (adlar oyunun data klasöründen) |
| Map | Grim Crypt > Dünya haritası, Explorer, Nanzhang NPC | Var: oyunun kendi harita karolarıyla |
| Key Bindings | Av Alanı > Kısayol | Var: başlat / durdur kısayolu |
| Plugins | — | Planlı: Python eklenti ana bilgisayarı |

Bu botta olup phBot'ta olmayanlar: Grim Crypt dungeon otomasyonu (72 dungeon, sıra), Explorer (52 NPC), kervan rotaları (yol ağında, feribotlu), Nanzhang harita ve NPC verisi, çift hesap denemesi, otomatik güncelleme (15 sn'de bir, zorunlu).

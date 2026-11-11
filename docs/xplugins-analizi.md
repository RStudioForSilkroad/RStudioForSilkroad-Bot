# phBot-xPlugins analizi

Kaynak: [JellyBitz/phBot-xPlugins](https://github.com/JellyBitz/phBot-xPlugins) (22 eklenti). Hepsi phBot'un Python eklenti arayüzü için yazılmıştır (`inject_joymax`, `get_npcs`, `get_monsters`, `QtBind`). Depoda lisans dosyası yoktur; bu yüzden kod kopyalanmadı, yalnızca fikirler Silkroad R Studio'nun kendi yapısına (C#, bellek okuma) uyarlandı.

| Eklenti | Ne yapar | Karar |
|---|---|---|
| xMobSelector | Yakındaki canavarları listeler, tür (unique, titan…) seçip otomatik hedefler | **Uyarlandı:** unique uyarısı (Av / Ayarlar → Otomatik bakım). Otomatik hedefleme zaten botun hedef seçiminde var. |
| xNPC | Yakındaki NPC'leri listeler | **Uyarlandı:** oyunda seçili NPC'yi tüccar olarak kaydetme (Kervan Rotası sayfası). |
| xKervanKey | JOYCONTROL oyuncusunun sorduğu bot kontrolünü (anti-bot sorusu) siteden bulduğu anahtarla yanıtlar | **Alınmadı.** Sunucunun bot denetimini otomatik geçmek içindir; bu projeye eklenmez. |
| xAutoDungeon | Forgotten World, Water Temple gibi dungeonlar için script | Bot kendi Grim Crypt otomasyonuna sahip; diğer dungeonlar paket enjeksiyonu ister, eklenmedi. |
| xScriptHelper | Script komutları (depodan eşya al, script / profil değiştir) | Paket enjeksiyonu ve phBot veritabanına bağlı; eklenmedi. |
| JellyDix | phBot olaylarını Discord'a bildirir | Mümkün (webhook ile); sonraki sürüm için aday. |
| xChat, xControl, xTargetSupport, xTranslator | Sohbet, parti yönetimi, ortak hedef, çeviri | Parti / sohbet paketleri gerekir; botun kapsamı dışında. |
| xAcademy, xAutoConfig, xLoginController, xMatchLocker, xPluginUpdater | Hesap, karakter ve eklenti yönetimi | phBot'a özgü; karşılığı yok. |
| xArena, xBattleInfinity, xCarnivalBalloon, xTrivia | Etkinlik otomasyonları | Paket tabanlı; eklenmedi. |
| xAvatarTester, xChanger, xPacketTool | Avatar önizleme, paket araçları | Paket enjeksiyonu; bot paket göndermez, bellek okur. |

Bu botun yaklaşımı: oyun belleğini okur ve istemci işlevlerini çağırır; sunucuya ham paket göndermez. Bu yüzden paket enjeksiyonuna dayanan eklentiler olduğu gibi taşınamaz.

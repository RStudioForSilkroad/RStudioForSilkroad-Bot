# Veri modeli (data/)

Bot, oyunun kendi tablolarından türeyen veriyle yolculuk eder. Bu dosya her veri dosyasının kaynağını, kurallarını ve nasıl yeniden üretileceğini
söyler. Amaç: veri bir kez elle üretilip unutulmasın; oyun güncellenince fark **canlı denemeyle değil, komutla** bulunsun.

## Oyun tablolarından üretilenler (tekrar üretilebilir)

| Dosya | Kaynak tablo (Media.ne → server_dep/silkroad/textdata) | İçerik |
|---|---|---|
| `transport-network.json` | `teleportdata.txt`, `teleportlink.txt` | `Gates` (kapılar: Id, Code, Model, Region, X, Y, Height, World) ve `Links` (kapıdan kapıya bağlantı: From, To, Fee, Level, Restrictions) |
| `gate-npcs.json` | `teleportbuilding.txt` | Kapı NPC'sinin durduğu yer (Model → Region, X, Y, Height). NPC çoğu zaman varış noktasından 28–67 birim uzaktadır |
| `reverse-points.json` | `refoptionalteleport.txt`, `textdata_object.txt` | Reverse Return Scroll'un götürdüğü 50 nokta (Id, Name/Turkish/Korean, Region, X, Y) |
| `skill-weapons.json` | `skilldata_*.txt` (sütun 50) | Skill'in yalnız belirli bir silahla kullanılabildiği silah tipi: `{"11": [skill id'leri]}`. 255 (her silah) yazılmaz. Tipler: 10 Warlock asası, 11 Wizard asası, 12 Crossbow, 13 Dagger, 14 Harp, 15 Cleric asası |
| `weapon-types.json` | `itemdata_*.txt` (tip 3/1/6, sütun 12) | Her silahın silah tipi, aynı biçimde gruplu. Skill ve silah tipi eşleşince silah değiştirmeye gerek kalmaz |

Kurallar:

- Dünya koordinatı: `X = (sx-135)*192 + yerelX/10`, `Y = (sy-92)*192 + yerelY/10`; `sx = region & 0xff`, `sy = region >> 8` (negatif/instance bölgelerde işaretli).
- `Links` yalnızca **hizmette olan** satırları taşır (`teleportlink` ilk sütun = 1). Kapalı satırlar ve GM tarafı 1 altınlık bağlantılar yolcunun kullanabileceği şeyler değildir.
- `Level` yalnızca satırda seviye şartı işaretliyse (sütun 7 = 1) doludur; yoksa 0.
- Her `Link.From` ve `Link.To` bir `Gates` kaydına çıkmak zorundadır (testle korunur).
- Beş büyük şehir kapısı (Id 1 Jangan, 2 Donwhang, 5 Hotan, 25 Samarkand, 20 Constantinople) için NPC yeri bulunmak zorundadır.
- 39 feribot/uçan gemi kapısının `teleportbuilding` kaydı yoktur; bunlar için NPC yeri iddia edilmez, bot gerçek NPC'yi canlı okur.

### Oyun güncellenince

1. Güncel tabloları kendi çıkarıcınla bir klasöre çıkar (`teleportdata`, `teleportlink`, `teleportbuilding`, `refoptionalteleport`, `textdata_object`, `skilldata*`, `itemdata*`; UTF-16). Arşiv anahtarları **depoya girmez**.
2. Farkı gör (hiçbir şeyi değiştirmez; fark varsa çıkış kodu 1):

```powershell
python tools/game-data/build_data.py --tables <klasör> --check
```

3. Fark gerçekse dosyaları yeniden yaz ve testleri çalıştır:

```powershell
python tools/game-data/build_data.py --tables <klasör> --write
powershell -NoProfile -ExecutionPolicy Bypass -File tools/run-tests.ps1
```

Son doğrulama: GuardyNE rev 35 ile oyun tabloları, üç dosya da tabloyla birebir aynı.

## Elle bakımı yapılanlar

| Dosya | Not |
|---|---|
| `grim-locations.json` | Grim dungeon girişleri; her kaydın `GateId`'si `transport-network.json`'da olmalı, Solo dungeonların `GATE_NE_GRIM_<kod>_S` kapısına bağlantısı olmalı (testle korunur) |
| `level-exp.json` | Seviye/EXP tablosu (`leveldata`) |
| `return-scrolls.json`, `town-regions.json` | Scroll ve şehir bölgeleri; oyun güncellemesinde `itemdata` / `refregion` ile karşılaştırılmalı |
| `caravan-scripts.json` | phBot kervan scriptlerinden (`PHBOT Caravan SC`) alınan hazır kervan yolları: `Scripts` (From, To, Source, Steps = `walk,X,Y,Z` / `wait,ms` / `teleport,<NPC adı>,<NPC adı>`) ve `Names` (feribot NPC adı → `GATE_NPC_*` kodu). Yalnız botun rotaları alınır (Hotan - Jangan, Hotan - Donwhang, Jangan - Donwhang iki yönde, Constantinople → Samarkand); sohbet/ticaret/binek komutları atılır. Yeniden üretmek: `python tools/game-data/import_phbot_caravan.py <script klasörü> build/game-tables/tables`. Her `teleport` ağdaki yönlü bir bağlantı olmak zorundadır (içe aktarma ve test doğrular) |
| `explorer-spots.json`, `learned-defaults.txt`, `Grim-Haritalari/` | Oyuncunun/geliştiricinin kaydettiği noktalar ve haritalar |

## Testle korunan değişmezler (`tests/V3Checks.cs`)

- Her bağlantı iki bilinen kapıyı birleştirir.
- Her şehir kapısının NPC yeri vardır.
- Her dungeonun kapısı bilinir; Solo dungeonların Solo kapısına bağlantısı vardır.
- Reverse noktalarının numaraları benzersizdir.
- Beş büyük şehir arasındaki her rota yalnızca kapılarla gider (en az bir sıçrama, yürüyüş ≤ 700 birim); rapor `tests/Route-Report.txt`'e yazılır. Eksik bir bağlantı (örneğin eskiden eksik olan Jangan → Donwhang) burada uzun yürüyüş olarak görünür.

# Yol ağı üretimi (geliştirici notu)

Oyunun `.ne` arşivlerinden (JoyMax PK2) yol ağı verisi çıkarılır. Oyuncuların yapması gereken bir şey yoktur; sonuç `data\road-mesh.bin.gz` ve `data\road-surface.bin.gz` içindedir.

1. `CaptureKeyTables.cs` — oyun açıkken istemcinin belleğinden arşiv dizininin Blowfish tablolarını bulur (salt okuma). Arşiv anahtarı bilinmediği için bu yol kullanıldı.
2. `Pk2Dump.cs` — tabloyla dizini çözüp `index-Data.tsv`, `index-Map.tsv`, `index-Media.tsv` yazar (yol, konum, boyut).
3. `RoadSurface.cs` — dünya haritasından yol ve su katmanlarını, nvm dosyalarından nesne katmanını üretir (`road-surface.bin.gz`).
4. `NavExport.cs` + `NavData.cs` — arazi ağına nesneleri ekler (merdiven, köprü, kapı yüzeyleri; duvarlar için pahalı kenarlar) ve `road-mesh.bin.gz` (RD02) üretir. Girdi: arazi-only `road-mesh.bin.gz` (RD01).

5. `Pk2Extract.cs` — aynı dizinle arşivden dosya çıkarır (örn. `server_dep\silkroad\textdata\npcpos.txt`, `characterdata*.txt`, `teleportdata.txt`). Dosya içerikleri şifresizdir; yalnızca dizin şifrelidir.
pcpos.txt`, `characterdata*.txt`, `teleportdata.txt`). Dosya içerikleri şifresizdir; yalnızca dizin şifrelidir.

Kaynak biçim bilgisi: açık kaynak RSBot NavMeshApi (arazi hücreleri, nesne BMS yürüme ağı, kenar bayrakları).

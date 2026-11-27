# FIX-REPORT (NewEra-fix, based on 3.13.9.0)

Tests: `tools\build.ps1` OK, `tools\run-tests.ps1` exit 0, 696 PASS / 0 FAIL (the 16-page assertion is untouched). Nothing was launched except the test exe and a read-only data harness. Each edited file keeps its own line ending (correction: Stability, MemoryCoreV3, AutomationV3, RoadMesh, NavigationChecks, GrimSurveyChecks, FloorBatchChecks are LF; V3App, Expedition, FloorNavigation, V3Checks are CRLF; an earlier grep-based check wrongly said all CRLF). `fix.patch` = diff of 5 files vs NewEra-main. Version files not touched.

## Defect B - false death after Reverse (fixed)

Fact: X -25920 / Y -17664 is exactly Region 0 with LocalX = LocalY = 0 (`((0&255)-135)*192`, `((0>>8)-92)*192`, MemoryCoreV3.cs:380-381). That is the loading-screen read; the alive flag (+0x65c) is 0 there. ReverseTravel.cs:137 already knows "the loading screen reads as region 0". `DeathRecovery.Observe` did not.

Change: `src/AutomationV3.cs:171` now requires `PositionReal(s.Player)` (new, AutomationV3.cs:254: `p != null && p.Region != 0`) before latching a death. A real death is seen on the first read with a real position (same tick, no added delay). Stability.cs `GameStateReady` / `AreaWatch` were deliberately not used: they would delay a real death by up to 5 s (SettleMs). Tests: V3Checks.cs:1036-1045 (3 checks).

## Defect A - D10 route (NOT fully fixable offline)

### Facts (offline harness reading the embedded data only)
- Start Reverse "Garden of Gods (3)": region 25672, X -11933.9 Y 1621.1. Goal (catalog, Height 1313): X -12389 Y 753, region 24390.
- Plan from catalog data = 52 steps, 0 hops, 1640 units. Steps 1-35 match the walked log exactly (-11920,1584 ... -12368,1073 ... -12411.7,1032 ... -12433,1000 -> -12433,945 = cell 308064).
- Then the mesh crosses a **soft-wall edge** 308064 -> forecourt cell 308059 (-12432,882, h 860.9, 96x96). EVERY terrain link into 308059 (7 of them, from 308064, 308065, 308060, 308076, 308078, 307956, 307957) is a soft wall (edge crosses a building outline, cost x40). The stairs are object triangles: 1370044 (-12384.5,869.6, h 863) -> 1370043 (-12394,856, h 953) -> 1370015 (-12385,843, h 1039) -> 1370016/17 (-12375,829 / -12394,829, h 1124) -> 1369985 (-12388,797, h 1132) -> 1369904/1369897/1369906 (-12390,791 / -12389,784 / -12389,777, h 1181/1228/1275) -> 1368747/48 (-12400,768 / -12377,766, h 1290/1301) -> plateau (h 1312.6) -> goal cell 1368396 (-12385,753). This matches the user's stairs at X -12390 Y 766. The ONLY entries into the stair/colonnade object triangles come from 308059 (via 1370044, 1370067, 1370068). So the real gate/opening of the temple compound is not in the mesh.
- The log's 38-step plan (tail -12433,945 -> **-12498,927 (cell 307957) -> -12498,865 (307956)** -> "Giriş" goal) is reproduced exactly only when 308059 (or any single stair/plateau cell: 1370044, 1370043, 1370017, 1368747, 1368748, 1368647, 1368396) is excluded, or when the goal height is 0. Then the planner returns a dead-end "live approach" (gap 156 from -12498,865), no exception. Round coordinates (-12498.0, 927.0) in the log are that mesh waypoint. Why the live run had that plan is NOT proven (settings.json BlockedRoadCells is empty on disk, events.log was not written after 20:06 so I could not check earlier runs).
- The stall: character reached (-12498,927) (arrival 5, stands about (-12495.1,928.4)); the live grid walker's first leg south to (-12495.1,900.4) never moved (7 identical moves "kabul=True"), then the rollback to (-12495.1,928.4) also repeated 4x without arriving. The mesh shows no wall on 307957 -> 307956 (non-soft edge, object share 1-2), so the blocker is not in the data; the client's walkability query said open.
- Walker recovery (ObstacleNavigation.cs:483-507): stall = 4 s without 0.5 unit progress; then rollback (up to 6 s) or re-search; up to 6 replans; then Failed -> `TryReplanRoad` bans the head mesh cell (max 4, persisted in settings.BlockedRoadCells for ever) and replans. Total before a verdict is about 30-45 s, so the user's stop 4 s after the first block was far too early to judge. Simulation (scratchpad Walk.cs): with an optimistic query and a physical wall, even a 50-unit-wide wall with open ground 25 units away is never bypassed (6 replans, fail at 31 s), because each block only marks a 2-unit radius at the middle of the 28-unit leg. I tried blocking directly in front of the character: no improvement, reverted (not justified).
- Hole found: persisted bans are only cleared when planning THROWS. A banned stair cell does not throw, it silently degrades to the dead-end live approach above, for all future runs.

### Changes (recovery only; no coordinates invented)
- `src/RoadMesh.cs:417,554` new `LastLiveApproach` flag set by every `Plan`.
- `src/Expedition.cs:377-400` (`PlanRoad`): logs "Harita portala bağlı değil; son kısım canlı yürünecek · son bağlı nokta ... · boşluk ..." whenever the plan ends in a live approach (this was invisible before), and if persisted bans exist and the same plan WITHOUT them is fully mapped, clears the bans (like the existing valve) and uses it. Skipped for caravans.
- Tests: NavigationChecks.cs:149-160 (D10 mapped via stairs; closing cell 308059 gives a flagged live approach; flag resets). These hard-code cell 308059: regenerate the mesh -> revisit.

### Unverified live
Whether the temple can be entered from 308064/(-12433,945) at all, what blocks (-12495,928) -> south, where the real opening is, and whether the earlier run had bans in memory.

### How to record the learned path (needed to really fix D10)
1. Open Grim page, tick "Yol öğret" ONLY after standing at the Reverse arrival (use Reverse Garden of Gods (3) by hand, wait for load).
2. Walk by hand the real way: arrival -> forecourt -> stairs (X -12390 Y 766) -> portal circle X -12389 Y 753, and stay on it a second. Keep the path continuous (points every 6 units).
3. Untick. Log shows "Yol öğrenildi · N nokta"; "Yolları kopyala" puts the line in the clipboard (settings.json -> LearnedPaths). Send me that line.
4. Add it as one line to `data\learned-defaults.txt` (`x,y,h;x,y,h;...`); `ChooseLearned` (WorldMap.cs:281) takes it when 0.85 x length + gaps < mesh cost (about 1640 here, so a path of up to about 1900 units wins; it must start within 140 units of the arrival or be planned to).
Optional: also record from the forecourt (-12432,882) alone; chains join if ends are within 90 units.

Harness (not shipped): scratchpad `...\scratchpad\h\{Harness,Plan,Walk}.cs`, built with `tools\common.ps1` Invoke-Csc.

# Package 3.13.10.0 (batched floor probes, graceful game exit, stable death)

Build OK, run-tests exit 0, 748 PASS / 0 FAIL. fix.patch = all differences src/data/tools/tests vs NewEra-main (3.13.10.0), generated files and Preview-*.png excluded. Not verified live: Grim floor walking in a real dungeon, game exit under a remote call, instance-load blip.

## Task 1 - Grim floor probes in batches
- `FloorNavigation.cs`: `Advance`, `ObservePosition`, `RouteToUncovered` take an optional `many` delegate (`NavigationProbeMany`, answers 1/0/-1; -1 = that pair is asked again singly, so UnexpectedResult behaviour is unchanged). Old signatures and the old code path (many == null) are untouched. Chunks <= `TargetSender.BatchMax` (16).
- Advance: the next queries of the BFS (rest of the working cell, then cells already queued) are fetched in one batch, never more than the budget / the 4500 limit allows; budget and Queries semantics identical (tested after every call for budgets 48, 7, 1).
- ObservePosition / RouteToUncovered: the up to 12 candidate cells go in one call (only the first open one counts). ObservePosition also skips the look when position and floor graph are unchanged (Covered only grows, so the repeat is a no-op).
- Callers: `AutomationV3.cs` MapStep (2 lines), `V3App.cs` RouteToUncovered (1 line). V3App per-target probe (line ~972) left as asked.
- NOT batchable: a character that moves every tick still needs one position probe per tick (one call, not fewer); queries whose answer picks the next one are not issued speculatively.
- Expected reduction: live floor scan 1003 single calls -> 71 batch calls (-93%, budget 48); a standing character 20 ticks -> 1 call; route start look-up up to 12 calls -> 1. Moving-character look-ups: 1 call per tick as before (survey fixtures: 20-37 -> 18-25 calls).
- Tests: `tests/FloorBatchChecks.cs` (new, called from V3Checks): identical graph/coverage/routes on all 18 Solo Trial captures and a synthetic room, call counts, chunk limit, -1 fallback.

## Task 2 - game process gone under a remote call
- `Stability.cs`: `GameGoneException`; `RemoteCaller` gets an injectable `alive` check (default `RemoteCaller.ProcessAlive(pid)`, wired in `MemoryCoreV3.cs` TargetSender). When a call fails with IOException/Win32Exception and the process is gone: no failure count, no back-off, no log line from the caller, journal result "gone".
- `V3App.cs`: the tick catches it, logs ONE line "Oyun kapandı (uzak çağrı sırasında süreç kayboldu)" and runs the existing exit path, now the method `GameExited()` (JobBeforeExit, Stop("Oyun kapandı."), ArmResume), no stack trace.
- Tests: V3Checks (denied call with game alive still counts; with game gone it does not; real process check).

## Task 3 - alive-flag blip while an instance loads
- `AutomationV3.cs` DeathRecovery: a cleared alive flag at a real position must last `ConfirmMs` (800 ms, static so tests can zero it) before the death latches; region-0 reads stay ignored. A real death is latched about 0.8 s after its first read plus one tick. I did not find an HP/alive read during load that I could prove, so no HP rule was added.
- Tests: V3Checks (blip ignored, blip then clear restarts the window, real death latched at 800 ms, region 0 ignored); the older immediate-death checks run with ConfirmMs = 0.

# Skill ID kaldırıldı (3.14.0.0 paketi)

Karar (kullanıcı, 2026-11-27): skill'ler yalnız hotbar tuşuna basılarak kullanılır. Kanıt: ID ile skill (boş tuşlu satır, remote thread'den 0x7074) istemcinin UI / gönderim geçmişi listelerinde use-after-free ile oyunu çökertiyordu; tuş basışı hiç çökertmedi.

Kaldırılan:
- `MemoryCoreV3.cs`: `TargetSender.Cast`, `CastPayload`, `ValidCast`; 0x7074 `BuildPacket` beyaz listesinden ve "skill-id" sayacından çıktı (bot yapısal olarak skill paketi gönderemez; test: 0x7074 reddedilir). Diğer opcode'lar (0x7045, 0x704c, 0x7034, 0x3053, 0x705a, 0x7046) aynı.
- `V3App.cs`: `PressSkill` (ID yolu, `CastPaceAllows`, `NoteCast`, `SkillLabel`); arka planda ID çalışsın diye `CommandsAllowed` içindeki `HasIdOnly` kolu. Saldırı / destek tuşları doğrudan `Press(key)`.
- `SkillRotation.cs`: `IdOnly`, `HasIdOnly`, `LastSkillId/LastKind`, Parti buff (`NextPartyBuff`, `PartyCast`, `MemberNames`).
- `WeaponSwap.cs`: cast pace kodu silindi; `WeaponReady` ve `WeaponRules` duruyor ama çağıran yok (uykuda: satırlar artık skill ID taşımıyor; Wizard/Cleric silah değişimi bu yüzden çalışmaz, çökme / arayüz bozulması yok). `SkillTiming` yalnız "hedef ister mi" için kaldı.
- Silinen dosyalar: `SkillLearned.cs` (Karakterin skilleri sekmesi, bellek taraması, Tüm skiller, Listeye ekle, Kimlikle sına), `SkillScan.cs`, `SkillCatalog.cs` (başka kullanan yoktu). Testleri de silindi.
- Arayüz: "Karakterin skilleri" alt sekmesi yok; Seçenekler'den silah değişimi ve "Skill aralığı %" satırları kalktı (değerler dosyada saklı); Parti buff türü seçilemiyor; "Nasıl çalışır / Seçenekler" metinleri yeni.

Davranış:
- Tuşu boş (ya da geçersiz) açık satır hiç çalıştırılmaz; çalıştırırken tek log satırı "N satırın tuşu yok; atılmaz", listede turuncu "TUŞ YOK". Hiç saldırı tuşu yoksa başlatma açık bir mesajla durur. Satır ve ayarlar silinmez; `SkillId` alanı durur (Buff ID olarak okunur: buff açık mı diye bakıp tuşa basmayı belirler), `Members`, `WeaponSwap`, `CastPacePercent` eski dosyalar için durur.
- Eski "Parti buff" satırları listede kalır (Gelişmiş tabloda `Members` gizli sütun), kullanılmaz, çalıştırırken tek log satırı.
- Düzenleyicide ilk alan Hotbar tuşu (kutuya basılan tuşu yakalar: 1-9, 0, A-Z, F1-F12; Ctrl+tuş desteklenmez, eski tuşlu satırlar da onu desteklemiyordu); Ekle ilk boş tuşla satır ekler. Imbue seçici adında "Imbue" geçen satırları açıp kapatır.
- Oyun önde değilken tuş gönderilmez (mevcut kapı ve "Arka plan tuşunu sına" korundu).

Test: 727 PASS (ID testleri çıktı, 0x7074 reddi, tuşsuz satır, tuşlu satır, eski ayar yükleme, Parti buff inert, sayfada ID kontrolü yok eklendi). Canlıda doğrulanmadı: gerçek avda tuş basışı, Imbue/Buff okuma, eski ayar dosyasıyla açılış.

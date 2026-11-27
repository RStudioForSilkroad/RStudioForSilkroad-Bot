# Stability package: changes

Base: `NewEra-backup-ui-2026-11-26`. Patch: `stability.patch` (src, tools, tests/V3Checks.cs; `git apply -p1`). Sources: ENTEGRASYON_PLANI.md sections 7 and 8.
New helper file: `src/Stability.cs` (pure logic, plus a small partial of the form). Everything is C# 5 (csc 4.0).
The game was never started; nothing here sends a new opcode, changes a payload or the order of calls, or touches GuardyNE / attestation.

Verified by: `tools\build.ps1` ("Build OK"), `tools\run-tests.ps1` (exit 0, 659 PASS lines, "No current test failures", 16-page assertion unchanged), new unit checks in `tests/V3Checks.cs` (`StabilityChecks`). Live behaviour (items 5-8) could NOT be verified against the game.

## 1. Exception hooks + pdb: DONE
- `Stability.cs` `StabilityLog.InstallExceptionHooks` (Application.ThreadException, SetUnhandledExceptionMode(CatchException), AppDomain.UnhandledException); called from `V3App.cs` Main (one line, before EnableVisualStyles, GUI path only). Full `Exception.ToString()` is appended synchronously to events.log; no Stop(), no dialog. Identical exceptions are logged once per 5 s.
- `tools/common.ps1` csc args: `/debug:pdbonly` (writes a .pdb next to the exe; resources/exe behaviour unchanged).
- Risk: an exception on the UI thread no longer shows the .NET dialog; the app continues.

## 2. SaveSettings robustness + transient IOException: DONE
- `SaveSettingsQuietly` (existing, Explorer.cs) now logs once per minute instead of swallowing silently; used in `V3App.cs` BeginOperation (was SaveSettings) and the death path in Tick, `GrimQueue.cs` MarkVisited.
- Tick's catch (V3App.cs): an `IOException` goes to `RetryAfterIoError` (Stability.cs): logged each time ("Geçici G/Ç hatası (n/5)"), the key is released, the job goes on; 5 in a row (each within 10 s of the previous) then Stop() as before.
- `AutomationV3.cs` `V3Settings.Save` left as is (the call sites are what could reach Tick). Stop()/OnFormClosed already wrapped.

## 3. Update idle gate: DONE
- `AutoUpdate.cs` TryApplyUpdate: Stop+Close only when `UpdateGate.Idle(active, grim.Armed, queueRunning, lotteryRunning)` (lottery added to the three requested fields); the existing pending-item / death-recovery deferral is kept. While busy the banner/status stay, re-evaluated every second by the existing timer. After an update is found or an apply failed the next manifest check is 300 s away (`UpdateKnownCheckSeconds`); `UpdateCheckSeconds` stays 15 (an existing test asserts it).
- Behaviour change: a hunt/dungeon/queue is no longer cut off by an update; the update waits until the bot is idle.

## 4. Bounded logs: DONE
- `CappedLog` replaces the `StringBuilder eventLog` (V3Ui.cs field): last ~2000 lines (trimmed in 200-line steps), thread-safe, with a `Version` counter.
- `LogFiles.Append`: events.log rotated at 2 MB to `events.log.1` (one backup), under one lock; `Log()` (V3Ui.cs) locks around the in-memory append and the file write.
- `LogView.cs`: Log tab reads only the last 200000 bytes (`ReadTail`, FileShare.ReadWrite). Export button unchanged.
- `PhShell.cs` `AppendLiveLog`: AppendText + trim when over the limit (was reassigning up to 60 KB per line). `PhPages.cs` `NotificationsTick`: returns early when `eventLog.Version` is unchanged (no re-split each tick).
- Log line formats unchanged. Overlap with the restyle: V3Ui.cs (field + Log), PhShell.cs (AppendLiveLog only).

## 5. Serialized remote calls: DONE
- `MemoryCoreV3.cs` TargetSender: `Execute`/`ExecuteBatch` now wrap the unchanged bodies (`ExecuteOnce`/`ExecuteBatchOnce`) in `RunRemote`: one process-wide `RemoteGate.Lock` and `RemoteGate.MinGapMs = 15` ms between consecutive calls (sleeps the remainder). Same thread, same order, same payloads.
- Behaviour: up to 15 ms of extra latency per call on the UI thread.

## 6. State validation: DONE
- `V3App.cs` CommandsAllowed -> `GameStateReady` (Stability.cs): refuses (false, no Stop) while the area is loading (5 s after a change), the client window is hung (existing `hungSince` while a job runs, `IsHungAppWindow` otherwise), calls are backing off, or the player pointer is invalid (`GameReader.PlayerPointerValid`, one 4-byte read).
- "Area change" = the character jumped more than 96 units, an indoor region changed, or memory could not be read for a while (world loading). A plain region-border crossing while walking is deliberately not counted (Grim dungeons span two outdoor regions). Tick status text says "oyun alanı yükleniyor..." while waiting.

## 7. Back off and stop on errors: DONE (client-exit part reuses existing logic)
- TargetSender counts consecutive timeouts / unexpected results / Win32 failures (`FailureStreak`); each is logged to events.log, followed by a back-off (400 ms x failures, max 2 s) during which calls throw `RemoteBackoffException` (a non-failure) and `CommandsAllowed` is false; 5 in a row: Tick stops the job with "Oyun çağrıları art arda 5 kez başarısız oldu".
- A timed-out call whose thread finished later is now released (`ReleasePending`, 0 ms wait) so the next call can proceed instead of failing for ever.
- Swallowing callers now log once per cause per minute: Stop() final move, navigation prefetch, GrimWalkFailed, potion use (`LogOnce`).
- `ClientLogWatch` (polled every 5 s while a job runs): a new "Disconnected by the server" line in `<game>\logs\client.log` or any change of `crash.log` stops the job, drops the sender (Stop disposes it and its pending thread handles) and arms the existing AutoResume plan like a game exit; a new sender needs a fresh reader check (Capture verifies pid and start time every tick). Client exit/restart was already handled by Tick (HasExited -> Stop, reader dropped).
- Risk: with two accounts sharing one game folder, the other client's disconnect/crash line also stops this job (the logs carry no pid).

## 8. ClientWake calmer: DONE
- `ClientWake.cs` FreezeWatch: no PrintWindow grab and no auto WakeClient/ForceForeground for 10 s after an area change or while the client window is hung. The user-initiated "Client göster" wake is unchanged.

## 9. Always-on diagnostics: DONE
- `GameReader` logs "Oyuna bağlanıldı · PID n · profile" and "Oyun bağlantısı bırakıldı · PID n" to events.log (file only).
- `RemoteTrace`: one aggregated line about every 10 s while calls happen (PID, calls, batches/queries, slowest ms, failures), file only, so the live log is not flooded; failures are written at once. This is the rate-limited reading of "one line per call batch".
- `LogSendSummary` (V3App.cs) no longer requires an active job (still silent when nothing was sent).

## 10. Small safe wins: PARTLY DONE
- Done: `SkillScan.Read` and `CompletionNotice.Observe` reuse one buffer (the latter clears it per piece); Log() thread-safety (item 4).
- Skipped: `RoadMesh.Plan` arrays (`distance`, `parent`) are per plan, not per iteration, and are used by closures and returned results; reusing them needs re-initialisation and reentrancy analysis, i.e. a redesign.

## Not changed / out of scope
Deferred as requested: moving remote calls to a worker thread, LAA flag, time budget per Tick. No edits to AssemblyInfo.cs, WhatsNew.cs, Ui.cs, ModernUi.cs, UiControls.cs, SkillPage.cs.

## Review fixes (second delivery)
Files changed since the first delivery: `src/Stability.cs`, `MemoryCoreV3.cs`, `V3App.cs`, `AutoUpdate.cs`, `WorldMap.cs`, `Explorer.cs` (one catch), `tests/V3Checks.cs`; `stability.patch` regenerated (16 files). Tests: 675 PASS lines.

- F1 (HIGH) fixed. A walkability result above 1 (stairs/ramps) now throws `UnexpectedResultException : IOException` (MemoryCoreV3.cs ExecuteOnce). `RemoteCaller.Run` (Stability.cs, the former `TargetSender.RunRemote`, now injectable with a clock and a log) treats it as a finished call: success, no back-off, no failure line. `WorldMap.cs` ClimbSafeProbe rethrows `RemoteBackoffException` before its general catch, so a back-off can no longer cache a slope step as walkable; `Explorer.cs` ring probe rethrows it too (it used to count it as "blocked"). The other broad catches around probes either return without caching (ObstacleNavigation prefetch) or let the exception through.
- F2 (HIGH) fixed. V3App.cs Tick, game-exit branch: `resumeBusy` is held while `Stop("Oyun kapandı.")` runs when a plan is already armed, so the plan armed by the log watch survives the exit.
- F3 fixed. `tickFailures.EndTick(tickFailed)` runs in a `finally` at the end of Tick (not after Capture): a look without a failure ends the streak.
- F4 fixed. `UpdateGate.Idle` includes `resume.Armed`; `UpdateGate.MayApply` lets an update through after `MaxDeferralMs` (6 h) of deferral, only with no item transaction, no death recovery and no resume being connected/started; the forced install is logged. The deferral timer is `updateDeferredSince` (AutoUpdate.cs).
- F5 fixed in part. crash.log now stops the job only when this client has exited or its window is hung (`ClientLogProblem`, Stability.cs); otherwise it is logged once a minute. The client.log "Disconnected" line keeps its handling: the log has no process id, so another account's disconnect in the same folder can still stop this job (documented risk). `ClientLogWatch.Failures` counts polls that could not read the logs; 3 in a row log a once-a-minute line.
- F6 skipped, not a bug: a test program (ReadOnly multiline TextBox, default MaxLength 32767) appended 118890 characters with `AppendText` without being cut, so no `MaxLength` change was made in PhShell.cs.
- F7 fixed (`ReadTail` trims the BOM). F8 fixed (`StabilityLog.Report` body is in try/catch).
- F9 fixed. `RemoteTrace.Flush()` runs when the sender or the reader is disposed; a summary is also written when the calls paused for 10 s, and its length is first-to-last call, not idle time.
- Tests added: unexpected result is not a failure; back-off refuses calls without a new failure; the limit of 5; streak reset after a clean look; update gate with resume and 6-hour deferral; BOM; trace flush; crash flag and unreadable-log count; tightened CappedLog line-count assertions.

## Skill scan fix (OutOfMemory / minutes-long scan)
Cause: `SkillScan.Candidates` kept every aligned dword that was a valid skill id with plausible pointers before it; in a 1+ GB game heap that gave millions of nodes, and `Choose` then built a 3M-entry index, per-node arrays and full-record group lists in the 32-bit process.
- `src/SkillScan.cs` `Candidates`: new optional `level` filter (the dword after the id must equal the skill table's level for that id) and `cap`; `Read` passes `SkillCatalog.LevelOf` and `MaxCandidates` (400000). Existing calls without the new arguments behave as before.
- `Choose`: no per-node `new[]{P0,P1}`, index-based groups (only groups of 5..1500 nodes are collected, as index lists), presized collections, a time budget (`ChooseBudgetMs` 10 s; on timeout nothing is chosen and the report says "zaman aşımı"). The selection rules (>= 80 % level match, >= 80 % distinct families, known-skill hits rank first) are unchanged.
- `Read`: read phase limited to `ReadBudgetMs` (20 s) and the candidate cap, checked per 1 MB piece (also inside a huge region); the report notes "aday sınırı doldu" / "okuma süresi doldu"; `OutOfMemoryException` is caught and reported as "Bellek yetmedi; tekrar dene" with an empty result, so `FinishScan` always runs and `skillScanning` is cleared.
- `src/SkillLearned.cs`: a second press during a scan shows an info line instead of throwing (no stack trace in the log); a 1-second UI timer shows "Taranıyor… N sn" and is stopped in `FinishScan`.
- Tests (`tests/SkillEngineChecks.cs`): level filter keeps only table-matching nodes and still finds the list; cap; time-budget return; a 4 MB buffer of random valid-looking ids is scanned in well under 5 s (filtered) and stays bounded unfiltered. Existing tests unchanged; tests: 681 PASS lines.
- Not verified live: the real heap (time and memory of a scan against the running game), and whether any genuine learned-skill node has a level that differs from the skill table (the old code already required 80 % to match, so a list with more than 20 % mismatching nodes was never chosen).

## Remote-call journal (crash correlation)
`remote-calls.log` next to events.log (`V3Settings.DirectoryPath`), one line per call into the game, written synchronously (so the last call before a crash is on disk), rotated at 2 MB with one `.1` backup (`LogFiles.Append`). Example:
`19:47:49.123 pid=14180 op=7074 len=11 kind=send 3ms gap=12ms ok`
- Fields: local time with milliseconds, pid, packet opcode (hex) and payload LENGTH only when the call carries a packet (never payload bytes or account/character data), kind (send / move / probe / mount / reverse / call), `xN` for a batch, duration, `gap=` (wait for the 15 ms minimum gap, when any) and result (ok / timeout / unexpected / error / invalid / backoff). One `backoff` line per back-off episode.
- `src/Stability.cs`: `RemoteJournal` (pure format + burst rule; a throwing writer is ignored), `RemoteTimeoutException` (an `IOException`, thrown at the two timeout sites in `MemoryCoreV3.cs`), `RemoteCaller.Run` writes the line inside the existing lock (so lines are ordered). `BuildPacket` reports opcode and payload length; `Move`/`NavigationProbe`/mount/reverse pass their kind.
- Burst rule: after 20 lines in one second, further successful probes collapse into one `kind=probe xN in M calls (burst)` line, written when the next line, the next second, a marker or 250 ms of burst arrives. Failures and sends are never collapsed. A crash inside a burst can lose up to 250 ms of collapsed probe counts.
- Marker lines (`HH:mm:ss.fff pid=N -- text`): `attach`, `detach` (GameReader), `game exited` (Tick), `crash.log changed` / `client.log: disconnected` (client log watch; written even when the job is not stopped).
- Tests: line format, collapse (single probes and batches), sends never collapsed, rotation, throwing writer, and the lines a `RemoteCaller` writes (including opcode/length and the single backoff line); 690 PASS lines in total.
- Not verified live: the real file in AppData while the game runs, and the timing overhead of one small file append per call.

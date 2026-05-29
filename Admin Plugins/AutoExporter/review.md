# AutoExporter plugin review

Reviewer stance: picky. Priorities, in order: correctness on non-happy paths, runtime behaviour on *other* machines (this is a distributed VMS, not a dev box), event-handler / resource leaks, testability, and gratuitous complexity.

> **Status:** H1, H2, M1, M2, M3, L1, L5 are fixed (notes inline). Remaining by choice: M4 (minor), L2/L3/L4 (acknowledged non-defects). 67 unit tests pass.

---

## High severity

### H1. Executions grid read a machine-local file, so it was empty on a remote Management Client  [FIXED]
`RefreshGrid()` used `new ExecutionLog().LoadRecent()`, but that file lives in `%ProgramData%` on the **Event Server**. On a separate Management Client the grid was empty, and `OnExecutionAdded` discarded the broadcast record and reloaded from the missing local file.
**Fix:** added `GetExecutionsRequest`/`GetExecutionsReply` messages; the Event Server serves `LoadRecent()`. The view now holds a newest-first in-memory list, populated from the reply, and appends broadcast records directly (no disk read). Works from a remote client.

### H2. Helper assembly/native-DLL resolution was hardcoded to `C:\Program Files\Milestone\...`  [FIXED]
On a non-default install (other drive, localized Program Files) the helper could not find `VideoOS.Platform.dll` / `CoreToolkits.dll` and died at resolve time.
**Fix:** `BuildSearchDirs` now reads the real install roots from `HKLM\SOFTWARE\VideoOS\...` (64-bit view) first, adds each root plus its `MIPDrivers\GisDriver`, then falls back to the `C:\Program Files` guesses. Mirrors the BarcodeReader helper.

---

## Medium severity

### M1. Inconsistent message-handler / timer teardown  [FIXED]
`StatusUserControl` (and its 30s timer) and `ExecutionsUserControl` only closed their handler in `Shutdown()`, which runs only if the host calls `ReleaseUserControl`.
**Fix:** all three controls (`Status`, `Executions`, `Dashboard`) now also close in `OnHandleDestroyed` (idempotent), matching `JobUserControl`.

### M2. `RefreshRunNowMenu()` leaked `ToolStripMenuItem` click handlers  [FIXED]
`Items.Clear()` removes but does not dispose, and the items carried capturing click lambdas, rebuilt on every refresh.
**Fix:** snapshot the items, `Clear()`, then dispose each.

### M3. Outcome decision was inline in the BG plugin (untestable)  [FIXED]
**Fix:** extracted `ExecutionOutcome.Classify(success, cameraCount, skippedCount)` (pure, internal) next to `TimeRange`; the BG plugin calls it; added `ExecutionOutcomeTests` covering Failed/Skipped/Partial/Success.

### M4. `ClearExecutions` is fire-and-forget  [NOT CHANGED]
Sends the clear and wipes the local grid without a reply, so a dropped message leaves server/UI briefly out of sync and other open clients are not refreshed. Low impact; left as-is. Revisit if multi-client clear consistency matters.

---

## Low severity / cleanup

### L1. Duplicate-path normalization used `Path.GetFullPath` on the client  [FIXED]
These are Event Server paths; resolving them against the client CWD was wrong for relative inputs. **Fix:** normalize by trim + trailing-separator-strip + lower-case only.

### L2. Three independent `CrossMessageHandler`s on one page  [NOT CHANGED]
Status, Executions, and Job controls each start their own handler. Not a bug; a shared handler would be lighter. Low priority.

### L3. `PumpMessages` uses `Application.DoEvents`  [NOT CHANGED]
Justified by the recorder-status-callback requirement and kept single-purpose. Acceptable.

### L4. Hand-rolled JSON in `ExecutionLog`  [NOT CHANGED]
Unit-tested; fine as-is. If a nested object is ever added, switch to a real serializer.

### L5. Dead members after refactors  [FIXED]
Removed the unused `StatusItemManager` and the `StatusKindId` / `StatusSingletonId` GUIDs (Status is merged into the dashboard). `ExecutionsItemManager` still serves the combined dashboard; name left as-is to avoid churn.

---

## Things that are good
- Export root cause (DBExporter "Recorder offline" for cameras with no recordings) handled by a pre-export `SequenceDataSource` probe on `RecordingSequence` (covers continuous and motion recording), surfaced as a distinct Partial/Skipped outcome with the camera names.
- Helper IPC: request/result JSON, tested stderr `PROGRESS` parser, deterministic exit codes, cancellation via `Process.Kill`, temp files cleaned in `finally`.
- Concurrency guard records benign double-triggers as `Skipped` instead of firing a false Failed event.
- Pure logic isolated from MIP and unit-tested (`TimeRange`, `ExecutionLog.PruneLines`, `StorageStatus`, `HelperProgressParser`, `ExecutionOutcome`).
- Permissions follow the PKI pattern (Read/Edit security actions + per-item `CheckPermission`).

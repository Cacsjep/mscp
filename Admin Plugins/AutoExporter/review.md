# AutoExporter plugin review

Reviewer stance: picky. Priorities: correctness on non-happy paths, runtime behaviour on other machines (distributed VMS, not a dev box), event-handler / resource leaks, testability, gratuitous complexity.

This is **round 2**, after the messaging refactor, the Running-row progress redesign, and the AVI fixes. Round-1 findings (H1, H2, M1, M2, M3, L1, L5) are resolved. 67 unit tests pass.

---

## Round 2 findings

### R1. Helper `WaitForCompletion` can hang forever  [MEDIUM]
`AutoExporterHelper/Program.cs` `WaitForCompletion`:
```
while (true) {
    int p = exporter.Progress;
    EmitProgress(...);
    if (p >= 100 || exporter.LastError > 0) return;
    PumpMessages(250);
}
```
If `Progress` plateaus below 100 without setting `LastError` (recorder stall, a camera that never finishes, a driver edge case), this loops forever. The helper has no internal timeout or cancellation. For a **manual** run the plugin never cancels it, so the helper runs indefinitely, never writes a result, never broadcasts a completion record, and the UI's Run Now stays disabled until the view is reopened.
**Fix:** add a no-progress watchdog (if `Progress` has not advanced in N minutes, `Cancel()` + return an error) and/or an absolute max-duration cap. Emit a clear error so the run is recorded as Failed.

### R2. AVI "camera n/m" total is the target count, not the resolved camera count  [MEDIUM, display]
The Running row shows `camera {CameraIndex+1}/{CameraCount}` where `CameraCount = cfg.Targets.Count`. When a job targets camera **groups**, the resolved camera count differs from the target count, so the denominator is wrong (e.g. "camera 3/2"). The helper knows the real resolved count.
**Fix:** have the helper include the resolved total in its PROGRESS line and thread it through `ProgressUpdate.CameraCount`, instead of using the target count in the plugin.

### R3. Re-opened view loses Trigger/Range on a still-running row  [LOW, cosmetic]
When you leave the node and return mid-run, the view reloads history from the Event Server, which does not contain the in-progress run. The next progress message re-synthesizes the Running row from `ProgressUpdate`, which carries RunId/JobName/Format/StartedUtc (duration is now correct) but **not** Trigger or Range, so those cells are blank until the run completes.
**Fix (proper):** have `OnGetExecutions` include currently-running jobs (from `_running`) in the reply, with full metadata. Requires storing run metadata (started, trigger, format, range) on `RunHandle`.

### R4. `OnExecutionsReply` ignores `CorrelationId`  [LOW]
The reply handler replaces `_records` wholesale for any reply. A stale reply (multiple Refreshes, or a reply that lands just after Clear) can repopulate the grid. Harmless but sloppy.
**Fix:** stamp the request's correlationId and ignore replies that do not match the latest request.

### R5. Dead/misleading overall percent in the plugin  [LOW]
`onProgress` still computes `overall = (camIdx + pct/100)/denom` and passes it as `ProgressUpdate.Percent`. For XProtect this is wrong (DBExporter.Progress is already the whole-export percent, so dividing by target count understates it), and the UI no longer displays `Percent` at all (it uses `CameraPercent`). Dead code that will mislead the next developer.
**Fix:** drop `Percent` (and the calc), or compute it correctly per format. Display already relies on `CameraPercent` + format.

### R6. Hardcoded grid cell indices in `ApplyProgress`  [LOW]
`row.Cells[9]` / `row.Cells[7]` for detail/duration are positional and silently break if the columns are reordered.
**Fix:** name the columns and address by name, or centralize column indices as constants.

### R7. `_records` grows unbounded while the view stays open  [LOW]
Every completed run is `Insert`ed; the list is only reset on a fetch/refresh/reopen. The Event Server file is capped, but a long-lived open view accumulates rows in memory.
**Fix:** cap `_records` (e.g. trim to the same 500 the server returns) after each insert.

### R8. Run Now can stay disabled after an abnormal end  [LOW]
`_runActive` is cleared on completion (ExecutionAdded) and on view reopen, but **not** on Refresh. If a helper dies without a completion record (see R1), Refresh will not re-enable Run Now, only reopening the node will.
**Fix:** reset `_runActive` (and re-request history) in `OnRefreshClick`, or add a stale-progress timeout that clears it.

### R9. Verify the AVI `Filename` change on a real run  [LOW, verify]
`Filename` was changed from a full path to `name + ".avi"` with `Path = camDir` (per the docs). Confirm on an actual AVI export that this yields `<name>.avi` (and `<name>_0001.avi` segments) and not a doubled extension. Not yet exercised end-to-end.

---

## Still-open from round 1 (by choice)
- **M4** ClearExecutions is fire-and-forget (no reply, no multi-client refresh).
- **L2** three `CrossMessageHandler`s on one page.
- **L3** `Application.DoEvents` pump in the helper.
- **L4** hand-rolled JSON in `ExecutionLog`.

## Good
- History served over messaging (works from a remote Management Client); broadcasts applied in-memory.
- Running-row updates in place (no full re-render at 4 Hz, keeps selection).
- Distinct outcomes (Success/Partial/Skipped/Failed) with skipped-camera names; benign double-trigger recorded as Skipped, not a false Failed event.
- Pre-export `SequenceDataSource` probe (RecordingSequence) is the right fix for "Recorder offline".
- Helper finds the install path via registry (portable), IPC is clean (result JSON + tested stderr parser + deterministic exit codes), temp files cleaned in `finally`.
- Handlers closed in both `Shutdown` and `OnHandleDestroyed`; Run Now menu items disposed on rebuild.
- Pure logic isolated and unit-tested (`TimeRange`, `ExecutionLog.PruneLines`, `StorageStatus`, `HelperProgressParser`, `ExecutionOutcome`).

## Suggested fix order
1. R1 (potential indefinite hang) and R2 (wrong camera count with groups).
2. R5 (remove dead/misleading percent), R8 (Run Now re-enable on Refresh).
3. R3/R4/R6/R7 as cleanup.

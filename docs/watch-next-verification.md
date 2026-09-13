# Watch Next regression checks (0.11.56)

- Next X/Y advances the current cursor unless an explicit coordinate edit is pending.
  Retained local expressions are not replaced by watch movement.
- Programmatic watch selection does not schedule ordinary coordinate refresh.
- Starting a watch cancels older preview reads and debounced refresh requests.
- Commands received while a watch replacement/Continue is pending are reported,
  not silently replaced. Running debuggees cannot be given another watch request.
- The VS run-mode event acknowledges Continue. If VS stays paused for 3 seconds,
  the UI reports the armed-but-not-running watch; it does not add another breakpoint.
- A watch hit updates cursor, kernel and view center to the watched coordinate.
  Its bounded view refresh runs even with Auto update off, retaining zoom.
  Other breakpoints retain the usual auto-update behavior.

Automated UI regression: `tests/ViewerWorkspacePreview/run.ps1 -Verify` checks
repeated cursor advancement with retained expressions, no coordinate timer from
watch selection, and exact centering at (5,5) with zoom 24. It uses the real viewer
but does not emulate COM breakpoint creation or debuggee execution.

Native manual check: pause before buffer writes; choose the output pointer and
valid dimensions/stride, Watch Go To (5,5), then Next X repeatedly and Next Y.
Confirm the editor stops after the target write, cursor/kernel match that target,
the target is centered at unchanged zoom, and one-shot breakpoint slots do not
accumulate. Repeat with coordinate expressions, Auto update off, and rapid clicks.
Keep unrelated source/data breakpoints intact; they can interrupt before the target.

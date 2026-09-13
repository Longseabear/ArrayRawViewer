# Viewer workspace redesign

Restore point: `backup/pre-ui-redesign-20260913` (commit `4ab86d3`).
Work branch: `codex/viewer-workspace-redesign`.

The image is the workspace, not the last section of a configuration form.

- Source: expression, Capture, pointer choices, editor selection, auto-update.
- Settings: collapsible, vertically resizable; frame interpretation, structure binding,
  solution-local profiles, numeric-local bindings. Existing persistence format is unchanged.
- Tabs: immediately above the image tools; short member paths with full expression tooltips.
- Inspection: X/Y, Center, Kernel W/H, channel, Zoom, Export and Stats stay outside settings scrolling.
- Hardware watch: explicit, collapsed group documenting that it resumes execution.
- Frame map: toggle beside the image; drag sets View ROI. View dimensions and directional
  movement are grouped here. It does not change Kernel dimensions.
- Export: kernel text copy plus full/View/Kernel RAW save, retaining existing semantics.

Read options retain the advanced acquisition paths. Capture still uses the existing
context acquisition path; rendering, memory ownership, Bayer phase and Watch logic are unchanged.

## Verification

Build the main extension Debug project, then run:

```powershell
.\tests\ViewerWorkspacePreview\run.ps1 -Verify
.\tests\ViewerWorkspacePreview\run.ps1
```

The standalone host uses the real control and deterministic 64x48 Bayer samples.
It disables profile writes and auto-updates; it does not emulate debugger integration.
The checks cover 600/900/1200-pixel windows, persistent inspection controls,
settings visibility, remaining viewer space, map/stats toggles, export scopes,
and array-index preservation in tab captions.

In VS additionally verify Capture, expression autocomplete, per-tab restoration,
coordinate expressions, map dragging, kernel dragging, keyboard copy, RAW save
and one-shot Watch/Next X/Next Y while paused in the native debuggee.
These require a real debugger session and are not covered by the standalone host.

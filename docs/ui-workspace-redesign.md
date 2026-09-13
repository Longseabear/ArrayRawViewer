# Viewer workspace redesign

## Current: single-window tabbed settings

Everything stays inside `Sensor RAW Array Viewer`; no separate settings window.
Expression/Capture and pointer selection stay at the top. Frame, Structure and
Profiles share one settings area, initially 145 device-independent pixels tall.
Drag the divider underneath to resize it (70–300 pixels). The settings tab strip
stays outside its scrolling content. Switching settings tabs does not resize the
image, rebuild controls, copy buffers or add debugger subscriptions.

Go To/Center/Kernel stay above the image and Watch stays below it. Frame map is
visible by default beside the image. The standalone preview hosts the real control
in one window. Its 600/900/1200-pixel checks require at least 230 pixels of image
height at a 780-pixel window height and verify settings-tab switching leaves that
height unchanged. Actual VS docking and debugger integration still need an
installed-extension manual test.

## Previous in-window layout (superseded)

Restore point: `backup/pre-ui-redesign-20260913` (commit `4ab86d3`).
Work branch: `codex/viewer-workspace-redesign`.

The image is the workspace, not the last section of a configuration form.

Responsive layout: at 1100 device-independent pixels and wider, settings occupy a
resizable left column. Narrower windows retain a resizable 170-pixel top settings
panel. The open/closed choice is retained when switching layouts. Inspection and
Watch use compact inline controls, while the frame-map sidebar is 194 pixels wide
and scrolls independently. Core commands remain exposed; no debugger behavior changed.

- Source: expression, Capture, pointer choices, editor selection, auto-update.
- Settings: open by default, collapsible and vertically resizable; frame interpretation, structure binding,
  solution-local profiles, numeric-local bindings. Existing persistence format is unchanged.
- Tabs: immediately above the image tools; short member paths with full expression tooltips.
- Inspection: X/Y, Center, Kernel W/H, channel, Zoom, Export and Stats stay outside settings scrolling.
- Go To resolves the entered X/Y; Center follows the actual selected pixel. Watch
  Go To X/Y and Watch Next X/Y are visible, distinct from ordinary map movement.

Hardware-watch review: the base address is resolved at arm time and one native
data breakpoint is used, with no sample polling while running. Hit handling is
event driven; one-shot removal is the default. The release retry timer is active
only for queued replacement (100 ms, at most 30 attempts). Removed a redundant
retired-breakpoint cleanup pass before arming. Native debugger latency and function
expression evaluation remain engine-dependent; no hardware latency benchmark was run.
- Hardware watch: visible by default, with Go To X/Y and Watch Next X/Y commands.
- Frame map: visible by default, toggle beside the image; drag sets View ROI. View dimensions and directional
  movement are grouped here. It does not change Kernel dimensions.
- Oversized View ROIs shrink proportionally to the 262,144-sample interactive budget.
  Drag commits the rectangle midpoint, not the release corner. Center on a cached
  pixel moves only the camera and does not request debugger memory.
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

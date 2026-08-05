# Sensor RAW Array Image Viewer

An installable Visual Studio 2015 through 2022 VSIX for viewing a
one-dimensional native sensor RAW buffer as a two-dimensional image.

## Build and install

1. Open `ArrayImageViewer.sln` in Visual Studio 2017 with the **Visual Studio
   extension development** workload installed.
2. Restore NuGet packages and build the `Debug` or `Release` configuration.
3. Close all Visual Studio 2017 instances and run the generated
   `src\ArrayImageViewer\bin\Debug\ArrayImageViewer.vsix` (or Release equivalent).
4. In Visual Studio 2017, open **Tools > Sensor RAW Array Viewer**. The viewer
   initially opens as a 1000x780 floating tool window; it can be docked normally.
   In a narrow docked layout, scroll the configuration area to reach the Frame
   Navigator while the image viewport remains visible.

Use the default `ArrayImageViewer.vsix` for Visual Studio 2017 (15.x), 2019
(16.x), and 2022 (17.x). It is built against the VS 2017 Shell facade and .NET
Framework 4.6. A separate VS 2015 package uses the 14.0 Shell facade and .NET
Framework 4.5; build it with:

```powershell
msbuild src\ArrayImageViewer.VS2015\ArrayImageViewer.VS2015.csproj /t:Restore,Build /p:Configuration=Release
```

Its output is `src\ArrayImageViewer.VS2015\bin\Release\ArrayImageViewer.vsix`.

`ArrayImageViewer` is correctly a **class-library** project, so it cannot launch
by itself. In its Debug configuration, F5 starts a separate Experimental Instance
(`devenv.exe /rootsuffix Exp`) and loads the extension there.
To run the native sample application itself, set `SensorRawDebuggee` as the startup
project instead.

If you rebuilt after a change to the extension command/menu, close every
Experimental Instance before pressing F5 again. For a normal VS 2017 or 2022
instance, run the freshly built VSIX again to update the installed extension, then
restart Visual Studio.

## Use the viewer

Pause the debuggee in the function that owns your buffer. You can select `A`, `B`,
or another pointer expression in the code editor, right-click it, and choose
**Open Sensor RAW Viewer for Selection**; this opens the viewer and captures that
expression. The **Tools > Sensor RAW Array Viewer** command remains available for
manual entry. Then choose **Refresh**
in the Pointer section. The list combines current-frame locals and arguments;
selecting `A (int*)` or `B (unsigned int*)` sets the expression and signedness.
**Capture** accepts a selected editor expression and cleans a declaration such as
`unsigned int* B` to `B`. The Expression field also accepts a manual expression
such as `sensorRaw`. Then enter the full-frame interpretation before loading:

- `expr`: native pointer or array expression, for example `raw` or `imageBuffer`.
- `W`, `H`: full image width and height. Enter an integer or a current-stack
  integer expression such as `imageWidth` or `height`.
- `stride`: samples per row, including any padding. It defaults to `W` and also
  accepts a current-stack integer expression such as `pitch`.
- `Q format`: enter `integerBits.fractionalBitsb`, for example `8.8b`. Its total
  is the number of valid stored bits (16 bits for `8.8b`) and values are displayed
  as `raw / 2^fractionalBits`. The renderer uses the complete configured Q range
  for colors, so two frames with the same Q format use the same brightness scale.
  With **Signed int** clear, `8.8b` ranges from `0` to `255.996...`; with it set,
  the range is `-128` to `127.996...`. A 32-bit pointer with `8.8b` is interpreted
  from its lower 16 bits.
- `Normalize`: **Q-format range** is the default and uses the configured
  representable raw range. **Loaded data range** finds min/max in the current
  cached ROI or full preview; **Manual raw range** accepts explicit inclusive
  raw endpoints. Select **Apply display range** to recolor already loaded samples
  without evaluating or reading debugger memory again. The active raw range is
  shown in the pixel inspector and retained in the session profile.
- `Element`: the stored debugger-memory element type. Pointer choices infer it
  automatically; choose `Int8`, `UInt8`, `Int16`, `UInt16`, `Int32`, or `UInt32`
  for a manual expression. This controls address stride in bytes and raw-value
  decoding, independently of the Q-format's valid-bit count.
- `signed`: stays synchronized with `Element` (`int*`/`uint*`, etc.) so a signed
  interpretation cannot accidentally use unsigned element addressing.
- `Pixel order`: choose the Bayer tile orientation: `GRFirst` (GRBG), `RFirst`
  (RGGB), `BFirst` (BGGR), or `GBFirst` (GBRG).
- `Pixel type`: choose physical sample grouping independently: `Bayer`, `Tetra`
  (each site is a 2x2 block), or `TetraSquare` (each site is a 4x4 block).
- `Visualize`: `Gray` for generic loss/score maps, `BayerRaw` for the original
  color-coded mosaic, or an individual `R`, `G`, `Gr`, `Gb`, or `B` plane.

Use **Auto-fill** to query numeric locals and match common names such as
`imageWidth`, `height`, `pitch`, `centerX`, and `centerY`. The expanded **LOCAL
VALUES** section remains available for an explicit variable choice.

Enter **Go to X/Y** and **ROI W/H**, then select **Load ROI**. The ROI is clamped
to the full frame: a 5x5 ROI requested at `(0, 0)` becomes centered at `(2, 2)`
and covers `(0..4, 0..4)`. The reader evaluates only those ROI samples, so a
4096x3072 buffer can be inspected without reading 12 million expressions. The red
rectangle marks the loaded filter ROI; it replaces the old full crosshair so cell
values stay readable. **Load ROI cells** enters high zoom and renders raw and
Q-format values inside the cells. The footer reports the current `X lim` and
`Y lim` in full-frame coordinates. Click directly on the loaded image to center
the current ROI W/H at that sample; it rereads the resulting ROI rather than
selecting a single pixel. Hold **Ctrl** and drag directly on the image to make
the drag bounds become the next ROI W/H. In ROI-context mode, the distinct empty
area is also selectable: it supplies coordinates for the next read even though
it has no currently loaded samples. To inspect a neighboring region, middle-drag
the image (or hold Shift while left-dragging); the new center is shown while
dragging and the ROI is reread only when the mouse is released. The **Pan**
buttons move by half of the current ROI size.

With the image canvas focused, arrow keys reload the ROI one half-ROI at a time.
`+`/`-` (or Page Up/Page Down) zoom around the selected pixel, and Home centers
the viewport on it. Mouse-wheel zoom is anchored at the cursor, so the sample
under the pointer remains under the pointer.

The **Frame Navigator** is an optional lightweight overview map. The primary ROI
interaction is on the image itself: click for the current ROI size, or Ctrl+drag
for a new ROI size. Both use full-frame coordinates and read only the selected
rectangle. Shift+drag and middle-drag remain reserved for panning the current ROI.

Full-frame **Gray** and Bayer mosaic rendering use a direct sample-to-BGRA path;
the 4096x3072 Gray benchmark on the development machine dropped from about 5-6
seconds to about 1 second. Composite demosaic remains intentionally more expensive
because each output pixel searches neighboring Bayer samples.

When the expression box receives focus while the debuggee is paused, it refreshes
the current stack's pointer locals and offers matching names as you type. Selecting
a suggestion applies its element type and signedness. Each pointer that is captured
or successfully loaded receives an in-window **Session Profile**: its dimensions,
stride, source element type, Q-format, Bayer settings, visualization mode, and ROI
are restored when that profile is selected. Settings update automatically and can
also be saved explicitly with **Save settings**. Profiles contain configuration only
(no samples are saved) and are discarded when the viewer window is closed.

`Visualize` supports `Gray`, original color-coded `BayerRaw`, a lightweight
`Composite` Bayer preview, and `R`/`G`/`Gr`/`Gb`/`B` planes. Composite uses the
nearest original samples in the loaded ROI; the hover/cell inspector always
reports the original raw value and true Bayer site.

## Debugger read paths and performance

The expression-evaluator reader is deliberately capped at 16,384 **ROI** samples
because evaluating one debugger expression per sample is too slow. A 5x5 through
128x128 filter ROI is therefore the intended debugger-backed workflow. For native
engines that expose `IDebugMemoryBytes2`, the viewer obtains the active native
stack frame and reads each ROI row as one debugger-memory range; the status line
reports `Read debugger memory`. Unsupported engines automatically fall back to
expression evaluation and report `Read expression fallback`.

On a native engine that reports `Read debugger memory`, **Full preview** reads the
configured full frame and opens it as a fit-to-window image. Native reads run in
small row batches on the Visual Studio UI apartment; the status line shows progress
and allows paint/input events between batches. No per-pixel text is created at that
zoom level; text/grid cells appear only after zooming in far enough that at most
900 cells are visible. The orange ROI rectangle remains overlaid on the full
preview, so the frame navigator can select a small inspectable region without
losing full-frame context. Large preview rendering runs on a background STA worker
and returns a frozen bitmap to the Visual Studio UI thread. Its pixel conversion is
written in 64-row blocks, rather than allocating a second full-frame BGRA array
alongside the raw samples.

For the normal "find this pixel, then inspect its neighborhood" workflow, select
**ROI context**. It reads only a bounded, aspect-ratio-preserving context around
the configured ROI (at most 16,384 samples, typically about 147x111 for a
4096x3072 frame). The context is drawn in its true full-frame location; the rest
of the canvas is a distinct slate color and intentionally has no samples behind
it. This makes the unrendered region obvious without allocating or converting a
full-frame bitmap. The viewer starts centered on the requested coordinate; zoom
until individual cells are visible to show raw and Q-format values.

The divider between the configuration panel and the viewer can be dragged up or
down. The configuration panel keeps its own vertical scrollbar, so a compact
tool window can reserve nearly all of its height for the image.

Full previews above the normal 4,194,304-sample interactive limit show a one-time
confirmation with their approximate debuggee-memory read size. A 4096x3072 `uint*`
frame is such a request; selecting **Yes** still reads it in responsive row batches.

## Core checks

`tests\ArrayImageViewer.Tests` is a dependency-free .NET 4.5 console test project.
It checks Q-format parsing/negative/high-fraction edge cases, signed/unsigned
source-element storage shape, stride handling, all four Bayer tile orders plus
Tetra/TetraSquare phase, individual green-plane visibility, ROI drag geometry,
small renderer output, the worker-thread bitmap handoff, and a real 4096x3072
Gray render. Build and run it with:

```powershell
msbuild tests\ArrayImageViewer.Tests\ArrayImageViewer.Tests.csproj /t:Build /p:Configuration=Release
.\tests\ArrayImageViewer.Tests\bin\Release\ArrayImageViewer.Tests.exe
```

## Bayer coordinate rule

`(0, 0)` is the top-left sample of the **full frame**. Bayer parity is calculated
from full-frame x/y coordinates, never from the currently zoomed viewport. Thus a
pixel's `R`, `Gr`, `Gb`, or `B` classification stays correct after panning or
zooming. For `Tetra` and `TetraSquare`, the same rule applies after grouping full
frame coordinates into 2x2 and 4x4 blocks respectively.

## Included native debuggee

`samples\SensorRawDebuggee` is an x64 C++ console application that chooses the
newest C++ toolset actually installed in the opened Visual Studio (normally v140
in 2015, v141 in 2017, v142 in 2019, or v143 in 2022). It allocates and fills
`sensorRaw`, a `uint32_t*` containing a 4096x3072 GRBG 13-bit test pattern, then
stops at `__debugbreak()`.

Set `SensorRawDebuggee` as the startup project and start debugging. At the break:

- Set `expr` to `previewRaw`, `W/H/stride` to `128/128/128`, Q format to
  `13.0b`, **Signed int** off, Pixel order to `GRFirst`, Pixel type to `Bayer`,
  Visualize to `BayerRaw`, and ROI W/H to `5/5`; then select **Load ROI**. This
  exercises the debugger-backed input path immediately.
- Set `expr` to `sensorRaw` and dimensions to `4096/3072/4096` to use the same
  settings intended for the full buffer. Set Go to X/Y and ROI W/H before
  selecting **Load ROI**; the 4096x3072 frame is never read as 12 million
  individual debugger expressions.

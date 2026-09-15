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

Enter **Go to X/Y** and **ROI W/H**, then select **ROI + context**. The ROI is clamped
to the full frame: a 5x5 ROI requested at `(0, 0)` becomes centered at `(2, 2)`
and covers `(0..4, 0..4)`. The reader evaluates a bounded context surrounding that
ROI, so a 4096x3072 buffer can be inspected without reading 12 million expressions.
The orange rectangle always marks exactly **Kernel W/H**, while **View W/H** is the
separate surrounding rectangle that is actually read and rendered; the rest of the
full-frame canvas uses a distinct unloaded color. **Read exact cells** is the deliberate close-up action:
it reads only the ROI, enters high zoom, and renders raw and
Q-format values inside the cells. The footer reports the current `X lim` and
`Y lim` in full-frame coordinates. Click and hold directly on the loaded image to
preview the current ROI W/H at that sample, then release to read it; this avoids
an accidental debugger read while choosing the location. Hold **Ctrl** and drag
directly on the image to make the drag bounds become the next ROI W/H. Press
**Esc** before release to cancel either selection. In ROI-context mode, the distinct empty
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
interaction is on the image itself: click-hold for the current ROI size, or
Ctrl+drag for a new ROI size. Both show a temporary teal outline, use full-frame
coordinates, and read only the selected rectangle on mouse release. Shift+drag
and middle-drag remain reserved for panning the current ROI.

Full-frame **Gray** and Bayer mosaic rendering use a direct sample-to-BGRA path;
the 4096x3072 Gray benchmark on the development machine dropped from about 5-6
seconds to about 1 second. Composite demosaic remains intentionally more expensive
because each output pixel searches neighboring Bayer samples.

When the expression box receives focus while the debuggee is paused, it refreshes
the current stack's pointer locals and offers matching names as you type. A
suggestion is applied only by pressing Enter or double-clicking it; refreshing
locals never replaces a manually entered member expression such as `this->D`.
Each pointer that is captured
or successfully loaded receives an in-window **Session Profile**: its dimensions,
stride, source element type, Q-format, Bayer settings, visualization mode, and ROI
are restored when that profile is selected. Settings update automatically and can
also be saved explicitly with **Save settings**. With **Remember for this solution**
enabled, the last viewer state is restored after Visual Studio restarts with that
same solution. Use **Set name** and **Save set** to store multiple named profile
sets (for example `Full sensor`, `Padded raw`, or `Loss map`); each contains the
expression, W/H/stride, Q/Bayer settings, View ROI, and Kernel ROI. These are
local to the solution and contain configuration only—no debuggee samples are saved.

**Auto update** is on by default: after you finish editing a field or select a
local value, the viewer rereads the bounded View ROI after a short debounce. It
also refreshes that bounded ROI whenever F10/F11 (or another debugger transition)
returns the debuggee to Break mode, so an updated output buffer is reread without
pressing a button. Full preview is deliberately not reread after every step. Turn
Auto update off when manually staging several settings. Invalid input is highlighted with a
red border, tooltip, and status message. View movement is not a data operation:
middle/Shift-drag, the View arrows, and keyboard arrows pan only the viewport and
never reread debugger memory.

### Structure templates

### JSON으로 구조체 설정 편집하기

**Tools > Options > Array RAW Viewer > Structure Templates**에서 **Open JSON**을
누르면 현재 표를 JSON 편집본으로 만들어 메모장에서 엽니다. 수정하고 메모장에서
저장한 뒤 **Load edited JSON**으로 표를 교체하고 **Save**로 현재 솔루션에 적용합니다.
Viewer에서는 **Reload templates**로 새 정의를 불러옵니다.

편집본은 실제 설정 저장소와 별개입니다. Open JSON을 다시 누르면 같은 편집본을
열며, 이후 표에서 변경한 내용이 자동으로 동기화되지는 않습니다. 잘못된 JSON은
기존 표와 저장된 설정을 바꾸지 않습니다. 다른 PC에서는 JSON을 전달하고
**Import JSON → Save**로 적용할 수 있습니다.

[구조체 2개 JSON 예시](examples/structure-templates.json)를 복사해서 시작하세요.
`ClassName`은 디버거에 표시되는 실제 타입명(템플릿 인수 포함), 나머지 필드는
그 객체 기준 멤버 접근식입니다. `this`나 인스턴스 이름은 넣지 않으며 검색 루트는
Viewer에서 지정합니다. stride는 width를 따릅니다. `uint16`과 `unsigned short`처럼
별칭이 다르게 표시될 경우 실제 디버거 타입명에 맞춰 주세요.

Use **Tools > Options > Array RAW Viewer > Structure Templates** to define one
or more framework mappings without repeatedly typing member expressions. Select
a saved template in the viewer to bind it. A template contains only a portable
class/template name plus `RAW DATA`, `WIDTH`, and `HEIGHT` member paths such as
`D`, `W`, and `H`. **SEARCH ROOT** belongs to the live Viewer session, not the
template: enter `this`, `frame`, `ctx->input`, or another current-frame
expression and press **Search objects**. The viewer composes `SEARCH ROOT=this`,
`RAW DATA=D` as `(this)->D`. For unusual APIs, write an explicit suffix such as
`->GetPointer()` or use `{root}` in a full expression such as `{root}.data()`.

**Bind current root** evaluates Width/Height, sets stride equal to Width, and
infers signedness and storage width from RAW DATA. Binding rejects missing
expressions and non-integral, pointer-to-pointer, floating-point, bool, void, or
64-bit raw data; supported sources are signed/unsigned primitive 8/16/32-bit
pointers. Templates are retained per solution, contain no debuggee samples, and
can be shared through the Options page's **Export JSON**/**Import JSON** buttons
(`FormatVersion: 1`). This makes
them suitable for committing alongside a framework integration or distributing
with a team setup guide. A minimal portable file is:

```json
{
  "FormatVersion": 1,
  "Templates": [{
    "ClassName": "MyStructure",
    "DataAccess": "D",
    "WidthAccess": "W",
    "HeightAccess": "H"
  }]
}
```

**Search objects** is the fast path for a parent object. Enter a single
root pointer such as `this`, then search. The viewer recursively expands only
that object graph (depth 4, maximum 64 debugger nodes); it does not search
Locals, Arguments, or the rest of the call stack. Only instances whose debugger
type matches a registered template class are listed. Selecting a candidate does
not change **SEARCH ROOT**. Press **Use captured object** to bind the selected
candidate; a value object such as `this->input` then binds through
`&((this)->input)` while a pointer member is used directly.

### Hardware pixel watch

While a native C++ debuggee is paused, **Watch (Go To X/Y)** installs one Visual Studio
native data breakpoint for the selected full-frame sample and immediately resumes
execution. The viewer resolves the selected pointer while paused and registers
the literal address `pointer + (y * stride + x) * elementSize`, so row padding
is honored and the native engine does not need to reevaluate a `this`-dependent
expression after it resumes. The breakpoint watches the source element's full
byte width. When that sample is written, Visual Studio stops and the current View
ROI refreshes. **Watch Next X** and **Watch Next Y** move the watched coordinate;
**Clear watch** removes the viewer-owned breakpoint. It is also removed when the
viewer closes.

This is a single hardware data breakpoint, not a whole-ROI watch. Plain pointer
variables such as `A`, `B`, or `inputBuffer` are watched directly. A function or
property expression such as `data->GetPointer()` is evaluated exactly once while
paused, then the resulting literal address is watched; this avoids repeatedly
calling the function from Visual Studio's data-breakpoint machinery. Re-arm that
watch after the function's pointer value changes or after a new debug session starts.

### Array tabs

The strip immediately above the image has one tab per comparison target. Use
**+ Array** to open a new input while retaining the current interpretation as a
starting point, then choose another pointer or expression. Switching tabs restores
that tab's expression, frame/Q/Bayer settings, View and Kernel ROI, zoom, scroll
position, frame-map preview, and last rendered ROI without rereading debugger
memory. Close (`×`) discards only that tab's in-memory image cache; no raw samples
are written to disk.

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
in 2015, v141 in 2017, v142 in 2019, or v143 in 2022). It stops at one
`__debugbreak()` with these pointer locals alive:

- `B` / `inputBuffer` / `sensorRaw`: `unsigned int*`, 4096x3072 GRBG, 13-bit input values.
- `outputBuffer`: `uint16_t*`, the matching 4096x3072 processed output. The
  sample applies black-level subtraction, Q8 digital gain, and 14-bit clipping
  before the breakpoint.
- `paddedRaw`: `uint16_t*`, 640x480 image with stride 672 and `0xDEAD` row padding.
- `A` / `scoreMap`: `int*`, signed Q8.8 Gray score map.
- `tetraRaw` and `tetraSquareRaw`: 12-bit `uint16_t*` test patterns for their
  corresponding physical Bayer pixel types.
- `defectMask`: 8-bit Gray grid/hot-pixel map.
- `imageSimulator`: an `ImageSimulator` object whose `input`/`output` members,
  `input_aux_stream` vector, and `output_aux_stream` C++ array all contain
  minimal `ImageStream { m_width, m_height, unsigned int* m_data }` values.

It also leaves each matching width, height, stride, center, and 5x5 ROI local in
scope. `cachedSensorPointer` demonstrates the recommended replacement for a
function-derived pointer expression: capture the pointer into a regular local,
rather than making the debugger evaluate `object.GetPointer()` repeatedly.

Set `SensorRawDebuggee` as the startup project and start debugging. At the break:

- Select `B (unsigned int*)` from Pointer, or enter `inputBuffer`; use
  `inputWidth/inputHeight/inputStride`, Q format `13.0b`, Pixel order `GRFirst`,
  Pixel type `Bayer`, and Visualize `BayerRaw`. Then select `outputBuffer` and
  use `outputWidth/outputHeight/outputStride`, `14.0b`, and the same Bayer
  settings to compare the processing result at exactly the same coordinates.
- Select `paddedRaw`; use `paddedWidth/paddedHeight/paddedStride` and `12.0b`.
  The `0xDEAD` padding must not render, proving the stride is measured in samples.
- Select `A (int*)` or `scoreMap`; use `smallWidth/smallHeight/smallStride`,
  signed `8.8b`, and Visualize `Gray`.
- Select `tetraRaw` or `tetraSquareRaw`; use the small dimensions, `12.0b`,
  `GRFirst`, and their matching Pixel type. Use `defectMask` with `UInt8`,
  `8.0b`, and `Gray` to verify an 8-bit generic map.
- The first break happens in `ImageSimulator.cpp` inside `ImageSimulator::ProcessAndBreakForCapture`, before
  its nested `for (y) / for (x)` processing loop writes `output.m_data[y * width + x]`.
  In the viewer use root `this` and **Capture structures**. The viewer detects the
  included `ImageSimulator` root and adds the `ImageStream` sample mapping
  (`m_data`, `m_width`, `m_height`) automatically. It should list the two direct
  streams, two vector elements, and two array elements; select one then press
  **Use captured object** to bind it. Bind `output`, choose a pixel, and use
  **Watch (Go to X, Y)** before continuing to stop at that exact nested-loop write.

### Native GoogleTest sample checks

The repository includes a standalone GoogleTest target for the `ImageSimulator`
fixture. CMake downloads GoogleTest 1.14.0 on first configuration; no manual
gtest or vcpkg installation is required.

```powershell
cmake -S tests\NativeImageSimulatorTests -B tests\NativeImageSimulatorTests\build -G "Visual Studio 17 2022" -A x64
cmake --build tests\NativeImageSimulatorTests\build --config Debug
ctest --test-dir tests\NativeImageSimulatorTests\build -C Debug --output-on-failure
```

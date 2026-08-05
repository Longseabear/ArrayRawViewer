# Sensor RAW Array Image Viewer

An installable Visual Studio 2015, 2017, 2019, and 2022 VSIX draft for viewing a
one-dimensional native sensor RAW buffer as a two-dimensional image.

## Build and install

1. Open `ArrayImageViewer.sln` in Visual Studio 2017 with the **Visual Studio
   extension development** workload installed.
2. Restore NuGet packages and build the `Debug` or `Release` configuration.
3. Close all Visual Studio 2017 instances and run the generated
   `src\ArrayImageViewer\bin\Debug\ArrayImageViewer.vsix` (or Release equivalent).
4. In Visual Studio 2017, open **Tools > Sensor RAW Array Viewer**.

The manifest accepts Visual Studio 2015 (14.x), 2017 (15.x), 2019 (16.x), and
2022 (17.x) Community, Professional, and Enterprise editions. The extension is
built against the Microsoft Visual Studio 2015 SDK baseline.

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

Pause the debuggee in the function that owns your buffer, then choose **Refresh**
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
- `signed`: select for `int*`; clear for `uint*`.
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
`Y lim` in full-frame coordinates.

## Draft limitation

The expression-evaluator reader is deliberately capped at 16,384 **ROI** samples
because evaluating one debugger expression per sample is too slow. A 5x5 through
128x128 filter ROI is therefore the intended debugger-backed workflow. The next
performance step is still a native debugger-memory reader that reads ranges in
blocks; it will replace only `DebugExpressionFrameReader`.

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

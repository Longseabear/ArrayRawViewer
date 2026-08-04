# Sensor RAW Array Image Viewer

An installable Visual Studio 2013, 2017, 2019, and 2022 VSIX draft for viewing a
one-dimensional native sensor RAW buffer as a two-dimensional image.

## Build and install

1. Open `ArrayImageViewer.sln` in Visual Studio 2017 with the **Visual Studio
   extension development** workload installed.
2. Restore NuGet packages and build the `Debug` or `Release` configuration.
3. Close all Visual Studio 2017 instances and run the generated
   `src\ArrayImageViewer\bin\Debug\ArrayImageViewer.vsix` (or Release equivalent).
4. In Visual Studio 2017, open **Tools > Sensor RAW Array Viewer**.

The manifest accepts Visual Studio 2013 (12.x), 2017 (15.x), 2019 (16.x), and
2022 (17.x) Community, Professional, and Enterprise editions. Visual Studio 2013
Premium and Ultimate editions are included as well.

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

Pause the debuggee in the function that owns your buffer, then choose **Refresh
locals**. Select `A (int*)` or `B (unsigned int*)` from the Pointer list; this
sets the expression and signedness automatically. **Capture selection** remains an
alternative for a pointer expression selected in the active editor, and the
Expression field accepts manual input such as `sensorRaw`. Then enter the frame
interpretation before loading:

- `expr`: native pointer or array expression, for example `raw` or `imageBuffer`.
- `W`, `H`: full image width and height.
- `stride`: samples per row, including any padding. It defaults to `W`.
- `frac`: Q-format fractional bit count. A raw value is displayed as
  `raw / 2^frac`.
- `signed`: select for `int*`; clear for `uint*`.
- `Bayer`: `GRBG`, `RGGB`, `GBRG`, `BGGR`, or `None`.
- `view`: `Raw` grayscale values, `Mosaic` color-coded original Bayer samples,
  `Composite` demosaicked preview, or an individual Bayer plane.

The **LOCAL VALUES** row can populate these fields from debugger locals instead of
manual typing. Choose **Refresh numeric locals**, then select the required local
beside each target: for example `imageWidth -> W`, `imageHeight -> H`,
`imageStride -> Stride`, `centerX -> X`, and `centerY -> Y`.

Use **Synthetic preview** to validate the renderer and UI without a debuggee.
When the native debuggee is paused, **Load pointer** evaluates `expr[index]`
through the Visual Studio expression evaluator. Enter `Go to X` and `Y`, then
press **Center** to center the exact full-frame coordinate. **Inspect cells**
centers the coordinate, enters high zoom, and displays the raw value (and the
Q-format value at larger cell sizes) inside each visible pixel cell. The orange
crosshair and status line always report raw value, Q-format value, and Bayer site.

## Draft limitation

The expression-evaluator reader is deliberately capped at 16,384 samples because
evaluating one debugger expression per pixel is too slow for a 4096x3072 image.
The full-frame renderer itself supports up to 16,777,216 samples, so it can be
exercised using Synthetic preview. The next required implementation step is a
native debugger-memory reader that reads a pointer range in blocks; it will replace
only `DebugExpressionFrameReader` and leave the viewer/core logic unchanged.

## Bayer coordinate rule

`(0, 0)` is the top-left sample of the **full frame**. Bayer parity is calculated
from full-frame x/y coordinates, never from the currently zoomed viewport. Thus a
pixel's `R`, `Gr`, `Gb`, or `B` classification stays correct after panning or
zooming.

## Included native debuggee

`samples\SensorRawDebuggee` is an x64 C++ Visual Studio 2013 (`v120`) console
application. It allocates and fills `sensorRaw`, a `uint32_t*` containing a
4096x3072 GRBG 13-bit test pattern, then stops at `__debugbreak()`.

Set `SensorRawDebuggee` as the startup project and start debugging. At the break:

- Set `expr` to `previewRaw`, `W/H/stride` to `128/128/128`, fractional bits to
  `0`, `signed` off, and Bayer to `GRBG`; then select **Load expression**. This
  exercises the debugger-backed input path immediately.
- Set `expr` to `sensorRaw` and dimensions to `4096/3072/4096` to use the same
  settings intended for the full buffer. The current expression reader will show
  its explicit large-frame limit; use **Synthetic preview** to inspect the
  full-resolution renderer until block memory reading is added.

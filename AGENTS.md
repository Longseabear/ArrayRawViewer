# ArrayImageViewer - Agent Instructions

## Project purpose

Build a Visual Studio debugger extension that visualizes pointer-backed sensor RAW
arrays (`int*`, `uint*`, and compatible signed/unsigned integral element types) as
two-dimensional images. The source is normally a plain one-dimensional buffer, not
a metadata-bearing type such as `cv::Mat`. The compatibility baseline is **Visual Studio 2013 through 2022**; do not use
APIs, language features, or dependencies that require a newer Visual Studio shell.

The extension must help a developer inspect:

- A selected debugger array or pointer expression.
- A user-defined interpretation of that buffer as a full frame.
- Element values interpreted with a configurable Q-format (integer bits and
  fractional bits).
- Sensor Bayer mosaics, especially GRBG, as both a raw mosaic and selectable
  color channels.

## Compatibility baseline

- Target the Visual Studio 2013 extensibility SDK baseline and a VSIX installation.
- Prefer C# 5.0-compatible language features and .NET Framework 4.5 so the
  extension can run in Visual Studio 2013. Do not introduce newer runtime API
  requirements without a compatibility fallback.
- Avoid WPF/WinForms packages that are not available in the VS 2013 SDK by default.
- Keep the extension functional for both 32-bit and 64-bit debuggee processes.

## Functional requirements

### Data source

- Accept debugger expressions resolving to pointer-backed integral arrays.
- Support at least `int*` and `uint*`; retain the source element's signedness and
  bit width when decoding values.
- Read memory only through Visual Studio debugger APIs. Never dereference a
  debuggee pointer in the extension process.
- Surface clear errors for null pointers, unreadable memory, incomplete reads, and
  unsupported element types.

### Dimensions

- Treat a raw pointer or one-dimensional array as having no implicit image shape.
  Do not infer width, height, ROI, stride, or buffer length from its address or
  surrounding memory.
- Before rendering, ask for full-frame width and height unless the same values
  have explicitly been retained for the active viewer session.
- Support a row stride in samples (not bytes) separately from full-frame width.
  Default the stride to width, but allow a larger stride for padded rows. Padding
  values are not image pixels and must never appear in the rendered image.
- Validate positive width, height, and stride; require `stride >= width`. Guard
  all frame-size and offset arithmetic against overflow and unreasonably large
  allocation/read requests.

### Zoom and viewport inspection

- After the user supplies full-frame width and height, render the complete array as
  one 2D image by default.
- Provide mouse-wheel or equivalent zoom centered on the cursor, plus pan controls
  (drag and/or scrollbars) so the user can navigate any part of a large image.
- The visible zoomed rectangle is a view-only viewport/ROI. It must be derived from
  the rendered full frame, not requested as an alternate buffer interpretation.
- Keep a prominent coordinate-jump control: entering full-frame `x, y` immediately
  centers the viewport on that exact sample. Do not require manually panning a
  4096x3072 image to find a pixel.
- Show the current viewport in full-frame pixel coordinates (for example,
  `x=1000..1004, y=700..704`) and retain a crosshair or other persistent marker at
  the selected `x, y` after a jump or click.
- Validate requested x/y against the full frame, retain the selected coordinate
  across zoom changes, and report its Bayer site using full-frame parity.
- At high zoom, render samples as crisp cells rather than blurred interpolation,
  with optional grid lines and the x/y, raw integer, decoded Q-format value, and
  Bayer channel under the cursor.
- Address full-frame sample `(x, y)` as `base + ((y * stride) + x) * elementSize`
  after checked arithmetic. The viewport determines which already-decoded/rendered
  samples are visible; it does not change frame dimensions or stride.

### Q-format decoding

- Make Q-format explicit in the UI and model: number of fractional bits plus
  signed/unsigned interpretation.
- Decode a stored sample as `sample / 2^fractionalBits`, preserving the original
  raw integer value for inspection.
- Use an exact integer/decimal representation where practical; do not silently
  lose sign or precision through an early floating-point conversion.
- Define and test edge cases: zero fractional bits, high fractional-bit counts,
  negative signed samples, and full-range unsigned samples.

### Bayer rendering

- Support at minimum GRBG, RGGB, GBRG, and BGGR layouts, plus a `None/Mono` mode.
- Apply Bayer parity to **full-frame coordinates**, not viewport-local coordinates:
  a viewport sample `(vx, vy)` maps to `(viewportX + vx, viewportY + vy)` before
  its Bayer channel is determined. Zooming, panning, and view boundaries must
  never reset or shift the Bayer phase.
- Provide these display modes for Bayer input:
  - Raw mosaic / grayscale values.
  - Composite Bayer preview (a simple documented demosaic is sufficient initially).
  - Individual `R`, `G` (combined), `Gr`, `Gb`, and `B` planes.
- Treat `Gr` and `Gb` as distinct pixel positions even when an aggregate green
  mode is offered.
- Document the coordinate convention: `(0, 0)` is the top-left sample, x advances
  right, y advances down. Bayer parity is derived from those coordinates.
- Clearly label whether a displayed color is an original mosaic sample or an
  interpolated composite-preview value. Pixel inspection always reports the
  original raw sample and its true Bayer site (`R`, `Gr`, `Gb`, or `B`).

## Architecture guidance

- Keep Visual Studio shell/debugger integration thin. Put value decoding,
  dimension validation, Bayer parity lookup, normalization, and rendering-input
  preparation in testable framework-independent classes.
- Use immutable request/result models where reasonable.
- Separate raw sample acquisition from display normalization. The UI must show the
  chosen normalization range and allow it to be changed without rereading memory.
- Design the first renderer for correctness and clarity; optimize large-image
  responsiveness after measurements identify a bottleneck.

## UX requirements

- Offer the viewer through a context-menu command when an eligible debug expression
  is selected, and provide an alternate command path for manually entering an
  expression.
- Before the first render, show a compact configuration surface containing
  expression/type summary, full-frame width/height, row stride, fractional bits,
  Bayer pattern, display mode, and normalization range.
- The full-frame preview is the default view. Zooming into a 5x5 area around any
  pixel in a 4096x3072 frame is a normal viewer interaction, not a pre-render
  configuration step.
- Show the value under the cursor as x/y, raw integer, decoded Q-format value, and
  Bayer channel.
- Clearly distinguish values inferred from debugger metadata from values supplied
  by the user.
- Keep the debugger responsive: perform large memory reads and pixel preparation
  asynchronously, then marshal UI updates back to the UI thread.

## Safety and performance

- Treat all debuggee data as untrusted.
- Set conservative configurable limits for total samples and bytes read. Require
  explicit user confirmation before exceeding the normal interactive limit.
- Check bounds before indexing every row/column transformation.
- Never persist raw debuggee memory or expressions without an explicit user-facing
  setting.

## Testing and verification

- Unit-test Q-format decoding, signedness, frame/stride validation and overflow
  handling, address calculation with padded rows, Bayer coordinate-to-channel
  mapping, and every supported Bayer pattern.
- Include viewer-coordinate tests for a 4096x3072 frame, padded strides, viewport
  clamping at frame edges, immediate coordinate jumps, and a 5x5 zoomed view around
  a chosen pixel.
- Test Bayer phase across viewport boundaries and arbitrary coordinate jumps: a
  known full-frame coordinate must retain its `R`/`Gr`/`Gb`/`B` classification at
  every zoom level and viewport location.
- Include deterministic test matrices for a 4x4 Bayer tile so channel placement is
  obvious.
- Add integration/manual verification instructions for a native C++ debuggee in
  Visual Studio 2013 with `int*` and `uint*` arrays.
- Before adding dependencies, verify they build and run under the VS 2013 target.

## Implementation workflow

1. Inspect existing code and preserve unrelated user changes.
2. Make the smallest coherent change for the requested behavior.
3. Add or update focused tests with behavior changes.
4. Build and run the relevant tests using the VS 2017-compatible toolchain where
   available; report limitations clearly.
5. Do not introduce newer Visual Studio requirements without explicit approval.

# Changelog

## 0.12.0 — Native outlines, line layout and reusable stroke rendering

- Add immutable outline settings: 11 preset dashes, up to 32 custom dash/gap pairs, caps, joins, miter limit, independent begin/end markers and linear gradient strokes.
- Retain line flips and zero-width/zero-height extents in native files, DrawingML, rendering and hit testing. Preserve collapsed segments without undefined directions; suppress all drawing for zero-width outlines.
- Fix closed shapes misclassified as arrows merely because `tailEnd` is present. Import supported grouped connectors rather than silently dropping them; connector attachment/routing is still not retained.
- Resolve sparse direct/layout/master outlines and theme line references with supported placeholder colors, fill transforms and explicit no-fill. Preserve native dash, cap, join, marker, width and gradient definitions; warn on unsupported compound/inset styles.
- Add reusable explicit-apply `OutlineEditor`, independent outline-gradient authoring, ribbon/search commands, append-only sample and diagnostics. Stale/locked drafts cannot partially commit; solid outline colors clear gradient overrides.
- Reuse direct-line paints and bounded O(1) dash effects. Remove uncached positional shape clones and include all stroke/direction state in retained-picture invalidation.
- Add model, native schema/interchange, pixel/resource and real-input browser regressions. Extend identical-driver benchmarks with direct solid lines and byte-identical cross-version import fixtures.
- New native properties require schema 9; schemas 1–8 remain readable. Compound/inset strokes, arbitrary connector paths and exact native Office marker sizing remain outside the implemented subset.

## 0.11.0 — Native gradient fills and theme color resolution

- Add immutable linear gradients with 2–64 stops, independent alpha, duplicate-position hard edges, scaled/unscaled direction and rotate-with-shape geometry.
- Share gradient rendering across shape/picture backing fills, slide backgrounds, table cells, previews, slide show, PNG and PDF. Update retained-scene, shape and thumbnail invalidation; bounded shader LRU supports temporary ownership and host unload/reload.
- Add reusable explicit-apply Uno gradient controls, shape/background/table commands and an editable gradient sample. Solid-fill choices remove gradients; normalized stops survive resize and undo/redo.
- Preserve supported native `gradFill`, `gsLst`, `lin`, alpha and rotation/scaling flags. Resolve matching layout/master fill inheritance and theme fill/background references, including placeholder colors.
- Resolve the theme related to each master and color-map overrides; share RGB/system/theme alpha, luminance, saturation, tint and shade handling. Unsupported modes produce warnings.
- Cache bounded shared XML parts during import. Extend same-driver benchmarks with repeated layout/theme imports and add model, native schema, round-trip, pixel/cache and real-input browser regressions.
- Native gradient documents require schema 8; schemas 1–7 remain readable. This is not full theme/master, radial/tiled gradient or native PowerPoint equivalence; see compatibility documentation.

## 0.10.0 — Picture layout and native picture fidelity

- Add immutable non-destructive picture crops, independent destination offsets, Contain/Cover/Stretch fitting, rectangular/elliptical masks, horizontal/vertical flips and picture-only opacity.
- Share source/destination calculations between Skia rendering and PPTX export. Imported pictures now follow native stretch/crop framing instead of always being contained inside their shape.
- Preserve supported `srcRect`, `fillRect`, `flipH`/`flipV`, `alphaModFix`, mask, outline, rotation and alternative text. Native files with picture properties require schema 7; schemas 1–6 remain readable.
- Deduplicate repeated picture assets and byte-identical aliases into one media part across slides; read supported raster types/dimensions from their signatures rather than trusting filename extensions.
- Add a reusable explicit-apply Uno PictureLayoutEditor, Picture Format ribbon, undoable commands, and an original editable six-picture sample.
- Bound decoded-image retention by bytes and entry count, release oversized/disabled entries after drawing, use O(1) LRU updates and avoid per-frame picture shape clones. Reuse drawing paints; cache statistics and explicit image-cache release are public.
- Add geometry, malformed-input, independent OOXML schema, native round-trip, pixel/cache and real-input browser regressions. Include repeated-picture raster and PPTX benchmarks against 0.9.
- Not full PowerPoint parity: advanced masks/effects, tiling, EXIF orientation, complete group transforms, native Office qualification and other documented boundaries remain.

## 0.9.0 — Custom tab alignment and safe inspector commits

- Fix carriage-return-only custom-tab input on Uno Skia; preserve every paragraph when reopening native text/table editors or building the formatting pane.
- Preserve mixed character/paragraph ranges when native controls normalize CR/LF delimiters; ignore delayed focus/selection events from removed inline text editors.

- Add immutable Left/Center/Right/Decimal tab stops, validated invariant authoring and actual glyph-measured field placement.
- Preserve custom tabs in character/paragraph edits, presentation resizing, native DrawingML and schema-6 documents, with schemas 1–5 readable.
- Expose immutable tab-layout diagnostics, regular-stop fallback and explicit mixed-direction limitations.
- Add a reusable custom-tabs editor and append-only editable sample, with real-input browser and independent schema/rendering regressions.
- Reuse the layout engine's private drawing paint, retaining native resource ownership and canvas-state restoration.
- Guard delayed raw-text, alternative-text and numeric inspector changes against stale selections/snapshots, locked targets and reentrant duplicate commits.


## 0.8.0 — Text-body and paragraph layout

- Isolate imported paragraph defaults from sibling paragraphs, honor body-level default spacing/styles, and avoid redundant pre-concatenation of imported text.

- Add independent text insets, wrapping policy, paragraph spacing/indents, exact line advance, regular tab intervals and left-to-right justification to shared immutable layout.
- Add reusable explicit-apply Uno text-layout authoring, commands and an editable paragraph sample. Preserve mixed character styling and make sizing use the same body margins.
- Round-trip supported native DrawingML body/paragraph properties, resolve inherited body attributes, retain empty-paragraph styles on separators and reject malformed dimensions. Gate extended native properties with schema 5.
- Clip to inset content bounds, avoid empty lines from oversized initial whitespace, retain the start of overwide aligned text and honor figure-space/nonbreaking-hyphen groups.
- Avoid discarded full-token shaping before bounded emergency-wrap probes; retain existing cache/virtualization performance gates and compare against immutable 0.7.
- Extend model, rendering, import/export and real-input browser regression coverage. Not full PowerPoint or Unicode typography conformance.

## 0.7.0 — Shared shaped text and measured sizing

- Keep fractional auto-fit sizes and imported font families visible in the ribbon without creating a formatting edit; retain the value when switching ribbon tabs.

- Unify plain/mixed measurement and drawing in a reusable HarfBuzz text layout engine, used by the editor, previews, slide show, tables, chart labels and PNG/PDF.
- Preserve repeated whitespace, four-space tabs, paragraph/soft-break distinctions and grapheme boundaries; align mixed font sizes to shared baselines and wrap bulleted continuations with hanging indents.
- Add bounded native glyph-layout retention, immutable line metrics, cache statistics and font-version invalidation for text and retained pictures.
- Add explicit shrink-to-fit and rotated-top-anchored resize-shape-to-text with undo, mixed-size-preserving slide resizing, and an append-only two-slide typography sample.
- Write/read native PPTX soft breaks separately from paragraph breaks and align text insets and bullet indents with the renderer.
- Match native input insets to zoom and italic state; commit shape text with Ctrl+Enter.
- Add shaping, geometry, caching, malformed input and native interchange tests, typography browser workflows and separate warm/first-use benchmark workloads.
- Preserve the user's NuGet metadata, package icons and release automation. Full bidi/fallback/Office typography and styled native input remain unfinished.

## 0.6.0 — Responsive chrome and table design

- Replace the clipped title-bar switch with a reusable compact, keyboard-operable toggle; center the title-bar controls and constrain long document names.
- Correct compact ribbon/font minimum sizes, provide explicit overflow scrolling, retain command search through a compact flyout, and adapt quick actions, notes, status and inspector layout.
- Add a Table Design ribbon, four palette presets, first/last-column emphasis, column banding and merged-aware selective border operations with shared-edge synchronization and transactional undo.
- Persist column-style flags in native DrawingML and schema-4 native files; retain schema 1–3 reads and existing native table/chart regression gates.
- Reuse weak-keyed immutable table ownership/reading-order indexes across layout and navigation. Cache table diagnostic statistics and coalesce shell geometry reporting.
- Retain user-resized pane widths across adaptive layouts; handle pointer capture/cancellation and refresh changed geometry after XAML arrangement without forcing synchronous layout.
- Extend same-runner comparisons against 0.5 with large-table lookup/layout workloads and add production-browser responsive chrome/table-design tests.


## 0.5.0 — accelerated canvas and direct table editing

- Replace raster-backed canvas controls with direct-composited Uno `SKCanvasElement` for slide editing, previews and presentation playback.
- Recycle viewport-bounded filmstrip and sorter tiles with keyboard navigation and drag/drop reordering.
- Retain bounded local-coordinate shape pictures and static scenes, with content-aware invalidation and rendering diagnostics.
- Reconcile single-slide edits incrementally and cache selected-shape lookups.
- Add direct table-cell text input, range selection, Tab/arrow navigation and character formatting with undo/redo.
- Add content-driven row auto-fit using shared rich-text measurement, including merged spans and rotated table anchors.
- Add a public 1,000-slide sample, real-input browser stress checks, pixel/cache regressions, and reproducible same-driver CPU/raster baseline comparisons.
- No full PowerPoint parity, lossless PPTX or physical-GPU benchmark claim. See compatibility and performance documentation.

## 0.4.0 — development preview

### Added

- Immutable merged/styled table partitions, validated track weights and text ranges, merge/split, insert/delete rows and columns, and equal distribution.
- Session-independent Uno cell navigator with range/track selection, explicit text apply, formatting, margins, borders and table header/banding/total options.
- Native PPTX rectangular merge origins/continuations, per-cell formatting and row/column proportions, including bounded malformed-input rejection.
- Native schema 3 for structured tables with backward reading of schemas 1 and 2.
- Shared Skia table layout, merged-border rendering and standalone `RenderTable` API.
- Headless structure, text/style, schema, rendering and random-operation regressions, plus production browser cell/merge/track workflows.

### Fixed

- Global Replace all now includes table origins while retaining merges and character styles.
- Table shape formatting propagates only changed properties instead of overwriting custom cell fonts/colors.
- Theme accents preserve direct cell fills; shape geometry changes refresh inspector fields.
- Empty-cell paragraph defaults survive supported PPTX round trips.

### Boundaries

Not full PowerPoint parity. Referenced Office table themes/effects, arbitrary cell subdivision, drawing tables, full border conflict rules and direct slide-cell WYSIWYG editing remain unfinished. Apply cell text drafts before selection changes; unapplied drafts are not recovery state. Master/theme, advanced typography, media/SmartArt, timelines, collaboration and comprehensive accessibility qualification remain separate work.


## 0.3.0 — development preview

### Added

- Immutable multi-series categorical charts: column, horizontal bar, line, area, pie and doughnut.
- Clustered/stacked/100% stacked bar and column modes, nullable missing data and Gap/Zero/Span policies.
- Reusable Uno chart data editor with bounded quoted TSV, series management/colors, title, background, legend/data-label options and doughnut hole size.
- Native PPTX chart parts and editable embedded XLSX workbooks for all supported series/types, including sparse caches and columns beyond Z.
- Schema-2 native chart serialization with backward reading of existing documents.
- Independent schemas, round trips, malformed-cache limits, data parsing, numeric edge cases, pixel regressions and actual browser chart workflows.

### Fixed

- Missing values no longer silently become zero on supported native chart import.
- New charts use readable insertion bounds and a white chart area rather than showing unrelated objects through the graph.
- Complete single-slice circle/ring geometry and normalized axes for subnormal values.
- Area charts no longer draw line-chart markers. Canvas state is restored after chart rendering.
- Shape Fill and theme accents update chart colors without discarding series or missing data; locked chart editing is disabled.

### Boundaries

Chart axes/point styles and Office layout remain simplified. There is no combination/scatter/bubble/3D support, stacked line/area, negative percentage stacking, external workbook execution or spreadsheet-grid editor. Complete master/theme inheritance, WYSIWYG rich input, merged tables, media, advanced timelines, secure coauthoring and accessibility qualification remain unfinished.

## 0.2.0 — development preview

### Added

- Non-destructive predefined layout mapping, placeholder roles/indexes, five native PPTX layout parts, and inherited placeholder geometry lookup.
- Immutable rich-text ranges; character/paragraph formatting APIs; mixed-style Skia rendering; native PPTX text runs.
- Incremental text drafts and style-preserving Replace all, including large-match and surrogate-boundary regression coverage.
- Native DrawingML tables and single-series column charts with editable embedded XLSX workbooks.
- Bounded chart-cache import, unsupported-chart diagnostics, and independent presentation/workbook schema tests.
- Alternative-text editing and PPTX preservation.
- Windows/macOS/Linux test and desktop-build matrix, plus browser tests for selected-word formatting and content-preserving layout changes.

### Fixed

- Layout changes no longer remove unmatched text or custom artwork.
- Rotated resizing preserves the opposite anchor; vertical-side aspect resizing works without changing the existing corner-resize contract.
- Triangle hit testing rejects empty corners, and marquee bounds account for rotation.
- Text replacement and base-format changes preserve unaffected mixed character styles.

### Boundaries

This is not full PowerPoint parity. Rich-text input is not yet fully WYSIWYG while typing; complex shaping, complete master/theme inheritance, advanced native tables/charts, SmartArt/media, animation timing trees, coauthoring and full accessibility remain unfinished. See [Compatibility](docs/compatibility.md). Tagged releases, package-registry publication and signing are separate from CI package generation.

## 0.1.0 — initial development preview

- Shared immutable document/command engine, Skia rendering, and six packable reusable libraries.
- Custom Uno ribbon/editor components, browser and desktop targets.
- Basic editing, notes, local comments/recovery, slideshow, native files, PPTX subset and PNG/PDF export.
- Initial headless/browser regression tests and GitHub Pages deployment.

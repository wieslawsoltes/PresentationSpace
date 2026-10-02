# Architecture

## Dependency direction

```text
App ──> Editor.Uno ──> Controls.Uno ──> Rendering.Skia ──> Core
               │              └──────> Ribbon.Uno
               └────> Formats ────────────────────────> Core
```

Core and Formats target `net10.0` without UI dependencies. Rendering adds SkiaSharp. Uno libraries target browser WebAssembly and native desktop, with explicit Library output and generated library layout metadata. No library references App. Storage is injected through `IWorkspaceStorage`.

## Documents and transactions

Documents, slides, shapes and styles are immutable records. Collections are immutable arrays/dictionaries. Images are document assets referenced by ID. Undo retains structurally shared snapshots, not a copied image payload per pointer move. History is bounded to 150 commands.

`EditorSession` is the command boundary. Pointer transforms use `BeginGesture`, `PreviewShapes`, `CommitGesture` and cancellation. Every preview starts from original geometry and one history entry is committed on release. Selection and active slide are not serialized. Preview notifications do not schedule a recovery write on every pointer event. Sessions and renderers are confined to their owning thread.

## Rich text and input drafts

`SlideShape.Text` is the canonical string, `TextStyle` its fallback style, and `TextRanges` the ordered explicit overrides. Ranges use UTF-16 positions, are non-overlapping/in bounds, and do not split surrogate pairs. Native JSON validation enforces these invariants. Missing additive properties in earlier native documents use their defaults.

`RichText` exposes segment iteration, range formatting, replacement and reconciliation. Adjacent equal overrides are compacted; overrides equal to the base style need not be stored. Reconciliation carries ranges through existing flat-text controls and propagates changed base-style properties without removing unrelated mixed styling. Replacing arbitrary text as one wholesale change still has a narrower preservation contract than structured operations.

`RichTextEditing.ReplaceAll` locates non-overlapping matches and streams copied/replacement segments through one StringBuilder, retaining untouched styles without rebuilding the entire text for each occurrence. Paragraph formatting expands a selection to its paragraph boundaries.

`SlideViewport` keeps a private immutable text draft. TextBox changes update the draft incrementally; committing creates one document edit with final text/ranges. Range selection is retained when a ribbon command takes focus. Character and paragraph formatting share public viewport APIs. The input overlay remains a plain TextBox; mixed visual styling is rendered on the Skia canvas after commit. This is not a complete text-shaping, caret-style or rich-edit control.

## Layout mapping and geometry

Slides use 96-DPI logical units; default dimensions are 1280 × 720. `SlideFactory` tags predefined placeholders with role/index. `SlideLayoutEngine.Apply` maps compatible placeholders and moves their bounds while preserving IDs, direct text formatting and content. Unmatched objects remain in their original z-order. Blank keeps content and placeholder roles, enabling later remapping. This model is not a full slide-master/layout/theme inheritance graph.

Viewports apply fit/zoom/pan transforms and inverse-transform pointer input. Resize deltas are evaluated in local object coordinates. `ResizeRotated` translates the result so the opposite handle remains stationary in slide coordinates. `VisualBounds` computes the rotated axis-aligned extent used for marquee selection. Selection groups use identifiers rather than nested transforms.

## Rendering

`SlideRenderer` handles basic shapes, pictures, text, merged/styled tables and six categorical chart types. `DrawRichText` wraps styled word segments and uses their font metrics, color, emphasis, underline and paragraph properties. Oversized words are broken at text elements. Rendering does not claim complete complex-script shaping or Office typography equivalence.

Each renderer owns typeface and image caches; the image budget is 64 MB, and images over 16 megapixels are rejected. Native assets and font availability affect platform output. Editing is invalidation-driven. The slide-show timer runs during active basic effects and stops once they finish. Skia chooses its backend; this is not a fully GPU-resident engine or a qualified performance benchmark.

PNG exports use sized raster surfaces. PDF exports use Skia's vector document canvas. Both consume the same slide model and renderer as the editor.

## Table model and authoring

`TableSpec` holds positive row/column weights and a complete, non-overlapping grid partition. `TableCell` records exist only at merge origins; text is never duplicated in hidden covered cells. Validation checks dimensions, coverage, finite sizes/margins/borders, colors, text limits and UTF-16 style ranges. The maximum grid is 100 × 100 and the text limit is one million characters per table.

`TableModel` merges/splits, edits visible text, inserts/deletes tracks and distributes relative weights. Merging concatenates nonempty source text in row-major order with paragraph breaks and explicit styles. Splitting retains the combined text at the top-left origin; it does not reconstruct pre-merge cell contents or arbitrarily subdivide tracks. Insertions expand merges crossing the new track; deletion preserves a surviving merge's text. Native schema 3 carries the authoritative structure, while old cells/columns are compatibility projections.

`TableLayout` computes normalized edges and constant-time grid-to-origin mapping. The renderer caches up to 32 table layouts per renderer using immutable identity and bounds, clips text to each margin inset and draws only origin-cell perimeters. Conflicting shared borders are drawn narrow-to-wide; equal widths use stable cell order. This is deterministic, not a claim of full Office border-conflict precedence. `RenderTable` also draws directly without a document. Caller transforms and save state are restored.

`TableDataEditor` is independent of EditorSession. Its navigator creates at most 8 × 4 visible cell slots plus track headers. Cell text requires explicit Apply/Ctrl+Enter; structure and style commands first incorporate a valid current text draft. Snapshots are emitted as `ValueChanged`, and the host commits one undoable edit. Unapplied drafts are not recovery state. General shape formatting applies only changed properties to cell defaults/ranges; global Replace all visits visible origins; theme accents leave explicit cell fills intact.

## Chart model and authoring

`ChartSpec` contains a categorical chart type, grouping, title, legend/data-label flags, blank-data policy, hole size, categories and immutable series. Series use nullable double values; null is distinct from zero. `ChartModel.Get` adapts version-1 fields without mutation, and `Apply` updates an authoritative chart record plus legacy projections. New chart data serializes as schema 2 to prevent old builds from quietly reading only the first-series projection.

`ChartModel.Intervals` centralizes ordinary, positive/negative and percentage stacking. `ChartAxisScale` normalizes value magnitudes before choosing ticks, so subnormal inputs do not underflow into NaN axis geometry. `SlideRenderer.RenderChart` can render independently of a document. Editing, thumbnails, slide show and PNG/PDF consume that renderer. Native PPTX export consumes the same chart data and grouping rules rather than flattened primitives.

`ChartDataEditor` is a session-independent Uno control with `SetValue` and `ValueChanged`. Its bounded quoted TSV parser validates the entire draft before emitting an immutable result. The host applies the result as one history transaction. Input fields require Apply/Ctrl+Enter before pane dismissal. Type conversions validate constraints before updating, and locked objects cannot be edited through the chart pane. Existing Shape Fill changes map to the first series; theme accent recoloring retains custom series colors.

## Native PPTX structures

Formats uses BCL ZIP/XML APIs with bounded input and explicit diagnostics. The production library does not require the Open XML SDK; tests use that SDK as an independent schema validator.

Text boxes emit `a:r` runs and paragraph properties. Placeholder roles/indexes and five generated layout parts connect to a generated master/theme. Import resolves omitted placeholder geometry through layout and master parts, and has partial style/background fallback; it is not full inherited-artwork/theme reconstruction.

Structured tables emit actual `a:tbl` graphic frames, with physical grid cells carrying merge-origin spans and hMerge/vMerge continuation flags. Cell text runs, explicit fills/borders/margins and row/column proportions are retained. Import reconstructs a complete non-overlapping partition and rejects malformed span/count/continuation data before large allocations. Referenced Office table-style effects are not fully resolved. Supported charts emit chart parts with relationships, typed category/value caches and embedded XLSX packages. Each workbook uses inline strings for labels and numeric value cells; missing points have no numeric cell, and labels cannot become spreadsheet formulas. Import reads supported cached chart data only, validates counts/indexes before allocating, and diagnoses unsupported chart types. External workbooks are never retrieved.

Unsupported OOXML features are not retained as opaque package parts. Exports can therefore be structurally valid without preserving every feature of the source deck. See [Compatibility](compatibility.md).

## Reusable components and persistence

Ribbon tabs/groups/buttons have no document-engine dependency. Viewport, previews, filmstrip, sorter, formatting/selection/comment pane, notes, color palette, splitters, status bar and slide-show player are individual Uno controls. Session controls detach their event handlers when unloaded. `PresentationEditor` composes the workspace and accepts a storage adapter.

File saving and recovery are distinct: a recovery write does not mark an unsaved portable file as saved. Canceled file saves return false. No cloud account, coauthoring service or identity layer is implied by recovery or Share.

Browser diagnostics expose counts/state for tests, not document mutation APIs. Browser tests exercise keyboard input, check rendered pixels and collect screenshots; headless tests verify data invariants and export schemas. Add model/validation behavior to Core, rendering to Rendering.Skia, interchange to Formats and controls to Controls.Uno. Keep unsupported behavior visible and add regression tests before exposing commands.

## Accelerated and retained rendering (0.5)

`PresentationCanvas` uses Uno Graphics2DSK `SKCanvasElement` rather than a raster-backed XAML canvas. Shape pictures and static scenes are retained with explicit budgets and content keys; image assets stay in their own cache. `VirtualSlideView` shares a renderer among recycled filmstrip/sorter tiles. `VirtualSlideLayout` is UI-independent and reusable. See [Performance](performance.md) for cache semantics and reproducible measurement.

Direct table-cell input uses the shared `TableLayout` and inverse rotation hit testing. Drafts commit through `EditorSession`, refusing stale table replacements. Row auto-fit uses the renderer's shared wrapped rich-text line measurement and returns a new immutable table plus total height.

## Responsive chrome and indexed tables (0.6)

`CompactToggleSwitch` and `RibbonScroller` are public controls in Ribbon.Uno. The toggle uses ToggleButton state/automation and an explicitly sized visual rather than scaling or cropping a platform ToggleSwitch template. The scroller compares content width against the entire host before showing arrows, avoiding a visibility oscillation caused by the arrows themselves. Document editing remains in the shared session; the compact search is another view of the same command list.

`TableGridIndex.For(table)` validates an immutable snapshot once and shares its ownership map and sorted merge-origin reading order using a `ConditionalWeakTable`. Equal-but-distinct snapshots deliberately receive distinct indexes. No global strong-reference list keeps old undo documents alive. `TableLayout` owns independent coordinate arrays, so callers cannot corrupt another layout by modifying its public X/Y arrays. Layout creation still has a first-use validation/index cost.

Table border authoring expands ranges to merged owners, maps the requested sides, updates matching neighboring sides, and constructs a new immutable table. The model stores one border per cell side; partial modifications along a neighbor's merged side therefore fail atomically. Direct fills/text styles override palette and emphasis defaults. Native schema 4 announces the additive column-style flags to prevent older readers silently ignoring them.


## Shared text layout (0.7)

`PresentationSpace.Core.TextFlow` tokenizes paragraph/soft-line breaks, tabs, breaking spaces and explicit opportunities using extended grapheme boundaries. It deliberately does not claim complete UAX #14 line breaking. `DocumentLayout` implements validated, content-preserving text scaling and whole-presentation resizing independently of Uno and Skia.

`PresentationSpace.Rendering.Skia.TextLayoutEngine` shapes horizontal text using `SkiaSharp.HarfBuzz` 3.119.2. UTF-16 HarfBuzz buffers preserve the model's source-index convention. Font/size/emphasis changes split shaping spans; paint-only changes preserve kerning and ligatures. A ligature spanning a paint boundary uses the style at its cluster start. Multiple fonts in one line share a baseline based on the largest ascent/descent. Tabs advance to four-space stops; trailing breaking spaces do not shift center/right alignment. Blank lines occupy a measured line. Soft breaks retain paragraph settings and bullet indentation without creating another list marker.

The same immutable layout feeds `MeasureRichTextHeight`, `DrawText`, `DrawRichText`, table row fitting, chart labels, editor/thumbnail/slide-show surfaces, PNG and PDF. Returned `TextLayoutMetrics` includes content dimensions, glyph count, per-line source indexes and baselines, and `HasMixedDirection`. RTL paragraph word ordering is limited to single-direction content; this is not a Unicode bidi implementation. Font coverage is the host's responsibility, and a missing glyph can still render as a replacement box.

The engine is single-thread-affine. It owns the fonts it resolves itself and all retained native text blobs, but borrows fonts supplied by `ITypefaceResolver`. Returned metrics are managed/immutable and never expose cached native blobs. `CacheBudget` defaults to an approximate 8 MiB and `MaximumCachedLayouts` to 256. Estimates include retained text, glyphs, ranges, lines and drawing runs, not process RSS or GPU memory. Oversized entries remain temporary and are disposed after the call; limits bound retention, not peak build cost. Text is limited to one million UTF-16 code units and 256 distinct family/size/emphasis combinations per layout. Call `Clear` after mutating a non-versioned font resolver. `IVersionedTypefaceResolver.Version` provides automatic invalidation; `TypefaceRegistry.Register` updates that version.

`TextLayoutEngine.Dispose` is terminal. The pre-existing `SlideRenderer.Dispose` contract remains reusable because Uno surfaces call it during unload/reload; it releases retained resources without permanently disabling that renderer. Each independent worker should own its renderer and release it when finished.

```csharp
using var engine = new TextLayoutEngine { TypefaceResolver = myFonts };
var style = new TextStyle { FontSize = 28 };
TextLayoutMetrics metrics = engine.Measure("Measured typography", style, 600);
engine.Draw(canvas, "Measured typography", style, new RectF(20, 20, 606, 120));
// Draw uses a three-unit inset, giving the same 600-unit content width.
```

`SlideRenderer.FitTextToShape` returns `TextFitResult`, with the original shape and `Fits=false` when the requested minimum cannot fit. `FitShapeToText` changes only height and anchors the rotated top edge. These commands are explicit immutable edits, not continuous auto-fit properties. Presentation resizing scales X/Y bounds independently and text/strokes by the smaller axis ratio; mixed ranges and table cells retain their relative font sizes. Validation completes before the editor swaps the document. The inline Uno input remains a plain native TextBox, now with scaled insets, italic state and Ctrl+Enter commit.

PPTX text export writes matching inset/hanging-bullet conventions and native `a:br` for soft breaks. Import normalizes paragraph boundaries to LF and soft breaks to VT; CRLF and Unicode paragraph/line separators therefore normalize on interchange. Unsupported paragraph properties, master inheritance and arbitrary package parts are not preserved losslessly.

## Text-body and paragraph layout (0.8)

`TextBoxSpec` describes independent insets and a wrap policy. `TextBoxModel.Resolve` retains legacy three-unit text-box/twelve-unit shape padding when no spec exists; `ContentBounds` returns non-negative inset geometry. `ApplyLayout` applies explicit whole-object paragraph properties across ranges without replacing their font, color or emphasis. `TextStyle` carries paragraph spacing, nullable first/left indentation, right indentation, a regular tab interval and nullable `LineSpacingPoints` (absolute baseline advance **in slide units**, despite the historical property naming convention). Internal coordinates use 96-DPI slide units, not typographic points. `DocumentLayout` scales absolute paragraph dimensions with text and horizontal/vertical body insets with their respective geometry axes.

`TextLayoutEngine` cache keys include wrapping. Per-line immutable metrics add top/left coordinates, paragraph-end and justification information. Before spacing is added once when a hard paragraph begins; after spacing only when it ends. Soft breaks retain the paragraph's hanging indent and do not repeat bullets. Automatic wrapping can justify ordinary breaking spaces in left-to-right text; tabs and forced/final lines are not stretched. Exact advance can intentionally overlap lines; total height includes the last glyph descent. Draw clips inside the same content bounds used by fitting, preserving the caller's canvas state. Long-token layout probes exact HarfBuzz prefixes directly instead of first shaping the entire token only to discard that result.

`TextLayoutEditor` is session-independent and emits immutable `SlideShape` values. The host rejects stale/locked selections, and committing creates one undoable edit. Fields are explicit-apply; Enter in a numeric field applies the draft. The ordinary text-entry overlay remains a plain TextBox and reflects insets/wrapping, not all complex paragraph visuals during typing.

PPTX uses `a:bodyPr` insets/wrap and `a:pPr` spacing, indents and default tab size. EMUs map through 9,525 units per slide unit; hundredths of a point map through 75. Missing body attributes are resolved per attribute through available shape, layout and master properties. This is not full master/style inheritance. Fixed before/after spacing is preserved; percentage paragraph spacing is resolved to absolute font-relative values as an import approximation. Vertical flow, multiple columns and persistent auto-fit are diagnosed, not simulated. Native schema 5 prevents earlier readers from silently dropping these properties.

## Custom tab layout (0.9)

`TextTabStop` values live in immutable `TextStyle.TabStops`; positions are bounded and strictly ordered, with at most 32 per paragraph. `TextTabStops` provides parsing, formatting, scaling and pure placement arithmetic independently of rendering or Uno. `SelectionEditSnapshot` is a single-use optimistic target guard for delayed inspector edits; it checks session, slide, selection membership, immutable shape identity and locking before an ordinary undoable edit. It is not a collaboration transaction or authorization boundary.

`TextLayoutEngine` shapes a right/center/decimal-aligned following field once, reusing those source-position-specific pieces when emitting the line. Period placement comes from the HarfBuzz cluster/position data, not a guessed character width. `TextLayoutMetrics.Tabs` contains immutable field/stop coordinates and clamp state. Explicit stops are anchored relative to the inset content rectangle; line-level centering/justification does not subsequently translate these anchors. Unsupported RTL field behavior is surfaced via `HasUnsupportedTabDirection`. Retained layout cache keys include the immutable stop arrays; changing stops invalidates layout and retained shape content. The engine is single-thread-affine, owns its private reusable `SKPaint`, disposes it with native blobs, and restores caller canvas state.

`TextLayoutEditor` emits immutable layout snapshots through its existing explicit-apply event. It has no session dependency. The editor validates source identity before committing the draft, retains character styles, and exposes commands and the `Custom Tabs` ribbon button. PPTX import/export uses native `a:tabLst/a:tab` with `l`, `ctr`, `r`, `dec`; explicit empty lists clear inherited tabs instead of inheriting a previous paragraph's local values. Tables use the same text interchange path.

## Linear gradient fills and inherited colors (0.11)

`GradientFill` / `GradientStop` / `GradientModel` are immutable UI-independent model and geometry APIs. The drawing model attaches optional fills to `SlideShape`, `Slide` and `TableCell`. Null uses the existing solid-fill behavior. Validation rejects invalid angles, unordered/out-of-range stops and unsupported shape kinds; equal positions are intentional. `GradientFillEditor` emits immutable values for independent hosts; FormatPane and table adapters supply transactions. The editor appends a sample without replacing current content.

Skia linear shaders are shared by immutable fill and geometry keys. Shader references owned by retained SKPictures are separate from the bounded shader LRU. Shape-cache dependencies include gradient and rotation; scene/thumbnail dependencies include background gradient. Render paths keep host clip/matrix/save state and release owned resources without breaking the existing reusable unload lifecycle.

`PptxCodec` resolves direct fills, placeholder inheritance and style references before assigning explicit model values. `DrawingColors` is scoped to the related master theme plus the applicable color map, and applies the documented ordered transform subset. Layout/master/theme XML is reused through a per-import bounded cache (64 parts, 4 MiB serialized-byte total; not an XML-object heap limit). External relationships remain excluded. Import resolves colors rather than preserving a full theme/style graph; export writes explicit editable gradient stops.

## Shared outline model and rendering (0.12)

Core exposes immutable `StrokeSpec`, `StrokeDashSegment`, `LineEnd`, `LineDirection` and `StrokeSettings`, plus `StrokeModel` for bounds checking, semantic comparison, dash input, endpoint geometry and conservative line hit testing. The `Outline` property is optional: legacy shapes keep their previous cap/join/arrow defaults. Native persistence selects schema 9 when outline/direction state or zero-extent lines require it. Presentation resizing scales physical stroke width while retaining relative dash and marker sizes.

`SlideRenderer` configures independent outline paints, shares the existing gradient shader engine, and paints endpoint decorations as undashed geometry. Host rotation, clipping and object opacity remain with the caller. Public `DrawLine` validates inputs; ordinary rendering uses the already validated document model. Direct draws reuse private paints/marker paths; a bounded dash-effect LRU keys preset/custom immutable pattern and width. Cache clearing releases explicit native references, while retained Skia pictures can keep their own references until picture eviction. Stroke/direction state participates in picture invalidation. Uncached drawing uses slide coordinates directly, avoiding positional shape clones.

`PptxCodec.Strokes` handles native single-stroke resolution and export, independently of UI. It applies lower-precedence placeholder/theme properties before sparse direct properties, and never interprets endpoint decorations on closed shapes as a change in geometry type. The line-coordinate reader preserves zero extents and validates exact integer source coordinates. Full connector routing is not part of the straight-line model.

`OutlineEditor` has no session dependency. The host captures a `SelectionEditSnapshot` before applying either its whole draft or the independent outline-gradient draft, suppresses only its own in-place refresh, and recaptures after success. Automation IDs for gradients are configurable so multiple independent editors can coexist without duplicate field identifiers. All model changes use normal undo/redo. `StrokeSample` is original editable content; browser and headless diagnostics exercise the production engines rather than separate demo renderers.

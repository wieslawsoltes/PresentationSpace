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

`SlideRenderer` handles basic shapes, pictures, text, uniform tables and six categorical chart types. `DrawRichText` wraps styled word segments and uses their font metrics, color, emphasis, underline and paragraph properties. Oversized words are broken at text elements. Rendering does not claim complete complex-script shaping or Office typography equivalence.

Each renderer owns typeface and image caches; the image budget is 64 MB, and images over 16 megapixels are rejected. Native assets and font availability affect platform output. Editing is invalidation-driven. The slide-show timer runs during active basic effects and stops once they finish. Skia chooses its backend; this is not a fully GPU-resident engine or a qualified performance benchmark.

PNG exports use sized raster surfaces. PDF exports use Skia's vector document canvas. Both consume the same slide model and renderer as the editor.

## Chart model and authoring

`ChartSpec` contains a categorical chart type, grouping, title, legend/data-label flags, blank-data policy, hole size, categories and immutable series. Series use nullable double values; null is distinct from zero. `ChartModel.Get` adapts version-1 fields without mutation, and `Apply` updates an authoritative chart record plus legacy projections. New chart data serializes as schema 2 to prevent old builds from quietly reading only the first-series projection.

`ChartModel.Intervals` centralizes ordinary, positive/negative and percentage stacking. `ChartAxisScale` normalizes value magnitudes before choosing ticks, so subnormal inputs do not underflow into NaN axis geometry. `SlideRenderer.RenderChart` can render independently of a document. Editing, thumbnails, slide show and PNG/PDF consume that renderer. Native PPTX export consumes the same chart data and grouping rules rather than flattened primitives.

`ChartDataEditor` is a session-independent Uno control with `SetValue` and `ValueChanged`. Its bounded quoted TSV parser validates the entire draft before emitting an immutable result. The host applies the result as one history transaction. Input fields require Apply/Ctrl+Enter before pane dismissal. Type conversions validate constraints before updating, and locked objects cannot be edited through the chart pane. Existing Shape Fill changes map to the first series; theme accent recoloring retains custom series colors.

## Native PPTX structures

Formats uses BCL ZIP/XML APIs with bounded input and explicit diagnostics. The production library does not require the Open XML SDK; tests use that SDK as an independent schema validator.

Text boxes emit `a:r` runs and paragraph properties. Placeholder roles/indexes and five generated layout parts connect to a generated master/theme. Import resolves omitted placeholder geometry through layout and master parts, and has partial style/background fallback; it is not full inherited-artwork/theme reconstruction.

Uniform tables emit actual `a:tbl` graphic frames. Supported charts emit chart parts with relationships, typed category/value caches and embedded XLSX packages. Each workbook uses inline strings for labels and numeric value cells; missing points have no numeric cell, and labels cannot become spreadsheet formulas. Import reads supported cached chart data only, validates counts/indexes before allocating, and diagnoses unsupported chart types. External workbooks are never retrieved.

Unsupported OOXML features are not retained as opaque package parts. Exports can therefore be structurally valid without preserving every feature of the source deck. See [Compatibility](compatibility.md).

## Reusable components and persistence

Ribbon tabs/groups/buttons have no document-engine dependency. Viewport, previews, filmstrip, sorter, formatting/selection/comment pane, notes, color palette, splitters, status bar and slide-show player are individual Uno controls. Session controls detach their event handlers when unloaded. `PresentationEditor` composes the workspace and accepts a storage adapter.

File saving and recovery are distinct: a recovery write does not mark an unsaved portable file as saved. Canceled file saves return false. No cloud account, coauthoring service or identity layer is implied by recovery or Share.

Browser diagnostics expose counts/state for tests, not document mutation APIs. Browser tests exercise keyboard input, check rendered pixels and collect screenshots; headless tests verify data invariants and export schemas. Add model/validation behavior to Core, rendering to Rendering.Skia, interchange to Formats and controls to Controls.Uno. Keep unsupported behavior visible and add regression tests before exposing commands.

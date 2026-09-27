# Compatibility and limitations

PresentationSpace 0.4 is a development preview, not a complete or pixel-exact PowerPoint clone, a byte-preserving OOXML editor, or a certified Office replacement.

## Format matrix

| Feature | Native `.pspace` | PPTX import | PPTX export |
|---|---|---|---|
| Slide size/order/hidden state | Preserved | Supported | Supported |
| Text | String plus explicit style ranges | Basic paragraphs/runs, font, size, color and emphasis | Editable native text runs |
| Mixed rich text | Preserved | Supported subset | Supported subset, including paragraph alignment/bullets/spacing |
| Basic preset shapes | Preserved | Known subset; other presets approximated | Editable presets |
| Rotation | Preserved | Basic object rotation | Supported |
| Alternative text | Preserved | `cNvPr` descriptions | `cNvPr` descriptions |
| Raster pictures | Embedded assets | PNG/JPEG/GIF/WebP subset | Embedded pictures |
| Groups | Shared selection identifiers | Flattened; complex group transforms can differ | Flattened |
| Tables | Merge-origin partition, track weights, cell text/styles/margins/borders | Supported rectangular merges, geometry and explicit cell formatting | Native DrawingML origins/continuations and supported explicit styling |
| Charts | Immutable categories/series, nullable values, type and basic options | Column, bar, line, area, pie and doughnut caches; supported grouping/options | Native chart parts with every series and editable embedded XLSX workbooks |
| Placeholder roles/indexes | Preserved | Roles/indexes and layout/master geometry fallback | Native placeholders and five predefined layout parts |
| Speaker notes | Preserved | Body notes | Supported |
| Local comments | Preserved | Not imported | Omitted with warning |
| Transitions | Preview settings | Basic fade/push/wipe | Basic transitions; exact timing not preserved |
| Object animations | Preview settings | Not imported | Omitted with warning |
| Master/theme hierarchy | Not a complete hierarchy | Partial geometry/default-style/background fallback | Generated master/theme; five predefined layouts |
| SmartArt, OLE, macros, media | Not modeled | Not executed; unsupported content omitted | Not exported |

PPTX import emits a compatibility warning. Unsupported content is not retained as opaque package parts for future lossless round-trip. **Keep the original PPTX.** Schema validation proves package structure for tested exports, not exact rendering, native PowerPoint open/save repair behavior or complete interoperability.

## Text and editing

Mixed character styles survive model edits, native serialization, supported PPTX interchange, Skia rendering and PNG/PDF output. Range indexes are UTF-16 and cannot split surrogate pairs. `ReplaceAll` preserves styles between matches; typing drafts reconcile changes incrementally before one undoable commit. Bold/italic/underline toggles apply one consistent state to the selected range. Alignment, bullets and line spacing apply to affected paragraphs.

The inline input control is still a **plain Uno TextBox**: it does not display every mixed run style during typing. The slide canvas shows mixed formatting after commit. With no characters selected, character formatting applies to the selected object rather than a separate future-insertion style. Independent styling of trailing empty paragraphs, advanced lists, fields, hyperlinks, tabs, baseline controls, full bidirectional layout, shaping/ligatures and typography equivalence with Office are not implemented or qualified. Font availability changes layout across operating systems.

Layout switching no longer deletes text placeholders. It maps compatible roles, preserves matched identity and direct formatting, and retains unmatched placeholders and custom objects. Blank retains existing content; returning to a layout can remap its placeholders. Legacy factory slides are recognized from known layout names and matching geometry only. This is not full PowerPoint layout/master inheritance or a master editor. Factory hint strings are ordinary text until replaced, not a separate placeholder prompt system.

Rotated resizing pins the opposite handle. Marquee selection uses rotated axis-aligned visual extents; that is not exact polygon intersection. Grouping links selection rather than creating nested coordinate systems. Front/back commands move to layer extremes; alignment/distribution do not provide a complete rotated/grouped arrangement engine.

## Table and chart fidelity

Tables now preserve rectangular merges, normalized row/column proportions, mixed character styles, margins, vertical/paragraph alignment and explicit solid cell fills and solid/dashed/dotted borders. Model operations include cell/range formatting, merge/split, row/column insertion/deletion and equal track distribution. Merge concatenates nonempty text in row-major order. Split restores the underlying grid and keeps combined text at the top-left; it is not arbitrary subdivision or recovery of pre-merge cell contents. Use Undo to restore pre-merge contents.

Native export writes correct DrawingML physical cells and continuation flags; import reconstructs the visible origin partition. Dimensions are limited to 100 rows × 100 columns and total visible cell text to one million characters. Overlaps, uncovered cells, orphaned continuations and invalid extents, margins, borders and text ranges are rejected. Covered continuation text is not retained if a malformed/unusual source stores hidden text there; a warning is emitted.

Referenced Office table styles and per-master theme effects are not fully resolved. Diagonal/compound/custom borders, picture/gradient/pattern fills, cell 3D effects, rotated/vertical cell text, formula/data links and Office-identical border/text-layout precedence are not implemented. Shared border conflicts use deterministic wider-border precedence. Explicit imported fills are retained, so toggling banding/header options does not replace a cell's direct formatting; reset cell formatting to use table defaults.

The Uno table editor has a paged cell navigator, not a full Excel-style grid or direct slide-cell rich editor. Text input is a plain TextBox; **apply before changing selection or closing the pane**. Other table actions include the current valid text draft, but unapplied text is not recovery state. Character formatting from this pane applies to whole selected cells; imported/model-authored mixed ranges are preserved. Cell margins can be set uniformly in the pane or individually through the model. Structured tables serialize as schema 3; schemas 1 and 2 remain readable, and older builds deliberately reject schema 3.

Native chart export creates chart relationships, typed caches and an embedded workbook for column, horizontal bar, line, area, pie and doughnut charts. Column/bar grouping supports clustered, stacked and 100% stacked. Other chart types use standard grouping. Positive and negative ordinary stacks accumulate separately. Pie/doughnut charts require one series; those types and 100% stacked charts require non-negative data. Unsupported conversions fail visibly without discarding series.

The model supports up to 32 series, 10,000 categories and 100,000 numeric positions per chart. Numeric values are finite doubles bounded to ±1e30. Missing values remain null, absent cache points and absent workbook cells rather than zero. Gap/Zero/Span are preserved; spanning affects line/area connections. New charts use a readable slide-relative size, a title/legend and white background. Chart-area color or transparency round-trips; legacy version-1 charts retain their transparent background. All-zero or all-missing circular charts display an empty-data state. Normalized axis calculations handle subnormal finite values without producing invalid geometry.

Chart labels/names use string cells, including values beginning with `=`; no spreadsheet formulas execute. Workbook addresses work beyond column Z. Import reads cached values only; it does not execute embedded workbooks or fetch external data. Multiple supported series are preserved. Duplicate indexes/order and invalid counts are rejected before numeric-array allocation; combination charts, conflicting series categories and unsupported families are diagnosed rather than partially imported.

This is not complete Excel/PowerPoint chart fidelity. Combination, scatter, bubble, radar, stock, surface and 3D charts; stacked line/area; multi-series circular charts; negative 100% stacks; multi-level categories; advanced axes, custom point/marker/data-label formatting, trendlines, error bars, live data links and Office-identical layout are unsupported. Circular slices use a fixed application palette, not imported per-point styles. Legends display the first 16 entries with an overflow count; labels are thinned on dense category axes and data labels are omitted above 50 numeric positions. Native Office may lay out the same chart differently.

The Uno data editor is an explicit-apply quoted TSV control, not a spreadsheet grid. Paste tab-separated cells with a header, category labels in the first column and one column per series. Empty numeric cells stay missing. Apply (or Ctrl+Enter in the data field) commits the title, hole size and data as one undoable edit. Apply before closing the pane or changing selection; unapplied text is not part of the document/recovery snapshot. Type, grouping, legend, label and color commands apply the current valid draft. Formula text in numeric cells is rejected.

New chart records serialize as native schema version 2. Earlier native documents remain readable; older application builds intentionally reject version 2. Legacy `Values`/`Labels` are compatibility projections, not the authoritative chart model. Keep the original PPTX because unsupported parts are not preserved opaquely.

## Remaining application boundaries

Master artwork, complete theme/style inheritance, custom layout round-trip and authoring remain unfinished. Unsupported custom layouts are not recreated on export. Theme import is not a full per-master color/font-map resolver. SmartArt, media/audio/video, OLE, macros, `.ppt`, full inking, arbitrary paths, attached connectors, cropping/masks, advanced picture effects and 3D are absent.

Slide-show mode fills the application viewport, not a second-display presenter console. Object animation order is a simple sequential preview, not an Office timing tree. Comments and recovery are local; Share exports a file. There is no identity provider, shared-document server, access-control system, CRDT, cloud version history or simultaneous coauthoring. Alternative text and automation labels are useful additions, not comprehensive screen-reader or accessibility conformance.

## Defensive limits and verification

Native/PPTX input: 64 MB. PPTX expansion: 128 MB total, 32 MB per part and 10,000 parts. Native documents: 2,000 slides and 20,000 shapes. Charts: 32 series, 10,000 categories, 100,000 numeric positions, validated before numeric-array allocation; TSV input at most 2,097,152 characters with 32,767-character cells. Raster decode: 16 megapixels; image insertion: 20 MB. PNG export: at most 32 megapixels. DTDs and external retrieval are prohibited. Invalid/duplicate identifiers, non-finite geometry and malformed rich-text ranges are rejected.

These are defensive limits, not performance guarantees. Headless tests cover data preservation, native chart/workbook/table schemas, rendering and geometry. Browser tests use actual keyboard input against the published Uno app. CI also compiles desktop targets; compilation is not equivalent to native interactive UI testing. Large-document performance, every browser/platform, touch-only operation, assistive technologies and PowerPoint-native visual/round-trip qualification need broader testing.

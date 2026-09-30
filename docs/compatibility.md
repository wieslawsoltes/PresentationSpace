# Compatibility and limitations

PresentationSpace 0.9 is a development preview, not a complete or pixel-exact PowerPoint clone, a byte-preserving OOXML editor, or a certified Office replacement.

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

The inline input control is still a **plain Uno TextBox**: it does not display every mixed run style during typing. The slide canvas shows mixed formatting after commit. With no characters selected, character formatting applies to the selected object rather than a separate future-insertion style. Independent styling of trailing empty paragraphs, advanced lists, fields, hyperlinks, custom tab stops, baseline controls, full bidirectional layout and typography equivalence with Office remain unfinished. Horizontal HarfBuzz shaping/ligatures are implemented in 0.7, with the limitations below. Font availability changes layout across operating systems.

Layout switching no longer deletes text placeholders. It maps compatible roles, preserves matched identity and direct formatting, and retains unmatched placeholders and custom objects. Blank retains existing content; returning to a layout can remap its placeholders. Legacy factory slides are recognized from known layout names and matching geometry only. This is not full PowerPoint layout/master inheritance or a master editor. Factory hint strings are ordinary text until replaced, not a separate placeholder prompt system.

Rotated resizing pins the opposite handle. Marquee selection uses rotated axis-aligned visual extents; that is not exact polygon intersection. Grouping links selection rather than creating nested coordinate systems. Front/back commands move to layer extremes; alignment/distribution do not provide a complete rotated/grouped arrangement engine.

## Table and chart fidelity

Tables now preserve rectangular merges, normalized row/column proportions, mixed character styles, margins, vertical/paragraph alignment and explicit solid cell fills and solid/dashed/dotted borders. Model operations include cell/range formatting, merge/split, row/column insertion/deletion and equal track distribution. Merge concatenates nonempty text in row-major order. Split restores the underlying grid and keeps combined text at the top-left; it is not arbitrary subdivision or recovery of pre-merge cell contents. Use Undo to restore pre-merge contents.

Native export writes correct DrawingML physical cells and continuation flags; import reconstructs the visible origin partition. Dimensions are limited to 100 rows × 100 columns and total visible cell text to one million characters. Overlaps, uncovered cells, orphaned continuations and invalid extents, margins, borders and text ranges are rejected. Covered continuation text is not retained if a malformed/unusual source stores hidden text there; a warning is emitted.

Referenced Office table styles and per-master theme effects are not fully resolved. Diagonal/compound/custom borders, picture/gradient/pattern fills, cell 3D effects, rotated/vertical cell text, formula/data links and Office-identical border/text-layout precedence are not implemented. Shared border conflicts use deterministic wider-border precedence. Explicit imported fills are retained, so toggling banding/header options does not replace a cell's direct formatting; reset cell formatting to use table defaults.

The Uno table pane has a paged cell navigator, not a full Excel-style grid. Direct on-slide cell selection and entry are also available by double-clicking a cell or through command search. Pane text input is a plain TextBox; **apply before changing selection or closing the pane**. The separate on-slide overlay commits on focus loss or Ctrl+Enter, cancels its current draft on Escape, and is flushed by file save/export. Automatic recovery includes committed document changes only; it does not capture active input drafts. Other table actions include the current valid text draft, but unapplied text is not recovery state. Character formatting from this pane applies to whole selected cells; imported/model-authored mixed ranges are preserved. Cell margins can be set uniformly in the pane or individually through the model. Structured tables serialize as schema 3; schemas 1 and 2 remain readable, and older builds deliberately reject schema 3.

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

## 0.5 performance and table-input additions

Direct on-slide table-cell entry, keyboard/range navigation, character formatting and explicit content-driven row auto-fit are implemented. The navigator remains available; direct input is a plain native TextBox overlay, not fully styled input. Row fit follows PresentationSpace's shared text metrics, not complete Office typography.

Filmstrip/sorter realization is viewport-bounded, rendering uses the Uno Skia compositor with bounded retained pictures, and one-slide edits avoid whole-deck reconciliation. Synthetic benchmark and browser stress-test methodology is in [Performance](performance.md). These changes are not full large-document, hardware-GPU, startup, mobile/touch or accessibility qualification. Full master/theme inheritance, complex shaping, SmartArt/media, Office timing trees, advanced charts, secure coauthoring and lossless unsupported PPTX preservation remain unfinished.

## 0.6 table-design and UI scope

The editor adds four built-in palettes, first/last-column emphasis, column banding, and all/outside/inside/horizontal/vertical/individual-side/no-border scopes. Direct cell fills and text styles are preserved, not silently cleared by palette selection. Shared border edits update both neighbors; a selection that cuts only part of a neighboring merged side is rejected. Segment-specific merged-side borders, full Office theme table styles and all style-precedence rules remain unfinished. Native documents with these column flags use schema 4 (schemas 1–3 remain readable); ordinary structured tables without the new flags remain schema 3. Native DrawingML `firstCol`, `lastCol` and `bandCol` round-trip; this does not make unsupported PPTX parts lossless.

Responsive shell changes cover compact title controls, constrained document titles, ribbon scrolling, search access and inspector/notes/status adaptation. This is not pixel-exact PowerPoint UI parity or a full screen-reader/touch/browser certification. Table text still uses a plain native input overlay, and local automatic recovery still retains committed document state rather than an active input draft.

User-resized filmstrip and docked inspector widths are retained for the current editor instance when resizing the window or closing/reopening a pane. They are not persisted across application restarts. Browser geometry diagnostics refresh after XAML arrangement and publish only changed snapshots; model/table diagnostics are not recomputed by layout callbacks.


## 0.7 text layout and sizing scope

Plain/mixed shape text, table text and chart labels share HarfBuzz/Skia measurement and drawing. Repeated spaces, four-space tab stops, mixed-size baselines, hanging bullets and extended-grapheme-safe emergency wrapping are implemented. CRLF/CR/LF/U+2029 are paragraph separators; VT/U+2028 are soft breaks. PPTX writes separate paragraphs and `a:br`; import normalizes them to LF and VT respectively. Trailing breaking spaces remain in the document but do not extend aligned visual line widths. NBSP, narrow NBSP and word-joiner groups are not emergency-split and can overflow a narrow box. Since 0.8, glyph overhang is clipped to the inset text content rectangle.

This is not complete Unicode UAX #9/UAX #14, mixed-direction paragraph layout, CJK typography, script/language itemization or automatic per-grapheme font fallback. Single-direction RTL ordering is supported, but mixed-script/direction text needs further work. `HasMixedDirection` exposes detected mixed strong directions. HarfBuzz can form available ligatures/joining glyphs only when the selected host font covers that script; no font coverage is invented. Paint changes inside a glyph cluster use that cluster's start style. Native input still shows a plain TextBox rather than all run styles; advanced lists, paragraph fields, custom tab stops, complete Office line-height rules and trailing-empty-paragraph styling remain unfinished.

Text fitting is an explicit undoable action. Shrink-to-fit uses uniform font scaling down to the requested minimum (8 units in the editor); it reports inability to fit instead of deleting content. Resize-shape-to-text changes height, retaining width and the rotated top edge. Table cells use the separate row auto-fit command. These are not persistent `normAutofit`/`spAutoFit` properties. Slide-size changes scale all shape geometry and mixed text/table styling; chart-specific label sizes are not modeled as authorable style fields, and resizing is not Office's full responsive layout system.

The original two-slide typography sample is appended to the current document; it does not discard existing slides. The current native schema remains 4 when required by table flags, with no new text-layout serialization schema. The same renderer feeds editing, thumbnails, slide show, PNG and PDF, but native Office visual/open-save qualification is still required for equivalence claims.

## 0.8 text-body and paragraph layout

Implemented: independent shape-text margins, wrapping/no-wrap, fixed paragraph before/after spacing, paragraph left/right and first-line indents, hanging marker offsets, explicit absolute line advance, regular tab intervals and ordinary-space justification for automatically wrapped left-to-right lines. Soft/forced and final lines are not justified, and tabs are not stretched. No-wrap still honors explicit hard/soft breaks. Margins that consume the shape leave an empty drawing area; text fitting fails without modifying content. Exact line advance can intentionally overlap glyphs; it is not the complete Office line-height algorithm.

The **Text layout** pane applies its explicit paragraph settings to the whole object, preserving mixed character styles. Use existing text selection/ribbon commands for paragraph-scoped edits. The pane's numeric units are slide units (96 per inch), not points. Apply before changing selection or closing it; unapplied property drafts are not saved or recovered. Body controls are for ordinary text-bearing shapes; tables retain their separate cell-margin editor and shared paragraph rendering. Native text input reflects insets and wrapping but not complete justified/mixed paragraph visuals while typing.

Supported DrawingML body/paragraph properties round-trip. Missing body attributes can inherit from available layout/master placeholder bodies. Before/after spacing expressed as percentages is approximated as absolute values relative to the paragraph font, not retained as a live percentage. Vertical/rotated text flow, text columns, persistent autofit, arbitrary custom tab stops/leaders, numbered/multilevel list semantics, full theme inheritance and every Office paragraph precedence rule remain unfinished. Body dimensions and paragraph spacing/indents are bounded and invalid numeric input is rejected. Native documents using any new property require schema 5; older schemas remain readable. This does not make arbitrary PPTX imports lossless.

Complete bidi/line-break conformance, automatic script/font fallback, fully styled input, master/layout/theme authoring, advanced objects/media/timing/coauthoring and native Office visual qualification remain separate gaps. Retain original PPTX files.

## 0.9 custom tab stops

Left, Center, Right and Decimal custom tab stops are modeled, editable and rendered for horizontal left-to-right fields. Each stop is between 0 and 10,000 96-DPI slide units, strictly increasing, with at most 32 entries. The first custom stop strictly after the current position wins; after the last custom stop, the regular interval applies. Stops are relative to the text content left edge (after text-box/cell insets), not the object's outer border. Decimal alignment uses the first ASCII period; values without a period align at their right edge. Locale-specific decimal/grouping marks, leaders, bar tabs, RTL/mixed-direction tab layout and exact Office field-wrapping behavior are not implemented. Layout metrics identify unsupported tab-direction content; no general bidi conformance is claimed.

Text before/after a tab retains its mixed fonts, sizes, colors and underline. An alignment that would move backwards is clamped to the current position, reported in metrics; an out-of-bounds stop is clipped inside the content rectangle, not silently moved onto the slide. Empty custom tab fields retain their positions. Overwide fields can wrap using normal line-breaking rules. Lines with explicit tabs retain absolute tab anchors rather than applying paragraph center/right offsets or justification again.

Native DrawingML tab lists and alignment flags round-trip in supported shape and table text. Absent lists inherit available body defaults, while an explicit empty list clears tabs. Per-layout/per-master paragraph inheritance and preservation of all unsupported package parts are still incomplete. Native custom stops require schema 6; ordinary older-feature documents keep the lowest applicable schema, and schemas 1–5 remain readable. Original PPTX files should be retained.

The custom-tab text field is an explicit-apply authoring control, not an on-canvas ruler or full Office Tabs dialog. One `position alignment` per line, invariant decimal numbers; an omitted alignment means Left. Ctrl+Enter or Apply commits only a valid complete draft. Settings apply to the whole shape in the pane; existing selection-aware commands and the immutable model provide per-paragraph differences. Changing selection discards unapplied layout drafts; recovery retains committed document state, not active drafts.

Raw inspector text/alternative-text/numeric focus-loss commits now check the captured immutable target selection. Stale/locked/changed targets are not overwritten. This guard is single-use and safe against reentrant focus callbacks, but does not implement multi-user concurrency or persist pane drafts across application restarts. Full PowerPoint parity, master/theme authoring, advanced typography/object/media/timing/coauthoring and native Office/desktop/physical-GPU qualification remain unfinished.


Native tab-definition entry accepts CR, LF and CRLF line separators; formatted definitions use LF. Multiline controls enable return handling before assigning existing content, so reopening text, cell or inspector input does not truncate later paragraphs. Separator-only CR/LF normalization preserves character/paragraph ranges, including offsets after collapsed CRLF pairs. A CRLF whose two code units have different styles takes the first code unit's style when collapsed. This does not provide fully styled native input or general rich-text merging of multiple unrelated replacements.

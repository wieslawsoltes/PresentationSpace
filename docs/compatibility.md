# Compatibility and limitations

PresentationSpace 0.2 is a development preview, not a complete or pixel-exact PowerPoint clone, a byte-preserving OOXML editor, or a certified Office replacement.

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
| Tables | Cell strings and uniform grid | Cell text; styling, dimensions and spans simplified | Native DrawingML table with the app's uniform style |
| Charts | One-series column chart | Cached values/labels/color for one unstacked column series | Native chart part with editable embedded XLSX workbook |
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

Native table export preserves an editable table structure, with the app's uniform cells, header/banding and basic text style. Import does not retain arbitrary row heights, column widths, cell formatting, merged cells, table themes or formula/data links.

Native chart export creates chart relationships, typed caches and an embedded workbook. Chart labels use string cells, including values beginning with `=`; the exporter does not turn labels into formulas. Import supports one unstacked column series and cached values. Multiple series, stacked/percentage charts, other chart families, uncached workbooks, full axes/legend/data-label styling and live data links remain unsupported. Missing cached points become zero with a warning. Unsupported chart types are omitted with a warning rather than silently selecting their first series. External data and assets are not fetched.

## Remaining application boundaries

Master artwork, complete theme/style inheritance, custom layout round-trip and authoring remain unfinished. Unsupported custom layouts are not recreated on export. Theme import is not a full per-master color/font-map resolver. SmartArt, media/audio/video, OLE, macros, `.ppt`, full inking, arbitrary paths, attached connectors, cropping/masks, advanced picture effects and 3D are absent.

Slide-show mode fills the application viewport, not a second-display presenter console. Object animation order is a simple sequential preview, not an Office timing tree. Comments and recovery are local; Share exports a file. There is no identity provider, shared-document server, access-control system, CRDT, cloud version history or simultaneous coauthoring. Alternative text and automation labels are useful additions, not comprehensive screen-reader or accessibility conformance.

## Defensive limits and verification

Native/PPTX input: 64 MB. PPTX expansion: 128 MB total, 32 MB per part and 10,000 parts. Native documents: 2,000 slides and 20,000 shapes. Chart caches: 10,000 points, validated before allocation. Raster decode: 16 megapixels; image insertion: 20 MB. PNG export: at most 32 megapixels. DTDs and external retrieval are prohibited. Invalid/duplicate identifiers, non-finite geometry and malformed rich-text ranges are rejected.

These are defensive limits, not performance guarantees. Headless tests cover data preservation, native chart/workbook/table schemas, rendering and geometry. Browser tests use actual keyboard input against the published Uno app. CI also compiles desktop targets; compilation is not equivalent to native interactive UI testing. Large-document performance, every browser/platform, touch-only operation, assistive technologies and PowerPoint-native visual/round-trip qualification need broader testing.

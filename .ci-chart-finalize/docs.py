from pathlib import Path
import hashlib

# Documentation edits are also revision checked before application.
def patch(path, expected, edits):
    p = Path(path)
    original = p.read_bytes()
    actual = hashlib.sha1(b'blob ' + str(len(original)).encode() + b'\0' + original).hexdigest()
    assert actual == expected, f'Unexpected source revision: {path}'
    text = original.decode('utf-8')
    for start, end, replacement in reversed(edits):
        text = text[:start] + replacement + text[end:]
    p.write_text(text, encoding='utf-8')

patch('CHANGELOG.md', '1db13419458b723cce7ab5489eba9bffb6856b1e', [(12,12,'''\n## 0.3.0 — development preview

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
''')])
patch('README.md', '46ef198f31ebc5ad3b754c462589bc81406ae241', [
(994,1337, '> **0.3 development preview.** PresentationSpace is inspired by the PowerPoint desktop workflow. It is not Microsoft software, a pixel-exact reproduction, a complete PowerPoint implementation, or a lossless editor for arbitrary PPTX files. Keep original imported presentations. Supported functionality and remaining gaps are documented below.\n'),
(1338,1360,'## What is new in 0.3\n'),
(1361,1697,'**Six chart types, multiple series.** Create column, horizontal bar, line, area, pie and doughnut charts. Column/bar charts support clustered, stacked and 100% stacked grouping. Add/remove series, change colors, edit titles, toggle legends/data labels, set the chart background, and set doughnut hole size from the reusable chart pane.\n'),
(1698,2114,'**Editable data without flattening.** Paste quoted tab-separated category/series data and apply one undoable edit. Blank numeric cells remain missing values; Gap, Zero and Span control their display. Native PPTX export writes real chart parts and editable embedded XLSX workbooks for every supported series. Formula-looking labels remain text cells. Supported chart caches round-trip without replacing missing points with zero.\n'),
(2115,2458,'**Shared, bounded implementation.** Core supplies immutable chart data, validation, tabular parsing, stacking and normalized axes. Skia renders those same types for the editor, slide show, PNG and PDF. The Uno chart editor has no session dependency and emits validated snapshots. Import validates series/cache limits before allocation and reports unsupported charts instead of taking only their first series.\n'),
(2459,2725,'The 0.2 content-preserving layouts, mixed text styles, native tables, rotated resizing and alternative text remain available. See the [changelog](CHANGELOG.md) for earlier work and [compatibility](docs/compatibility.md) for exact chart and application boundaries.\n'),
(3251,3373,'| Objects | Text, basic geometric shapes, lines/arrows, raster pictures, uniform tables and six categorical chart types |\n'),
(5967,6107,'| `PresentationSpace.Core` | Immutable documents, rich-text operations, layouts, chart data/axes, geometry, commands, selection and undo | No UI framework |\n'),
(6224,6343,'| `PresentationSpace.Rendering.Skia` | Slides, six chart types, mixed text, images, thumbnails, selection, PNG/PDF | Core + SkiaSharp |\n'),
(6440,6576,'| `PresentationSpace.Controls.Uno` | Viewport, filmstrip, sorter, inspector, chart data editor, notes, splitters, status and slide show | Uno + renderer |\n'),
(8161,8161,'''### Use charts independently

```csharp
var data = new ChartSpec
{
    Kind = ChartKind.Line,
    Title = "Quarterly performance",
    Categories = ["Q1", "Q2", "Q3"],
    Series = [
        new() { Name = "Actual", Color = "#D35230", Values = [10, null, 25] },
        new() { Name = "Plan", Color = "#4472C4", Values = [15, 18, 22] }
    ]
};
var shape = ChartModel.Apply(new SlideShape
{
    Name = "Performance chart", Bounds = new(100, 180, 960, 540)
}, data);
session.Insert(shape);

// Or draw directly on an existing Skia canvas, without an editor or document.
renderer.RenderChart(canvas, data, new RectF(0, 0, 640, 360), new TextStyle());
```

`ChartDataEditor.SetValue(data)` loads the standalone Uno control; `ValueChanged` emits a validated `ChartSpec`. `ChartTabularData.Format/Parse` support spreadsheet copy/paste. `ChartModel.Apply` maintains legacy projection fields; use the chart API rather than editing those projections directly. Existing Shape Fill commands recolor the first series.

'''),
(8193,8552,"Native `.pspace` preserves this application's model, including mixed text, notes, local comments and animation settings. New chart data is saved with schema version 2 so older builds reject it rather than silently losing series. Version 0.3 still reads version-1 documents. PNG/PDF are delivery formats. PPTX supports real text runs, preset shapes, embedded pictures, uniform native tables, native supported multi-series charts, notes and basic transitions. It does not preserve arbitrary unsupported OOXML parts.\n"),
(10034,10193,'Browser/OS interception can vary; ribbon alternatives are available. Search includes `Apply Blank layout`, `Apply Title only layout`, text-format commands, `Edit chart data`, `Chart type Line`, `Chart type Doughnut` and `Chart grouping Stacked`. In the chart data input, `Ctrl+Enter` applies the draft. Apply the draft before closing the pane or changing selection.\n'),
(10531,10754,'- **Browser and GitHub Pages:** production WebAssembly publish, real Chromium keyboard interactions, selected-word formatting, undo/redo, content-preserving layouts, multi-series chart data/type/grouping workflows, screenshots, six-library packaging and Pages deployment.\n'),
(11224,11710,'Complete master/layout/theme inheritance and authoring; full WYSIWYG rich text and complex-script shaping; merged/styled tables and advanced chart families/axis formatting; SmartArt, media, freeform inking and advanced drawing effects; attached connectors; Office timing trees, motion paths and second-display presenter view; `.ppt`/`.pptm`; complete accessibility and touch qualification; secure cloud coauthoring/history/administration; and pixel-level PowerPoint UI parity remain unfinished.\n')])
patch('docs/architecture.md','87665c4149bfad631e3df5f7e8b89977eae38ca5',[
(3710,4058,'`SlideRenderer` handles basic shapes, pictures, text, uniform tables and six categorical chart types. `DrawRichText` wraps styled word segments and uses their font metrics, color, emphasis, underline and paragraph properties. Oversized words are broken at text elements. Rendering does not claim complete complex-script shaping or Office typography equivalence.\n'),
(4603,4603,'''\n## Chart model and authoring

`ChartSpec` contains a categorical chart type, grouping, title, legend/data-label flags, blank-data policy, hole size, categories and immutable series. Series use nullable double values; null is distinct from zero. `ChartModel.Get` adapts version-1 fields without mutation, and `Apply` updates an authoritative chart record plus legacy projections. New chart data serializes as schema 2 to prevent old builds from quietly reading only the first-series projection.

`ChartModel.Intervals` centralizes ordinary, positive/negative and percentage stacking. `ChartAxisScale` normalizes value magnitudes before choosing ticks, so subnormal inputs do not underflow into NaN axis geometry. `SlideRenderer.RenderChart` can render independently of a document. Editing, thumbnails, slide show and PNG/PDF consume that renderer. Native PPTX export consumes the same chart data and grouping rules rather than flattened primitives.

`ChartDataEditor` is a session-independent Uno control with `SetValue` and `ValueChanged`. Its bounded quoted TSV parser validates the entire draft before emitting an immutable result. The host applies the result as one history transaction. Input fields require Apply/Ctrl+Enter before pane dismissal. Type conversions validate constraints before updating, and locked objects cannot be edited through the chart pane. Existing Shape Fill changes map to the first series; theme accent recoloring retains custom series colors.
'''),
(5145,5586,'Uniform tables emit actual `a:tbl` graphic frames. Supported charts emit chart parts with relationships, typed category/value caches and embedded XLSX packages. Each workbook uses inline strings for labels and numeric value cells; missing points have no numeric cell, and labels cannot become spreadsheet formulas. Import reads supported cached chart data only, validates counts/indexes before allocating, and diagnoses unsupported chart types. External workbooks are never retrieved.\n')])
patch('docs/compatibility.md','b3b420cba29f9631eb37af68178a18c1e36a48d5',[
(33,196,'PresentationSpace 0.3 is a development preview, not a complete or pixel-exact PowerPoint clone, a byte-preserving OOXML editor, or a certified Office replacement.\n'),
(1185,1340,'| Charts | Immutable categories/series, nullable values, type and basic options | Column, bar, line, area, pie and doughnut caches; supported grouping/options | Native chart parts with every series and editable embedded XLSX workbooks |\n'),
(4606,5230,'''Native chart export creates chart relationships, typed caches and an embedded workbook for column, horizontal bar, line, area, pie and doughnut charts. Column/bar grouping supports clustered, stacked and 100% stacked. Other chart types use standard grouping. Positive and negative ordinary stacks accumulate separately. Pie/doughnut charts require one series; those types and 100% stacked charts require non-negative data. Unsupported conversions fail visibly without discarding series.

The model supports up to 32 series, 10,000 categories and 100,000 numeric positions per chart. Numeric values are finite doubles bounded to ±1e30. Missing values remain null, absent cache points and absent workbook cells rather than zero. Gap/Zero/Span are preserved; spanning affects line/area connections. New charts use a readable slide-relative size, a title/legend and white background. Chart-area color or transparency round-trips; legacy version-1 charts retain their transparent background. All-zero or all-missing circular charts display an empty-data state. Normalized axis calculations handle subnormal finite values without producing invalid geometry.

Chart labels/names use string cells, including values beginning with `=`; no spreadsheet formulas execute. Workbook addresses work beyond column Z. Import reads cached values only; it does not execute embedded workbooks or fetch external data. Multiple supported series are preserved. Duplicate indexes/order and invalid counts are rejected before numeric-array allocation; combination charts, conflicting series categories and unsupported families are diagnosed rather than partially imported.

This is not complete Excel/PowerPoint chart fidelity. Combination, scatter, bubble, radar, stock, surface and 3D charts; stacked line/area; multi-series circular charts; negative 100% stacks; multi-level categories; advanced axes, custom point/marker/data-label formatting, trendlines, error bars, live data links and Office-identical layout are unsupported. Circular slices use a fixed application palette, not imported per-point styles. Legends display the first 16 entries with an overflow count; labels are thinned on dense category axes and data labels are omitted above 50 numeric positions. Native Office may lay out the same chart differently.

The Uno data editor is an explicit-apply quoted TSV control, not a spreadsheet grid. Paste tab-separated cells with a header, category labels in the first column and one column per series. Empty numeric cells stay missing. Apply (or Ctrl+Enter in the data field) commits the title, hole size and data as one undoable edit. Apply before closing the pane or changing selection; unapplied text is not part of the document/recovery snapshot. Type, grouping, legend, label and color commands apply the current valid draft. Formula text in numeric cells is rejected.

New chart records serialize as native schema version 2. Earlier native documents remain readable; older application builds intentionally reject version 2. Legacy `Values`/`Labels` are compatibility projections, not the authoritative chart model. Keep the original PPTX because unsupported parts are not preserved opaquely.
'''),
(6179,6605,'Native/PPTX input: 64 MB. PPTX expansion: 128 MB total, 32 MB per part and 10,000 parts. Native documents: 2,000 slides and 20,000 shapes. Charts: 32 series, 10,000 categories, 100,000 numeric positions, validated before numeric-array allocation; TSV input at most 2,097,152 characters with 32,767-character cells. Raster decode: 16 megapixels; image insertion: 20 MB. PNG export: at most 32 megapixels. DTDs and external retrieval are prohibited. Invalid/duplicate identifiers, non-finite geometry and malformed rich-text ranges are rejected.\n')])

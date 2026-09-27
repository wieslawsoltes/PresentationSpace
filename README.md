<div align="center">

# PresentationSpace

**A familiar presentation workspace. An independent, reusable engine.**

Office-style slide editing in C# with **Uno Platform** and **SkiaSharp**. One document engine and application for the browser and native desktop.

[![Build and test](https://github.com/wieslawsoltes/PresentationSpace/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/PresentationSpace/actions/workflows/build.yml)
[![Browser and Pages](https://github.com/wieslawsoltes/PresentationSpace/actions/workflows/pages.yml/badge.svg)](https://github.com/wieslawsoltes/PresentationSpace/actions/workflows/pages.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

[**Open browser editor**](https://wieslawsoltes.github.io/PresentationSpace/) · [Architecture](docs/architecture.md) · [Compatibility](docs/compatibility.md) · [Changelog](CHANGELOG.md) · [Releases](https://github.com/wieslawsoltes/PresentationSpace/releases)

</div>

> **0.4 development preview.** PresentationSpace is inspired by the PowerPoint desktop workflow. It is not Microsoft software, a pixel-exact reproduction, a complete PowerPoint implementation, or a lossless editor for arbitrary PPTX files. Keep original imported presentations. Supported functionality and remaining gaps are documented below.

## What is new in 0.4

**Merged cells with preserved content.** Merge a rectangular selection without dropping text or mixed character styles. Split it back into its underlying grid, insert/delete rows and columns through merges, set relative track sizes, and distribute rows or columns equally. A surviving merge retains its content even when its origin row or column is deleted.

**A reusable table authoring control.** The Uno `TableDataEditor` provides cell/range/row/column selection, a bounded grid navigator, explicit cell-text commits, per-cell fills and text formatting, cell margins, border widths/styles/colors, and header/banding/total-row options. Valid changes are emitted as immutable snapshots and integrated as undoable edits. Text input is a plain TextBox, not inline slide-cell WYSIWYG editing.

**Native interchange and shared rendering.** PPTX tables retain row/column proportions, rectangular merges, supported rich text, margins and explicit cell formatting. Import checks merge origins and continuations against a bounded grid. Skia uses the same model for editing, slide show, PNG/PDF and standalone `RenderTable`. Native schema 3 prevents older builds from silently flattening merged cells; schemas 1 and 2 remain readable.

Global find/replace includes table origins and preserves cell styles. Shape formatting changes only the requested properties, and theme accents retain explicit custom cell colors. The 0.3 six-type chart pipeline and earlier layout/rich-text features remain available. See [Compatibility](docs/compatibility.md) for the supported subset and [Changelog](CHANGELOG.md) for earlier work.

## The workspace

The custom Uno workspace combines a compact title bar, quick-access commands, tabbed ribbon, resizable filmstrip, Skia canvas, notes, formatting/selection/comment panes, slide sorter and slide show. The browser uses the same .NET application compiled to WebAssembly, not an HTML mock-up or a separate JavaScript presentation engine.

| Area | Implemented functionality |
|---|---|
| Slides | New, duplicate, delete, reorder, hide, non-destructive predefined layouts, backgrounds, slide sizing and numbers |
| Objects | Text, basic geometric shapes, lines/arrows, raster pictures, merged/styled tables and six categorical chart types |
| Manipulation | Multi-selection, marquee, dragging, eight resize handles, rotation, aspect constraints, snapping, nudging, pan and zoom |
| Arrangement | Selection groups, front/back ordering, alignment/distribution, object locking and visibility |
| Text | Mixed character styles, bullets, paragraph alignment/spacing, wrapping, vertical alignment and text editing |
| Presentation | Basic fade/push/wipe transitions; appear/fade/fly-in effects; hidden-slide skipping; black/white screen |
| Workflow | Transactional undo/redo, internal clipboard, command search, local comments, notes, find/replace and device-local recovery |
| Files | Native `.pspace`, documented PPTX subset, slide PNG and vector PDF export |

The ribbon includes Home, Insert, Draw, Design, Transitions, Animations, Slide Show, Review, View, Shape Format and Help, plus the File workspace. Commands use the shared document session. Unsupported cloud features are not simulated.

## Start developing

Install the **.NET 10 SDK** and `wasm-tools` for browser builds. `global.json` pins **Uno SDK 6.7.30**, whose matched bundle uses **Uno 6.7.135** and **SkiaSharp 3.119.2**. Skia package versions are intentionally aligned.

```sh
git clone https://github.com/wieslawsoltes/PresentationSpace.git
cd PresentationSpace
dotnet workload install wasm-tools

# Headless engine, rendering, PPTX and embedded-workbook tests
dotnet test tests/PresentationSpace.Tests/PresentationSpace.Tests.csproj -c Release

# Browser development
dotnet run --project src/PresentationSpace.App/PresentationSpace.App.csproj \
  -f net10.0-browserwasm -p:PresentationTargetFrameworks=net10.0-browserwasm

# Native desktop
dotnet run --project src/PresentationSpace.App/PresentationSpace.App.csproj \
  -f net10.0-desktop -p:PresentationTargetFrameworks=net10.0-desktop
```

`PresentationTargetFrameworks` restricts Uno projects only. Do not override `TargetFrameworks` globally: that incorrectly retargets UI-independent libraries.

### Browser publishing

```sh
dotnet publish src/PresentationSpace.App/PresentationSpace.App.csproj \
  -f net10.0-browserwasm -c Release \
  -p:PresentationTargetFrameworks=net10.0-browserwasm \
  -p:WasmShellWebAppBasePath=/PresentationSpace/ -o artifacts/browser
python3 tools/prepare-pages.py artifacts/browser artifacts/site
```

Serve the distribution over HTTP(S) at the configured base path. `file://` loading is unsupported. CI tests the published application in Chromium before deploying to GitHub Pages.

## Six independently reusable libraries

| Package | Responsibility | Dependencies |
|---|---|---|
| `PresentationSpace.Core` | Immutable documents, rich-text operations, layouts, tables, chart data/axes, geometry, commands, selection and undo | No UI framework |
| `PresentationSpace.Formats` | Bounded PPTX ZIP/XML, native chart/table parts and embedded chart workbooks | Core |
| `PresentationSpace.Rendering.Skia` | Slides, merged/styled tables, six chart types, mixed text, images, thumbnails, selection, PNG/PDF | Core + SkiaSharp |
| `PresentationSpace.Ribbon.Uno` | Ribbon tabs, groups, buttons and Office-style palette | Uno |
| `PresentationSpace.Controls.Uno` | Viewport, filmstrip, sorter, inspector, table/chart data editors, notes, splitters, status and slide show | Uno + renderer |
| `PresentationSpace.Editor.Uno` | Embeddable complete editor and injectable storage contract | Reusable libraries above |

`PresentationSpace.App` is the executable host. No library references the app. CI produces six NuGet packages; automatic publication to nuget.org is not configured.

### Embed the editor

```csharp
using PresentationSpace.Core;
using PresentationSpace.Editor.Uno;

var session = new EditorSession(SlideFactory.Welcome());
var editor = new PresentationEditor(session, myStorageService);
myPage.Content = editor;
```

Implement `IWorkspaceStorage` for your host's pickers, saves and recovery. Return `false` on canceled saves. The app supplies a device-local Uno implementation.

### Use the engine without Uno

```csharp
using PresentationSpace.Core;
using PresentationSpace.Formats;
using PresentationSpace.Rendering.Skia;

var session = new EditorSession();
var title = SlideFactory.Text("Hello, slides", 80, 100, 900, 130, 56);
title = RichText.Format(title, 0, 5,
    style => style with { Bold = true, Color = "#D35230" });
session.Insert(title);
session.ApplyLayout("Title only"); // Unmatched content is retained.

using var renderer = new SlideRenderer();
byte[] png = renderer.ExportPng(session.Document, session.CurrentSlide);
ExportResult pptx = PptxCodec.Export(session.Document);
// Display pptx.Warnings; valid OOXML is not a guarantee of visual equivalence.
```

Headless hosts must provide the operating system's Skia native-assets package. Sessions and renderers are single-thread-affine; use one renderer per concurrent worker and dispose it.

### Use charts independently

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

### Use tables independently

```csharp
var table = TableModel.Create(rows: 4, columns: 3);
table = TableModel.SetText(table, 0, 0, "Quarterly results");
table = TableModel.Merge(table, new TableRange(0, 0, 1, 3));
table = TableModel.EditCells(table, new TableRange(1, 0, 3, 3),
    cell => cell with { Fill = "#EAF1FA", MarginLeft = 12 });
var shape = TableModel.Apply(new SlideShape
{
    Name = "Results table", Bounds = new(100, 180, 960, 400)
}, table);
session.Insert(shape);
renderer.RenderTable(canvas, table, new RectF(0, 0, 640, 300));
```

`TableDataEditor.SetValue(table)` loads the standalone Uno control; `ValueChanged` emits validated snapshots. `TableLayout` supplies normalized edges, merge-origin bounds and grid lookup. `TableModel.Apply` maintains compatibility projections; edit the table model rather than `Cells`/`TableColumns` directly. Limits are 100 rows, 100 columns and one million text characters per table.

Select a table and open **Format Shape → Table design & layout**, or search **Edit table data**. Click a cell, Shift+click to extend the selection, or click row/column headers. Apply text with **Apply cell text** or **Ctrl+Enter** before selecting another cell. Structural/style actions apply the current valid text draft. The navigator pages through eight rows and four columns; the slide shows the entire table.

## Files, privacy and recovery

Native `.pspace` preserves this application's model, including mixed text, notes, local comments and animation settings. Structured tables use schema version 3 and charts require at least version 2, so older builds reject unsupported structure rather than silently losing it. Version 0.4 reads schemas 1, 2 and 3. PNG/PDF are delivery formats. PPTX supports real text runs, preset shapes, embedded pictures, merged/styled native tables, native supported multi-series charts, notes and basic transitions. It does not preserve arbitrary unsupported OOXML parts.

Editing needs no account or server. **AutoSave means device-local recovery, not OneDrive, cloud backup or coauthoring.** Download a native file for durable storage. Browser storage can be cleared or evicted. Share exports files.

Input checks bound ZIP/XML sizes, reject invalid geometry and ranges, prohibit DTDs and avoid external-relationship retrieval. Chart cache sizes and point indexes are validated before allocation. Formula-looking chart labels remain text in the embedded workbook. See [Security](SECURITY.md).

## Keyboard and pointer workflow

| Action | Shortcut |
|---|---|
| New slide / save / open | `Ctrl+M` / `Ctrl+S` / `Ctrl+O` |
| Undo / redo outside text inputs | `Ctrl+Z` / `Ctrl+Y` or `Ctrl+Shift+Z` |
| Bold / italic / underline | `Ctrl+B` / `Ctrl+I` / `Ctrl+U`; character selection or selected objects |
| Copy / cut / paste / duplicate objects | `Ctrl+C` / `Ctrl+X` / `Ctrl+V` / `Ctrl+D` |
| Select all / group / ungroup | `Ctrl+A` / `Ctrl+G` / `Ctrl+Shift+G` |
| Edit text | Double-click, `F2`, or `Enter` |
| Move selection | Arrows; `Shift` for ten-unit steps |
| Constrain resize / rotate | Hold `Shift` |
| Temporarily disable snapping | Hold `Alt` while dragging |
| Pan / zoom | Middle-button or Space-drag / `Ctrl+wheel`, zoom slider, Fit |
| Command search | `Alt+Q` |
| Present beginning / current slide | `F5` / `Shift+F5` |
| Navigate / end show | Arrows, Page Up/Down, Space / `Esc` |
| Black / white screen | `B` / `W` during slide show |

Browser/OS interception can vary; ribbon alternatives are available. Search includes `Apply Blank layout`, `Apply Title only layout`, text-format commands, `Edit chart data`, `Chart type Line`, `Chart type Doughnut` and `Chart grouping Stacked`. In the chart data input, `Ctrl+Enter` applies the draft. Apply the draft before closing the pane or changing selection.

## Automation and validation

The normal workflows are deliberately limited to three responsibilities:

- **Build and test:** Linux, Windows and macOS matrix; headless editing/geometry/rendering tests, native serialization, PPTX round trips, independent Open XML validation of presentations and chart workbooks, and desktop compilation.
- **Browser and GitHub Pages:** production WebAssembly publish, real Chromium keyboard interactions, selected-word formatting, undo/redo, content-preserving layouts, multi-series chart data/type/grouping and table editing/merge/track workflows, screenshots, six-library packaging and Pages deployment.
- **Release:** tag-triggered tests, self-contained desktop distributions, browser output, NuGet artifacts and a GitHub Release. Tagged release execution and signing are separate from ordinary build checks.

Read each run's results rather than treating configured coverage as completed qualification. Tests establish behavior for their covered cases, not every PowerPoint file, accessibility standard, GPU, browser or production-scale workload.

## Remaining major work

Complete master/layout/theme inheritance and authoring; full WYSIWYG rich text and complex-script shaping; full Office table-style/theme fidelity and advanced chart families/axis formatting; SmartArt, media, freeform inking and advanced drawing effects; attached connectors; Office timing trees, motion paths and second-display presenter view; `.ppt`/`.pptm`; complete accessibility and touch qualification; secure cloud coauthoring/history/administration; and pixel-level PowerPoint UI parity remain unfinished.

See [Compatibility](docs/compatibility.md) for precise boundaries, including typed text-overlay behavior and non-lossless PPTX import. Contributions should add tests alongside functionality and keep modules independently consumable. See [Contributing](CONTRIBUTING.md).

## License and attribution

[MIT](LICENSE). Uno Platform and SkiaSharp are open-source dependencies. This project does not bundle Microsoft PowerPoint code, logos or proprietary fonts. PowerPoint and Microsoft are trademarks of Microsoft Corporation. PresentationSpace is independent and is not endorsed by or affiliated with Microsoft.

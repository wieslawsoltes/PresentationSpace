<div align="center">

# PresentationSpace

**A familiar presentation workspace. An independent, reusable engine.**

Office-style slide editing in C# with **Uno Platform** and **SkiaSharp**. One document engine and application for the browser and native desktop.

[![Build and test](https://github.com/wieslawsoltes/PresentationSpace/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/PresentationSpace/actions/workflows/build.yml)
[![Browser and Pages](https://github.com/wieslawsoltes/PresentationSpace/actions/workflows/pages.yml/badge.svg)](https://github.com/wieslawsoltes/PresentationSpace/actions/workflows/pages.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![NuGet](https://img.shields.io/nuget/vpre/PresentationSpace.Core.svg?label=NuGet)](https://www.nuget.org/packages/PresentationSpace.Core)
[![Downloads](https://img.shields.io/nuget/dt/PresentationSpace.Core.svg)](https://www.nuget.org/packages/PresentationSpace.Core)

[**Open browser editor**](https://wieslawsoltes.github.io/PresentationSpace/) · [Architecture](docs/architecture.md) · [Compatibility](docs/compatibility.md) · [Changelog](CHANGELOG.md) · [Releases](https://github.com/wieslawsoltes/PresentationSpace/releases)

</div>

> **0.7 development preview.** PresentationSpace is inspired by the PowerPoint desktop workflow. It is not Microsoft software, a pixel-exact reproduction, a complete PowerPoint implementation, or a lossless editor for arbitrary PPTX files. Keep original imported presentations. Supported functionality and remaining gaps are documented below.

## What is new in 0.7

**Shared shaped text layout.** Plain and mixed-style text now use the same HarfBuzz glyph shaping, wrapping, line metrics and baseline placement in the slide editor, thumbnails, slide show, tables, chart labels, PNG and vector PDF. Color and underline changes do not break font shaping. Repeated spaces and four-space tab stops are measured explicitly; paragraph breaks and soft line breaks remain distinct. Emergency wrapping respects extended grapheme boundaries and explicit non-breaking groups. Bulleted continuations retain their hanging indent without repeating the marker.

**Measured text fitting and slide sizing.** Select text-bearing shapes and use **Shrink text to fit** or **Resize shape to text** in Shape Format or command search. These explicit undoable operations preserve content and mixed font-size ratios. Resizing a rotated text box keeps its top edge in place. Widescreen/Standard/Portrait sizing now scales mixed text and table styling instead of flattening runs or resetting layouts. **Open typography sample** appends two editable slides to the current deck without replacing existing work.

**Reusable, bounded text caching.** `TextLayoutEngine` returns immutable line metrics and retains bounded native glyph blobs shared by measurement and drawing. Font resolver identity/version changes invalidate both text layouts and retained slide pictures. See [Typography architecture](docs/architecture.md#shared-text-layout-07) and [Performance](docs/performance.md) for cache ownership, first-use costs and reproducible measurements.

This is horizontal single-direction shaping, not complete Unicode bidirectional/line-breaking or PowerPoint typography conformance. Hosts must supply fonts covering their text. Mixed-direction paragraphs are diagnosed in layout metrics; automatic font fallback and fully styled native input remain unfinished. Text fitting is explicit, not a persistent Office auto-fit mode.

## What is new in 0.6

**Aligned, responsive editor chrome.** A reusable compact AutoSave toggle centers its complete pill and thumb inside a keyboard-operable hit target. Ribbon buttons and font controls use explicit compact minimum sizes rather than clipping larger theme templates. The title and document name share constrained columns; quick actions adapt to the available width. Hidden command search stays available through **Alt+Q** and the **More presentation actions** menu. Ribbon tabs and commands expose scroll arrows only when they overflow.

**Table Design.** Four palette presets, first/last-column emphasis, column banding and ten border scopes are available in the new ribbon tab, the table inspector and command search. Border operations update both sides of shared edges and retain untouched text, styles, geometry and merges. Unsupported partial changes along a neighboring merged-cell edge are rejected as one transaction. Column flags are written as native DrawingML attributes; native documents with those flags use schema 4, while earlier schemas remain readable.

**Indexed table editing and layout.** An immutable, weak-keyed `TableGridIndex` shares validated ownership and reading order across table layouts and cell navigation. Warm lookup is constant-time and layouts rebuild only their coordinate edges, not the table's ownership map. Diagnostics coalesce UI updates and reuse table statistics until the table changes. See [Performance](docs/performance.md) for measured workloads and the cost of first use.

Earlier direct Skia composition, retained drawing, virtualized slide views, rich text, native chart/workbook and merged-table features remain available. Use **Open large-deck sample** for a 1,000-slide workspace and **Rendering statistics** for draw/cache counters. These counters are not GPU duration or FPS.

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

The ribbon includes Home, Insert, Draw, Design, Transitions, Animations, Slide Show, Review, View, Shape Format, Table Design and Help, plus the File workspace. Commands use the shared document session. Unsupported cloud features are not simulated.

Select a table and open **Format Shape → Table design & layout**, or search **Edit table data**. Click a cell, Shift+click to extend the selection, or click row/column headers. Apply text with **Apply cell text** or **Ctrl+Enter** before selecting another cell. Structural/style actions apply the current valid text draft. The navigator pages through eight rows and four columns; the slide shows the entire table.

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

## Download

Every [release](https://github.com/wieslawsoltes/PresentationSpace/releases/latest) ships a self-contained, single-file desktop app — no .NET install needed:

| OS | x64 | Arm64 |
| --- | --- | --- |
| Windows | `PresentationSpace-<version>-win-x64.zip` | `PresentationSpace-<version>-win-arm64.zip` |
| macOS | `PresentationSpace-<version>-osx-x64.tar.gz` | `PresentationSpace-<version>-osx-arm64.tar.gz` |
| Linux | `PresentationSpace-<version>-linux-x64.tar.gz` | `PresentationSpace-<version>-linux-arm64.tar.gz` |

Extract and run `PresentationSpace` (`PresentationSpace.exe` on Windows). Builds are not code-signed yet: on macOS clear the quarantine flag with `xattr -d com.apple.quarantine PresentationSpace`; on Windows choose **More info → Run anyway** in SmartScreen. Verify downloads against `SHA256SUMS`.

## NuGet packages

All six libraries are MIT-licensed and published on [NuGet.org](https://www.nuget.org/packages?q=PresentationSpace). `PresentationSpace.Core`, `PresentationSpace.Formats` and `PresentationSpace.Rendering.Skia` target `net10.0` and have no UI-framework dependency (the renderer uses SkiaSharp and SkiaSharp.HarfBuzz). The `*.Uno` packages target `net10.0-desktop` and `net10.0-browserwasm` on Uno Platform 6.7 with the Skia renderer. Every package is versioned together with the app, and symbols ship on NuGet.org as `.snupkg` with SourceLink.

```sh
dotnet add package PresentationSpace.Core
```

| Package | Version | Downloads | Description |
|---|---|---|---|
| [PresentationSpace.Core](https://www.nuget.org/packages/PresentationSpace.Core) | [![NuGet](https://img.shields.io/nuget/vpre/PresentationSpace.Core.svg)](https://www.nuget.org/packages/PresentationSpace.Core) | [![Downloads](https://img.shields.io/nuget/dt/PresentationSpace.Core.svg)](https://www.nuget.org/packages/PresentationSpace.Core) | Immutable presentation model, rich text, tables, charts, geometry, editing commands and transactional undo, with no UI dependency |
| [PresentationSpace.Formats](https://www.nuget.org/packages/PresentationSpace.Formats) | [![NuGet](https://img.shields.io/nuget/vpre/PresentationSpace.Formats.svg)](https://www.nuget.org/packages/PresentationSpace.Formats) | [![Downloads](https://img.shields.io/nuget/dt/PresentationSpace.Formats.svg)](https://www.nuget.org/packages/PresentationSpace.Formats) | Bounded, dependency-free PPTX import and editable PowerPoint export, including native tables, charts and embedded chart workbooks |
| [PresentationSpace.Rendering.Skia](https://www.nuget.org/packages/PresentationSpace.Rendering.Skia) | [![NuGet](https://img.shields.io/nuget/vpre/PresentationSpace.Rendering.Skia.svg)](https://www.nuget.org/packages/PresentationSpace.Rendering.Skia) | [![Downloads](https://img.shields.io/nuget/dt/PresentationSpace.Rendering.Skia.svg)](https://www.nuget.org/packages/PresentationSpace.Rendering.Skia) | SkiaSharp slide, table and chart rendering, thumbnails, PNG and vector PDF export |
| [PresentationSpace.Ribbon.Uno](https://www.nuget.org/packages/PresentationSpace.Ribbon.Uno) | [![NuGet](https://img.shields.io/nuget/vpre/PresentationSpace.Ribbon.Uno.svg)](https://www.nuget.org/packages/PresentationSpace.Ribbon.Uno) | [![Downloads](https://img.shields.io/nuget/dt/PresentationSpace.Ribbon.Uno.svg)](https://www.nuget.org/packages/PresentationSpace.Ribbon.Uno) | Office-style ribbon tabs, groups, command buttons, overflow scrolling and a compact toggle switch for Uno Platform |
| [PresentationSpace.Controls.Uno](https://www.nuget.org/packages/PresentationSpace.Controls.Uno) | [![NuGet](https://img.shields.io/nuget/vpre/PresentationSpace.Controls.Uno.svg)](https://www.nuget.org/packages/PresentationSpace.Controls.Uno) | [![Downloads](https://img.shields.io/nuget/dt/PresentationSpace.Controls.Uno.svg)](https://www.nuget.org/packages/PresentationSpace.Controls.Uno) | Slide viewport, filmstrip, sorter, inspector, table/chart data editors, notes, status bar and slide-show player for Uno Platform |
| [PresentationSpace.Editor.Uno](https://www.nuget.org/packages/PresentationSpace.Editor.Uno) | [![NuGet](https://img.shields.io/nuget/vpre/PresentationSpace.Editor.Uno.svg)](https://www.nuget.org/packages/PresentationSpace.Editor.Uno) | [![Downloads](https://img.shields.io/nuget/dt/PresentationSpace.Editor.Uno.svg)](https://www.nuget.org/packages/PresentationSpace.Editor.Uno) | Embeddable, complete Office-style presentation editor with pluggable storage for Uno Platform |

Dependencies follow the layers: `Core ← Formats`, `Core ← Rendering.Skia`, `Core + Rendering.Skia + Ribbon.Uno ← Controls.Uno ← Editor.Uno (+ Formats)`. `Ribbon.Uno` depends only on Uno. `PresentationSpace.App` is the executable host; no library references the app.

### PresentationSpace.Core

The document engine: immutable `PresentationDocument`/`Slide`/`SlideShape` records, mixed-style rich text, merged and styled tables, multi-series chart data, layouts, geometry, selection and a transactional `EditorSession` with undo/redo. Use it standalone to generate or transform decks on a server or in tests. No dependencies and no UI requirement.

```sh
dotnet add package PresentationSpace.Core
```

**Key types**
- `EditorSession`: current document, slide and selection; `Insert`, `Apply`, `EditSlide`, `AddSlide`, `Undo`/`Redo`, `Changed` event.
- `SlideFactory`: `Text(...)` shapes, `Create(layout)` slides and the `Welcome()` sample deck.
- `RichText`: character-range formatting (`Format`, `Replace`, `Segments`).
- `TableModel` / `TableSpec`: create, merge/split, insert/delete tracks, style presets and borders (limits: 100 rows, 100 columns, one million characters).
- `ChartModel` / `ChartSpec` / `ChartTabularData`: six chart kinds, validation and spreadsheet-style copy/paste text.
- `DocumentSerializer`: native `.pspace` JSON with schema validation.

**Usage**

```csharp
using PresentationSpace.Core;

var session = new EditorSession(SlideFactory.Welcome());
session.AddSlide("Title only");

var title = SlideFactory.Text("Hello, slides", 80, 100, 900, 130, 56);
title = RichText.Format(title, 0, 5, style => style with { Bold = true, Color = "#D35230" });
session.Insert(title);                 // one undoable transaction
session.ApplyLayout("Title slide");    // unmatched content is retained

var table = TableModel.Create(rows: 4, columns: 3);
table = TableModel.SetText(table, 0, 0, "Quarterly results");
table = TableModel.Merge(table, new TableRange(0, 0, 1, 3));
table = TableModel.ApplyStyle(table, TableStylePreset.Blue);
session.Insert(TableModel.Apply(new SlideShape { Name = "Results", Bounds = new(100, 260, 960, 320) }, table));

session.Undo();
string json = DocumentSerializer.Serialize(session.Document); // native .pspace
```

`TableModel.Apply` and `ChartModel.Apply` maintain legacy projection fields (`Cells`, `TableColumns`, `Values`, `Labels`); edit the table/chart model rather than those projections. `TableLayout` supplies normalized edges, merge-origin bounds and hit-testing. Sessions are single-thread-affine.

### PresentationSpace.Formats

Bounded PresentationML (PPTX) import and editable PowerPoint export: real text runs, preset shapes, pictures, merged/styled native tables, native multi-series charts with embedded workbooks, notes and basic transitions. ZIP/XML sizes are bounded, DTDs are prohibited and external relationships are never fetched. Depends on Core only; no UI requirement.

```sh
dotnet add package PresentationSpace.Formats
```

**Key types**
- `PptxCodec.Import(byte[])`: returns an `ImportResult` with the `Document` and `Warnings`.
- `PptxCodec.Export(PresentationDocument)`: returns an `ExportResult` with the `.pptx` `Data` and `Warnings`.
- `ImportResult` / `ExportResult`: report unsupported content instead of silently dropping it.

**Usage**

```csharp
using PresentationSpace.Core;
using PresentationSpace.Formats;

ImportResult imported = PptxCodec.Import(File.ReadAllBytes("deck.pptx"));
foreach (var warning in imported.Warnings)
    Console.WriteLine(warning);        // unsupported parts are reported, not silently kept

var session = new EditorSession(imported.Document);
session.ReplaceText("2025", "2026");

ExportResult exported = PptxCodec.Export(session.Document);
File.WriteAllBytes("deck-2026.pptx", exported.Data);
```

Import is not lossless for arbitrary PPTX files, and valid OOXML output does not guarantee visual equivalence. See [Compatibility](docs/compatibility.md).

### PresentationSpace.Rendering.Skia

Draws slides, mixed-style text, images, merged/styled tables and the six chart types to any `SKCanvas`, with picture/scene caching, thumbnails and PNG/vector PDF export. Use it headlessly for exports or inside any Skia-based UI. Depends on Core, SkiaSharp 3.119.2 and SkiaSharp.HarfBuzz 3.119.2; headless hosts must provide matching SkiaSharp and HarfBuzzSharp native assets for their operating system. Linux examples use `SkiaSharp.NativeAssets.Linux.NoDependencies` 3.119.2 and `HarfBuzzSharp.NativeAssets.Linux` 8.3.1.1.

```sh
dotnet add package PresentationSpace.Rendering.Skia
```

**Key types**
- `SlideRenderer`: `Render`, `ExportPng`, `ExportPdf`, `RenderTable`, `RenderChart`, `DrawRichText` and `AutoFitTableRows`.
- `SlideRenderer.CacheStatistics` / `RenderCacheStatistics`: picture and scene cache counters.
- `TypefaceRegistry` / `ITypefaceResolver`: register embedded font files and resolve typefaces per `TextStyle`.

**Usage**

```csharp
using PresentationSpace.Core;
using PresentationSpace.Rendering.Skia;
using SkiaSharp;

var document = SlideFactory.Welcome();
using var renderer = new SlideRenderer();
File.WriteAllBytes("slide-1.png", renderer.ExportPng(document, document.Slides[0], width: 1920));
File.WriteAllBytes("deck.pdf", renderer.ExportPdf(document));

// Draw a chart onto any SKCanvas, without a document or editor.
var chart = new ChartSpec
{
    Kind = ChartKind.Line,
    Title = "Quarterly performance",
    Categories = ["Q1", "Q2", "Q3"],
    Series = [new() { Name = "Actual", Color = "#D35230", Values = [10, null, 25] }]
};
using var surface = SKSurface.Create(new SKImageInfo(640, 360));
renderer.RenderChart(surface.Canvas, chart, new RectF(0, 0, 640, 360), new TextStyle());
```

Renderers are single-thread-affine: use one per concurrent worker and dispose it.

### PresentationSpace.Ribbon.Uno

A self-contained Office-style ribbon for any Uno Platform app: tabs, captioned groups, large/small command buttons, overflow scroll arrows, collapse on double-click and an accessible compact toggle switch. It has no dependency on the presentation engine. Requires Uno Platform.

```sh
dotnet add package PresentationSpace.Ribbon.Uno
```

**Key types**
- `RibbonControl`: `SetTabs`, `SelectTab`, `ToggleCollapsed`, `SelectedTab` and the `TabChanged` event.
- `RibbonTab(Title, Build)`: a tab whose groups are built on selection.
- `RibbonGroup(title, params UIElement[] items)` and `RibbonGroup.Column(...)` for stacked small buttons.
- `RibbonCommandButton(id, label, glyph, action, large, shortcut)`: sets automation name/ID and tooltip.
- `CompactToggleSwitch` and `OfficePalette` (shared brushes and `Brush(hex)`).

**Usage**

```csharp
using PresentationSpace.Ribbon.Uno;

var ribbon = new RibbonControl();
ribbon.SetTabs([
    new RibbonTab("Home", () => [
        new RibbonGroup("Clipboard",
            new RibbonCommandButton("paste", "Paste", "", Paste, shortcut: "Ctrl+V"),
            RibbonGroup.Column(
                new RibbonCommandButton("cut", "Cut", "", Cut, large: false),
                new RibbonCommandButton("copy", "Copy", "", Copy, large: false)))
    ]),
    new RibbonTab("View", () => [new RibbonGroup("Zoom", new RibbonCommandButton("fit", "Fit", "", Fit))])
]);
ribbon.TabChanged += (_, tab) => System.Diagnostics.Debug.WriteLine($"Selected {tab}");
myPage.Content = ribbon;
```

### PresentationSpace.Controls.Uno

The reusable editing surfaces behind the workspace, each bound to a shared `EditorSession`: a Skia slide viewport with selection, handles, snapping and in-place text editing; virtualized filmstrip and sorter; the Format/Selection/Comments inspector; standalone table and chart data editors; notes; status bar; and a slide-show player. Depends on Core, Rendering.Skia, Ribbon.Uno and Uno's Skia canvas; requires Uno Platform.

```sh
dotnet add package PresentationSpace.Controls.Uno
```

**Key types**
- `SlideViewport`: interactive canvas (`Session`, `SetZoom`, `Fit`, `ShowGrid`, `EditText`, `CommitText`).
- `SlideFilmstrip` / `SlideSorter`: recycled slide navigators (`Session`, `SlideInvoked`).
- `FormatPane` (`Mode` = `InspectorMode.Format`/`Selection`/`Comments`), `NotesPane`, `PresentationStatusBar`.
- `TableDataEditor` / `ChartDataEditor`: `SetValue(...)` plus a `ValueChanged` event that emits validated `TableSpec`/`ChartSpec` snapshots.
- `PresentationPlayer`: full-screen show with `Start(document, from)`, `Next`, `Previous`, `Close`.

**Usage**

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PresentationSpace.Controls.Uno;
using PresentationSpace.Core;

var session = new EditorSession(SlideFactory.Welcome());
var filmstrip = new SlideFilmstrip { Session = session };
var viewport = new SlideViewport { Session = session };

var root = new Grid();
root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
root.ColumnDefinitions.Add(new ColumnDefinition());
Grid.SetColumn(viewport, 1);
root.Children.Add(filmstrip);
root.Children.Add(viewport);
window.Content = root;

// Standalone chart data editor: apply each validated snapshot to the selected shape.
var chartEditor = new ChartDataEditor();
chartEditor.SetValue(new ChartSpec { Categories = ["Q1", "Q2"], Series = [new() { Values = [4, 7] }] });
chartEditor.ValueChanged += (_, chart) => session.Apply("Edit chart", shape => ChartModel.Apply(shape, chart));
```

### PresentationSpace.Editor.Uno

The complete workspace in one control: title bar, quick access, ribbon, filmstrip, canvas, notes, inspector panes, sorter, slide show, command search (**Alt+Q**), PPTX/PNG/PDF commands and device-local recovery. Embed it in any Uno app and supply storage through `IWorkspaceStorage`. Depends on Controls.Uno and Formats; requires Uno Platform.

```sh
dotnet add package PresentationSpace.Editor.Uno
```

**Key types**
- `PresentationEditor(EditorSession?, IWorkspaceStorage?)`: the embeddable `UserControl`; exposes `Session`, `Viewport`, `Ribbon` and `Storage`.
- `PresentationEditor.RestoreRecoveryAsync()`, `StartShow(fromBeginning)`, `ShowSorter()`, `ShowNormal()`, `ShowInspector(mode)`.
- `IWorkspaceStorage`: host pickers, saves and recovery (`OpenAsync`, `SaveAsync`, `ReadRecoveryAsync`, `WriteRecoveryAsync`).
- `WorkspaceFile(Name, Data)`: a file returned from `OpenAsync`.

**Usage**

```csharp
using PresentationSpace.Core;
using PresentationSpace.Editor.Uno;

var editor = new PresentationEditor(new EditorSession(SlideFactory.Welcome()), new HostStorage());
window.Content = editor;               // or a Page's Content
await editor.RestoreRecoveryAsync();

sealed class HostStorage : IWorkspaceStorage
{
    public Task<WorkspaceFile?> OpenAsync(IReadOnlyList<string> extensions) => Task.FromResult<WorkspaceFile?>(null); // show a picker
    public Task<bool> SaveAsync(string suggestedName, byte[] data, string mimeType) => Task.FromResult(false);      // false when canceled
    public Task<string?> ReadRecoveryAsync() => Task.FromResult<string?>(null);
    public Task WriteRecoveryAsync(string json) => Task.CompletedTask;
}
```

Return `false` from `SaveAsync` when the user cancels; recovery must not imply that a portable file was saved. The app supplies a device-local Uno implementation (`LocalWorkspaceStorage`).

## Files, privacy and recovery

Native `.pspace` preserves this application's model, including mixed text, notes, local comments and animation settings. Structured tables use schema version 3 and charts require at least version 2, so older builds reject unsupported structure rather than silently losing it. Version 0.5 reads schemas 1, 2 and 3. PNG/PDF are delivery formats. PPTX supports real text runs, preset shapes, embedded pictures, merged/styled native tables, native supported multi-series charts, notes and basic transitions. It does not preserve arbitrary unsupported OOXML parts.

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

- **Build and test:** Linux, Windows and macOS matrix; headless editing/geometry/rendering tests, native serialization, PPTX round trips, independent Open XML validation of presentations and chart workbooks, desktop compilation, and same-runner CPU/raster baseline comparisons.
- **Browser and GitHub Pages:** production WebAssembly publish, real Chromium keyboard interactions, selected-word formatting, undo/redo, content-preserving layouts, multi-series chart data/type/grouping table editing/merge/track workflows, direct on-slide cell input, row auto-fit, bounded 1,000-slide navigation and responsive title-bar/table-design regressions, screenshots, six-library packaging and Pages deployment.
- **Release:** runs for `v*` tags or a supplied manual version. It runs the tests, publishes self-contained single-file desktop executables for Windows, macOS and Linux (x64 and arm64), builds the browser output, packs the six libraries with symbols and emits `SHA256SUMS`. Tags attach all assets to a GitHub Release and publish the packages to NuGet.org with [Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing) (OIDC, no stored API key) from the protected `nuget` environment. Manual runs are dry runs: they build and upload every asset as workflow artifacts but publish nothing. Signing is not configured.

Read each run's results rather than treating configured coverage as completed qualification. Tests establish behavior for their covered cases, not every PowerPoint file, accessibility standard, GPU, browser or production-scale workload.

## Remaining major work

Complete master/layout/theme inheritance and authoring; full WYSIWYG rich text and complex-script shaping; full Office table-style/theme fidelity and advanced chart families/axis formatting; SmartArt, media, freeform inking and advanced drawing effects; attached connectors; Office timing trees, motion paths and second-display presenter view; `.ppt`/`.pptm`; complete accessibility and touch qualification; secure cloud coauthoring/history/administration; and pixel-level PowerPoint UI parity remain unfinished.

See [Compatibility](docs/compatibility.md) for precise boundaries, including typed text-overlay behavior and non-lossless PPTX import. Contributions should add tests alongside functionality and keep modules independently consumable. See [Contributing](CONTRIBUTING.md).

## License and attribution

[MIT](LICENSE). Uno Platform and SkiaSharp are open-source dependencies. This project does not bundle Microsoft PowerPoint code, logos or proprietary fonts. PowerPoint and Microsoft are trademarks of Microsoft Corporation. PresentationSpace is independent and is not endorsed by or affiliated with Microsoft.

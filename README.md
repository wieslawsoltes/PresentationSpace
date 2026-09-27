<div align="center">

# PresentationSpace

**A familiar presentation workspace. An independent, reusable engine.**

Office-style slide editing in C# with **Uno Platform** and **SkiaSharp**. One document engine and application for the browser and native desktop.

[![Build and test](https://github.com/wieslawsoltes/PresentationSpace/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/PresentationSpace/actions/workflows/build.yml)
[![Browser and Pages](https://github.com/wieslawsoltes/PresentationSpace/actions/workflows/pages.yml/badge.svg)](https://github.com/wieslawsoltes/PresentationSpace/actions/workflows/pages.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

[**Open browser editor**](https://wieslawsoltes.github.io/PresentationSpace/) · [Architecture](docs/architecture.md) · [Compatibility](docs/compatibility.md) · [Changelog](CHANGELOG.md) · [Releases](https://github.com/wieslawsoltes/PresentationSpace/releases)

</div>

> **0.2 development preview.** PresentationSpace is inspired by the PowerPoint desktop workflow. It is not Microsoft software, a pixel-exact reproduction, a complete PowerPoint implementation, or a lossless editor for arbitrary PPTX files. Keep original imported presentations. Supported functionality and remaining gaps are documented below.

## What is new in 0.2

**Content-preserving layouts.** Switch between five layouts without deleting unmatched text or custom artwork. Placeholder identity, formatting, notes and comments survive layout changes. PPTX export writes the five actual layout parts and placeholder roles; import can resolve missing placeholder geometry through layout/master parts.

**Mixed text formatting.** Select characters and apply font, size, color, bold, italic or underline. The immutable model, Skia renderer, native files and PPTX text runs preserve mixed styles. Paragraph commands expand to affected paragraphs; incremental typing drafts and Replace all retain unaffected styles. The input overlay remains a plain TextBox while typing, rather than a complete WYSIWYG rich-text control.

**Native PPTX tables and column charts.** Tables are real DrawingML tables, not collections of rectangles. Supported column charts export as chart parts with cached data and editable embedded XLSX workbooks, not bars and labels. Import preserves one unstacked series; unsupported chart types are diagnosed, not silently reduced to one series.

**Geometry and accessibility.** Rotated resizing keeps the opposite handle fixed, vertical-side aspect resizing works, triangle hit testing rejects empty corners, and marquee selection uses rotated visual extents. Alternative text is editable and preserved in PPTX.

## The workspace

The custom Uno workspace combines a compact title bar, quick-access commands, tabbed ribbon, resizable filmstrip, Skia canvas, notes, formatting/selection/comment panes, slide sorter and slide show. The browser uses the same .NET application compiled to WebAssembly, not an HTML mock-up or a separate JavaScript presentation engine.

| Area | Implemented functionality |
|---|---|
| Slides | New, duplicate, delete, reorder, hide, non-destructive predefined layouts, backgrounds, slide sizing and numbers |
| Objects | Text, basic geometric shapes, lines/arrows, raster pictures, uniform tables and single-series column charts |
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
| `PresentationSpace.Core` | Immutable documents, rich-text operations, layouts, geometry, commands, selection and undo | No UI framework |
| `PresentationSpace.Formats` | Bounded PPTX ZIP/XML, native chart/table parts and embedded chart workbooks | Core |
| `PresentationSpace.Rendering.Skia` | Slides, mixed text, images, thumbnails, selection, PNG/PDF | Core + SkiaSharp |
| `PresentationSpace.Ribbon.Uno` | Ribbon tabs, groups, buttons and Office-style palette | Uno |
| `PresentationSpace.Controls.Uno` | Viewport, filmstrip, sorter, inspector, notes, splitters, status and slide show | Uno + renderer |
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

## Files, privacy and recovery

Native `.pspace` preserves this application's model, including mixed text, notes, local comments and animation settings. PNG/PDF are delivery formats. PPTX supports real text runs, preset shapes, embedded pictures, uniform native tables, native single-series column charts, notes and basic transitions. It does not preserve arbitrary unsupported OOXML parts.

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

Browser/OS interception can vary; ribbon alternatives are available. Search includes `Apply Blank layout`, `Apply Title only layout` and text-format commands.

## Automation and validation

The normal workflows are deliberately limited to three responsibilities:

- **Build and test:** Linux, Windows and macOS matrix; headless editing/geometry/rendering tests, native serialization, PPTX round trips, independent Open XML validation of presentations and chart workbooks, and desktop compilation.
- **Browser and GitHub Pages:** production WebAssembly publish, real Chromium keyboard interactions, selected-word formatting, undo/redo, content-preserving layouts, screenshots, six-library packaging and Pages deployment.
- **Release:** tag-triggered tests, self-contained desktop distributions, browser output, NuGet artifacts and a GitHub Release. Tagged release execution and signing are separate from ordinary build checks.

Read each run's results rather than treating configured coverage as completed qualification. Tests establish behavior for their covered cases, not every PowerPoint file, accessibility standard, GPU, browser or production-scale workload.

## Remaining major work

Complete master/layout/theme inheritance and authoring; full WYSIWYG rich text and complex-script shaping; merged/styled tables and multi-series/additional charts; SmartArt, media, freeform inking and advanced drawing effects; attached connectors; Office timing trees, motion paths and second-display presenter view; `.ppt`/`.pptm`; complete accessibility and touch qualification; secure cloud coauthoring/history/administration; and pixel-level PowerPoint UI parity remain unfinished.

See [Compatibility](docs/compatibility.md) for precise boundaries, including typed text-overlay behavior and non-lossless PPTX import. Contributions should add tests alongside functionality and keep modules independently consumable. See [Contributing](CONTRIBUTING.md).

## License and attribution

[MIT](LICENSE). Uno Platform and SkiaSharp are open-source dependencies. This project does not bundle Microsoft PowerPoint code, logos or proprietary fonts. PowerPoint and Microsoft are trademarks of Microsoft Corporation. PresentationSpace is independent and is not endorsed by or affiliated with Microsoft.

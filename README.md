<div align="center">

# PresentationSpace

**A familiar presentation workspace. An independent, reusable engine.**

Office-style slide editing built in C# with **Uno Platform** and **SkiaSharp**, targeting the browser and native desktop from one application.

[![Build and test](https://github.com/wieslawsoltes/PresentationSpace/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/PresentationSpace/actions/workflows/build.yml)
[![Browser and Pages](https://github.com/wieslawsoltes/PresentationSpace/actions/workflows/pages.yml/badge.svg)](https://github.com/wieslawsoltes/PresentationSpace/actions/workflows/pages.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

[**Open browser editor**](https://wieslawsoltes.github.io/PresentationSpace/) · [Architecture](docs/architecture.md) · [Compatibility](docs/compatibility.md) · [Releases](https://github.com/wieslawsoltes/PresentationSpace/releases)

</div>

> **0.1 development preview.** This is an independent implementation inspired by the PowerPoint desktop editing workflow—not Microsoft software, not a pixel-exact reproduction, and not full PowerPoint feature parity. The supported functionality and remaining gaps are documented explicitly. Keep original PowerPoint files when importing.

## The workspace

PresentationSpace combines a compact title bar, quick-access commands, a tabbed ribbon, a resizable slide filmstrip, a Skia-rendered editing canvas, speaker notes, formatting/selection/comment panes, a slide sorter, and a slide-show view.

The UI is implemented with Uno controls and custom C# components. The slide engine is SkiaSharp; the browser build is the same .NET application compiled for WebAssembly, not an HTML mock-up or a separate JavaScript presentation engine.

### Implemented capabilities

| Area | Available in this implementation |
|---|---|
| Slide editing | New, duplicate, delete, reorder, hide, predefined layouts, slide backgrounds, widescreen/standard/portrait sizing, slide numbers |
| Objects | Text boxes, rectangles, rounded rectangles, ellipses, triangles, diamonds, lines, arrows, raster pictures, simple tables and bar charts |
| Direct manipulation | Selection, multi-selection, marquee, move, eight resize handles, rotation handle, aspect constraint, grid snapping, keyboard nudging |
| Arrangement | Group selection, ungroup, front/back ordering, six alignment modes, horizontal/vertical distribution, object lock and visibility |
| Typography | Font family/size, bold, italic, underline, bullets, paragraph alignment, line spacing, vertical alignment, wrapping and inline text editing |
| Data | Editable table cells and chart labels/values through the formatting pane |
| Presentation | None/fade/push/wipe slide transitions; appear/fade/fly-in object effects; hidden-slide skipping; previous/next; black/white screen |
| Workflow | Immutable undo/redo, internal clipboard, command search, local comments, find/replace, notes and device-local recovery |
| Files | Native `.pspace` JSON, documented-subset PPTX import/export, slide PNG export, vector PDF export |

The ribbon provides Home, Insert, Draw, Design, Transitions, Animations, Slide Show, Review, View, Shape Format and Help tabs, plus the File workspace. Commands act on the shared document session; unsupported cloud functionality is not simulated.

## Start developing

Requirements: **.NET 10 SDK** and the `wasm-tools` workload for browser builds. `global.json` pins **Uno SDK 6.7.30**. The SDK's supported bundle uses **Uno 6.7.135** and **SkiaSharp 3.119.2**; Skia packages are intentionally kept on that matched version.

```sh
git clone https://github.com/wieslawsoltes/PresentationSpace.git
cd PresentationSpace
dotnet workload install wasm-tools

# Engine, file-format, rendering and Open XML schema tests
dotnet test tests/PresentationSpace.Tests/PresentationSpace.Tests.csproj -c Release

# Browser development
dotnet run --project src/PresentationSpace.App/PresentationSpace.App.csproj \
  -f net10.0-browserwasm -p:PresentationTargetFrameworks=net10.0-browserwasm

# Native desktop
dotnet run --project src/PresentationSpace.App/PresentationSpace.App.csproj \
  -f net10.0-desktop -p:PresentationTargetFrameworks=net10.0-desktop
```

The custom `PresentationTargetFrameworks` property restricts **Uno projects only**. Do not globally override `TargetFrameworks`: that would incorrectly retarget the UI-independent libraries.

### Publish the browser build

```sh
dotnet publish src/PresentationSpace.App/PresentationSpace.App.csproj \
  -f net10.0-browserwasm -c Release \
  -p:PresentationTargetFrameworks=net10.0-browserwasm \
  -p:WasmShellWebAppBasePath=/PresentationSpace/ -o artifacts/browser
python3 tools/prepare-pages.py artifacts/browser artifacts/site
```

Serve the resulting directory over HTTP(S), at the configured base path. Opening the HTML through `file://` is not supported. The GitHub Pages workflow publishes this distribution and verifies the application in headless Chromium before deployment.

## Six reusable libraries

| Package/project | Responsibility | UI dependency |
|---|---|---|
| `PresentationSpace.Core` | Immutable document model, geometry, validation, commands, selection, transactions and undo/redo | None |
| `PresentationSpace.Formats` | Bounded OOXML ZIP/XML import and export with compatibility diagnostics | None |
| `PresentationSpace.Rendering.Skia` | Slide rendering, typography, image/typeface caches, selection adorners, PNG and PDF export | SkiaSharp only |
| `PresentationSpace.Ribbon.Uno` | Ribbon tabs, groups, command buttons and Office-style palette | Uno |
| `PresentationSpace.Controls.Uno` | Viewport, previews, filmstrip, sorter, inspector, notes, color palette, splitters, status bar and slide show | Uno + renderer |
| `PresentationSpace.Editor.Uno` | Composed editor and injectable storage contract | The reusable components above |

`PresentationSpace.App` is the small executable host. The libraries are packable independently; there is **no dependency from a library back to the application**. Package artifacts are produced by release automation; automatic publishing to nuget.org is deliberately not configured.

### Embed the complete editor

```csharp
using PresentationSpace.Core;
using PresentationSpace.Editor.Uno;

var session = new EditorSession(SlideFactory.Welcome());
var editor = new PresentationEditor(session, myStorageService);
myPage.Content = editor;
```

Implement `IWorkspaceStorage` to provide file pickers/downloads and recovery storage for your host. A canceled save must return `false`. The application includes a device-local Uno implementation.

### Use the engine without Uno

```csharp
using PresentationSpace.Core;
using PresentationSpace.Formats;
using PresentationSpace.Rendering.Skia;

var session = new EditorSession();
session.Insert(SlideFactory.Text("Hello, slides", 80, 100, 900, 130, 56));
session.Insert(ShapeKind.Ellipse);
session.Align(AlignKind.Right);
session.Undo();

using var renderer = new SlideRenderer();
byte[] png = renderer.ExportPng(session.Document, session.CurrentSlide);
ExportResult pptx = PptxCodec.Export(session.Document);
// Always surface pptx.Warnings to the user before relying on interchange fidelity.
```

A headless host must supply the appropriate SkiaSharp native-assets package for its operating system. Rendering is single-thread-affine; use one renderer per concurrent worker and dispose it.

## Files, privacy and recovery

`.pspace` is the editable native format and preserves the application's model, including notes, local comments and animation settings. PNG/PDF are delivery formats. PPTX interoperability is intentionally explicit about approximation: chart/table export currently creates editable primitives, and import does not reconstruct every PowerPoint feature.

Editing and local recovery do not require an account or server. **AutoSave is a device-local recovery copy, not OneDrive or multi-user synchronization.** Download a native file for durable storage and transfer. Browser storage may be cleared or evicted; recovery is not a backup guarantee.

The import layer rejects oversized/duplicate ZIP parts, prohibits XML DTDs, bounds XML expansion, validates native geometry and identifiers, and does not execute macros or retrieve external assets. See [Security](SECURITY.md) for limits and reporting guidance.

## Keyboard and pointer workflow

| Action | Shortcut |
|---|---|
| New slide / save / open | `Ctrl+M` / `Ctrl+S` / `Ctrl+O` |
| Undo / redo in the viewport | `Ctrl+Z` / `Ctrl+Y` or `Ctrl+Shift+Z` |
| Copy / cut / paste / duplicate selected objects | `Ctrl+C` / `Ctrl+X` / `Ctrl+V` / `Ctrl+D` |
| Select all / group / ungroup | `Ctrl+A` / `Ctrl+G` / `Ctrl+Shift+G` |
| Edit text | Double-click, `F2`, or `Enter` |
| Move selection | Arrow keys; hold `Shift` for ten-unit steps |
| Constrain resize / rotate | Hold `Shift` |
| Temporarily disable grid snapping | Hold `Alt` while dragging |
| Zoom | `Ctrl+wheel`; slider; Fit to Window |
| Command search | `Alt+Q` |
| Present from beginning / current slide | `F5` / `Shift+F5` |
| Navigate / end slide show | Arrows, Page Up/Down, Space / `Esc` |
| Black / white screen | `B` / `W` during slide show |

Browser and operating-system shortcut interception can vary. Ribbon and status-bar alternatives remain available.

## Automation and quality gates

- **Build and test:** unit/geometry/editing tests, native serialization, PPTX round trips, Open XML schema validation, PNG/PDF smoke tests, and desktop compilation.
- **Browser and GitHub Pages:** workload restore, production WebAssembly publish, distribution validation, Chromium startup/edit/undo/presentation checks, screenshots, artifact upload and Pages deployment.
- **Release:** tagged version builds, tests, independently packaged NuGet libraries, self-contained desktop distributions, browser distribution and a GitHub Release. No signing credentials or package-registry token are required for unsigned release artifacts.

Checks are evidence for their specific covered paths—not a claim of full PowerPoint compatibility, complete accessibility conformance, production-scale performance or testing across every browser and native OS.

## Roadmap and boundaries

Major remaining areas include complete master/layout/theme inheritance, mixed rich-text editing and shaping, native charts/workbooks and table semantics, SmartArt, freeform inking, complex paths/effects, connectors with attachment logic, motion paths and a full animation timeline, media/audio/video, real presenter view on a second display, native `.ppt`/`.pptm`, comprehensive accessibility, enterprise collaboration and administration, and pixel-level PowerPoint visual parity.

See [Compatibility](docs/compatibility.md) for the precise import/export boundary. Contributions should add tests alongside behavior and preserve independently reusable modules. See [Contributing](CONTRIBUTING.md).

## License and attribution

[MIT](LICENSE). Uno Platform and SkiaSharp are third-party open-source dependencies; the application does not bundle Microsoft PowerPoint code, logos or proprietary fonts. PowerPoint and Microsoft are trademarks of Microsoft Corporation. This project is independent and is not endorsed by or affiliated with Microsoft.

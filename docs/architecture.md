# Architecture

## Dependency direction

```text
App ──> Editor.Uno ──> Controls.Uno ──> Rendering.Skia ──> Core
               │              └──────> Ribbon.Uno
               └────> Formats ────────────────────────> Core
```

Core and Formats target `net10.0`. Rendering targets `net10.0` and references SkiaSharp. Uno libraries target browser WebAssembly and native desktop, with an explicit `Library` output type and generated library layout metadata.

## Document and transaction model

Documents, slides, styles and shapes are immutable records. Collections use immutable arrays/dictionaries. Images are stored once as document assets and referred to by ID. Undo stores structurally shared document snapshots rather than copying an entire image payload for every pointer movement.

`EditorSession` is the command boundary. A normal command captures before/after state. A pointer transform uses `BeginGesture`, `PreviewShapes`, and `CommitGesture`; previews are computed from the gesture's original geometry and produce one history entry on release. Cancellation restores the original document. History is bounded to 150 commands. Callbacks distinguish preview changes from committed changes, avoiding a recovery write on every pointer event.

Selection and active-slide state are separate from the serializable document. Grouping uses a group identifier and shared selection; it is not a nested transform hierarchy. Renderers and sessions should be confined to their owning thread.

## Geometry and coordinates

Slides use 96-DPI logical units. The default page is 1280 × 720. Viewports apply an explicit fit/zoom transform, then draw the document and selection overlays. Pointer positions are transformed back into document space. Rotated hit testing inverse-transforms the point into each object's local rectangle. Images are fit within their bounds; cropping and arbitrary image effects are not implemented.

## Rendering

`SlideRenderer` handles shapes, wrapped text, images, simple tables and bar charts. It owns a typeface cache and a 64 MB image-cache budget; images over 16 megapixels are rejected before use. PNG export rasterizes to an explicitly sized surface; PDF uses Skia's vector document canvas. Font availability and Skia fallback affect rendering across systems.

Rendering is invalidation-driven for editing. The slide-show timer runs while an entry effect/transition is active and stops when its duration has elapsed. This is not a qualified rendering benchmark or a claim of full GPU residency; Skia chooses its backend, and export uses CPU-accessible surfaces.

## Reusable controls

`RibbonControl`, `RibbonGroup`, and `RibbonCommandButton` do not reference the document engine. `SlideViewport`, `SlideFilmstrip`, `FormatPane`, `NotesPane`, `SlideSorter`, `PresentationStatusBar`, `PaneSplitter`, `ColorPalette`, and `PresentationPlayer` are individually consumable controls. Session controls detach event handlers when unloaded.

`PresentationEditor` composes these controls and provides the complete workspace. Storage is injected through `IWorkspaceStorage`. A host may replace pickers, persistence or recovery without changing the renderer or document model.

## Storage and interoperability

Native JSON uses generated `System.Text.Json` metadata and validates input. PPTX uses BCL ZIP/XML APIs with explicit package limits and diagnostics. It does not launch Office, use a remote conversion service, or evaluate embedded content. The Open XML SDK is used only by the test project to independently validate exported package schemas.

Save-to-file and local recovery have different semantics: recovery does not mark an unsaved portable file as saved. The host returns a boolean for file save so cancellation is not misreported as success.

## Extending the system

Add model data and validation in Core, command behavior in `EditorSession`, rendering in Rendering.Skia, format handling in Formats, and controls in Controls.Uno. Add headless regression tests before wiring a ribbon command. Document any interchange approximation. Do not introduce a reference from a reusable library to App or hide unsupported behavior behind a no-op command.

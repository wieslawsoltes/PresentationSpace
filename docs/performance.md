# Performance and direct canvas editing

## Rendering architecture

`PresentationCanvas` derives from Uno `SKCanvasElement`. The editor viewport, slide previews and presentation player receive the compositor canvas in logical coordinates. They do not reset its matrix or clear the whole destination; every draw restores the caller's matrix, clip and save count. This removes the application's previous `SKXamlCanvas` raster-copy path on the configured Uno Skia targets. Backend availability still determines actual hardware acceleration; no dedicated physical-GPU qualification is implied.

`SlideRenderer` retains local-coordinate non-image shape pictures, ignoring only properties handled by the outer transform/opacity/animation layer. Changes to dimensions, shape content or styles invalidate the affected picture. Repeated static slides can reuse one retained scene. Animated frames continue evaluating their transforms and opacity. PNG/PDF use the same model; these export paths are not fully GPU-resident.

The default picture LRU is limited to 1,024 entries and 16 MiB of Skia's approximate picture-byte accounting. The one retained scene has its own 16 MiB ceiling including estimated dependencies; the two estimates can total up to 32 MiB. Image decoding retains its separate 64 MiB budget and images are deliberately excluded from retained scenes. These are conservative cache estimates, **not process-memory or GPU-memory limits**: managed models, font glyph caches and backend resources have other costs. Filmstrip/sorter use shared 8 MiB picture caches and no retained scene. Disabling picture caching or clearing/disposal releases the retained entries. Call `ClearRenderCache()` after changing a typeface provider in place; replacing the provider invalidates automatically.

## Deck-scale work

`VirtualSlideLayout` computes an allocation-free half-open visible range. `VirtualSlideView` recycles only viewport tiles, one row of overscan, and a bounded reuse pool. Tile preview updates compare drawing inputs; note/name/selection changes do not re-render the slide content. Pointer gesture previews defer thumbnail updates until commit. Keyboard and drag/drop operations resolve current slide identity rather than trusting stale recycled indices.

`EditorSession.EditSlide` reconciles only the changed slide. Rich-text reconciliation returns immutable inputs unchanged for geometry-only edits, uses positional identity when order is stable, and builds ID lookups only when needed. `PrimaryShape` and `SelectedShapes` share a selection cache. Native save/export/recovery still serialize the complete document and can remain noticeable for large files; this update does not claim constant-time persistence.

## Reproducible comparison

Run from a full repository checkout with the .NET 10 SDK:

```sh
bash tools/benchmark-comparison.sh
```

The script creates a detached worktree at `8d1de0fdd59fbc96ee6718723ac9b6e9e9f4088a` (0.4), copies the exact same benchmark driver into it, and runs baseline/current sequentially on the same machine. Tiered compilation is disabled in both processes to avoid tier-promotion bias. Eight warm-up iterations are excluded; 200 editing or 80 raster iterations report median, 95th percentile, and managed bytes allocated on the executing thread. The driver covers reconciliation/nudging in a 1,000-slide, 12,000-object document and repeated/moving-object raster drawing of 300 mixed-text shapes at 1280×720.

Results are generated under `artifacts/performance` and uploaded by the Linux build job as `performance-comparison`. They are synthetic CPU/raster measurements, **not browser FPS, startup time, real-world Office comparison or physical-GPU frame duration**. Host load, JIT, fonts and driver/backend choices affect results; inspect raw JSON and rerun locally rather than generalizing a single speed ratio.

`tools/browser-performance.py` exercises real keyboard/pointer input against the production WebAssembly build. It checks direct cell editing, text-driven row sizing, and 1,000-slide navigation with bounded realized and allocated tile counts. Its optional input-to-model timing includes Playwright overhead and is not a rendering benchmark. The public sample and Rendering statistics command are also available interactively; the diagnostics do not mutate documents.

## Direct table editing and measurement boundaries

Double-click a slide cell to open native input. Enter/F2 edits a selected cell; Tab/Shift+Tab navigates merge origins; arrows move across cells and Shift extends the range. Ctrl+Enter commits, Escape cancels the current text draft, and focus loss commits. Undo remains transactional. Save and export flush pending cell text. Automatic local recovery stores committed document edits, not an active input draft; commit with Ctrl+Enter or move focus before relying on recovery. A concurrently replaced or deleted table is never overwritten by a stale cell editor.

The plain input overlay does not display individual rich styles while typing. Character-range styles are retained in the document and shown by Skia after commit. Inserted text uses reconciliation rather than full Office insertion-style semantics; bidirectional shaping, IME behavior and assistive technologies need additional qualification.

Auto-fit measures the same wrapped text lines used by table rendering, includes margins/borders, and distributes deficits across merged row spans. It preserves column widths, cell content and the table's rotated opposite-edge anchor. This is explicit row fitting, not continuous Office auto-layout; oversized results are rejected, and complete Office line-breaking/complex-script metrics remain unfinished.

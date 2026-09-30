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

The historical 0.6 comparison created a detached worktree at `74334f0e23865d62e506093b9929d4e29e0a9175` (0.5), copied the exact same benchmark driver into it, and ran baseline/current sequentially on the same machine. Subsequent 0.7 and 0.8 baselines are described below. Tiered compilation is disabled in both processes to avoid tier-promotion bias. Eight warm-up iterations are excluded; 200 editing or 80 raster iterations report median, 95th percentile, and managed bytes allocated on the executing thread. The driver covers reconciliation/nudging in a 1,000-slide, 12,000-object document and repeated/moving-object raster drawing of 300 mixed-text shapes at 1280×720. It also measures 512 deterministic cell lookups and repeated bounds changes for one 64×64-cell immutable table.

Results are generated under `artifacts/performance` and uploaded by the Linux build job as `performance-comparison`. They are synthetic CPU/raster measurements, **not browser FPS, startup time, real-world Office comparison or physical-GPU frame duration**. Host load, JIT, fonts and driver/backend choices affect results; inspect raw JSON and rerun locally rather than generalizing a single speed ratio.

`tools/browser-performance.py` exercises real keyboard/pointer input against the production WebAssembly build. It checks direct cell editing, text-driven row sizing, and 1,000-slide navigation with bounded realized and allocated tile counts. Its optional input-to-model timing includes Playwright overhead and is not a rendering benchmark. The public sample and Rendering statistics command are also available interactively; the diagnostics do not mutate documents.

## Direct table editing and measurement boundaries

Double-click a slide cell to open native input. Enter/F2 edits a selected cell; Tab/Shift+Tab navigates merge origins; arrows move across cells and Shift extends the range. Ctrl+Enter commits, Escape cancels the current text draft, and focus loss commits. Undo remains transactional. Save and export flush pending cell text. Automatic local recovery stores committed document edits, not an active input draft; commit with Ctrl+Enter or move focus before relying on recovery. A concurrently replaced or deleted table is never overwritten by a stale cell editor.

The plain input overlay does not display individual rich styles while typing. Character-range styles are retained in the document and shown by Skia after commit. Inserted text uses reconciliation rather than full Office insertion-style semantics; bidirectional shaping, IME behavior and assistive technologies need additional qualification.

Auto-fit measures the same wrapped text lines used by table rendering, includes margins/borders, and distributes deficits across merged row spans. It preserves column widths, cell content and the table's rotated opposite-edge anchor. This is explicit row fitting, not continuous Office auto-layout; oversized results are rejected, and complete Office line-breaking/complex-script metrics remain unfinished.

## Table indexing and measured 0.5 comparison

`TableGridIndex` shares validated ownership and merge-origin reading order by immutable table identity. The first access validates the snapshot, sorts its origins and builds the index; subsequent access avoids repeated linear `CellAt` scans, validation and ownership-map construction. `TableLayout` retains independent coordinate arrays and binary-searches track boundaries. A content edit creates a new snapshot and pays the first-use cost again. Weak keys allow unreferenced snapshots and their indexes to be collected, but live undo snapshots still consume memory. These are warm-index optimizations, not a claim of constant-time new-table creation or whole-PPTX export.

The initial PR implementation (`72c19c1`, Actions run `36346283055`) ran the identical driver sequentially against 0.5 and 0.6 on one Linux runner:

| Warm workload | 0.5 median ms | 0.6 median ms | Managed bytes/op, before → after |
|---|---:|---:|---:|
| 512 lookups in a 64×64 table | 2.1029 | 0.0056 | 45,056 → 0 |
| Layout the same 64×64 table at changing bounds | 1.5894 | 0.0008 | 1,873,260 → 736 |

The four existing editing/raster workloads were essentially unchanged (speed ratios 1.00–1.04× in that run). The large ratios for table workloads reflect the intentionally repeated, already-indexed case; they must not be generalized to overall editing, cold loading, export or frame rate. All timing and native-allocation limitations above apply. Inspect the `performance-comparison` artifact and rerun on representative documents.


## 0.7 shared shaped-text workloads

The historical 0.7 comparison baseline is immutable commit `42d73dac8e81931ab2f25b095e0aad4cc23207d0` (0.6 plus package documentation). The same driver is compiled against both source trees and retains the prior six editing/raster/table workloads. Four additional workloads distinguish repeated paragraph measurement, repeated paragraph drawing, first-use rich paragraph layout, and first-use emergency wrapping of a 12,000-character token. Each first-use iteration creates/releases its own renderer; no cached layout is presented as cold work.

HarfBuzz shaping performs more work than unshaped character drawing. The retained text-layout cache is shared by drawing and measurement, independent of shape position and height. A width, text, range, style or font-resolver version change causes a new layout. The layout cache is bounded by entry count and approximate retained bytes; temporary build allocations/native storage and GPU resources are not measured by managed-allocation counters. Font combinations per layout are limited to 256. Long tokens use exponentially bounded prefix probes and grapheme-safe binary refinement, rather than repeatedly scanning the entire remaining suffix.

CI writes `baseline.json`, `current.json` and `comparison.md`. Use those observed results for the tested commit; no blanket speed-up is guaranteed. Warm and first-use text results, as well as any regression in the existing six workloads, must be reported together. These synthetic CPU/raster checks are not browser FPS, physical-GPU completion time, cold application startup, native Office fidelity or an end-to-end presentation benchmark.

First-use layout also indexes font-only sections once at extended-grapheme boundaries, caches font metrics for the build, and reuses exact shaped fragments with the same resolved font/size. This temporary cache has at most 256 entries and 32,768 retained characters (individual fragments at most 4,096); it is separate from the retained text-layout LRU. Per-source paint styles are applied after shaping so reusing a word does not reuse another occurrence's color or underline. These bounds do not cap peak allocation for the entire document.

## 0.8 paragraph/body layout workloads

`tools/benchmark-comparison.sh` now uses immutable 0.7 commit `2044ea15a92ed4384c685a9ea73dd85b850811a6`. The identical public-API driver compiles against both source trees, with all ten existing editing, raster, table, warm-text and first-use-text workloads retained. Do not compare ratios across different CI runners. The artifacts retain both medians and allocation costs, including regressions, rather than only favorable cached measurements.

For splittable tokens longer than 512 UTF-16 units, wrapped layout now avoids a complete shaping pass that would be discarded before prefix fitting. Every candidate prefix still uses HarfBuzz shaping and extended grapheme boundaries; there is no guessed-width approximation. No-wrap and explicit non-breaking groups still shape the full span. A long token that fits the remaining line is retained there rather than forcing a new line. Common uniform inset specs are reused immutable values. Paragraph layout adds work and can change line counts; all reported timings are synthetic CPU/raster observations, not universal speed guarantees, browser FPS or physical-GPU duration.

## 0.9 tab layout and drawing ownership

The same-driver comparison now uses immutable 0.8 baseline `24b4799cdab540f9f754198b3b56a4a547ad36a4`. All ten earlier workloads remain, plus repeated drawing and first-use layout of ordinary tabbed text. Both versions support those public APIs; no unsupported baseline feature is represented as a fast or slow custom-tab implementation. Aligned custom fields require measured lookahead, so their first-use cost depends on text length/styles. Source-position-specific shaped pieces are reused within each field rather than shaping twice during emission.

A `TextLayoutEngine` owns and lazily reuses its private `SKPaint` for drawing. It remains single-thread-affine and disposes that native resource exactly once; paint is not returned to hosts. Warm layout measurements and native painting are different workloads. Use the executed CI artifact medians/allocations, including regressions; do not infer whole-application speed, physical-GPU completion, browser FPS or cold startup from CPU/raster microbenchmarks. Managed bytes exclude native and GPU allocations.

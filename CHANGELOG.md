# Changelog

## 0.2.0 — development preview

### Added

- Non-destructive predefined layout mapping, placeholder roles/indexes, five native PPTX layout parts, and inherited placeholder geometry lookup.
- Immutable rich-text ranges; character/paragraph formatting APIs; mixed-style Skia rendering; native PPTX text runs.
- Incremental text drafts and style-preserving Replace all, including large-match and surrogate-boundary regression coverage.
- Native DrawingML tables and single-series column charts with editable embedded XLSX workbooks.
- Bounded chart-cache import, unsupported-chart diagnostics, and independent presentation/workbook schema tests.
- Alternative-text editing and PPTX preservation.
- Windows/macOS/Linux test and desktop-build matrix, plus browser tests for selected-word formatting and content-preserving layout changes.

### Fixed

- Layout changes no longer remove unmatched text or custom artwork.
- Rotated resizing preserves the opposite anchor; vertical-side aspect resizing works without changing the existing corner-resize contract.
- Triangle hit testing rejects empty corners, and marquee bounds account for rotation.
- Text replacement and base-format changes preserve unaffected mixed character styles.

### Boundaries

This is not full PowerPoint parity. Rich-text input is not yet fully WYSIWYG while typing; complex shaping, complete master/theme inheritance, advanced native tables/charts, SmartArt/media, animation timing trees, coauthoring and full accessibility remain unfinished. See [Compatibility](docs/compatibility.md). Tagged releases, package-registry publication and signing are separate from CI package generation.

## 0.1.0 — initial development preview

- Shared immutable document/command engine, Skia rendering, and six packable reusable libraries.
- Custom Uno ribbon/editor components, browser and desktop targets.
- Basic editing, notes, local comments/recovery, slideshow, native files, PPTX subset and PNG/PDF export.
- Initial headless/browser regression tests and GitHub Pages deployment.

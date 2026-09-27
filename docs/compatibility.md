# Compatibility and limitations

This preview implements a useful presentation-editing subset. It is not a complete PowerPoint clone, byte-preserving OOXML editor, or certified Office replacement.

## Format matrix

| Feature | Native `.pspace` | PPTX import | PPTX export |
|---|---|---|---|
| Slide sizes/order/hidden state | Preserved | Supported | Supported |
| Basic text boxes | Preserved | Supported, simplified styling | Editable text |
| Mixed rich-text runs | Not modeled | First run style applied to the box | Uniform runs per box |
| Basic preset shapes | Preserved | Supported subset; other presets approximated | Editable presets |
| Rotation | Preserved | Basic object rotation | Supported |
| Raster pictures | Embedded assets | PNG/JPEG/GIF/WebP subset | Embedded pictures |
| Groups | Group identifiers | Flattened; complex group transforms may differ | Flattened |
| Tables | Cell strings, uniform grid | Cell text; styling/spans simplified | Editable cell shapes, not native tables |
| Charts | One-series bar chart | Native chart frames omitted | Editable bars/labels, not native charts |
| Speaker notes | Preserved | Body notes | Supported |
| Local review comments | Preserved | Not imported | Omitted with warning |
| Transitions | Preview settings | Basic fade/push/wipe | Basic transitions; exact timing not preserved |
| Object animations | Preview settings | Not imported | Omitted with warning |
| Master/layout/theme inheritance | Limited layouts/theme palette | Partial geometry fallback; artwork/inherited styling not fully reconstructed | One blank layout and generated master/theme |
| SmartArt, OLE, macros, media | Not modeled | Not executed; unsupported frames omitted | Not exported |

PPTX import always emits a compatibility warning. Unsupported features are not stored as opaque parts for lossless future round-trip. **Keep the original file.** A successful Open XML schema check validates package structure, not exact PowerPoint rendering or every Office repair behavior.

## Editing boundaries

Text editing is currently one string/style per shape, not a complete rich-text editor. Complex-script shaping, bidirectional paragraph layout, ligatures, font embedding, baseline grids and typography equivalence with Office are not qualified. The inline text overlay is not rotation-aware. Table merges, formula/data links, arbitrary chart types, freeform ink, connectors attached to ports, crop/mask editing, picture filters, gradient/mesh fills, 3D and advanced effects are absent.

Layout selection currently resets text placeholders while retaining non-text objects; it does not perform full PowerPoint placeholder-content migration. Front/back commands move to the extreme of the layer order. Grouping links selection rather than creating a nested coordinate system. Presenter mode fills the app viewport, not a second-display presenter console. Animation order/timing is a basic sequential preview, not an Office timing-tree implementation.

Comments and recovery are local. No identity provider, shared document server, CRDT, access-control system, cloud history, OneDrive integration or simultaneous coauthoring is implemented. Share exports a file.

## Limits

Native/PPTX file input: 64 MB. PPTX expansion: 128 MB total, 32 MB per part, 10,000 parts. Native documents: 2,000 slides and 20,000 shapes. Raster image decode: 16 megapixels; image insertion: 20 MB. PNG export: at most 32 megapixels. The parser rejects invalid/duplicate IDs and non-finite geometry, and does not retrieve external relationships.

These are defensive limits, not performance guarantees. Large-document responsiveness, all supported browsers, touch-only workflows, assistive technologies and every native platform still require broader qualification.

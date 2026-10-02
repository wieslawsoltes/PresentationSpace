using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using PresentationSpace.Core;
using PresentationSpace.Rendering.Skia;
using SkiaSharp;

// This exact driver is compiled against both the immutable 0.11 baseline and the changed source in CI.
// CPU/raster benchmark, not a hardware-GPU benchmark or an end-to-end browser FPS claim.
var results = new List<object>();
void Measure(string name, int iterations, Action<int> work)
{
    for (int i = 0; i < 8; i++) work(i);
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    var times = new double[iterations]; long allocated = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < iterations; i++) { long start = Stopwatch.GetTimestamp(); work(i); times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds; }
    long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated; Array.Sort(times);
    results.Add(new { name, iterations, medianMilliseconds = times[iterations / 2], p95Milliseconds = times[(int)(iterations * .95)], bytesPerOperation = bytes / (double)iterations });
}
var shape = SlideFactory.Text("Immutable selected text", 10, 10, 200, 100) with { TextRanges = [new(0, 9, new TextStyle { Bold = true })] };
var slides = Enumerable.Range(0, 1000).Select(i => new Slide { Shapes = Enumerable.Range(0, 12).Select(j => shape with { Id = Guid.NewGuid() }).ToImmutableArray() }).ToImmutableArray();
var document = new PresentationDocument { Slides = slides };
var editedSlide = slides[500] with { Shapes = slides[500].Shapes.SetItem(0, slides[500].Shapes[0] with { Bounds = new(20, 20, 200, 100) }) };
var edited = document with { Slides = slides.SetItem(500, editedSlide) };
PresentationDocument? sink = null;
Measure("reconcile-one-shape-in-1000-slide-deck", 200, _ => sink = RichText.Reconcile(document, edited));
var session = new EditorSession(document); session.SelectSlide(500); session.Select(session.CurrentSlide.Shapes[0].Id);
Measure("nudge-one-shape-in-1000-slide-deck", 200, i => session.Nudge(i % 2 == 0 ? 1 : -1, 0));
var drawings = Enumerable.Range(0, 300).Select(i => SlideFactory.Text("Mixed text " + i, i % 15 * 84, i / 15 * 35, 82, 32, 10) with { TextRanges = [new(0, 5, new TextStyle { Bold = true, FontSize = 10 })] }).ToImmutableArray();
var scene = new Slide { Shapes = drawings }; var deck = new PresentationDocument { Slides = [scene] };
using var surface = SKSurface.Create(new SKImageInfo(1280, 720)); using var renderer = new SlideRenderer();
Measure("render-static-300-rich-text-shapes", 80, _ => renderer.Render(surface.Canvas, deck, scene));
Measure("render-one-moving-shape-of-300", 80, i => renderer.Render(surface.Canvas, deck, scene with { Shapes = drawings.SetItem(0, drawings[0] with { Bounds = drawings[0].Bounds with { X = i % 8 } }) }));
var table = TableModel.Create(64, 64);
TableCell? cellSink = null;
Measure("lookup-512-cells-in-64x64-table", 80, iteration =>
{
    for (int n = 0; n < 512; n++) { int slot = (n * 17 + iteration * 97) % 4096; cellSink = TableModel.CellAt(table, slot / 64, slot % 64); }
});
TableLayout? layoutSink = null;
Measure("layout-64x64-table-at-changing-bounds", 80, i => layoutSink = new TableLayout(table, new(i % 10, 0, 1280, 720)));
// Public APIs are intentionally shared with the 0.11 baseline. Warm layout and
// first-use shaping are separate workloads; report both, not only cache hits.
var paragraph = SlideFactory.Text(string.Join(" ", Enumerable.Repeat("Office typography: AVATAR, efficient spaces and shared measurements.", 40)), 0, 0, 620, 700, 18);
paragraph = paragraph with { TextRanges = [new(7, 10, new() { FontSize = 25, Bold = true })] };
using var textRenderer = new SlideRenderer();
float heightSink = 0;
Measure("measure-warm-rich-paragraph", 120, _ => heightSink = textRenderer.MeasureRichTextHeight(paragraph, 620, 3));
Measure("draw-warm-rich-paragraph", 80, _ => textRenderer.DrawRichText(surface.Canvas, paragraph));
Measure("layout-first-use-rich-paragraph", 24, _ =>
{
    using var cold = new SlideRenderer(); heightSink = cold.MeasureRichTextHeight(paragraph, 620, 3);
});
var longToken = SlideFactory.Text(new string('x', 12000), 0, 0, 300, 600, 14);
Measure("layout-first-use-12000-character-token", 16, _ =>
{
    using var cold = new SlideRenderer(); heightSink = cold.MeasureRichTextHeight(longToken, 300, 3);
});
// Explicit tab types are new in 0.9, so compare the unchanged public regular-tab
// API separately, not as though the baseline had supported the new feature.
using var tabEngine = new TextLayoutEngine();
var tabStyle = new TextStyle { FontSize = 16, DefaultTabSize = 100 };
const string tabText = "Part A\t12.50\tReady\nPart B\t3.25\tPending";
Measure("draw-warm-regular-tabbed-text", 160, _ => tabEngine.Draw(surface.Canvas, tabText, tabStyle, new(0, 0, 600, 140)));
Measure("layout-first-use-regular-tabbed-text", 80, _ =>
{
    using var cold = new TextLayoutEngine(); heightSink = cold.Measure(tabText, tabStyle, 600).Height;
});
// Same legacy picture APIs on both revisions. Decode and setup are outside warm draws.
using var pixels = new SKBitmap(512,256);
var random = new Random(7913);
for(int y=0;y<256;y++)for(int x=0;x<512;x++)pixels.SetPixel(x,y,new SKColor((byte)random.Next(256),(byte)random.Next(256),(byte)random.Next(256)));
using var rawImage=SKImage.FromBitmap(pixels);using var encoded=rawImage.Encode(SKEncodedImageFormat.Png,100);
var imageAsset=new PresentationAsset("shared","image/png",Convert.ToBase64String(encoded.ToArray()));
var picture=new SlideShape{Kind=ShapeKind.Image,AssetId=imageAsset.Id,Bounds=new(0,0,100,50),Fill="#00000000",StrokeWidth=0};
var pictureSlide=new Slide{Shapes=Enumerable.Range(0,128).Select(i=>picture with{Id=Guid.NewGuid(),Bounds=new(i%12*105,i/12*55,100,50)}).ToImmutableArray()};
var pictures=new PresentationDocument{Slides=[pictureSlide],Assets=ImmutableDictionary<string,PresentationAsset>.Empty.Add(imageAsset.Id,imageAsset)};
using var pictureRenderer=new SlideRenderer();
Measure("draw-warm-128-reused-pictures",80,_=>pictureRenderer.Render(surface.Canvas,pictures,pictureSlide));
var mediaDeck=pictures with{Slides=Enumerable.Range(0,24).Select(i=>new Slide{Shapes=[picture with{Id=Guid.NewGuid()}]}).ToImmutableArray()};
int repeatedPicturePptxBytes=0;
Measure("export-24-slides-sharing-one-picture",12,_=>repeatedPicturePptxBytes=PresentationSpace.Formats.PptxCodec.Export(mediaDeck).Data.Length);
// Both revisions import the same native solid/text deck. This isolates shared
// layout/master/theme XML parsing instead of benchmarking unsupported old gradients.
var importDeck = new PresentationDocument { Slides = Enumerable.Range(0, 120).Select(i => new Slide {
    LayoutName = "Title only", Shapes = [SlideFactory.Text("Slide " + i, 50, 40, 1100, 100)] }).ToImmutableArray() };
// The baseline writes one fixture next to its report; the current process reads
// those exact bytes. Exporter changes must not silently change the import workload.
string? importPath = args.Length == 0 ? null : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!, "import-source.pptx");
byte[] importBytes;
if (importPath is not null && File.Exists(importPath)) importBytes = File.ReadAllBytes(importPath);
else
{
    importBytes = PresentationSpace.Formats.PptxCodec.Export(importDeck).Data;
    if (importPath is not null) { Directory.CreateDirectory(Path.GetDirectoryName(importPath)!); File.WriteAllBytes(importPath, importBytes); }
}
string importSourceSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(importBytes));
Measure("import-120-slides-sharing-layout-and-theme", 20, _ => sink = PresentationSpace.Formats.PptxCodec.Import(importBytes).Document);
// Same ordinary solid-line model on both revisions; direct draws isolate
// per-frame object/paint allocation rather than cached scene replay.
var lineSlide = new Slide { Shapes = Enumerable.Range(0, 128).Select(i => new SlideShape {
    Kind = ShapeKind.Line, Bounds = new(i % 16 * 80, i / 16 * 85, 60, 40), Stroke = "#243247", StrokeWidth = 3 }).ToImmutableArray() };
var lineDeck = new PresentationDocument { Slides = [lineSlide] };
using var lineRenderer = new SlideRenderer { EnablePictureCache = false, EnableSceneCache = false };
Measure("draw-direct-128-solid-lines", 80, _ => lineRenderer.Render(surface.Canvas, lineDeck, lineSlide));
GC.KeepAlive(heightSink);
GC.KeepAlive(cellSink); GC.KeepAlive(layoutSink);
GC.KeepAlive(sink);
var report = new { runtime = Environment.Version.ToString(), os = Environment.OSVersion.ToString(), processorCount = Environment.ProcessorCount, importSourceSha256, repeatedPicturePptxBytes, results };
string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
if (args.Length > 0) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!); File.WriteAllText(args[0], json); }
Console.WriteLine(json);

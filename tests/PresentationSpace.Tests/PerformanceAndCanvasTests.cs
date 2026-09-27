using System.Collections.Immutable;
using PresentationSpace.Core;
using PresentationSpace.Formats;
using PresentationSpace.Rendering.Skia;
using SkiaSharp;
using Xunit;

namespace PresentationSpace.Tests;

public sealed class PerformanceAndCanvasTests
{
    [Theory]
    [InlineData(false, 220, 768)] [InlineData(false, 300, 960)] [InlineData(true, 1200, 800)] [InlineData(true, 400, 300)]
    public void VirtualRangesAreBoundedAndCoverVisibleRows(bool grid, double width, double height)
    {
        var layout = VirtualSlideLayout.Create(2000, width, .5625, grid);
        foreach (double offset in new[] { 0d, 100, layout.ExtentHeight / 2, layout.ExtentHeight - height })
        {
            var (start, end) = layout.VisibleRange(offset, height);
            Assert.InRange(start, 0, 1999); Assert.InRange(end, start + 1, 2000);
            Assert.True(end - start <= (Math.Ceiling(height / layout.Pitch) + 4) * layout.Columns);
            Assert.True(layout.Top(start) <= offset + .01);
        }
    }
    [Fact] public void VirtualEndRevealIncludesLastSlide()
    {
        var layout = VirtualSlideLayout.Create(2000, 1200, .5625, true);
        double offset = layout.RevealOffset(1999, 0, 800);
        Assert.Equal(2000, layout.VisibleRange(offset, 800).End);
        Assert.Equal(0, layout.RevealOffset(0, offset, 800));
        Assert.Equal((0, 0), layout.VisibleRange(0, 0));
    }
    [Fact] public void EmptyVirtualLayoutHasNoTiles()
    { var layout = VirtualSlideLayout.Create(0, 220, .5625, false); Assert.Equal((0, 0), layout.VisibleRange(0, 900)); Assert.Equal(0, layout.ExtentHeight); }
    [Theory] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(-1)]
    public void BadVirtualRatiosAreRejected(double ratio) => Assert.Throws<ArgumentOutOfRangeException>(() => VirtualSlideLayout.Create(1, 220, ratio, false));
    [Fact] public void TwoThousandSlideFixtureIsValid() { var d = PerformanceSample.Create(2000); DocumentSerializer.Validate(d); Assert.Equal(10000, d.Slides.Sum(s => s.Shapes.Length)); }
    [Fact] public void GeometryReconciliationKeepsUnchangedIdentity()
    {
        var shape = SlideFactory.Text("alpha beta", 0, 0, 300, 100) with { TextRanges = [new(0, 5, new TextStyle { Bold = true })] };
        var before = new Slide { Shapes = [shape, shape with { Id = Guid.NewGuid() }] };
        var moved = shape with { Bounds = shape.Bounds with { X = 12 } };
        var after = before with { Shapes = before.Shapes.SetItem(0, moved) };
        var result = RichText.Reconcile(before, after);
        Assert.Same(after, result);
        Assert.Same(before.Shapes[1], result.Shapes[1]);
        Assert.Equal(shape.TextRanges, result.Shapes[0].TextRanges);
    }
    [Fact] public void UnchangedShapeReconciliationDoesNotAllocateNewShape()
    { var s = SlideFactory.Text("abc", 0, 0, 200, 80) with { TextRanges = [new(0, 1, new TextStyle { Bold = true })] }; Assert.Same(s, RichText.Reconcile(s, s)); }
    [Fact] public void ReorderedSlidesStillReconcileStylesByIdentity()
    {
        var s = SlideFactory.Text("abcdef", 0, 0, 200, 80) with { TextRanges = [new(3, 3, new TextStyle { Bold = true })] };
        var a = new Slide { Shapes = [s] }; var b = new Slide(); var before = new PresentationDocument { Slides = [a, b] };
        var after = before with { Slides = [b, a with { Shapes = [s with { Text = "xabcdef" }] }] };
        var result = RichText.Reconcile(before, after); Assert.Equal(4, result.Slides[1].Shapes[0].TextRanges[0].Start); Assert.Same(b, result.Slides[0]);
    }
    [Fact] public void SelectedShapeCacheTracksUndoAndSelectionChanges()
    {
        var s = new EditorSession(); s.Insert(ShapeKind.Rectangle); var original = s.PrimaryShape!;
        Assert.Same(original, s.PrimaryShape); s.Nudge(8, 0); Assert.NotSame(original, s.PrimaryShape); s.Undo(); Assert.Same(original, s.PrimaryShape);
        s.Select(null); Assert.Null(s.PrimaryShape); Assert.Empty(s.SelectedShapes);
    }
    [Fact] public void RangeLookupMatchesSegmentsAtBoundaries()
    {
        var s = SlideFactory.Text("0123456789", 0, 0, 200, 80) with { TextRanges = [new(2, 2, new TextStyle { Bold = true }), new(6, 3, new TextStyle { Italic = true })] };
        for (int i = 0; i < s.Text.Length; i++) Assert.Equal(RichText.Segments(s, i, 1).Single().Style, RichText.StyleAt(s, i));
        Assert.Equal(s.TextStyle, RichText.StyleAt(s, -1)); Assert.Equal(s.TextStyle, RichText.StyleAt(s, s.Text.Length));
    }
    private static PresentationDocument Scene() => new() { Width = 640, Height = 360, Slides = [new Slide { Shapes = [
        SlideFactory.Text("Retained mixed text", 20, 20, 480, 70, 24) with { TextRanges = [new(0, 8, new TextStyle { FontSize = 24, Bold = true, Color = "#FF0000" })] },
        new SlideShape { Kind = ShapeKind.Ellipse, Bounds = new(60, 160, 140, 100), Rotation = 25, Opacity = .7f },
        TableModel.Apply(new SlideShape { Kind = ShapeKind.Table, Bounds = new(250, 150, 320, 180) }, TableModel.Create(3, 3))
    ] }] };
    private static byte[] Pixels(SlideRenderer renderer, PresentationDocument d, Slide? slide = null)
    {
        using var surface = SKSurface.Create(new SKImageInfo(640, 360)); surface.Canvas.Clear(SKColors.Transparent);
        renderer.Render(surface.Canvas, d, slide ?? d.Slides[0]); using var image = surface.Snapshot(); using var data = image.Encode(SKEncodedImageFormat.Png, 100); return data.ToArray();
    }
    [Fact] public void RetainedAndUncachedPixelsMatch()
    {
        var d = Scene(); using var cached = new SlideRenderer(); using var plain = new SlideRenderer { EnablePictureCache = false };
        Assert.Equal(Pixels(plain, d), Pixels(cached, d)); Assert.Equal(Pixels(plain, d), Pixels(cached, d)); Assert.True(cached.CacheStatistics.SceneHits > 0);
    }
    [Fact] public void NotesAndSelectionDoNotRebuildScene()
    {
        var d = Scene(); using var r = new SlideRenderer(); Pixels(r, d); var before = r.CacheStatistics;
        Pixels(r, d, d.Slides[0] with { Notes = "metadata only" }); Assert.Equal(before.Misses, r.CacheStatistics.Misses); Assert.Equal(before.SceneHits + 1, r.CacheStatistics.SceneHits);
    }
    [Fact] public void MovedObjectReusesItsLocalPicture()
    {
        var d = Scene(); using var r = new SlideRenderer { EnableSceneCache = false }; Pixels(r, d); var before = r.CacheStatistics;
        var slide = d.Slides[0]; slide = slide with { Shapes = slide.Shapes.SetItem(0, slide.Shapes[0] with { Bounds = slide.Shapes[0].Bounds with { X = 30 } }) };
        Pixels(r, d, slide); Assert.Equal(before.Misses, r.CacheStatistics.Misses); Assert.True(r.CacheStatistics.Hits > before.Hits);
        using var plain = new SlideRenderer { EnablePictureCache = false }; Assert.Equal(Pixels(plain, d, slide), Pixels(r, d, slide));
    }
    [Fact] public void TextEditInvalidatesOnlyOnePicture()
    {
        var d = Scene(); using var r = new SlideRenderer(); Pixels(r, d); long misses = r.CacheStatistics.Misses;
        var slide = d.Slides[0]; slide = slide with { Shapes = slide.Shapes.SetItem(0, slide.Shapes[0] with { Text = "Updated mixed text" }) };
        Pixels(r, d, slide); Assert.Equal(misses + 1, r.CacheStatistics.Misses);
    }
    [Fact] public void PictureCountAndBudgetAreBoundedAndClearable()
    {
        using var r = new SlideRenderer { MaximumCachedPictures = 2, EnableSceneCache = false }; var d = Scene(); Pixels(r, d); Assert.InRange(r.CacheStatistics.Pictures, 0, 2);
        r.PictureCacheBudget = 1; Pixels(r, d); Assert.Equal(0, r.CacheStatistics.Pictures); Assert.Equal(0, r.CacheStatistics.ApproximateBytes);
        r.Dispose(); r.Dispose(); r.PictureCacheBudget = 16000000; Pixels(r, d); Assert.True(r.CacheStatistics.Pictures > 0); r.ClearRenderCache(); Assert.Equal(0, r.CacheStatistics.Pictures);
    }
    [Fact] public void SharedCanvasMatrixAndPixelsOutsideClipArePreserved()
    {
        using var r = new SlideRenderer(); var d = Scene(); using var surface = SKSurface.Create(new SKImageInfo(800, 600));
        var c = surface.Canvas; c.Clear(SKColors.Lime); c.Translate(30, 40); c.Scale(.5f); var matrix = c.TotalMatrix; var clip = c.LocalClipBounds; int saves = c.SaveCount;
        r.Render(c, d, d.Slides[0]); r.Render(c, d, d.Slides[0]); Assert.Equal(matrix, c.TotalMatrix); Assert.Equal(clip, c.LocalClipBounds); Assert.Equal(saves, c.SaveCount);
        using var bitmap = SKBitmap.FromImage(surface.Snapshot()); Assert.Equal(SKColors.Lime, bitmap.GetPixel(5, 5)); Assert.Equal(SKColors.Lime, bitmap.GetPixel(790, 590));
    }
    [Fact] public void TableAutoFitAccountsForMergedMultilineContent()
    {
        using var r = new SlideRenderer(); var table = TableModel.Create(3, 3); table = TableModel.Merge(table, new(0, 0, 2, 2)); table = TableModel.SetText(table, 0, 0, string.Join(" ", Enumerable.Repeat("wrapped text", 30)));
        var result = r.AutoFitTableRows(table, 360); var layout = new TableLayout(result.Table, new(0, 0, 360, result.Height));
        foreach (var cell in result.Table.Cells)
        { var b = layout.Bounds(cell); float required = r.MeasureRichTextHeight(TableModel.TextShape(result.Table, cell), Math.Max(1, b.Width - cell.MarginLeft - cell.MarginRight)) + cell.MarginTop + cell.MarginBottom; Assert.True(b.Height + .01f >= required); }
        Assert.Same(table.Cells[0], result.Table.Cells[0]);
        var d = new PresentationDocument { Slides = [new Slide { Shapes = [TableModel.Apply(new SlideShape { Kind = ShapeKind.Table, Bounds = new(10, 10, 360, result.Height) }, result.Table)] }] };
        var imported = PptxCodec.Import(PptxCodec.Export(d).Data).Document;
        Assert.Equal(result.Table.RowCount, imported.Slides[0].Shapes[0].Table!.RowCount);
    }
    [Theory] [InlineData(float.NaN)] [InlineData(0)] [InlineData(float.PositiveInfinity)]
    public void AutoFitRejectsInvalidWidth(float width) { using var r = new SlideRenderer(); Assert.Throws<ArgumentOutOfRangeException>(() => r.AutoFitTableRows(TableModel.Create(), width)); }
    [Fact] public void TableAutoFitRespondsToWidthAndFontSize()
    {
        using var r = new SlideRenderer(); var table = TableModel.SetText(TableModel.Create(2, 1), 0, 0, string.Join(" ", Enumerable.Repeat("wrapping", 25)));
        Assert.True(r.AutoFitTableRows(table, 180).Height > r.AutoFitTableRows(table, 600).Height);
        var larger = table with { TextStyle = table.TextStyle with { FontSize = 40 } }; Assert.True(r.AutoFitTableRows(larger, 600).Height > r.AutoFitTableRows(table, 600).Height);
    }
}

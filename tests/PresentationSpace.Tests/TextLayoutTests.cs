using System.Collections.Immutable;
using HarfBuzzSharp;
using PresentationSpace.Core;
using PresentationSpace.Rendering.Skia;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using Xunit;
using HBBuffer = HarfBuzzSharp.Buffer;

namespace PresentationSpace.Tests;

public sealed class TextLayoutTests
{
    private sealed class Resolver(SKTypeface face) : IVersionedTypefaceResolver
    {
        public long Version { get; set; }
        public SKTypeface? Resolve(TextStyle style) => face;
    }
    private static SlideShape Text(string value, float size = 24, float width = 320, float height = 200) => SlideFactory.Text(value, 10, 10, width, height, size);
    private static byte[] Pixels(Action<SKCanvas> draw)
    {
        using var surface = SKSurface.Create(new SKImageInfo(640, 400)); surface.Canvas.Clear(SKColors.White);
        draw(surface.Canvas); using var snapshot = surface.Snapshot(); using var data = snapshot.Encode(SKEncodedImageFormat.Png, 100); return data.ToArray();
    }
    [Theory]
    [InlineData("alpha\r\nbeta", 2)] [InlineData("alpha\rbeta", 2)] [InlineData("alpha\nbeta", 2)]
    [InlineData("alpha\vbeta", 2)] [InlineData("alpha\u2028beta", 2)] [InlineData("alpha\u2029beta", 2)]
    [InlineData("\r\n\r\n", 3)] [InlineData("", 1)] [InlineData("a\n", 2)]
    public void HardAndSoftBreaksUseConsistentLineMetrics(string value, int count)
    {
        using var renderer = new SlideRenderer(); var shape = Text(value);
        var layout = renderer.LayoutRichText(shape, 320);
        Assert.Equal(count, layout.Lines.Length);
        Assert.InRange(Math.Abs(layout.Lines.Sum(l => l.Height) - layout.Height), 0, .001);
        Assert.Equal(layout.Height + 6, renderer.MeasureRichTextHeight(shape, 326, 3));
    }
    [Theory]
    [InlineData("AVATAR")] [InlineData("office")] [InlineData("a\u0301")] [InlineData("مرحبا")]
    public void WidthAndGlyphCountMatchDirectUtf16HarfBuzz(string value)
    {
        using var face = SKTypeface.FromFamilyName("Arial"); var resolver = new Resolver(face);
        using var engine = new TextLayoutEngine { TypefaceResolver = resolver };
        using var font = new SKFont(face, 32); using var shaper = new SKShaper(face);
        using var buffer = new HBBuffer(); buffer.AddUtf16(value); buffer.GuessSegmentProperties();
        var expected = shaper.Shape(buffer, font);
        var actual = engine.Measure(value, new() { FontSize = 32 }, 2000);
        Assert.Equal(expected.Codepoints.Length, actual.GlyphCount);
        Assert.InRange(Math.Abs(expected.Width - actual.Width), 0, .001);
    }
    [Fact] public void PaintOnlyRangesKeepKerningAndLigatureAdvances()
    {
        var plain = Text("AVATARoffice");
        var painted = RichText.Format(plain, 1, 4, style => style with { Color = "#C05030", Underline = true });
        using var renderer = new SlideRenderer();
        var a = renderer.LayoutRichText(plain, 2000); var b = renderer.LayoutRichText(painted, 2000);
        Assert.Equal(a.Width, b.Width); Assert.Equal(a.GlyphCount, b.GlyphCount); Assert.Equal(a.Height, b.Height);
    }
    [Fact] public void RedundantMixedRangesRenderExactlyLikePlainText()
    {
        var plain = Text("Repeated   spaces\tand words\nWrapped content with a long URL-like fragment.", width: 250);
        var ranged = plain with { TextRanges = [new(0, plain.Text.Length, plain.TextStyle)] };
        using var renderer = new SlideRenderer();
        Assert.Equal(Pixels(c => renderer.DrawText(c, plain.Text, plain.TextStyle, plain.Bounds)), Pixels(c => renderer.DrawRichText(c, ranged)));
        Assert.Equal(renderer.MeasureRichTextHeight(plain, 250), renderer.MeasureRichTextHeight(ranged, 250));
    }
    [Fact] public void RepeatedSpacesHaveRealWidthAndTrailingSpacesDoNotShiftAlignment()
    {
        using var engine = new TextLayoutEngine(); var style = new TextStyle { FontSize = 24, Alignment = ParagraphAlignment.Right };
        Assert.True(engine.Measure("a   b", style, 900).Width > engine.Measure("a b", style, 900).Width);
        Assert.Equal(engine.Measure("a b", style, 900).Width, engine.Measure("a b   ", style, 900).Width);
        Assert.Equal(Pixels(c => engine.Draw(c, "a b", style, new(0, 0, 300, 100))), Pixels(c => engine.Draw(c, "a b   ", style, new(0, 0, 300, 100))));
    }
    [Fact] public void TabsAdvanceToTheNextFourSpaceStop()
    {
        using var face = SKTypeface.FromFamilyName("Arial"); using var font = new SKFont(face, 24);
        using var engine = new TextLayoutEngine { TypefaceResolver = new Resolver(face) }; var style = new TextStyle { FontSize = 24 };
        float stop = font.MeasureText(" ") * 4;
        float a = engine.Measure("a", style, 900).Width, b = engine.Measure("b", style, 900).Width;
        Assert.InRange(Math.Abs((MathF.Floor(a / stop) + 1) * stop + b - engine.Measure("a\tb", style, 900).Width), 0, .001);
    }
    [Theory] [InlineData("10\u00a0kg")] [InlineData("10\u202fkg")] [InlineData("non\u2060breaking")]
    public void NonBreakingGroupsAreNeverEmergencySplit(string text)
    {
        using var engine = new TextLayoutEngine(); var layout = engine.Measure(text, new(), 1);
        Assert.Single(layout.Lines); Assert.Equal(text.Length, layout.Lines[0].Length);
    }
    [Theory]
    [InlineData("a\u0301b\u0301c\u0301")] [InlineData("👨‍👩‍👧‍👦🙂🇵🇱")]
    [InlineData("a-\u0301b")] [InlineData("a \u0301b")]
    public void EmergencyWrapBoundariesNeverDivideAnExtendedGrapheme(string value)
    {
        using var engine = new TextLayoutEngine(); var layout = engine.Measure(value, new(), 1);
        var boundaries = TextFlow.GraphemeBoundaries(value);
        Assert.All(layout.Lines, line => { Assert.Contains(line.Start, boundaries); Assert.Contains(line.Start + line.Length, boundaries); });
        Assert.Equal(value.Length, layout.Lines.Sum(l => l.Length));
    }
    [Fact] public void BulletContinuationKeepsIndentButNotAnotherMarker()
    {
        using var engine = new TextLayoutEngine(); var style = new TextStyle { Bullets = true, FontSize = 24 };
        var layout = engine.Measure("first second third fourth fifth\vsoft continuation\nnew item", style, 150);
        Assert.True(layout.Lines.Length > 3);
        Assert.Equal(2, layout.Lines.Count(l => l.ParagraphStart));
        Assert.All(layout.Lines, line => Assert.Equal(30, line.Indent));
        Assert.True(layout.Lines[0].ParagraphStart); Assert.True(layout.Lines[^1].Indent > 0);
    }
    [Fact] public void MixedSizeTextUsesOneBaselineWithoutDroppingTallRuns()
    {
        var shape = Text("tiny BIG tiny", 16) with { TextRanges = [new(5, 3, new() { FontSize = 70 })] };
        using var renderer = new SlideRenderer(); var layout = renderer.LayoutRichText(shape, 1000);
        Assert.Single(layout.Lines); Assert.True(layout.Height >= 70);
        Assert.True(layout.Lines[0].Baseline > 50); Assert.True(layout.Lines[0].Baseline < layout.Height);
    }
    [Fact] public void LayoutCacheSharesMeasurementAndPaintingAcrossPositions()
    {
        using var engine = new TextLayoutEngine(); var style = new TextStyle();
        var first = engine.Measure("Cached shaped glyphs", style, 300);
        var stats = engine.CacheStatistics;
        Pixels(c => engine.Draw(c, "Cached shaped glyphs", style, new(0, 0, 306, 120)));
        Pixels(c => engine.Draw(c, "Cached shaped glyphs", style, new(40, 40, 306, 190)));
        Assert.Equal(stats.Misses, engine.CacheStatistics.Misses); Assert.True(engine.CacheStatistics.Hits >= 2);
        Assert.Same(first, engine.Measure("Cached shaped glyphs", style, 300));
    }
    [Fact] public void WarmMeasurementHasNoManagedAllocations()
    {
        using var engine = new TextLayoutEngine(); var style = new TextStyle();
        for (int i = 0; i < 100; i++) engine.Measure("Warm measurement", style, 300);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) engine.Measure("Warm measurement", style, 300);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
    [Fact] public void CacheEvictionAndDisabledCacheRetainNoNativeEntries()
    {
        using var engine = new TextLayoutEngine { MaximumCachedLayouts = 2 }; var style = new TextStyle();
        for (int i = 0; i < 6; i++) engine.Measure("word" + i, style, 400);
        Assert.Equal(2, engine.CacheStatistics.Entries);
        engine.CacheBudget = 1; engine.Measure("temporary", style, 400);
        Assert.Equal(0, engine.CacheStatistics.Entries); Assert.Equal(0, engine.CacheStatistics.ApproximateBytes);
        engine.Clear(); engine.Dispose(); engine.Dispose();
        Assert.Throws<ObjectDisposedException>(() => engine.Measure("x", style, 400));
    }
    [Fact] public void ResolverVersionInvalidatesTextAndRetainedPictures()
    {
        using var face = SKTypeface.FromFamilyName("Arial"); var resolver = new Resolver(face);
        using var renderer = new SlideRenderer { TypefaceResolver = resolver };
        var shape = Text("versioned fonts"); var deck = new PresentationDocument { Slides = [new() { Shapes = [shape] }] };
        Pixels(c => renderer.Render(c, deck, deck.Slides[0])); var before = renderer.CacheStatistics;
        long textMisses = renderer.TextCacheStatistics.Misses; resolver.Version++;
        Pixels(c => renderer.Render(c, deck, deck.Slides[0]));
        Assert.True(renderer.CacheStatistics.Misses > before.Misses); Assert.True(renderer.TextCacheStatistics.Misses > textMisses);
        renderer.Dispose(); Assert.True(face.GlyphCount > 0); // Resolver owns the font, not either rendering cache.
    }
    [Fact] public void ClearingRendererRetainsItsReusableLifecycle()
    {
        using var renderer = new SlideRenderer(); var shape = Text("reusable renderer");
        renderer.LayoutRichText(shape, 300); renderer.Dispose();
        Assert.Equal(0, renderer.TextCacheStatistics.Entries);
        Assert.True(renderer.LayoutRichText(shape, 300).GlyphCount > 0);
    }
    [Fact] public void TextDrawRestoresCanvasTransformAndClip()
    {
        using var engine = new TextLayoutEngine(); using var surface = SKSurface.Create(new SKImageInfo(200, 150)); var canvas = surface.Canvas;
        canvas.Translate(4, 5); canvas.Scale(2); canvas.ClipRect(new(2, 2, 70, 50));
        var matrix = canvas.TotalMatrix; var clip = canvas.DeviceClipBounds; int saves = canvas.SaveCount;
        engine.Draw(canvas, "wrapped words", new(), new(10, 10, 40, 30));
        Assert.Equal(matrix, canvas.TotalMatrix); Assert.Equal(clip, canvas.DeviceClipBounds); Assert.Equal(saves, canvas.SaveCount);
    }
    [Theory] [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(0)] [InlineData(-1)] [InlineData(100001)]
    public void InvalidLayoutWidthFailsBeforeAllocation(float width)
    { using var engine = new TextLayoutEngine(); Assert.Throws<ArgumentOutOfRangeException>(() => engine.Measure("x", new(), width)); Assert.Equal(0, engine.CacheStatistics.Entries); }
    [Fact] public void InvalidInputAndSplitSurrogateStylesAreRejected()
    {
        using var engine = new TextLayoutEngine();
        Assert.Throws<InvalidDataException>(() => engine.Measure(new string('x', TextFlow.MaximumTextLength + 1), new(), 100));
        Assert.Throws<InvalidDataException>(() => engine.Measure("🙂", new(), 100, [new(1, 1, new())]));
        Assert.Throws<InvalidDataException>(() => engine.Measure("x", new() { FontSize = float.NaN }, 100));
    }
    [Fact] public void MixedDirectionIsDiagnosedInsteadOfClaimingBidiConformance()
    {
        using var engine = new TextLayoutEngine();
        Assert.True(engine.Measure("Hello مرحبا", new(), 900).HasMixedDirection);
        Assert.False(engine.Measure("مرحبا بالعالم", new(), 900).HasMixedDirection);
        Assert.True(engine.Measure("مرحبا بالعالم", new(), 900).Lines[0].RightToLeft);
    }
    [Fact] public void LongUnbrokenWordProgressesAndPreservesEverySourceIndex()
    {
        using var engine = new TextLayoutEngine { CacheBudget = 1 }; string text = new('x', 16000);
        var result = engine.Measure(text, new() { FontSize = 12 }, 200);
        Assert.True(result.Lines.Length > 100); Assert.Equal(text.Length, result.Lines.Sum(l => l.Length));
        for (int i = 1; i < result.Lines.Length; i++) Assert.Equal(result.Lines[i-1].Start + result.Lines[i-1].Length, result.Lines[i].Start);
        Assert.Equal(0, engine.CacheStatistics.Entries);
    }
}

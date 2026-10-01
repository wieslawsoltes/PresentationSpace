using System.Collections.Immutable;
using System.Globalization;
using PresentationSpace.Core;
using Xunit;

namespace PresentationSpace.Tests;

public sealed class GradientPrecisionTests
{
    [Theory]
    [InlineData(0.123456789f)]
    [InlineData(0.000000123456789f)]
    [InlineData(0.99999994f)]
    [InlineData(float.Epsilon)]
    [InlineData(0f)]
    [InlineData(1f)]
    public void AuthoringRoundTripPreservesExactPositionAndOpacity(float value)
    {
        ImmutableArray<GradientStop> stops = [new(0, "#102030", value), new(value, "#AABBCC", value), new(1, "#405060")];
        var restored = GradientModel.ParseStops(GradientModel.FormatStops(stops));
        Assert.True(stops.SequenceEqual(restored));
        Assert.True(GradientModel.Equivalent(new() { Stops = stops }, new() { Stops = restored }));
    }

    [Fact]
    public void SampledFloatBitPatternsRoundTripWithoutEditorDrift()
    {
        var random = new Random(41983);
        for (int iteration = 0; iteration < 4096; iteration++)
        {
            // Sample the full positive float exponent range below one, not just
            // uniformly distributed magnitudes which miss very small values.
            float offset = BitConverter.Int32BitsToSingle(random.Next(0x3f800001));
            float opacity = BitConverter.Int32BitsToSingle(random.Next(0x3f800001));
            ImmutableArray<GradientStop> stops = [new(0, "#102030", opacity), new(offset, "#AABBCC"), new(1, "#405060")];
            Assert.True(stops.SequenceEqual(GradientModel.ParseStops(GradientModel.FormatStops(stops))));
        }
    }

    [Theory]
    [InlineData("100.000001")]
    [InlineData("100.000001%")]
    [InlineData("-1e-100")]
    [InlineData("1e300")]
    public void OutOfRangePositionsAndOpacityAreRejectedBeforeFloatRounding(string value)
    {
        Assert.Throws<InvalidDataException>(() => GradientModel.ParseStops($"0 #000000\n{value} #FFFFFF"));
        Assert.Throws<InvalidDataException>(() => GradientModel.ParseStops($"0 #000000 {value}\n100 #FFFFFF"));
    }

    [Fact]
    public void OrdinaryPercentagesStayReadableAndCultureIndependent()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
            ImmutableArray<GradientStop> stops = [new(0, "#FF0000", .3f), new(.125f, "#00FF00", .7f), new(1, "#0000FF")];
            string text = GradientModel.FormatStops(stops);
            Assert.Equal("0 #FF0000 30\n12.5 #00FF00 70\n100 #0000FF 100", text);
            Assert.True(stops.SequenceEqual(GradientModel.ParseStops(text)));
        }
        finally { CultureInfo.CurrentCulture = original; }
    }
}

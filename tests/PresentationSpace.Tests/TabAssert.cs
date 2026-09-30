using System.Collections.Immutable;
using PresentationSpace.Core;
using Xunit;

namespace PresentationSpace.Tests;

internal static class TabAssert
{
    // ImmutableArray equality compares storage identity; interchange creates new
    // storage. Check each position and mode exactly, not backing-array identity.
    public static void Equal(ImmutableArray<TextTabStop> expected, ImmutableArray<TextTabStop> actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Position, actual[i].Position);
            Assert.Equal(expected[i].Alignment, actual[i].Alignment);
        }
    }
}

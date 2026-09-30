using System.Collections.Immutable;
using System.Xml.Linq;
using PresentationSpace.Core;

namespace PresentationSpace.Formats;

public static partial class PptxCodec
{
    private static ImmutableArray<TextTabStop> ReadTabStops(XElement? list, ImmutableArray<TextTabStop> fallback)
    {
        if (list is null) return fallback;
        var values = ImmutableArray.CreateBuilder<TextTabStop>();
        foreach (var node in list.Elements(A + "tab"))
        {
            if (values.Count == TextTabStops.MaximumCount) throw new InvalidDataException("At most 32 custom tab stops are supported.");
            if (node.Attribute("pos") is null) throw new InvalidDataException("A custom tab needs an explicit position.");
            float position = TextNumber(node, "pos", 0, Emu);
            var alignment = (string?)node.Attribute("algn") switch {
                null or "l" => TextTabAlignment.Left, "ctr" => TextTabAlignment.Center, "r" => TextTabAlignment.Right,
                "dec" => TextTabAlignment.Decimal, _ => throw new InvalidDataException("Unsupported tab alignment.") };
            values.Add(new(position, alignment));
        }
        var result = values.ToImmutable(); TextTabStops.Validate(result); return result;
    }
}

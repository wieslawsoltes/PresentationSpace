namespace PresentationSpace.Core;

/// <summary>Allocation-free geometry for virtualized filmstrip and sorter hosts. All units are logical pixels.</summary>
public readonly record struct VirtualSlideLayout(int Count, int Columns, double ItemWidth, double ItemHeight, double Gap = 12)
{
    public int Rows => (Count + Columns - 1) / Columns;
    public double Pitch => ItemHeight + Gap;
    public double ExtentHeight => Rows == 0 ? 0 : Rows * Pitch;
    public static VirtualSlideLayout Create(int count, double viewportWidth, double aspectRatio, bool grid)
    {
        if (count < 0 || count > 2000 || !double.IsFinite(viewportWidth) || !double.IsFinite(aspectRatio) || aspectRatio <= 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        double available = Math.Max(96, viewportWidth - 16);
        int columns = grid ? (int)Math.Clamp(Math.Floor((available + 12) / 288), 1, 2000) : 1;
        double width = grid ? Math.Min(276, (available - (columns - 1) * 12) / columns) : available;
        return new(count, columns, width, Math.Max(40, (width - 28) * aspectRatio) + 36);
    }
    public (int Start, int End) VisibleRange(double offset, double height, int overscanRows = 1)
    {
        if (!double.IsFinite(offset) || !double.IsFinite(height) || overscanRows < 0 || Columns <= 0 || Pitch <= 0)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (Count == 0 || height <= 0) return (0, 0);
        int first = (int)Math.Clamp(Math.Floor(Math.Max(0, offset) / Pitch), 0, Rows - 1);
        int last = (int)Math.Clamp(Math.Ceiling((Math.Max(0, offset) + height) / Pitch), first + 1, Rows);
        return (Math.Max(0, first - overscanRows) * Columns, Math.Min(Count, (last + Math.Min(Rows - last, overscanRows)) * Columns));
    }
    public double Top(int index) => index / Columns * Pitch;
    public double Left(int index) => 8 + index % Columns * (ItemWidth + Gap);
    public double RevealOffset(int index, double offset, double viewportHeight)
    {
        if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
        double top = Top(index), bottom = top + ItemHeight;
        return Math.Clamp(top < offset ? top : bottom > offset + viewportHeight ? bottom - viewportHeight : offset, 0, Math.Max(0, ExtentHeight - viewportHeight));
    }
}

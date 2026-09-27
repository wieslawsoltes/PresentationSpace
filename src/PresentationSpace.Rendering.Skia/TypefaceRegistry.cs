using PresentationSpace.Core;
using SkiaSharp;
namespace PresentationSpace.Rendering.Skia;

/// <summary>Resolves borrowed typefaces. The provider must outlive renderers that use it.</summary>
public interface ITypefaceResolver
{
    SKTypeface? Resolve(TextStyle style);
}

/// <summary>Host-supplied fonts, independent of Uno or installed operating-system fonts.
/// Register before rendering and dispose after the owning workspace has closed.</summary>
public sealed class TypefaceRegistry : ITypefaceResolver, IDisposable
{
    private readonly Dictionary<(string Family, bool Bold, bool Italic), SKTypeface> _faces = [];
    private readonly Dictionary<(bool Bold, bool Italic), SKTypeface> _fallback = [];
    private bool _disposed;

    public void Register(string family, byte[] data, bool bold = false, bool italic = false, bool fallback = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        if (data.Length > 16 * 1024 * 1024) throw new InvalidDataException("Font input exceeds 16 MB.");
        var key = (family.ToUpperInvariant(), bold, italic);
        if (_faces.ContainsKey(key)) throw new InvalidOperationException("This font face is already registered.");
        using var fontData = SKData.CreateCopy(data);
        var face = SKTypeface.FromData(fontData) ?? throw new InvalidDataException("Invalid font data.");
        _faces.Add(key, face);
        if (fallback) _fallback[(bold, italic)] = face;
    }

    public SKTypeface? Resolve(TextStyle style)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_faces.TryGetValue((style.FontFamily.ToUpperInvariant(), style.Bold, style.Italic), out var face)) return face;
        if (_fallback.TryGetValue((style.Bold, style.Italic), out face)) return face;
        return _fallback.GetValueOrDefault((false, false));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var face in _faces.Values) face.Dispose();
        _faces.Clear();
        _fallback.Clear();
    }
}

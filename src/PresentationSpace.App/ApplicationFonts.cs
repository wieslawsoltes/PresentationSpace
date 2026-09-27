using PresentationSpace.Rendering.Skia;
using Windows.Storage;
using Windows.Storage.Streams;
namespace PresentationSpace.App;

internal static class ApplicationFonts
{
    // The font bytes remain in the framework's licensed application assets, not in this repository.
    // WebAssembly has no operating-system font manager: provide real sans-serif faces explicitly.
    private static readonly TypefaceRegistry Registry = new();
    public static async Task InitializeAsync()
    {
#if __WASM__
        async Task Load(string fileName, bool bold, bool italic)
        {
            var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Uno.Fonts.OpenSans/Fonts/" + fileName));
            var buffer = await FileIO.ReadBufferAsync(file);
            var bytes = new byte[buffer.Length];
            using (var reader = DataReader.FromBuffer(buffer)) reader.ReadBytes(bytes);
            Registry.Register("Open Sans", bytes, bold, italic, fallback: true);
        }
        await Task.WhenAll(
            Load("OpenSans-Regular.ttf", false, false),
            Load("OpenSans-Bold.ttf", true, false),
            Load("OpenSans-Italic.ttf", false, true),
            Load("OpenSans-BoldItalic.ttf", true, true));
        SlideRenderer.DefaultTypefaceResolver = Registry;
#else
        await Task.CompletedTask;
#endif
    }
}

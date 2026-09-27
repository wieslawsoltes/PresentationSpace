using Microsoft.UI.Xaml;
using PresentationSpace.Core;
namespace PresentationSpace.Editor.Uno;

public sealed partial class PresentationEditor
{
    public int AllocatedFilmstripTiles => _filmstrip.AllocatedTileCount;
    public int AllocatedSorterTiles => _sorter.AllocatedTileCount;
    public int RealizedFilmstripTiles => _filmstrip.RealizedCount;
    public int RealizedSorterTiles => _sorter.RealizedCount;
    private void BuildPerformanceCommands()
    {
        _filmstrip.RealizationChanged += (_, _) => ViewChanged?.Invoke(this, EventArgs.Empty);
        _sorter.RealizationChanged += (_, _) => ViewChanged?.Invoke(this, EventArgs.Empty);
        Viewport.InteractionChanged += (_, _) => ViewChanged?.Invoke(this, EventArgs.Empty);
        _commands.Add(("Focus slide thumbnails", () => _filmstrip.Focus(FocusState.Programmatic)));
        _commands.Add(("Focus slide sorter", () => { ShowSorter(); _sorter.Focus(FocusState.Programmatic); }));
        _commands.Add(("Open large-deck sample", () => Run(async () =>
        {
            FlushEdits(); if (!await ConfirmDiscardAsync()) return;
            Session.Load(PerformanceSample.Create()); ShowNormal(); Viewport.Fit();
        })));
        _commands.Add(("Rendering statistics", () => Run(() =>
        {
            var stats = Viewport.RenderStatistics;
            return MessageAsync("Rendering statistics", $"Canvas: Uno SKCanvasElement\nViewport draws: {Viewport.DrawCount}\nLast CPU draw callback: {Viewport.LastDrawMilliseconds:0.###} ms\nPicture hits / misses: {stats.Hits} / {stats.Misses}\nRetained scene hits: {stats.SceneHits}\nApproximate retained picture bytes: {stats.ApproximateBytes:N0}\nFilmstrip / sorter tiles: {RealizedFilmstripTiles} / {RealizedSorterTiles}\n\nCPU callback timing is not GPU frame duration or FPS. Export remains raster/vector Skia work on the CPU.");
        })));
    }
}

namespace PresentationSpace.Editor.Uno;

public sealed partial class PresentationEditor
{
    private double _preferredFilmstripWidth = 220, _preferredFormatWidth = 296;
    private bool _panePreferencesInitialized;

    private void InitializePanePreferences()
    {
        if (_panePreferencesInitialized || _leftSplitter is null || _rightSplitter is null) return;
        _panePreferencesInitialized = true;
        TrackChrome("filmstrip-splitter", _leftSplitter);
        TrackChrome("format-splitter", _rightSplitter);
        // Store only explicit user resizes. Collapsing a pane or adapting it to
        // a narrow viewport must not overwrite the user's desktop widths.
        _leftSplitter.Resized += (_, width) => _preferredFilmstripWidth = width;
        _rightSplitter.Resized += (_, width) => _preferredFormatWidth = width;
    }
}

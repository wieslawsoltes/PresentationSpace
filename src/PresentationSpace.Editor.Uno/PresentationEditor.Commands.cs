using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace PresentationSpace.Editor.Uno;

public sealed partial class PresentationEditor
{
    private bool _commandQueued;
    public long CommandExecutionVersion { get; private set; }

    private void FocusCommandSearch()
    {
        if (_commandQueued) return;
        void Focus(AutoSuggestBox search)
        {
            search.ApplyTemplate(); search.Focus(FocusState.Programmatic);
            FindSearchTextBox(search)?.SelectAll();
        }
        if (_search.Visibility == Visibility.Visible) Focus(_search);
        else if (_moreQuick is not null)
        {
            _compactSearch.Width = Math.Clamp(ActualWidth - 64, 200, 440);
            _searchFlyout.ShowAt(_moreQuick);
            DispatcherQueue.TryEnqueue(() => Focus(_compactSearch));
        }
    }

    private static TextBox? FindSearchTextBox(DependencyObject parent)
    {
        if (parent is TextBox input) return input;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (FindSearchTextBox(VisualTreeHelper.GetChild(parent, i)) is { } child) return child;
        return null;
    }

    private void SubmitCommand(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (_commandQueued) return;
        string query = (args.ChosenSuggestion as string ?? args.QueryText).Trim();
        var match = _commands.FirstOrDefault(command => command.Title.Equals(query, StringComparison.OrdinalIgnoreCase));
        if (match.Execute is null) { Notice("Choose a command from the search suggestions."); return; }
        _commandQueued = true;
        sender.IsSuggestionListOpen = false;
        // Leave the AutoSuggestBox event before changing focus and clearing its native input.
        // Otherwise its final input synchronization can restore the just-submitted query.
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (ReferenceEquals(sender, _compactSearch)) _searchFlyout.Hide();
                Viewport.Focus(FocusState.Programmatic);
                if (FindSearchTextBox(sender) is { } input) input.Text = "";
                sender.Text = "";
                sender.ItemsSource = null;
                sender.IsSuggestionListOpen = false;
                // A command may focus a newly-created text editor; do not steal that focus afterward.
                match.Execute();
                CommandExecutionVersion++;
                ViewChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception error) { Notice(error.Message, true); }
            finally { _commandQueued = false; }
        })) _commandQueued = false;
    }
}

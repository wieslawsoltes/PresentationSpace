using System.Collections.Immutable;

namespace PresentationSpace.Core;

/// <summary>A single-use optimistic guard for delayed inspector/input edits. Does not own the session.</summary>
public sealed class SelectionEditSnapshot
{
    private readonly EditorSession _owner;
    private readonly Guid _slideId;
    private readonly ImmutableArray<SlideShape> _shapes;
    private bool _consumed;
    private SelectionEditSnapshot(EditorSession session)
    {
        _owner = session; _slideId = session.CurrentSlide.Id;
        _shapes = session.SelectedShapes.ToImmutableArray();
    }
    public static SelectionEditSnapshot Capture(EditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return new(session);
    }
    /// <summary>Rejects a changed selection, replaced snapshot, locked object or different session/slide.</summary>
    public bool IsCurrent(EditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_consumed || !ReferenceEquals(session, _owner) || session.CurrentSlide.Id != _slideId ||
            _shapes.IsEmpty || session.Selection.Count != _shapes.Length) return false;
        int index = 0;
        foreach (var shape in session.SelectedShapes)
        {
            if (index >= _shapes.Length || shape.Locked || !ReferenceEquals(shape, _shapes[index++])) return false;
        }
        return index == _shapes.Length;
    }
    /// <summary>Applies one ordinary undoable selection edit only while its captured targets are unchanged.</summary>
    public bool TryApply(EditorSession session, string label, Func<SlideShape, SlideShape> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (!IsCurrent(session)) return false;
        // Consume before notifying observers so reentrant focus callbacks cannot repeat the edit.
        _consumed = true;
        session.Apply(label, edit);
        return true;
    }
}

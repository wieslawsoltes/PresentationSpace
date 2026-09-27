using System.Collections.Immutable;

namespace PresentationSpace.Core;

public sealed class EditorChangedEventArgs(bool preview = false) : EventArgs
{
    public bool IsPreview { get; } = preview;
}

/// <summary>UI-independent immutable editing. A pointer gesture creates exactly one undo entry.</summary>
public sealed class EditorSession
{
    private sealed record State(PresentationDocument Document, int SlideIndex, ImmutableHashSet<Guid> Selection);
    private sealed record Entry(string Label, State Before, State After);
    private readonly List<Entry> _undo = [], _redo = [];
    private State? _gesture;
    private PresentationDocument _saved;
    private ImmutableArray<SlideShape> _clipboard = [];
    public PresentationDocument Document { get; private set; }
    public int SlideIndex { get; private set; }
    public ImmutableHashSet<Guid> Selection { get; private set; } = ImmutableHashSet<Guid>.Empty;
    public Slide CurrentSlide => Document.Slides[SlideIndex];
    public IEnumerable<SlideShape> SelectedShapes => CurrentSlide.Shapes.Where(s => Selection.Contains(s.Id));
    public SlideShape? PrimaryShape => SelectedShapes.LastOrDefault();
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string UndoLabel => CanUndo ? _undo[^1].Label : "";
    public string RedoLabel => CanRedo ? _redo[^1].Label : "";
    public bool IsDirty => !ReferenceEquals(Document, _saved);
    public bool SnapToGrid { get; set; } = true;
    public float GridSize { get; set; } = 8;
    public event EventHandler<EditorChangedEventArgs>? Changed;
    public EditorSession(PresentationDocument? document = null)
    {
        Document = document ?? new(); DocumentSerializer.Validate(Document); _saved = Document;
    }
    private State Capture() => new(Document, SlideIndex, Selection);
    private void Restore(State state)
    {
        Document = state.Document; SlideIndex = Math.Clamp(state.SlideIndex, 0, Document.Slides.Length - 1); Selection = state.Selection; Notify();
    }
    private void Notify(bool preview = false) => Changed?.Invoke(this, new(preview));
    private void Push(string label, State before)
    {
        if (ReferenceEquals(before.Document, Document)) { Notify(); return; }
        _undo.Add(new(label, before, Capture()));
        if (_undo.Count > 150) _undo.RemoveAt(0);
        _redo.Clear(); Notify();
    }
    public void MarkSaved() { _saved = Document; Notify(); }
    public void Load(PresentationDocument document)
    {
        DocumentSerializer.Validate(document); Document = document; _saved = document; SlideIndex = 0;
        Selection = ImmutableHashSet<Guid>.Empty; _undo.Clear(); _redo.Clear(); _gesture = null; _clipboard = []; Notify();
    }
    public void SelectSlide(int index)
    {
        CancelGesture(); SlideIndex = Math.Clamp(index, 0, Document.Slides.Length - 1); Selection = ImmutableHashSet<Guid>.Empty; Notify();
    }
    public void Select(Guid? id, bool additive = false)
    {
        if (!additive) Selection = ImmutableHashSet<Guid>.Empty;
        if (id is { } value)
        {
            var shape = CurrentSlide.Shapes.FirstOrDefault(s => s.Id == value);
            if (shape is not null)
            {
                IEnumerable<Guid> ids = shape.GroupId is { } g ? CurrentSlide.Shapes.Where(s => s.GroupId == g).Select(s => s.Id) : [value];
                bool remove = additive && Selection.Contains(value);
                Selection = remove ? Selection.Except(ids) : Selection.Union(ids);
            }
        }
        Notify(true);
    }
    public void SelectAll() { Selection = CurrentSlide.Shapes.Where(s => !s.Locked && !s.Hidden).Select(s => s.Id).ToImmutableHashSet(); Notify(true); }
    public void SelectRect(RectF bounds, bool additive = false)
    {
        var ids = CurrentSlide.Shapes.Where(s => !s.Locked && !s.Hidden && bounds.Intersects(Geometry.VisualBounds(s))).Select(s => s.Id);
        Selection = additive ? Selection.Union(ids) : ids.ToImmutableHashSet(); Notify(true);
    }
    public void EditDocument(string label, Func<PresentationDocument, PresentationDocument> edit)
    {
        CommitGesture(); var before = Capture(); Document = RichText.Reconcile(Document, edit(Document)); SlideIndex = Math.Clamp(SlideIndex, 0, Document.Slides.Length - 1);
        Selection = Selection.Intersect(CurrentSlide.Shapes.Select(s => s.Id)); Push(label, before);
    }
    public void EditSlide(string label, Func<Slide, Slide> edit) => EditDocument(label, d => d with { Slides = d.Slides.SetItem(SlideIndex, edit(d.Slides[SlideIndex])) });
    public void Apply(string label, Func<SlideShape, SlideShape> edit)
    {
        if (Selection.Count == 0) return;
        EditSlide(label, s => s with { Shapes = s.Shapes.Select(x => Selection.Contains(x.Id) && !x.Locked ? TableModel.Reconcile(x, ReconcileChartFill(x, edit(x))) : x).ToImmutableArray() });
    }
    private static SlideShape ReconcileChartFill(SlideShape before, SlideShape after)
    {
        if (before.Kind != ShapeKind.Chart || after.Kind != ShapeKind.Chart || before.Chart is not { } chart || !ReferenceEquals(chart, after.Chart) || before.Fill == after.Fill) return after;
        return ChartModel.Apply(after, chart with { Series = chart.Series.SetItem(0, chart.Series[0] with { Color = after.Fill }) });
    }
    public void BeginGesture() { if (_gesture is null) _gesture = Capture(); }
    public void PreviewShapes(Func<SlideShape, SlideShape> edit)
    {
        BeginGesture(); var original = _gesture!.Document.Slides[_gesture.SlideIndex];
        var slide = CurrentSlide with { Shapes = original.Shapes.Select(s => Selection.Contains(s.Id) && !s.Locked ? edit(s) : s).ToImmutableArray() };
        Document = Document with { Slides = Document.Slides.SetItem(SlideIndex, slide) }; Notify(true);
    }
    public void CommitGesture(string label = "Transform objects") { if (_gesture is not { } before) return; _gesture = null; Push(label, before); }
    public void CancelGesture() { if (_gesture is not { } before) return; _gesture = null; Restore(before); }
    public void Undo() { CancelGesture(); if (!CanUndo) return; var e = _undo[^1]; _undo.RemoveAt(_undo.Count - 1); _redo.Add(e); Restore(e.Before); }
    public void Redo() { CancelGesture(); if (!CanRedo) return; var e = _redo[^1]; _redo.RemoveAt(_redo.Count - 1); _undo.Add(e); Restore(e.After); }
    public void AddSlide(string layout = "Title and content")
    {
        CommitGesture(); var slide = SlideFactory.Create(layout, Document.Width, Document.Height);
        var before = Capture(); Document = Document with { Slides = Document.Slides.Insert(SlideIndex + 1, slide) }; SlideIndex++; Selection = ImmutableHashSet<Guid>.Empty; Push("New slide", before);
    }
    public void DuplicateSlide()
    {
        CommitGesture(); var source = CurrentSlide;
        var groups = source.Shapes.Where(s => s.GroupId != null).Select(s => s.GroupId!.Value).Distinct().ToDictionary(g => g, _ => Guid.NewGuid());
        var slide = source with { Id = Guid.NewGuid(), Name = source.Name + " copy", Shapes = source.Shapes.Select(s => s with { Id = Guid.NewGuid(), GroupId = s.GroupId is { } g ? groups[g] : null }).ToImmutableArray(), Comments = [] };
        var before = Capture(); Document = Document with { Slides = Document.Slides.Insert(++SlideIndex, slide) }; Selection = ImmutableHashSet<Guid>.Empty; Push("Duplicate slide", before);
    }
    public void DeleteSlide()
    {
        if (Document.Slides.Length == 1) { EditSlide("Clear slide", s => s with { Shapes = [], Notes = "" }); return; }
        EditDocument("Delete slide", d => d with { Slides = d.Slides.RemoveAt(SlideIndex) });
    }
    public void MoveSlide(int from, int to)
    {
        if (from < 0 || from >= Document.Slides.Length || to < 0 || to >= Document.Slides.Length || from == to) return;
        CommitGesture(); var before = Capture(); var slide = Document.Slides[from]; Document = Document with { Slides = Document.Slides.RemoveAt(from).Insert(to, slide) }; SlideIndex = to; Selection = ImmutableHashSet<Guid>.Empty; Push("Reorder slide", before);
    }
    public void Insert(SlideShape shape) { EditSlide("Insert " + shape.Kind, s => s with { Shapes = s.Shapes.Add(shape) }); Select(shape.Id); }
    public void Insert(ShapeKind kind)
    {
        if (kind == ShapeKind.Table)
        {
            var legacy = new SlideShape { Kind = ShapeKind.Table, Name = "Table " + (CurrentSlide.Shapes.Length + 1), Bounds = new(Document.Width * .18f, Document.Height * .25f, Document.Width * .64f, Document.Height * .5f), Cells = ["Category", "Value", "Change", "Product A", "125", "+12%", "Product B", "98", "+8%", "Product C", "156", "+24%"], TextStyle = new() { FontSize = 24 } };
            Insert(TableModel.Apply(legacy, TableModel.Get(legacy))); return;
        }
        if (kind == ShapeKind.Chart)
        {
            Insert(ChartModel.Apply(new SlideShape
            {
                Name = "Chart " + (CurrentSlide.Shapes.Length + 1),
                Bounds = new(Document.Width * .2f, Document.Height * .3f, Document.Width * .6f, Document.Height * .6f)
            }, new ChartSpec
            {
                Title = "Chart title", Categories = ["Q1", "Q2", "Q3", "Q4"],
                Series = [new() { Name = "Series 1", Values = [42, 68, 54, 89] }]
            }));
            return;
        }
        float x = Document.Width * 0.28f, y = Document.Height * 0.3f;
        Insert(new SlideShape { Kind = kind, Name = kind + " " + (CurrentSlide.Shapes.Length + 1), Bounds = new(x,y,kind == ShapeKind.Text ? 500 : 320,kind == ShapeKind.Text ? 90 : 190), Text = kind == ShapeKind.Text ? "Your text here" : "", Fill = kind == ShapeKind.Text || kind is ShapeKind.Line or ShapeKind.Arrow ? "#00000000" : "#D35230", Stroke = kind is ShapeKind.Line or ShapeKind.Arrow ? "#D35230" : "#00000000", StrokeWidth = kind is ShapeKind.Line or ShapeKind.Arrow ? 4 : 1.5f,
            Cells = kind == ShapeKind.Table ? ["Category","Value","Change","Product A","125","+12%","Product B","98","+8%","Product C","156","+24%"] : [], Values = kind == ShapeKind.Chart ? [42,68,54,89] : [], Labels = kind == ShapeKind.Chart ? ["Q1","Q2","Q3","Q4"] : [] });
    }
    public void InsertImage(byte[] data, string mime, string name)
    {
        if (data.Length > 20 * 1024 * 1024) throw new InvalidDataException("Images are limited to 20 MB.");
        string id = Guid.NewGuid().ToString("N");
        var shape = new SlideShape { Kind = ShapeKind.Image, Name = name, AssetId = id, Bounds = new(240,150,600,380), Fill = "#00000000" };
        EditDocument("Insert picture", d => d with { Assets = d.Assets.Add(id,new(id,mime,Convert.ToBase64String(data))), Slides = d.Slides.SetItem(SlideIndex,CurrentSlide with { Shapes = CurrentSlide.Shapes.Add(shape) }) }); Select(shape.Id);
    }
    public void DeleteSelection() => EditSlide("Delete objects", s => s with { Shapes = s.Shapes.Where(x => !Selection.Contains(x.Id) || x.Locked).ToImmutableArray() });
    public void Copy() => _clipboard = SelectedShapes.ToImmutableArray();
    public void Cut() { Copy(); DeleteSelection(); }
    public void Paste()
    {
        if (_clipboard.IsEmpty) return;
        var groups = _clipboard.Where(s => s.GroupId != null).Select(s => s.GroupId!.Value).Distinct().ToDictionary(g => g, _ => Guid.NewGuid());
        var pasted = _clipboard.Select(s => s with { Id = Guid.NewGuid(), GroupId = s.GroupId is { } g ? groups[g] : null, Bounds = s.Bounds with { X = s.Bounds.X + 24, Y = s.Bounds.Y + 24 } }).ToImmutableArray();
        EditSlide("Paste objects", s => s with { Shapes = s.Shapes.AddRange(pasted) }); Selection = pasted.Select(s => s.Id).ToImmutableHashSet(); Notify();
    }
    public void DuplicateSelection() { Copy(); Paste(); }
    public void Nudge(float dx, float dy) => Apply("Move objects", s => s with { Bounds = s.Bounds with { X = s.Bounds.X + dx, Y = s.Bounds.Y + dy } });
    public void Align(AlignKind kind)
    {
        var objects = SelectedShapes.ToArray(); if (objects.Length == 0) return;
        var box = objects.Length == 1 ? new RectF(0,0,Document.Width,Document.Height) : Geometry.Union(objects);
        Apply("Align objects", s => s with { Bounds = kind switch { AlignKind.Left => s.Bounds with { X = box.X }, AlignKind.Center => s.Bounds with { X = box.Center.X - s.Bounds.Width / 2 }, AlignKind.Right => s.Bounds with { X = box.Right - s.Bounds.Width }, AlignKind.Top => s.Bounds with { Y = box.Y }, AlignKind.Middle => s.Bounds with { Y = box.Center.Y - s.Bounds.Height / 2 }, _ => s.Bounds with { Y = box.Bottom - s.Bounds.Height } } });
    }
    public void Distribute(bool horizontal)
    {
        var shapes = SelectedShapes.OrderBy(s => horizontal ? s.Bounds.X : s.Bounds.Y).ToArray(); if (shapes.Length < 3) return;
        var box = Geometry.Union(shapes); float gap = ((horizontal ? box.Width : box.Height) - shapes.Sum(s => horizontal ? s.Bounds.Width : s.Bounds.Height)) / (shapes.Length - 1), p = horizontal ? box.X : box.Y;
        var positions = new Dictionary<Guid,float>(); foreach (var s in shapes) { positions[s.Id] = p; p += (horizontal ? s.Bounds.Width : s.Bounds.Height) + gap; }
        Apply("Distribute objects", s => s with { Bounds = horizontal ? s.Bounds with { X = positions[s.Id] } : s.Bounds with { Y = positions[s.Id] } });
    }
    public void Group() { if (Selection.Count < 2) return; var id = Guid.NewGuid(); Apply("Group objects", s => s with { GroupId = id }); }
    public void Ungroup() => Apply("Ungroup objects", s => s with { GroupId = null });
    public void BringToFront() => EditSlide("Bring to front", s => s with { Shapes = s.Shapes.Where(x => !Selection.Contains(x.Id)).Concat(s.Shapes.Where(x => Selection.Contains(x.Id))).ToImmutableArray() });
    public void SendToBack() => EditSlide("Send to back", s => s with { Shapes = s.Shapes.Where(x => Selection.Contains(x.Id)).Concat(s.Shapes.Where(x => !Selection.Contains(x.Id))).ToImmutableArray() });
    public void ReplaceText(string find, string replacement)
    {
        if (string.IsNullOrEmpty(find)) return;
        EditDocument("Replace text", d => d with { Slides = d.Slides.Select(s => s with { Shapes = s.Shapes.Select(x => x.Kind == ShapeKind.Table ? TableModel.ReplaceAll(x, find, replacement) : RichTextEditing.ReplaceAll(x, find, replacement)).ToImmutableArray() }).ToImmutableArray() });
    }
}

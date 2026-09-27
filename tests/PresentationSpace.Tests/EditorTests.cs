using PresentationSpace.Core;
using Xunit;
namespace PresentationSpace.Tests;

public class EditorTests
{
    [Fact] public void NativeRoundTrip() { var d = SlideFactory.Welcome(); var r = DocumentSerializer.Deserialize(DocumentSerializer.Serialize(d)); Assert.Equal(4,r.Slides.Length); Assert.Equal(d.Slides[0].Shapes[2].Text,r.Slides[0].Shapes[2].Text); }
    [Fact] public void UndoRedoPreservesDocumentIdentity() { var s = new EditorSession(); var before = s.Document; s.Insert(ShapeKind.Rectangle); var after = s.Document; s.Undo(); Assert.Same(before,s.Document); Assert.False(s.IsDirty); s.Redo(); Assert.Same(after,s.Document); }
    [Fact] public void GestureIsOneUndoEntry() { var s = new EditorSession(); s.Insert(ShapeKind.Rectangle); var before = s.Document; s.BeginGesture(); for(int i=0;i<100;i++) s.PreviewShapes(x => x with { Bounds = x.Bounds with { X = 100+i } }); s.CommitGesture(); Assert.Equal(199,s.PrimaryShape!.Bounds.X); s.Undo(); Assert.Same(before,s.Document); }
    [Fact] public void CancelGestureRestoresState() { var s = new EditorSession(); s.Insert(ShapeKind.Ellipse); var before = s.Document; s.PreviewShapes(x => x with { Rotation = 60 }); s.CancelGesture(); Assert.Same(before,s.Document); }
    [Fact] public void NewEditClearsRedo() { var s = new EditorSession(); s.Insert(ShapeKind.Rectangle); s.Undo(); s.AddSlide(); Assert.False(s.CanRedo); }
    [Fact] public void CannotDeleteLastSlide() { var s = new EditorSession(); s.DeleteSlide(); Assert.Single(s.Document.Slides); }
    [Fact] public void SlideReorderingIsUndoable() { var s = new EditorSession(SlideFactory.Welcome()); var id = s.Document.Slides[0].Id; s.MoveSlide(0,3); Assert.Equal(id,s.Document.Slides[3].Id); s.Undo(); Assert.Equal(id,s.Document.Slides[0].Id); }
    [Fact] public void DuplicateGetsFreshIdentifiers() { var s = new EditorSession(SlideFactory.Welcome()); s.DuplicateSlide(); DocumentSerializer.Validate(s.Document); Assert.NotEqual(s.Document.Slides[0].Shapes[0].Id,s.Document.Slides[1].Shapes[0].Id); }
    [Fact] public void GroupSelectionAndUngroup() { var s = new EditorSession(); s.Insert(ShapeKind.Rectangle); s.Insert(ShapeKind.Ellipse); s.SelectAll(); s.Group(); var ids = s.Selection.ToArray(); s.Select(ids[0]); Assert.Equal(2,s.Selection.Count); s.Ungroup(); Assert.All(s.SelectedShapes,x => Assert.Null(x.GroupId)); }
    [Fact] public void GroupCopyRemapsGroupIdentifiers() { var s = new EditorSession(); s.Insert(ShapeKind.Rectangle); s.Insert(ShapeKind.Ellipse); s.SelectAll(); s.Group(); var g = s.PrimaryShape!.GroupId; s.Copy(); s.Paste(); Assert.NotEqual(g,s.PrimaryShape!.GroupId); Assert.Equal(2,s.Selection.Count); DocumentSerializer.Validate(s.Document); }
    [Theory] [InlineData(AlignKind.Left,0)] [InlineData(AlignKind.Right,960)] public void SingleObjectAlignsToSlide(AlignKind kind,float expected) { var s = new EditorSession(); s.Insert(ShapeKind.Rectangle); s.Align(kind); Assert.Equal(expected,s.PrimaryShape!.Bounds.X); }
    [Fact] public void ReplaceIsCaseInsensitiveAndUndoable() { var s = new EditorSession(); s.Insert(SlideFactory.Text("Hello HELLO",10,10,400,100)); s.ReplaceText("hello","World"); Assert.Equal("World World",s.PrimaryShape!.Text); s.Undo(); Assert.Equal("Hello HELLO",s.PrimaryShape!.Text); }
    [Fact] public void RotatedHitTest() { var s = new SlideShape { Bounds = new(100,100,200,40), Rotation = 90 }; Assert.True(Geometry.HitTest(s,new(200,190))); Assert.False(Geometry.HitTest(s,new(290,120))); }
    [Fact] public void EllipseRejectsBoundingBoxCorner() { var s = new SlideShape { Kind = ShapeKind.Ellipse, Bounds = new(0,0,100,100) }; Assert.False(Geometry.HitTest(s,new(1,1))); Assert.True(Geometry.HitTest(s,new(50,50))); }
    [Fact] public void ResizeMaintainsAspect() { var r = Geometry.Resize(new(100,100,200,100),4,new(100,200),true); Assert.Equal(300,r.Width); Assert.Equal(150,r.Height); }
    [Fact] public void InvalidGeometryRejected() { var d = new PresentationDocument { Width = float.NaN }; Assert.Throws<InvalidDataException>(() => DocumentSerializer.Validate(d)); }
    [Fact] public void UnsupportedNativeVersionRejected() { Assert.Throws<InvalidDataException>(() => DocumentSerializer.Deserialize("{\"SchemaVersion\":99}")); }
    [Fact] public void SelectionIsClearedOnSlideChange() { var s = new EditorSession(SlideFactory.Welcome()); s.SelectAll(); s.SelectSlide(1); Assert.Empty(s.Selection); }
    [Fact] public void MarkSavedTracksUndo() { var s = new EditorSession(); s.Insert(ShapeKind.Text); s.MarkSaved(); s.Nudge(1,1); Assert.True(s.IsDirty); s.Undo(); Assert.False(s.IsDirty); }
}

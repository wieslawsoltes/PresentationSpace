using System.Collections.Immutable;
using PresentationSpace.Core;
using Xunit;

namespace PresentationSpace.Tests;

public sealed class SelectionEditSnapshotTests
{
    private static EditorSession Session()
    {
        var session = new EditorSession(new PresentationDocument { Slides = [new() { Shapes = [SlideFactory.Text("First",0,0,200,100), SlideFactory.Text("Second",250,0,200,100)] }] });
        session.Select(session.CurrentSlide.Shapes[0].Id); return session;
    }
    [Fact] public void CurrentDraftCommitsOnceAndSupportsUndo()
    {
        var session=Session(); var draft=SelectionEditSnapshot.Capture(session);
        Assert.True(draft.TryApply(session,"Input",s=>s with{AlternativeText="Description"}));
        Assert.Equal("Description",session.PrimaryShape!.AlternativeText);
        Assert.False(draft.TryApply(session,"Duplicate input",s=>s with{AlternativeText="Wrong"}));
        session.Undo();Assert.Equal("",session.PrimaryShape!.AlternativeText);
        Assert.False(draft.IsCurrent(session));
        session.Redo();Assert.Equal("Description",session.PrimaryShape!.AlternativeText);
    }
    [Fact] public void DelayedFocusLossCannotOverwriteNewlySelectedObject()
    {
        var session=Session();var draft=SelectionEditSnapshot.Capture(session);session.Select(session.CurrentSlide.Shapes[1].Id);
        Assert.False(draft.TryApply(session,"Old text",s=>s with{Text="stale"}));
        Assert.Equal("Second",session.PrimaryShape!.Text);Assert.False(session.CanUndo);
    }
    [Fact] public void NewSnapshotOfTheSameIdentityInvalidatesDraft()
    {
        var session=Session();var draft=SelectionEditSnapshot.Capture(session);session.Nudge(1,0);
        Assert.False(draft.TryApply(session,"Old bounds",s=>s with{Bounds=s.Bounds with{X=99}}));
        Assert.Equal(1,session.PrimaryShape!.Bounds.X);
    }
    [Fact] public void LockAndSecondarySelectionChangesInvalidateDraft()
    {
        var session=Session();session.SelectAll();var draft=SelectionEditSnapshot.Capture(session);
        session.EditSlide("External edit",slide=>slide with{Shapes=slide.Shapes.SetItem(0,slide.Shapes[0] with{Locked=true})});
        Assert.False(draft.IsCurrent(session));
        Assert.False(draft.TryApply(session,"Old text",s=>s with{Text="stale"}));
    }
    [Fact] public void UnrelatedSlideMetadataDoesNotInvalidateShapeDraft()
    {
        var session=Session();var draft=SelectionEditSnapshot.Capture(session);session.EditSlide("Notes",s=>s with{Notes="Speaker notes"});
        Assert.True(draft.IsCurrent(session));Assert.True(draft.TryApply(session,"Text",s=>s with{Text="Updated"}));
        Assert.Equal("Speaker notes",session.CurrentSlide.Notes);
    }
    [Fact] public void NewSelectionMembershipInvalidatesDraftEvenWithSamePrimary()
    {
        var session=Session();session.Select(session.CurrentSlide.Shapes[1].Id);var draft=SelectionEditSnapshot.Capture(session);session.SelectAll();
        Assert.False(draft.IsCurrent(session));
    }
    [Fact] public void DifferentSessionOrSlideIsRejected()
    {
        var session=Session();var draft=SelectionEditSnapshot.Capture(session);
        var other=new EditorSession(session.Document);other.Select(session.PrimaryShape!.Id);Assert.False(draft.IsCurrent(other));
        session.DuplicateSlide();Assert.False(draft.IsCurrent(session));
    }
    [Fact] public void ReentrantObserverCannotSubmitDraftTwice()
    {
        var session=Session();var draft=SelectionEditSnapshot.Capture(session);bool? repeated=null;
        session.Changed+=(_,_)=>repeated=draft.TryApply(session,"Again",s=>s with{Text="Wrong"});
        Assert.True(draft.TryApply(session,"Once",s=>s with{Text="Correct"}));Assert.False(repeated);Assert.Equal("Correct",session.PrimaryShape!.Text);
    }
    [Theory][InlineData("20 +1")][InlineData("20 -0")][InlineData("20 1")]
    public void AlignmentParserAcceptsNamesNotNumericEnumValues(string value)=>Assert.Throws<InvalidDataException>(()=>TextTabStops.Parse(value));
}

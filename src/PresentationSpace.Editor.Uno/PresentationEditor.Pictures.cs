using Microsoft.UI.Xaml;
using PresentationSpace.Core;
using PresentationSpace.Controls.Uno;
using PresentationSpace.Rendering.Skia;
using PresentationSpace.Ribbon.Uno;

namespace PresentationSpace.Editor.Uno;

public sealed partial class PresentationEditor
{
    private void EditPicture(string name, Func<PictureSpec,PictureSpec> change)
    {
        FlushEdits();
        if (!Session.SelectedShapes.Any(s => s.Kind == ShapeKind.Image && !s.Locked)) { Notice("Select an unlocked picture."); return; }
        Session.Apply(name, s => s.Kind == ShapeKind.Image ? PictureModel.Apply(s,change(PictureModel.Resolve(s))) : s);
        Viewport.Focus(FocusState.Programmatic);
    }
    private void OpenPictureEditor()
    {
        FlushEdits();
        if (Session.Selection.Count != 1 || Session.PrimaryShape is not {Kind:ShapeKind.Image,Locked:false}) { Notice("Select one unlocked picture to edit its crop and layout."); return; }
        var target = SelectionEditSnapshot.Capture(Session);
        ShowInspector(InspectorMode.Format);
        DispatcherQueue.TryEnqueue(() => { if (target.IsCurrent(Session)) _format.FocusPictureCrop(); });
    }
    private void OpenPictureSample()
    {
        FlushEdits(); Session.EditDocument("Insert picture layout sample", d =>
        {
            var sample = DocumentLayout.Resize(PictureLayoutSample.Create(),d.Width,d.Height);
            var next = d with {Slides=d.Slides.AddRange(sample.Slides),Assets=d.Assets.AddRange(sample.Assets)};
            DocumentSerializer.Validate(next); return next;
        });
        Session.SelectSlide(Session.Document.Slides.Length-1);
        Session.Select(Session.CurrentSlide.Shapes.First(s=>s.Kind==ShapeKind.Image).Id);
        ShowNormal(); Viewport.Fit(); Ribbon.SelectTab("Picture Format");
    }
    private void BuildPictureCommands()
    {
        _commands.Add(("Edit picture layout",OpenPictureEditor)); _commands.Add(("Open picture layout sample",OpenPictureSample));
        foreach (var fit in Enum.GetValues<PictureFit>()) _commands.Add(("Picture fit " + fit,()=>EditPicture("Picture fit",p=>p with {Fit=fit})));
        foreach (var mask in Enum.GetValues<PictureMask>()) _commands.Add(("Picture mask " + mask,()=>EditPicture("Picture shape",p=>p with {Mask=mask})));
        _commands.Add(("Picture flip horizontal",()=>EditPicture("Flip picture horizontally",p=>p with {FlipHorizontal=!p.FlipHorizontal})));
        _commands.Add(("Picture flip vertical",()=>EditPicture("Flip picture vertically",p=>p with {FlipVertical=!p.FlipVertical})));
        _commands.Add(("Picture reset crop",()=>EditPicture("Reset picture crop",p=>p with {Source=PictureInsets.Empty,Destination=PictureInsets.Empty,Fit=PictureFit.Contain})));
        foreach (int amount in new[] {0,25,50,75})
            _commands.Add(("Picture transparency " + amount,()=>EditPicture("Picture transparency",p=>p with {Opacity=1-amount/100f})));
    }
    private IEnumerable<RibbonGroup> PictureGroups()
    {
        yield return new("Picture",Cmd("picture-insert","Insert\nPicture","\uE91B",()=>Run(InsertPictureAsync)),Cmd("picture-layout","Crop &\nLayout","\uE7A8",OpenPictureEditor));
        yield return new("Framing",Cmd("picture-fit","Fit","\uE740",()=>EditPicture("Fit picture",p=>p with{Fit=PictureFit.Contain})),
            Cmd("picture-cover","Fill","\uE740",()=>EditPicture("Fill picture",p=>p with{Fit=PictureFit.Cover})),
            Cmd("picture-stretch","Stretch","\uE8A1",()=>EditPicture("Stretch picture",p=>p with{Fit=PictureFit.Stretch})),
            Cmd("picture-reset","Reset\nCrop","\uE7A7",()=>EditPicture("Reset picture crop",p=>p with{Source=PictureInsets.Empty,Destination=PictureInsets.Empty,Fit=PictureFit.Contain})));
        yield return new("Picture Style",Menu("picture-mask","Crop to\nShape","\uE91B",Enum.GetValues<PictureMask>().Select(m=>(m.ToString(),(Action)(()=>EditPicture("Picture shape",p=>p with{Mask=m}))))),
            Menu("picture-alpha","Transparency","\uE790",new[]{0,25,50,75}.Select(n=>(n+"%",(Action)(()=>EditPicture("Picture transparency",p=>p with{Opacity=1-n/100f}))))),
            Colors("picture-border","Border","\uE70F",color=>Session.Apply("Picture border",s=>s.Kind==ShapeKind.Image?s with{Stroke=color,StrokeWidth=Math.Max(1,s.StrokeWidth)}:s),true));
        yield return new("Arrange",Cmd("picture-flip-h","Flip\nHorizontal","\uE8AB",()=>EditPicture("Flip picture horizontally",p=>p with{FlipHorizontal=!p.FlipHorizontal})),
            Cmd("picture-flip-v","Flip\nVertical","\uE8AA",()=>EditPicture("Flip picture vertically",p=>p with{FlipVertical=!p.FlipVertical})),
            Menu("picture-arrange","Arrange","\uE8A1",ArrangeActions()));
    }
}

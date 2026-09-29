using PresentationSpace.Core;
using PresentationSpace.Rendering.Skia;

namespace PresentationSpace.Editor.Uno;

public sealed partial class PresentationEditor
{
    private void FitSelectedText(bool resizeShape)
    {
        FlushEdits();
        if (Session.Selection.Count == 0) { Notice("Select a text box or a text-bearing shape first."); return; }
        using var renderer = new SlideRenderer();
        try
        {
            Session.Apply(resizeShape ? "Resize shape to text" : "Shrink text to fit", shape =>
            {
                if (resizeShape) return renderer.FitShapeToText(shape);
                var fit = renderer.FitTextToShape(shape);
                if (!fit.Fits) throw new InvalidOperationException("The selected text cannot fit without going below 8 slide units. Enlarge its shape or shorten the text.");
                return fit.Shape;
            });
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or InvalidDataException) { Notice(error.Message, true); }
    }
    private void OpenTypographySample()
    {
        FlushEdits();
        // Insert rather than replace the current document; the entire operation is undoable.
        Session.EditDocument("Insert typography sample", d =>
        {
            var sample = DocumentLayout.Resize(TypographySample.Create(), d.Width, d.Height);
            var result = d with { Slides = d.Slides.AddRange(sample.Slides) };
            DocumentSerializer.Validate(result);
            return result;
        });
        Session.SelectSlide(Session.Document.Slides.Length - 2);
        ShowNormal(); Viewport.Fit();
    }
}

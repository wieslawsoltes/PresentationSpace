using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PresentationSpace.Core;
using PresentationSpace.Controls.Uno;
using PresentationSpace.Ribbon.Uno;
namespace PresentationSpace.App;
public sealed class MainPage : Page
{
    public MainPage()
    {
        var session=new EditorSession(SlideFactory.Welcome());var view=new SlideViewport{Session=session};
        var ribbon=new RibbonControl();ribbon.SetTabs([new("Home",()=>[new RibbonGroup("Slides",new RibbonCommandButton("new-slide","New slide","\uE710",()=>session.AddSlide())),new RibbonGroup("Insert",new RibbonCommandButton("insert-text","Text Box","\uE8D2",()=>session.Insert(ShapeKind.Text)),new RibbonCommandButton("insert-shape","Shapes","\uE91B",()=>session.Insert(ShapeKind.Rectangle)))])]);
        var grid=new Grid{RowDefinitions={new(){Height=GridLength.Auto},new(){Height=new GridLength(1,GridUnitType.Star)}}};grid.Children.Add(ribbon);Grid.SetRow(view,1);grid.Children.Add(view);Content=grid;
        Loaded+=(_,_)=>{
#if __WASM__
            global::Uno.Foundation.WebAssemblyRuntime.InvokeJS("document.documentElement.setAttribute('data-presentationspace','ready')");
#endif
        };
    }
}

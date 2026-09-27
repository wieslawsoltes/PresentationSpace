using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PresentationSpace.Core;
namespace PresentationSpace.Controls.Uno;

public abstract class SessionControl : UserControl
{
    private EditorSession? _session;
    private bool _subscribed;
    public EditorSession? Session
    {
        get=>_session;
        set { Detach(); _session=value; Attach(); OnSessionChanged(false); }
    }
    protected SessionControl() { Loaded+=(_,_)=>{Attach();OnSessionChanged(false);}; Unloaded+=(_,_)=>Detach(); }
    private void Attach(){if(!_subscribed&&_session is not null){_session.Changed+=Changed;_subscribed=true;}}
    private void Detach(){if(_subscribed&&_session is not null)_session.Changed-=Changed;_subscribed=false;}
    private void Changed(object? sender,EditorChangedEventArgs e)=>OnSessionChanged(e.IsPreview);
    protected abstract void OnSessionChanged(bool preview);
}

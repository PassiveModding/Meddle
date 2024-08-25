using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.UI;

namespace Meddle.Plugin.Services;

public class PluginState : IService
{
    public bool DrawLayout { get; set; }
    public event Action<ParsedInstance>? OnInstanceClick;
    public event Action<ParsedInstance>? OnLayoutTabInstanceHover;
    
    public void InvokeInstanceClick(ParsedInstance instance)
    {
        OnInstanceClick?.Invoke(instance);
    }
    
    public void InvokeInstanceHover(ParsedInstance instance)
    {
        OnLayoutTabInstanceHover?.Invoke(instance);
    }
}

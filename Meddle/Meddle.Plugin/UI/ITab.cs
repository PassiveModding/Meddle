using Meddle.Plugin.Models;

namespace Meddle.Plugin.UI;

public interface ITab : IDisposable
{
    public string Name { get; }
    public int Order { get; }
    //public bool DisplayTab { get; }
    public MenuType MenuType { get; }
    public void Draw();
}

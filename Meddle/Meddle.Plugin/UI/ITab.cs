namespace Meddle.Plugin.UI;

public interface ITab : IDisposable
{
    public string Name { get; }
    public void Draw();
}
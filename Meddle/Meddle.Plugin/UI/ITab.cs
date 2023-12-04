namespace Meddle.Plugin.UI;

public interface ITab : IDisposable
{
    public string Name { get; }
    public int Order { get; }

    public void Draw();
}

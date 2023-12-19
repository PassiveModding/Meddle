using SharpGLTF.IO;

namespace Meddle.Xande;

public class ExtraDataManager
{
    private Dictionary<string, object> ExtraData { get; } = new();

    public void AddShapeNames(IEnumerable<NewModelShape> shapes)
    {
        ExtraData.Add("targetNames", shapes.Select(s => s.Name).ToArray());
    }

    public JsonContent Serialize() => JsonContent.CreateFrom(ExtraData);
}

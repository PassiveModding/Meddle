using System.Text.Json;
using System.Text.Json.Nodes;
using SharpGLTF.IO;

namespace Meddle.Plugin.Xande;

public class ExtraDataManager
{
    private Dictionary<string, object> ExtraData { get; } = new();

    public void AddShapeNames(IEnumerable<NewModelShape> shapes)
    {
        ExtraData.Add("targetNames", shapes.Select(s => s.Name).ToArray());
    }

    public JsonNode Serialize() => JsonNode.Parse(JsonSerializer.Serialize( ExtraData ))!;
}

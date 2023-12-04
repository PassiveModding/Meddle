using Dalamud.Logging;
using Lumina.Models.Models;
using SharpGLTF.Scenes;

namespace Meddle.Xande;

public class ModelMeta
{
    public required string ModelPath { get; set; }
    public required string[] EnabledShapes { get; set; }
    public required string[] EnabledAttributes { get; set; }
    public required uint ShapesMask { get; set; }
    public required uint AttributesMask { get; set; }

    public void ApplyModifiers(InstanceBuilder builder, IEnumerable<Shape> shapeList, string[]? attributes)
    {
        builder.Content.UseMorphing().SetValue(shapeList.Select(s => EnabledShapes.Any(n => s.Name.Equals(n, StringComparison.Ordinal)) ? 1f : 0).ToArray());

        if (attributes != null)
        {
            if (!attributes.All(EnabledAttributes.Contains))
                builder.Remove();
        }
    }
}

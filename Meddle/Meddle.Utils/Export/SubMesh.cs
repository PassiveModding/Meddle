using Meddle.Utils.Files.Structs.Model;

namespace Meddle.Utils.Export;

public class SubMesh
{
    public uint IndexOffset { get; }
    public uint IndexCount { get; }
    public IReadOnlyList<string> Attributes { get; }

    public SubMesh(Submesh subMesh, uint meshIndexOffset, (string name, short id)[] modelAttributes)
    {
        IndexOffset = subMesh.IndexOffset - meshIndexOffset;
        IndexCount = subMesh.IndexCount;
        
        var attributes = new List<string>();
        foreach (var attribute in modelAttributes)
        {
            if ((subMesh.AttributeIndexMask & (1 << attribute.id)) != 0)
                attributes.Add(attribute.name);
        }
        
        Attributes = attributes;
    }
}

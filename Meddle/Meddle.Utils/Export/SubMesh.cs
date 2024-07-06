using Meddle.Utils.Files.Structs.Model;

namespace Meddle.Utils.Export;

public class SubMesh
{
    public uint IndexOffset { get; }
    public uint IndexCount { get; }
    public IReadOnlyList<string> Attributes { get; }

    public SubMesh(Submesh subMesh, uint meshIndexOffset)
    {
        IndexOffset = subMesh.IndexOffset - meshIndexOffset;
        IndexCount = subMesh.IndexCount;
        Attributes = new List<string>(); // TODO
    }
}

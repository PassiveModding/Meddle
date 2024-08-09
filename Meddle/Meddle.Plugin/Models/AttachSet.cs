using Meddle.Plugin.Models.Skeletons;
using SharpGLTF.Transforms;

namespace Meddle.Plugin.Models;

public class AttachSet
{
    public AttachSet(
        string id, ParsedAttach attach, ParsedSkeleton ownerSkeleton, AffineTransform transform, string? ownerId)
    {
        Id = id;
        Attach = attach;
        OwnerSkeleton = ownerSkeleton;
        Transform = transform;
        OwnerId = ownerId;
    }

    public string Id { get; set; }
    public string? OwnerId { get; set; }
    public ParsedAttach Attach { get; set; }
    public ParsedSkeleton OwnerSkeleton { get; set; }
    public AffineTransform Transform { get; set; }
}

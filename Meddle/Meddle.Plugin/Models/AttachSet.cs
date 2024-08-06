using Meddle.Plugin.Skeleton;
using SharpGLTF.Transforms;

namespace Meddle.Plugin.Models;


public class AttachSet
{
    public AttachSet(string id, Attach attach, Skeleton.Skeleton ownerSkeleton, AffineTransform transform, string? ownerId)
    {
        Id = id;
        Attach = attach;
        OwnerSkeleton = ownerSkeleton;
        Transform = transform;
        OwnerId = ownerId;
    }
    
    public string Id { get; set; }
    public string? OwnerId { get; set; }
    public Attach Attach { get; set; }
    public Skeleton.Skeleton OwnerSkeleton { get; set; }
    public AffineTransform Transform { get; set; }
}

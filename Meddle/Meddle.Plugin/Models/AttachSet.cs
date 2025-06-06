using Meddle.Plugin.Models.Skeletons;
using SharpGLTF.Transforms;

namespace Meddle.Plugin.Models;

public class AttachSet
{
    public AttachSet(
        string id, string name, ParsedAttach attach, ParsedSkeleton skeleton, AffineTransform transform, string? ownerId)
    {
        Id = id;
        Name = name;
        Attach = attach;
        Skeleton = skeleton;
        Transform = transform;
        OwnerId = ownerId;
        if (attach.OwnerSkeleton != null)
        {
            AttachBoneName = attach.OwnerSkeleton?
               .PartialSkeletons[attach.PartialSkeletonIdx]
               .HkSkeleton?.BoneNames[(int)attach.BoneIdx];
        }
    }

    public string Id { get; set; }
    public string Name { get; set;  }
    public string? OwnerId { get; set; }
    public ParsedAttach Attach { get; set; }
    public ParsedSkeleton Skeleton { get; set; }
    public AffineTransform Transform { get; set; }
    public string? AttachBoneName { get; set; }
}

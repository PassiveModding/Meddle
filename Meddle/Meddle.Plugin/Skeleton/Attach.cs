using FFXIVClientStructs.Interop;
using Meddle.Utils.Skeletons;
using CSAttach = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Attach;

namespace Meddle.Plugin.Skeleton;

public unsafe class Attach
{
    public Attach(CSAttach attach)
    {
        // 0 => Root
        // 3 => Fashion Accessories
        // 4 => Weapon
        ExecuteType = attach.ExecuteType;
        AttachmentCount = attach.AttachmentCount;
        switch (ExecuteType)
        {
            case 0:
                // nothing to do here
                return;
            // 1/2 -> not sure, seem to be transformed to a root item on exec
            case 3:
            {
                if (attach.OwnerCharacter->Skeleton != null)
                    OwnerSkeleton = new Skeleton(attach.OwnerCharacter->Skeleton);
                TargetSkeleton = new Skeleton(attach.TargetSkeleton);
                AttachmentCount = attach.AttachmentCount;
                if (attach.AttachmentCount != 0)
                {
                    var transform = attach.SkeletonBoneAttachments[0];
                    OffsetTransform = new Transform(transform.ChildTransform);
                    
                    PartialSkeletonIdx = transform.SkeletonIdx;
                    // not really sure how correct this is just yet
                    if (OwnerSkeleton!.PartialSkeletons[PartialSkeletonIdx].BoneCount <= transform.BoneIdx)
                    {
                        BoneIdx = 0; // TODO: sub_14041DCA0
                    }
                    else
                    {
                        BoneIdx = transform.BoneIdx;
                    }
                }
                break;
            }
            case 4:
            {
                OwnerSkeleton = new Skeleton(attach.OwnerSkeleton);
                TargetSkeleton = new Skeleton(attach.TargetSkeleton);
                AttachmentCount = attach.AttachmentCount;
                if (attach.AttachmentCount != 0)
                {
                    var att = attach.SkeletonBoneAttachments[0];
                    OffsetTransform = new Transform(att.ChildTransform);

                    PartialSkeletonIdx = att.SkeletonIdx;
                    BoneIdx = att.BoneIdx;
                }
                break;
            }
            default:
            {
                throw new NotImplementedException($"Unsupported Execute Type: {ExecuteType}, please report this");
            }
        }
    }

    public int AttachmentCount { get; }
    public int ExecuteType { get; }
    public Skeleton? TargetSkeleton { get; }
    public Skeleton? OwnerSkeleton { get; }
    public Transform? OffsetTransform { get; }
    public byte PartialSkeletonIdx { get; }
    public ushort BoneIdx { get; }
}

using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;

namespace Meddle.Plugin.Models.Skeletons;

public unsafe class ParsedAttach
{
    public ParsedAttach()
    {
        ExecuteType = 0;
        AttachmentCount = 0;
    }
    
    public ParsedAttach(Attach attach)
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
            case 3:
            {
                if (attach.OwnerCharacter->Skeleton != null)
                    OwnerSkeleton = new ParsedSkeleton(attach.OwnerCharacter->Skeleton);
                TargetSkeleton = new ParsedSkeleton(attach.TargetSkeleton);
                AttachmentCount = attach.AttachmentCount;
                if (attach.AttachmentCount != 0)
                {
                    var transform = attach.SkeletonBoneAttachments[0];
                    OffsetTransform = new Transform(transform.ChildTransform);
                    PartialSkeletonIdx = transform.BoneIndexMask.PartialSkeletonIdx;

                    var ownerSkeleton = attach.OwnerCharacter->Skeleton;

                    Skeleton.Bone? foundBone = null;
                    var foundBoneIdx = 0;
                    for (var i = 0; i < ownerSkeleton->AttachBoneCount; i++)
                    {
                        var bone = ownerSkeleton->AttachBonesSpan[i];
                        if (bone.BoneIndex == transform.BoneIndexMask.BoneIdx)
                        {
                            foundBone = bone;
                            foundBoneIdx = i;
                            break;
                        }
                    }

                    if (foundBone == null)
                    {
                        // should default but gonna throw for now
                        throw new InvalidOperationException("Bone not found");
                    }

                    var boneMask = ownerSkeleton->BoneMasksSpan[foundBoneIdx];
                    // some case for if boneMask == -1 but meh
                    PartialSkeletonIdx = boneMask.PartialSkeletonIdx;
                    BoneIdx = boneMask.BoneIdx;
                }

                break;
            }
            case 4:
            {
                OwnerSkeleton = new ParsedSkeleton(attach.OwnerSkeleton);
                TargetSkeleton = new ParsedSkeleton(attach.TargetSkeleton);
                AttachmentCount = attach.AttachmentCount;
                if (attach.AttachmentCount != 0)
                {
                    var att = attach.SkeletonBoneAttachments[0];
                    OffsetTransform = new Transform(att.ChildTransform);

                    PartialSkeletonIdx = att.BoneIndexMask.PartialSkeletonIdx;
                    BoneIdx = att.BoneIndexMask.BoneIdx;
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
    public ParsedSkeleton? TargetSkeleton { get; }
    public ParsedSkeleton? OwnerSkeleton { get; }
    public Transform? OffsetTransform { get; }
    public byte PartialSkeletonIdx { get; }
    public uint BoneIdx { get; }
}

using FFXIVClientStructs.Interop;
using Meddle.Utils.Skeletons;
using CSAttach = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Attach;
using CSSkeleton = FFXIVClientStructs.FFXIV.Client.Graphics.Render.Skeleton;

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
                    PartialSkeletonIdx = transform.BoneIndexMask.SkeletonIdx;

                    var ownerSkeleton = attach.OwnerCharacter->Skeleton;

                    CSSkeleton.Bone? foundBone = null;
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
                    PartialSkeletonIdx = boneMask.SkeletonIdx;
                    BoneIdx = boneMask.BoneIdx;
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

                    PartialSkeletonIdx = att.BoneIndexMask.SkeletonIdx;
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
    public Skeleton? TargetSkeleton { get; }
    public Skeleton? OwnerSkeleton { get; }
    public Transform? OffsetTransform { get; }
    public byte PartialSkeletonIdx { get; }
    public uint BoneIdx { get; }
}

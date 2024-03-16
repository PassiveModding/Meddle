using FFXIVClientStructs.Interop;
using PartialCSSkeleton = FFXIVClientStructs.FFXIV.Client.Graphics.Render.PartialSkeleton;

namespace Meddle.Plugin.Models;

public unsafe class PartialSkeleton
{
    public HkSkeleton? HkSkeleton { get; }
    public IReadOnlyList<SkeletonPose> Poses { get;}
    public int ConnectedBoneIndex { get; }

    public PartialSkeleton(Pointer<PartialCSSkeleton> partialSkeleton) : this(partialSkeleton.Value)
    {
    }
    
    public PartialSkeleton(PartialCSSkeleton* partialSkeleton)
    {
        if (partialSkeleton->SkeletonResourceHandle != null)
            HkSkeleton = new(partialSkeleton->SkeletonResourceHandle->HavokSkeleton);

        ConnectedBoneIndex = partialSkeleton->ConnectedBoneIndex;

        var poses = new List<SkeletonPose>();
        for (var i = 0; i < 4; ++i)
        {
            var pose = partialSkeleton->GetHavokPose(i);
            if (pose != null)
            {
                if (pose->Skeleton != partialSkeleton->SkeletonResourceHandle->HavokSkeleton)
                {
                    throw new ArgumentException($"Pose is not the same as the skeleton");
                }
                poses.Add(new(pose));
            }
        }
        
        Poses = poses;
    }
}

using FFXIVClientStructs.Interop;

namespace Meddle.Plugin.Models;

public unsafe class PartialSkeleton
{
    public HkSkeleton? HkSkeleton { get; set; }
    public List<SkeletonPose> Poses { get; set; }
    public int ConnectedBoneIndex { get; set; }

    public PartialSkeleton(Pointer<FFXIVClientStructs.FFXIV.Client.Graphics.Render.PartialSkeleton> partialSkeleton) : this(partialSkeleton.Value)
    {

    }

    public PartialSkeleton(FFXIVClientStructs.FFXIV.Client.Graphics.Render.PartialSkeleton* partialSkeleton)
    {
        if (partialSkeleton->SkeletonResourceHandle != null)
            HkSkeleton = new(partialSkeleton->SkeletonResourceHandle->HavokSkeleton);

        ConnectedBoneIndex = partialSkeleton->ConnectedBoneIndex;

        Poses = new();
        //return;
        for (var i = 0; i < 4; ++i)
        {
            var pose = partialSkeleton->GetHavokPose(i);
            if (pose != null)
            {
                if (pose->Skeleton != partialSkeleton->SkeletonResourceHandle->HavokSkeleton)
                {
                    throw new ArgumentException($"Pose is not the same as the skeleton");
                }
                Poses.Add(new(pose));
            }
        }
    }
}

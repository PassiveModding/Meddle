using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.Interop;
using Meddle.Plugin.Utils;

namespace Meddle.Plugin.Models.Skeletons;

public class ParsedPartialSkeleton
{
    public unsafe ParsedPartialSkeleton(Pointer<PartialSkeleton> partialSkeleton) :
        this(partialSkeleton.Value) { }

    public unsafe ParsedPartialSkeleton(PartialSkeleton* partialSkeleton)
    {
        if (partialSkeleton->SkeletonResourceHandle != null)
        {
            HkSkeleton = new ParsedHkaSkeleton(partialSkeleton->SkeletonResourceHandle->HavokSkeleton);
            HandlePath = partialSkeleton->SkeletonResourceHandle->FileName.ParseString();
        }

        BoneCount = StructExtensions.GetBoneCount(partialSkeleton);
        ConnectedBoneIndex = partialSkeleton->ConnectedBoneIndex;

        var poses = new List<ParsedHkaPose>();
        for (var i = 0; i < partialSkeleton->HavokPoses.Length; ++i)
        {
            var pose = partialSkeleton->GetHavokPose(i);
            if (pose != null)
            {
                if (pose->Skeleton != partialSkeleton->SkeletonResourceHandle->HavokSkeleton)
                {
                    throw new ArgumentException(
                        $"Pose is not the same as the skeleton {(nint)pose->Skeleton:X16} != {(nint)partialSkeleton->SkeletonResourceHandle->HavokSkeleton:X16}");
                }

                poses.Add(new ParsedHkaPose(pose));
            }
        }

        Poses = poses;
    }

    public string? HandlePath { get; }
    public ParsedHkaSkeleton? HkSkeleton { get; }
    public IReadOnlyList<ParsedHkaPose> Poses { get; }
    public int ConnectedBoneIndex { get; }
    public uint BoneCount { get; }
}

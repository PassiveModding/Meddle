using System.Text.Json.Serialization;
using FFXIVClientStructs.Havok.Animation.Rig;
using FFXIVClientStructs.Interop;
using Meddle.Utils.Skeletons;
using PartialCSSkeleton = FFXIVClientStructs.FFXIV.Client.Graphics.Render.PartialSkeleton;
using CSSkeleton = FFXIVClientStructs.FFXIV.Client.Graphics.Render.Skeleton;

namespace Meddle.Plugin.Skeleton;

public class Skeleton
{
    public unsafe Skeleton(Pointer<CSSkeleton> skeleton) : this(skeleton.Value) { }

    public unsafe Skeleton(CSSkeleton* skeleton)
    {
        Transform = new Transform(skeleton->Transform);
        var partialSkeletons = new List<PartialSkeleton>();
        for (var i = 0; i < skeleton->PartialSkeletonCount; ++i)
        {
            partialSkeletons.Add(new PartialSkeleton(&skeleton->PartialSkeletons[i]));
        }

        PartialSkeletons = partialSkeletons;
    }

    public Transform Transform { get; }
    public IReadOnlyList<PartialSkeleton> PartialSkeletons { get; }
}

public class PartialSkeleton
{
    public unsafe PartialSkeleton(Pointer<PartialCSSkeleton> partialSkeleton) :
        this(partialSkeleton.Value) { }

    public unsafe PartialSkeleton(PartialCSSkeleton* partialSkeleton)
    {
        if (partialSkeleton->SkeletonResourceHandle != null)
        {
            HkSkeleton = new HkSkeleton(partialSkeleton->SkeletonResourceHandle->HavokSkeleton);
            HandlePath = partialSkeleton->SkeletonResourceHandle->FileName.ToString();
        }

        ConnectedBoneIndex = partialSkeleton->ConnectedBoneIndex;

        var poses = new List<SkeletonPose>();
        for (var i = 0; i < partialSkeleton->HavokPoses.Length; ++i)
        {
            var pose = partialSkeleton->GetHavokPose(i);
            if (pose != null)
            {
                if (pose->Skeleton != partialSkeleton->SkeletonResourceHandle->HavokSkeleton)
                {
                    throw new ArgumentException("Pose is not the same as the skeleton");
                }

                poses.Add(new SkeletonPose(pose));
            }
        }

        Poses = poses;
    }

    public string? HandlePath { get; }
    public HkSkeleton? HkSkeleton { get; }
    public IReadOnlyList<SkeletonPose> Poses { get; }
    public int ConnectedBoneIndex { get; }
}

public class HkSkeleton
{
    public unsafe HkSkeleton(Pointer<hkaSkeleton> skeleton) : this(skeleton.Value) { }

    public unsafe HkSkeleton(hkaSkeleton* skeleton)
    {
        var boneNames = new List<string?>();
        var boneParents = new List<short>();
        var referencePose = new List<Transform>();

        for (var i = 0; i < skeleton->Bones.Length; ++i)
        {
            boneNames.Add(skeleton->Bones[i].Name.String);
            boneParents.Add(skeleton->ParentIndices[i]);
            referencePose.Add(new Transform(skeleton->ReferencePose[i]));
        }

        BoneNames = boneNames;
        BoneParents = boneParents;
        ReferencePose = referencePose;
    }

    public IReadOnlyList<string?> BoneNames { get; }
    public IReadOnlyList<short> BoneParents { get; }

    [JsonIgnore]
    public IReadOnlyList<Transform> ReferencePose { get; }
}

public class SkeletonPose
{
    public unsafe SkeletonPose(Pointer<hkaPose> pose) : this(pose.Value) { }

    public unsafe SkeletonPose(hkaPose* pose)
    {
        var transforms = new List<Transform>();

        var boneCount = pose->LocalPose.Length;
        for (var i = 0; i < boneCount; ++i)
        {
            var localSpace = pose->AccessBoneLocalSpace(i);
            transforms.Add(new Transform(*localSpace));
        }

        Pose = transforms;
    }

    [JsonIgnore]
    public IReadOnlyList<Transform> Pose { get; }
}

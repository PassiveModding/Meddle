using System.Text.Json.Serialization;
using FFXIVClientStructs.Havok.Animation.Rig;
using FFXIVClientStructs.Havok.Common.Base.Math.QsTransform;
using FFXIVClientStructs.Interop;
using Meddle.Utils.Export;
using PartialCSSkeleton = FFXIVClientStructs.FFXIV.Client.Graphics.Render.PartialSkeleton;
using CSSkeleton = FFXIVClientStructs.FFXIV.Client.Graphics.Render.Skeleton;

namespace Meddle.Utils.Skeletons;

public class Skeleton
{
    public Transform Transform { get; }
    public IReadOnlyList<PartialSkeleton> PartialSkeletons { get; }

    public unsafe Skeleton(Pointer<CSSkeleton> skeleton) : this(skeleton.Value) { }

    public unsafe Skeleton(CSSkeleton* skeleton)
    {
        Transform = new Transform(skeleton->Transform);
        var partialSkeletons = new List<PartialSkeleton>();
        for (var i = 0; i < skeleton->PartialSkeletonCount; ++i)
            partialSkeletons.Add(new PartialSkeleton(&skeleton->PartialSkeletons[i]));

        PartialSkeletons = partialSkeletons;
    }
}

public class PartialSkeleton
{
    public HkSkeleton? HkSkeleton { get; }
    public IReadOnlyList<SkeletonPose> Poses { get; }
    public int ConnectedBoneIndex { get; }

    public unsafe PartialSkeleton(Pointer<PartialCSSkeleton> partialSkeleton) :
        this(partialSkeleton.Value) { }

    public unsafe PartialSkeleton(PartialCSSkeleton* partialSkeleton)
    {
        if (partialSkeleton->SkeletonResourceHandle != null)
            HkSkeleton = new HkSkeleton(partialSkeleton->SkeletonResourceHandle->HavokSkeleton);

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

                poses.Add(new SkeletonPose(pose));
            }
        }

        Poses = poses;
    }
}

public class HkSkeleton
{
    public IReadOnlyList<string?> BoneNames { get; }
    public IReadOnlyList<short> BoneParents { get; }

    [JsonIgnore]
    public IReadOnlyList<Transform> ReferencePose { get; }

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
}

public class SkeletonPose
{
    [JsonIgnore]
    public IReadOnlyList<Transform> Pose { get; }

    public unsafe SkeletonPose(Pointer<hkaPose> pose) : this(pose.Value) { }

    public unsafe SkeletonPose(hkaPose* pose)
    {
        var transforms = new List<Transform>();
        var poseSpan = new Span<hkQsTransformf>(pose->LocalPose.Data, pose->LocalPose.Length);
        foreach (var transform in poseSpan)
            transforms.Add(new Transform(transform));
        
        /*var boneCount = pose->LocalPose.Length;
        for (var i = 0; i < boneCount; ++i)
        {
            var localSpace = pose->AccessBoneLocalSpace(i);
            transforms.Add(new Transform(*localSpace));
        }*/

        Pose = transforms;
    }
}

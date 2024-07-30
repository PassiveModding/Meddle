using System.Numerics;
using Meddle.Plugin.Models;
using Meddle.Plugin.Skeleton;
using SharpGLTF.Scenes;

namespace Meddle.Utils.Skeletons;

public static class SkeletonUtils
{
    public static List<BoneNodeBuilder> GetBoneMap(Skeleton skeleton, out BoneNodeBuilder? root)
    {
        List<BoneNodeBuilder> boneMap = new();
        root = null;

        foreach (var partial in skeleton.PartialSkeletons)
        {
            var hkSkeleton = partial.HkSkeleton;
            if (hkSkeleton == null)
                continue;

            var pose = partial.Poses.FirstOrDefault();

            var skeleBones = new BoneNodeBuilder[hkSkeleton.BoneNames.Count];
            for (var i = 0; i < hkSkeleton.BoneNames.Count; i++)
            {
                var name = hkSkeleton.BoneNames[i];
                if (string.IsNullOrEmpty(name))
                    continue;

                if (boneMap.FirstOrDefault(b => b.BoneName.Equals(name, StringComparison.OrdinalIgnoreCase)) is
                    { } dupeBone)
                {
                    skeleBones[i] = dupeBone;
                    continue;
                }

                if (partial.ConnectedBoneIndex == i)
                {
                    throw new InvalidOperationException(
                        $"Bone {name} on {i} is connected to a skeleton that should've already been declared");
                }

                var bone = new BoneNodeBuilder(name);
                if (pose != null)
                {
                    var transform = pose.Pose[i];
                    bone.UseScale().UseTrackBuilder("pose").WithPoint(0, transform.Scale);
                    bone.UseRotation().UseTrackBuilder("pose").WithPoint(0, transform.Rotation);
                    bone.UseTranslation().UseTrackBuilder("pose").WithPoint(0, transform.Translation);
                }

                bone.SetLocalTransform(hkSkeleton.ReferencePose[i].AffineTransform, false);

                var parentIdx = hkSkeleton.BoneParents[i];
                if (parentIdx != -1)
                    skeleBones[parentIdx].AddNode(bone);
                else
                {
                    if (root != null)
                        throw new InvalidOperationException("Multiple root bones found");
                    root = bone;
                }

                skeleBones[i] = bone;
                boneMap.Add(bone);
            }
        }

        if (!NodeBuilder.IsValidArmature(boneMap))
        {
            throw new InvalidOperationException(
                $"Joints are not valid, {string.Join(", ", boneMap.Select(x => x.Name))}");
        }

        return boneMap;
    }
    
    public static List<BoneNodeBuilder> GetAnimatedBoneMap(List<AnimationFrameData> animation, bool includePositionalData, out BoneNodeBuilder? root)
    {
        List<BoneNodeBuilder> boneMap = [];
        root = null;

        var firstFrame = animation.FirstOrDefault();
        if (firstFrame == null)
            throw new InvalidOperationException("No skeleton found in animation");
        
        for (var partialIdx = 0; partialIdx < firstFrame.Skeleton.PartialSkeletons.Count; partialIdx++)
        {
            var partial = firstFrame.Skeleton.PartialSkeletons[partialIdx];
            var hkSkeleton = partial.HkSkeleton;
            if (hkSkeleton == null)
                continue;

            var skeleBones = new BoneNodeBuilder[hkSkeleton.BoneNames.Count];
            for (var i = 0; i < hkSkeleton.BoneNames.Count; i++)
            {
                var name = hkSkeleton.BoneNames[i];
                if (string.IsNullOrEmpty(name))
                    continue;

                if (boneMap.FirstOrDefault(b => b.BoneName.Equals(name, StringComparison.OrdinalIgnoreCase)) is
                    { } dupeBone)
                {
                    skeleBones[i] = dupeBone;
                    continue;
                }

                if (partial.ConnectedBoneIndex == i)
                {
                    throw new InvalidOperationException(
                        $"Bone {name} on {i} is connected to a skeleton that should've already been declared");
                }

                var bone = new BoneNodeBuilder(name);
                foreach (var frame in animation)
                {
                    if (frame.Skeleton.PartialSkeletons.Count != firstFrame.Skeleton.PartialSkeletons.Count)
                        throw new InvalidOperationException("Partial skeleton count mismatch");
                    var framePartial = frame.Skeleton.PartialSkeletons[partialIdx];
                    if (framePartial.Poses.Count == 0)
                        throw new InvalidOperationException("No poses found in frame");
                    
                    var framePose = framePartial.Poses[0];

                    var offset = frame.Time - firstFrame.Time;

                    if (framePose.Pose.Count != hkSkeleton.BoneNames.Count)
                        throw new InvalidOperationException("Bone count mismatch");
                    
                    var transform = framePose.Pose[i];
                    bone.UseScale().UseTrackBuilder("pose").WithPoint((float)offset.TotalSeconds, transform.Scale);
                    bone.UseRotation().UseTrackBuilder("pose").WithPoint((float)offset.TotalSeconds, transform.Rotation);
                    bone.UseTranslation().UseTrackBuilder("pose").WithPoint((float)offset.TotalSeconds, transform.Translation);
                }

                bone.SetLocalTransform(hkSkeleton.ReferencePose[i].AffineTransform, false);

                var parentIdx = hkSkeleton.BoneParents[i];
                if (parentIdx != -1)
                    skeleBones[parentIdx].AddNode(bone);
                else
                {
                    if (root != null)
                        throw new InvalidOperationException("Multiple root bones found");
                    root = bone;
                }

                skeleBones[i] = bone;
                boneMap.Add(bone);
            }
        }

        if (includePositionalData && root != null)
        {
            // apply to root
            var firstTransform = animation[0].Transform;
            foreach (var (dateTime, _, transform) in animation)
            {
                var offset = dateTime - firstFrame.Time;
                var translation = transform.Translation - firstTransform.Translation;
                var rotationAdjust = transform.Rotation;
                var scale = transform.Scale;
                root.UseScale().UseTrackBuilder("pose").WithPoint((float)offset.TotalSeconds, scale);
                root.UseRotation().UseTrackBuilder("pose").WithPoint((float)offset.TotalSeconds, rotationAdjust);
                root.UseTranslation().UseTrackBuilder("pose").WithPoint((float)offset.TotalSeconds, translation);
            }
        }

        if (!NodeBuilder.IsValidArmature(boneMap))
        {
            throw new InvalidOperationException(
                $"Joints are not valid, {string.Join(", ", boneMap.Select(x => x.Name))}");
        }

        return boneMap;
    }
}

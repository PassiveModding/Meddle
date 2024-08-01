using System.Numerics;
using Meddle.Plugin.Models;
using Meddle.Plugin.Skeleton;
using SharpGLTF.Scenes;

namespace Meddle.Utils.Skeletons;

public static class SkeletonUtils
{
    public static List<BoneNodeBuilder> GetBoneMap(
        IReadOnlyList<PartialSkeleton> partialSkeletons, bool includePose, out BoneNodeBuilder? root)
    {
        List<BoneNodeBuilder> boneMap = new();
        root = null;

        for (var partialIdx = 0; partialIdx < partialSkeletons.Count; partialIdx++)
        {
            var partial = partialSkeletons[partialIdx];
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

                var bone = new BoneNodeBuilder(name)
                {
                    PartialSkeletonIndex = partialIdx,
                    BoneIndex = i
                };
                if (pose != null && includePose)
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

    public static List<BoneNodeBuilder> GetBoneMap(Skeleton skeleton, out BoneNodeBuilder? root)
    {
        return GetBoneMap(skeleton.PartialSkeletons, true, out root);
    }

    public static List<BoneNodeBuilder> GetAnimatedBoneMap(
        List<AnimationFrameData> animation, bool includePositionalData, out BoneNodeBuilder? root)
    {
        root = null;

        var firstFrame = animation.FirstOrDefault();
        if (firstFrame == null)
            throw new InvalidOperationException("No skeleton found in animation");

        var partialSkeletons = animation.SelectMany(x => x.Skeleton.PartialSkeletons)
                                        .Where(x => x.HandlePath != null)
                                        .DistinctBy(x => x.HandlePath)
                                        .ToArray();


        var boneMap = GetBoneMap(partialSkeletons, false, out root);

        var bonePoseMap = GetBonePoseMap(boneMap, animation);

        var startTime = bonePoseMap.Keys.Min();
        foreach (var (time, boneTransforms) in bonePoseMap)
        {
            var totalSeconds = (float)(time - startTime).TotalSeconds;
            foreach (var (bone, transform) in boneTransforms)
            {
                bone.UseScale().UseTrackBuilder("pose").WithPoint(totalSeconds, transform.Scale);
                bone.UseRotation().UseTrackBuilder("pose").WithPoint(totalSeconds, transform.Rotation);
                bone.UseTranslation().UseTrackBuilder("pose").WithPoint(totalSeconds, transform.Translation);
            }
        }

        if (root != null && includePositionalData)
        {
            var firstTranslation = firstFrame.Skeleton.Transform.Translation;
            foreach (var frame in animation)
            {
                var totalSeconds = (float)(frame.Time - startTime).TotalSeconds;
                var position = frame.Transform.Translation - firstTranslation;
                var rotation = frame.Transform.Rotation;
                var scale = frame.Transform.Scale;
                root.UseScale().UseTrackBuilder("pose").WithPoint(totalSeconds, scale);
                root.UseRotation().UseTrackBuilder("pose").WithPoint(totalSeconds, rotation);
                root.UseTranslation().UseTrackBuilder("pose").WithPoint(totalSeconds, position);
            }
        }

        // handle attach skeletons
        var distinctAttaches = new List<(AnimationFrameData AttachFrame, AttachedSkeleton DistinctAttach)>();
        foreach (var frame in animation)
        {
            // add first occurrence of each attach id
            foreach (var attach in frame.Attachments)
            {
                if (distinctAttaches.Any(x => x.Item2.AttachId == attach.AttachId))
                    continue;
                distinctAttaches.Add((frame, attach));
            }
        }

        //foreach (var distinctAttach in distinctAttaches)
        for (int i = 0; i < distinctAttaches.Count; i++)
        {
            var da = distinctAttaches[i];
            var attachBoneMap = GetBoneMap(da.DistinctAttach.Skeleton.PartialSkeletons, false, out var attachRoot);
            if (attachRoot == null)
                continue;
            
            var attachName = da.AttachFrame.Skeleton.PartialSkeletons[da.DistinctAttach.Attach.PartialSkeletonIdx]
                                           .HkSkeleton!.BoneNames[da.DistinctAttach.Attach.BoneIdx];
            var attachPointBone = boneMap.FirstOrDefault(x => x.BoneName.Equals(attachName, StringComparison.OrdinalIgnoreCase));
            if (attachPointBone == null)
                continue;
            
            attachPointBone.AddNode(attachRoot);
            
            attachRoot.SetSuffixRecursively(i);
            if (da.DistinctAttach.Attach.OffsetTransform is { } ct)
            {
                attachRoot.WithLocalScale(ct.Scale);
                attachRoot.WithLocalRotation(ct.Rotation);
                attachRoot.WithLocalTranslation(ct.Translation);
            }

            var attachBonePoseMap = GetAttachBonePoseMap(da.DistinctAttach, attachBoneMap, animation);
            foreach (var (time, boneTransforms) in attachBonePoseMap)
            {
                var totalSeconds = (float)(time - startTime).TotalSeconds;
                foreach (var (bone, transform) in boneTransforms)
                {
                    bone.UseScale().UseTrackBuilder("pose").WithPoint(totalSeconds, transform.Scale);
                    bone.UseRotation().UseTrackBuilder("pose").WithPoint(totalSeconds, transform.Rotation);
                    bone.UseTranslation().UseTrackBuilder("pose").WithPoint(totalSeconds, transform.Translation);
                }
            }
        }

        if (!NodeBuilder.IsValidArmature(boneMap))
        {
            throw new InvalidOperationException(
                $"Joints are not valid, {string.Join(", ", boneMap.Select(x => x.Name))}");
        }

        return boneMap;
    }

    private static Dictionary<DateTime, Dictionary<BoneNodeBuilder, Transform>> GetAttachBonePoseMap(
        AttachedSkeleton distinctAttach, List<BoneNodeBuilder> attachBoneMap, List<AnimationFrameData> animation)
    {
        var attachBonePoseMap = new Dictionary<DateTime, Dictionary<BoneNodeBuilder, Transform>>();
        foreach (var bone in attachBoneMap)
        {
            if (bone.PartialSkeletonIndex == null || bone.BoneIndex == null)
                continue;
                
            foreach (var frame in animation)
            {
                var attach = frame.Attachments.FirstOrDefault(x => x.AttachId == distinctAttach.AttachId);
                var pose = attach?.Skeleton.PartialSkeletons[bone.PartialSkeletonIndex.Value].Poses.FirstOrDefault();
                if (pose == null)
                    continue;

                var transform = pose.Pose[bone.BoneIndex.Value];
                if (!attachBonePoseMap.TryGetValue(frame.Time, out var boneTransforms))
                {
                    boneTransforms = new Dictionary<BoneNodeBuilder, Transform>();
                    attachBonePoseMap.Add(frame.Time, boneTransforms);
                }

                boneTransforms.TryAdd(bone, transform);
            }
        }
        
        return attachBonePoseMap;
    }
    
    private static Dictionary<DateTime, Dictionary<BoneNodeBuilder, Transform>> GetBonePoseMap(List<BoneNodeBuilder> boneMap, List<AnimationFrameData> animation)
    {
        var bonePoseMap = new Dictionary<DateTime, Dictionary<BoneNodeBuilder, Transform>>();

        foreach (var bone in boneMap)
        {
            if (bone.PartialSkeletonIndex == null || bone.BoneIndex == null)
                continue;

            foreach (var frame in animation)
            {
                var pose = frame.Skeleton.PartialSkeletons[bone.PartialSkeletonIndex.Value].Poses.FirstOrDefault();
                if (pose == null)
                    continue;

                var transform = pose.Pose[bone.BoneIndex.Value];
                if (!bonePoseMap.TryGetValue(frame.Time, out var boneTransforms))
                {
                    boneTransforms = new Dictionary<BoneNodeBuilder, Transform>();
                    bonePoseMap.Add(frame.Time, boneTransforms);
                }

                boneTransforms.TryAdd(bone, transform);
            }
        }
        
        return bonePoseMap;
    }

}

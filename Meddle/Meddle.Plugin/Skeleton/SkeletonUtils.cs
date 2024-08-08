using System.Numerics;
using Meddle.Plugin.Models;
using Meddle.Plugin.Skeleton;
using SharpGLTF.Scenes;
using SharpGLTF.Transforms;

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
                    BoneIndex = i,
                    PartialSkeletonHandle = partial.HandlePath ?? throw new InvalidOperationException($"No handle path for {name} [{partialIdx},{i}]"),
                    PartialSkeletonIndex = partialIdx
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

    public static List<BoneNodeBuilder> GetBoneMap(Skeleton skeleton, bool includePose, out BoneNodeBuilder? root)
    {
        return GetBoneMap(skeleton.PartialSkeletons, includePose, out root);
    }

    /*public static List<BoneNodeBuilder> GetAnimatedBoneMap(
        List<(DateTime, FrameData)> animation, bool includePositionalData, out BoneNodeBuilder? root)
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
                                           .HkSkeleton!.BoneNames[(int)da.DistinctAttach.Attach.BoneIdx];
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
    
    private static Dictionary<DateTime, Dictionary<BoneNodeBuilder, Transform>> GetBonePoseMap(List<BoneNodeBuilder> boneMap, List<(DateTime time, FrameData data)> animation)
    {
        var bonePoseMap = new Dictionary<DateTime, Dictionary<BoneNodeBuilder, Transform>>();

        foreach (var bone in boneMap)
        {
            if (bone.PartialSkeletonIndex == null || bone.BoneIndex == null)
                continue;

            foreach (var frame in animation)
            {
                var pose = frame.data.Skeleton.PartialSkeletons[bone.PartialSkeletonIndex.Value].Poses.FirstOrDefault();
                if (pose == null)
                    continue;

                var transform = pose.Pose[bone.BoneIndex.Value];
                if (!bonePoseMap.TryGetValue(frame.time, out var boneTransforms))
                {
                    boneTransforms = new Dictionary<BoneNodeBuilder, Transform>();
                    bonePoseMap.Add(frame.time, boneTransforms);
                }

                boneTransforms.TryAdd(bone, transform);
            }
        }
        
        return bonePoseMap;
    }*/

    public static Dictionary<string, (List<BoneNodeBuilder> Bones, BoneNodeBuilder? Root, List<(DateTime Time, AttachSet Attach)> Timeline)> GetAnimatedBoneMap((DateTime Time, AttachSet[] Attaches)[] frames)
    {
        var attachDict = new Dictionary<string, (List<BoneNodeBuilder> Bones, BoneNodeBuilder? Root, List<(DateTime Time, AttachSet Attach)> Timeline)>();
        var attachTimelines = new Dictionary<string, List<(DateTime Time, AttachSet Attach)>>();
        foreach (var frame in frames)
        {
            foreach (var attach in frame.Attaches)
            {
                if (!attachTimelines.TryGetValue(attach.Id, out var timeline))
                {
                    timeline = new List<(DateTime Time, AttachSet Attach)>();
                    attachTimelines.Add(attach.Id, timeline);
                }

                timeline.Add((frame.Time, attach));
            }
        }
        
        var allTimes = frames.Select(x => x.Time).ToArray();
        
        var startTime = frames.Min(x => x.Time);
        foreach (var (attachId, timeline) in attachTimelines)
        {
            var firstAttach = timeline.First().Attach;
            if (!attachDict.TryGetValue(attachId, out var attachBoneMap))
            {
                attachBoneMap = ([], null, timeline);
                attachDict.Add(attachId, attachBoneMap);
            }

            foreach (var time in allTimes)
            {
                var frame = timeline.FirstOrDefault(x => x.Time == time);
                var frameTime = TotalSeconds(time, startTime);
                if (frame != default)
                {
                    var newMap = GetBoneMap(frame.Attach.OwnerSkeleton, false, out var attachRoot);
                    if (attachRoot == null)
                        continue;

                    attachBoneMap.Root ??= attachRoot;

                    foreach (var attachBone in newMap)
                    {
                        var bone = attachBoneMap.Bones.FirstOrDefault(
                            x => x.BoneName.Equals(attachBone.BoneName, StringComparison.OrdinalIgnoreCase));
                        if (bone == null)
                        {
                            attachBoneMap.Bones.Add(attachBone);
                            bone = attachBone;
                        }

                        var partial = frame.Attach.OwnerSkeleton.PartialSkeletons[attachBone.PartialSkeletonIndex];
                        if (partial.Poses.Count == 0)
                            continue;

                        var transform = partial.Poses[0].Pose[bone.BoneIndex];
                        bone.UseScale().UseTrackBuilder("pose").WithPoint(frameTime, transform.Scale);
                        bone.UseRotation().UseTrackBuilder("pose").WithPoint(frameTime, transform.Rotation);
                        bone.UseTranslation().UseTrackBuilder("pose").WithPoint(frameTime, transform.Translation);
                    }

                    var firstTranslation = firstAttach.Transform.Translation;
                    attachRoot.UseScale().UseTrackBuilder("pose").WithPoint(frameTime, frame.Attach.Transform.Scale);
                    attachRoot.UseRotation().UseTrackBuilder("pose").WithPoint(frameTime, frame.Attach.Transform.Rotation);
                    attachRoot.UseTranslation().UseTrackBuilder("pose").WithPoint(frameTime, frame.Attach.Transform.Translation - firstTranslation);

                    attachDict[attachId] = attachBoneMap;
                }
            }

            foreach (var time in allTimes)
            {
                var frame = timeline.FirstOrDefault(x => x.Time == time);
                if (frame != default) continue;
                // set scaling to 0 when not present
                foreach (var bone in attachBoneMap.Bones)
                {
                    bone.UseScale().UseTrackBuilder("pose").WithPoint(TotalSeconds(time, startTime), Vector3.Zero);
                }
            }
        }
        
        return attachDict;
    }
    
    public static float TotalSeconds(DateTime time, DateTime startTime)
    {
        var value = (float)(time - startTime).TotalSeconds;
        // handle really close to 0 values
        if (value < 0.0001f)
            return 0;
        return value;
    }
}

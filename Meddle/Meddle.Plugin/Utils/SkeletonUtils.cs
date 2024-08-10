using System.Numerics;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Skeletons;
using Meddle.Utils;
using SharpGLTF.Scenes;

namespace Meddle.Plugin.Utils;

public static class SkeletonUtils
{
    public static List<BoneNodeBuilder> GetBoneMap(
        IReadOnlyList<ParsedPartialSkeleton> partialSkeletons, bool includePose, out BoneNodeBuilder? root)
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
                    PartialSkeletonHandle = partial.HandlePath ??
                                            throw new InvalidOperationException(
                                                $"No handle path for {name} [{partialIdx},{i}]"),
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

    public static List<BoneNodeBuilder> GetBoneMap(ParsedSkeleton skeleton, bool includePose, out BoneNodeBuilder? root)
    {
        return GetBoneMap(skeleton.PartialSkeletons, includePose, out root);
    }

    public static
        Dictionary<string, (List<BoneNodeBuilder> Bones, BoneNodeBuilder? Root, List<(DateTime Time, AttachSet Attach)>
            Timeline)> GetAnimatedBoneMap((DateTime Time, AttachSet[] Attaches)[] frames)
    {
        var attachDict =
            new Dictionary<string, (List<BoneNodeBuilder> Bones, BoneNodeBuilder? Root,
                List<(DateTime Time, AttachSet Attach)> Timeline)>();
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
                    attachRoot.UseRotation().UseTrackBuilder("pose")
                              .WithPoint(frameTime, frame.Attach.Transform.Rotation);
                    attachRoot.UseTranslation().UseTrackBuilder("pose")
                              .WithPoint(frameTime, frame.Attach.Transform.Translation - firstTranslation);

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

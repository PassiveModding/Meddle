using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Skeletons;
using Meddle.Utils;
using Microsoft.Extensions.Logging;
using SharpGLTF.Scenes;
using SharpGLTF.Transforms;

namespace Meddle.Plugin.Utils;

public static class SkeletonUtils
{
    public enum PoseMode
    {
        None,
        LocalScaleOnly,
        Local,
        // Model
    }
    
    public static (List<BoneNodeBuilder> List, BoneNodeBuilder Root)[] GetBoneMaps(
        ParsedSkeleton skeleton, PoseMode poseMode)
    {
        List<BoneNodeBuilder> boneMap = new();
        List<BoneNodeBuilder> rootList = new();

        for (var partialIdx = 0; partialIdx < skeleton.PartialSkeletons.Count; partialIdx++)
        {
            var partial = skeleton.PartialSkeletons[partialIdx];
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

                var bone = new BoneNodeBuilder(name)
                {
                    BoneIndex = i,
                    PartialSkeletonHandle = partial.HandlePath ??
                                            throw new InvalidOperationException(
                                                $"No handle path for {name} [{partialIdx},{i}]"),
                    PartialSkeletonIndex = partialIdx,
                };

                var boneTransform = hkSkeleton.ReferencePose[i].AffineTransform;
                bone.SetLocalTransform(boneTransform, false);

                var parentIdx = hkSkeleton.BoneParents[i];
                if (parentIdx != -1)
                {
                    skeleBones[parentIdx].AddNode(bone);
                }
                else
                {
                    rootList.Add(bone);
                }

                skeleBones[i] = bone;
                boneMap.Add(bone);
            }
        }

        // create separate lists based on each root
        var boneMapList = new List<(List<BoneNodeBuilder> List, BoneNodeBuilder Root)>();
        foreach (var root in rootList)
        {
            var bones = NodeBuilder.Flatten(root).Cast<BoneNodeBuilder>().ToList();
            if (!NodeBuilder.IsValidArmature(bones))
            {
                throw new InvalidOperationException($"Armature is invalid, {string.Join(", ", bones.Select(x => x.BoneName))}");
            }
            
            boneMapList.Add((bones, root));
        }

        var boneMaps = boneMapList.ToArray();

        // set pose scaling
        if (poseMode != PoseMode.None)
        {
            foreach (var map in boneMaps)
            {
                foreach (var bone in map.List)
                {
                    ApplyPose(bone, skeleton, poseMode, 0);
                }
            }
        }

        return boneMaps;
    }
    

    private static void ApplyPose(BoneNodeBuilder bone, 
                                  ParsedSkeleton skeleton, 
                                  PoseMode poseMode, 
                                  float time)
    {
        if (poseMode == PoseMode.None)
        {
            return; // no pose applied
        }
        
        var boneTransform = GetBoneTransform(skeleton, bone);
        if (boneTransform == null)
        {
            return;
        }
        
        AddBoneKeyframe(bone, poseMode, time, boneTransform.Value);
    }
    
    public static List<BoneNodeBuilder> GetBoneMap(ParsedSkeleton skeleton, PoseMode poseMode, out BoneNodeBuilder? root)
    {
        var maps = GetBoneMaps(skeleton, poseMode);
        if (maps.Length == 0)
        {
            throw new InvalidOperationException("No roots were found");
        }

        // only known instance of this thus far is the Air-Wheeler A9 mount
        // contains two roots, one for the mount and an n_pluslayer which contains an additional skeleton
        var rootMap = maps.FirstOrDefault(x => x.Root.BoneName.Equals("n_root", StringComparison.OrdinalIgnoreCase));
        if (rootMap != default)
        {
            root = rootMap.Root;
            return rootMap.List;
        }
        
        var map0 = maps[0];
        root = map0.Root;
        return map0.List;
    }

    public record AttachGrouping(List<BoneNodeBuilder> Bones, BoneNodeBuilder? Root, List<(DateTime Time, AttachSet Attach)> Timeline);
    public static Dictionary<string, AttachGrouping> GetAnimatedBoneMap((DateTime Time, AttachSet[] Attaches)[] frames)
    {
        var attachDict = new Dictionary<string, AttachGrouping>();
        var attachTimelines = new Dictionary<string, List<(DateTime Time, AttachSet Attach)>>();
        foreach (var frame in frames)
        {
            foreach (var attach in frame.Attaches)
            {
                var timelineName = $"{attach.Id}_{attach.Name}";
                if (!attachTimelines.TryGetValue(timelineName, out var timeline))
                {
                    timeline = [];
                    attachTimelines.Add(timelineName, timeline);
                }

                timeline.Add((frame.Time, attach));
            }
        }

        var startTime = frames.Min(x => x.Time);
        foreach (var (attachId, timeline) in attachTimelines)
        {
            ProcessTimeline(attachId, timeline, attachDict, startTime);
        }

        return attachDict;
    }

    private static void ProcessTimeline(string attachId, List<(DateTime Time, AttachSet Attach)> timeline, 
                                        Dictionary<string, AttachGrouping> attachDict,
                                        DateTime startTime)
    {
        if (!attachDict.TryGetValue(attachId, out var attachBoneMap))
        {
            attachBoneMap = new AttachGrouping([], null, timeline);
            attachDict.Add(attachId, attachBoneMap);
        }

        if (timeline.Count == 0)
        {
            return; // no frames for this attach
        }

        var firstAttach = timeline.First().Attach;
        foreach (var time in timeline.Select(x => x.Time).Distinct())
        {
            var frame = timeline.FirstOrDefault(x => x.Time == time);
            var nextFrame = timeline.FirstOrDefault(x => x.Time > time);
            var frameTime = TotalSeconds(frame.Time, startTime);

            // get the bone map for this attach
            var boneMap = GetBoneMap(frame.Attach.Skeleton, PoseMode.None, out var attachRoot);
            if (attachRoot == null)
                continue;

            if (attachBoneMap.Root == null)
            {
                attachBoneMap = attachBoneMap with { Root = attachRoot };
            }

            // for each bone in the bone map
            foreach (var attachBone in boneMap)
            {
                // get the transform for this bone
                var currentTransform = GetBoneTransform(frame.Attach.Skeleton, attachBone);
                if (currentTransform == null)
                    continue; // no transform for this bone

                // find the bone in the attachBoneMap
                var bone = attachBoneMap.Bones.FirstOrDefault(x => x.BoneName.Equals(attachBone.BoneName, StringComparison.OrdinalIgnoreCase));
                bool firstOccurrence = false;
                if (bone == null)
                {
                    attachBoneMap.Bones.Add(attachBone);
                    bone = attachBone;
                    firstOccurrence = true;
                }

                var nextFrameBone = nextFrame != default ? GetBoneTransform(nextFrame.Attach.Skeleton, attachBone) : null;
                if (!firstOccurrence && nextFrameBone != null && IsSameTransform(nextFrameBone.Value, currentTransform.Value))
                {
                    continue; // skip applying if next frame is identical
                }
                
                // apply the pose to the bone
                ApplyPose(bone, frame.Attach.Skeleton, PoseMode.Local, frameTime);
            }
            
            ApplyRootTransform(attachRoot, frame.Attach.Transform, firstAttach.Transform, frameTime);
            attachDict[attachId] = attachBoneMap;
        }
    }
    
    private static void AddBoneKeyframe(BoneNodeBuilder bone, PoseMode poseMode, float time, AffineTransform transform)
    {
        bone.UseScale().UseTrackBuilder("pose").WithPoint(time, transform.Scale);
        
        if (poseMode != PoseMode.LocalScaleOnly)
        {
            bone.UseRotation().UseTrackBuilder("pose").WithPoint(time, transform.Rotation);
            bone.UseTranslation().UseTrackBuilder("pose").WithPoint(time, transform.Translation);
        }
    }

    private static bool IsSameTransform(AffineTransform? current, AffineTransform? previous)
    {
        if (current == null || previous == null)
            return false;

        return current.Equals(previous);
    }
    
    private static void ApplyRootTransform(BoneNodeBuilder attachRoot, AffineTransform currentTransform, AffineTransform firstTransform, float frameTime)
    {
        var relativeTranslation = currentTransform.Translation - firstTransform.Translation;
        
        attachRoot.UseScale().UseTrackBuilder("root").WithPoint(frameTime, currentTransform.Scale);
        attachRoot.UseRotation().UseTrackBuilder("root").WithPoint(frameTime, currentTransform.Rotation);
        attachRoot.UseTranslation().UseTrackBuilder("root").WithPoint(frameTime, relativeTranslation);
    }
    
    private static AffineTransform? GetBoneTransform(ParsedSkeleton skeleton, BoneNodeBuilder bone)
    {
        var partial = skeleton.PartialSkeletons[bone.PartialSkeletonIndex];
        if (partial.Poses.Count == 0) return null;
            
        var pose = partial.Poses[0];
        var boneTransform = pose.Pose[bone.BoneIndex].AffineTransform;
        
        // Apply root scaling if this is a root bone
        if (bone.Parent is not BoneNodeBuilder)
        {
            var scale = boneTransform.Scale * skeleton.Transform.Scale;
            return new AffineTransform(scale, boneTransform.Rotation, boneTransform.Translation);
        }
        
        return boneTransform;
    }
    
    public static float TotalSeconds(DateTime time, DateTime startTime)
    {
        var seconds = (float)(time - startTime).TotalSeconds;
        return seconds < 0.0001f ? 0f : seconds;
    }
}

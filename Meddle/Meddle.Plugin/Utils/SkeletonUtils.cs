using System.ComponentModel;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Skeletons;
using Meddle.Plugin.UI;
using Meddle.Utils;
using SharpGLTF.Scenes;
using SharpGLTF.Transforms;

namespace Meddle.Plugin.Utils;

public static class SkeletonUtils
{
    public enum PoseMode
    {
        [Description("Reference Pose")]
        None,
        [Description("Reference Pose with Scale")]
        LocalScaleOnly,
        [Description("Pose")]
        Local
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
                    //ApplyPose(bone, skeleton, poseMode, 0);
                    var boneTransform = GetBoneTransform(skeleton, bone);
                    if (boneTransform == null)
                    {
                        continue;
                    }
    
                    AddBoneKeyframe(bone, poseMode, 0, boneTransform.Value);
                }
            }
        }

        return boneMaps;
    }
    

    private static void ApplyPose(
        BoneNodeBuilder bone,
        BoneNodeBuilder root,
        AttachSet attachSet,
        PoseMode poseMode,
        float time)
    {
        if (poseMode == PoseMode.None)
        {
            return; // no pose applied
        }
        
        var boneTransform = GetBoneTransform(attachSet.Skeleton, bone);
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
            root = null;
            return [];
            //throw new InvalidOperationException("No roots were found");
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
    public static Dictionary<string, AttachGrouping> GetAnimatedBoneMap((DateTime Time, AttachSet[] Attaches)[] frames, AnimationExportSettings settings)
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
            ProcessTimeline(attachId, timeline, attachDict, startTime, settings);
        }

        return attachDict;
    }

    private static void ProcessTimeline(
        string attachId,
        List<(DateTime Time, AttachSet Attach)> timeline,
        Dictionary<string, AttachGrouping> attachDict,
        DateTime startTime,
        AnimationExportSettings settings)
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

        // Track last transform and last frame time for each bone
        var lastTransforms = new Dictionary<string, (AffineTransform Transform, float Time, bool KeyframeSet)>();
        foreach (var time in timeline.Select(x => x.Time).Distinct())
        {
            var frame = timeline.FirstOrDefault(x => x.Time == time);
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
                if (bone == null)
                {
                    attachBoneMap.Bones.Add(attachBone);
                    bone = attachBone;
                }

                // Keyframe logic
                var boneName = attachBone.BoneName;
                if (!lastTransforms.TryGetValue(boneName, out var value) || IsLastFrameInTimeline(timeline, frame.Time))
                {
                    ApplyPose(bone, attachRoot, frame.Attach, PoseMode.Local, frameTime);
                    lastTransforms[boneName] = (currentTransform.Value, frameTime, true);
                }
                else
                {
                    var (lastTransform, lastTime, keyframeSet) = value;

                    if (IsSameTransform(currentTransform.Value, lastTransform))
                    {
                        // No change, just update last info
                        lastTransforms[boneName] = (lastTransform, frameTime, keyframeSet);
                        continue;
                    }
                    // Change detected
                    if (!keyframeSet)
                    {
                        // Add keyframe for last frame
                        ApplyPose(bone, attachRoot, frame.Attach, PoseMode.Local, lastTime);
                    }
                    // Add keyframe for current frame
                    ApplyPose(bone, attachRoot, frame.Attach, PoseMode.Local, frameTime);
                    lastTransforms[boneName] = (currentTransform.Value, frameTime, true);
                }
            }

            // var startPos = firstFrame.Attach.Transform.Translation;
            // var pos = frame.Attach.Transform.Translation;
            // var rot = frame.Attach.Transform.Rotation;
            // var scale = frame.Attach.Transform.Scale;
            // var relativeTranslation = pos - startPos;
            // attachRoot.UseScale().UseTrackBuilder("root").WithPoint(frameTime, scale);
            // attachRoot.UseRotation().UseTrackBuilder("root").WithPoint(frameTime, rot);
            // attachRoot.UseTranslation().UseTrackBuilder("root").WithPoint(frameTime, relativeTranslation);
            
            // if (settings.IncludePositionalData)
            // {
            //     // If positional data is included, apply the translation and rotation to the root
            //
            //     if (!settings.IncludeAbsolutePosition)
            //     {
            //         pos -= startPos;
            //     }
            //     
            //     attachRoot.UseScale().UseTrackBuilder("pose").WithPoint(frameTime, scale);
            //     attachRoot.UseRotation().UseTrackBuilder("pose").WithPoint(frameTime, rot);
            //     attachRoot.UseTranslation().UseTrackBuilder("pose").WithPoint(frameTime, pos);
            // }
            
            attachDict[attachId] = attachBoneMap;
        }
    }
    
    private static bool IsLastFrameInTimeline(
        List<(DateTime Time, AttachSet Attach)> timeline, DateTime time)
    {
        return timeline.LastOrDefault().Time == time;
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

    // Helper to compare transforms with tolerance
    private static bool IsSameTransform(AffineTransform current, AffineTransform previous, float tolerance = 0.0001f)
    {
        return IsVectorSame(current.Scale, previous.Scale, tolerance) &&
               IsQuaternionSame(current.Rotation, previous.Rotation, tolerance) &&
               IsVectorSame(current.Translation, previous.Translation, tolerance);
    }

    private static bool IsVectorSame(System.Numerics.Vector3 a, System.Numerics.Vector3 b, float tolerance)
    {
        return Math.Abs(a.X - b.X) < tolerance &&
               Math.Abs(a.Y - b.Y) < tolerance &&
               Math.Abs(a.Z - b.Z) < tolerance;
    }

    private static bool IsQuaternionSame(System.Numerics.Quaternion a, System.Numerics.Quaternion b, float tolerance)
    {
        return Math.Abs(a.X - b.X) < tolerance &&
               Math.Abs(a.Y - b.Y) < tolerance &&
               Math.Abs(a.Z - b.Z) < tolerance &&
               Math.Abs(a.W - b.W) < tolerance;
    }
    
    public static AffineTransform? GetBoneTransform(ParsedSkeleton skeleton, BoneNodeBuilder bone)
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

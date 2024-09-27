using System.Numerics;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Skeletons;
using Meddle.Utils;
using SharpGLTF.Scenes;
using SharpGLTF.Transforms;

namespace Meddle.Plugin.Utils;

public static class SkeletonUtils
{
    public enum PoseMode
    {
        Local,
        Model
    }
    
    public static (List<BoneNodeBuilder> List, BoneNodeBuilder Root)[] GetBoneMaps(IReadOnlyList<ParsedPartialSkeleton> partialSkeletons, PoseMode? poseMode)
    {
        List<BoneNodeBuilder> boneMap = new();
        List<BoneNodeBuilder> rootList = new();

        for (var partialIdx = 0; partialIdx < partialSkeletons.Count; partialIdx++)
        {
            var partial = partialSkeletons[partialIdx];
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
                
                bone.SetLocalTransform(hkSkeleton.ReferencePose[i].AffineTransform, false);

                var parentIdx = hkSkeleton.BoneParents[i];
                if (parentIdx != -1)
                    skeleBones[parentIdx].AddNode(bone);
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
        if (poseMode != null)
        {
            foreach (var map in boneMaps)
            {
                foreach (var bone in map.List)
                {
                    ApplyPose(bone, partialSkeletons, poseMode.Value, 0);
                }
            }
        }
        
        return boneMaps;
    }

    private static void ApplyPose(
        BoneNodeBuilder bone, IReadOnlyList<ParsedPartialSkeleton> partialSkeletons, PoseMode poseMode, float time)
    {
        var partial = partialSkeletons[bone.PartialSkeletonIndex];
        if (partial.Poses.FirstOrDefault() is not { } pose)
            return;

        switch (poseMode)
        {
            case PoseMode.Model when bone.Parent is BoneNodeBuilder parent:
            {
                var boneTransform = pose.ModelPose[bone.BoneIndex].AffineTransform;
                var parentPose = partialSkeletons[parent.PartialSkeletonIndex].Poses.FirstOrDefault();
                if (parentPose == null)
                    return;

                var parentMatrix = parentPose.ModelPose[parent.BoneIndex].AffineTransform.Matrix;
                var matrix = boneTransform.Matrix * AffineInverse(parentMatrix);
                var affine = new AffineTransform(matrix).GetDecomposed();
                bone.UseScale().UseTrackBuilder("pose").WithPoint(time, affine.Scale);
                bone.UseRotation().UseTrackBuilder("pose").WithPoint(time, affine.Rotation);
                bone.UseTranslation().UseTrackBuilder("pose").WithPoint(time, affine.Translation);
                break;
            }
            case PoseMode.Model:
            {
                var boneTransform = pose.ModelPose[bone.BoneIndex].AffineTransform;
                bone.UseScale().UseTrackBuilder("pose").WithPoint(time, boneTransform.Scale);
                bone.UseRotation().UseTrackBuilder("pose").WithPoint(time, boneTransform.Rotation);
                bone.UseTranslation().UseTrackBuilder("pose").WithPoint(time, boneTransform.Translation);
                break;
            }
            case PoseMode.Local:
            {
                var boneTransform = pose.Pose[bone.BoneIndex].AffineTransform;
                bone.UseScale().UseTrackBuilder("pose").WithPoint(time, boneTransform.Scale);
                bone.UseRotation().UseTrackBuilder("pose").WithPoint(time, boneTransform.Rotation);
                bone.UseTranslation().UseTrackBuilder("pose").WithPoint(time, boneTransform.Translation);
                break;
            }
            default:
                throw new InvalidOperationException("Pose mode must be set to Local or Model");
        }
    }
    
    public static List<BoneNodeBuilder> GetBoneMap(ParsedSkeleton skeleton, PoseMode? poseMode, out BoneNodeBuilder? root)
    {
        var maps = GetBoneMaps(skeleton.PartialSkeletons, poseMode);
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
    public static Dictionary<string, AttachGrouping> GetAnimatedBoneMap((DateTime Time, AttachSet[] Attaches)[] frames, PoseMode poseMode)
    {
        var attachDict = new Dictionary<string, AttachGrouping>();
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
                attachBoneMap = new AttachGrouping([], null, timeline);
                attachDict.Add(attachId, attachBoneMap);
            }

            foreach (var time in allTimes)
            {
                var frame = timeline.FirstOrDefault(x => x.Time == time);
                var frameTime = TotalSeconds(time, startTime);
                if (frame == default) continue;

                var boneMap = GetBoneMap(frame.Attach.Skeleton, null, out var attachRoot);
                if (attachRoot == null)
                    continue;

                if (attachBoneMap.Root == null)
                {
                    attachBoneMap = attachBoneMap with { Root = attachRoot };
                }

                foreach (var attachBone in boneMap)
                {
                    var bone = attachBoneMap.Bones.FirstOrDefault(
                        x => x.BoneName.Equals(attachBone.BoneName, StringComparison.OrdinalIgnoreCase));
                    if (bone == null)
                    {
                        attachBoneMap.Bones.Add(attachBone);
                        bone = attachBone;
                    }

                    ApplyPose(bone, frame.Attach.Skeleton.PartialSkeletons, poseMode, frameTime);
                }

                var firstTranslation = firstAttach.Transform.Translation;
                attachRoot.UseScale().UseTrackBuilder("root").WithPoint(frameTime, frame.Attach.Transform.Scale);
                attachRoot.UseRotation().UseTrackBuilder("root").WithPoint(frameTime, frame.Attach.Transform.Rotation);
                attachRoot.UseTranslation().UseTrackBuilder("root").WithPoint(frameTime, frame.Attach.Transform.Translation - firstTranslation);
                attachDict[attachId] = attachBoneMap;
            }
        }

        return attachDict;
    }
    
    public static Matrix4x4 AffineInverse(Matrix4x4 matrix)
    {
        if (!Matrix4x4.Invert(matrix, out var invMatrix))
            throw new InvalidOperationException("Failed to invert matrix");
        
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        if (matrix.M44 == 1.0)
            invMatrix.M44 = 1f;
        
        return invMatrix;
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

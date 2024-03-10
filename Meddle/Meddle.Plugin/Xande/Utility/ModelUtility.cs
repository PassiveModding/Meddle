using Meddle.Plugin.Xande.Models;
using SharpGLTF.Scenes;
using Xande.Models.Export;

namespace Meddle.Plugin.Xande.Utility;

public static class ModelUtility
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
                if (boneMap.FirstOrDefault(b => b.BoneName.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } dupeBone)
                {
                    skeleBones[i] = dupeBone;
                    continue;
                }
                if (partial.ConnectedBoneIndex == i)
                    throw new InvalidOperationException($"Bone {name} on {i} is connected to a skeleton that should've already been declared");

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

        if (!NodeBuilder.IsValidArmature(boneMap.Cast<NodeBuilder>()))
            throw new InvalidOperationException($"Joints are not valid, {string.Join(", ", boneMap.Select(x => x.Name))}");

        return boneMap;
    }
}

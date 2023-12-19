using Dalamud.Logging;
using Serilog;
using SharpGLTF.Scenes;
using System.Xml.Linq;

namespace Meddle.Xande.Utility;

public static class ModelUtility
{
    public static List<NodeBuilder> GetBoneMap(NewSkeleton skeleton, int prefix, out NodeBuilder? root)
    {
        List<NodeBuilder> boneMap = new();
        root = null;

        foreach (var partial in skeleton.PartialSkeletons)
        {
            var hkSkeleton = partial.HkSkeleton;
            if (hkSkeleton == null)
                continue;

            var pose = partial.Poses.FirstOrDefault();

            for (var i = 0; i < hkSkeleton.BoneNames.Count; i++)
            {
                var name = hkSkeleton.BoneNames[i];
                if (string.IsNullOrEmpty(name))
                    continue;
                if (boneMap.Any(b => b.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var bone = new NodeBuilder(prefix != -1 ? $"{name}_{prefix}" : name);
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
                    boneMap[parentIdx].AddNode(bone);
                else
                {
                    if (root != null)
                        throw new Exception("Multiple root bones found");
                    root = bone;
                }

                boneMap.Add(bone);
            }
        }

        if (!NodeBuilder.IsValidArmature(boneMap))
            throw new Exception(
                $"Joints are not valid, {string.Join(", ", boneMap.Select(x => x.Name))}");

        return boneMap;
    }
}

using FFXIVClientStructs.Havok;
using FFXIVClientStructs.Interop;
using Meddle.Plugin.Xande;

namespace Meddle.Plugin.Models;

public unsafe class SkeletonPose
{
    public IReadOnlyList<Transform> Pose { get; set; }

    public SkeletonPose(hkaPose* pose)
    {
        var transforms = new List<Transform>();
        var boneCount = pose->LocalPose.Length;
        for (var i = 0; i < boneCount; ++i)
            transforms.Add(new Transform(pose->LocalPose[i]));
        
        Pose = transforms;
    }
}

using FFXIVClientStructs.Havok;
using FFXIVClientStructs.Interop;
using Meddle.Plugin.Xande;

namespace Meddle.Plugin.Models;

public unsafe class SkeletonPose
{
    public List<Transform> Pose { get; set; }

    public SkeletonPose(Pointer<hkaPose> pose) : this(pose.Value)
    {

    }

    public SkeletonPose(hkaPose* pose)
    {
        Pose = new();

        var boneCount = pose->LocalPose.Length;
        for (var i = 0; i < boneCount; ++i)
            Pose.Add(new(pose->LocalPose[i]));
    }
}

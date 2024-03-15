using System.Text.Json.Serialization;
using FFXIVClientStructs.Havok;
using FFXIVClientStructs.Interop;

namespace Meddle.Plugin.Models;

public unsafe class SkeletonPose
{
    [JsonIgnore]
    public IReadOnlyList<Transform> Pose { get; set; }

    public SkeletonPose(Pointer<hkaPose> pose) : this(pose.Value)
    {
    }
    
    public SkeletonPose(hkaPose* pose)
    {
        var transforms = new List<Transform>();
        var boneCount = pose->LocalPose.Length;
        for (var i = 0; i < boneCount; ++i)
            transforms.Add(new Transform(pose->LocalPose[i]));
        
        Pose = transforms;
    }
}

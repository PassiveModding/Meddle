using System.Text.Json.Serialization;
using FFXIVClientStructs.Havok.Animation.Rig;
using FFXIVClientStructs.Interop;

namespace Meddle.Plugin.Models.Skeletons;

public class ParsedHkaPose
{
    public unsafe ParsedHkaPose(Pointer<hkaPose> pose) : this(pose.Value) { }

    public unsafe ParsedHkaPose(hkaPose* pose)
    {
        var boneCount = pose->Skeleton->Bones.Length;
        
        var transforms = new List<Transform>();
        var syncedLocalPose = pose->GetSyncedPoseLocalSpace()->Data;
        for (var i = 0; i < boneCount; ++i)
        {
            var localSpace = syncedLocalPose[i];
            transforms.Add(new Transform(localSpace));
        }

        var modelTransforms = new List<Transform>();
        var modelSpace = pose->GetSyncedPoseModelSpace()->Data;
        for (var i = 0; i < boneCount; ++i)
        {
            var model = modelSpace[i];
            modelTransforms.Add(new Transform(model));
        }
        

        Pose = transforms;
        ModelPose = modelTransforms;
    }

    [JsonIgnore]
    public IReadOnlyList<Transform> Pose { get; }
    
    [JsonIgnore]
    public IReadOnlyList<Transform> ModelPose { get; }
}

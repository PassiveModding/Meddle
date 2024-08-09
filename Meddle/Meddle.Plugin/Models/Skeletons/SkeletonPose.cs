using System.Text.Json.Serialization;
using FFXIVClientStructs.Havok.Animation.Rig;
using FFXIVClientStructs.Interop;
using Meddle.Plugin.Utils;
using Meddle.Utils.Skeletons;

namespace Meddle.Plugin.Models.Skeletons;

public class ParsedHkaPose
{
    public unsafe ParsedHkaPose(Pointer<hkaPose> pose) : this(pose.Value) { }

    public unsafe ParsedHkaPose(hkaPose* pose)
    {
        var transforms = new List<Transform>();

        var boneCount = pose->LocalPose.Length;
        for (var i = 0; i < boneCount; ++i)
        {
            var localSpace = PoseUtil.AccessBoneLocalSpace(pose, i);
            if (localSpace == null)
            {
                throw new Exception("Failed to access bone local space");
            }

            transforms.Add(new Transform(*localSpace));
        }

        Pose = transforms;
    }

    [JsonIgnore]
    public IReadOnlyList<Transform> Pose { get; }
}

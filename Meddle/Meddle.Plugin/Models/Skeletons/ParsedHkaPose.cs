using System.Text.Json.Serialization;
using FFXIVClientStructs.FFXIV.Common.Math;
using FFXIVClientStructs.Havok.Animation.Rig;
using FFXIVClientStructs.Interop;
using Meddle.Plugin.Utils;

namespace Meddle.Plugin.Models.Skeletons;

public class ParsedHkaPose
{
    public unsafe ParsedHkaPose(Pointer<hkaPose> pose) : this(pose.Value) { }

    public unsafe ParsedHkaPose(hkaPose* pose)
    {
        var localSpaceTransforms = new List<Transform>();
        var hkLocalSpaceMatrices = new List<Matrix4x4>();
        var hkModelSpaceMatrices = new List<Matrix4x4>();

        var boneCount = pose->Skeleton->Bones.Length;
        
        for (var i = 0; i < boneCount; ++i)
        {
            var localSpace = pose->AccessBoneLocalSpace(i);
            if (localSpace == null)
            {
                throw new ArgumentException($"Failed to access bone {i}");
            }
            hkLocalSpaceMatrices.Add(Alloc.GetMatrix(localSpace));
            localSpaceTransforms.Add(new Transform(*localSpace));
        }
        for (var i = 0; i < boneCount; ++i)
        {
            var modelSpace = pose->AccessBoneModelSpace(i, hkaPose.PropagateOrNot.DontPropagate);
            if (modelSpace == null)
            {
                throw new ArgumentException($"Failed to access model bone {i}");
            }
            hkModelSpaceMatrices.Add(Alloc.GetMatrix(modelSpace));
        }

        Pose = localSpaceTransforms;
        HkLocalSpaceMatrices = hkLocalSpaceMatrices;
        HkModelSpaceMatrices = hkModelSpaceMatrices;
    }

    [JsonIgnore]
    public IReadOnlyList<Transform> Pose { get; }

    [JsonIgnore]
    public IReadOnlyList<Matrix4x4> HkLocalSpaceMatrices { get; }
    
    [JsonIgnore]
    public IReadOnlyList<Matrix4x4> HkModelSpaceMatrices { get; }
}

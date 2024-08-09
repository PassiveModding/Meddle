using System.Text.Json.Serialization;
using FFXIVClientStructs.Havok.Animation.Rig;
using FFXIVClientStructs.Interop;
using Meddle.Utils.Skeletons;

namespace Meddle.Plugin.Models.Skeletons;

public class ParsedHkaSkeleton
{
    public unsafe ParsedHkaSkeleton(Pointer<hkaSkeleton> skeleton) : this(skeleton.Value) { }

    public unsafe ParsedHkaSkeleton(hkaSkeleton* skeleton)
    {
        var boneNames = new List<string?>();
        var boneParents = new List<short>();
        var referencePose = new List<Transform>();

        for (var i = 0; i < skeleton->Bones.Length; ++i)
        {
            boneNames.Add(skeleton->Bones[i].Name.String);
            boneParents.Add(skeleton->ParentIndices[i]);
            referencePose.Add(new Transform(skeleton->ReferencePose[i]));
        }

        BoneNames = boneNames;
        BoneParents = boneParents;
        ReferencePose = referencePose;
    }

    public IReadOnlyList<string?> BoneNames { get; }
    public IReadOnlyList<short> BoneParents { get; }

    [JsonIgnore]
    public IReadOnlyList<Transform> ReferencePose { get; }
}

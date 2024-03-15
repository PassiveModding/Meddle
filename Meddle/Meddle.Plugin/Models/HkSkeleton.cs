using FFXIVClientStructs.Havok;
using FFXIVClientStructs.Interop;
using Meddle.Plugin.Xande;

namespace Meddle.Plugin.Models;

public unsafe class HkSkeleton
{
    public IReadOnlyList<string?> BoneNames { get; set; }
    public IReadOnlyList<short> BoneParents { get; set; }
    public IReadOnlyList<Transform> ReferencePose { get; set; }

    public HkSkeleton(hkaSkeleton* skeleton)
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
}

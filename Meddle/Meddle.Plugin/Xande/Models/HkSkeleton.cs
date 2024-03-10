using FFXIVClientStructs.Havok;
using FFXIVClientStructs.Interop;

namespace Meddle.Plugin.Xande.Models;

public unsafe class HkSkeleton
{
    public List<string?> BoneNames { get; set; }
    public List<short> BoneParents { get; set; }
    public List<Transform> ReferencePose { get; set; }

    public HkSkeleton(Pointer<hkaSkeleton> skeleton) : this(skeleton.Value)
    {

    }

    public HkSkeleton(hkaSkeleton* skeleton)
    {
        BoneNames = new();
        BoneParents = new();
        ReferencePose = new();

        for (var i = 0; i < skeleton->Bones.Length; ++i)
        {
            BoneNames.Add(skeleton->Bones[i].Name.String);
            BoneParents.Add(skeleton->ParentIndices[i]);
            ReferencePose.Add(new(skeleton->ReferencePose[i]));
        }
    }
}

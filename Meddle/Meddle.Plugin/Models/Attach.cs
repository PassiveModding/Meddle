using FFXIVClientStructs.Interop;
using CSAttach = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Attach;

namespace Meddle.Plugin.Models;

public unsafe class Attach
{
    public int ExecuteType { get; }

    public Transform OffsetTransform { get; }
    public byte PartialSkeletonIdx { get; }
    public ushort BoneIdx { get; }
    
    public Attach(Pointer<CSAttach> attach) : this(attach.Value)
    {
    }
    
    public Attach(CSAttach* attach)
    {
        // 0 => Mount
        // 3 => Fashion Accessories
        // 4 => Weapon
        ExecuteType = attach->ExecuteType;
        
        // TODO: Deconstruct union based on type
        var att = attach->SkeletonBoneAttachments[0];
        OffsetTransform = new(att.ChildTransform);
        if (ExecuteType == 0)
            return;

        //if (ExecuteType != 4)
        //    return;
        
        PartialSkeletonIdx = att.SkeletonIdx;
        BoneIdx = att.BoneIdx;
    }
}

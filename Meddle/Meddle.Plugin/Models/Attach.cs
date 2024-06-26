﻿using FFXIVClientStructs.Interop;
using CSAttach = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Attach;

namespace Meddle.Plugin.Models;

public unsafe class Attach
{
    public int ExecuteType { get; }

    public Transform? OffsetTransform { get; }
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
        if (ExecuteType == 0)
            return;
        

        var att = attach->SkeletonBoneAttachments[0];
        OffsetTransform = new(att.ChildTransform);
        
        PartialSkeletonIdx = att.SkeletonIdx;
        BoneIdx = att.BoneIdx;
    }
}

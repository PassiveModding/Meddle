using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.STD;

namespace Meddle.Plugin.Models.Structs;

// Client::Graphics::Render::Skeleton
//   Client::Graphics::ReferencedClassBase
[StructLayout(LayoutKind.Explicit, Size = 0x100)]
public unsafe struct Skeleton
{
    // Used by attach execute type 3
    // 1. OwnerCharacter->Skeleton->AttachBonesSpan; find bone by BoneIndex matching Attach.BoneIdx
    // 2. Use the found bone's index to get the BoneIndexMask from OwnerCharacter->Skeleton->BoneMasksSpan
    // 3. Use the BoneIndexMask to get the Skeleton index and Bone index in the owner's skeleton
    [FieldOffset(0x88)]
    public Bone* AttachBones;

    [FieldOffset(0xA0)]
    public uint AttachBoneCount;

    [FieldOffset(0x98)]
    public BoneIndexMask* AttachBoneMasks;

    [FieldOffset(0xB8)]
    public CharacterBase* Owner;

    [StructLayout(LayoutKind.Explicit, Size = 0x40)]
    public struct Bone
    {
        [FieldOffset(0x0)]
        public StdString BoneName;

        [FieldOffset(0x20)]
        public uint BoneIndex;
    }

    [StructLayout(LayoutKind.Explicit, Size = 0x2)]
    public struct BoneIndexMask
    {
        [FieldOffset(0x0)]
        public ushort SkeletonIdxBoneIdx;

        public readonly byte SkeletonIdx => (byte)((SkeletonIdxBoneIdx >> 12) & 0xF);
        public readonly ushort BoneIdx => (ushort)(SkeletonIdxBoneIdx & 0xFFF);
    }

    public Span<Bone> AttachBonesSpan => new(AttachBones, (int)AttachBoneCount);
    public Span<BoneIndexMask> BoneMasksSpan => new(AttachBoneMasks, (int)AttachBoneCount);
}

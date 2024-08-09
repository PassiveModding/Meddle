using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.Interop;
using Meddle.Plugin.Models.Skeletons;
using Meddle.Plugin.Models.Structs;
using PartialSkeleton = FFXIVClientStructs.FFXIV.Client.Graphics.Render.PartialSkeleton;
using Skeleton = FFXIVClientStructs.FFXIV.Client.Graphics.Render.Skeleton;

namespace Meddle.Plugin.Models;

public static class StructExtensions
{
    public const int CharacterBaseAttachOffset = 0xD0; // CharacterBase + 0xD0 -> Attach

    public const int ModelEnabledAttributeIndexMaskOffset = 0xAC; // Model + 0xAC -> EnabledAttributeIndexMask
    public const int ModelEnabledShapeKeyIndexMaskOffset = 0xC8;  // Model + 0xC8 -> EnabledShapeKeyIndexMask

    public const int PartialSkeletonFlagsOffset = 0x8; // PartialSkeleton + 0x8 -> Flags

    public static unsafe Attach GetAttach(this Pointer<CharacterBase> character)
    {
        if (character == null) throw new ArgumentNullException(nameof(character));
        if (character.Value == null) throw new ArgumentNullException(nameof(character));

        var characterBase = character.Value; // + 0xD0 gives Attach data
        var offset = (nint)characterBase + CharacterBaseAttachOffset;
        return *(Attach*)offset;
    }

    public static ParsedAttach GetParsedAttach(this Pointer<CharacterBase> character)
    {
        var attach = GetAttach(character);
        return new ParsedAttach(attach);
    }

    public static unsafe uint GetFlags(this Pointer<PartialSkeleton> partialSkeleton)
    {
        if (partialSkeleton == null) throw new ArgumentNullException(nameof(partialSkeleton));
        if (partialSkeleton.Value == null) throw new ArgumentNullException(nameof(partialSkeleton));

        var flags = *(uint*)((nint)partialSkeleton.Value + PartialSkeletonFlagsOffset);
        return flags;
    }

    public static uint GetBoneCount(this Pointer<PartialSkeleton> partialSkeleton)
    {
        var flags = partialSkeleton.GetFlags();
        return (flags >> 5) & 0xFFFu;
    }

    public static unsafe ParsedSkeleton GetParsedSkeleton(this Pointer<CharacterBase> character)
    {
        if (character == null) throw new ArgumentNullException(nameof(character));
        if (character.Value == null) throw new ArgumentNullException(nameof(character));
        return GetParsedSkeleton(character.Value->Skeleton);
    }

    public static unsafe ParsedSkeleton GetParsedSkeleton(this Pointer<Model> model)
    {
        if (model == null) throw new ArgumentNullException(nameof(model));
        if (model.Value == null) throw new ArgumentNullException(nameof(model));
        return GetParsedSkeleton(model.Value->Skeleton);
    }

    private static unsafe ParsedSkeleton GetParsedSkeleton(this Pointer<Skeleton> skeleton)
    {
        if (skeleton == null) throw new ArgumentNullException(nameof(skeleton));
        return new ParsedSkeleton(skeleton.Value);
    }

    public static unsafe (uint EnabledAttributeIndexMask, uint EnabledShapeKeyIndexMask) GetModelMasks(
        this Pointer<Model> model)
    {
        if (model == null) throw new ArgumentNullException(nameof(model));
        if (model.Value == null) throw new ArgumentNullException(nameof(model));

        var modelBase = model.Value;
        var enabledAttributeIndexMask = *(uint*)((nint)modelBase + ModelEnabledAttributeIndexMaskOffset);
        var enabledShapeKeyIndexMask = *(uint*)((nint)modelBase + ModelEnabledShapeKeyIndexMaskOffset);
        return (enabledAttributeIndexMask, enabledShapeKeyIndexMask);
    }
    
    public static unsafe Meddle.Utils.Export.Model.ShapeAttributeGroup ParseModelShapeAttributes(
        Pointer<Model> modelPointer)
    {
        if (modelPointer == null) throw new ArgumentNullException(nameof(modelPointer));
        if (modelPointer.Value == null) throw new ArgumentNullException(nameof(modelPointer));
        var model = modelPointer.Value;
        var (enabledAttributeIndexMask, enabledShapeKeyIndexMask) = modelPointer.GetModelMasks();
        var shapes = new List<(string, short)>();
        foreach (var shape in model->ModelResourceHandle->Shapes)
        {
            shapes.Add((MemoryHelper.ReadStringNullTerminated((nint)shape.Item1.Value), shape.Item2));
        }

        var attributes = new List<(string, short)>();
        foreach (var attribute in model->ModelResourceHandle->Attributes)
        {
            attributes.Add((MemoryHelper.ReadStringNullTerminated((nint)attribute.Item1.Value), attribute.Item2));
        }

        var shapeAttributeGroup = new Meddle.Utils.Export.Model.ShapeAttributeGroup(
            enabledShapeKeyIndexMask, enabledAttributeIndexMask, shapes.ToArray(), attributes.ToArray());

        return shapeAttributeGroup;
    }
}

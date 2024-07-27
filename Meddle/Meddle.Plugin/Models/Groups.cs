using System.Numerics;
using Meddle.Plugin.Skeleton;
using Meddle.Utils.Export;
using Meddle.Utils.Files;

namespace Meddle.Plugin.Models;
public record CharacterGroup(
    Meddle.Utils.Export.CustomizeParameter CustomizeParams,
    CustomizeData CustomizeData,
    GenderRace GenderRace,
    MdlFileGroup[] MdlGroups,
    Skeleton.Skeleton Skeleton,
    AttachedModelGroup[] AttachedModelGroups);

public record AttachedModelGroup(Attach Attach, MdlFileGroup[] MdlGroups, Skeleton.Skeleton Skeleton);
public record MdlFileGroup(string Path, MdlFile MdlFile, MtrlFileGroup[] MtrlFiles, Model.ShapeAttributeGroup? ShapeAttributeGroup);
public record MtrlFileGroup(string Path, MtrlFile MtrlFile, string ShpkPath, ShpkFile ShpkFile, TexResourceGroup[] TexFiles);
public record TexResourceGroup(string MtrlPath, string ResourcePath, TextureResource Resource);
public record SklbFileGroup(string Path, SklbFile File);
public record Resource(string MdlPath, Vector3 Position, Quaternion Rotation, Vector3 Scale);

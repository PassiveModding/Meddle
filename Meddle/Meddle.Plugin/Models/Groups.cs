using System.Numerics;
using Meddle.Plugin.Models.Skeletons;
using Meddle.Utils.Export;
using Meddle.Utils.Files;

namespace Meddle.Plugin.Models;

public record CharacterGroup(
    CustomizeParameter CustomizeParams,
    CustomizeData CustomizeData,
    GenderRace GenderRace,
    MdlFileGroup[] MdlGroups,
    ParsedSkeleton Skeleton,
    AttachedModelGroup[] AttachedModelGroups);

public record AttachedModelGroup(ParsedAttach Attach, MdlFileGroup[] MdlGroups, ParsedSkeleton Skeleton);

public record MdlFileGroup(
    string CharacterPath,
    string Path,
    DeformerGroup? DeformerGroup,
    MdlFile MdlFile,
    MtrlFileGroup[] MtrlFiles,
    Model.ShapeAttributeGroup? ShapeAttributeGroup);

public record MtrlFileGroup(
    string MdlPath,
    string Path,
    MtrlFile MtrlFile,
    string ShpkPath,
    ShpkFile ShpkFile,
    TexResourceGroup[] TexFiles);

public record TexResourceGroup(string MtrlPath, string Path, TextureResource Resource);

public record SklbFileGroup(string Path, SklbFile File);

public record Resource(string MdlPath, Vector3 Position, Quaternion Rotation, Vector3 Scale);

public record DeformerGroup(string Path, ushort RaceSexId, ushort DeformerId);

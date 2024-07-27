using Meddle.Utils.Export;
using Meddle.Utils.Files;

namespace Meddle.UI.Models;

public record MdlFileGroup(string Path, MdlFile MdlFile, MtrlFileGroup[] MtrlFiles, Model.ShapeAttributeGroup? ShapeAttributeGroup);
public record MtrlFileGroup(string Path, MtrlFile MtrlFile, string ShpkPath, ShpkFile ShpkFile, TexFileGroup[] TexFiles);
public record TexFileGroup(string Path, TexFile TexFile);
public record SklbFileGroup(string Path, SklbFile File);
public record BgObjectGroup(LgbFile.Group.InstanceObject ObjectInfo, MdlFileGroup MdlGroup);



using Meddle.Plugin.Enums;
using Meddle.Plugin.UI;

namespace Meddle.Plugin.Models.ExportRequest;

public interface IExportRequest;
public class ExportModelRequest(
    Model model,
    Skeleton skeleton,
    CustomizeParameters? customizeParameters = null,
    GenderRace? raceCode = null)
    : IExportRequest
{
    public Model Model { get; } = model;
    public Skeleton Skeleton { get; } = skeleton;
    public CustomizeParameters? CustomizeParameters { get; } = customizeParameters;
    public GenderRace? RaceCode { get; } = raceCode;
}
    
public class MaterialExportRequest(Material material, CustomizeParameters? customizeParameters = null) : IExportRequest
{
    public Material Material { get; } = material;
    public CustomizeParameters? CustomizeParameters { get; } = customizeParameters;
}

public class ExportTreeRequest(CharacterTree tree) : IExportRequest
{
    public CharacterTree Tree { get; } = tree;
}

public class ExportPartialTreeRequest(
    CharacterTree tree, 
    Model[] selectedModels, 
    AttachedChild[] attachedChildren)
    : IExportRequest
{
    public Model[] SelectedModels { get; } = selectedModels;
    public AttachedChild[] AttachedChildren { get; } = attachedChildren;

    public GenderRace? RaceCode { get; } = tree.RaceCode;
    public CustomizeParameters? CustomizeParameters { get; } = tree.CustomizeParameter;
    public Skeleton Skeleton { get; } = tree.Skeleton;
}
    
public class ExportAttachRequest(
    AttachedChild child,
    GenderRace? raceCode = null,
    CustomizeParameters? customizeParameters = null)
    : IExportRequest
{
    public AttachedChild Child { get; } = child;
    public GenderRace? RaceCode { get; } = raceCode;
    public CustomizeParameters? CustomizeParameters { get; } = customizeParameters;
}

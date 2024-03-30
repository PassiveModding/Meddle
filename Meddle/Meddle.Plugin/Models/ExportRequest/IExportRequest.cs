using Meddle.Plugin.UI;

namespace Meddle.Plugin.Models.ExportRequest;

public interface IExportRequest;
public class ExportModelRequest(Model model) : IExportRequest
{
    public Model Model { get; } = model;
    public Skeleton? SkeletonOverride { get; set; }
}
    
public class MaterialExportRequest(Material material) : IExportRequest
{
    public Material Material { get; } = material;
}
    
public class ExportTreeRequest(CharacterTreeSet set, bool applySettings = false) : IExportRequest
{
    public CharacterTreeSet Set { get; } = set;
    public bool ApplySettings { get; set; } = applySettings;
}
    
public class ExportAttachRequest(AttachedChild child) : IExportRequest
{
    public AttachedChild Child { get; } = child;
}

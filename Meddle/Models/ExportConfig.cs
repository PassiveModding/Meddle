namespace Meddle.Plugin.Models;

public class ExportConfig
{
    public bool GenerateMissingBones { get; set; } = false;
    
    public Customize.Customize? Customize { get; set; }
}
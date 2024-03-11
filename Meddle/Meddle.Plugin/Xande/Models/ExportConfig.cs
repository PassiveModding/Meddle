using Meddle.Plugin.Xande.Enums;

namespace Meddle.Plugin.Xande.Models;

public class ExportConfig
{
    public bool OpenFolderWhenComplete { get; set; }
    public ExportType ExportType { get; set; }
    public bool IncludeReaperEye { get; set; }
}

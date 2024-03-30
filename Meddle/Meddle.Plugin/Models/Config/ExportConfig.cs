using Meddle.Plugin.Enums;

namespace Meddle.Plugin.Models.Config;

public class ExportConfig
{
    public bool OpenFolderWhenComplete { get; set; }
    public ExportType ExportType { get; set; }
    public bool IncludeReaperEye { get; set; }
    public bool ParallelBuild { get; set; }
}

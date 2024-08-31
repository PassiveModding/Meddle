namespace Meddle.Plugin.Models.Composer;

public record ProgressEvent(string Name, int Progress, int Total, ProgressEvent? SubProgress = null);

namespace Meddle.Plugin.Models.Composer;

public record ProgressEvent(int ContextHash, string Name, int Progress, int Total, ProgressEvent? SubProgress = null);

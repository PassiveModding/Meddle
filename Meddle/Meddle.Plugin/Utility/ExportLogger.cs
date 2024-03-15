using Dalamud.Plugin.Services;
using Serilog.Events;

namespace Meddle.Plugin.Utility;

public class ExportLogger
{
    private readonly IPluginLog log;
    public List<(LogEventLevel level, string message)> LoggedMessages;

    public (LogEventLevel level, string? message) GetLastLog()
    {
        return LoggedMessages.LastOrDefault();
    }

    public ExportLogger(IPluginLog log)
    {
        this.log = log;
        LoggedMessages = new();
    }

    public void Log(LogEventLevel level, string message)
    {
        LoggedMessages.Add((level, message));
    }

    public void Warn(string message)
    {
        Log(LogEventLevel.Warning, message);
        log.Warning(message);
    }

    public void Debug(string message)
    {
        Log(LogEventLevel.Debug, message);
        log.Debug(message);
    }

    public void Info(string message)
    {
        Log(LogEventLevel.Information, message);
        log.Information(message);
    }

    public void Error(string message)
    {
        Log(LogEventLevel.Error, message);
        log.Error(message);
    }

    public void Error(Exception ex, string message)
    {
        Log(LogEventLevel.Error, message);
        log.Error(ex, message);
    }
}

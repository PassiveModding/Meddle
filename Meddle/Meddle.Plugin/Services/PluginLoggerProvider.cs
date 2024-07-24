using Dalamud.IoC;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging;

namespace Meddle.Plugin.Services;

public class PluginLoggerProvider : ILoggerProvider
{
    [PluginService]
    private IPluginLog PluginLog { get; set; } = null!;

    public ILogger CreateLogger(string categoryName)
    {
        return new PluginLogger(PluginLog, categoryName);
    }

    public void Dispose()
    {
        PluginLog.Debug("Disposing logger provider");
    }
}

public class PluginLogger : ILogger
{
    private readonly string categoryName;
    private readonly IPluginLog log;

    public PluginLogger(IPluginLog log, string categoryName)
    {
        this.log = log;
        this.categoryName = categoryName;
    }

    public IDisposable? BeginScope<TState>(TState state)
    {
        log.Debug($"Scope: {state}");
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = $"[{categoryName}] {formatter(state, exception)}";
        switch (logLevel)
        {
            case LogLevel.Trace:
                log.Verbose(message);
                break;
            case LogLevel.Debug:
                log.Debug(message);
                break;
            case LogLevel.Information:
                log.Info(message);
                break;
            case LogLevel.Warning:
                log.Warning(message);
                break;
            case LogLevel.Error:
                log.Error(exception, message);
                break;
            case LogLevel.Critical:
                log.Error(exception, message);
                break;
            case LogLevel.None:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(logLevel), logLevel, null);
        }
    }
}

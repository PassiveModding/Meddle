using Microsoft.Extensions.Logging;

namespace Meddle.Plugin.Utils;

public class EventLogger<T> : ILogger
{
    public ILogger<T> Logger { get; }
    public event Action<LogLevel, string>? OnLogEvent;
    
    public EventLogger(ILogger<T> logger)
    {
        Logger = logger;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        Logger.Log(logLevel, eventId, state, exception, formatter);
        OnLogEvent?.Invoke(logLevel, message);
    }
    
    public bool IsEnabled(LogLevel logLevel) => Logger.IsEnabled(logLevel);
    
    public IDisposable BeginScope<TState>(TState state) => Logger.BeginScope(state);
}

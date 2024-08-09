using Dalamud.Interface.ImGuiNotification;
using Dalamud.IoC;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging;

namespace Meddle.Plugin.Services;

public class PluginLoggerProvider : ILoggerProvider
{
    private readonly Configuration config;

    public PluginLoggerProvider(Configuration config)
    {
        this.config = config;
    }

    [PluginService]
    private IPluginLog PluginLog { get; set; } = null!;

    [PluginService]
    private INotificationManager NotificationManager { get; set; } = null!;

    public ILogger CreateLogger(string categoryName)
    {
        return new PluginLogger(PluginLog, NotificationManager, config, categoryName);
    }

    public void Dispose()
    {
        PluginLog.Debug("Disposing logger provider");
    }
}

public class PluginLogger : ILogger
{
    private readonly string categoryName;
    private readonly Configuration config;
    private readonly IPluginLog log;
    private readonly INotificationManager notificationManager;

    private readonly List<IActiveNotification> notifications = new();

    public PluginLogger(
        IPluginLog log, INotificationManager notificationManager, Configuration config, string categoryName)
    {
        this.log = log;
        this.notificationManager = notificationManager;
        this.config = config;
        this.categoryName = categoryName;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var formattedMessage = formatter(state, exception);
        var message = $"[{categoryName}] {formattedMessage}";
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
                LogNotification(formattedMessage, logLevel);
                break;
            case LogLevel.Warning:
                log.Warning(message);
                LogNotification(formattedMessage, logLevel);
                break;
            case LogLevel.Error:
                log.Error(exception, message);
                LogNotification(formattedMessage, logLevel);
                break;
            case LogLevel.Critical:
                log.Error(exception, message);
                LogNotification(formattedMessage, logLevel);
                break;
            case LogLevel.None:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(logLevel), logLevel, null);
        }
    }

    private void LogNotification(string message, LogLevel level)
    {
        if (level < config.MinimumNotificationLogLevel) return;

        var type = level switch
        {
            LogLevel.Trace => NotificationType.Info,
            LogLevel.Debug => NotificationType.Info,
            LogLevel.Information => NotificationType.Info,
            LogLevel.Warning => NotificationType.Warning,
            LogLevel.Error => NotificationType.Error,
            LogLevel.Critical => NotificationType.Error,
            LogLevel.None => NotificationType.None,
            _ => NotificationType.None
        };

        var notification = new Notification
        {
            Title = categoryName,
            Content = message,
            Type = type,
            InitialDuration = TimeSpan.FromSeconds(2)
        };

        var notif = notificationManager.AddNotification(notification);
        notifications.Add(notif);

        if (notifications.Count > 5)
        {
            var toRemove = notifications[0];
            notifications.RemoveAt(0);
            toRemove.DismissNow();
        }
    }
}

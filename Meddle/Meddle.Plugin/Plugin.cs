using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Meddle.Plugin.Services;
using Meddle.SqPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OtterTex;

namespace Meddle.Plugin;

public sealed class Plugin : IDalamudPlugin
{
    private readonly IHost? app;
    private readonly ILogger pluginLog;
    public static ILogger<Plugin> Logger { get; private set; } = NullLogger<Plugin>.Instance;
    public static INotificationManager NotificationManager { get; private set; } = null!;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        var service = new Service();
        pluginInterface.Inject(service);
        
        var dLogger = service.GetLog() ?? throw new InvalidOperationException("Service log is null");
        pluginLog = new PluginSerilogWrapper(dLogger.Logger);
        pluginLog.LogDebug("Meddle Plugin initializing...");
        Global.Logger = pluginLog;
        
        try
        {
#if HAS_LOCAL_CS
            FFXIVClientStructs.Interop.Generated.Addresses.Register();
            InteropGenerator.Runtime.Resolver.GetInstance.Setup();
            InteropGenerator.Runtime.Resolver.GetInstance.Resolve();
#endif
            
            var config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            pluginInterface.Inject(config);
            config.Migrate();

            var host = Host.CreateDefaultBuilder();
            host.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.Trace);
                var loggerProvider = new PluginLoggerProvider(config);
                pluginInterface.Inject(loggerProvider);
                logging.AddProvider(loggerProvider);
            });

            host.ConfigureServices(services =>
            {
                services.Configure<ConsoleLifetimeOptions>(options => options.SuppressStatusMessages = true);
                var packDir = Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory;
                pluginLog.LogInformation("Getting SqPack at {Path}", packDir);
                service.RegisterServices(services);
                services
                    .AddServices(pluginInterface)    
                    .AddSingleton(config)
                    .AddUi()
                    .AddSingleton(new SqPack.SqPack(packDir));
            });

            app = host.Build();
            Logger = app.Services.GetRequiredService<ILogger<Plugin>>();
            NotificationManager = app.Services.GetRequiredService<INotificationManager>();
            Global.Logger = app.Services.GetRequiredService<ILogger<Global>>();
            NativeDll.Initialize(app.Services.GetRequiredService<IDalamudPluginInterface>().AssemblyLocation.DirectoryName);
            var pack = app.Services.GetRequiredService<SqPack.SqPack>();
            pack.RsfData = config.RsfConfig.GetRsfData();
            app.Services.GetRequiredService<RsfWatcher>();

            app.Start();
        }
        catch (Exception e)
        {
            pluginLog.LogError(e, "Failed to initialize plugin");
            Dispose();
        }
    }

    public void Dispose()
    {
        app?.StopAsync();
        app?.WaitForShutdown();
        app?.Dispose();
        pluginLog.LogDebug("Plugin disposed");
    }
}

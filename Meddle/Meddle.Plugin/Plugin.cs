using Dalamud.Plugin;
using Meddle.Plugin.Services;
using Meddle.Plugin.Utils;
using Meddle.Utils.Files.SqPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OtterTex;

namespace Meddle.Plugin;

public sealed class Plugin : IDalamudPlugin
{
    public static readonly string DefaultExportDirectory = Path.Combine(Path.GetTempPath(), "Meddle.Export");
    private readonly IHost? app;
    private readonly ILogger pluginLog;
    public static ILogger<Plugin> Logger { get; private set; } = NullLogger<Plugin>.Instance;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        var service = new Service();
        pluginInterface.Inject(service);
        
        var dLogger = service.GetLog() ?? throw new InvalidOperationException("Service log is null");
        pluginLog = new PluginSerilogWrapper(dLogger.Logger);
        pluginLog.LogDebug("Meddle Plugin initializing...");
        Meddle.Utils.Global.Logger = pluginLog;
        
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
            
            Alloc.Init();

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
                service.RegisterServices(services);
                services
                    .AddServices(pluginInterface)    
                    .AddSingleton(config)
                    .AddUi()
                    .AddSingleton(new SqPack(Environment.CurrentDirectory));
            });

            app = host.Build();
            Logger = app.Services.GetRequiredService<ILogger<Plugin>>();
            Meddle.Utils.Global.Logger = app.Services.GetRequiredService<ILogger<Meddle.Utils.Global>>();
            NativeDll.Initialize(app.Services.GetRequiredService<IDalamudPluginInterface>().AssemblyLocation.DirectoryName);

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
        Alloc.Dispose();
    }
}

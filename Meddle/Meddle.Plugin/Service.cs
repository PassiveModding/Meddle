using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace Meddle.Plugin;

public class Service {
    [PluginService]
    public static DalamudPluginInterface PluginInterface { get; private set; } = null!;

    [PluginService]
    public static IFramework Framework { get; private set; } = null!;

    [PluginService]
    public static ICommandManager CommandManager { get; private set; } = null!;

    [PluginService]
    public static IPluginLog Log { get; private set; } = null!;

    [PluginService]
    public static IClientState ClientState { get; private set; } = null!;

    [PluginService]
    public static IDataManager GameData { get; private set; } = null!;
    [PluginService]
    public static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService]
    public static IGameInteropProvider Interop { get; private set; } = null!;
    [PluginService]
    public static IGameGui Gui { get; private set; } = null!;
}
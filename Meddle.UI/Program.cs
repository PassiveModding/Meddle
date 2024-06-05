using System.Diagnostics;
using System.Numerics;
using ImGuiNET;
using Meddle.UI.Windows;
using Meddle.Utils.Files.SqPack;
using Microsoft.Extensions.Logging;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;
using WebSocketSharp;

namespace Meddle.UI;

public class Program
{
    public static readonly string DataDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Meddle");

    private readonly ILoggerFactory logFactory = LoggerFactory.Create(builder => builder.AddConsole());
    private readonly ILogger<Program> logger;
    
    public Configuration Configuration;
    public Sdl2Window Window;
    public GraphicsDevice GraphicsDevice;
    public ImGuiHandler ImGuiHandler;
    public ImageHandler ImageHandler;
    public SettingsWindow SettingsWindow;
    
    public Program()
    {        
        if (!Directory.Exists(DataDirectory))
        {
            Directory.CreateDirectory(DataDirectory);
        }

        Configuration = Configuration.Load();
        logger = logFactory.CreateLogger<Program>();
        logger.LogInformation("Kweh!");
        SettingsWindow = new(Configuration);
        SettingsWindow.OnRedrawBrowser += () =>
        {
            SqPackWindowTask = null;
        };
    }

    private async Task RunAsync()
    {
        VeldridStartup.CreateWindowAndGraphicsDevice(
            new WindowCreateInfo(
                Configuration.WindowX,
                Configuration.WindowY,
                Configuration.WindowWidth,
                Configuration.WindowHeight,
                WindowState.Normal,
                "Meddle"),
            out var window,
            out var graphicsDevice);
        
        if (window is null || graphicsDevice is null) {
            logger.LogError("Failed to create window and graphics device.");
            return;
        }
        
        Window = window;
        GraphicsDevice = graphicsDevice;
        Window.Resized += Resize;
        var commandList = GraphicsDevice.ResourceFactory.CreateCommandList();

        ImGuiHandler = new ImGuiHandler(Window, GraphicsDevice, Configuration.DisplayScale);
        ImageHandler = new ImageHandler(GraphicsDevice, ImGuiHandler);
        
        var stopwatch = Stopwatch.StartNew();
        while (Window.Exists)
        {
            var deltaTime = stopwatch.ElapsedTicks / (float) Stopwatch.Frequency;
            if (deltaTime < 1f / Configuration.FpsLimit)
            {
                continue;
            }
            stopwatch.Restart();

            var snapshot = Window.PumpEvents();
            if (!Window.Exists) break;

            ImGuiHandler.Update(deltaTime, snapshot);
            Draw();

            commandList.Begin();
            commandList.SetFramebuffer(GraphicsDevice.MainSwapchain.Framebuffer);
            var forty = 40f / 255f;
            commandList.ClearColorTarget(0, new RgbaFloat(forty, forty, forty, 1f));

            ImGuiHandler.Render(commandList);
            commandList.End();
            GraphicsDevice.SubmitCommands(commandList);
            GraphicsDevice.SwapBuffers(GraphicsDevice.MainSwapchain);
        }
        
        Window.Resized -= Resize;
        GraphicsDevice.WaitForIdle();
        ImageHandler.Dispose();
        ImGuiHandler.Dispose();
        GraphicsDevice.Dispose();
        Window.Close();
        
        Configuration.WindowWidth = Window.Width;
        Configuration.WindowHeight = Window.Height;
        Configuration.WindowX = Window.X;
        Configuration.WindowY = Window.Y;
        Configuration.Save();
        
        logger.LogInformation("Kweh! Kweh!");
    }

    public static async Task Main()
    {
        var program = new Program();
        await program.RunAsync();
    }

    private static Task<SqPackWindow>? SqPackWindowTask;
    private void Draw()
    {
        ImGui.SetNextWindowPos(Vector2.Zero);
        ImGui.SetNextWindowSize(new Vector2(Window.Width, Window.Height));
        if (ImGui.Begin("Main", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.MenuBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove))
        {          
            SettingsWindow.Draw();
            
            if (!string.IsNullOrWhiteSpace(Configuration.GameDirectory))
            {
                SqPackWindowTask ??= Task.Run(() =>
                {
                    var pack = new SqPack(Configuration.GameDirectory);
                    var pathManager = new PathManager(pack, logFactory.CreateLogger<PathManager>());
                    var cacheFile = Path.Combine(DataDirectory, "parsed_paths.txt");
                    pathManager.RunImport(cacheFile);
                    return new SqPackWindow(pack, ImageHandler, pathManager, Configuration, logFactory.CreateLogger<SqPackWindow>());
                });
                if (SqPackWindowTask.IsCompletedSuccessfully)
                {
                    var sqPackWindow = SqPackWindowTask.Result;
                    try
                    {
                        sqPackWindow.Draw();
                    }
                    catch (Exception e)
                    {
                        ImGui.TextWrapped($"Error: {e.Message}");
                        logger.LogError(e, "Error drawing SqPackWindow");
                    }
                }
                else if (SqPackWindowTask.IsFaulted)
                {
                    var exception = SqPackWindowTask.Exception;
                    ImGui.TextWrapped($"Error: {exception?.Message}");
                }
                else
                {
                    ImGui.Text("Loading SqPackWindow...");
                }
            }
        }
    }

    private void Resize()
    {
        var width = Window.Width;
        var height = Window.Height;
        GraphicsDevice.MainSwapchain.Resize((uint)width, (uint)height);
    }
}

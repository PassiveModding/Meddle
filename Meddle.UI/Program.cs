using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using ImGuiNET;
using Meddle.UI.Windows;
using Microsoft.Extensions.Logging;
using Veldrid;
using Veldrid.StartupUtilities;

namespace Meddle.UI;

public class Program
{
    public static readonly string DataDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Meddle");

    private static readonly ILoggerFactory LogFactory = LoggerFactory.Create(builder => builder.AddConsole());
    
    private static void Init()
    {
        if (!Directory.Exists(DataDirectory))
        {
            Directory.CreateDirectory(DataDirectory);
        }

        Services.Configuration = Configuration.Load();
    }

    public static async Task Main()
    {
        Init();

        var logger = LogFactory.CreateLogger<Program>();
        logger.LogInformation("Kweh!");

        VeldridStartup.CreateWindowAndGraphicsDevice(
            new WindowCreateInfo(
                Services.Configuration.WindowX,
                Services.Configuration.WindowY,
                Services.Configuration.WindowWidth,
                Services.Configuration.WindowHeight,
                WindowState.Normal,
                "Meddle"),
            out var window,
            out var graphicsDevice);
        
        if (window is null || graphicsDevice is null) {
            logger.LogError("Failed to create window and graphics device.");
            return;
        }
        
        Services.Window = window;
        Services.GraphicsDevice = graphicsDevice;
        Services.Window.Resized += Resize;
        var commandList = Services.GraphicsDevice.ResourceFactory.CreateCommandList();

        Services.ImGuiHandler = new ImGuiHandler(Services.Window, Services.GraphicsDevice);
        Services.ImageHandler = new ImageHandler();
        
        var stopwatch = Stopwatch.StartNew();
        while (Services.Window.Exists)
        {
            var deltaTime = stopwatch.ElapsedTicks / (float) Stopwatch.Frequency;
            if (deltaTime < 1f / Services.Configuration.FpsLimit)
            {
                continue;
            }
            stopwatch.Restart();

            var snapshot = Services.Window.PumpEvents();
            if (!Services.Window.Exists) break;

            Services.ImGuiHandler.Update(deltaTime, snapshot);
            Draw();

            commandList.Begin();
            commandList.SetFramebuffer(Services.GraphicsDevice.MainSwapchain.Framebuffer);
            var forty = 40f / 255f;
            commandList.ClearColorTarget(0, new RgbaFloat(forty, forty, forty, 1f));

            Services.ImGuiHandler.Render(commandList);
            commandList.End();
            Services.GraphicsDevice.SubmitCommands(commandList);
            Services.GraphicsDevice.SwapBuffers(Services.GraphicsDevice.MainSwapchain);
        }
        
        Services.Window.Resized -= Resize;
        Services.GraphicsDevice.WaitForIdle();
        Services.ImageHandler.Dispose();
        Services.ImGuiHandler.Dispose();
        Services.GraphicsDevice.Dispose();
        Services.Window.Close();
        
        Services.Configuration.WindowWidth = Services.Window.Width;
        Services.Configuration.WindowHeight = Services.Window.Height;
        Services.Configuration.WindowX = Services.Window.X;
        Services.Configuration.WindowY = Services.Window.Y;
        Services.Configuration.Save();
        
        logger.LogInformation("Kweh! Kweh!");
    }

    private static SqPackWindow? SqPackWindow;
    private static void Draw()
    {
        DrawSettings();
        
        if (!string.IsNullOrWhiteSpace(Services.Configuration.GameDirectory))
        {
            try
            {
                SqPackWindow ??= new SqPackWindow();
                SqPackWindow.Draw();
            }
            catch (Exception e)
            {
                ImGui.TextWrapped(e.ToString());
            }
        }
    }

    private static void DrawSettings()
    {
        if (ImGui.Begin("Meddle", ImGuiWindowFlags.AlwaysAutoResize))
        {
            Services.Configuration.GameDirectory ??= string.Empty;
            var gameDirectory = Services.Configuration.GameDirectory;
            if (ImGui.InputText("Game Directory", ref gameDirectory, 1024))
            {
                Services.Configuration.GameDirectory = gameDirectory;
            }
        }
    }
    
    private static void Resize()
    {
        var width = Services.Window.Width;
        var height = Services.Window.Height;
        Services.GraphicsDevice.MainSwapchain.Resize((uint)width, (uint)height);
    }
}

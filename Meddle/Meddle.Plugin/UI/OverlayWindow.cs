using System.Numerics;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using Microsoft.Extensions.Logging;

namespace Meddle.Plugin.UI;

public interface IOverlay
{
    void DrawOverlay();
}

public class OverlayWindow : Window
{
    private readonly ILogger<OverlayWindow> log;
    private readonly IOverlay[] overlays;

    public OverlayWindow(
        ILogger<OverlayWindow> log,
        IEnumerable<IOverlay> overlays)
        : base("##MeddleOverlay",
               ImGuiWindowFlags.NoDecoration |
               ImGuiWindowFlags.NoBackground |
               ImGuiWindowFlags.NoInputs |
               ImGuiWindowFlags.NoSavedSettings |
               ImGuiWindowFlags.NoBringToFrontOnFocus)
    {
        this.log = log;
        this.overlays = overlays.ToArray();
        IsOpen = true;
    }

    public override void PreDraw()
    {
        Size = ImGui.GetIO().DisplaySize;
        Position = Vector2.Zero;
    }

    public override void Draw()
    {
        foreach (var overlay in overlays)
        {
            try
            {
                overlay.DrawOverlay();
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error drawing overlay.");
            }
        }
    }
}

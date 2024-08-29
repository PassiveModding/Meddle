using System.Numerics;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Services;
using Meddle.Plugin.Utils;
using Microsoft.Extensions.Logging;

namespace Meddle.Plugin.UI.Windows;

public class LayoutOverlay : Window
{
    private readonly Configuration config;

    public ParsedInstanceType DrawTypes { get; set; } = ParsedInstanceType.AllSupported;

    private readonly LayoutService layoutService;

    private readonly Queue<ParsedInstance> layoutTabHoveredInstances = new();
    private readonly ILogger<LayoutOverlay> log;
    private readonly SigUtil sigUtil;
    public bool ShouldDraw;

    public LayoutOverlay(
        ILogger<LayoutOverlay> log,
        SigUtil sigUtil,
        LayoutService layoutService,
        Configuration config) : base("##MeddleLayoutOverlay",
                                     ImGuiWindowFlags.NoDecoration |
                                     ImGuiWindowFlags.NoBackground |
                                     ImGuiWindowFlags.NoInputs |
                                     ImGuiWindowFlags.NoSavedSettings |
                                     ImGuiWindowFlags.NoBringToFrontOnFocus)
    {
        this.log = log;
        this.sigUtil = sigUtil;
        this.layoutService = layoutService;
        this.config = config;
    }

    public event Action<ParsedInstance>? OnInstanceClick;

    public void EnqueueLayoutTabHoveredInstance(ParsedInstance instance)
    {
        if (!ShouldDraw)
            return;
        layoutTabHoveredInstances.Enqueue(instance);
    }

    public override void PreDraw()
    {
        Size = ImGui.GetIO().DisplaySize;
        Position = Vector2.Zero;
    }

    public override void Draw()
    {
        if (!ShouldDraw)
            return;
        var currentLayout = layoutService.GetWorldState();
        if (currentLayout == null)
            return;
        var objects = layoutService.ParseObjects();
        var activeHovered = DrawLayers(currentLayout);
        var activeObjects = DrawObjects(objects);
        var allActive = activeHovered.Concat(activeObjects).ToArray();
        if (allActive.Length > 0 && allActive.Any(x => DrawTypes.HasFlag(x.Type)))
        {
            ImGui.BeginTooltip();
            foreach (var instance in allActive)
            {
                DrawTooltip(instance);
            }

            ImGui.EndTooltip();
        }

        while (layoutTabHoveredInstances.Count > 0)
        {
            var instance = layoutTabHoveredInstances.Dequeue();
            // draw line to instance
            var currentPos = sigUtil.GetLocalPosition();
            if (WorldToScreen(instance.Transform.Translation, out var screenPos, out var inView) &&
                WorldToScreen(currentPos, out var currentScreenPos, out var currentInView))
            {
                var bg = ImGui.GetBackgroundDrawList();
                bg.AddLine(currentScreenPos, screenPos, ImGui.GetColorU32(config.WorldDotColor), 2);
            }
        }
    }

    private IList<ParsedInstance> DrawObjects(ParsedInstance[] objects)
    {
        var hovered = new List<ParsedInstance>();
        foreach (var obj in objects)
        {
            if (DrawInstanceOverlay(obj))
            {
                hovered.Add(obj);
            }
        }

        return hovered;
    }

    private bool DrawInstanceOverlay(ParsedInstance obj)
    {
        var localPos = sigUtil.GetLocalPosition();
        if (Vector3.Abs(obj.Transform.Translation - localPos).Length() > config.WorldCutoffDistance)
            return false;
        if (!WorldToScreen(obj.Transform.Translation, out var screenPos, out var inView))
            return false;
        if (!DrawTypes.HasFlag(obj.Type))
            return false;
        if (obj is ParsedCharacterInstance {Visible: false})
            return false;
        
        var screenPosVec = new Vector2(screenPos.X, screenPos.Y);
        var bg = ImGui.GetBackgroundDrawList();

        bg.AddCircleFilled(screenPosVec, 5, ImGui.GetColorU32(config.WorldDotColor));
        if (ImGui.IsMouseHoveringRect(screenPosVec - new Vector2(5, 5),
                                      screenPosVec + new Vector2(5, 5)) &&
            !ImGui.IsWindowHovered(ImGuiHoveredFlags.AnyWindow))
        {
            ImGui.SetNextFrameWantCaptureMouse(true);
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                log.LogInformation("Clicked on {InstanceType} {InstanceId}", obj.Type, obj.Id);
                OnInstanceClick?.Invoke(layoutService.ResolveInstance(obj));
            }
            return true;
        }

        return false;
    }

    private void DrawTooltip(ParsedInstance instance)
    {
        if (!DrawTypes.HasFlag(instance.Type))
            return;
        if (instance is ParsedCharacterInstance {Visible: false})
            return;
        
        ImGui.Text($"Type: {instance.Type}");
        if (instance is ParsedUnsupportedInstance unsupportedInstance)
        {
            ImGui.Text($"Instance Type: {unsupportedInstance.InstanceType}");
        }
        
        ImGui.Text($"Position: {instance.Transform.Translation.ToFormatted()}");
        ImGui.Text($"Rotation: {instance.Transform.Rotation.ToFormatted()}");
        ImGui.Text($"Scale: {instance.Transform.Scale.ToFormatted()}");

        if (instance is ParsedHousingInstance housingInstance)
        {
            ImGui.Text($"Housing: {housingInstance.Name}");
            ImGui.Text($"Kind: {housingInstance.Kind}");
            if (housingInstance.Item != null)
            {
                ImGui.Text($"Item Name: {housingInstance.Item.Name}");
            }

            Vector4? color = housingInstance.Stain == null
                                 ? null
                                 : ImGui.ColorConvertU32ToFloat4(housingInstance.Stain.Color);
            if (color != null)
            {
                ImGui.ColorButton("Stain", color.Value);
            }
            else
            {
                ImGui.Text("No Stain");
            }
        }

        if (instance is ParsedBgPartsInstance bgInstance)
        {
            ImGui.Text($"Path: {bgInstance.Path}");
        }
        
        if (instance is ParsedCharacterInstance characterInstance)
        {
            ImGui.Text($"Character: {characterInstance.Name}");
            ImGui.Text($"Kind: {characterInstance.Kind}");
        }

        // children
        if (instance.Children.Count > 0)
        {
            ImGui.Text("Children:");
            ImGui.Indent();
            foreach (var child in instance.Children)
            {
                DrawTooltip(child);
            }

            ImGui.Unindent();
        }
    }

    private List<ParsedInstance> DrawLayers(ParsedInstance[] instances)
    {
        var hovered = new List<ParsedInstance>();
        foreach (var instance in instances)
        {
            if (DrawInstanceOverlay(instance))
            {
                hovered.Add(instance);
            }
        }

        return hovered;
    }

    private unsafe bool WorldToScreenFallback(Vector3 worldPos, out Vector2 screenPos, out bool inView)
    {
        var currentCamera = sigUtil.GetCamera();

        if (!currentCamera->WorldToScreen(worldPos, out var sPos))
        {
            screenPos = Vector2.Zero;
            inView = false;
            return false;
        }

        screenPos = sPos;
        inView = true;
        return true;
    }

    public unsafe bool WorldToScreen(Vector3 worldPos, out Vector2 screenPos, out bool inView)
    {
        var device = sigUtil.GetDevice();
        var control = sigUtil.GetControl();

        // Read current ViewProjectionMatrix plus game window size
        var windowPos = ImGuiHelpers.MainViewport.Pos;

        Matrix4x4 viewProjectionMatrix;
        if (control->LocalPlayer != null)
        {
            viewProjectionMatrix = control->ViewProjectionMatrix;
        }
        else
        {
            var fallbackResult = WorldToScreenFallback(worldPos, out screenPos, out inView);
            if (!fallbackResult)
            {
                screenPos = Vector2.Zero;
                inView = false;
                return false;
            }

            return true;
        }

        float width = device->Width;
        float height = device->Height;

        var pCoords = Vector4.Transform(new Vector4(worldPos, 1.0f), viewProjectionMatrix);
        var inFront = pCoords.W > 0.0f;

        if (Math.Abs(pCoords.W) < float.Epsilon)
        {
            screenPos = Vector2.Zero;
            inView = false;
            return false;
        }

        pCoords *= MathF.Abs(1.0f / pCoords.W);
        screenPos = new Vector2(pCoords.X, pCoords.Y);

        screenPos.X = (0.5f * width * (screenPos.X + 1f)) + windowPos.X;
        screenPos.Y = (0.5f * height * (1f - screenPos.Y)) + windowPos.Y;

        inView = inFront &&
                 screenPos.X > windowPos.X && screenPos.X < windowPos.X + width &&
                 screenPos.Y > windowPos.Y && screenPos.Y < windowPos.Y + height;

        return inFront;
    }
}

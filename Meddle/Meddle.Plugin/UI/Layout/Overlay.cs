using System.Numerics;
using ImGuiNET;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Utils;

namespace Meddle.Plugin.UI.Layout;

public partial class LayoutWindow
{
    private void DrawOverlayWindow(out List<ParsedInstance> hovered, out List<ParsedInstance> selected)
    {
        hovered = [];
        selected = [];
        
        ImGui.SetNextWindowPos(Vector2.Zero);
        ImGui.SetNextWindowSize(ImGui.GetIO().DisplaySize);
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration |
                                       ImGuiWindowFlags.NoBackground |
                                       ImGuiWindowFlags.NoInputs |
                                       ImGuiWindowFlags.NoSavedSettings |
                                       ImGuiWindowFlags.NoBringToFrontOnFocus;
        try
        {
            if (ImGui.Begin("##LayoutOverlay", flags))
            {
                DrawLayers(currentLayout, out hovered, out selected);
                if (hovered.Count > 0 && hovered.Any(x => drawTypes.HasFlag(x.Type)))
                {
                    ImGui.BeginTooltip();
                    foreach (var instance in hovered)
                    {
                        DrawTooltip(instance);
                    }
                    ImGui.EndTooltip();
                }

                if (traceToHovered)
                {
                    var first = hovered.FirstOrDefault();
                    if (first != null)
                    {
                        if (WorldToScreen(first.Transform.Translation, out var screenPos, out var inView) &&
                            WorldToScreen(currentPos, out var currentScreenPos, out var currentInView))
                        {
                            var bg = ImGui.GetBackgroundDrawList();
                            bg.AddLine(currentScreenPos, screenPos, ImGui.GetColorU32(config.WorldDotColor), 2);
                        }
                    }
                }
                
            }
        } 
        finally
        {
            ImGui.End();
        }
    }
    
    private void DrawLayers(ParsedInstance[] instances, out List<ParsedInstance> hovered, out List<ParsedInstance> selected)
    {
        hovered = new List<ParsedInstance>();
        selected = new List<ParsedInstance>();
        foreach (var instance in instances)
        {
            var state = DrawInstanceOverlay(instance);
            if (state == InstanceSelectState.Hovered)
            {
                hovered.Add(instance);
            }
            else if (state == InstanceSelectState.Selected)
            {
                selected.Add(instance);
            }
        }
    }
    
    private void DrawTooltip(ParsedInstance instance)
    {
        if (!drawTypes.HasFlag(instance.Type))
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

        if (instance is ParsedLightInstance lightInstance)
        {
            ImGui.ColorButton("Color", lightInstance.Color);
        }

        if (instance is IPathInstance pathInstance)
        {
            ImGui.Text($"Path: {pathInstance.Path.FullPath}");
            if (pathInstance.Path.GamePath != pathInstance.Path.FullPath)
            {
                ImGui.Text($"Game Path: {pathInstance.Path.GamePath}");
            }
        }
        
        if (instance is ParsedCharacterInstance characterInstance)
        {
            ImGui.Text($"Character: {characterInstance.Name}");
            ImGui.Text($"Kind: {characterInstance.Kind}");
        }

        // children
        if (instance is ParsedSharedInstance { Children.Count: > 0 } shared)
        {
            ImGui.Text("Children:");
            ImGui.Indent();
            foreach (var child in shared.Children)
            {
                DrawTooltip(child);
            }

            ImGui.Unindent();
        }
    }

    private enum InstanceSelectState
    {
        Hovered,
        Selected,
        None
    }
    
    private InstanceSelectState DrawInstanceOverlay(ParsedInstance obj)
    {
        var localPos = sigUtil.GetLocalPosition();
        if (Vector3.Abs(obj.Transform.Translation - localPos).Length() > config.WorldCutoffDistance)
            return InstanceSelectState.None;
        if (!WorldToScreen(obj.Transform.Translation, out var screenPos, out var inView))
            return InstanceSelectState.None;
        if (!drawTypes.HasFlag(obj.Type))
            return InstanceSelectState.None;
        if (obj is ParsedCharacterInstance {Visible: false})
            return InstanceSelectState.None;

        if (obj is ParsedSharedInstance sharedInstance)
        {
            var flattened = sharedInstance.Flatten();
            // if none of the children are in drawTypes, return
            if (!flattened.Where(x => x is not ParsedSharedInstance).Any(x => drawTypes.HasFlag(x.Type)))
                return InstanceSelectState.None;
        }
        
        var screenPosVec = new Vector2(screenPos.X, screenPos.Y);
        var bg = ImGui.GetBackgroundDrawList();

        bg.AddCircleFilled(screenPosVec, 5, ImGui.GetColorU32(config.WorldDotColor));
        if (ImGui.IsMouseHoveringRect(screenPosVec - new Vector2(5, 5), screenPosVec + new Vector2(5, 5)) &&
            !ImGui.IsWindowHovered(ImGuiHoveredFlags.AnyWindow))
        {
            ImGui.SetNextFrameWantCaptureMouse(true);
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                return InstanceSelectState.Selected;
            }
            return InstanceSelectState.Hovered;
        }

        return InstanceSelectState.None;
    }
}

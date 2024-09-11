using System.Numerics;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Models.Structs;
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
                DrawLayers(currentLayout, null, out hovered, out selected);
            }
        } 
        finally
        {
            ImGui.End();
        }
    }
    
    private void DrawLayers(ParsedInstance[] instances, ParsedInstance? parent, out List<ParsedInstance> hovered, out List<ParsedInstance> selected)
    {
        hovered = new List<ParsedInstance>();
        selected = new List<ParsedInstance>();
        foreach (var instance in instances)
        {
            var state = DrawInstanceOverlay(instance, parent);
            if (state == InstanceSelectState.Hovered)
            {
                hovered.Add(instance);
            }
            else if (state == InstanceSelectState.Selected)
            {
                selected.Add(instance);
            }

            if (instance is ParsedSharedInstance shared && drawChildren)
            {
                foreach (var child in shared.Children)
                {
                    DrawLayers([child], shared, out var childHovered, out var childSelected);
                    hovered.AddRange(childHovered);
                }
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
            ImGui.Text($"Light Type: {lightInstance.Light.LightType}");
            ImGui.ColorButton("Color", new Vector4(lightInstance.Light.Color.Rgb, lightInstance.Light.Color.Intensity));
            ImGui.SameLine();
            ImGui.Text($"HDR: {lightInstance.Light.Color._vec3.ToFormatted()}");
            ImGui.SameLine();
            ImGui.Text($"RGB: {lightInstance.Light.Color.Rgb.ToFormatted()}");
            ImGui.SameLine();
            ImGui.Text($"Intensity: {lightInstance.Light.Color.Intensity}");
            ImGui.Text($"Range: {lightInstance.Light.Range}");
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
    
    private InstanceSelectState DrawInstanceOverlay(ParsedInstance obj, ParsedInstance? parent)
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

        var dotColor = config.WorldDotColor;
        if (selectedInstances.ContainsKey(obj.Id) || (parent != null && selectedInstances.ContainsKey(parent.Id)))
        {
            dotColor = new Vector4(1f, 1f, 1f, 0.5f);
        }
        
        // if obj is light instance, draw a short line in the direction of the light
        if (obj is ParsedLightInstance light)
        {
            dotColor = new Vector4(light.Light.Color.Rgb, 0.5f);

            if (light.Light.LightType != LightType.PointLight)
            {
                var range = Math.Min(light.Light.Range, 1);
                var lightDir = Vector3.Transform(new Vector3(0, 0, range), obj.Transform.Rotation);
                WorldToScreen(obj.Transform.Translation + lightDir, out var endPos, out _);
                bg.AddLine(screenPosVec, endPos, ImGui.GetColorU32(dotColor), 2);
            }
            
            bg.AddCircle(screenPosVec, 5.1f, ImGui.GetColorU32(config.WorldDotColor));
        }
        
        bg.AddCircleFilled(screenPosVec, 5, ImGui.GetColorU32(dotColor));
        
        if (ImGui.IsMouseHoveringRect(screenPosVec - new Vector2(5, 5), screenPosVec + new Vector2(5, 5)) &&
            !ImGui.IsWindowHovered(ImGuiHoveredFlags.AnyWindow))
        {
            if (traceToHovered)
            {
                if (WorldToScreen(currentPos, out var currentScreenPos, out var currentInView))
                {
                    bg.AddLine(currentScreenPos, screenPos, ImGui.GetColorU32(config.WorldDotColor), 2);
                }
            }
            
            if (drawChildren && traceToParent && parent != null)
            {
                if (WorldToScreen(parent.Transform.Translation, out var parentScreenPos, out var parentInView))
                {
                    bg.AddLine(screenPos, parentScreenPos, ImGui.GetColorU32(config.WorldDotColor), 2);
                }
            }
            
            using (var tt = ImRaii.Tooltip())
            {
                DrawTooltip(obj);
            }
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

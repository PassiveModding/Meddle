using System.Numerics;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using Dalamud.Bindings.ImGui;
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

            if (instance is ParsedSharedInstance shared && config.LayoutConfig.DrawChildren)
            {
                foreach (var child in shared.Children)
                {
                    DrawLayers([child], shared, out var childHovered, out _);
                    hovered.AddRange(childHovered);
                }
            }
        }
    }
    
    private void DrawTooltip(ParsedInstance instance, bool extras = true)
    {
        if (!config.LayoutConfig.DrawTypes.HasFlag(instance.Type))
            return;
        if (instance is ParsedCharacterInstance {Visible: false})
            return;
        
        if (extras)
        {
            if (instance is IPathInstance pathInstance)
            {
                ImGui.Text($"Path: {pathInstance.Path.FullPath}");
                if (pathInstance.Path.GamePath != pathInstance.Path.FullPath)
                {
                    ImGui.Text($"Game Path: {pathInstance.Path.GamePath}");
                }
            }
        }
        
        ImGui.Text($"Pos: {instance.Transform.Translation.ToFormatted()} Rot: {instance.Transform.Rotation.ToFormatted()} Scale: {instance.Transform.Scale.ToFormatted()}");

        if (extras)
        {
            if (instance is ParsedUnsupportedInstance unsupportedInstance)
            {
                ImGui.Text($"Instance Type: {unsupportedInstance.InstanceType}");
            }

            if (instance is ParsedHousingInstance housingInstance)
            {
                ImGui.Text($"Housing: {housingInstance.Name}");
                ImGui.Text($"Kind: {housingInstance.Kind}");

                if (housingInstance.Stain != null)
                {
                    ImGui.Text($"Stain ({housingInstance.Stain.RowId})");
                    ImGui.SameLine();
                    ImGui.ColorButton("Stain", housingInstance.Stain.Color);
                }
                else
                {
                    ImGui.Text($"Stain (Default:{housingInstance.DefaultStain.RowId})");
                    ImGui.SameLine();
                    ImGui.ColorButton("Stain", housingInstance.DefaultStain.Color);
                }
            }

            if (instance is ParsedLightInstance lightInstance)
            {
                ImGui.Text($"Light Type: {lightInstance.Light.LightType}");
                ImGui.ColorButton(
                    "Color", new Vector4(lightInstance.Light.Color.Rgb, lightInstance.Light.Color.Intensity));
                ImGui.SameLine();
                ImGui.Text($"HDR: {lightInstance.Light.Color._vec3.ToFormatted()}");
                ImGui.SameLine();
                ImGui.Text($"RGB: {lightInstance.Light.Color.Rgb.ToFormatted()}");
                ImGui.SameLine();
                ImGui.Text($"Intensity: {lightInstance.Light.Color.Intensity}");
                ImGui.Text($"Range: {lightInstance.Light.Range}");
            }

            if (instance is ParsedCharacterInstance characterInstance)
            {
                ImGui.Text($"Character: {characterInstance.Name}");
                ImGui.Text($"Kind: {characterInstance.Kind}");
            }
        }

        // children
        if (instance is ParsedSharedInstance { Children.Count: > 0 } shared)
        {
            var groupedChildren = shared.Children.GroupBy(x =>
            {
                var type = x.Type;
                
                if (x is IPathInstance parsedInstance)
                {
                    return (type, parsedInstance.Path.FullPath);
                }

                return (type, "");
            }).Where(x => config.LayoutConfig.DrawTypes.HasFlag(x.Key.type));

            using var groupIndent = ImRaii.PushIndent();
            foreach (var childGroup in groupedChildren)
            {
                using var childIndent = ImRaii.PushIndent();
                var childArray = childGroup.ToArray();
                ImGui.Text($"{childGroup.Key.type}: {childGroup.Count()}");
                for (var i = 0; i < childArray.Length; i++)
                {
                    var child = childArray[i];
                    DrawTooltip(child, i == 0);
                }
            }
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
        if (obj is ParsedCameraInstance cameraInstance || 
            obj is ParsedEnvLightInstance envLightInstance)
        {
            // don't draw these instances
            return InstanceSelectState.None;
        }
        
        if (Vector3.Abs(obj.Transform.Translation - searchOrigin).Length() > config.LayoutConfig.WorldCutoffDistance)
            return InstanceSelectState.None;
        if (!WorldToScreen(obj.Transform.Translation, out var screenPos, out var inView))
            return InstanceSelectState.None;
        if (!config.LayoutConfig.DrawTypes.HasFlag(obj.Type))
            return InstanceSelectState.None;
        if (obj is ParsedCharacterInstance {Visible: false})
            return InstanceSelectState.None;

        if (obj is ISearchableInstance searchable)
        {
            if (!searchable.Search(search))
                return InstanceSelectState.None;
        }

        if (parent != null)
        {
            if (parent is ParsedHousingInstance hi)
            {
                // if housing not in flags, ignore
                if (!config.LayoutConfig.DrawTypes.HasFlag(ParsedInstanceType.Housing))
                    return InstanceSelectState.None;
            }
        }

        if (obj is ParsedSharedInstance sharedInstance)
        {
            var objArr = sharedInstance.Flatten();
            // if none of the children are in drawTypes, return
            if (!objArr.Where(x => x is not ParsedSharedInstance).Any(x => config.LayoutConfig.DrawTypes.HasFlag(x.Type)))
                return InstanceSelectState.None;
        }

        var screenPosVec = new Vector2(screenPos.X, screenPos.Y);
        var bg = ImGui.GetBackgroundDrawList();

        var dotColor = config.LayoutConfig.WorldDotColor;
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
            
            bg.AddCircle(screenPosVec, 5.1f, ImGui.GetColorU32(config.LayoutConfig.WorldDotColor));
        }
        
        bg.AddCircleFilled(screenPosVec, 5, ImGui.GetColorU32(dotColor));
        
        if (ImGui.IsMouseHoveringRect(screenPosVec - new Vector2(5, 5), screenPosVec + new Vector2(5, 5)) &&
            !ImGui.IsWindowHovered(ImGuiHoveredFlags.AnyWindow))
        {
            if (config.LayoutConfig.TraceToHovered)
            {
                if (WorldToScreen(playerPosition, out var currentScreenPos, out _))
                {
                    bg.AddLine(currentScreenPos, screenPos, ImGui.GetColorU32(config.LayoutConfig.WorldDotColor), 2);
                }
            }
            
            if (config.LayoutConfig is {DrawChildren: true, TraceToParent: true} && parent != null)
            {
                if (WorldToScreen(parent.Transform.Translation, out var parentScreenPos, out _))
                {
                    bg.AddLine(screenPos, parentScreenPos, ImGui.GetColorU32(config.LayoutConfig.WorldDotColor), 2);
                }
            }
            
            using (ImRaii.Tooltip())
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

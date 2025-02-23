using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Utils;

namespace Meddle.Plugin.UI.Layout;

public partial class LayoutWindow
{
    private const ParsedInstanceType DefaultDrawTypes = ParsedInstanceType.Character |
                                                        ParsedInstanceType.Housing |
                                                        ParsedInstanceType.Terrain |
                                                        ParsedInstanceType.BgPart |
                                                        ParsedInstanceType.Light |
                                                        ParsedInstanceType.SharedGroup;

    public class LayoutConfig
    {
        public ParsedInstanceType DrawTypes { get; set; } = DefaultDrawTypes;
        public bool DrawOverlay { get; set; } = true;
        public bool DrawChildren { get; set; }
        public bool TraceToParent { get; set; } = true;
        public bool OrderByDistance { get; set; } = true;
        public bool TraceToHovered { get; set; } = true;
        public bool HideOffscreenCharacters { get; set; } = true;
        public bool ExcludeParented { get; set; } = true;
        public int MaxItemCount { get; set; } = 100;
        public OriginAdjustment OriginAdjustment { get; set; } = OriginAdjustment.Camera;
    }
    
    public enum OriginAdjustment
    {
        Player,
        Camera,
        Origin
    }
    

    private void DrawOptions()
    {
        if (!ImGui.CollapsingHeader("Options")) return;
        var cutoff = config.WorldCutoffDistance;
        if (ImGui.DragFloat("Cutoff Distance", ref cutoff, 1, 0, 10000))
        {
            config.WorldCutoffDistance = cutoff;
            config.Save();
        }

        var dotColor = config.WorldDotColor;
        if (ImGui.ColorEdit4("Dot Color", ref dotColor, ImGuiColorEditFlags.NoInputs))
        {
            config.WorldDotColor = dotColor;
            config.Save();
        }

        var drawOverlay = config.LayoutConfig.DrawOverlay;
        if (ImGui.Checkbox("Draw Overlay", ref drawOverlay))
        {
            config.LayoutConfig.DrawOverlay = drawOverlay;
            config.Save();
        }
        
        var drawChildren = config.LayoutConfig.DrawChildren;
        if (ImGui.Checkbox("Draw Children", ref drawChildren))
        {
            config.LayoutConfig.DrawChildren = drawChildren;
            config.Save();
        }
        
        var traceToParent = config.LayoutConfig.TraceToParent;
        if (drawChildren && ImGui.Checkbox("Trace to Parent", ref traceToParent))
        {
            config.LayoutConfig.TraceToParent = traceToParent;
            config.Save();
        }
        
        var orderByDistance = config.LayoutConfig.OrderByDistance;
        if (ImGui.Checkbox("Order by Distance", ref orderByDistance))
        {
            config.LayoutConfig.OrderByDistance = orderByDistance;
            config.Save();
        }
        
        var traceToHovered = config.LayoutConfig.TraceToHovered;
        if (ImGui.Checkbox("Trace to Hovered", ref traceToHovered))
        {
            config.LayoutConfig.TraceToHovered = traceToHovered;
            config.Save();
        }

        ImGui.Text($"Current Origin: (X:{currentPos.X:F2}, Y:{currentPos.Y:F2}, Z{currentPos.Z:F2})");
        var originAdjustment = config.LayoutConfig.OriginAdjustment;
        if (EnumExtensions.DrawEnumDropDown("Origin", ref originAdjustment))
        {
            config.LayoutConfig.OriginAdjustment = originAdjustment;
            config.Save();
        }
        
        ImGui.SameLine();
        UiUtil.HintCircle("The origin point for layout item searches\n" +
                          "Player: The player's position at the time of export\n" +
                          "Camera: The camera's position at the time of export\n" +
                          "Origin: The object's origin point\n" +
                          "NOTE: Recommended selection is Camera, as this will ensure that objects in cutscenes are still within range.");
        
        // var adjustOrigin = config.LayoutConfig.AdjustOrigin;
        // if (ImGui.Checkbox("Adjust Origin", ref adjustOrigin))
        // {
        //     config.LayoutConfig.AdjustOrigin = adjustOrigin;
        //     config.Save();
        // }
        // ImGui.SameLine();
        // UiUtil.HintCircle("Subtracts the players position at the time of export from the position of the object, this will mean " +
        //                   "that the object will be centered around the player when exported, " +
        //                   "but may cause issues if exporting multiple components separately if the player moves between exports");
        
        
        var hideOffscreenCharacters = config.LayoutConfig.HideOffscreenCharacters;
        if (ImGui.Checkbox("Hide Offscreen Characters", ref hideOffscreenCharacters))
        {
            config.LayoutConfig.HideOffscreenCharacters = hideOffscreenCharacters;
            config.Save();
        }
        
        // var excludeParented = config.LayoutConfig.ExcludeParented;
        // if (ImGui.Checkbox("Exclude Parented", ref excludeParented))
        // {
        //     config.LayoutConfig.ExcludeParented = excludeParented;
        //     config.Save();
        // }
        //
        // ImGui.SameLine();
        // UiUtil.HintCircle("Excludes objects that are parented to other objects\n" +
        //                   "For example:\n" +
        //                   " - Player mounts exist as an attachment to the player as well as a separate object, this will prevent them from being duplicated\n");
        
        var maxItemCount = config.LayoutConfig.MaxItemCount;
        if (ImGui.DragInt("Max Item Count", ref maxItemCount, 1, 1, 50000))
        {
            config.LayoutConfig.MaxItemCount = maxItemCount;
            config.Save();
        }
        
        var drawTypes = config.LayoutConfig.DrawTypes;
        if (ImGui.BeginCombo("Draw Types", drawTypes.ToString()))
        {
            foreach (var type in Enum.GetValues<ParsedInstanceType>())
            {
                var selected = drawTypes.HasFlag(type);
                using var style = ImRaii.PushStyle(ImGuiStyleVar.Alpha, selected ? 1 : 0.5f);
                if (ImGui.Selectable(type.ToString(), selected, ImGuiSelectableFlags.DontClosePopups))
                {
                    if (selected)
                    {
                        drawTypes &= ~type;
                    }
                    else
                    {
                        drawTypes |= type;
                    }
                }
            }

            ImGui.EndCombo();
        }
        
        if (drawTypes == 0)
        {
            drawTypes = DefaultDrawTypes;
        }
        
        if (drawTypes != config.LayoutConfig.DrawTypes)
        {
            config.LayoutConfig.DrawTypes = drawTypes;
            config.Save();
        }
    }
    
    // private static T[] GetFlags<T>(T flags) where T : Enum
    // {
    //     var list = new List<T>();
    //     foreach (T type in Enum.GetValues(typeof(T)))
    //     {
    //         if (flags.HasFlag(type))
    //         {
    //             list.Add(type);
    //         }
    //     }
    //
    //     return list.ToArray();
    // }
}

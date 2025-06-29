using System.Numerics;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Utils;

namespace Meddle.Plugin.UI.Layout;

public partial class LayoutWindow
{
    public const ParsedInstanceType DefaultDrawTypes = ParsedInstanceType.Character |
                                                       ParsedInstanceType.Housing |
                                                       ParsedInstanceType.Terrain |
                                                       ParsedInstanceType.BgPart |
                                                       ParsedInstanceType.Light |
                                                       ParsedInstanceType.SharedGroup |
                                                       ParsedInstanceType.Camera |
                                                       ParsedInstanceType.Decal |
                                                       ParsedInstanceType.EnvLighting;

    public class LayoutConfig
    {
        // Overlay options
        public ParsedInstanceType DrawTypes { get; set; } = DefaultDrawTypes;
        public bool DrawOverlay { get; set; } = true;
        public bool DrawChildren { get; set; }
        public bool TraceToParent { get; set; } = true;
        public bool OrderByDistance { get; set; } = true;
        public bool TraceToHovered { get; set; } = true;
        public float WorldCutoffDistance { get; set; } = 100f;
        public Vector4 WorldDotColor { get; set; } = new(1f, 1f, 1f, 0.5f);
        public bool IncludeSharedGroupsWhereSubItemsAreWithinRange { get; set; } = true;
        public bool HideOffscreenCharacters { get; set; } = true;
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
        var cutoff = config.LayoutConfig.WorldCutoffDistance;
        if (ImGui.DragFloat("Cutoff Distance", ref cutoff, 1, 0, 10000))
        {
            config.LayoutConfig.WorldCutoffDistance = cutoff;
            config.Save();
        }

        var dotColor = config.LayoutConfig.WorldDotColor;
        if (ImGui.ColorEdit4("Dot Color", ref dotColor, ImGuiColorEditFlags.NoInputs))
        {
            config.LayoutConfig.WorldDotColor = dotColor;
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
        
        var includeSharedGroupsWhereSubItemsAreVisible = config.LayoutConfig.IncludeSharedGroupsWhereSubItemsAreWithinRange;
        if (ImGui.Checkbox("Include Shared Groups Where Sub Items Are Visible", ref includeSharedGroupsWhereSubItemsAreVisible))
        {
            config.LayoutConfig.IncludeSharedGroupsWhereSubItemsAreWithinRange = includeSharedGroupsWhereSubItemsAreVisible;
            config.Save();
        }
        
        ImGui.SameLine();
        UiUtil.HintCircle("If enabled, shared groups will be included in the layout if any of their sub items are visible.\n" +
                          "This may mean that complex shared groups will be included in the layout, even if only a small subset of the items are within the cutoff distance.\n" +
                          "If disabled, shared groups will only be included if the shared group origin is within the cutoff distance.");
        
        var traceToParent = config.LayoutConfig.TraceToParent;
        if (drawChildren && ImGui.Checkbox("Trace to Parent", ref traceToParent))
        {
            config.LayoutConfig.TraceToParent = traceToParent;
            config.Save();
        }
        
        var traceToHovered = config.LayoutConfig.TraceToHovered;
        if (ImGui.Checkbox("Trace to Hovered", ref traceToHovered))
        {
            config.LayoutConfig.TraceToHovered = traceToHovered;
            config.Save();
        }

        ImGui.Text($"Current Origin: (X:{searchOrigin.X:F2}, Y:{searchOrigin.Y:F2}, Z{searchOrigin.Z:F2})");
        var originAdjustment = config.LayoutConfig.OriginAdjustment;
        if (EnumExtensions.DrawEnumDropDown("Origin", ref originAdjustment))
        {
            config.LayoutConfig.OriginAdjustment = originAdjustment;
            config.Save();
        }
        
        ImGui.SameLine();
        UiUtil.HintCircle("The origin point for layout display (does not affect exports)\n" +
                          "Player: The player's position\n" +
                          "Camera: The camera's position\n" +
                          "Origin: 0,0,0");

        if (config.LayoutConfig.OriginAdjustment != OriginAdjustment.Camera)
        {
            ImGui.TextColored(new Vector4(1, 0, 0, 1), "WARNING: Using Player or Origin as the origin point may cause issues with cutscenes");
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
        
        // Search options
        
        ImGui.Separator();
        
        var hideOffscreenCharacters = config.LayoutConfig.HideOffscreenCharacters;
        if (ImGui.Checkbox("Hide Offscreen Characters", ref hideOffscreenCharacters))
        {
            config.LayoutConfig.HideOffscreenCharacters = hideOffscreenCharacters;
            config.Save();
        }
        
        var orderByDistance = config.LayoutConfig.OrderByDistance;
        if (ImGui.Checkbox("Order by Distance", ref orderByDistance))
        {
            config.LayoutConfig.OrderByDistance = orderByDistance;
            config.Save();
        }

        var maxItemCount = config.LayoutConfig.MaxItemCount;
        if (ImGui.DragInt("Max Item Count", ref maxItemCount, 1, 1, 50000))
        {
            config.LayoutConfig.MaxItemCount = maxItemCount;
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

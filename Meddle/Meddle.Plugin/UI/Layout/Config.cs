using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using Meddle.Plugin.Models.Layout;

namespace Meddle.Plugin.UI.Layout;

public partial class LayoutWindow
{
    [Flags]
    public enum ExportType
    {
        // ReSharper disable InconsistentNaming
        GLTF = 1,
        GLB = 2,
        OBJ = 4
        // ReSharper restore InconsistentNaming
    }

    private const ParsedInstanceType DefaultDrawTypes = ParsedInstanceType.Character | ParsedInstanceType.Housing | ParsedInstanceType.Terrain | ParsedInstanceType.BgPart | ParsedInstanceType.SharedGroup;
    private const ExportType DefaultExportType = ExportType.GLTF;
    private ParsedInstanceType drawTypes = DefaultDrawTypes;
    private ExportType exportType = DefaultExportType;
    private bool drawOverlay = true;
    private bool orderByDistance = true;
    private bool traceToHovered = true;
    private bool hideOffscreenCharacters = true;
    private int maxItemCount = 100;
    private bool bakeTextures = true;
    
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

        ImGui.Checkbox("Draw Overlay", ref drawOverlay);
        ImGui.Checkbox("Order by Distance", ref orderByDistance);
        ImGui.Checkbox("Trace to Hovered", ref traceToHovered);
        ImGui.Checkbox("Hide Offscreen Characters", ref hideOffscreenCharacters);
        ImGui.Checkbox("Bake Textures", ref bakeTextures);
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Computes some properties of textures before exporting them, will increase export time significantly");
        }
        
        ImGui.DragInt("Max Item Count", ref maxItemCount, 1, 1, 50000);
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("The maximum number of items to draw in the layout window, does not affect exports");
        }

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
        
        if (ImGui.BeginCombo("Export Type", exportType.ToString()))
        {
            foreach (var type in Enum.GetValues<ExportType>())
            {
                var selected = exportType.HasFlag(type);
                using var style = ImRaii.PushStyle(ImGuiStyleVar.Alpha, selected ? 1 : 0.5f);
                if (ImGui.Selectable(type.ToString(), selected, ImGuiSelectableFlags.DontClosePopups))
                {
                    if (selected)
                    {
                        exportType &= ~type;
                    }
                    else
                    {
                        exportType |= type;
                    }
                }
            }

            ImGui.EndCombo();
        }
        
        if (exportType == 0)
        {
            exportType = DefaultExportType;
        }
    }
    
    private static T[] GetFlags<T>(T flags) where T : Enum
    {
        var list = new List<T>();
        foreach (T type in Enum.GetValues(typeof(T)))
        {
            if (flags.HasFlag(type))
            {
                list.Add(type);
            }
        }

        return list.ToArray();
    }
}

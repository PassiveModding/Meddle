using System.ComponentModel;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;

namespace Meddle.Plugin.Models;

public enum MenuType
{
    Default = 0,
    Debug = 1,
    Testing = 2,
}

[Flags]
public enum ExportType
{
    // ReSharper disable InconsistentNaming
    GLTF = 1,
    GLB = 2,
    OBJ = 4
    // ReSharper restore InconsistentNaming
}

public enum TextureMode
{
    Bake,
    Raw
}

[Flags]
public enum CacheFileType
{
    [Description(".tex")]
    Tex = 1,
    [Description(".mtrl")]
    Mtrl = 2,
    [Description(".mdl")]
    Mdl = 4,
    [Description(".shpk")]
    Shpk = 8,
    [Description(".pbd")]
    Pbd = 16
}

public static class EnumExtensions
{
    private static string GetSingularDescription(this Enum value)
    {
        var field = value.GetType().GetField(value.ToString());
        if (field == null) return value.ToString();
        var attribute = (DescriptionAttribute?)Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute));
        return attribute?.Description ?? value.ToString();
    }
    
    public static string GetDescription(this Enum value)
    {
        // if multiple flags, return all descriptions
        if (value.GetType().GetCustomAttributes(typeof(FlagsAttribute), false).Length > 0)
        {
            var values = Enum.GetValues(value.GetType());
            var descriptions = new List<string>();
            foreach (Enum enumValue in values)
            {
                if (value.HasFlag(enumValue))
                {
                    descriptions.Add(enumValue.GetSingularDescription());
                }
            }

            return string.Join(", ", descriptions);
        }
        
        return value.GetSingularDescription();
    }

    public static bool DrawEnumCombo<TEnum>(string label, ref TEnum value) where TEnum : struct, Enum
    {
        bool changed = false;
        if (ImGui.BeginCombo(label, value.GetDescription()))
        {
            foreach (TEnum enumValue in Enum.GetValues<TEnum>())
            {
                var selected = value.HasFlag(enumValue);
                using var style = ImRaii.PushStyle(ImGuiStyleVar.Alpha, selected ? 1 : 0.5f);
                if (ImGui.Selectable(enumValue.GetDescription(), selected, ImGuiSelectableFlags.DontClosePopups))
                {
                    changed = true;
                    if (selected)
                    {
                        value = (TEnum)(object)(((int)(object)value) & ~(int)(object)enumValue);
                    }
                    else
                    {
                        value = (TEnum)(object)(((int)(object)value) | (int)(object)enumValue);
                    }
                }
            }

            ImGui.EndCombo();
        }
        
        return changed;
    }
}

using System.Runtime.InteropServices;
using System.Text;
using Meddle.Utils.Files;
using Meddle.Utils.Files.Structs.Material;

namespace Meddle.Utils.Helpers;

public static class MaterialUtils
{
    public static string GetShaderPackageName(this MtrlFile file)
    {
        var strings = file.GetStrings();
        return strings[file.FileHeader.ShaderPackageNameOffset];
    }
    
    public static Dictionary<int, string> GetStrings(this MtrlFile file)
    {
        var strings = new Dictionary<int, string>();
        var stringReader = new SpanBinaryReader(file.Strings);
        var offset = 0;
        while (offset < file.Strings.Length)
        {
            var str = stringReader.ReadByteString(offset);
            strings.Add(offset, Encoding.UTF8.GetString(str));
            offset += str.Length + 1;
        }

        return strings;
    }
    
    public static Dictionary<ushort, string> GetTexturePaths(this MtrlFile file)
    {
        var strings = file.GetStrings();
        var texturePaths = new Dictionary<ushort, string>();
        var textureOffsets = file.TextureOffsets.Select(x => x.Offset).Distinct();
        foreach (var offset in textureOffsets)
        {
            var path = strings[offset];
            texturePaths.Add(offset, path);
        }
        return texturePaths;
    }
    
    public static Dictionary<ushort, string> GetUvColorSetStrings(this MtrlFile file)
    {
        var strings = file.GetStrings();
        var uvColorSetStrings = new Dictionary<ushort, string>();
        var uvColorSetOffsets = file.UvColorSets.Select(x => x.NameOffset).Distinct();
        foreach (var offset in uvColorSetOffsets)
        {
            var str = strings[offset];
            uvColorSetStrings.Add(offset, str);
        }
        return uvColorSetStrings;
    }
    
    public static Dictionary<ushort, string> GetColorSetStrings(this MtrlFile file)
    {
        var strings = file.GetStrings();
        var colorSetStrings = new Dictionary<ushort, string>();
        var colorSetOffsets = file.ColorSets.Select(x => x.NameOffset).Distinct();
        foreach (var offset in colorSetOffsets)
        {
            var str = strings[offset];
            colorSetStrings.Add(offset, str);
        }
        return colorSetStrings;
    }

    public static IColorTableSet? GetColorTable(this MtrlFile file)
    {
        if (file.FileHeader.DataSetSize == 0)
        {
            return null;
        }

        if (!file.HasTable)
        {
            return null;
        }
        
        var dataSetReader = new SpanBinaryReader(file.DataSet);
        return file.LargeColorTable switch
        {
            true => new ColorTableSet
            {
                ColorTable = new ColorTable(ref dataSetReader),
                ColorDyeTable = file.HasDyeTable ? new ColorDyeTable(ref dataSetReader) : null
            },
            false => new LegacyColorTableSet
            {
                ColorTable = new LegacyColorTable(ref dataSetReader),
                ColorDyeTable = file.HasDyeTable ? new LegacyColorDyeTable(ref dataSetReader) : null
            }
        };
    }
    
    public static Dictionary<uint, float[]> GetConstants(this MtrlFile file)
    {
        var constants = new Dictionary<uint, float[]>();
        foreach (var constant in file.Constants)
        {
            var id = constant.ConstantId;
            var index = constant.ValueOffset / 4;
            var count = constant.ValueSize / 4;
            var buf = new List<uint>(128);
            for (var j = 0; j < count; j++)
            {
                if (file.ShaderValues.Length <= index + j)
                {
                    throw new Exception($"Constant 0x{id:X8} value out of bounds, offset {index + j}, size {file.ShaderValues.Length}");
                }

                var value = file.ShaderValues[index + j];
                buf.Add(value);
            }

            // even if duplicate, last probably takes precedence
            constants[id] = MemoryMarshal.Cast<uint, float>(buf.ToArray()).ToArray();
        }
        return constants;
    }
}

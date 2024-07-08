using System.Text;
using Meddle.Utils.Files;

namespace Meddle.Utils.Models;

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
}

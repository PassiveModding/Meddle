using System.Text;
using Meddle.Utils.Files;

namespace Meddle.Utils.Models;

public class Material
{
    public MtrlFile File { get; }
    public IReadOnlyDictionary<int, string> Strings { get; }
    public readonly string ShaderPackageName;
    public readonly IReadOnlyDictionary<ushort, string> TexturePaths;
    public readonly IReadOnlyDictionary<ushort, string> UvColorSetStrings;
    public readonly IReadOnlyDictionary<ushort, string> ColorSetStrings;

    public Material(MtrlFile file)
    {
        File = file;
        Strings = GetStrings();
        ShaderPackageName = Strings[File.FileHeader.ShaderPackageNameOffset];
        TexturePaths = GetTexturePaths();
        UvColorSetStrings = GetUvColorSetStrings();
        ColorSetStrings = GetColorSetStrings();
    }
    
    private Dictionary<int, string> GetStrings()
    {
        var strings = new Dictionary<int, string>();
        var stringReader = new SpanBinaryReader(File.Strings);
        var offset = 0;
        while (offset < File.Strings.Length)
        {
            var str = stringReader.ReadByteString(offset);
            strings.Add(offset, Encoding.UTF8.GetString(str));
            offset += str.Length + 1;
        }

        return strings;
    }
    
    private Dictionary<ushort, string> GetTexturePaths()
    {
        var texturePaths = new Dictionary<ushort, string>();
        var textureOffsets = File.TextureOffsets.Select(x => x.Offset).Distinct();
        foreach (var offset in textureOffsets)
        {
            var path = Strings[offset];
            texturePaths.Add(offset, path);
        }
        return texturePaths;
    }
    
    private Dictionary<ushort, string> GetUvColorSetStrings()
    {
        var uvColorSetStrings = new Dictionary<ushort, string>();
        var uvColorSetOffsets = File.UvColorSets.Select(x => x.NameOffset).Distinct();
        foreach (var offset in uvColorSetOffsets)
        {
            var str = Strings[offset];
            uvColorSetStrings.Add(offset, str);
        }
        return uvColorSetStrings;
    }
    
    private Dictionary<ushort, string> GetColorSetStrings()
    {
        var colorSetStrings = new Dictionary<ushort, string>();
        var colorSetOffsets = File.ColorSets.Select(x => x.NameOffset).Distinct();
        foreach (var offset in colorSetOffsets)
        {
            var str = Strings[offset];
            colorSetStrings.Add(offset, str);
        }
        return colorSetStrings;
    }
}

using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using Meddle.Utils;
using Meddle.Utils.Files;
using Meddle.Utils.Files.Structs.Material;

namespace Meddle.Plugin.Utils;

public class ParseUtil
{
    public static unsafe Dictionary<int, ColorTable> ParseColorTableTextures(CharacterBase* characterBase)
    {
        var colorTableTextures = new Dictionary<int, ColorTable>();
        for (var i = 0; i < characterBase->ColorTableTexturesSpan.Length; i++)
        {
            var colorTableTex = characterBase->ColorTableTexturesSpan[i];
            //var colorTableTex = characterBase->ColorTableTexturesSpan[(modelIdx * CharacterBase.MaxMaterialCount) + j];
            if (colorTableTex == null) continue;

            var colorTableTexture = colorTableTex.Value;
            if (colorTableTexture != null)
            {
                var textures = ParseUtil.ParseColorTableTexture(colorTableTexture).AsSpan();
                var colorTableBytes = MemoryMarshal.AsBytes(textures);
                var colorTableBuf = new byte[colorTableBytes.Length];
                colorTableBytes.CopyTo(colorTableBuf);
                var reader = new SpanBinaryReader(colorTableBuf);
                var cts = ColorTable.Load(ref reader);
                colorTableTextures[i] = cts;
            }
        }

        return colorTableTextures;
    }
    
    // Only call from main thread or you will probably crash
    public static unsafe MaterialResourceHandle.ColorTableRow[] ParseColorTableTexture(Texture* colorTableTexture)
    {
        var (colorTableRes, stride) = DXHelper.ExportTextureResource(colorTableTexture);
        if ((TexFile.TextureFormat)colorTableTexture->TextureFormat != TexFile.TextureFormat.R16G16B16A16F)
            throw new ArgumentException(
                $"Color table is not R16G16B16A16F ({(TexFile.TextureFormat)colorTableTexture->TextureFormat})");
        if (colorTableTexture->Width != 8 || colorTableTexture->Height != 32)
            throw new ArgumentException(
                $"Color table is not 4x16 ({colorTableTexture->Width}x{colorTableTexture->Height})");

        var stridedData = ImageUtils.AdjustStride(stride, (int)colorTableTexture->Width * 8,
                                                  (int)colorTableTexture->Height, colorTableRes.Data);
        var reader = new SpanBinaryReader(stridedData);
        var tableData = reader.Read<MaterialResourceHandle.ColorTableRow>(32);
        return tableData.ToArray();
    }
}

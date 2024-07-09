using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using Meddle.Utils;
using Meddle.Utils.Files;

namespace Meddle.Plugin.Utils;

public class ParseUtil
{
    
    // Only call from UI thread or you will probably crash
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

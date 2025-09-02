using System.Collections.Concurrent;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Meddle.Plugin.Utils;
using Meddle.Utils;
using Meddle.Utils.Files;
using Meddle.Utils.Files.Structs.Material;
using Meddle.Utils.Helpers;
using Microsoft.Extensions.Logging;
using Texture = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Texture;

namespace Meddle.Plugin.Services;

public class ParseService : IDisposable, IService
{
    private readonly EventLogger<ParseService> logger;

    public readonly ConcurrentDictionary<string, ShpkFile> ShpkCache = new();
    public readonly ConcurrentDictionary<string, MdlFile> MdlCache = new();
    public readonly ConcurrentDictionary<string, MtrlFile> MtrlCache = new();
    public readonly ConcurrentDictionary<string, TexFile> TexCache = new();
    public void ClearCaches()
    {
        ShpkCache.Clear();
        MdlCache.Clear();
        MtrlCache.Clear();
        TexCache.Clear();
    }

    public ParseService(ILogger<ParseService> logger)
    {
        this.logger = new EventLogger<ParseService>(logger);
    }

    public void Dispose()
    {
        logger.LogDebug("Disposing ParseUtil");
    }
    public unsafe Dictionary<int, IColorTableSet> ParseColorTableTextures(CharacterBase* characterBase)
    {
        var colorTableTextures = new Dictionary<int, IColorTableSet>();
        for (var i = 0; i < characterBase->ColorTableTexturesSpan.Length; i++)
        {
            var colorTableTex = characterBase->ColorTableTexturesSpan[i];
            if (colorTableTex == null) continue;

            var colorTableTexture = colorTableTex.Value;
            if (colorTableTexture != null)
            {
                var colorTableSet = ParseColorTableTexture(colorTableTexture);
                colorTableTextures[i] = colorTableSet;
            }
        }

        return colorTableTextures;
    }

    // Only call from main thread or you will probably crash
    public unsafe IColorTableSet ParseColorTableTexture(Texture* colorTableTexture)
    {
        var (colorTableRes, stride) = DxHelper.ExportTextureResource(colorTableTexture);
        if ((TexFile.TextureFormat)colorTableTexture->TextureFormat != TexFile.TextureFormat.R16G16B16A16F)
        {
            throw new ArgumentException(
                $"Color table is not R16G16B16A16F ({(TexFile.TextureFormat)colorTableTexture->TextureFormat})");
        }

        if (colorTableTexture->ActualWidth == 4 && colorTableTexture->ActualHeight == 16)
        {
            // legacy table
            var stridedData = ImageUtils.AdjustStride(stride, (int)colorTableTexture->ActualWidth * 8,
                                                      (int)colorTableTexture->ActualHeight, colorTableRes.Data);
            var reader = new SpanBinaryReader(stridedData);
            return new LegacyColorTableSet
            {
                ColorTable = new LegacyColorTable(ref reader)
            };
        }

        if (colorTableTexture->ActualWidth == 8 && colorTableTexture->ActualHeight == 32)
        {
            // new table
            var stridedData = ImageUtils.AdjustStride(stride, (int)colorTableTexture->ActualWidth * 8,
                                                      (int)colorTableTexture->ActualHeight, colorTableRes.Data);
            var reader = new SpanBinaryReader(stridedData);
            return new ColorTableSet
            {
                ColorTable = new ColorTable(ref reader)
            };
        }

        throw new ArgumentException(
            $"Color table is not 4x16 or 8x32 ({colorTableTexture->ActualWidth}x{colorTableTexture->ActualHeight})");
    }
}

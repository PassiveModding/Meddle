using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Utils;
using Meddle.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;
using Meddle.Utils.Files.Structs.Material;
using Meddle.Utils.Helpers;
using Microsoft.Extensions.Logging;
using CustomizeParameter = Meddle.Utils.Export.CustomizeParameter;
using Material = FFXIVClientStructs.FFXIV.Client.Graphics.Render.Material;
using Texture = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Texture;

namespace Meddle.Plugin.Services;

public class ParseService : IDisposable, IService
{
    private readonly EventLogger<ParseService> logger;
    private readonly SqPack pack;
    private readonly PbdHooks pbdHooks;

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

    public ParseService(SqPack pack, PbdHooks pbdHooks, ILogger<ParseService> logger)
    {
        this.pack = pack;
        this.pbdHooks = pbdHooks;
        this.logger = new EventLogger<ParseService>(logger);
        this.logger.OnLogEvent += OnLog;
    }

    public void Dispose()
    {
        logger.LogDebug("Disposing ParseUtil");
        logger.OnLogEvent -= OnLog;
    }

    public event Action<LogLevel, string>? OnLogEvent;

    private void OnLog(LogLevel logLevel, string message)
    {
        OnLogEvent?.Invoke(logLevel, message);
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
        var (colorTableRes, stride) = DXHelper.ExportTextureResource(colorTableTexture);
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

using System.Diagnostics;
using System.Runtime.InteropServices;
using Dalamud.Memory;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using FFXIVClientStructs.Interop;
using Meddle.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;
using Meddle.Utils.Files.Structs.Material;
using Meddle.Utils.Models;
using Meddle.Utils.Skeletons.Havok;
using Meddle.Utils.Skeletons.Havok.Models;
using Microsoft.Extensions.Logging;
using Attach = Meddle.Plugin.Skeleton.Attach;
using Texture = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Texture;

namespace Meddle.Plugin.Utils;

public class ParseUtil : IDisposable
{
    private static readonly ActivitySource ActivitySource = new("Meddle.Plugin.Utils.ParseUtil");
    private readonly DXHelper dxHelper;
    private readonly EventLogger<ParseUtil> logger;
    private readonly IFramework framework;
    private readonly SqPack pack;
    public event Action<LogLevel, string>? OnLogEvent; 

    private readonly Dictionary<string, ShpkFile> shpkCache = new();

    public ParseUtil(SqPack pack, IFramework framework, DXHelper dxHelper, ILogger<ParseUtil> logger)
    {
        this.pack = pack;
        this.framework = framework;
        this.dxHelper = dxHelper;
        this.logger = new EventLogger<ParseUtil>(logger);
        this.logger.OnLogEvent += OnLog;
    }
    
    private void OnLog(LogLevel logLevel, string message)
    {
        OnLogEvent?.Invoke(logLevel, message);
    }
    
    public unsafe Dictionary<int, ColorTable> ParseColorTableTextures(CharacterBase* characterBase)
    {
        using var activity = ActivitySource.StartActivity();
        var colorTableTextures = new Dictionary<int, ColorTable>();
        for (var i = 0; i < characterBase->ColorTableTexturesSpan.Length; i++)
        {
            var colorTableTex = characterBase->ColorTableTexturesSpan[i];
            if (colorTableTex == null) continue;

            var colorTableTexture = colorTableTex.Value;
            if (colorTableTexture != null)
            {
                var textures = ParseColorTableTexture(colorTableTexture);
                var cts = new ColorTable
                {
                    Rows = textures
                };
                colorTableTextures[i] = cts;
            }
        }

        return colorTableTextures;
    }

    // Only call from main thread or you will probably crash
    public unsafe ColorTableRow[] ParseColorTableTexture(Texture* colorTableTexture)
    {
        using var activity = ActivitySource.StartActivity();
        var (colorTableRes, stride) = dxHelper.ExportTextureResource(colorTableTexture);
        if ((TexFile.TextureFormat)colorTableTexture->TextureFormat != TexFile.TextureFormat.R16G16B16A16F)
        {
            throw new ArgumentException(
                $"Color table is not R16G16B16A16F ({(TexFile.TextureFormat)colorTableTexture->TextureFormat})");
        }

        if (colorTableTexture->Width == 4 && colorTableTexture->Height == 16)
        {
            // legacy table
            var stridedData = ImageUtils.AdjustStride(stride, (int)colorTableTexture->Width * 8,
            (int)colorTableTexture->Height, colorTableRes.Data);
            var reader = new SpanBinaryReader(stridedData);
            var tableData = reader.Read<LegacyColorTableRow>(16);
            return tableData.ToArray().Select(x => x.ToNew()).ToArray();
        }
        
        if (colorTableTexture->Width == 8 && colorTableTexture->Height == 32)
        {
            // new table
            var stridedData = ImageUtils.AdjustStride(stride, (int)colorTableTexture->Width * 8,
                                                      (int)colorTableTexture->Height, colorTableRes.Data);
            var reader = new SpanBinaryReader(stridedData);
            var tableData = reader.Read<ColorTableRow>(32);
            return tableData.ToArray();
        }
        
        throw new ArgumentException(
            $"Color table is not 4x16 or 8x32 ({colorTableTexture->Width}x{colorTableTexture->Height})");
    }

    public unsafe ExportUtil.CharacterGroup HandleCharacterGroup(
        CharacterBase* characterBase,
        Dictionary<int, ColorTable> colorTableTextures,
        Dictionary<Pointer<CharacterBase>, Dictionary<int, ColorTable>> attachDict,
        CustomizeParameter customizeParams,
        CustomizeData customizeData,
        GenderRace genderRace)
    {
        using var activity = ActivitySource.StartActivity();
        var skeleton = new Skeleton.Skeleton(characterBase->Skeleton);
        var mdlGroups = new List<Model.MdlGroup>();
        for (var i = 0; i < characterBase->SlotCount; i++)
        {
            var mdlGroup = HandleModelPtr(characterBase, i, colorTableTextures);
            if (mdlGroup != null)
            {
                mdlGroups.Add(mdlGroup);
            }
        }

        var attachGroups = new List<ExportUtil.AttachedModelGroup>();
        foreach (var (attachBase, attachColorTableTextures) in attachDict)
        {
            var attachGroup = HandleAttachGroup(attachBase, attachColorTableTextures);
            attachGroups.Add(attachGroup);
        }

        return new ExportUtil.CharacterGroup(
            customizeParams,
            customizeData,
            genderRace,
            mdlGroups.ToArray(),
            skeleton,
            attachGroups.ToArray());
    }

    public unsafe Model.MdlGroup? HandleModelPtr(
        CharacterBase* characterBase, int modelIdx, Dictionary<int, ColorTable> colorTables)
    {
        using var activity = ActivitySource.StartActivity();
        var modelPtr = characterBase->ModelsSpan[modelIdx];
        if (modelPtr == null || modelPtr.Value == null)
        {
            //logger.LogWarning("Model Ptr {ModelIndex} is null", modelIdx);
            return null;
        }
        var model = modelPtr.Value;

        var mdlFileName = model->ModelResourceHandle->ResourceHandle.FileName.ToString();
        activity?.SetTag("mdl", mdlFileName);
        var mdlFileResource = pack.GetFileOrReadFromDisk(mdlFileName);
        if (mdlFileResource == null)
        {
            logger.LogWarning("Model file {MdlFileName} not found", mdlFileName);
            return null;
        }

        var shapesMask = model->EnabledShapeKeyIndexMask;
        var shapes = new List<(string, short)>();
        foreach (var shape in model->ModelResourceHandle->Shapes)
        {
            shapes.Add((MemoryHelper.ReadStringNullTerminated((nint)shape.Item1.Value), shape.Item2));
        }

        var attributeMask = model->EnabledAttributeIndexMask;
        var attributes = new List<(string, short)>();
        foreach (var attribute in model->ModelResourceHandle->Attributes)
        {
            attributes.Add((MemoryHelper.ReadStringNullTerminated((nint)attribute.Item1.Value), attribute.Item2));
        }

        var shapeAttributeGroup =
            new Model.ShapeAttributeGroup(shapesMask, attributeMask, shapes.ToArray(), attributes.ToArray());

        var mdlFile = new MdlFile(mdlFileResource);
        var mtrlGroups = new List<Material.MtrlGroup>();
        for (var j = 0; j < model->MaterialsSpan.Length; j++)
        {
            var materialPtr = model->MaterialsSpan[j];
            var material = materialPtr.Value;
            if (material == null)
            {
                logger.LogWarning("Material Ptr {MaterialIndex} is null for {MdlFileName}", j, mdlFileName);
                continue;
            }

            var mtrlGroup = HandleMtrl(material, modelIdx, j, colorTables);
            if (mtrlGroup != null)
            {
                mtrlGroups.Add(mtrlGroup);
            }
        }

        return new Model.MdlGroup(mdlFileName, mdlFile, mtrlGroups.ToArray(), shapeAttributeGroup);
    }

    private ShpkFile HandleShpk(string shader)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("shader", shader);
        if (shpkCache.TryGetValue(shader, out var shpk))
        {
            return shpk;
        }

        var shpkFileResource = pack.GetFileOrReadFromDisk($"shader/sm5/shpk/{shader}");
        if (shpkFileResource == null)
        {
            throw new Exception($"Failed to load shader package {shader}");
        }

        var shpkFile = new ShpkFile(shpkFileResource);
        shpkCache[shader] = shpkFile;
        return shpkFile;
    }

    private unsafe Material.MtrlGroup? HandleMtrl(
        FFXIVClientStructs.FFXIV.Client.Graphics.Render.Material* material, int modelIdx, int j,
        Dictionary<int, ColorTable> colorTables)
    {
        using var activity = ActivitySource.StartActivity();
        var mtrlFileName = material->MaterialResourceHandle->ResourceHandle.FileName.ToString();
        var shader = material->MaterialResourceHandle->ShpkNameString;
        activity?.SetTag("mtrl", mtrlFileName);
        activity?.SetTag("shader", shader);

        var mtrlFileResource = pack.GetFileOrReadFromDisk(mtrlFileName);
        if (mtrlFileResource == null)
        {
            logger.LogWarning("Material file {MtrlFileName} not found", mtrlFileName);
            return null;
        }

        var mtrlFile = new MtrlFile(mtrlFileResource);
        var colorTable = material->MaterialResourceHandle->ColorTableSpan;
        if (colorTable.Length == 32)
        {
            var colorTableBytes = MemoryMarshal.AsBytes(colorTable);
            var colorTableBuf = new byte[colorTableBytes.Length];
            colorTableBytes.CopyTo(colorTableBuf);
            var reader = new SpanBinaryReader(colorTableBuf);
            var cts = ColorTable.Load(ref reader);
            mtrlFile.ColorTable = cts;
        }

        if (colorTables.TryGetValue((modelIdx * CharacterBase.MaterialsPerSlot) + j, out var gpuColorTable))
        {
            mtrlFile.ColorTable = gpuColorTable;
        }

        var shpkFile = HandleShpk(shader);
        var texGroups = new List<Meddle.Utils.Export.Texture.TexGroup>();

        for (int i = 0; i < material->MaterialResourceHandle->TextureCount; i++)
        {
            var textureEntry = material->MaterialResourceHandle->TexturesSpan[i];
            if (textureEntry.TextureResourceHandle == null)
            {
                logger.LogWarning("Texture handle is null on {MtrlFileName}", mtrlFileName);
                continue;
            }
            
            var texturePath = material->MaterialResourceHandle->TexturePathString(i);
            var data = dxHelper.ExportTextureResource(textureEntry.TextureResourceHandle->Texture);
            var texResourceGroup = new Meddle.Utils.Export.Texture.TexGroup(texturePath, data.Resource);
            texGroups.Add(texResourceGroup);
        }

        /*var textureNames = mtrlFile.GetTexturePaths().Select(x => x.Value).ToArray();
        foreach (var textureName in textureNames)
        {
            var texGroup = HandleTexture(textureName);
            if (texGroup != null)
            {
                texGroups.Add(texGroup);
            }
        }*/

        return new Material.MtrlGroup(mtrlFileName, mtrlFile, shader, shpkFile, texGroups.ToArray());
    }

    /*private Meddle.Utils.Export.Texture.TexGroup? HandleTexture(string textureName)
    {
        using var activity = ActivitySource.StartActivity();
        var texFileResource = pack.GetFileOrReadFromDisk(textureName);
        if (texFileResource == null)
        {
            logger.LogWarning("Texture file {TextureName} not found", textureName);
            return null;
        }

        var texFile = new TexFile(texFileResource);
        return new Meddle.Utils.Export.Texture.TexGroup(textureName, texFile);
    }*/

    public unsafe ExportUtil.AttachedModelGroup HandleAttachGroup(
        CharacterBase* attachBase, Dictionary<int, ColorTable> colorTables)
    {
        using var activity = ActivitySource.StartActivity();
        var attach = new Attach(attachBase->Attach);
        var models = new List<Model.MdlGroup>();
        var skeleton = new Skeleton.Skeleton(attachBase->Skeleton);
        for (var i = 0; i < attachBase->ModelsSpan.Length; i++)
        {
            var mdlGroup = HandleModelPtr(attachBase, i, colorTables);
            if (mdlGroup != null)
            {
                models.Add(mdlGroup);
            }
        }

        var attachGroup = new ExportUtil.AttachedModelGroup(attach, models.ToArray(), skeleton);
        return attachGroup;
    }

    private unsafe List<HavokSkeleton> ParseSkeletons(Human* human)
    {
        var skeletonResourceHandles =
            new Span<Pointer<SkeletonResourceHandle>>(human->Skeleton->SkeletonResourceHandles,
                                                      human->Skeleton->PartialSkeletonCount);
        var skeletons = new List<HavokSkeleton>();
        foreach (var skeletonPtr in skeletonResourceHandles)
        {
            var skeletonResourceHandle = skeletonPtr.Value;
            if (skeletonResourceHandle == null)
            {
                continue;
            }

            var fileName = skeletonResourceHandle->ResourceHandle.FileName.ToString();
            var skeletonFileResource = pack.GetFileOrReadFromDisk(fileName);
            if (skeletonFileResource == null)
            {
                continue;
            }

            var sklbFile = new SklbFile(skeletonFileResource);
            var tempFile = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(tempFile, sklbFile.Skeleton.ToArray());
                var hkXml = framework.RunOnTick(() =>
                {
                    var xml = HkUtil.HkxToXml(tempFile);
                    return xml;
                }).GetAwaiter().GetResult();
                var havokXml = HavokUtils.ParseHavokXml(hkXml);

                skeletons.Add(havokXml);
            } finally
            {
                File.Delete(tempFile);
            }
        }

        return skeletons;
    }

    public void Dispose()
    {
        logger.LogInformation("Disposing ParseUtil");
        logger.OnLogEvent -= OnLog;
    }
}

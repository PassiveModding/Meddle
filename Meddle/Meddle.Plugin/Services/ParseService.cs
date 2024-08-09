using System.Diagnostics;
using System.Runtime.InteropServices;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.Interop;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Skeletons;
using Meddle.Plugin.Utils;
using Meddle.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;
using Meddle.Utils.Files.Structs.Material;
using Meddle.Utils.Models;
using Microsoft.Extensions.Logging;
using Material = FFXIVClientStructs.FFXIV.Client.Graphics.Render.Material;
using Texture = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Texture;

namespace Meddle.Plugin.Services;

public class ParseService : IDisposable
{
    private static readonly ActivitySource ActivitySource = new("Meddle.Plugin.Utils.ParseUtil");
    private readonly DXHelper dxHelper;
    private readonly IFramework framework;
    private readonly EventLogger<ParseService> logger;
    private readonly SqPack pack;
    private readonly PbdHooks pbdHooks;

    private readonly Dictionary<string, ShpkFile> shpkCache = new();

    public ParseService(
        SqPack pack, IFramework framework, DXHelper dxHelper, PbdHooks pbdHooks, ILogger<ParseService> logger)
    {
        this.pack = pack;
        this.framework = framework;
        this.dxHelper = dxHelper;
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

    public unsafe AttachedModelGroup? ParseDrawObjectAsAttach(DrawObject* drawObject)
    {
        if (drawObject == null) return null;
        if (drawObject->GetObjectType() != ObjectType.CharacterBase) return null;
        var drawCharacterBase = (CharacterBase*)drawObject;
        var attachGroup = ParseCharacterBase(drawCharacterBase);
        var attach = StructExtensions.GetParsedAttach(drawCharacterBase);
        return new AttachedModelGroup(attach, attachGroup.MdlGroups, attachGroup.Skeleton);

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

    /// <summary>
    /// Parse a character base into a character group excluding attach data and customize data
    /// </summary>
    /// <param name="characterBase"></param>
    /// <returns></returns>
    public unsafe CharacterGroup ParseCharacterBase(CharacterBase* characterBase)
    {
        var colorTableTextures = ParseColorTableTextures(characterBase);
        var models = new List<MdlFileGroup>();
        foreach (var modelPtr in characterBase->ModelsSpan)
        {
            if (modelPtr == null) continue;
            var model = modelPtr.Value;
            if (model == null) continue;
            var modelData = HandleModelPtr(characterBase, (int)model->SlotIndex, colorTableTextures);
            if (modelData == null) continue;
            models.Add(modelData);
        }
        var skeleton = StructExtensions.GetParsedSkeleton(characterBase);
        return new CharacterGroup(new CustomizeParameter(), new CustomizeData(), GenderRace.Unknown, models.ToArray(), skeleton, []);
    }

    /*public unsafe CharacterGroup HandleCharacterGroup(
        CharacterBase* characterBase,
        Dictionary<int, ColorTable> colorTableTextures,
        Dictionary<Pointer<CharacterBase>, Dictionary<int, ColorTable>> attachDict,
        CustomizeParameter customizeParams,
        CustomizeData customizeData,
        GenderRace genderRace)
    {
        using var activity = ActivitySource.StartActivity();
        var skeleton = new ParsedSkeleton(characterBase->Skeleton);
        var mdlGroups = new List<MdlFileGroup>();
        for (var i = 0; i < characterBase->SlotCount; i++)
        {
            var mdlGroup = HandleModelPtr(characterBase, i, colorTableTextures);
            if (mdlGroup != null)
            {
                mdlGroups.Add(mdlGroup);
            }
        }

        var attachGroups = new List<AttachedModelGroup>();
        foreach (var (attachBase, attachColorTableTextures) in attachDict)
        {
            var attachGroup = HandleAttachGroup(attachBase, attachColorTableTextures);
            attachGroups.Add(attachGroup);
        }

        return new CharacterGroup(
            customizeParams,
            customizeData,
            genderRace,
            mdlGroups.ToArray(),
            skeleton,
            attachGroups.ToArray());
    }*/

    public unsafe MdlFileGroup? HandleModelPtr(CharacterBase* characterBase, int slotIdx, Dictionary<int, ColorTable> colorTables)
    {
        using var activity = ActivitySource.StartActivity();
        var modelPtr = characterBase->ModelsSpan[slotIdx];
        if (modelPtr == null || modelPtr.Value == null)
        {
            //logger.LogWarning("Model Ptr {ModelIndex} is null", modelIdx);
            return null;
        }

        var model = modelPtr.Value;

        var mdlFileName = model->ModelResourceHandle->ResourceHandle.FileName.ToString();
        var mdlFileActorName = characterBase->ResolveMdlPath((uint)slotIdx);
        activity?.SetTag("mdl", mdlFileName);
        var mdlFileResource = pack.GetFileOrReadFromDisk(mdlFileName);
        if (mdlFileResource == null)
        {
            logger.LogWarning("Model file {MdlFileName} not found", mdlFileName);
            return null;
        }

        var shapeAttributeGroup = StructExtensions.ParseModelShapeAttributes(model);
        var mdlFile = new MdlFile(mdlFileResource);
        var mtrlFileNames = mdlFile.GetMaterialNames().Select(x => x.Value).ToArray();
        var mtrlGroups = new List<MtrlFileGroup>();
        for (var j = 0; j < model->MaterialsSpan.Length; j++)
        {
            var materialPtr = model->MaterialsSpan[j];
            var material = materialPtr.Value;
            if (material == null)
            {
                logger.LogWarning("Material Ptr {MaterialIndex} is null for {MdlFileName}", j, mdlFileName);
                continue;
            }

            var mdlMtrlFileName = mtrlFileNames[j];
            var mtrlGroup = ParseMtrl(mdlMtrlFileName, material, slotIdx, j, colorTables);
            if (mtrlGroup != null)
            {
                mtrlGroups.Add(mtrlGroup);
            }
        }

        var deformerData = pbdHooks.TryGetDeformer((nint)characterBase, (uint)slotIdx);
        DeformerGroup? deformerGroup = null;
        if (deformerData != null)
        {
            deformerGroup = new DeformerGroup(deformerData.Value.PbdPath, deformerData.Value.RaceSexId,
                                              deformerData.Value.DeformerId);
        }

        return new MdlFileGroup(mdlFileActorName, mdlFileName, deformerGroup, mdlFile, mtrlGroups.ToArray(),
                                shapeAttributeGroup);
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

    private unsafe MtrlFileGroup? ParseMtrl(
        string mdlPath,
        Material* material, int modelIdx, int j,
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
        var texGroups = new List<TexResourceGroup>();

        for (var i = 0; i < material->MaterialResourceHandle->TextureCount; i++)
        {
            var textureEntry = material->MaterialResourceHandle->TexturesSpan[i];
            if (textureEntry.TextureResourceHandle == null)
            {
                logger.LogWarning("Texture handle is null on {MtrlFileName}", mtrlFileName);
                continue;
            }

            var texturePath = material->MaterialResourceHandle->TexturePathString(i);
            var resourcePath = textureEntry.TextureResourceHandle->ResourceHandle.FileName.ToString();
            var data = dxHelper.ExportTextureResource(textureEntry.TextureResourceHandle->Texture);
            var texResourceGroup = new TexResourceGroup(texturePath, resourcePath, data.Resource);
            texGroups.Add(texResourceGroup);
        }

        return new MtrlFileGroup(mdlPath, mtrlFileName, mtrlFile, shader, shpkFile, texGroups.ToArray());
    }

    /*public unsafe AttachedModelGroup HandleAttachGroup(
        Pointer<CharacterBase> attachBase, Dictionary<int, ColorTable> colorTables)
    {
        using var activity = ActivitySource.StartActivity();
        var attach = new ParsedAttach(attachBase.GetAttach());
        var models = new List<MdlFileGroup>();
        var skeleton = new ParsedSkeleton(attachBase.Value->Skeleton);
        for (var i = 0; i < attachBase.Value->ModelsSpan.Length; i++)
        {
            var mdlGroup = HandleModelPtr(attachBase, i, colorTables);
            if (mdlGroup != null)
            {
                models.Add(mdlGroup);
            }
        }

        var attachGroup = new AttachedModelGroup(attach, models.ToArray(), skeleton);
        return attachGroup;
    }*/
}

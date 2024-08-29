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
using Meddle.Utils.Models;
using Microsoft.Extensions.Logging;
using CustomizeParameter = Meddle.Utils.Export.CustomizeParameter;
using Material = FFXIVClientStructs.FFXIV.Client.Graphics.Render.Material;
using Texture = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Texture;

namespace Meddle.Plugin.Services;

public class ParseService : IDisposable, IService
{
    private static readonly ActivitySource ActivitySource = new("Meddle.Plugin.Utils.ParseUtil");
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
        var (colorTableRes, stride) = DXHelper.ExportTextureResource(colorTableTexture);
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

    public Task<MdlFileGroup> ParseFromModelInfo(ParsedModelInfo info)
    {
        var mdlFileResource = pack.GetFileOrReadFromDisk(info.Path);
        if (mdlFileResource == null)
        {
            throw new Exception($"Failed to load model file {info.Path}");
        }

        var mdlFile = new MdlFile(mdlFileResource);
        var mtrlGroups = new List<IMtrlFileGroup>();

        foreach (var materialInfo in info.Materials)
        {
            var mtrlFileResource = pack.GetFileOrReadFromDisk(materialInfo.Path);
            if (mtrlFileResource == null)
            {
                logger.LogWarning("Material file {MtrlFileName} not found", materialInfo.Path);
                mtrlGroups.Add(new MtrlFileStubGroup(materialInfo.Path));
                continue;
            }
            
            var mtrlFile = new MtrlFile(mtrlFileResource);
            if (materialInfo.ColorTable != null)
            {
                mtrlFile.ColorTable = materialInfo.ColorTable.Value;
            }
            
            var shpkName = mtrlFile.GetShaderPackageName();
            var shpkFile = HandleShpk(shpkName);

            var texGroups = new List<TexResourceGroup>();
            foreach (var textureInfo in materialInfo.Textures)
            {
                var textureResource = pack.GetFileOrReadFromDisk(textureInfo.Path);
                if (textureResource == null)
                {
                    logger.LogWarning("Texture file {TexturePath} not found", textureInfo.Path);
                    continue;
                }

                var texGroup = new TexResourceGroup(textureInfo.PathFromMaterial, textureInfo.Path, textureInfo.Resource);
                texGroups.Add(texGroup);
            }

            mtrlGroups.Add(new MtrlFileGroup(materialInfo.PathFromModel, materialInfo.Path, mtrlFile, shpkName, shpkFile,
                                             texGroups.ToArray()));
        }
        
        DeformerGroup? deformerGroup = null;
        if (info.Deformer != null)
        {
            deformerGroup = new DeformerGroup(info.Deformer.Value.PbdPath, 
                                              info.Deformer.Value.RaceSexId,
                                              info.Deformer.Value.DeformerId);
        }

        return Task.FromResult(new MdlFileGroup(info.PathFromCharacter, info.Path, deformerGroup, mdlFile, mtrlGroups.ToArray(), info.ShapeAttributeGroup));
    }
    
    public Task<MdlFileGroup> ParseFromPath(string mdlPath)
    {
        var mdlFileResource = pack.GetFileOrReadFromDisk(mdlPath);
        if (mdlFileResource == null)
        {
            throw new Exception($"Failed to load model file {mdlPath}");
        }
        
        var mdlFile = new MdlFile(mdlFileResource);
        var mtrlFileNames = mdlFile.GetMaterialNames().Select(x => x.Value).ToArray();
        var mtrlGroups = new List<IMtrlFileGroup>();

        foreach (var mtrlFileName in mtrlFileNames)
        {
            if (!MtrlCache.TryGetValue(mtrlFileName, out var mtrlFile))
            {
                var mtrlFileResource = pack.GetFileOrReadFromDisk(mtrlFileName);
                if (mtrlFileResource == null)
                {
                    logger.LogWarning("Material file {MtrlFileName} not found", mtrlFileName);
                    mtrlGroups.Add(new MtrlFileStubGroup(mtrlFileName));
                    continue;
                }
                
                mtrlFile = new MtrlFile(mtrlFileResource);
                MtrlCache[mtrlFileName] = mtrlFile;
            }

            var shpkName = mtrlFile.GetShaderPackageName();
            var shpkFile = HandleShpk(shpkName);

            var texturePaths = mtrlFile.GetTexturePaths().Select(x => x.Value).ToArray();
            var texGroups = new List<TexResourceGroup>();
            foreach (var texturePath in texturePaths)
            {
                if (!TexCache.TryGetValue(texturePath, out var texFile))
                {
                    var textureResource = pack.GetFileOrReadFromDisk(texturePath);
                    if (textureResource == null)
                    {
                        logger.LogWarning("Texture file {TexturePath} not found", texturePath);
                        continue;
                    }

                    texFile = new TexFile(textureResource);
                    TexCache[texturePath] = texFile;
                }

                var texRes = Meddle.Utils.Export.Texture.GetResource(texFile);
                var texGroup = new TexResourceGroup(texturePath, texturePath, texRes);
                texGroups.Add(texGroup);
            }

            mtrlGroups.Add(new MtrlFileGroup(mtrlFileName, mtrlFileName, mtrlFile, shpkName, shpkFile,
                                             texGroups.ToArray()));
        }
        
        return Task.FromResult(new MdlFileGroup(mdlPath, mdlPath, null, mdlFile, mtrlGroups.ToArray(), null));
    }

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

        var mdlFileName = model->ModelResourceHandle->ResourceHandle.FileName.ParseString();
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
        var mtrlGroups = new List<IMtrlFileGroup>();
        for (var j = 0; j < model->MaterialsSpan.Length; j++)
        {
            var materialPtr = model->MaterialsSpan[j];
            var material = materialPtr.Value;
            var mdlMtrlFileName = mtrlFileNames[j];
            if (material == null)
            {
                logger.LogWarning("Material Ptr {MaterialIndex} is null for {MdlFileName}", j, mdlFileName);
                mtrlGroups.Add(new MtrlFileStubGroup(mdlMtrlFileName));
                continue;
            }

            var mtrlGroup = ParseMtrl(mdlMtrlFileName, material, slotIdx, j, colorTables);
            if (mtrlGroup == null)
            {
                logger.LogWarning("Failed to parse material {MdlMtrlFileName}", mdlMtrlFileName);
                mtrlGroups.Add(new MtrlFileStubGroup(mdlMtrlFileName));
            }
            else
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
        if (ShpkCache.TryGetValue(shader, out var shpk))
        {
            return shpk;
        }

        var shpkFileResource = pack.GetFileOrReadFromDisk($"shader/sm5/shpk/{shader}");
        if (shpkFileResource == null)
        {
            throw new Exception($"Failed to load shader package {shader}");
        }

        var shpkFile = new ShpkFile(shpkFileResource);
        ShpkCache[shader] = shpkFile;
        return shpkFile;
    }

    private unsafe MtrlFileGroup? ParseMtrl(
        string mdlMtrlPath,
        Material* material, int modelIdx, int j,
        Dictionary<int, ColorTable> colorTables)
    {
        using var activity = ActivitySource.StartActivity();
        var mtrlFileName = material->MaterialResourceHandle->ResourceHandle.FileName.ParseString();
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
            var resourcePath = textureEntry.TextureResourceHandle->ResourceHandle.FileName.ParseString();
            var data = DXHelper.ExportTextureResource(textureEntry.TextureResourceHandle->Texture);
            var texResourceGroup = new TexResourceGroup(texturePath, resourcePath, data.Resource);
            texGroups.Add(texResourceGroup);
        }

        return new MtrlFileGroup(mdlMtrlPath, mtrlFileName, mtrlFile, shader, shpkFile, texGroups.ToArray());
    }

    /*public CharacterGroup HandleModelPath(string path)
    {
        var data = pack.GetFileOrReadFromDisk(path);
        if (data == null)
        {
            throw new Exception($"Failed to load model file {path}");
        }

        var mdlFile = new MdlFile(data);
        var mtrlFileNames = mdlFile.GetMaterialNames().Select(x => x.Value).ToArray();
        var mtrlGroups = new List<IMtrlFileGroup>();

        for (var i = 0; i < mtrlFileNames.Length; i++)
        {
            var mtrlFileName = mtrlFileNames[i];
            var mtrlFileResource = pack.GetFileOrReadFromDisk(mtrlFileName);
            if (mtrlFileResource == null)
            {
                logger.LogWarning("Material file {MtrlFileName} not found", mtrlFileName);
                mtrlGroups.Add(new MtrlFileStubGroup(mtrlFileName));
                continue;
            }

            var mtrlFile = new MtrlFile(mtrlFileResource);
            var texturePaths = mtrlFile.GetTexturePaths().Select(x => x.Value).ToArray();
            var texGroups = new List<TexResourceGroup>();
            foreach (var texturePath in texturePaths)
            {
                var textureResource = pack.GetFileOrReadFromDisk(texturePath);
                if (textureResource == null)
                {
                    logger.LogWarning("Texture file {TexturePath} not found", texturePath);
                    continue;
                }

                var texFile = new TexFile(textureResource);
            }

        }

        return null;
    }


    public unsafe CharacterGroup HandleCharacterGroup(
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
    }

    public unsafe AttachedModelGroup HandleAttachGroup(
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

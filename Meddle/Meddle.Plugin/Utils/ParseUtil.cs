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
using Attach = Meddle.Plugin.Skeleton.Attach;
using Texture = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Texture;

namespace Meddle.Plugin.Utils;

public class ParseUtil
{
    private readonly SqPack pack;
    private readonly IFramework framework;

    public ParseUtil(SqPack pack, IFramework framework)
    {
        this.pack = pack;
        this.framework = framework;
    }
    
    public unsafe Dictionary<int, ColorTable> ParseColorTableTextures(CharacterBase* characterBase)
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
                var textures = ParseColorTableTexture(colorTableTexture).AsSpan();
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
    public unsafe MaterialResourceHandle.ColorTableRow[] ParseColorTableTexture(Texture* colorTableTexture)
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
    
    public unsafe Model.MdlGroup? HandleModelPtr(CharacterBase* characterBase, int modelIdx, Dictionary<int, ColorTable> colorTables)
    {
        var modelPtr = characterBase->ModelsSpan[modelIdx];
        if (modelPtr == null) return null;
        var model = modelPtr.Value;
        if (model == null) return null;

        var mdlFileName = model->ModelResourceHandle->ResourceHandle.FileName.ToString();
        var mdlFileResource = pack.GetFile(mdlFileName);
        if (mdlFileResource == null)
        {
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

        var mdlFile = new MdlFile(mdlFileResource.Value.file.RawData);
        var mtrlGroups = new List<Material.MtrlGroup>();
        for (var j = 0; j < model->MaterialsSpan.Length; j++)
        {
            var materialPtr = model->MaterialsSpan[j];
            var material = materialPtr.Value;
            if (material == null)
            {
                continue;
            }

            var mtrlFileName = material->MaterialResourceHandle->ResourceHandle.FileName.ToString();
            var shader = material->MaterialResourceHandle->ShpkNameString;

            var mtrlFileResource = pack.GetFile(mtrlFileName);
            if (mtrlFileResource == null)
            {
                continue;
            }

            var mtrlFile = new MtrlFile(mtrlFileResource.Value.file.RawData);
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
            
            if (colorTables.TryGetValue((modelIdx * CharacterBase.MaxMaterialCount) + j, out var gpuColorTable))
            {
                mtrlFile.ColorTable = gpuColorTable;
            }

            var shpkFileResource = pack.GetFile($"shader/sm5/shpk/{shader}");
            if (shpkFileResource == null)
            {
                continue;
            }

            var shpkFile = new ShpkFile(shpkFileResource.Value.file.RawData);

            var texGroups = new List<Meddle.Utils.Export.Texture.TexGroup>();

            var textureNames = mtrlFile.GetTexturePaths().Select(x => x.Value).ToArray();
            foreach (var textureName in textureNames)
            {
                var texFileResource = pack.GetFile(textureName);
                if (texFileResource == null)
                {
                    continue;
                }

                var texFile = new TexFile(texFileResource.Value.file.RawData);
                texGroups.Add(new Meddle.Utils.Export.Texture.TexGroup(textureName, texFile));
            }

            mtrlGroups.Add(
                new Material.MtrlGroup(mtrlFileName, mtrlFile, shader, shpkFile, texGroups.ToArray()));
        }

        return new Model.MdlGroup(mdlFileName, mdlFile, mtrlGroups.ToArray(), shapeAttributeGroup);
    }

    public unsafe ExportUtil.AttachedModelGroup HandleAttachGroup(CharacterBase* attachBase, Dictionary<int, ColorTable> colorTables)
    {
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
    
    private unsafe List<HavokXml> ParseSkeletons(Human* human)
    {
        var skeletonResourceHandles =
            new Span<Pointer<SkeletonResourceHandle>>(human->Skeleton->SkeletonResourceHandles,
                                                      human->Skeleton->PartialSkeletonCount);
        var skeletons = new List<HavokXml>();
        foreach (var skeletonPtr in skeletonResourceHandles)
        {
            var skeletonResourceHandle = skeletonPtr.Value;
            if (skeletonResourceHandle == null)
            {
                continue;
            }

            var skeletonFileResource =
                pack.GetFile(skeletonResourceHandle->ResourceHandle.FileName.ToString());
            if (skeletonFileResource == null)
            {
                continue;
            }

            var sklbFile = new SklbFile(skeletonFileResource.Value.file.RawData);
            var tempFile = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(tempFile, sklbFile.Skeleton.ToArray());
                var hkXml = framework.RunOnTick(() =>
                {
                    var xml = HkUtil.HkxToXml(tempFile);
                    return xml;
                }).GetAwaiter().GetResult();
                var havokXml = new HavokXml(hkXml);

                skeletons.Add(havokXml);
            } finally
            {
                File.Delete(tempFile);
            }
        }

        return skeletons;
    }
}

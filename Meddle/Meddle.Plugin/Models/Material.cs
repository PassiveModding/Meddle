using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Dalamud.Memory;
using Lumina.Data.Files;
using Lumina.Data.Parsing;
using Meddle.Plugin.Utility;

namespace Meddle.Plugin.Models;

public unsafe class Material
{
    public string HandlePath { get; set; }
    public uint ShaderFlags { get; set; }
    public IReadOnlyList<ShaderKey> ShaderKeys { get; set; }
    public ShaderPackage ShaderPackage { get; set; }
    public IReadOnlyList<Texture> Textures { get; set; }
    
    public bool TryGetTexture(TextureUsage usage, out Texture texture)
    {
        var match = Textures.FirstOrDefault(x => x.Usage == usage);
        if (match == null)
        {
            texture = null!;
            return false;
        }

        texture = match;
        return true;
    }
    
    public Texture GetTexture(TextureUsage usage)
    {
        if (!TryGetTexture(usage, out var texture))
            throw new ArgumentException($"No texture for {usage}");
        return texture!;
    }
    
    [JsonIgnore]
    public Half[]? ColorTable { get; set; }

    [JsonPropertyName("ColorTable")]
    public ushort[]? JsonColorTable => ColorTable?.Select(BitConverter.HalfToUInt16Bits).ToArray();
    
    public Material(FFXIVClientStructs.FFXIV.Client.Graphics.Render.Material* material, FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Texture* colorTable)
    {
        HandlePath = material->MaterialResourceHandle->ResourceHandle.FileName.ToString();
        ShaderFlags = material->ShaderFlags;

        var shaderKeyCategories =
            material->MaterialResourceHandle->ShaderPackageResourceHandle->ShaderPackage->MaterialKeysSpan;
        var shaderKeyValues = material->ShaderKeyValuesSpan;
        var shaderKeys = new ShaderKey[shaderKeyValues.Length];
        for (var i = 0; i < shaderKeyValues.Length; ++i)
        {
            shaderKeys[i] = new ShaderKey
            {
                Category = shaderKeyCategories[i],
                Value = shaderKeyValues[i]
            };
        }
        
        ShaderKeys = shaderKeys;
        
        ShaderPackage = new ShaderPackage(material->MaterialResourceHandle->ShaderPackageResourceHandle, 
                        MemoryHelper.ReadStringNullTerminated((nint)material->MaterialResourceHandle->ShpkName));
        var textures = new List<Texture>();
        for (var i = 0; i < material->MaterialResourceHandle->TextureCount; ++i)
        {
            var handleTexture = &material->MaterialResourceHandle->Textures[i];
            FFXIVClientStructs.FFXIV.Client.Graphics.Render.Material.TextureEntry* matEntry = null;
            if (handleTexture->Index1 != 0x1F)
                matEntry = &material->Textures[handleTexture->Index1];
            else if (handleTexture->Index2 != 0x1F)
                matEntry = &material->Textures[handleTexture->Index2];
            else if (handleTexture->Index3 != 0x1F)
                matEntry = &material->Textures[handleTexture->Index3];
            else
            {
                foreach (var tex in material->TexturesSpan)
                {
                    if (tex.Texture == handleTexture->TextureResourceHandle)
                    {
                        matEntry = &tex;
                        break;
                    }
                }
            }
            textures.Add(new Texture(matEntry, material->MaterialResourceHandle->Strings, handleTexture, ShaderPackage));
        }
        
        Textures = textures;

        if (colorTable != null)
        {
            var data = DXHelper.ExportTextureResource(colorTable);

            if ((TexFile.TextureFormat)colorTable->TextureFormat != TexFile.TextureFormat.R16G16B16A16F)
                throw new ArgumentException($"Color table is not R16G16B16A16F ({(TexFile.TextureFormat)colorTable->TextureFormat})");
            if (colorTable->Width != 4 || colorTable->Height != 16)
                throw new ArgumentException($"Color table is not 4x16 ({colorTable->Width}x{colorTable->Height})");

            var stridedData = TextureHelper.AdjustStride(data.Stride, (int)colorTable->Width * 8, (int)colorTable->Height, data.Data);

            ColorTable = MemoryMarshal.Cast<byte, Half>(stridedData.AsSpan()).ToArray();
            if (ColorTable.Length != 4 * 16 * 4)
                throw new ArgumentException($"Color table is not 4x16x4 ({ColorTable.Length})");
        }
        //else
        //    Log.Warning($"No color table for {HandlePath}");
    }
    
    public struct ShaderKey
    {
        public uint Category;
        public uint Value;
    }
}

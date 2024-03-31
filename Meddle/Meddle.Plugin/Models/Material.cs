using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Dalamud.Memory;
using FFXIVClientStructs.Interop;
using Lumina.Data.Files;
using Lumina.Data.Parsing;
using Meddle.Plugin.Utility;
using CSMaterial = FFXIVClientStructs.FFXIV.Client.Graphics.Render.Material;
using CSTexture = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Texture;

namespace Meddle.Plugin.Models;

public unsafe class Material
{
    public string HandlePath { get; }
    public uint ShaderFlags { get; }
    public IReadOnlyList<ShaderKey> ShaderKeys { get; }
    public ShaderPackage ShaderPackage { get; }
    public IReadOnlyList<Texture> Textures { get; }
    
    public MaterialParameter? Parameters { get; }
    
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
    public ColorTable ColorTable { get; }
    
    public Material(Pointer<CSMaterial> material, Pointer<CSTexture> colorTable) : this(material.Value, colorTable.Value)
    {

    }
    
    public Material(CSMaterial* material, CSTexture* colorTable)
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
            CSMaterial.TextureEntry* matEntry = null;
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

            var table = MemoryMarshal.Cast<byte, Half>(stridedData.AsSpan()).ToArray();
            if (table.Length != 4 * 16 * 4)
                throw new ArgumentException($"Color table is not 4x16x4 ({table.Length})");
            
            ColorTable = new ColorTable(table);
        }
        else
        {
            Service.Log.Warning($"No color table for {HandlePath} using default");
            ColorTable = new ColorTable();
        }
        
        // material parameters
        var matParams = material->MaterialParameterCBuffer;
        // hair, iris, skin, character, characterglass
        if ((ShaderPackage.Name == "hair.shpk" ||
             ShaderPackage.Name == "iris.shpk" ||
             ShaderPackage.Name == "skin.shpk" ||
             ShaderPackage.Name == "character.shpk" ||
             ShaderPackage.Name == "characterglass.shpk") && matParams != null)
        {
            var m = matParams->LoadBuffer<Vector4>(0, 6);
            if (m != null && m.Length == 6)
            {
                Parameters = new MaterialParameter
                {
                    DiffuseColor = Normalize(new Vector3(m[0].X, m[0].Y, m[0].Z)),
                    AlphaThreshold = m[0].W == 0 ? 0.5f : m[0].W,
                    FresnelValue0 = Normalize(new Vector3(m[1].X, m[1].Y, m[1].Z)),
                    SpecularMask = m[1].W,
                    LipFresnelValue0 = Normalize(new Vector3(m[2].X, m[2].Y, m[2].Z)),
                    Shininess = m[2].W / 255f,
                    EmissiveColor = Normalize(new Vector3(m[3].X, m[3].Y, m[3].Z)),
                    LipShininess = m[3].W / 255f,
                    TileScale = new Vector2(m[4].X, m[4].Y),
                    AmbientOcclusionMask = m[4].Z,
                    TileIndex = m[4].W,
                    ScatteringLevel = m[5].X,
                    UNK_15B70E35 = m[5].Y,
                    NormalScale = m[5].Z
                };
            }
        }
    }
    
    private static Vector3 Normalize(Vector3 v)
    {
        var len = v.Length();
        if (len == 0)
            return Vector3.Zero;
        return v / len;
    }
    
    public struct MaterialParameter
    {
        public Vector3 DiffuseColor;
        public float AlphaThreshold;
        public Vector3 FresnelValue0;
        public float SpecularMask;
        public Vector3 LipFresnelValue0;
        public float Shininess;
        public Vector3 EmissiveColor;
        public float LipShininess;
        public Vector2 TileScale;
        public float AmbientOcclusionMask;
        public float TileIndex;
        public float ScatteringLevel;
        public float UNK_15B70E35;
        public float NormalScale;
    }
    
    public struct ShaderKey
    {
        public uint Category;
        public uint Value;
    }
}

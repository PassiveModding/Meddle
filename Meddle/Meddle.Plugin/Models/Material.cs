using System.Numerics;
using System.Text.Json.Serialization;
using Dalamud.Memory;
using FFXIVClientStructs.Interop;
using Lumina.Data.Parsing;
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
    public MaterialParameters MaterialParameters { get; }
    
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

    public bool TryGetSkTexture(TextureUsage usage, out SKTexture skTexture)
    {
        if (!TryGetTexture(usage, out var texture))
        {
            skTexture = null!;
            return false;
        }

        skTexture = texture.Resource.ToTexture();
        return true;
    }
    
    public Texture GetTexture(TextureUsage usage)
    {
        if (!TryGetTexture(usage, out var texture))
            throw new ArgumentException($"No texture for {usage}");
        return texture!;
    }
    
    [JsonIgnore]
    public ColorTable? ColorTable { get; }
    
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
            ColorTable = new ColorTable(colorTable);
        }
        else
        {
            Service.Log.Warning($"[{ShaderPackage.Name}] No color table for {HandlePath}");
        }

        var matParams = material->MaterialParameterCBuffer;
        if (matParams != null)
        {
            if (MaterialParameters.ValidShaders.Contains(ShaderPackage.Name))
            {
                var m = matParams->LoadBuffer<Vector4>(0, 6);
                if (m != null && m.Length == 6)
                {
                    MaterialParameters = new MaterialParameters(m);
                }
                else if (m == null)
                {
                    Service.Log.Warning($"No material parameters for {HandlePath}");
                }
                else
                {
                    Service.Log.Warning($"Invalid material parameters for {HandlePath}, expected 6 but got {m.Length}");
                }
            }
            else
            {
                Service.Log.Warning($"[{ShaderPackage.Name}] Skipping material parameters for {HandlePath}");
            }
        }
        else
        {
            Service.Log.Warning($"No material parameters for {HandlePath}");
        }
    }
    
    // https://github.com/Shaderlayan/Ouroboros
    public struct ShaderKey
    {
        public enum ShaderKeyCategory : uint
        {
            // Note:
            // CharacterGlass always Color
            // Hair always Color
            // Iris always Multi
            // Skin always Multi
            VertexColorModeMulti = 4113354501,
            SkinType = 940355280,
            HairType = 612525193,
            TextureMode = 3054951514,
            DecalMode = 3531043187,
            SpecularMapMode = 3367837167
        }

        public enum TextureMode : uint
        {
            Multi = 1556481461,
            
            // Diffuse Color: #D50000 Specular Color: #FFFFFF Specular Strength: 1.00 Gloss Strength: 100 Emissive Color: #8C0000
            // Ignores vertex colors and normal map (except for opacity)
            // Accepts no color table and no textures
            Simple = 581216959, 
            Compatibility = 1611594207 // Diffuse / Specular
        }
        
        public enum DecalMode : uint
        {
            None = 1111668802,
            Alpha = 1480746461, // Face paint
            Color = 4083110193 // FC crest
        }

        // This is a setting of the Compatibility (Diffuse / Specular) Texture Mode, and has no effect outside of it.
        public enum SpecularMapMode : uint
        {
            Color = 428675533,
            Multi = 2687453224 
        }

        public enum VertexColorModeMultiValue : uint
        {
            Color = 3756477356,
            Multi = 2815623008
        }

        public enum SkinTypeValue : uint
        {
            Face = 4117181732,
            Body = 735790577,
            BodyWithHair = 1476344676 // used notably on hrothgar
        }

        public enum HairTypeValue : uint
        {
            Hair = 4156069230,
            Face = 1851494160
        }
        
        public uint Category;
        public uint Value;
        
        public ShaderKeyCategory CategoryEnum => (ShaderKeyCategory)Category;
        public TextureMode TextureModeEnum => (TextureMode)Value;
        public DecalMode DecalModeEnum => (DecalMode)Value;
        public SpecularMapMode SpecularMapModeEnum => (SpecularMapMode)Value;
        public VertexColorModeMultiValue VertexColorModeMultiValueEnum => (VertexColorModeMultiValue)Value;
        public SkinTypeValue SkinTypeValueEnum => (SkinTypeValue)Value;
        public HairTypeValue HairTypeValueEnum => (HairTypeValue)Value;
    }
}
